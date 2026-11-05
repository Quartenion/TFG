using UnityEngine;

/// <summary>
/// Mueve un objeto entre dos puntos (A y B) sincronizado con la música.
///
/// Modo oscilación (Oscillate = true).
///   El objeto va de A a B y vuelve a A de forma continua, al ritmo de la canción. beatsPerCycle es la duración del
///   ciclo completo en beats, y phaseOffsetBeats desfasa el ciclo, lo que sirve para escalonar varias plataformas.
///
/// Modo disparo (Oscillate = false).
///   El objeto espera quieto en A. Cuando el jugador está a triggerBeats de distancia, empieza a moverse hacia B,
///   tarda beatsPerCycle beats en llegar y se queda en B.
///
/// Configuración.
///   1. Se añade el componente al GameObject.
///   2. Se definen A y B con los controles de la vista de escena o con los botones del Inspector.
///   3. Se asigna BeatGridVisualizer, que se busca automáticamente si se deja vacío.
///   4. Si el objeto tiene un Rigidbody, se marca como Is Kinematic.
/// </summary>
[AddComponentMenu("Level/Beat Oscillator")]
public class BeatOscillator : MonoBehaviour, IRewindable
{
    // -=+=- Enums -=+=-

    public enum EasingMode
    {
        Linear,
        SmoothStep,   // suave al principio y al final (ideal para plataformas)
        EaseIn,       // lento al principio y rápido al final
        EaseOut,      // rápido al principio y lento al final
        Bounce,       // rebota al llegar al destino
    }

    // -=+=- Inspector -=+=-

    [Header("Referencias")]
    [Tooltip("BeatManager de la escena. Se localiza automáticamente si se deja vacío.")]
    [SerializeField] private BeatManager _beatManager;

    [Tooltip("BeatGridVisualizer de la escena. Proporciona walkSpeed y la dirección del nivel.\n" +
             "Se busca automáticamente en Start si se deja vacío.")]
    [SerializeField] private BeatGridVisualizer _grid;

    [Header("Puntos")]
    [Tooltip("Si está activo, pointA y pointB son OFFSETS respecto a la posición inicial del objeto.\n" +
             "Mueve el objeto en el editor y la oscilación se mueve con él.\n" +
             "Si está desactivo, son posiciones absolutas en world space (comportamiento original).")]
    [SerializeField] private bool _localSpace = false;

    [Tooltip("Posición de inicio. World space si Local Space = false; offset local si = true.\n" +
             "Usa el botón 'Capturar A' o el handle verde.")]
    [SerializeField] public Vector3 pointA;

    [Tooltip("Posición final. World space si Local Space = false; offset local si = true.\n" +
             "Usa el botón 'Capturar B' o el handle rojo.")]
    [SerializeField] public Vector3 pointB;

    [Header("Modo")]
    [Tooltip("TRUE  → oscila continuamente A→B→A al ritmo de la canción.\n" +
             "FALSE → se mueve de A a B una sola vez cuando el jugador se acerca y se queda en B.")]
    [SerializeField] private bool _oscillate = true;

    [Tooltip("Solo en modo disparo: NO se mueve por cercanía del jugador; espera a que algo llame\n" +
             "a Trigger() (p. ej. un GazeSwitch/interruptor). Útil para puertas activadas a mano.")]
    [SerializeField] private bool _waitForExternalTrigger = false;

    [Header("Timing")]
    [Tooltip("Oscilación: beats para un ciclo completo A→B→A.\n" +
             "Disparo:    beats que tarda el movimiento de A a B.")]
    [Min(0.25f)]
    [SerializeField] private float _beatsPerCycle = 2f;

    [Tooltip("Solo en modo oscilación.\n" +
             "Desplaza la fase inicial del ciclo en beats.\n" +
             "0 = empieza en A.    beatsPerCycle × 0.5 = empieza en B.")]
    [SerializeField] private float _phaseOffsetBeats = 0f;

    [Tooltip("Solo en modo disparo.\n" +
             "Beats de antelación respecto a la llegada del jugador en los que empieza el movimiento.")]
    [Min(1)]
    [SerializeField] private int _triggerBeats = 4;

