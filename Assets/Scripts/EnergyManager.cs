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
///  - Miss en modo rojo: anade sangre extra (penalizacion).
///  - Modo azul (bonus, tras N hits seguidos): la jeringuilla se vacia y los hits
///    AnADEN sangre azul. Riesgo: cuanto mas acumulas, mas cerca del limite. Recompensa:
///    al fallar, se inyecta toda la sangre azul de golpe -> energia restaurada + boost.
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
    [Tooltip("Sangre que se inyecta (sale de la jeringuilla) por cada acierto")]
    [SerializeField] private float _hitInjectAmount = 14f;
    [Tooltip("Sangre extra que entra al fallar")]
    [SerializeField] private float _missAbsorbAmount = 10f;

    [Header("Modo azul (bonus)")]
    [Tooltip("Sangre azul que se anade a la jeringuilla por cada acierto en bonus")]
    [SerializeField] private float _bluePerHit = 12f;
    [Tooltip("Multiplicador del boost: amount = bluePercent * multiplier (0..1)")]
    [SerializeField] private float _blueBoostMultiplier = 1.5f;

    [Header("Eventos")]
    public UnityEvent<float> OnSyringeChanged;       // 0..1
    public UnityEvent<float> OnEnergyChanged;        // 0..1
    public UnityEvent OnSyringeFull;                  // = muerte / fallo
    public UnityEvent OnEnergyFull;                   // jeringuilla vacia (energia al maximo)
    public UnityEvent<float> OnRedHit;                // cantidad inyectada (sale de S)
    public UnityEvent<float> OnRedMiss;               // cantidad absorbida
    public UnityEvent<float> OnBlueInjection;         // boost aplicado al fallar en bonus
    public UnityEvent OnBlueDepleted;                 // la sangre azul ha llegado a 0 sin fallo
    public UnityEvent<float> OnBlueModeEntered;       // entra en azul. Pasa overflow inicial

    private float _syringe;
    private bool _isBlueMode;

    public float SyringeBlood => _syringe;
    public float MaxSyringe => _maxSyringe;
    public float MaxEnergy => _maxSyringe;

    /// <summary>Energia disponible para gastar. En rojo: hueco libre. En azul: sangre azul.</summary>
    public float Energy => _isBlueMode ? _syringe : (_maxSyringe - _syringe);
    /// <summary>0..1 de energia disponible (consciente del modo).</summary>
    public float EnergyPercent => _maxSyringe > 0f ? Mathf.Clamp01(Energy / _maxSyringe) : 0f;
    /// <summary>0..1 de cuanta sangre hay en la jeringuilla (independiente del modo).</summary>
    public float SyringePercent => _maxSyringe > 0f ? Mathf.Clamp01(_syringe / _maxSyringe) : 0f;
    public bool IsBlueMode => _isBlueMode;
    public bool IsDead => !_isBlueMode && _syringe >= _maxSyringe;

    public bool HasEnergy(float amount) => Energy >= amount;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _syringe = Mathf.Clamp(_startingSyringe, 0f, _maxSyringe);
    }

    private void Update()
    {
        if (_isBlueMode)
        {
            // En azul, el buffer baja solo (decay)
            if (_blueDecayPerSecond > 0f && _syringe > 0f)
            {
                ChangeSyringe(-_blueDecayPerSecond * Time.deltaTime);
            }

            // Si la sangre azul se agota, perdemos el bonus y volvemos al modo rojo
            // (la jeringuilla empieza a llenarse de nuevo de sangre roja desde 0).
            if (_syringe <= 0f)
            {
                _isBlueMode = false;
                OnBlueDepleted?.Invoke();
            }
        }
        else
        {
            // En rojo, la jeringuilla se llena pasivamente
            if (_passiveFillPerSecond > 0f && _syringe < _maxSyringe)
            {
                ChangeSyringe(_passiveFillPerSecond * Time.deltaTime);
            }
        }
    }

    // === API publica ===

    /// <summary>
    /// Acierto en modo rojo: inyecta una cantidad fija (la jeringuilla baja).
    /// Si la cantidad a inyectar es mayor que la sangre roja disponible, el sobrante
    /// hace que la jeringuilla entre automaticamente en modo azul con ese overflow
    /// como sangre azul inicial.
    /// </summary>
    public float HitRed()
    {
        if (_isBlueMode) return 0f;

        float subtract = _hitInjectAmount;
        float redAvailable = _syringe;

        if (redAvailable >= subtract)
        {
            // Caso normal: hay sangre roja suficiente para absorber la inyeccion
            ChangeSyringe(-subtract);
            OnRedHit?.Invoke(subtract);
            return subtract;
        }

        // Overflow: la inyeccion vacia el resto de roja y el sobrante se convierte en azul
        float overflow = subtract - redAvailable;

        if (redAvailable > 0f)
        {
            ChangeSyringe(-redAvailable);
            OnRedHit?.Invoke(redAvailable);
        }

        // Cambio de modo a azul. La jeringuilla pasa a contener overflow como sangre azul.
        _isBlueMode = true;
        ChangeSyringe(overflow);
        OnBlueModeEntered?.Invoke(overflow);

        return redAvailable;
    }

    /// <summary>Fallo en modo rojo: anade sangre extra (penalizacion).</summary>
    public float MissRed()
    {
        if (_isBlueMode) return 0f;
        float before = _syringe;
        ChangeSyringe(_missAbsorbAmount);
        float absorbed = _syringe - before;
        if (absorbed > 0f) OnRedMiss?.Invoke(absorbed);
        return absorbed;
    }

    /// <summary>Acierto en modo bonus: anade sangre azul (acumula). Riesgo de overflow.</summary>
    public void HitBlue()
    {
        if (!_isBlueMode) return;
        ChangeSyringe(_bluePerHit);
    }

    /// <summary>
    /// Fallo en modo bonus: inyecta toda la sangre azul de golpe.
    /// Devuelve el "boost" calculado (que el sistema externo puede usar para velocidad,
    /// salto extra, escudo... lo que decidas).
    /// </summary>
    public float MissBlueAndInject()
    {
        if (!_isBlueMode) return 0f;
        float bluePercent = SyringePercent; // proporcion de azul acumulado
        float boost = bluePercent * _blueBoostMultiplier;
        ChangeSyringe(-_syringe); // vacia la jeringuilla
        OnBlueInjection?.Invoke(boost);
        return boost;
    }

    /// <summary>Activa el modo bonus. Vacia la jeringuilla para empezar a acumular azul.</summary>
    public void EnterBlueMode()
    {
        if (_isBlueMode) return;
        _isBlueMode = true;
        ChangeSyringe(-_syringe);
    }

    /// <summary>Sale del modo bonus (no inyecta, solo cambia el estado).</summary>
    public void ExitBlueMode()
    {
        _isBlueMode = false;
    }

    /// <summary>
    /// Intenta gastar energia (saltar, correr...).
    ///  - Modo rojo: anade sangre a la jeringuilla (consume energia del cuerpo).
    ///  - Modo azul: consume sangre azul (la barra baja). Si no hay azul suficiente, falla.
    /// Devuelve true si la accion pudo realizarse.
    /// </summary>
    public bool TrySpend(float amount)
    {
        if (amount <= 0f) return true;

        if (_isBlueMode)
        {
            if (_syringe < amount) return false;   // no hay azul suficiente
            ChangeSyringe(-amount);
            return true;
        }

        if (Energy < amount) return false;          // no hay energia roja suficiente
        ChangeSyringe(amount);
        return true;
    }

    /// <summary>Restaura energia gratis (anti-sangre). Resta de la jeringuilla.</summary>
    public void Restore(float amount)
    {
        if (amount > 0f) ChangeSyringe(-amount);
    }

    // === Internals ===

    private void ChangeSyringe(float delta)
    {
        float prev = _syringe;
        _syringe = Mathf.Clamp(_syringe + delta, 0f, _maxSyringe);
        if (!Mathf.Approximately(prev, _syringe))
        {
            OnSyringeChanged?.Invoke(SyringePercent);
            OnEnergyChanged?.Invoke(EnergyPercent);

            if (_syringe >= _maxSyringe && prev < _maxSyringe) OnSyringeFull?.Invoke();
            if (_syringe <= 0f && prev > 0f) OnEnergyFull?.Invoke();
        }
    }
}
