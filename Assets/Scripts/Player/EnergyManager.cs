using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Funcionamiento de la jeringuilla.
///  - Solo hay una variable, la sangre que hay en la jeringuilla (S).
///  - La energía del jugador es MaxSangre - S, por lo que, cuanta menos sangre hay en la jeringuilla, más energía
///    tiene el jugador en el corazón.
///  - Si la jeringuilla se llena por completo, el corazón se queda sin sangre y el jugador muere.
///
/// Cómo cambia la sangre.
///  - Con el tiempo, la sangre va llenando la jeringuilla poco a poco, es decir, la energía se va consumiendo.
///  - Las acciones (saltar, hacer dash, deslizarse...) también añaden sangre a la jeringuilla.
///  - Un acierto en modo rojo quita sangre de la jeringuilla y la devuelve al corazón. La cantidad depende del
///    resultado, ya que Perfect da 1,5 veces lo que da Good.
///  - Un fallo en modo rojo añade sangre extra como penalización.
///  - En el modo azul (bonus), la jeringuilla roja está vacía y los aciertos añaden sangre azul. Cuanta más sangre
///    azul se acumula, más cerca está el límite, y al fallar se pierde toda.
/// </summary>
public class EnergyManager : MonoBehaviour
{
    public static EnergyManager Instance { get; private set; }

    [Header("Capacidad de la jeringuilla")]
    [SerializeField] private float _maxSyringe = 100f;

    [Header("Llenado pasivo (modo rojo)")]
    [Tooltip("Sangre roja que entra en la jeringuilla por segundo (energia que se pierde sola)")]
    [SerializeField] private float _passiveFillPerSecond = 4f;

    [Header("Decay del azul (modo bonus)")]
    [Tooltip("Sangre azul que se pierde por segundo en bonus (la barra baja sola)")]
    [SerializeField] private float _blueDecayPerSecond = 6f;

    [Header("Hit / Miss en modo rojo")]
    [Tooltip("Sangre base que se inyecta por cada acierto (Good). Perfect da 1.5x este valor.")]
    [SerializeField] private float _hitInjectAmount = 14f;
    [Tooltip("Sangre extra que entra al fallar")]
    [SerializeField] private float _missAbsorbAmount = 10f;

    [Header("Modo azul (bonus)")]
    [Tooltip("Sangre azul que se anade a la jeringuilla por cada acierto en bonus")]
    [SerializeField] private float _bluePerHit = 12f;

    [Header("Sobrecarga (jeringuilla azul llena)")]
    [Tooltip("Beats en los que las acciones NO gastan energía al llenar la jeringuilla azul del todo.")]
    [SerializeField] private float _overloadBeats = 4f;

    [Header("Respiro (tras perder la sangre azul)")]
    [Tooltip("Beats en los que las ventanas de acierto son más amplias después de perder la sangre azul.")]
    [SerializeField] private float _graceBeats = 3f;
    [Tooltip("Aumento máximo de las ventanas (0,5 = +50 %) si se pierde la jeringuilla azul LLENA; proporcional a lo perdido.")]
    [SerializeField] private float _graceMaxBonus = 0.5f;

    [Header("Debug / Diseño de nivel")]
    [Tooltip("God Mode: las acciones no gastan energía y la jeringuilla no se llena pasivamente.\n" +
             "Los obstáculos siguen siendo letales (ObstacleCollision no se ve afectado).")]
    public bool godMode = false;

    [Header("Eventos")]
    public UnityEvent<float> OnSyringeChanged;
    public UnityEvent<float> OnEnergyChanged;
    public UnityEvent OnSyringeFull;
    public UnityEvent OnEnergyFull;
    public UnityEvent<float> OnRedHit;
    public UnityEvent<float> OnRedMiss;
    public UnityEvent<float> OnBlueLost;   // fallo con sangre azul: se pierde toda (recibe la cantidad perdida)