    [Header("Movimiento")]
    [SerializeField] private EasingMode _easing = EasingMode.SmoothStep;

    [Tooltip("Usa Rigidbody.MovePosition en lugar de transform.position.\n" +
             "Mejor para colisiones físicas. Requiere Rigidbody en modo Kinematic.")]
    [SerializeField] private bool _useRigidbody = false;

    // -=+=- Estado interno -=+=-

    private Rigidbody _rb;
    private bool      _ready;          // true en cuanto ScheduledStartDSP > 0
    private Vector3   _originPos;      // posición inicial del objeto, para el modo local

    // Modo disparo
    private enum Phase { Idle, Moving, Done }
    private Phase  _phase      = Phase.Idle;
    private double _triggerDSP = double.MaxValue;
    private bool   _triggerRequested;   // llamada externa a Trigger() pendiente de aplicar

    // Modo controlado por la mirada. SetOpen(t) fija directamente la posición (de 0 a 1 equivale de A a B).
    private bool  _driven;
    private float _drivenT;

    // -=+=- Ciclo de vida -=+=-

    private void Awake()
    {
        _rb        = GetComponent<Rigidbody>();
        _originPos = transform.position;

        if (_beatManager == null)
            _beatManager = BeatManager.Instance;

        // Posición inicial mientras la música no ha empezado
        Move(WorldA);
    }

    private void Start()
    {
        // Segundo intento de encontrar las referencias, por si Awake se ejecutó antes que el Singleton.
        if (_beatManager == null)
            _beatManager = BeatManager.Instance;

        if (_grid == null)
            _grid = FindAnyObjectByType<BeatGridVisualizer>();
    }

    private void Update()
    {
        // Modo controlado por la mirada (SetOpen). La posición la fija directamente el progreso externo (de 0 a 1
        // equivale de A a B). Tiene prioridad y no necesita BeatManager.
        if (_driven)
        {
            Move(Vector3.Lerp(WorldA, WorldB, Ease(_drivenT)));
            return;
        }

        if (_beatManager == null) return;

        // Se espera a que la música esté programada.
        if (!_ready)
        {
            if (!_beatManager.HasClock) return;

            // En el modo disparo por cercanía, el momento del disparo se calcula una sola vez. Con el disparo externo,
            // _triggerDSP se deja en MaxValue, por lo que solo lo dispara Trigger().
            if (!_oscillate && !_waitForExternalTrigger)
                ComputeTriggerDSP();

            _ready = true;
        }

        if (_oscillate)
            UpdateOscillate();
        else
            UpdateOneShot();
    }

    // -=+=- Modo oscilación -=+=-

    private void UpdateOscillate()
    {
        double now     = BeatManager.DspNow;
        double elapsed = _beatManager.ClockBeats;
        double shifted = elapsed + _phaseOffsetBeats;

        // Posición normalizada dentro del ciclo, en [0, 1)
        double cycleT = (shifted / _beatsPerCycle) % 1.0;
        if (cycleT < 0.0) cycleT += 1.0;

        float t      = (float)cycleT;
        bool  toB    = t < 0.5f;
        // halfT va siempre de 0 a 1 en cada mitad, para que Ease se aplique en la misma dirección tanto de A a B como de
        // B a A. Así, Bounce rebota suavemente al llegar a B y otra vez al llegar a A.
        float halfT  = toB ? t * 2f : (t - 0.5f) * 2f;
        float eased  = Ease(halfT);

        Move(toB
            ? Vector3.Lerp(WorldA, WorldB, eased)
            : Vector3.Lerp(WorldB, WorldA, eased));
    }

    // -=+=- Modo disparo -=+=-

    private double _arrivalOffset = double.NaN;   // segundos de canción hasta que llega el jugador (se calcula una vez, en A)

    private void ComputeTriggerDSP()
    {
        if (double.IsNaN(_arrivalOffset)) _arrivalOffset = ComputeArrivalOffset();
        _triggerDSP = _beatManager.DspAtSongTime(_arrivalOffset)
                      - (double)(_triggerBeats * _beatManager.BeatDuration);
    }

