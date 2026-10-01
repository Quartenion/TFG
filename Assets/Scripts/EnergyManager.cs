using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Modelo de la jeringuilla:
///  - Hay UNA sola variable: la sangre que esta en la jeringuilla (S).
///  - Energia del jugador = MaxSangre - S. Cuanta menos sangre en la jeringuilla,
///    mas energia tiene el jugador en el cuerpo / corazon.
///  - Si la jeringuilla se llena por completo -> el corazon se queda sin sangre -> muerte.
///
/// Acciones:
///  - Pasivo: la sangre va llenando la jeringuilla (la energia se va consumiendo).
///  - Acciones (saltar, correr): tambien anaden sangre a la jeringuilla.
///  - Hit en modo rojo: quita sangre de la jeringuilla (devuelve sangre al corazon).
///    La cantidad escala con el timing: Perfect = 1.5x, Good = 1.0x.
///  - Miss en modo rojo: anade sangre extra (penalizacion).
///  - Modo azul (bonus): la jeringuilla se vacia y los hits anaden sangre azul.
///    Riesgo: cuanto mas acumulas, mas cerca del limite.
///    Recompensa: al fallar, se inyecta toda la sangre azul de golpe -> energia + boost.
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
    [Tooltip("Multiplicador del boost: boost = bluePercent * multiplier")]
    [SerializeField] private float _blueBoostMultiplier = 1.5f;

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
    public UnityEvent<float> OnBlueInjection;
    public UnityEvent OnBlueDepleted;
    public UnityEvent<float> OnBlueModeEntered;

    private float _syringe;
    private bool  _isBlueMode;
    private bool  _paused;   // congela el llenado pasivo (rebobinado, fin de nivel)
    private float _lastDrainTime = -999f;   // último instante en que un BloodSucker drenó sangre

    public float SyringeBlood => _syringe;
    public float MaxSyringe => _maxSyringe;

    /// <summary>
    /// Energia disponible para gastar.
    /// Rojo:  hueco libre en la jeringuilla (0..maxSyringe).
    /// Azul:  energia base (100%) + sangre azul bonus (0..maxSyringe) = maxSyringe..2*maxSyringe.
    /// </summary>
    public float Energy => _isBlueMode
        ? (_maxSyringe + _syringe)   // base 100% + bonus azul
        : (_maxSyringe - _syringe);  // hueco disponible (energia roja)

    /// <summary>
    /// 0..1 de energia disponible.
    /// En azul usa solo la porcion bonus (0..1) para no romper el display de la UI.
    /// </summary>
    public float EnergyPercent => _maxSyringe > 0f
        ? (_isBlueMode
            ? Mathf.Clamp01(_syringe / _maxSyringe)   // 0..1 del bonus azul
            : Mathf.Clamp01(Energy   / _maxSyringe))  // 0..1 de energia roja
        : 0f;

    /// <summary>0..1 de cuanta sangre hay en la jeringuilla.</summary>
    public float SyringePercent => _maxSyringe > 0f ? Mathf.Clamp01(_syringe / _maxSyringe) : 0f;

    public bool IsBlueMode => _isBlueMode;
    public bool IsDead => !godMode && !_isBlueMode && _syringe >= _maxSyringe;

    /// <summary>True si un BloodSucker ha drenado sangre en los últimos 0.15 s. Lo usa la barra para parpadear.</summary>
    public bool IsBeingDrained => Time.time - _lastDrainTime < 0.15f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _baseMaxSyringe = _maxSyringe;
        ApplyShopUpgrades();
        // Todos los niveles empiezan con la jeringuilla VACÍA: ni sangre roja ni azul (energía al 100 %).
        // (Ya no hay "sangre inicial" configurable en el Inspector.)
        _syringe    = 0f;
        _isBlueMode = false;

        // EXCEPCIÓN — «Buen desayuno» (tienda): empiezas el nivel ya en MODO AZUL, con la barra azul al
        // 20 / 40 / 60 % según el nivel de la mejora.
        int breakfast = PlayerProgress.GetLevel(ShopIds.StartBoost);
        if (breakfast > 0)
        {
            _isBlueMode = true;
            _syringe    = _maxSyringe * Mathf.Clamp01(0.2f * breakfast);
        }
        PlayerProgress.OnChanged += ApplyShopUpgrades;   // (si se compra en la tienda del freeroam, al momento)
    }

    private void OnDestroy()
    {
        PlayerProgress.OnChanged -= ApplyShopUpgrades;
        if (Instance == this) Instance = null;
    }

    // ── Mejoras de la tienda ────────────────────────────────────────────────────
    private float _baseMaxSyringe;

    private int _streak;   // aciertos seguidos (para «Racha»)

    /// <summary>Aciertos seguidos en modo rojo (se reinicia al fallar).</summary>
    public int Streak => _streak;

    /// <summary>«Sangre azul»: la sangre azul se gasta un 25 % / 50 % más despacio.</summary>
    public static float BlueDecayMultiplier
    {
        get { int l = PlayerProgress.GetLevel(ShopIds.BlueBoost); return l <= 0 ? 1f : l == 1 ? 0.75f : 0.5f; }
    }

    /// <summary>«Coagulante»: pérdida pasiva −15 % / −30 %.</summary>
    public static float PassiveMultiplier
    {
        get { int l = PlayerProgress.GetLevel(ShopIds.SlowBleed); return l <= 0 ? 1f : l == 1 ? 0.85f : 0.7f; }
    }

    /// <summary>«Piel gruesa»: los drenajes (BloodSuckers, golpes) quitan −25 % / −50 %.</summary>
    public static float DrainMultiplier
    {
        get { int l = PlayerProgress.GetLevel(ShopIds.DrainGuard); return l <= 0 ? 1f : l == 1 ? 0.75f : 0.5f; }
    }

    /// <summary>«Metabolismo eficiente»: las acciones cuestan un 5 % menos por nivel.</summary>
    public static float ActionCostMultiplier => 1f - 0.05f * PlayerProgress.GetLevel(ShopIds.Efficient);

    /// <summary>«Pulso fuerte»: cada acierto devuelve +15 % / +30 % / +50 % de sangre.</summary>
    public static float PulseMultiplier
    {
        get
        {
            int l = PlayerProgress.GetLevel(ShopIds.StrongPulse);
            return l <= 0 ? 1f : l == 1 ? 1.15f : l == 2 ? 1.3f : 1.5f;
        }
    }

    /// <summary>«Jeringuilla grande»: +10 % de capacidad por nivel. Conserva la sangre que ya hay.</summary>
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
    /// ¿Está el jugador MANTENIENDO el corazón (clic izquierdo sostenido, con la mejora «Corazón firme»)?
    /// Mientras tanto no se pierde sangre pasivamente (los fallos, golpes y BloodSuckers siguen contando).
    /// </summary>
    public bool IsHoldingHeart { get; private set; }

    private void UpdateSteadyHeart()
    {
        bool owned   = PlayerProgress.GetLevel(ShopIds.SteadyHeart) > 0;
        bool playing = BeatManager.Instance == null || BeatManager.Instance.IsPlaying;
        bool pressed = owned && playing && Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed);
        _steadyHoldTime = pressed ? _steadyHoldTime + Time.deltaTime : 0f;
        // En cuanto la mano se queda cerrada (ver HandAnimationController) ya cuenta; si no hay mano visible
        // (p. ej. en el kayak), vale con mantener el clic '_steadyHoldDelay' segundos.
        IsHoldingHeart  = pressed && (HandAnimationController.IsHandHeld || _steadyHoldTime >= _steadyHoldDelay);
    }

    private void Update()
    {
        UpdateSteadyHeart();

        // God Mode: congelar toda la lógica de energía (sin llenado pasivo, sin muerte por jeringuilla).
        if (godMode) return;

        // Pausada (rebobinado, fin de nivel): la jeringuilla no cambia sola.
        if (_paused) return;

        // No aplicar cambios pasivos antes de que empiece el juego.
        // Cuando BeatManager no está reproduciendo (juego no iniciado), congelamos todo.
        if (BeatManager.Instance != null && !BeatManager.Instance.IsPlaying) return;

        if (_isBlueMode)
        {
            if (_blueDecayPerSecond > 0f && _syringe > 0f)
                ChangeSyringe(-_blueDecayPerSecond * BlueDecayMultiplier * Time.deltaTime);   // («Sangre azul»)

            if (_syringe <= 0f)
            {
                _isBlueMode = false;
                OnBlueDepleted?.Invoke();
            }
        }
        else
        {
            // Corazón firme: mientras lo mantienes, no se pierde sangre pasivamente.
            if (_passiveFillPerSecond > 0f && _syringe < _maxSyringe && !IsHoldingHeart)
                ChangeSyringe(_passiveFillPerSecond * PassiveMultiplier * Time.deltaTime);    // («Coagulante»)
        }
    }

    // =========================================================================
    // API publica
    // =========================================================================

    /// <summary>
    /// Acierto en modo rojo con resultado de timing.
    /// Perfect = 1.5x de la cantidad base. Good = 1.0x.
    /// Si el overflow supera la sangre disponible, entra en modo azul automaticamente.
    /// </summary>
    public float HitRed(TimingResult timing)
    {
        if (_isBlueMode) return 0f;

        float timingMultiplier;
        if (timing == TimingResult.Perfect)
            timingMultiplier = PlayerProgress.GetLevel(ShopIds.Perfectionist) > 0 ? 2f : 1.5f;   // («Perfeccionista»)
        else
            timingMultiplier = 1.0f;

        float subtract = _hitInjectAmount * timingMultiplier * PulseMultiplier;   // (mejora «Pulso fuerte»)

        // «Segundo aliento»: con poca energía, los aciertos valen el doble.
        if (PlayerProgress.GetLevel(ShopIds.SecondWind) > 0 && EnergyPercent < 0.2f) subtract *= 2f;
        // «Racha»: desde 8 aciertos seguidos, +25 %.
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

        // Overflow: vacia el rojo y el sobrante se convierte en azul
        float overflow = subtract - redAvailable;

        if (redAvailable > 0f)
        {
            ChangeSyringe(-redAvailable);
            OnRedHit?.Invoke(redAvailable);
        }

        _isBlueMode = true;
        ChangeSyringe(overflow);
        OnBlueModeEntered?.Invoke(overflow);

        return redAvailable;
    }

    /// <summary>
    /// Fallo en modo rojo: anade sangre extra (penalizacion).
    /// <paramref name="penaltyScale"/> escala la penalizacion (1 = normal). Lo usan el kayak (fallar remando
    /// cuesta mas) y el anti-spam de RhythmInput (cada clic fallido de mas en el mismo beat cuesta el doble).
    /// </summary>
    public float MissRed(float penaltyScale = 1f)
    {
        if (_isBlueMode) return 0f;
        _streak = 0;                // (se rompe la racha)

        float before = _syringe;
        ChangeSyringe(_missAbsorbAmount * Mathf.Max(0f, penaltyScale) * StudyConfig.MissPenaltyMult);   // (modo prueba)
        float absorbed = _syringe - before;
        StudyTelemetry.MissPenalty(absorbed);
        if (absorbed > 0f) OnRedMiss?.Invoke(absorbed);
        return absorbed;
    }

    /// <summary>Acierto en modo bonus: acumula sangre azul.</summary>
    public void HitBlue()
    {
        if (!_isBlueMode) return;
        ChangeSyringe(_bluePerHit);
    }

    /// <summary>
    /// Fallo en modo bonus: inyecta toda la sangre azul de golpe y devuelve el boost.
    /// </summary>
    public float MissBlueAndInject()
    {
        if (!_isBlueMode) return 0f;
        float bluePercent = SyringePercent;
        float boost = bluePercent * _blueBoostMultiplier;
        StudyTelemetry.BlueLost(_syringe);
        ChangeSyringe(-_syringe);
        OnBlueInjection?.Invoke(boost);
        return boost;
    }

    /// <summary>Sale del modo bonus sin inyectar.</summary>
    public void ExitBlueMode()
    {
        _isBlueMode = false;
    }

    /// <summary>
    /// Intenta gastar energia (saltar, correr, dash...).
    /// Devuelve true si la accion pudo realizarse.
    /// </summary>
    public bool TrySpend(float amount)
    {
        if (amount <= 0f) return true;
        amount *= ActionCostMultiplier * StudyConfig.ActionCostMult;   // «Metabolismo eficiente» (tienda) · modo prueba
        if (godMode)      return true;  // God Mode: acción siempre permitida, sin coste
        if (_isBlueMode || Energy >= amount || CanOverspendWithExtraLife) StudyTelemetry.ActionCost(amount);

        if (_isBlueMode)
        {
            // Energia total en azul = 100% base + bonus azul.
            // Se consume del bonus azul primero. Si el bonus no cubre el coste,
            // el exceso se absorbe como sangre roja (sale del modo azul).
            // La accion SIEMPRE se permite.
            if (_syringe >= amount)
            {
                // El bonus cubre el coste entero
                ChangeSyringe(-amount);
            }
            else
            {
                // Gastar todo el bonus azul y convertir el exceso en sangre roja
                float excess = amount - _syringe;
                ChangeSyringe(-_syringe);   // drena todo el azul → syringe = 0
                _isBlueMode = false;
                ChangeSyringe(excess);      // absorbe el exceso como sangre roja
                OnBlueDepleted?.Invoke();
            }
            return true;
        }

        if (Energy < amount)
        {
            // Sin energía suficiente PERO con VIDAS EXTRA: la acción se permite igual. La jeringuilla se llena, el
            // corazón se queda seco y se gasta una vida extra (vuelves con el corazón a tope en el siguiente beat).
            if (!CanOverspendWithExtraLife) return false;
            ChangeSyringe(amount);
            return true;
        }
        ChangeSyringe(amount);
        return true;
    }

    /// <summary>¿Se pueden hacer acciones sin energía? Sí mientras quede alguna VIDA EXTRA (se gastará al secarse).</summary>
    public static bool CanOverspendWithExtraLife => PlayerProgress.GetCount(ShopIds.ExtraLife) > 0;

    /// <summary>Restaura energia gratis. Resta de la jeringuilla.</summary>
    public void Restore(float amount)
    {
        if (amount > 0f) ChangeSyringe(-amount);
    }

    /// <summary>
    /// Drena sangre del jugador de forma continua: la añade a la jeringuilla (= pierde energía).
    /// Lo llama el BloodSucker cada frame. En God Mode o modo azul no tiene efecto. Marca
    /// IsBeingDrained para que la barra parpadee. Si la jeringuilla se llena, dispara la muerte
    /// (OnSyringeFull) igual que llenarla por fallos.
    /// </summary>
    /// <summary>
    /// GOLPE (ataques del jefe): quita 'fraction' de TODA la sangre que se puede llegar a tener, contando la roja y la
    /// azul (el máximo total es 2 × la jeringuilla: 100 % rojo + 100 % azul). 0.25 = un cuarto.
    ///   • En azul: primero se gasta la sangre azul; si no llega, se sale del azul y el resto sale de la roja.
    ///   • En rojo: la jeringuilla se llena esa cantidad (si se llena del todo, mueres por sangre; vidas extra y
    ///     Adrenalina funcionan).
    /// God Mode lo ignora. «Piel gruesa» lo reduce.
    /// </summary>
    public void TakeHitFraction(float fraction)
    {
        if (fraction <= 0f || godMode) return;
        float amount = 2f * _maxSyringe * fraction * DrainMultiplier;
        _lastDrainTime = Time.time;
        if (_isBlueMode)
        {
            if (_syringe > amount) { ChangeSyringe(-amount); return; }   // solo baja el azul
            amount -= _syringe;                                          // se acaba el azul…
            _isBlueMode = false;
            ChangeSyringe(-_syringe);
            OnBlueDepleted?.Invoke();
        }
        ChangeSyringe(amount);                                           // …y el resto, sangre roja
    }

    public void DrainBlood(float amount)
    {
        if (amount <= 0f) return;
        if (godMode || _isBlueMode) return;   // en God Mode / bonus azul no te pueden chupar sangre
        _lastDrainTime = Time.time;
        amount *= DrainMultiplier;   // («Piel gruesa»)
        ChangeSyringe(amount);                // positivo = llena la jeringuilla = pierde energía
    }

    /// <summary>
    /// Pausa/reanuda la lógica pasiva de la jeringuilla (llenado y decay del azul). La usan el Rebobinado
    /// y el fin de nivel.
    /// </summary>
    public void SetPaused(bool paused) => _paused = paused;

    // =========================================================================
    // Internals
    // =========================================================================

    private void ChangeSyringe(float delta)
    {
        float prev = _syringe;
        _syringe = Mathf.Clamp(_syringe + delta, 0f, _maxSyringe);

        if (!Mathf.Approximately(prev, _syringe))
        {
            OnSyringeChanged?.Invoke(SyringePercent);
            OnEnergyChanged?.Invoke(EnergyPercent);

            // OnSyringeFull = muerte. Solo dispara en modo rojo.
            // En modo azul (bonus), llenar la jeringuilla NO mata al jugador.
            if (!_isBlueMode && _syringe >= _maxSyringe && prev < _maxSyringe)
                OnSyringeFull?.Invoke();

            if (_syringe <= 0f && prev > 0f)
                OnEnergyFull?.Invoke();
        }
    }
}