    /// <summary>Empieza la Sobrecarga (duración en beats). Lo usa el efecto visual.</summary>
    public static event System.Action<float> OverloadStarted;
    /// <summary>Empieza el Respiro (fracción de sangre azul perdida, de 0 a 1, y duración en beats).</summary>
    public static event System.Action<float, float> GraceStarted;
    public UnityEvent OnBlueDepleted;
    public UnityEvent<float> OnBlueModeEntered;

    private float _syringe;
    private bool  _isBlueMode;
    private bool  _paused;   // congela el llenado pasivo (rebobinado y fin de nivel)
    private float _lastDrainTime = -999f;   // último instante en que un BloodSucker drenó sangre

    public float SyringeBlood => _syringe;
    public float MaxSyringe => _maxSyringe;

    /// <summary>
    /// Energía disponible para gastar. En rojo es el hueco libre en la jeringuilla (de 0 a maxSyringe). En azul es la
    /// energía base (100 %) más la sangre azul (de 0 a maxSyringe), es decir, de maxSyringe a 2 * maxSyringe.
    /// </summary>
    public float Energy => _isBlueMode
        ? (_maxSyringe + _syringe)   // base del 100 % más el bonus azul
        : (_maxSyringe - _syringe);  // hueco disponible (energía roja)

    /// <summary>
    /// Energía disponible, de 0 a 1. En azul solo usa la parte del bonus (de 0 a 1), para que la interfaz se muestre bien.
    /// </summary>
    public float EnergyPercent => _maxSyringe > 0f
        ? (_isBlueMode
            ? Mathf.Clamp01(_syringe / _maxSyringe)   // bonus azul, de 0 a 1
            : Mathf.Clamp01(Energy   / _maxSyringe))  // energía roja, de 0 a 1
        : 0f;

    /// <summary>Cantidad de sangre que hay en la jeringuilla, de 0 a 1.</summary>
    public float SyringePercent => _maxSyringe > 0f ? Mathf.Clamp01(_syringe / _maxSyringe) : 0f;

    public bool IsBlueMode => _isBlueMode;
    public bool IsDead => !godMode && !_isBlueMode && _syringe >= _maxSyringe;

    /// <summary>Indica si un BloodSucker ha drenado sangre en los últimos 0,15 s. Lo usa la barra para parpadear.</summary>
    public bool IsBeingDrained => Time.time - _lastDrainTime < 0.15f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (GetComponent<OverloadFX>() == null) gameObject.AddComponent<OverloadFX>();   // efecto de la Sobrecarga y del Respiro
        _baseMaxSyringe = _maxSyringe;
        ApplyShopUpgrades();
        // Todos los niveles empiezan con la jeringuilla vacía, sin sangre roja ni azul, es decir, con la energía al 100 %.
        _syringe    = 0f;
        _isBlueMode = false;

