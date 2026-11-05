using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Gestiona los clics del jugador y los evalúa respecto al beat de la música.
///
/// Funcionamiento de la jeringuilla.
///  - Modo rojo (normal). Con Perfect o Good se llama a HitRed(timing) y la sangre roja baja. Perfect da
///    1,5 veces más energía que Good. Con Miss se llama a MissRed() y la sangre roja sube como penalización.
///    Además, la sangre roja sube poco a poco por sí sola (EnergyManager.Update).
///  - Modo azul (bonus). Con Perfect o Good se llama a HitBlue() y la sangre azul sube, lo que acumula riesgo.
///    Con Miss se llama a LoseBlue(), se pierde toda la sangre azul y se vuelve al modo rojo.
///
/// El resultado del clic lo calcula BeatManager.EvaluateClickTiming(), que compensa la latencia. OnTimingResult
/// avisa a la interfaz para que muestre Perfect, Good o Miss. El sistema anti-spam está en EnterSlot y ProcessClick.
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
    public UnityEvent<float> OnBlueLost;

    [Header("Eventos - timing (nuevo)")]
    [Tooltip("Se dispara con cada click. Conecta TimingFeedbackUI.Show() aqui para " +
             "mostrar PERFECT, GOOD o MISS en pantalla.")]
    public UnityEvent<TimingResult> OnTimingResult;

    // -=+=- Estado público -=+=-

    public int CurrentCombo { get; private set; }
    public bool IsBonusActive { get; private set; }

    /// <summary>Último resultado registrado. Lo pueden consultar otros sistemas.</summary>
    public TimingResult LastTimingResult { get; private set; }

    // -=+=- Anti-spam por beat -=+=-
    // El "slot" es el beat más cercano al clic (o el medio beat, con "Doble tempo"). Solo el primer clic de cada
    // slot puede acertar, y cada clic de más es un fallo que cuesta el doble que el anterior, por lo que, después
    // de un acierto, el siguiente clic ya cuesta el doble de lo que curó. Además, si varios slots seguidos tienen
    // clics de más, sube el "calor" del spam y todos los fallos cuestan (1 + calor) veces más. Un jugador que no
    // hace spam nunca paga más que un fallo normal.
    private const float MaxSpamScale = 8f;
    private const int   MaxSpamHeat  = 3;
    private int  _slot = int.MinValue;
    private int  _clicksInSlot;
    private bool _slotHadHit;
    private int  _spamHeat;
    private float _pendingMissScale = 1f;   // multiplicador del fallo que se está procesando

    // -=+=- Entrada -=+=-

    private InputAction _leftClickAction;
    private InputAction _rightClickAction;

    // Modo Dios: último beat en el que se hizo un clic automático, para hacer solo uno por beat.
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
        // Se sincroniza el estado del bonus con EnergyManager, ya que este puede entrar en modo azul cuando un
        // acierto desborda la jeringuilla (HitRed) o salir de él cuando la sangre azul se agota sola.
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

        // Modo Dios: hace un clic perfecto en cada beat, salvo en el kayak, por lo que el ritmo se juega solo.
        if (EnergyManager.Instance.godMode
            && _beatManager != null && _beatManager.IsPlaying
            && (_player == null || !_player.IsOnKayak))
        {
            int beat = _beatManager.CurrentBeatIndex;
            if (_lastAutoBeatIdx < 0) _lastAutoBeatIdx = beat;   // no se hace el clic automático en el primer fotograma
            else if (beat != _lastAutoBeatIdx)
            {
                _lastAutoBeatIdx = beat;
                ProcessClick(affectSyringe: false);   // muestra el resultado y, a través de WaterDiveController, recoge la cuerda
            }
        }
        else _lastAutoBeatIdx = -1;   // se reinicia al salir del modo Dios o al entrar en el kayak
    }

    private void HandleClick(InputAction.CallbackContext ctx)
    {
        // Si la música no está sonando, SongTime vale 0 y el clic siempre daría Perfect (0 % BeatDuration = 0),
        // por lo que en ese caso se ignora.
        if (_beatManager == null || !_beatManager.IsPlaying) return;
        if (TutorialGate.Frozen) return;   // el tiempo está parado por el tutorial, así que el clic no cuenta

        // En el kayak, KayakController gestiona los dos botones y llama directamente a EnergyManager, por lo que
        // RhythmInput no hace nada para no contar el clic dos veces. Si la referencia al jugador no está asignada
        // en la escena, se busca automáticamente.
        if (_player == null) _player = FindAnyObjectByType<PlayerMovement>();
        if (_player != null && _player.IsOnKayak) return;

        // Auto-latido (tienda): en su beat el latido lo da la mejora, así que el clic del jugador se ignora para que no se solapen.
        if (AutoBeater.SuppressPlayerClickNow()) return;

        ProcessClick(affectSyringe: true);
    }

    /// <summary>
    /// Clic que llega desde otro sistema, como el kayak, cuyos botones sirven a la vez para remar y para latir. Hace lo
    /// mismo que un clic normal (indicador, jeringuilla, energía, rachas, monedas, contratos y telemetría) y devuelve
    /// el resultado para que el sistema que lo llama haga lo suyo, como remar o girar.
    /// 'missScale' multiplica la penalización de un fallo, ya que el kayak castiga más.
    /// </summary>
    public TimingResult ExternalClick(float missScale = 1f)
    {
        // Si el mismo clic ya se procesó en este fotograma (por ejemplo, como clic normal), no se cuenta otra vez.
        if (_lastClickFrame == Time.frameCount) return LastTimingResult;
        _externalMissScale = Mathf.Max(0f, missScale);
        ProcessClick(affectSyringe: true);
        _externalMissScale = 1f;
        return LastTimingResult;
    }
    private float _externalMissScale = 1f;

    private int _lastClickFrame = -1;

    private void ProcessClick(bool affectSyringe)
    {
        if (affectSyringe) _lastClickFrame = Time.frameCount;
        // Procesa un clic, real o automático. Con affectSyringe = false (clic automático del modo Dios) solo se
        // muestra el indicador del beat, sin tocar la jeringuilla. La animación del punto de mira se lanza siempre,
        // sea cual sea el resultado.
        if (_crosshair != null) _crosshair.TriggerClick();

        // Se evalúa el clic. BeatManager usa CorrectedTime, que compensa la latencia.
        TimingResult result = TimingResult.Miss;
        if (_beatManager != null)
            result = _beatManager.EvaluateClickTiming();   // con "Doble tempo" también cuenta el medio beat

        // Anti-spam: solo el primer clic del slot puede acertar. Solo se aplica a los clics reales, no al automático.
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

        // Se avisa a la interfaz siempre, también con el corazón lanzado, para que los indicadores del beat sigan
        // apareciendo y el jugador pueda calcular cuándo tirar de la cuerda.
        OnTimingResult?.Invoke(result);

        // Clic automático del modo Dios: solo muestra el indicador, sin tocar la jeringuilla.
        if (!affectSyringe) return;

        RunStats.HeartPress(result);   // contratos: latidos, fallos, Perfect y rachas
        if (_beatManager != null) StudyTelemetry.Click(_beatManager.ClickOffset, result);   // modo prueba

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
            // En modo azul se acumula sangre azul. El resultado del clic no cambia la cantidad, ya que el riesgo
            // está en que la jeringuilla se puede llenar y matar al jugador.
            em.HitBlue();
        }
        else if (em != null)
        {
            // En modo rojo, Perfect da 1,5 veces la energía normal y Good la da una vez.
            em.HitRed(timing);
        }

        // "Racha dorada" (tienda): 5 monedas cada 16 aciertos seguidos.
        _coinStreak++;
        if (_coinStreak % 16 == 0 && PlayerProgress.GetLevel(ShopIds.ComboCoins) > 0) LevelCoins.Collect(5);

        // "Latido dorado" (tienda): 1 moneda por cada Perfect.
        if (timing == TimingResult.Perfect && PlayerProgress.GetLevel(ShopIds.PerfectCoin) > 0)
            LevelCoins.Collect(1);

        OnBeatHit?.Invoke();
    }

    private int _coinStreak;   // aciertos seguidos, para "Racha dorada"

    /// <summary>Cambia de slot y actualiza el "calor" del spam según cómo terminó el anterior.</summary>
    private void EnterSlot(int slot)
    {
        if (_slot == int.MinValue || slot - _slot > 4) _spamHeat = 0;                    // después de una pausa larga, se enfría
        else if (_clicksInSlot > 1)      _spamHeat = Mathf.Min(MaxSpamHeat, _spamHeat + 1);  // otro slot con spam
        else if (_slotHadHit)            _spamHeat = Mathf.Max(0, _spamHeat - 1);            // slot sin spam, se enfría
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
            // Fallo en azul, se pierde toda la sangre azul.
            float lost = em != null ? em.LoseBlue() : 0f;
            if (lost > 0f) OnBlueLost?.Invoke(lost);
            em?.ExitBlueMode();
            IsBonusActive = false;
            OnBonusDeactivated?.Invoke();
        }
        else
        {
            // Fallo en rojo, la sangre roja sube como penalización, multiplicada por el anti-spam.
            em?.MissRed(_pendingMissScale * _externalMissScale);
        }

        if (_trackCombo)
        {
            CurrentCombo = 0;
            OnComboChanged?.Invoke(0);
        }

        OnBeatMiss?.Invoke();
    }
}