    /// <summary>Rebobinado. Vuelve a calcular el disparo con el reloj actual, y Update deja el objeto en A, a medio camino o en B.</summary>
    public void OnTimeRewind()
    {
        if (_driven || _oscillate || _waitForExternalTrigger || !_ready || _beatManager == null) return;
        ComputeTriggerDSP();
        _phase = Phase.Idle;
        _triggerRequested = false;
    }

    private double ComputeArrivalOffset() => BeatGridVisualizer.ArrivalOffset(_grid, transform.position, this);

    private void UpdateOneShot()
    {
        // Done es el estado final, así que no hay nada más que hacer.
        if (_phase == Phase.Done) return;

        // Si hay una llamada externa a Trigger() pendiente, empieza a moverse ahora.
        if (_triggerRequested && _phase == Phase.Idle)
        {
            _triggerDSP = BeatManager.DspNow;
            _triggerRequested = false;
        }

        double now = BeatManager.DspNow;

        // -=+=- En reposo: espera en A hasta el momento del disparo -=+=-
        if (_phase == Phase.Idle)
        {
            if (now < _triggerDSP) { Move(WorldA); return; }
            _phase = Phase.Moving;
        }

        // -=+=- En movimiento: de A a B -=+=-
        double duration = (double)_beatsPerCycle * _beatManager.BeatDuration;
        // El final se decide con t sin suavizar, para que Bounce pueda rebotar un momento por encima de B sin pasar a
        // Done antes de tiempo.
        float t     = Mathf.Clamp01((float)((now - _triggerDSP) / duration));
        float eased = Ease(t);

        Move(Vector3.Lerp(WorldA, WorldB, eased));

        if (t >= 1f)
        {
            Move(WorldB);
            _phase = Phase.Done;
        }
    }

    // -=+=- Funciones auxiliares -=+=-

    private void Move(Vector3 pos)
    {
        if (_useRigidbody && _rb != null && _rb.isKinematic)
            _rb.MovePosition(pos);
        else
            transform.position = pos;
    }

    private float Ease(float t)
    {
        switch (_easing)
        {
            case EasingMode.Linear:     return t;
            case EasingMode.SmoothStep: return t * t * (3f - 2f * t);
            case EasingMode.EaseIn:     return t * t;
            case EasingMode.EaseOut:    return 1f - (1f - t) * (1f - t);
            case EasingMode.Bounce:     return Easing.BounceOut(t);
            default:                    return t;
        }
    }

    // -=+=- Funciones auxiliares de los puntos -=+=-

    private Vector3 WorldA => _localSpace ? _originPos + pointA : pointA;
    private Vector3 WorldB => _localSpace ? _originPos + pointB : pointB;

    // -=+=- API pública -=+=-

    public void CaptureCurrentAsA()
    {
        pointA = _localSpace ? transform.position - _originPos : transform.position;
    }

    public void CaptureCurrentAsB()
    {
        pointB = _localSpace ? transform.position - _originPos : transform.position;
    }

    /// <summary>
    /// Lanza el movimiento de A a B en este momento (modo disparo). Está pensado para activarlo desde fuera, por ejemplo
    /// conectando el OnActivated de un GazeSwitch a este método para abrir una puerta. Solo tiene efecto si el objeto
    /// sigue en A, y no hace nada en el modo oscilación.
    /// </summary>
    public void Trigger()
    {
        if (_oscillate) return;
        _triggerRequested = true;
    }

    /// <summary>Reinicia el disparo, por lo que el objeto vuelve a A y espera otra llamada a Trigger().</summary>
    public void ResetTrigger()
    {
        if (_oscillate) return;
        _phase            = Phase.Idle;
        _triggerDSP       = double.MaxValue;
        _triggerRequested = false;
        _driven           = false;
        Move(WorldA);
    }

    /// <summary>
    /// Modo controlado por la mirada. Fija la posición directamente con un progreso de 0 a 1 (de A a B), y se conecta al
    /// On Progress de un GazeSwitch. El objeto se mueve mientras el progreso sube y, como el GazeSwitch lo descarga al
    /// dejar de mirar, vuelve solo a A. Si llega a 1 con el interruptor en One Shot, el interruptor congela el progreso
    /// en 1 y el objeto se queda en B.
    /// </summary>
    public void SetOpen(float t01)
    {
        _driven  = true;
        _drivenT = Mathf.Clamp01(t01);
    }