        // Excepción con "Buen desayuno" (tienda). El nivel empieza ya en modo azul, con la barra azul al 20, 40 o 60 %
        // según el nivel de la mejora.
        int breakfast = PlayerProgress.GetLevel(ShopIds.StartBoost);
        if (breakfast > 0)
        {
            _isBlueMode = true;
            _syringe    = _maxSyringe * Mathf.Clamp01(0.2f * breakfast);
        }
        PlayerProgress.OnChanged += ApplyShopUpgrades;   // si se compra en la tienda de la zona de inicio, se aplica al momento
    }

    private void OnDestroy()
    {
        PlayerProgress.OnChanged -= ApplyShopUpgrades;
        if (Instance == this) Instance = null;
    }

    // -=+=- Mejoras de la tienda -=+=-
    private float _baseMaxSyringe;

    private int _streak;   // aciertos seguidos, para "Racha"

    /// <summary>Aciertos seguidos en modo rojo. Se reinicia al fallar.</summary>
    public int Streak => _streak;

    /// <summary>"Sangre azul". La sangre azul se gasta un 25 o un 50 % más despacio.</summary>
    public static float BlueDecayMultiplier
    {
        get { int l = PlayerProgress.GetLevel(ShopIds.BlueBoost); return l <= 0 ? 1f : l == 1 ? 0.75f : 0.5f; }
    }

    /// <summary>"Coagulante". La pérdida pasiva baja un 15 o un 30 %.</summary>
    public static float PassiveMultiplier
    {
        get { int l = PlayerProgress.GetLevel(ShopIds.SlowBleed); return l <= 0 ? 1f : l == 1 ? 0.85f : 0.7f; }
    }

    /// <summary>"Piel gruesa". Los drenajes (BloodSuckers y golpes) quitan un 25 o un 50 % menos.</summary>
    public static float DrainMultiplier
    {
        get { int l = PlayerProgress.GetLevel(ShopIds.DrainGuard); return l <= 0 ? 1f : l == 1 ? 0.75f : 0.5f; }
    }

    /// <summary>"Metabolismo eficiente". Las acciones cuestan un 5 % menos por cada nivel.</summary>
    public static float ActionCostMultiplier => 1f - 0.05f * PlayerProgress.GetLevel(ShopIds.Efficient);

    /// <summary>"Pulso fuerte". Cada acierto devuelve un 15, un 30 o un 50 % más de sangre.</summary>
    public static float PulseMultiplier
    {
        get
        {
            int l = PlayerProgress.GetLevel(ShopIds.StrongPulse);
            return l <= 0 ? 1f : l == 1 ? 1.15f : l == 2 ? 1.3f : 1.5f;
        }
    }

    /// <summary>"Jeringuilla grande". Un 10 % más de capacidad por nivel, conservando la sangre que ya hay.</summary>
    private void ApplyShopUpgrades()
    {
        if (_baseMaxSyringe <= 0f) _baseMaxSyringe = _maxSyringe;
        float newMax = _baseMaxSyringe * (1f + 0.1f * PlayerProgress.GetLevel(ShopIds.BigSyringe));
        if (Mathf.Approximately(newMax, _maxSyringe)) return;
        _maxSyringe = newMax;
        _syringe = Mathf.Min(_syringe, _maxSyringe);
        OnSyringeChanged?.Invoke(SyringePercent);
        OnEnergyChanged?.Invoke(EnergyPercent);
    }

    [Header("Corazón firme (mejora de la tienda)")]
    [Tooltip("Segundos manteniendo el clic izquierdo para que cuente como 'mantener el corazón' (un clic normal\n" +
             "de beat es más corto y no lo activa).")]
    [SerializeField] private float _steadyHoldDelay = 0.25f;
    private float _steadyHoldTime;

    /// <summary>
    /// Indica si el jugador está manteniendo el corazón (clic izquierdo sostenido, con la mejora "Corazón firme").
    /// Mientras tanto no se pierde sangre de forma pasiva, aunque los fallos, los golpes y los BloodSuckers siguen contando.
    /// </summary>
    public bool IsHoldingHeart { get; private set; }

    private void UpdateSteadyHeart()
    {
        bool owned   = PlayerProgress.GetLevel(ShopIds.SteadyHeart) > 0;
        bool playing = BeatManager.Instance == null || BeatManager.Instance.IsPlaying;
        bool pressed = owned && playing && Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed);
        _steadyHoldTime = pressed ? _steadyHoldTime + Time.deltaTime : 0f;
        // En cuanto la mano se queda cerrada (ver HandAnimationController) ya cuenta. Si no hay mano visible, por ejemplo
        // en el kayak, basta con mantener el clic '_steadyHoldDelay' segundos.
        IsHoldingHeart  = pressed && (HandAnimationController.IsHandHeld || _steadyHoldTime >= _steadyHoldDelay);
    }

    private void Update()
    {
        UpdateSteadyHeart();

        // Modo Dios: se congela toda la lógica de energía, sin llenado pasivo ni muerte por la jeringuilla.
        if (godMode) return;

        // En pausa (rebobinado o fin de nivel), la jeringuilla no cambia sola.
        if (_paused) return;

        // Antes de que empiece la partida, es decir, mientras BeatManager no está reproduciendo, no se aplican cambios pasivos.
        if (BeatManager.Instance != null && !BeatManager.Instance.IsPlaying) return;

        if (_isBlueMode)
        {
            if (_blueDecayPerSecond > 0f && _syringe > 0f)
                ChangeSyringe(-_blueDecayPerSecond * BlueDecayMultiplier * Time.deltaTime);   // "Sangre azul"

            if (_syringe <= 0f)
            {
                _isBlueMode = false;
                OnBlueDepleted?.Invoke();
            }
        }
        else
        {
            // "Corazón firme": mientras se mantiene el corazón, no se pierde sangre de forma pasiva.
            if (_passiveFillPerSecond > 0f && _syringe < _maxSyringe && !IsHoldingHeart)
                ChangeSyringe(_passiveFillPerSecond * PassiveMultiplier * Time.deltaTime);    // "Coagulante"
        }
    }

    // -=+=- API pública -=+=-

    /// <summary>
    /// Acierto en modo rojo. Perfect da 1,5 veces la cantidad base y Good la da una vez. Si lo que se recupera supera
    /// la sangre que hay en la jeringuilla, se entra automáticamente en modo azul.
    /// </summary>
    public float HitRed(TimingResult timing)
    {
        if (_isBlueMode) return 0f;

        float timingMultiplier;
        if (timing == TimingResult.Perfect)
            timingMultiplier = PlayerProgress.GetLevel(ShopIds.Perfectionist) > 0 ? 2f : 1.5f;   // "Perfeccionista"
        else
            timingMultiplier = 1.0f;

        float subtract = _hitInjectAmount * timingMultiplier * PulseMultiplier;   // mejora "Pulso fuerte"

        // "Segundo aliento": con poca energía, los aciertos valen el doble.
        if (PlayerProgress.GetLevel(ShopIds.SecondWind) > 0 && EnergyPercent < 0.2f) subtract *= 2f;
        // "Racha": a partir de 8 aciertos seguidos, un 25 % más.
        _streak++;
        if (PlayerProgress.GetLevel(ShopIds.Streak) > 0 && _streak >= 8) subtract *= 1.25f;
        float redAvailable = _syringe;

        StudyTelemetry.Recovered(subtract);
        if (redAvailable >= subtract)
        {
            ChangeSyringe(-subtract);
            OnRedHit?.Invoke(subtract);
            return subtract;
        }

        // Si sobra, se vacía la sangre roja y lo que sobra se convierte en sangre azul.
        float overflow = subtract - redAvailable;

        if (redAvailable > 0f)
        {
            ChangeSyringe(-redAvailable);
            OnRedHit?.Invoke(redAvailable);
        }

        if (BlueBroken) return redAvailable;   // la jeringuilla azul está rota después de un golpe del jefe, así que no se llena

        _isBlueMode = true;
        ChangeSyringe(overflow);
        OnBlueModeEntered?.Invoke(overflow);

        return redAvailable;
    }

    /// <summary>
    /// Fallo en modo rojo, que añade sangre extra como penalización. <paramref name="penaltyScale"/> multiplica la
    /// penalización (1 es lo normal). Lo usan el kayak, ya que fallar remando cuesta más, y el anti-spam de RhythmInput,
    /// en el que cada clic de más en el mismo beat cuesta el doble.
    /// </summary>
    public float MissRed(float penaltyScale = 1f)
    {
        if (_isBlueMode) return 0f;
        _streak = 0;                // se rompe la racha

        float before = _syringe;
        ChangeSyringe(_missAbsorbAmount * Mathf.Max(0f, penaltyScale) * StudyConfig.MissPenaltyMult);   // modo prueba
        float absorbed = _syringe - before;
        StudyTelemetry.MissPenalty(absorbed);
        if (absorbed > 0f) OnRedMiss?.Invoke(absorbed);
        return absorbed;
    }

    /// <summary>Acierto en modo bonus, que acumula sangre azul.</summary>
    public void HitBlue()
    {
        if (!_isBlueMode) return;
        ChangeSyringe(_bluePerHit);
    }

    /// <summary>
    /// Fallo en modo bonus. Se pierde toda la sangre azul y empieza el "Respiro", en el que durante unos beats las
    /// ventanas de acierto son más amplias, tanto más cuanta más sangre azul se haya perdido. Devuelve la cantidad perdida.
    /// </summary>
    public float LoseBlue()
    {
        if (!_isBlueMode) return 0f;
        float lost = _syringe;
        float frac = _maxSyringe > 0f ? Mathf.Clamp01(lost / _maxSyringe) : 0f;
        StudyTelemetry.BlueLost(lost);
        _overloadUntil = double.NegativeInfinity;   // fallar corta la Sobrecarga
        ChangeSyringe(-_syringe);
        if (frac > 0f)
        {
            _graceFrac = frac;
            _graceUntil = SongBeat() + _graceBeats;
            GraceStarted?.Invoke(frac, _graceBeats);
        }
        OnBlueLost?.Invoke(lost);
        return lost;
    }

    // -=+=- Sobrecarga y Respiro -=+=-
    private double _overloadUntil = double.NegativeInfinity;
    private double _graceUntil = double.NegativeInfinity;
    private float _graceFrac;

    /// <summary>Beat actual de la canción, según el reloj del BeatManager. Es muy negativo si no hay canción.</summary>
    private static double SongBeat()
    {
        BeatManager bm = BeatManager.Instance;
        return bm != null && bm.HasClock ? bm.ClockBeats : double.NegativeInfinity;
    }

    /// <summary>Indica si está en Sobrecarga, es decir, si la jeringuilla azul se llenó del todo hace menos de _overloadBeats beats.</summary>
    public bool IsOverloaded => SongBeat() < _overloadUntil;

    /// <summary>Multiplicador de las ventanas de acierto por el "Respiro" (1 es lo normal).</summary>
    public static float GraceWindowMultiplier
    {
        get
        {
            EnergyManager em = Instance;
            if (em == null || SongBeat() >= em._graceUntil) return 1f;
            return 1f + em._graceMaxBonus * em._graceFrac;
        }
    }

    private void StartOverload()
    {
        _overloadUntil = SongBeat() + _overloadBeats;
        OverloadStarted?.Invoke(_overloadBeats);
    }

    /// <summary>Sale del modo bonus sin inyectar la sangre azul.</summary>
    public void ExitBlueMode()
    {
        _isBlueMode = false;
    }

    /// <summary>Intenta gastar energía en una acción (saltar, hacer dash...). Devuelve true si la acción se puede realizar.</summary>
    public bool TrySpend(float amount)
    {
        if (amount <= 0f) return true;
        if (IsOverloaded) return true;   // en Sobrecarga, las acciones no gastan energía
        // Antes de empezar el nivel (zona de inicio o cuenta atrás), el EnergyManager está desactivado y las acciones no gastan energía.
        if (!isActiveAndEnabled || _paused) return true;
        amount *= ActionCostMultiplier * StudyConfig.ActionCostMult;   // "Metabolismo eficiente" (tienda) y modo prueba
        if (godMode)      return true;  // modo Dios: la acción siempre se permite, sin coste
        if (_isBlueMode || Energy >= amount || CanOverspendWithExtraLife) StudyTelemetry.ActionCost(amount);

        if (_isBlueMode)
        {
            // En azul, la energía total es la base del 100 % más el bonus azul. Primero se gasta el bonus azul y, si no
            // cubre el coste, el resto se convierte en sangre roja y se sale del modo azul. En este modo, la acción siempre se permite.
            if (_syringe >= amount)
            {
                // El bonus cubre el coste entero.
                ChangeSyringe(-amount);
            }
            else
            {
                // Se gasta todo el bonus azul y lo que falta se convierte en sangre roja.
                float excess = amount - _syringe;
                ChangeSyringe(-_syringe);   // se vacía todo el azul, por lo que syringe = 0
                _isBlueMode = false;
                ChangeSyringe(excess);      // lo que falta se añade como sangre roja
                OnBlueDepleted?.Invoke();
            }
            return true;
        }

        if (Energy < amount)
        {
            // Sin energía suficiente, pero con vidas extra, la acción se permite igual. La jeringuilla se llena, el corazón se
            // queda sin sangre y se gasta una vida extra, con la que el jugador vuelve con el corazón lleno en el siguiente beat.
            if (!CanOverspendWithExtraLife) return false;
            ChangeSyringe(amount);
            return true;
        }
        ChangeSyringe(amount);
        return true;
    }

    /// <summary>Indica si se pueden hacer acciones sin energía, lo que ocurre mientras quede alguna vida extra.</summary>
    public static bool CanOverspendWithExtraLife => PlayerProgress.GetCount(ShopIds.ExtraLife) > 0;

    /// <summary>Devuelve energía gratis, quitando sangre de la jeringuilla.</summary>
    public void Restore(float amount)
    {
        if (amount > 0f) ChangeSyringe(-amount);
    }

    /// <summary>
    /// Golpe de un ataque del jefe. Quita 'fraction' de toda la sangre que se puede llegar a tener, contando la roja y la
    /// azul (el máximo total es dos jeringuillas, 100 % de roja y 100 % de azul), por lo que 0.25 es un cuarto.
    ///   - En azul, primero se gasta la sangre azul y, si no llega, se sale del azul y el resto sale de la roja.
    ///   - En rojo, la jeringuilla se llena esa cantidad. Si se llena del todo, el jugador muere, aunque las vidas extra
    ///     y la Adrenalina siguen funcionando.
    /// El modo Dios lo ignora y "Piel gruesa" lo reduce.
    /// </summary>
    public void TakeHitFraction(float fraction)
    {
        if (fraction <= 0f || godMode) return;
        float amount = 2f * _maxSyringe * fraction * DrainMultiplier;
        _lastDrainTime = Time.time;
        if (_isBlueMode)
        {
            if (_syringe > amount) { ChangeSyringe(-amount); return; }   // solo baja el azul
            amount -= _syringe;                                          // se acaba el azul...
            _isBlueMode = false;
            ChangeSyringe(-_syringe);
            OnBlueDepleted?.Invoke();
        }
        ChangeSyringe(amount);                                           // ...y el resto sale de la sangre roja
    }

    /// <summary>
    /// Golpe del jefe. En modo azul se pierde toda la sangre azul, y nada más. Si no, la jeringuilla se llena
    /// 'redFraction' de sangre roja (0.5 es media jeringuilla, así que dos golpes seguidos sin azul matan al jugador).
    /// </summary>
    public void TakeBossHit(float redFraction)
    {
        if (godMode) return;
        _lastDrainTime = Time.time;
        // Después de un golpe del jefe, la jeringuilla azul queda rota unos segundos y no se puede volver a llenar.
        _blueBrokenUntil = Time.time + _blueBrokenSeconds;
        if (_isBlueMode)
        {
            // La sangre azul hace de escudo, que se rompe (con un sonido de cristal) y absorbe el golpe entero.
            _isBlueMode = false;
            ChangeSyringe(-_syringe);
            OnBlueDepleted?.Invoke();
            GlassShatterSfx.Play(_shieldBreakSound, _shieldBreakVolume);
            return;
        }
        ChangeSyringe(_maxSyringe * Mathf.Clamp01(redFraction) * DrainMultiplier);
    }

    [Header("Escudo azul (golpes del jefe)")]
    [Tooltip("Segundos que la jeringuilla azul queda ROTA tras un golpe del jefe (no se puede volver a llenar).")]
    [SerializeField] private float _blueBrokenSeconds = 3f;
    [Tooltip("(Opcional) Sonido de cristal al romperse el escudo azul. Vacío = uno generado por código.")]
    [SerializeField] private AudioClip _shieldBreakSound;
    [Range(0f, 1f)] [SerializeField] private float _shieldBreakVolume = 0.9f;
    private float _blueBrokenUntil = -10f;

    /// <summary>Indica si la jeringuilla azul está rota por un golpe reciente del jefe. Mientras tanto, los aciertos no dan azul.</summary>
    public bool BlueBroken => Time.time < _blueBrokenUntil;

    /// <summary>
    /// Drena sangre del jugador de forma continua, añadiéndola a la jeringuilla, por lo que pierde energía. Lo llama el
    /// BloodSucker en cada fotograma, y no tiene efecto en modo Dios ni en modo azul. Activa IsBeingDrained para que la
    /// barra parpadee. Si la jeringuilla se llena, el jugador muere (OnSyringeFull), igual que al llenarla por fallos.
    /// </summary>
    public void DrainBlood(float amount)
    {
        if (amount <= 0f) return;
        if (godMode || _isBlueMode) return;   // en modo Dios o en modo azul, no se puede drenar sangre
        _lastDrainTime = Time.time;
        amount *= DrainMultiplier;   // "Piel gruesa"
        ChangeSyringe(amount);                // positivo: llena la jeringuilla, por lo que se pierde energía
    }

    /// <summary>
    /// Pausa o reanuda la lógica pasiva de la jeringuilla (el llenado y el gasto de la sangre azul). La usan el
    /// Rebobinado y el fin de nivel.
    /// </summary>
    public void SetPaused(bool paused) => _paused = paused;

    /// <summary>
    /// Fija directamente la sangre de la jeringuilla. Se usa al final del nivel tutorial, que vacía las dos jeringuillas
    /// con una animación. 'blue' indica si sigue en modo azul (con value &gt; 0).
    /// </summary>
    public void SetSyringeDirect(float value, bool blue)
    {
        _syringe = Mathf.Clamp(value, 0f, _maxSyringe);
        _isBlueMode = blue && _syringe > 0f;
        if (!_isBlueMode) _overloadUntil = double.NegativeInfinity;
        OnSyringeChanged?.Invoke(SyringePercent);
        OnEnergyChanged?.Invoke(EnergyPercent);
    }

    // -=+=- Funciones internas -=+=-

    private void ChangeSyringe(float delta)
    {
        // Mientras el tutorial frena o está parado, la jeringuilla no se llena (ni por fallos ni por costes), así que no se puede morir.
        if (delta > 0f && !_isBlueMode && TutorialGate.Busy) return;
        float prev = _syringe;
        _syringe = Mathf.Clamp(_syringe + delta, 0f, _maxSyringe);

        if (!Mathf.Approximately(prev, _syringe))
        {
            OnSyringeChanged?.Invoke(SyringePercent);
            OnEnergyChanged?.Invoke(EnergyPercent);

            // Si la jeringuilla azul se llena del todo, empieza la Sobrecarga.
            if (_isBlueMode && _syringe >= _maxSyringe && prev < _maxSyringe) StartOverload();

            // OnSyringeFull significa la muerte, y solo se lanza en modo rojo, ya que en modo azul llenar la jeringuilla no mata al jugador.
            if (!_isBlueMode && _syringe >= _maxSyringe && prev < _maxSyringe)
                OnSyringeFull?.Invoke();

            if (_syringe <= 0f && prev > 0f)
                OnEnergyFull?.Invoke();
        }
    }
}
