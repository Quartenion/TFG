using UnityEngine;

/// <summary>
/// Obstáculo que BAJA UNAS ESCALERAS a saltos, un escalón por beat (o cada N beats),
/// aterrizando en cada escalón EXACTAMENTE sobre el beat de la música.
///
/// Usa el mismo modelo de tiempo DSP que <see cref="FallingObstacle"/>:
/// se ancla a <see cref="BeatManager.ScheduledStartDSP"/> para que el ritmo sea
/// perfecto aunque cambie el frame-rate, y (opcionalmente) se sincroniza con la
/// llegada del jugador usando el <see cref="BeatGridVisualizer"/>.
///
/// GEOMETRÍA DE LA ESCALERA (dos modos):
///   • Automático: coloca el objeto en el escalón MÁS ALTO, orienta su eje Z (forward)
///     hacia donde bajan las escaleras y define nº de escalones, avance y caída por escalón.
///   • Waypoints:  arrastra un Transform por escalón (para escaleras irregulares o curvas).
///
/// RITMO:
///   landingDSP[k] = anchorDSP + (k - impactStep) × beatsPerStep × beatDuration
///   → cada aterrizaje cae sobre un beat; entre beats el objeto describe un arco (bote).
///
/// LETALIDAD:
///   Este script SÓLO mueve el objeto. Para que mate/rompa al jugador, el obstáculo
///   necesita (igual que el resto): Layer "Obstacle" + un Collider sólido + ObstacleInfo.
///   La detección la siguen haciendo ObstacleCollision (a pie) y KayakController (en kayak).
///
/// SETUP:
///   1. Layer = "Obstacle", añade un Collider (Box) y un ObstacleInfo (Front/Bottom letales).
///   2. Añade este componente. NO hace falta Rigidbody (el script mueve el transform).
///      Si notas que la colisión con el kayak falla por ir rápido, añade un Rigidbody KINEMÁTICO.
///   3. Coloca el objeto en el escalón superior y orienta su forward escaleras abajo.
///   4. Asigna BeatManager y BeatGridVisualizer, y ajusta escalones + beatsPerStep.
/// </summary>
public class StairFallingObstacle : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Referencias")]
    [Tooltip("BeatManager de la escena. Se localiza en BeatManager.Instance si se deja vacío.")]
    [SerializeField] private BeatManager _beatManager;

    [Tooltip("BeatGridVisualizer del nivel: aporta walkSpeed, Origin y Forward para sincronizar\n" +
             "con la llegada del jugador. Sin él se usa Z del mundo y walkSpeed = 6 (menos preciso).")]
    [SerializeField] private BeatGridVisualizer _grid;

    [Header("Geometría de la escalera — modo")]
    [Tooltip("Si está activo, la escalera se define con la lista de Waypoints (un Transform por escalón).\n" +
             "Si está desactivado, se genera automáticamente desde el escalón superior (este objeto).")]
    [SerializeField] private bool _useWaypoints = false;

    [Tooltip("Un Transform por escalón, de ARRIBA hacia ABAJO. Solo se usa si 'Use Waypoints' está activo.")]
    [SerializeField] private Transform[] _stepWaypoints;

    [Header("Geometría de la escalera — automática")]
    [Tooltip("DISTANCIA DE CAÍDA en escalones (variable). Al empezar el juego el obstáculo aparece esta\n" +
             "cantidad de escalones por encima/detrás y cae hasta la posición del inspector (su destino final).\n" +
             "Ej.: 4 = empieza 4 escalones atrás y da 4 saltos hasta el sitio donde lo colocaste.")]
    [SerializeField] [Min(1)] private int _stepCount = 4;

    [Tooltip("Avance horizontal por escalón (metros). 2 m = un escalón real por salto (huella de la escalera).")]
    [SerializeField] private float _runPerStep = 2f;

    [Tooltip("Caída vertical por escalón (metros). 0.21 m = altura real entre escalones.")]
    [SerializeField] private float _dropPerStep = 0.21f;

    [Tooltip("Dirección horizontal del descenso:\n" +
             "• -Z (hacia el jugador): POR DEFECTO. Las escaleras bajan hacia -Z.\n" +
             "• +Z: bajan alejándose del jugador.\n" +
             "• Forward del objeto: usa el eje Z azul del objeto (para escaleras en diagonal;\n" +
             "  orienta la flecha azul escaleras abajo).")]
    [SerializeField] private DescentAxis _descentDirection = DescentAxis.MinusZ;

    [Header("Ritmo")]
    [Tooltip("Beats entre aterrizajes. 1 = un escalón por beat. 0.5 = dos escalones por beat (más rápido).\n" +
             "2 = un escalón cada dos beats (más lento y pesado).")]
    [SerializeField] [Min(0.125f)] private float _beatsPerStep = 1f;

    [Tooltip("Altura del bote entre escalones (metros). Apex del arco a mitad de salto.")]
    [SerializeField] [Min(0f)] private float _hopHeight = 0.6f;

    [Tooltip("Grados que rueda/voltea el objeto por escalón (tumbling). 0 = no gira. 180 = da media vuelta por escalón.")]
    [SerializeField] private float _tumbleDegreesPerStep = 90f;

    [Header("Sincronización")]
    [Tooltip("Si está activo, el obstáculo llega a su posición FINAL (la del inspector) justo cuando el\n" +
             "jugador alcanza esa posición (como FallingObstacle). Colócalo donde quieras el encuentro.")]
    [SerializeField] private bool _anchorToPlayer = true;

    [Tooltip("Solo si 'Anchor To Player' está DESACTIVADO: beat absoluto de la canción en el que el\n" +
             "obstáculo llega a su posición final. Los escalones previos caen antes según beatsPerStep.")]
    [SerializeField] private int _startBeat = 16;

    [Header("Aviso visual (opcional)")]
    [Tooltip("Hijo que se activa como aviso antes de empezar a caer (sombra, icono...). Vacío = sin aviso.")]
    [SerializeField] private GameObject _warningIndicator;

    [Tooltip("Beats de antelación con los que aparece el aviso ANTES del primer salto.")]
    [SerializeField] [Min(0)] private int _warningBeats = 2;

    [Header("Al llegar abajo")]
    [Tooltip("Qué hacer tras aterrizar el último escalón: Stop (se queda), Loop (vuelve arriba) o Destroy.")]
    [SerializeField] private EndBehaviour _endBehaviour = EndBehaviour.Stop;

    [Tooltip("Solo con End Behaviour = Loop: beats de espera abajo antes de reaparecer arriba.")]
    [SerializeField] [Min(0)] private int _loopRestBeats = 4;

    [Header("Impacto — audio y cámara")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Golpe que suena en cada aterrizaje (cada escalón).")]
    [SerializeField] private AudioClip   _stepSfx;
    [Tooltip("Golpe final opcional al tocar el último escalón. Vacío = usa el mismo que el resto.")]
    [SerializeField] private AudioClip   _finalSfx;
    [Range(0f, 1f)]
    [SerializeField] private float _sfxVolume = 0.9f;

    [Tooltip("Segundos antes del aterrizaje visual en los que suena el golpe (compensa latencia de audio).\n" +
             "0 = coincide con el impacto. 0.08 = 80 ms antes (típico).")]
    [SerializeField] [Min(0f)] private float _sfxLeadTime = 0.08f;

    [Tooltip("Trauma de cámara en cada aterrizaje [0-1]. 0 = sin shake.")]
    [SerializeField] [Range(0f, 1f)] private float _shakePerStep = 0.12f;

    [Tooltip("Trauma extra en el aterrizaje final [0-1].")]
    [SerializeField] [Range(0f, 1f)] private float _shakeFinal = 0.35f;

    [Header("Randomizar X (botón en el Inspector)")]
    [Tooltip("X mínima al pulsar 'Mover X aleatorio'.")]
    [SerializeField] private float _randomXMin = -10.643f;
    [Tooltip("X máxima al pulsar 'Mover X aleatorio'.")]
    [SerializeField] private float _randomXMax = 7.643f;

    public enum EndBehaviour { Stop, Loop, Destroy }

    public enum DescentAxis { MinusZ, PlusZ, ObjectForward }

    // ─────────────────────────────────────────────────────────────────────────
    // Estado interno
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3[]  _stepPos;        // posición world de cada escalón (0 = arriba)
    private double[]   _landingDSP;     // instante DSP en el que se aterriza cada escalón
    private bool[]     _sfxFired;       // sfx ya disparado para ese escalón
    private Vector3    _tumbleAxis = Vector3.right;
    private Quaternion _baseRotation = Quaternion.identity;

    private int        _stepSegments;   // nº de saltos = escalones-1 tramos... = _stepPos.Length-1
    private double      _warningDSP;
    private double      _endDSP;         // = _landingDSP[último]
    private double      _loopSpanDSP;    // duración de un ciclo completo (para Loop)
    private bool        _initialized;

    private enum Phase { Idle, Warning, Falling, Ended }
    private Phase _phase = Phase.Idle;

    // ─────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _baseRotation = transform.rotation;
        BuildSteps();

        // Reposiciona al INICIO (varios escalones por encima/detrás del destino final colocado).
        if (_stepPos != null && _stepPos.Length > 0)
            transform.position = _stepPos[0];

        if (_warningIndicator != null)
            _warningIndicator.SetActive(false);
    }

    private void Start()
    {
        if (_beatManager == null)
            _beatManager = BeatManager.Instance;
    }

    private void Update()
    {
        if (!_initialized)
        {
            if (_beatManager == null || _beatManager.ScheduledStartDSP <= 0) return;
            CalculateTiming();
            _initialized = true;
        }

        if (_phase == Phase.Ended) return;

        double now = AudioSettings.dspTime;

        // Aviso / inicio
        if (_phase == Phase.Idle && now >= _warningDSP)
        {
            _phase = Phase.Warning;
            if (_warningIndicator != null) _warningIndicator.SetActive(true);
        }
        if (_phase == Phase.Warning && now >= _landingDSP[0])
        {
            _phase = Phase.Falling;
            if (_warningIndicator != null) _warningIndicator.SetActive(false);
        }

        // Antes del primer salto: posado arriba
        if (_phase != Phase.Falling)
        {
            transform.position = _stepPos[0];
            transform.rotation = _baseRotation;
            return;
        }

        // ── Fase de caída por la escalera ──
        FireLandingSfx(now);

        if (now >= _endDSP)
        {
            transform.position = _stepPos[_stepPos.Length - 1];
            transform.rotation = RotationAt(_stepSegments);
            HandleEnd();
            return;
        }

        // Localizar el tramo actual: mayor k con landingDSP[k] <= now
        int k = 0;
        for (int i = 0; i < _stepSegments; i++)
            if (now >= _landingDSP[i]) k = i; else break;

        double segStart = _landingDSP[k];
        double segEnd   = _landingDSP[k + 1];
        float  t        = segEnd > segStart
            ? Mathf.Clamp01((float)((now - segStart) / (segEnd - segStart)))
            : 1f;

        // Posición: interpolación + arco de bote (apex en t=0.5)
        Vector3 flat = Vector3.Lerp(_stepPos[k], _stepPos[k + 1], t);
        flat.y += _hopHeight * 4f * t * (1f - t);
        transform.position = flat;

        // Rotación de tumbling continua (k + t escalones "rodados")
        transform.rotation = RotationAt(k + t);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Construcción de la escalera
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildSteps()
    {
        if (_useWaypoints && _stepWaypoints != null && _stepWaypoints.Length >= 2)
        {
            int n = _stepWaypoints.Length;
            _stepPos = new Vector3[n];
            for (int i = 0; i < n; i++)
                _stepPos[i] = _stepWaypoints[i] != null
                    ? _stepWaypoints[i].position
                    : transform.position;
        }
        else
        {
            int steps = Mathf.Max(1, _stepCount);
            int n     = steps + 1;             // posiciones = saltos + 1
            _stepPos  = new Vector3[n];

            Vector3 horiz = DescentHoriz();
            Vector3 end   = transform.position;   // la posición del inspector = DESTINO final
            int last = n - 1;
            for (int i = 0; i < n; i++)
            {
                int back = last - i;              // escalones por encima/detrás del final
                _stepPos[i] = end - horiz * (back * _runPerStep) + Vector3.up * (back * _dropPerStep);
            }
        }

        _stepSegments = _stepPos.Length - 1;
        _sfxFired     = new bool[_stepPos.Length];

        // Eje de tumbling: perpendicular al avance horizontal global (rueda hacia delante).
        Vector3 dir = _stepPos[_stepPos.Length - 1] - _stepPos[0];
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        _tumbleAxis = Vector3.Cross(Vector3.up, dir.normalized).normalized;
        if (_tumbleAxis.sqrMagnitude < 0.0001f) _tumbleAxis = Vector3.right;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cálculo de timing (DSP)
    // ─────────────────────────────────────────────────────────────────────────

    private void CalculateTiming()
    {
        double beatDur = _beatManager.BeatDuration;
        double stepDur = System.Math.Max(0.01, _beatsPerStep * beatDur);

        // El ancla es SIEMPRE el destino final = la posición del inspector (último escalón).
        int anchorStep = _stepPos.Length - 1;

        double anchorDSP;
        if (_anchorToPlayer)
        {
            Vector3 impactPos = _stepPos[anchorStep];
            float distFromOrigin;
            float walkSpeed;

            if (_grid != null)
            {
                distFromOrigin = Vector3.Dot(impactPos - _grid.Origin, _grid.Forward);
                walkSpeed      = _grid.walkSpeed;
            }
            else
            {
                distFromOrigin = impactPos.z;
                walkSpeed      = 6f;
                Debug.LogWarning(
                    $"[StairFallingObstacle] '{name}': BeatGridVisualizer no asignado. " +
                    "Usando Z directa y walkSpeed = 6 m/s.", this);
            }

            anchorDSP = _beatManager.ScheduledStartDSP + (double)(distFromOrigin / Mathf.Max(0.01f, walkSpeed));
        }
        else
        {
            anchorDSP = _beatManager.ScheduledStartDSP + (double)_startBeat * beatDur;
        }

        // Instante de aterrizaje de cada escalón (el final cae en anchorDSP; los previos, antes)
        _landingDSP = new double[_stepPos.Length];
        for (int k = 0; k < _stepPos.Length; k++)
            _landingDSP[k] = anchorDSP + (k - anchorStep) * stepDur;

        _warningDSP  = _landingDSP[0] - _warningBeats * beatDur;
        _endDSP      = _landingDSP[_stepPos.Length - 1];
        _loopSpanDSP = _stepSegments * stepDur + _loopRestBeats * beatDur;

        for (int i = 0; i < _sfxFired.Length; i++) _sfxFired[i] = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Dirección horizontal (normalizada) hacia la que baja la escalera.</summary>
    private Vector3 DescentHoriz()
    {
        switch (_descentDirection)
        {
            case DescentAxis.PlusZ:  return Vector3.forward;
            case DescentAxis.MinusZ: return Vector3.back;
            default: // ObjectForward
                Vector3 f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }
    }

    /// <summary>Rotación de tumbling tras haber "rodado" <paramref name="stepsRolled"/> escalones.</summary>
    private Quaternion RotationAt(float stepsRolled)
    {
        if (Mathf.Approximately(_tumbleDegreesPerStep, 0f)) return _baseRotation;
        return Quaternion.AngleAxis(_tumbleDegreesPerStep * stepsRolled, _tumbleAxis) * _baseRotation;
    }

    private void FireLandingSfx(double now)
    {
        // Escalones 1..n: el aterrizaje ocurre al final de cada salto.
        for (int k = 1; k < _stepPos.Length; k++)
        {
            if (_sfxFired[k]) continue;
            if (now >= _landingDSP[k] - _sfxLeadTime)
            {
                bool isFinal = (k == _stepPos.Length - 1);
                AudioClip clip = (isFinal && _finalSfx != null) ? _finalSfx : _stepSfx;
                if (_audioSource != null && clip != null)
                    _audioSource.PlayOneShot(clip, _sfxVolume);

                float trauma = isFinal ? _shakeFinal : _shakePerStep;
                if (trauma > 0f) CameraShaker.Instance?.Shake(trauma);

                _sfxFired[k] = true;
            }
        }
    }

    private void HandleEnd()
    {
        switch (_endBehaviour)
        {
            case EndBehaviour.Destroy:
                Destroy(gameObject);
                break;

            case EndBehaviour.Loop:
                // Reprograma un ciclo completo por delante y vuelve arriba.
                double shift = _loopSpanDSP;
                for (int k = 0; k < _landingDSP.Length; k++) _landingDSP[k] += shift;
                _warningDSP += shift;
                _endDSP      = _landingDSP[_landingDSP.Length - 1];
                for (int i = 0; i < _sfxFired.Length; i++) _sfxFired[i] = false;
                transform.position = _stepPos[0];
                transform.rotation = _baseRotation;
                _phase = Phase.Idle;
                break;

            case EndBehaviour.Stop:
            default:
                _phase = Phase.Ended;
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Randomizar X
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mueve el objeto a una X (mundo) aleatoria dentro de [_randomXMin, _randomXMax].
    /// Lo llama el botón "Mover X aleatorio" del Inspector.
    /// </summary>
    public void RandomizeXPosition()
    {
        float lo = Mathf.Min(_randomXMin, _randomXMax);
        float hi = Mathf.Max(_randomXMin, _randomXMax);
        Vector3 p = transform.position;
        p.x = Random.Range(lo, hi);
        transform.position = p;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gizmos de editor
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector3[] preview = BuildPreviewSteps();
        if (preview == null || preview.Length < 2) return;

        // Línea de la escalera + esferas por escalón (último = destino final, resaltado)
        int endIdx = preview.Length - 1;
        for (int i = 0; i < preview.Length; i++)
        {
            bool isEnd   = (i == endIdx);
            bool isStart = (i == 0);
            Gizmos.color = isEnd ? new Color(0.2f, 1f, 0.3f, 1f)          // verde = final
                                 : (isStart ? new Color(1f, 0.85f, 0.1f, 1f) // amarillo = inicio
                                            : new Color(1f, 0.45f, 0f, 0.9f));
            Gizmos.DrawSphere(preview[i], (isEnd || isStart) ? 0.16f : 0.10f);
            if (i > 0)
            {
                Gizmos.color = new Color(1f, 0.45f, 0f, 0.5f);
                Gizmos.DrawLine(preview[i - 1], preview[i]);
            }
        }

        // Arcos de bote entre escalones
        UnityEditor.Handles.color = new Color(1f, 0.6f, 0.15f, 0.7f);
        for (int i = 0; i < preview.Length - 1; i++)
            DrawHopArc(preview[i], preview[i + 1]);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3[] preview = BuildPreviewSteps();
        if (preview == null || preview.Length < 2) return;

        float bpm     = (_beatManager != null) ? _beatManager.Bpm : 120f;
        float beatDur = 60f / Mathf.Max(1f, bpm);
        float stepSec = Mathf.Max(0.01f, _beatsPerStep * beatDur);

        var style = new GUIStyle
        {
            normal    = { textColor = new Color(1f, 0.75f, 0.2f) },
            fontStyle = FontStyle.Bold,
            fontSize  = 10
        };

        int endIdx = preview.Length - 1;
        for (int i = 0; i < preview.Length; i++)
        {
            int beatsFromEnd = Mathf.RoundToInt((i - endIdx) * _beatsPerStep);
            string tag = (i == endIdx)
                ? $"escalón {i}  ★ FINAL (posición colocada)"
                : (i == 0 ? $"escalón {i}  ◀ INICIO ({beatsFromEnd} beats)"
                          : $"escalón {i}  ({beatsFromEnd} beats)");
            UnityEditor.Handles.Label(preview[i] + Vector3.up * 0.35f, tag, style);
        }

        UnityEditor.Handles.Label(
            preview[0] + Vector3.up * 0.9f,
            $"cae {preview.Length - 1} escalones · {_beatsPerStep} beat/escalón · {stepSec:F2}s/escalón\n" +
            $"termina en la posición colocada · {(_anchorToPlayer ? "anclado al jugador" : $"start beat {_startBeat}")}",
            style);
    }

    private Vector3[] BuildPreviewSteps()
    {
        if (Application.isPlaying && _stepPos != null) return _stepPos;

        if (_useWaypoints)
        {
            if (_stepWaypoints == null || _stepWaypoints.Length < 2) return null;
            var arr = new Vector3[_stepWaypoints.Length];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = _stepWaypoints[i] != null ? _stepWaypoints[i].position : transform.position;
            return arr;
        }

        int steps = Mathf.Max(1, _stepCount);
        int n = steps + 1;
        var res = new Vector3[n];
        Vector3 horiz = DescentHoriz();
        Vector3 top = transform.position;
        for (int i = 0; i < n; i++)
            res[i] = top + horiz * (i * _runPerStep) + Vector3.down * (i * _dropPerStep);
        return res;
    }

    private void DrawHopArc(Vector3 a, Vector3 b)
    {
        const int SEG = 10;
        Vector3 prev = a;
        for (int s = 1; s <= SEG; s++)
        {
            float t = s / (float)SEG;
            Vector3 p = Vector3.Lerp(a, b, t);
            p.y += _hopHeight * 4f * t * (1f - t);
            UnityEditor.Handles.DrawLine(prev, p);
            prev = p;
        }
    }
#endif
}
