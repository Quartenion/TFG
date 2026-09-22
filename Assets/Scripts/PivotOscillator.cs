using UnityEngine;

/// <summary>
/// Rota este GameObject alrededor de su propio eje, sincronizado con la música.
/// Los hijos orbitan automáticamente alrededor del pivote (este objeto).
///
/// SETUP (3 pasos):
///   1. Crea un GameObject vacío en el CENTRO de rotación → llámalo "Pivot".
///   2. Añade este script al Pivot.
///   3. Haz el obstáculo HIJO del Pivot y desplázalo la distancia que quieras (el brazo).
///
/// Ejemplo para una sierra lateral:
///   PivotCenter (PivotOscillator, Rotation Axis = Z)
///     └── Cube (posición X = 3 → brazo de 3 metros)
///
/// MODO OSCILACIÓN (Oscillate = true):
///   El pivote oscila ±(oscillationAngle/2) desde su rotación inicial.
///   Los hijos barren ese arco.
///
/// MODO ROTACIÓN CONTINUA (Oscillate = false):
///   El pivote gira 360° cada beatsPerCycle beats.
/// </summary>
[AddComponentMenu("Level/Pivot Oscillator")]
public class PivotOscillator : MonoBehaviour
{
    // ── Enums ──────────────────────────────────────────────────────────────────

    public enum EasingMode { Linear, SmoothStep, EaseIn, EaseOut, EaseInOut, Bounce }

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Referencias")]
    [Tooltip("BeatManager de la escena. Se localiza automáticamente si se deja vacío.")]
    [SerializeField] private BeatManager _beatManager;

    [Header("Eje de rotación")]
    [Tooltip("Eje LOCAL alrededor del cual rota este pivote.\n" +
             "Vector3.forward (Z) → péndulo en XY  (sierra lateral vista de frente).\n" +
             "Vector3.up     (Y) → aspas en XZ    (molino visto desde arriba).\n" +
             "Vector3.right  (X) → péndulo en YZ.")]
    [SerializeField] private Vector3 _rotationAxis = Vector3.forward;

    [Header("Modo")]
    [Tooltip("TRUE  → oscila de lado a lado continuamente (péndulo).\n" +
             "FALSE → gira hasta el ángulo objetivo UNA sola vez cuando el jugador\n" +
             "        está a triggerBeats de distancia, y se queda ahí.")]
    [SerializeField] private bool _oscillate = true;

    [Tooltip("Solo en modo disparo: NO gira por cercanía del jugador; espera a que algo llame a\n" +
             "Trigger() (p. ej. un GazeSwitch). Úsalo para puertas que se abren a mano.")]
    [SerializeField] private bool _waitForExternalTrigger = false;

    [Tooltip("Ángulo total de la oscilación (modo oscilación): repartido simétricamente ±mitad.\n" +
             "Ángulo objetivo (modo disparo): rota desde 0° hasta este valor y se queda.")]
    [SerializeField] private float _oscillationAngle = 120f;

    [Header("Timing")]
    [Tooltip("Beats por ciclo completo (A→B→A en oscilación) o beats que dura el giro (disparo).")]
    [SerializeField] [Min(0.25f)] private float _beatsPerCycle = 2f;

    [Tooltip("Solo modo oscilación: desfase de fase en beats.")]
    [SerializeField] private float _phaseOffsetBeats = 0f;

    [Tooltip("Solo modo disparo: beats de antelación respecto a la llegada del jugador\n" +
             "en los que se activa el giro.")]
    [SerializeField] [Min(1)] private int _triggerBeats = 4;

    [Header("Referencias (modo disparo)")]
    [Tooltip("BeatGridVisualizer para calcular la distancia del jugador.\n" +
             "Se busca automáticamente si se deja vacío.")]
    [SerializeField] private BeatGridVisualizer _grid;

    [Header("Posición inicial (solo modo oscilación)")]
    [Tooltip("La oscilación empieza en el ángulo máximo positivo (+half).")]
    [SerializeField] private bool _startFromMaxPos = false;

