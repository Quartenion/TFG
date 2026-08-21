using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Gestiona los clicks del jugador y los evalua contra el beat de la musica.
///
/// Mecanica de la jeringuilla:
///  - Modo rojo (normal):
///      Perfect/Good -> HitRed(timing): la barra baja. Perfect da 1.5x energia, Good da 1x.
///      Miss         -> MissRed(): la barra sube (penalizacion).
///      Pasivo       -> la barra sube sola (EnergyManager.Update).
///  - Modo azul (bonus):
///      Perfect/Good -> HitBlue(): la barra sube (acumula riesgo).
///      Miss         -> MissBlueAndInject(): inyeccion masiva + boost + vuelta al rojo.
///
/// CAMBIOS respecto a la version anterior:
///   - Usa BeatManager.EvaluateTiming() en vez de comparar manualmente con _hitWindow.
///     La ventana efectiva viene del BeatManager (con compensacion de latencia incluida).
///   - Pasa TimingResult a EnergyManager.HitRed() para escalar la energia por precision.
///   - Expone OnTimingResult para que TimingFeedbackUI muestre PERFECT / GOOD / MISS.
///   - _hitWindow se conserva en el Inspector como legacy pero ya no controla la deteccion.
/// </summary>
public class RhythmInput : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private BeatManager _beatManager;
    [SerializeField] private CrosshairController _crosshair;
    [Tooltip("Referencia al PlayerMovement para detectar el modo kayak.\n" +
             "Cuando IsOnKayak=true, KayakController gestiona los inputs; RhythmInput los ignora.")]
    [SerializeField] private PlayerMovement _player;

    [Header("Deteccion de hits")]
    [Tooltip("LEGACY: ya no controla la deteccion. La ventana viene de BeatManager.GoodWindow. " +
             "Se mantiene para no romper prefabs existentes.")]
    [SerializeField] private float _hitWindow = 0.15f;
    /// <summary>Ventana legacy expuesta solo para suprimir CS0414. No se usa en la deteccion.</summary>
    public float HitWindowLegacy => _hitWindow;

    [Header("Combo")]
    [Tooltip("El bonus no depende del combo. Sirve para metricas de UI y TFG.")]
    [SerializeField] private bool _trackCombo = true;

    [Header("Eventos - logica")]
    public UnityEvent OnBeatHit;
    public UnityEvent OnBeatMiss;
    public UnityEvent<int> OnComboChanged;
    public UnityEvent OnBonusActivated;
    public UnityEvent OnBonusDeactivated;
    public UnityEvent<float> OnBlueInjection;

    [Header("Eventos - timing (nuevo)")]
    [Tooltip("Se dispara con cada click. Conecta TimingFeedbackUI.Show() aqui para " +
             "mostrar PERFECT, GOOD o MISS en pantalla.")]
    public UnityEvent<TimingResult> OnTimingResult;

    // --- Estado publico ---

    public int CurrentCombo { get; private set; }
    public bool IsBonusActive { get; private set; }

    /// <summary>Ultimo resultado de timing registrado. Util para otros sistemas.</summary>
    public TimingResult LastTimingResult { get; private set; }

    // --- Anti-spam: un solo hit valido por beat ---
    // Si el jugador hace click varias veces en la misma ventana de beat,
    // solo el primero cuenta como hit; los demas se convierten en miss.
    private int _lastHitBeatIdx = -1;

    // --- Input ---

    private InputAction _leftClickAction;
    private InputAction _rightClickAction;

    private void OnEnable()
    {
        _leftClickAction = new InputAction("RhythmLeftClick", binding: "<Mouse>/leftButton");
        _leftClickAction.performed += HandleClick;
        _leftClickAction.Enable();

        _rightClickAction = new InputAction("RhythmRightClick", binding: "<Mouse>/rightButton");
        _rightClickAction.performed += HandleClick;
        _rightClickAction.Enable();
    }

    private void OnDisable()
    {
        if (_leftClickAction != null)
        {
            _leftClickAction.performed -= HandleClick;
            _leftClickAction.Disable();
        }
        if (_rightClickAction != null)
        {
            _rightClickAction.performed -= HandleClick;
            _rightClickAction.Disable();
        }
    }

    private void Update()
    {
        // Sincronizar estado de bonus con EnergyManager.
        // EnergyManager puede entrar en modo azul por overflow de un hit (HitRed),
        // o salir de el por decay natural. Detectamos esos cambios aqui.
        if (EnergyManager.Instance == null) return;

        bool managerBonus = EnergyManager.Instance.IsBlueMode;
        if (managerBonus && !IsBonusActive)
        {
            IsBonusActive = true;
            OnBonusActivated?.Invoke();
        }
        else if (!managerBonus && IsBonusActive)
        {
            IsBonusActive = false;
            OnBonusDeactivated?.Invoke();
        }
    }

    private void HandleClick(InputAction.CallbackContext ctx)
    {
        // Bug: cuando SongTime=0 (sin música), EvaluateTiming devuelve PERFECT
        // (0 % BeatDuration = 0 = exactamente en el beat). Ignorar si la música no corre.
        if (_beatManager == null || !_beatManager.IsPlaying) return;

        // Modo kayak: KayakController gestiona ambos botones (izq y dcho) y llama
        // a EnergyManager directamente. RhythmInput se salta para evitar doble-conteo.
        if (_player != null && _player.IsOnKayak) return;

        // Animar crosshair independientemente del resultado de timing.
        if (_crosshair != null) _crosshair.TriggerClick();

        // Evaluar timing. BeatManager usa CorrectedTime (con compensacion de latencia).
        TimingResult result = TimingResult.Miss;
        if (_beatManager != null)
            result = _beatManager.EvaluateTiming();

        // Anti-spam: un solo hit valido por beat.
        // Si el jugador ya acerto en este mismo beat (misma ventana), el click extra
        // se convierte en Miss aunque tecnicamente caiga dentro de la ventana.
        // Esto impide que spamear dentro del beat window genere energia ilimitada.
        if (result != TimingResult.Miss && _beatManager != null)
        {
            int beatIdx = _beatManager.CurrentBeatIndex;
            if (beatIdx == _lastHitBeatIdx)
            {
                // Click extra en la misma ventana → penalizar como miss
                result = TimingResult.Miss;
            }
            else
            {
                // Primer hit valido en este beat → registrar y permitir
                _lastHitBeatIdx = beatIdx;
            }
        }

        LastTimingResult = result;

        // Notificar a la UI de feedback.
        OnTimingResult?.Invoke(result);

        if (result != TimingResult.Miss)
            RegisterHit(result);
        else
            RegisterMiss();
    }

    private void RegisterHit(TimingResult timing)
    {
        var em = EnergyManager.Instance;

        if (_trackCombo)
        {
            CurrentCombo++;
            OnComboChanged?.Invoke(CurrentCombo);
        }

        if (em != null && em.IsBlueMode)
        {
            // En modo azul: acumula sangre azul. El timing no escala en azul
            // porque el riesgo ya esta en que la barra puede llenarse y matar.
            em.HitBlue();
        }
        else if (em != null)
        {
            // En modo rojo: Perfect = 1.5x energia, Good = 1.0x energia.
            em.HitRed(timing);
        }

        OnBeatHit?.Invoke();
    }

    private void RegisterMiss()
    {
        var em = EnergyManager.Instance;
        bool wasBonus = IsBonusActive;

        if (wasBonus)
        {
            // Fallo en azul: inyeccion masiva con boost de velocidad/salto.
            float boost = em != null ? em.MissBlueAndInject() : 0f;
            if (boost > 0f) OnBlueInjection?.Invoke(boost);
            em?.ExitBlueMode();
            IsBonusActive = false;
            OnBonusDeactivated?.Invoke();
        }
        else
        {
            // Fallo en rojo: penalizacion (la barra sube).
            em?.MissRed();
        }

        if (_trackCombo)
        {
            CurrentCombo = 0;
            OnComboChanged?.Invoke(0);
        }

        OnBeatMiss?.Invoke();
    }
}