    // -=+=- Gizmos -=+=-

#if UNITY_EDITOR
    private void OnDrawGizmos()         => DrawGizmos(false);
    private void OnDrawGizmosSelected() => DrawGizmos(true);

    private void DrawGizmos(bool selected)
    {
        float alpha  = selected ? 1f   : 0.4f;
        float radius = selected ? 0.18f : 0.09f;

        // En el modo edición, _originPos no está inicializado, así que se usa transform.position.
        Vector3 origin = Application.isPlaying ? _originPos : transform.position;
        Vector3 wA = _localSpace ? origin + pointA : pointA;
        Vector3 wB = _localSpace ? origin + pointB : pointB;

        // Línea de A a B
        UnityEditor.Handles.color = new Color(1f, 1f, 1f, selected ? 0.6f : 0.15f);
        UnityEditor.Handles.DrawDottedLine(wA, wB, 5f);

        var styleA = new GUIStyle { normal = { textColor = new Color(0.2f, 0.95f, 0.35f, alpha) }, fontStyle = FontStyle.Bold, fontSize = 12 };
        var styleB = new GUIStyle { normal = { textColor = new Color(0.95f, 0.25f, 0.25f, alpha) }, fontStyle = FontStyle.Bold, fontSize = 12 };

        // Punto A
        Gizmos.color = new Color(0.2f, 0.95f, 0.35f, alpha);
        Gizmos.DrawSphere(wA, radius);
        UnityEditor.Handles.Label(wA + Vector3.up * 0.35f, _localSpace ? "A (local)" : "A", styleA);

        // Punto B
        Gizmos.color = new Color(0.95f, 0.25f, 0.25f, alpha);
        Gizmos.DrawSphere(wB, radius);
        UnityEditor.Handles.Label(wB + Vector3.up * 0.35f, _localSpace ? "B (local)" : "B", styleB);

        // Indicador del disparo (solo en el modo disparo y con el objeto seleccionado)
        if (selected && !_oscillate && _grid != null)
        {
            float   bpm         = BeatManager.BpmOf(_beatManager);
            float   triggerDist = _triggerBeats * _grid.walkSpeed * (60f / bpm);
            Vector3 triggerPos  = transform.position - _grid.Forward * triggerDist;
            triggerPos.y        = wA.y;

            UnityEditor.Handles.color = new Color(1f, 0.8f, 0f, 0.2f);
            UnityEditor.Handles.DrawSolidDisc(triggerPos, Vector3.up, 0.4f);
            UnityEditor.Handles.color = new Color(1f, 0.8f, 0f, 0.7f);
            UnityEditor.Handles.DrawDottedLine(triggerPos, transform.position, 4f);

            var styleTrigger = new GUIStyle { normal = { textColor = new Color(1f, 0.8f, 0f) }, fontSize = 10 };
            UnityEditor.Handles.Label(triggerPos + Vector3.up * 0.3f, $"▶ trigger ({_triggerBeats} beats)", styleTrigger);
        }

        // Posición actual en el modo Play
        if (selected && Application.isPlaying && _ready)
        {
            Gizmos.color = new Color(1f, 0.9f, 0f, 0.9f);
            Gizmos.DrawSphere(transform.position, radius * 0.65f);

            if (!_oscillate)
            {
                string label;
                switch (_phase)
                {
                    case Phase.Idle:   label = "⏸ Idle";       break;
                    case Phase.Moving: label = "▶ Moviendo";   break;
                    case Phase.Done:   label = "✔ Done";       break;
                    default:           label = "";              break;
                }
                var stylePhase = new GUIStyle { normal = { textColor = Color.yellow }, fontStyle = FontStyle.Bold, fontSize = 10 };
                UnityEditor.Handles.Label(transform.position + Vector3.up * 0.55f, label, stylePhase);
            }
        }
    }
#endif
}