    [Tooltip("La oscilación empieza en el ángulo máximo negativo (-half).")]
    [SerializeField] private bool _startFromMaxNeg = false;

    [Header("Giro continuo")]
    [Tooltip("Si está activo, ignora oscillate y gira indefinidamente a velocidad constante.")]
    [SerializeField] private bool _spinConstantly = false;

    [Tooltip("Grados por segundo. Negativo invierte la dirección.")]
    [SerializeField] private float _spinSpeed = 90f;

    [Header("Movimiento")]
    [Tooltip("Curva del movimiento. Para un péndulo NATURAL usa EaseInOut: frena en AMBOS extremos y\n" +
             "acelera al pasar por el centro (como un péndulo real). SmoothStep da un efecto parecido.\n" +
             "EaseIn / EaseOut solo frenan en un extremo (asimétrico).")]
    [SerializeField] private EasingMode _easing = EasingMode.EaseInOut;

    // ── Estado interno ─────────────────────────────────────────────────────────

    private bool       _ready;
    private Quaternion _initialRotation;

    // Modo disparo (beat-synced)
    private enum Phase { Idle, Moving, Done }
    private Phase  _phase      = Phase.Idle;
    private double _triggerDSP = double.MaxValue;
    private bool   _triggerRequested;   // Trigger() externo pendiente de aplicar

    // Modo "conducido por la mirada": SetOpen(t) fija directamente el grado de apertura (0..1).
    private bool  _driven;
    private float _drivenT;

