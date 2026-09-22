using UnityEngine;

/// <summary>
/// FIREBALL (tipo "podoboo" de lava), SINCRONIZADO AL BEAT.
///
/// Usa el mismo modelo de tiempo DSP que StairFallingObstacle / WaterSpike: se ancla a
/// <see cref="BeatManager.ScheduledStartDSP"/> y calcula cuándo LLEGA el jugador a su
/// posición con el <see cref="BeatGridVisualizer"/> (Origin / Forward / walkSpeed).
/// Por eso toda la trayectoria se mide en BEATS (no en metros ni segundos): despega,
/// llega al apex, cuelga y aterriza SIEMPRE sobre el beat, aunque baje el frame-rate.
///
/// SECUENCIA:
///   1. Reposo (escondido en la lava) hasta 'Activation Beats' beats antes de que el
///      jugador llegue a su posición.
///   2. SALTA hasta jumpHeight en 'Rise Beats' (desacelerando al llegar arriba).
///   3. Se QUEDA colgando 'Hang Beats' en el apex.
///   4. BAJA en 'Fall Beats' dándose la vuelta (gira mientras cae).
/// Si el jugador entra en killRadius en cualquier momento -> game over instantáneo.
///
/// SETUP:
///   1. Pon el componente en el objeto del fireball (con su malla/partículas de fuego).
///   2. Asigna BeatManager y BeatGridVisualizer (se auto-localizan si se dejan vacíos).
///   3. Coloca la bola en su posición de REPOSO (p. ej. metida en la lava, bajo el recorrido).
///   4. Ajusta Activation Beats para que esté en el aire cuando el jugador pase por debajo:
///      Activation Beats ≈ Rise Beats + Hang Beats  →  el jugador llega justo cuando empieza a caer.
///   Los gizmos muestran el apex del salto y el radio de muerte.
///
/// LETALIDAD: la muerte la gestiona este mismo script con killRadius (fiable para un objeto
///   en movimiento). No necesita estar en la layer "Obstacle" ni tener ObstacleInfo.
/// </summary>
public class FireballObstacle : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("BeatManager de la escena. Se localiza en BeatManager.Instance si se deja vacío.")]
    [SerializeField] private BeatManager _beatManager;

    [Tooltip("BeatGridVisualizer del nivel: aporta walkSpeed, Origin y Forward para saber cuándo\n" +
             "llega el jugador a la posición del fireball. Sin él se usa Z del mundo y walkSpeed = 6.")]
    [SerializeField] private BeatGridVisualizer _grid;

    [Header("Activación (en beats)")]
    [Tooltip("Beats ANTES de que el jugador llegue a la posición del fireball en los que despega.\n" +
             "≈ Rise Beats + Hang Beats → el jugador llega justo cuando la bola empieza a caer.")]
    [Min(0f)]
    [SerializeField] private float _activationBeats = 1.5f;

    [Tooltip("Si está DESACTIVADO, ignora al jugador y despega en este beat absoluto de la canción.")]
    [SerializeField] private bool _anchorToPlayer = true;

    [Tooltip("Solo si 'Anchor To Player' está desactivado: beat de la canción en el que despega.")]
    [SerializeField] private int _startBeat = 16;

    [Header("Salto (en beats)")]
    [Tooltip("Altura del salto sobre la posición de reposo (metros).")]
    [SerializeField] private float _jumpHeight = 6f;
    [Tooltip("Beats en subir hasta el apex.")]
    [Min(0.0625f)]
    [SerializeField] private float _riseBeats = 1f;
    [Tooltip("Beats COLGANDO en el aire en el apex.")]
    [Min(0f)]
    [SerializeField] private float _hangBeats = 0.5f;
    [Tooltip("Beats en bajar de vuelta.")]
    [Min(0.0625f)]
    [SerializeField] private float _fallBeats = 1f;

    [Header("Voltereta al bajar")]
    [Tooltip("Grados que gira mientras CAE (dándose la vuelta). 180 = boca abajo, 360 = giro completo.")]
    [SerializeField] private float _flipDegrees = 360f;
    [Tooltip("Eje LOCAL del giro (por defecto X = da la voltereta hacia delante/atrás).")]
    [SerializeField] private Vector3 _flipAxis = Vector3.right;

    [Header("Letal")]
    [Tooltip("Si el jugador entra en este radio, game over. ≈ radio de la bola + radio del jugador.")]
    [SerializeField] private float _killRadius = 1.2f;
    [Tooltip("GameOverManager (se busca solo si está vacío).")]
    [SerializeField] private GameOverManager _gameOverManager;

    [Header("Repetición (opcional)")]
    [Tooltip("Si está activo, vuelve a saltar en bucle (podoboo continuo). Si no, salta una vez.")]
    [SerializeField] private bool _loop = false;
    [Tooltip("Beats de reposo abajo entre saltos cuando Loop está activo.")]
    [Min(0f)]
    [SerializeField] private float _loopRestBeats = 2f;

    // ── Estado interno (DSP) ──────────────────────────────────────────────────
    private Vector3    _startPos;
    private Quaternion _startRot;
    private Transform  _player;
    private bool       _dead;
    private bool       _initialized;

    private double _launchDSP;      // instante en que despega
    private double _apexDSP;        // llega arriba
    private double _fallStartDSP;   // termina de colgar, empieza a caer
    private double _landDSP;        // vuelve al reposo
    private double _cycleSpanDSP;   // duración de un ciclo (para Loop)

    private void Awake()
    {
        _startPos = transform.position;
        _startRot = transform.rotation;
    }

    private void Start()
    {
        if (_beatManager == null) _beatManager = BeatManager.Instance;
        if (_grid == null)        _grid = FindAnyObjectByType<BeatGridVisualizer>();
        if (_gameOverManager == null) _gameOverManager = FindAnyObjectByType<GameOverManager>();

        var pm = FindAnyObjectByType<PlayerMovement>();
        if (pm != null) _player = pm.transform;
    }

    private void Update()
    {
        if (_beatManager == null) return;

        // Muerte al tocar (en cualquier fase, incluso en reposo: el fuego siempre quema).
        if (!_dead && _player != null &&
            (transform.position - _player.position).sqrMagnitude <= _killRadius * _killRadius)
        {
            _dead = true;
            if (_gameOverManager != null) _gameOverManager.TriggerGameOver();
            else Debug.LogError("[FireballObstacle] GameOverManager no está asignado.", this);
            return;
        }
        if (_dead) return;

        // Esperar a que la música esté programada para conocer ScheduledStartDSP.
        if (!_initialized)
        {
            if (_beatManager.ScheduledStartDSP <= 0.0) return;
            ComputeTiming();
            _initialized = true;
        }

        double now = AudioSettings.dspTime;

        // 1) Antes de despegar: reposo.
        if (now < _launchDSP)
        {
            transform.SetPositionAndRotation(_startPos, _startRot);
            return;
        }

        // 2) Subida (ease-out: rápido al salir, frena arriba).
        if (now < _apexDSP)
        {
            float u    = SpanT(now, _launchDSP, _apexDSP);
            float ease = Mathf.Sin(u * Mathf.PI * 0.5f);
            transform.SetPositionAndRotation(_startPos + Vector3.up * (_jumpHeight * ease), _startRot);
            return;
        }

        // 3) Colgando en el apex.
        if (now < _fallStartDSP)
        {
            transform.SetPositionAndRotation(_startPos + Vector3.up * _jumpHeight, _startRot);
            return;
        }

        // 4) Caída (ease-in: acelera al caer) + voltereta.
        if (now < _landDSP)
        {
            float u    = SpanT(now, _fallStartDSP, _landDSP);
            float ease = u * u;
            Vector3 axis = _flipAxis.sqrMagnitude > 1e-6f ? _flipAxis.normalized : Vector3.right;
            transform.SetPositionAndRotation(
                _startPos + Vector3.up * (_jumpHeight * (1f - ease)),
                _startRot * Quaternion.AngleAxis(_flipDegrees * u, axis));
            return;
        }

        // 5) Aterrizó: vuelve al reposo. Si hay loop, reprograma el siguiente salto.
        transform.SetPositionAndRotation(_startPos, _startRot);
        if (_loop)
        {
            _launchDSP    += _cycleSpanDSP;
            _apexDSP      += _cycleSpanDSP;
            _fallStartDSP += _cycleSpanDSP;
            _landDSP      += _cycleSpanDSP;
        }
    }

    // ── Timing (DSP) ──────────────────────────────────────────────────────────

    private void ComputeTiming()
    {
        double beatDur = _beatManager.BeatDuration;
        double rise = System.Math.Max(0.001, _riseBeats * beatDur);
        double hang = System.Math.Max(0.0,   _hangBeats * beatDur);
        double fall = System.Math.Max(0.001, _fallBeats * beatDur);

        double launchDSP;
        if (_anchorToPlayer)
        {
            float dist = _grid != null
                ? Vector3.Dot(_startPos - _grid.Origin, _grid.Forward)
                : _startPos.z;
            float walk = _grid != null ? _grid.walkSpeed : 6f;
            if (_grid == null)
                Debug.LogWarning($"[FireballObstacle] '{name}': BeatGridVisualizer no asignado. " +
                                 "Usando Z del mundo y walkSpeed = 6 m/s (el timing puede ser impreciso).", this);

            double arrivalDSP = _beatManager.ScheduledStartDSP + (double)(dist / Mathf.Max(0.01f, walk));
            launchDSP = arrivalDSP - _activationBeats * beatDur;
        }
        else
        {
            launchDSP = _beatManager.ScheduledStartDSP + _startBeat * beatDur;
        }

        _launchDSP    = launchDSP;
        _apexDSP      = _launchDSP + rise;
        _fallStartDSP = _apexDSP + hang;
        _landDSP      = _fallStartDSP + fall;
        _cycleSpanDSP = rise + hang + fall + System.Math.Max(0.0, _loopRestBeats * beatDur);
    }

    /// <summary>Progreso [0..1] de 'now' dentro del tramo [a, b].</summary>
    private static float SpanT(double now, double a, double b)
    {
        return b > a ? Mathf.Clamp01((float)((now - a) / (b - a))) : 1f;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Vector3 basePos = Application.isPlaying ? _startPos : transform.position;

        // Radio de muerte.
        Gizmos.color = new Color(1f, 0.2f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, _killRadius);

        // Apex del salto.
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.9f);
        Vector3 apex = basePos + Vector3.up * _jumpHeight;
        Gizmos.DrawLine(basePos, apex);
        Gizmos.DrawWireSphere(apex, 0.3f);

#if UNITY_EDITOR
        var style = new GUIStyle { normal = { textColor = new Color(1f, 0.6f, 0.1f) }, fontSize = 10, fontStyle = FontStyle.Bold };
        UnityEditor.Handles.Label(
            apex + Vector3.up * 0.4f,
            $"Fireball — salto {_riseBeats:0.##}+{_hangBeats:0.##}+{_fallBeats:0.##} beats\n" +
            $"despega {_activationBeats:0.##} beats antes de la llegada del jugador",
            style);
#endif
    }
}
