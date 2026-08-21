using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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
    [SerializeField] private float _startingSyringe = 30f;

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

    [Header("Anti-spam (ventana deslizante)")]
    [Tooltip("Numero de inputs (hits + misses) que se tienen en cuenta para calcular la tasa de fallos. " +
             "Ventana mas grande = reaccion mas lenta al spam pero mas estable.")]
    [SerializeField] private int   _antispamWindow      = 10;
    [Tooltip("Porcentaje de fallos en la ventana a partir del cual se activa el multiplicador. " +
             "0.5 = se activa cuando hay mas fallos que aciertos (igual que tu idea original).")]
    [Range(0f, 1f)]
    [SerializeField] private float _antispamThreshold   = 0.5f;
    [Tooltip("Multiplicador maximo de penalizacion cuando TODOS los inputs de la ventana son fallos. " +
             "3 = un fallo en spam puro cuesta 3x mas sangre que un fallo normal.")]
    [SerializeField] private float _antispamMaxMult     = 3f;

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

    // Anti-spam — ventana deslizante de los ultimos N inputs
    private readonly Queue<bool> _inputWindow = new Queue<bool>(); // true = hit, false = miss
    private int _windowMisses;  // cuantos false hay en _inputWindow (evita recorrer la cola)

    public float SyringeBlood => _syringe;
    public float MaxSyringe => _maxSyringe;
    public float MaxEnergy => _maxSyringe;

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

    /// <summary>
    /// Tasa de fallos en la ventana deslizante [0..1].
    /// 0 = todos aciertos, 1 = todos fallos. Util para mostrar feedback visual.
    /// </summary>
    public float SpamMissRate => _inputWindow.Count > 0
        ? (float)_windowMisses / _inputWindow.Count
        : 0f;

    /// <summary>
    /// Multiplicador de penalizacion anti-spam actual [1..maxMult].
    /// 1 = sin spam, maxMult = spam puro. Util para UI de warning.
    /// </summary>
    public float SpamMultiplier => GetMissMultiplier();
    /// <summary>
    /// En modo azul siempre true (energia base = 100%).
    /// En modo rojo, comprueba si hay hueco suficiente.
    /// </summary>
    public bool HasEnergy(float amount) => _isBlueMode || Energy >= amount;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _syringe = Mathf.Clamp(_startingSyringe, 0f, _maxSyringe);
    }

    private void Update()
    {
        // God Mode: congelar toda la lógica de energía (sin llenado pasivo, sin muerte por jeringuilla).
        if (godMode) return;

        // No aplicar cambios pasivos antes de que empiece el juego.
        // Cuando BeatManager no está reproduciendo (juego no iniciado), congelamos todo.
        if (BeatManager.Instance != null && !BeatManager.Instance.IsPlaying) return;

        if (_isBlueMode)
        {
            if (_blueDecayPerSecond > 0f && _syringe > 0f)
                ChangeSyringe(-_blueDecayPerSecond * Time.deltaTime);

            if (_syringe <= 0f)
            {
                _isBlueMode = false;
                OnBlueDepleted?.Invoke();
            }
        }
        else
        {
            if (_passiveFillPerSecond > 0f && _syringe < _maxSyringe)
                ChangeSyringe(_passiveFillPerSecond * Time.deltaTime);
        }
    }

    // =========================================================================
    // API publica
    // =========================================================================

    /// <summary>
    /// Acierto en modo rojo sin timing explicito. Equivale a Good. Compatibilidad.
    /// </summary>
    public float HitRed()
    {
        return HitRed(TimingResult.Good);
    }

    /// <summary>
    /// Acierto en modo rojo con resultado de timing.
    /// Perfect = 1.5x de la cantidad base. Good = 1.0x.
    /// Si el overflow supera la sangre disponible, entra en modo azul automaticamente.
    /// </summary>
    public float HitRed(TimingResult timing)
    {
        if (_isBlueMode) return 0f;

        RecordInput(isHit: true);   // registrar acierto en la ventana anti-spam

        float timingMultiplier;
        if (timing == TimingResult.Perfect)
            timingMultiplier = 1.5f;
        else
            timingMultiplier = 1.0f;

        float subtract = _hitInjectAmount * timingMultiplier;
        float redAvailable = _syringe;

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
    /// La cantidad se multiplica por el factor anti-spam si el jugador
    /// lleva mas fallos que aciertos en los ultimos _antispamWindow inputs.
    /// </summary>
    public float MissRed()
    {
        if (_isBlueMode) return 0f;
        RecordInput(isHit: false);  // registrar fallo en la ventana anti-spam
        float multiplier = GetMissMultiplier();
        float before = _syringe;
        ChangeSyringe(_missAbsorbAmount * multiplier);
        float absorbed = _syringe - before;
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
        ChangeSyringe(-_syringe);
        OnBlueInjection?.Invoke(boost);
        return boost;
    }

    /// <summary>Activa el modo bonus manualmente.</summary>
    public void EnterBlueMode()
    {
        if (_isBlueMode) return;
        _isBlueMode = true;
        ChangeSyringe(-_syringe);
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
        if (godMode)      return true;  // God Mode: acción siempre permitida, sin coste

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

        if (Energy < amount) return false;
        ChangeSyringe(amount);
        return true;
    }

    /// <summary>Restaura energia gratis. Resta de la jeringuilla.</summary>
    public void Restore(float amount)
    {
        if (amount > 0f) ChangeSyringe(-amount);
    }

    // =========================================================================
    // Anti-spam internals
    // =========================================================================

    /// <summary>
    /// Registra un input en la ventana deslizante.
    /// Si la ventana esta llena, descarta el input mas antiguo.
    /// </summary>
    private void RecordInput(bool isHit)
    {
        // Expulsar el input mas antiguo si la ventana esta llena
        if (_inputWindow.Count >= _antispamWindow)
        {
            bool removed = _inputWindow.Dequeue();
            if (!removed) _windowMisses--;
        }

        _inputWindow.Enqueue(isHit);
        if (!isHit) _windowMisses++;
    }

    /// <summary>
    /// Devuelve el multiplicador de penalizacion [1 .. _antispamMaxMult].
    /// Escala linealmente entre el threshold y el 100% de fallos.
    /// </summary>
    private float GetMissMultiplier()
    {
        if (_inputWindow.Count == 0) return 1f;
        float missRate = (float)_windowMisses / _inputWindow.Count;
        float t = Mathf.InverseLerp(_antispamThreshold, 1f, missRate);
        return Mathf.Lerp(1f, _antispamMaxMult, t);
    }

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