    // Modo apertura manual (tiempo real, sin beat manager)
    private bool  _manualOpen;
    private float _manualElapsed;
    private float _manualDuration;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_beatManager == null) _beatManager = BeatManager.Instance;
        _initialRotation = transform.rotation;
    }

    private void Start()
    {
        if (_beatManager == null) _beatManager = BeatManager.Instance;
        if (_grid == null)        _grid = FindAnyObjectByType<BeatGridVisualizer>();
    }

    // ── API pública ────────────────────────────────────────────────────────────

    /// <summary>
    /// Abre este pivote inmediatamente usando tiempo real (sin beat manager).
    /// Útil para puertas que se abren al interactuar antes de que empiece la música.
    /// </summary>
    /// <param name="durationSeconds">Duración de la apertura en segundos.</param>
    public void TriggerNow(float durationSeconds = 1.2f)
    {
        _manualOpen     = true;
        _manualElapsed  = 0f;
        _manualDuration = Mathf.Max(0.01f, durationSeconds);
    }

    /// <summary>
    /// Dispara el giro (modo disparo) AHORA, sincronizado con el beat (dura beatsPerCycle beats).
    /// Pensado para activarlo desde fuera — p. ej. un GazeSwitch conecta su OnActivated aquí para
    /// abrir la puerta. Sin argumentos, así se conecta limpio en el evento del Inspector.
    /// Solo tiene efecto si aún no ha girado; no hace nada en modo oscilación.
    /// </summary>
    public void Trigger()
    {
        if (_oscillate) return;
        _triggerRequested = true;
    }

    /// <summary>Reinicia el disparo: vuelve a 0° y queda esperando otro Trigger().</summary>
    public void ResetTrigger()
    {
        if (_oscillate) return;
        _phase             = Phase.Idle;
        _triggerDSP        = double.MaxValue;
        _triggerRequested  = false;
        _manualOpen        = false;
        _driven            = false;
        transform.rotation = _initialRotation;
    }

    /// <summary>
    /// Modo CONDUCIDO POR LA MIRADA: fija el grado de apertura directamente con un progreso 0..1
    /// (conecta aquí GazeSwitch → On Progress). El objeto gira proporcionalmente mientras el
    /// progreso sube; como el GazeSwitch descarga el progreso al dejar de mirar, el objeto vuelve
    /// solo a su sitio. Si el progreso llega a 1 con el switch en One Shot, el switch congela el
    /// progreso en 1 y el objeto se queda abierto.
    /// </summary>
    public void SetOpen(float t01)
    {
        _driven  = true;
        _drivenT = Mathf.Clamp01(t01);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        // Modo CONDUCIDO POR LA MIRADA (SetOpen): el ángulo lo fija directamente el progreso
        // externo (0..1). Tiene prioridad sobre todo lo demás.
        if (_driven)
        {
            transform.rotation = _initialRotation
                * Quaternion.AngleAxis(_oscillationAngle * Ease(_drivenT), _rotationAxis.normalized);
            return;
        }

        // Apertura manual (tiempo real) — tiene prioridad sobre el resto
        if (_manualOpen)
        {
            _manualElapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(_manualElapsed / _manualDuration);
            float eased = Ease(t);
            transform.rotation = _initialRotation
                * Quaternion.AngleAxis(_oscillationAngle * eased, _rotationAxis.normalized);
            return;
        }

        if (_spinConstantly)
        {
            transform.Rotate(_rotationAxis.normalized, _spinSpeed * Time.deltaTime, Space.Self);
            return;
        }

        if (_beatManager == null) return;

        if (!_ready)
        {
            if (_beatManager.ScheduledStartDSP <= 0.0) return;

            // En modo trigger externo se deja _triggerDSP = MaxValue: solo Trigger() lo dispara.
            if (!_oscillate && !_waitForExternalTrigger)
                ComputeTriggerDSP();

            _ready = true;
        }

        float angleDeg = _oscillate ? UpdateOscillate() : UpdateOneShot();

        transform.rotation = _initialRotation
            * Quaternion.AngleAxis(angleDeg, _rotationAxis.normalized);
    }

    // ── Modos de movimiento ────────────────────────────────────────────────────

    private float UpdateOscillate()
    {
        double now     = AudioSettings.dspTime;
        double elapsed = (now - _beatManager.ScheduledStartDSP) / _beatManager.BeatDuration;
        double shifted = elapsed + _phaseOffsetBeats;

        double cycleT = (shifted / _beatsPerCycle) % 1.0;
        if (cycleT < 0.0) cycleT += 1.0;

        // Posición inicial: max neg = 0.0, max pos = 0.5 (a mitad de ciclo)
        if      (_startFromMaxPos) cycleT = (cycleT + 0.5) % 1.0;
        else if (_startFromMaxNeg) { /* 0.0 → ya es el default */ }

        float t     = (float)cycleT;
        bool  toPos = t < 0.5f;
        float halfT = toPos ? t * 2f : (t - 0.5f) * 2f;
        float eased = Ease(halfT);
        float half  = _oscillationAngle * 0.5f;

        return toPos
            ? Mathf.Lerp(-half,  half, eased)
            : Mathf.Lerp( half, -half, eased);
    }

    private void ComputeTriggerDSP()
    {
        float dist, speed;

        if (_grid != null)
        {
            dist  = Vector3.Dot(transform.position - _grid.Origin, _grid.Forward);
            speed = _grid.walkSpeed;
        }
        else
        {
            dist  = transform.position.z;
            speed = 6f;
            Debug.LogWarning($"[PivotOscillator] '{name}': BeatGridVisualizer no encontrado. " +
                             "Usando Z y walkSpeed=6.", this);
        }

        double arrivalDSP = _beatManager.ScheduledStartDSP + (double)(dist / speed);
        _triggerDSP       = arrivalDSP - (double)(_triggerBeats * _beatManager.BeatDuration);
    }

    private float UpdateOneShot()
    {
        if (_phase == Phase.Done) return _oscillationAngle;

        // Aplicar un Trigger() externo pendiente: empieza a girar ahora.
        if (_triggerRequested && _phase == Phase.Idle)
        {
            _triggerDSP = AudioSettings.dspTime;
            _triggerRequested = false;
        }

        double now = AudioSettings.dspTime;

        if (_phase == Phase.Idle)
        {
            if (now < _triggerDSP) return 0f;
            _phase = Phase.Moving;
        }

        // Moving: 0° → _oscillationAngle en _beatsPerCycle beats
        double duration = (double)_beatsPerCycle * _beatManager.BeatDuration;
        float  t        = Mathf.Clamp01((float)((now - _triggerDSP) / duration));
        float  eased    = Ease(t);

        if (t >= 1f) _phase = Phase.Done;

        return Mathf.Lerp(0f, _oscillationAngle, eased);
    }

    // ── Easing ─────────────────────────────────────────────────────────────────

    private float Ease(float t)
    {
        switch (_easing)
        {
            case EasingMode.Linear:     return t;
            case EasingMode.SmoothStep: return t * t * (3f - 2f * t);
            case EasingMode.EaseIn:     return t * t;
            case EasingMode.EaseOut:    return 1f - (1f - t) * (1f - t);
            // Sinusoidal: frena en ambos extremos y va rápido por el centro → péndulo natural.
            case EasingMode.EaseInOut:  return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
            case EasingMode.Bounce:     return BounceOut(t);
            default:                    return t;
        }
    }

    private static float BounceOut(float t)
    {
        const float n = 7.5625f, d = 2.75f;
        if (t < 1f / d)   { return n * t * t; }
        if (t < 2f / d)   { t -= 1.500f / d; return n * t * t + 0.750f;    }
        if (t < 2.5f / d) { t -= 2.250f / d; return n * t * t + 0.937500f; }
                             t -= 2.625f / d; return n * t * t + 0.984375f;
    }

    // ── Gizmos ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 pivot = transform.position;
        Vector3 axis  = transform.TransformDirection(
            _rotationAxis.sqrMagnitude > 0.001f ? _rotationAxis.normalized : Vector3.forward);

        // Dibujar un círculo/arco por cada hijo directo
        foreach (Transform child in transform)
        {
            Vector3 arm    = child.position - pivot;
            float   radius = arm.magnitude;
            if (radius < 0.01f) continue;

            Vector3 armDir = arm / radius;

            if (_oscillate)
            {
                float   half     = _oscillationAngle * 0.5f;
                Vector3 edgeA    = Quaternion.AngleAxis(-half, axis) * armDir;
                Vector3 edgeB    = Quaternion.AngleAxis( half, axis) * armDir;

                UnityEditor.Handles.color = new Color(1f, 0.6f, 0.1f, 0.5f);
                UnityEditor.Handles.DrawWireArc(pivot, axis, edgeA, _oscillationAngle, radius);

                UnityEditor.Handles.color = new Color(1f, 0.6f, 0.1f, 0.35f);
                UnityEditor.Handles.DrawLine(pivot, pivot + edgeA * radius);
                UnityEditor.Handles.DrawLine(pivot, pivot + edgeB * radius);
            }
            else
            {
                UnityEditor.Handles.color = new Color(1f, 0.6f, 0.1f, 0.45f);
                UnityEditor.Handles.DrawWireDisc(pivot, axis, radius);
            }

            // Línea brazo
            UnityEditor.Handles.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            UnityEditor.Handles.DrawDottedLine(pivot, child.position, 5f);
        }

        // Pivote
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
        Gizmos.DrawSphere(pivot, 0.12f);

        // Eje
        UnityEditor.Handles.color = new Color(0.5f, 0.5f, 1f, 0.7f);
        UnityEditor.Handles.DrawLine(pivot - axis * 0.5f, pivot + axis * 0.5f);

        // Info label
        float bpm      = _beatManager != null ? _beatManager.Bpm : 120f;
        float cycleSec = _beatsPerCycle * (60f / bpm);
        string modeStr = _oscillate ? $"±{_oscillationAngle * 0.5f:F0}°" : "360°";
        var style = new GUIStyle { normal = { textColor = new Color(1f, 0.6f, 0.15f) }, fontSize = 10 };
        UnityEditor.Handles.Label(
            pivot + Vector3.up * 0.4f,
            $"{modeStr}  •  {_beatsPerCycle} beats ({cycleSec:F2}s)", style);
    }
#endif
}
