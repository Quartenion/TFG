using UnityEngine;

/// <summary>
/// Obstáculo que cae del techo usando caída libre, sincronizado con la música.
///
/// Coloca el GameObject en la escena a la altura de aterrizaje (Y final).
/// El script calcula cuándo tiene que empezar a caer para aterrizar exactamente
/// cuando el jugador llega, usando cinemática de caída libre: y = ceilingY - ½gt²
///
/// CÁLCULO DE TIMING:
///   fallDistance = ceilingY - landY
///   fallDuration = √(2 × fallDistance / fallGravity)     ← física real
///   arrivalDSP   = ScheduledStartDSP + distFromOrigin / walkSpeed
///   startFallDSP = arrivalDSP - fallDuration
///
/// SETUP:
///   1. Añade este componente al prefab/GameObject del obstáculo.
///   2. Asigna BeatManager y BeatGridVisualizer.
///   3. Ajusta _ceilingY y _fallGravity.
///   4. (Opcional) Añade un hijo con la sombra/icono de aviso y asígnalo a _warningIndicator.
/// </summary>
public class FallingObstacle : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Referencias")]
    [Tooltip("BeatManager de la escena. Se localiza en BeatManager.Instance si se deja vacío.")]
    [SerializeField] private BeatManager _beatManager;

    [Tooltip("BeatGridVisualizer de la escena. Proporciona walkSpeed, Origin y Forward del nivel.\n" +
             "Sin él se usa Z del mundo y walkSpeed = 6 (menos preciso).")]
    [SerializeField] private BeatGridVisualizer _grid;

    [Header("Caída")]
    [Tooltip("Altura Y del techo desde donde cae el obstáculo.")]
    [SerializeField] private float _ceilingY = 10f;

    [Tooltip("Gravedad de la caída libre (m/s²). t = √(2h/g).\n" +
             "9.81 = gravedad real.  20-30 = caída más rápida y dramática.")]
    [SerializeField] private float _fallGravity = 20f;

    [Tooltip("Beats antes de la llegada del jugador en los que empieza la caída.\n" +
             "El bloque cae durante estos beats y aterriza cuando el jugador llega.\n" +
             "Si el valor es menor que el tiempo físico de caída, el bloque seguirá cayendo cuando el jugador pase.")]
    [SerializeField] [Min(1)] private int _beatsBeforeArrival = 4;

    [Header("Aviso visual (opcional)")]
    [Tooltip("Objeto hijo que se activa como aviso antes de la caída (sombra en el suelo, icono...).\n" +
             "Déjalo vacío si no quieres indicador previo.")]
    [SerializeField] private GameObject _warningIndicator;

    [Tooltip("Beats de antelación con los que aparece el aviso ANTES de que empiece la caída.")]
    [SerializeField] [Min(0)] private int _warningBeats = 2;

    [Header("Impacto — Cámara")]
    [Tooltip("Trauma de cámara al aterrizar [0-1]. 0 = sin shake. 0.4 = sacudida notable.")]
    [SerializeField] [Range(0f, 1f)] private float _shakeTrauma = 0.4f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _landSfx;
    [Range(0f, 1f)]
    [SerializeField] private float _sfxVolume = 1f;

    [Tooltip("Segundos antes del aterrizaje visual en los que se dispara el sonido.\n" +
             "Compensa la latencia de audio para que el golpe suene al beat.\n" +
             "0 = coincide con el impacto visual. 0.08 = 80 ms antes (valor típico).")]
    [SerializeField] [Min(0f)] private float _sfxLeadTime = 0.08f;

    // ─────────────────────────────────────────────────────────────────────────
    // Estado interno
    // ─────────────────────────────────────────────────────────────────────────

    private float  _landY;
    private double _warningDSP;
    private double _startFallDSP;
    private double _sfxDSP;          // DSP en el que se dispara el sonido (antes del impacto)
    private float  _fallDuration;    // √(2h/g) en segundos
    private bool   _initialized;
    private bool   _sfxPlayed;

    private enum Phase { Idle, Warning, Falling, Landed }
    private Phase _phase = Phase.Idle;

    // ─────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _landY = transform.position.y;
        ApplyY(_ceilingY);

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

        if (_phase == Phase.Landed) return;

        double now = AudioSettings.dspTime;

        switch (_phase)
        {
            case Phase.Idle:
                if (now >= _warningDSP)
                {
                    _phase = Phase.Warning;
                    if (_warningIndicator != null)
                        _warningIndicator.SetActive(true);
                }
                break;

            case Phase.Warning:
                if (now >= _startFallDSP)
                {
                    _phase = Phase.Falling;
                    if (_warningIndicator != null)
                        _warningIndicator.SetActive(false);
                }
                break;

            case Phase.Falling:
            {
                // Caída libre: y = ceilingY - ½ g t²
                float t = (float)(now - _startFallDSP);
                float y = _ceilingY - 0.5f * _fallGravity * t * t;

                // Sonido anticipado: se dispara _sfxLeadTime segundos antes del impacto visual
                if (!_sfxPlayed && now >= _sfxDSP)
                {
                    PlayLandSfx();
                    _sfxPlayed = true;
                }

                if (y <= _landY)
                {
                    ApplyY(_landY);
                    _phase = Phase.Landed;
                    OnLanded();
                }
                else
                {
                    ApplyY(y);
                }
                break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cálculo de timing
    // ─────────────────────────────────────────────────────────────────────────

    private void CalculateTiming()
    {
        float fallDistance = _ceilingY - _landY;
        float g            = Mathf.Max(_fallGravity, 0.01f);

        // Tiempo que tarda en caer con gravedad real: h = ½gt² → t = √(2h/g)
        _fallDuration = Mathf.Sqrt(2f * fallDistance / g);

        float distFromOrigin;
        float walkSpeed;

        if (_grid != null)
        {
            distFromOrigin = Vector3.Dot(transform.position - _grid.Origin, _grid.Forward);
            walkSpeed      = _grid.walkSpeed;
        }
        else
        {
            distFromOrigin = transform.position.z;
            walkSpeed      = 6f;
            Debug.LogWarning(
                $"[FallingObstacle] '{name}': BeatGridVisualizer no asignado. " +
                "Usando Z directa y walkSpeed = 6 m/s.", this);
        }

        double arrivalDSP   = _beatManager.ScheduledStartDSP
                              + (double)(distFromOrigin / walkSpeed);
        double beatDuration = _beatManager.BeatDuration;

        _startFallDSP = arrivalDSP - (double)(_beatsBeforeArrival * beatDuration);
        _sfxDSP       = arrivalDSP - (double)_sfxLeadTime;
        _warningDSP   = _startFallDSP - (double)(_warningBeats * beatDuration);
        _sfxPlayed    = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyY(float y)
    {
        Vector3 p = transform.position;
        p.y = y;
        transform.position = p;
    }

    private void OnLanded()
    {
        // El sonido ya se habrá disparado antes (_sfxLeadTime s antes del impacto).
        // Solo reproducirlo aquí como fallback si por algún motivo no sonó.
        if (!_sfxPlayed) PlayLandSfx();
    }

    private void PlayLandSfx()
    {
        if (_audioSource != null && _landSfx != null)
            _audioSource.PlayOneShot(_landSfx, _sfxVolume);

        if (_shakeTrauma > 0f)
            CameraShaker.Instance?.Shake(_shakeTrauma);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gizmos de editor
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        float groundY   = Application.isPlaying ? _landY : transform.position.y;
        float fallDist  = _ceilingY - groundY;
        float g         = Mathf.Max(_fallGravity, 0.01f);
        float fallTime  = fallDist > 0f ? Mathf.Sqrt(2f * fallDist / g) : 0f;

        Vector3 posGround = new Vector3(transform.position.x, groundY,   transform.position.z);
        Vector3 posCeil   = new Vector3(transform.position.x, _ceilingY, transform.position.z);

        UnityEditor.Handles.color = new Color(1f, 0.45f, 0f, 0.6f);
        UnityEditor.Handles.DrawDottedLine(posGround, posCeil, 5f);

        Gizmos.color = new Color(1f, 0.45f, 0f, 0.5f);
        Gizmos.DrawWireCube(posCeil, Vector3.one);
        Gizmos.DrawSphere(posCeil, 0.12f);

        Gizmos.DrawLine(posCeil, posCeil + Vector3.down * 0.6f);
        Gizmos.DrawLine(posCeil + Vector3.down * 0.6f, posCeil + Vector3.down * 0.4f + Vector3.right * 0.15f);
        Gizmos.DrawLine(posCeil + Vector3.down * 0.6f, posCeil + Vector3.down * 0.4f - Vector3.right * 0.15f);

        var style = new GUIStyle
        {
            normal    = { textColor = new Color(1f, 0.55f, 0.1f) },
            fontStyle = FontStyle.Bold,
            fontSize  = 10
        };
        // Mostrar si los beats configurados son suficientes para completar la caída
        float bpm          = (_beatManager != null) ? _beatManager.Bpm : 180f;
        float beatDur      = 60f / bpm;
        float triggerSecs  = _beatsBeforeArrival * beatDur;
        string fallWarning = fallTime > triggerSecs
            ? $"  ⚠ caída={fallTime:F2}s > {_beatsBeforeArrival} beats={triggerSecs:F2}s"
            : "";

        UnityEditor.Handles.Label(
            posCeil + Vector3.up * 0.55f,
            $"↓ {_beatsBeforeArrival} beats ({triggerSecs:F2}s)  |  caída {fallTime:F2}s{fallWarning}",
            style);
    }
#endif
}
