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
/// El timing lo evalua BeatManager.EvaluateClickTiming() (con compensacion de latencia). OnTimingResult avisa a la
/// UI de feedback (PERFECT / GOOD / MISS). Anti-spam: ver EnterSlot / ProcessClick.
/// </summary>
public class RhythmInput : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private BeatManager _beatManager;
    [SerializeField] private CrosshairController _crosshair;
    [Tooltip("Referencia al PlayerMovement para detectar el modo kayak.\n" +
             "Cuando IsOnKayak=true, KayakController gestiona los inputs; RhythmInput los ignora.")]
    [SerializeField] private PlayerMovement _player;

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

    // --- Anti-spam (por beat) ---
    // «Slot» = beat (o medio beat, con «Doble tempo») más cercano al clic. Solo el PRIMER clic de cada slot puede
    // acertar; cada clic de más es un fallo que cuesta el doble que el anterior (tras un acierto ya cuesta 2×,
    // más de lo que curó). Además, si varios slots seguidos tienen clics de más, sube el «calor» del spam y TODOS
    // los fallos cuestan (1 + calor)× más. Un jugador que no spamea nunca paga más que un fallo normal.
    private const float MaxSpamScale = 8f;
    private const int   MaxSpamHeat  = 3;
    private int  _slot = int.MinValue;
    private int  _clicksInSlot;
    private bool _slotHadHit;
    private int  _spamHeat;
    private float _pendingMissScale = 1f;   // multiplicador del fallo que se está procesando

    // --- Input ---

    private InputAction _leftClickAction;
    private InputAction _rightClickAction;

    // God Mode: último beat auto-clicado (para auto-clicar una vez por beat).
    private int _lastAutoBeatIdx = -1;

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

        // God Mode: auto-clic PERFECTO en cada beat (juega el ritmo solo), EXCEPTO en el kayak.
        if (EnergyManager.Instance.godMode
            && _beatManager != null && _beatManager.IsPlaying
            && (_player == null || !_player.IsOnKayak))
        {
            int beat = _beatManager.CurrentBeatIndex;
            if (_lastAutoBeatIdx < 0) _lastAutoBeatIdx = beat;   // no auto-clicar en el primer frame
            else if (beat != _lastAutoBeatIdx)
            {
                _lastAutoBeatIdx = beat;
                ProcessClick(affectSyringe: false);   // feedback + (vía WaterDiveController) reelar
            }
        }
        else _lastAutoBeatIdx = -1;   // reset al salir de God Mode o entrar al kayak
    }

    private void HandleClick(InputAction.CallbackContext ctx)
    {
        // Bug: cuando SongTime=0 (sin música), EvaluateTiming devuelve PERFECT
        // (0 % BeatDuration = 0 = exactamente en el beat). Ignorar si la música no corre.
        if (_beatManager == null || !_beatManager.IsPlaying) return;

        // Modo kayak: KayakController gestiona ambos botones (izq y dcho) y llama
        // a EnergyManager directamente. RhythmInput se salta para evitar doble-conteo.
        if (_player != null && _player.IsOnKayak) return;

        // Auto-latido (tienda): en SU beat el latido lo da él; el clic del jugador se ignora (si no, se pisaban).
        if (AutoBeater.SuppressPlayerClickNow()) return;

        ProcessClick(affectSyringe: true);
    }

    /// <summary>
    /// Procesa un "clic" (real o auto). Con <paramref name="affectSyringe"/> = false (auto-clic de
    /// God Mode) solo muestra el feedback del beat, sin tocar la jeringuilla.
    /// </summary>
    private void ProcessClick(bool affectSyringe)
    {
        // Animar crosshair independientemente del resultado de timing.
        if (_crosshair != null) _crosshair.TriggerClick();

        // Evaluar timing. BeatManager usa CorrectedTime (con compensacion de latencia).
        TimingResult result = TimingResult.Miss;
        if (_beatManager != null)
            result = _beatManager.EvaluateClickTiming();   // (con «Doble tempo» también el medio beat)

        // Anti-spam: solo el primer clic del slot puede acertar (solo con clics reales, no con el auto-clic).
        _pendingMissScale = 1f;
        if (affectSyringe && _beatManager != null)
        {
            int slot = _beatManager.NearestClickSlot;
            if (slot != _slot) EnterSlot(slot);

            if (_clicksInSlot > 0) result = TimingResult.Miss;
            _pendingMissScale = Mathf.Min(MaxSpamScale, Mathf.Pow(2f, _clicksInSlot)) * (1 + _spamHeat);
            _clicksInSlot++;
            if (result != TimingResult.Miss) _slotHadHit = true;
        }

        LastTimingResult = result;

        // Notificar a la UI de feedback. Esto ocurre SIEMPRE (también con el corazón lanzado), para
        // que los indicadores del beat sigan apareciendo y puedas cronometrar los tirones de cuerda.
        OnTimingResult?.Invoke(result);

        // God Mode auto-clic: solo feedback visual, no toca la jeringuilla (ni late ni rellena).
        if (!affectSyringe) return;

        RunStats.HeartPress(result);   // (contratos: latidos, fallos, Perfect, rachas)
        if (_beatManager != null) StudyTelemetry.Click(_beatManager.ClickOffset, result);   // (modo prueba)

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

        // «Racha dorada» (tienda): +5 monedas cada 16 aciertos seguidos.
        _coinStreak++;
        if (_coinStreak % 16 == 0 && PlayerProgress.GetLevel(ShopIds.ComboCoins) > 0) LevelCoins.Collect(5);

        // «Latido dorado» (tienda): +1 moneda por cada Perfect.
        if (timing == TimingResult.Perfect && PlayerProgress.GetLevel(ShopIds.PerfectCoin) > 0)
            LevelCoins.Collect(1);

        OnBeatHit?.Invoke();
    }

    private int _coinStreak;   // aciertos seguidos (para «Racha dorada»)

    /// <summary>Cambia de slot: actualiza el «calor» del spam según cómo acabó el anterior.</summary>
    private void EnterSlot(int slot)
    {
        if (_slot == int.MinValue || slot - _slot > 4) _spamHeat = 0;                    // pausa larga: se enfría
        else if (_clicksInSlot > 1)      _spamHeat = Mathf.Min(MaxSpamHeat, _spamHeat + 1);  // otro slot con spam
        else if (_slotHadHit)            _spamHeat = Mathf.Max(0, _spamHeat - 1);            // slot limpio: enfría
        _slot = slot;
        _clicksInSlot = 0;
        _slotHadHit = false;
    }

    private void RegisterMiss()
    {
        _coinStreak = 0;
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
            // Fallo en rojo: penalizacion (la barra sube), escalada por el anti-spam.
            em?.MissRed(_pendingMissScale);
        }

        if (_trackCombo)
        {
            CurrentCombo = 0;
            OnComboChanged?.Invoke(0);
        }

        OnBeatMiss?.Invoke();
    }
}
