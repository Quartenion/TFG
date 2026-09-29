using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hace que uno o varios "spikes" SUBAN desde su posición inicial (escondida bajo el agua)
/// hasta una altura ALEATORIA (por defecto entre 1 y 30), como pinchos saliendo del agua.
///
/// La subida está SINCRONIZADA con la llegada del jugador (modelo DSP, igual que FallingObstacle):
/// cada spike empieza a subir <see cref="_riseBeats"/> beats ANTES de que el jugador llegue a su
/// posición, y termina de subir justo cuando el jugador llega. Así el jugador ve el pincho
/// emerger poco a poco por delante como aviso.
///
/// DOS FORMAS DE USARLO:
///   • Uno por objeto.
///   • Un solo componente en el PADRE con "Affect Children": anima cada hijo con su propia altura.
///
/// COLOCACIÓN:
///   Coloca los spikes en su posición escondida (bajo el agua). Cada uno subirá esa posición
///   + una cantidad aleatoria [Min Rise, Max Rise] hacia arriba.
///
/// NOTA: este script solo MUEVE los spikes. Si además deben ser letales, añádeles un Collider
/// en la layer "Obstacle" y un ObstacleInfo, igual que el resto de obstáculos.
/// </summary>
[AddComponentMenu("Level/Water Spike")]
public class WaterSpike : MonoBehaviour, IRewindable
{
    public enum SpikeEasing
    {
        Linear,
        SmoothStep,   // suave al inicio y final
        EaseOut,      // rápido al salir, frena al llegar (recomendado)
        Overshoot,    // sale, se pasa un poco y se asienta (efecto "pop")
    }

    [Header("Objetivos")]
    [Tooltip("Si está activo, anima TODOS los hijos directos (cada uno con su altura aleatoria) en\n" +
             "lugar de este objeto. Pon el componente en un padre vacío que agrupe los spikes.")]
    [SerializeField] private bool _affectChildren = false;

    [Header("Altura aleatoria")]
    [Tooltip("Altura mínima (en metros) que sube un spike sobre su posición inicial.")]
    [SerializeField] private float _minRise = 1f;

    [Tooltip("Altura máxima (en metros) que sube un spike sobre su posición inicial.")]
    [SerializeField] private float _maxRise = 30f;

    [Header("Subida sincronizada")]
    [Tooltip("Beats POR DELANTE del jugador durante los que el spike sube. Empieza a subir estos\n" +
             "beats antes de que el jugador llegue a su posición y termina justo al llegar.")]
    [Min(0.25f)]
    [SerializeField] private float _riseBeats = 8f;

    [Tooltip("Beats de adelanto con los que el spike queda COMPLETAMENTE arriba antes de la llegada\n" +
             "del jugador. 0 = termina de subir justo cuando el jugador llega. Súbelo si quieres que\n" +
             "esté totalmente fuera un poco antes (más margen de reacción).")]
    [Min(0f)]
    [SerializeField] private float _fullUpLeadBeats = 0f;

    [Header("Movimiento")]
    [SerializeField] private SpikeEasing _easing = SpikeEasing.EaseOut;

    [Tooltip("Usa Rigidbody.MovePosition (requiere Rigidbody kinemático). Solo aplica en modo por objeto.")]
    [SerializeField] private bool _useRigidbody = false;

    [Header("Aleatoriedad")]
    [Tooltip("Semilla del azar de la ALTURA. -1 = distinta cada ejecución. >= 0 = reproducible.")]
    [SerializeField] private int _seed = -1;

    [Header("Referencias")]
    [Tooltip("BeatManager de la escena. Se localiza en BeatManager.Instance si se deja vacío.")]
    [SerializeField] private BeatManager _beatManager;

    [Tooltip("BeatGridVisualizer del nivel: aporta walkSpeed, Origin y Forward para calcular cuándo\n" +
             "llega el jugador a cada spike. Sin él se usa Z del mundo y walkSpeed = 6 (menos preciso).")]
    [SerializeField] private BeatGridVisualizer _grid;

    // ── Estado interno ──────────────────────────────────────────────────────────

    private struct Spike
    {
        public Transform t;
        public Rigidbody rb;
        public Vector3   basePos;       // posición inicial (escondida)
        public float     rise;          // altura aleatoria asignada
        public double    startRiseDSP;  // instante DSP en el que empieza a subir
        public double    riseSpanDSP;   // duración de la subida (segundos DSP)
    }

    private readonly List<Spike> _spikes = new List<Spike>();
    private bool _initialized;   // true cuando ya se han calculado los tiempos (música programada)

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_maxRise < _minRise) (_minRise, _maxRise) = (_maxRise, _minRise);

        System.Random rng = _seed < 0
            ? new System.Random()
            : new System.Random(_seed);

        foreach (Transform tr in CollectTargets())
        {
            float rise = Mathf.Lerp(_minRise, _maxRise, (float)rng.NextDouble());
            _spikes.Add(new Spike
            {
                t       = tr,
                rb      = tr.GetComponent<Rigidbody>(),
                basePos = tr.position,
                rise    = rise,
            });
        }

        if (_beatManager == null) _beatManager = BeatManager.Instance;
    }

    private void Start()
    {
        if (_beatManager == null) _beatManager = BeatManager.Instance;
        if (_grid == null)        _grid = FindAnyObjectByType<BeatGridVisualizer>();
    }

    private void Update()
    {
        if (_beatManager == null) return;

        // Esperar a que la música esté programada para conocer ScheduledStartDSP.
        if (!_initialized)
        {
            if (_beatManager.ScheduledStartDSP <= 0.0) return;
            ComputeTimings();
            _initialized = true;
        }

        double now     = AudioSettings.dspTime;
        bool   allDone = true;

        for (int i = 0; i < _spikes.Count; i++)
        {
            Spike s = _spikes[i];
            if (s.t == null) continue;

            float t;
            if (now <= s.startRiseDSP)                        t = 0f;
            else if (now >= s.startRiseDSP + s.riseSpanDSP)   t = 1f;
            else t = (float)((now - s.startRiseDSP) / s.riseSpanDSP);

            if (t < 1f) allDone = false;

            float eased = Ease(t);
            MoveSpike(s, s.basePos + Vector3.up * (s.rise * eased));
        }

        // Cuando todos han terminado de subir, fijar el tope y apagar el Update.
        if (allDone) enabled = false;
    }

    // ── Timing ──────────────────────────────────────────────────────────────────

    private void ComputeTimings()
    {
        double beatDur  = _beatManager.BeatDuration;
        double riseSpan = System.Math.Max(0.01, _riseBeats * beatDur);
        double leadSecs = _fullUpLeadBeats * beatDur;

        float walkSpeed = _grid != null ? _grid.walkSpeed : 6f;
        if (_grid == null)
            Debug.LogWarning($"[WaterSpike] '{name}': BeatGridVisualizer no asignado. " +
                             "Usando Z del mundo y walkSpeed = 6 m/s (el timing puede ser impreciso).", this);

        for (int i = 0; i < _spikes.Count; i++)
        {
            Spike s = _spikes[i];

            float dist = _grid != null
                ? Vector3.Dot(s.basePos - _grid.Origin, _grid.Forward)
                : s.basePos.z;

            double arrivalDSP = _beatManager.ScheduledStartDSP + (double)(dist / Mathf.Max(0.01f, walkSpeed));
            double fullUpDSP  = arrivalDSP - leadSecs;          // instante en que está totalmente arriba

            s.startRiseDSP = fullUpDSP - riseSpan;              // empieza a subir riseBeats antes
            s.riseSpanDSP  = riseSpan;

            _spikes[i] = s;
        }
    }

    /// <summary>REBOBINADO: recalcula las subidas con el reloj actual y reactiva el Update (los pinchos bajan).</summary>
    public void OnTimeRewind()
    {
        if (!_initialized || _beatManager == null) return;
        ComputeTimings();
        enabled = true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private List<Transform> CollectTargets()
    {
        var list = new List<Transform>();
        if (_affectChildren)
        {
            foreach (Transform c in transform) list.Add(c);
            if (list.Count == 0) list.Add(transform);   // sin hijos → anima este objeto
        }
        else
        {
            list.Add(transform);
        }
        return list;
    }

    private void MoveSpike(Spike s, Vector3 pos)
    {
        if (!_affectChildren && _useRigidbody && s.rb != null && s.rb.isKinematic)
            s.rb.MovePosition(pos);
        else
            s.t.position = pos;
    }

    private float Ease(float t)
    {
        switch (_easing)
        {
            case SpikeEasing.Linear:     return t;
            case SpikeEasing.SmoothStep: return t * t * (3f - 2f * t);
            case SpikeEasing.EaseOut:    return 1f - (1f - t) * (1f - t);
            case SpikeEasing.Overshoot:
            {
                // Back ease-out: sobrepasa levemente y se asienta.
                const float c1 = 1.70158f;
                const float c3 = c1 + 1f;
                float x = t - 1f;
                return 1f + c3 * x * x * x + c1 * x * x;
            }
            default: return t;
        }
    }

    // ── Gizmos ──────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // En play mode dibuja el objetivo real; en edit mode, el rango posible [min, max].
        if (Application.isPlaying && _spikes.Count > 0)
        {
            foreach (Spike s in _spikes)
            {
                if (s.t == null) continue;
                Vector3 top = s.basePos + Vector3.up * s.rise;
                Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
                Gizmos.DrawLine(s.basePos, top);
                Gizmos.DrawSphere(top, 0.15f);
            }
            return;
        }

        float min = Mathf.Min(_minRise, _maxRise);
        float max = Mathf.Max(_minRise, _maxRise);

        List<Transform> targets = new List<Transform>();
        if (_affectChildren) { foreach (Transform c in transform) targets.Add(c); }
        if (targets.Count == 0) targets.Add(transform);

        foreach (Transform tr in targets)
        {
            Vector3 baseP = tr.position;
            Vector3 lo    = baseP + Vector3.up * min;
            Vector3 hi    = baseP + Vector3.up * max;

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.DrawLine(lo, hi);
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.25f);
            Gizmos.DrawLine(baseP, lo);
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
            Gizmos.DrawSphere(lo, 0.10f);
            Gizmos.DrawSphere(hi, 0.10f);
        }

        var style = new GUIStyle { normal = { textColor = new Color(0.4f, 0.9f, 1f) }, fontSize = 10 };
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * (max + 0.4f),
            $"Water Spike — sube [{min:0.#}, {max:0.#}] m en {_riseBeats:0.#} beats{(_affectChildren ? "  (hijos)" : "")}",
            style);
    }
#endif
}
