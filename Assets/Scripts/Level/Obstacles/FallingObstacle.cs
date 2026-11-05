using UnityEngine;

/// <summary>
/// Obstáculo que cae del techo en caída libre, sincronizado con la música.
///
/// El GameObject se coloca en la escena a la altura en la que aterriza (la Y final). El script calcula cuándo tiene
/// que empezar a caer para aterrizar justo cuando llega el jugador, con la fórmula de la caída libre,
/// y = ceilingY - ½gt².
///
/// Cálculo del momento de la caída.
///   fallDistance = ceilingY - landY
///   fallDuration = √(2 × fallDistance / fallGravity)
///   arrivalDSP   = DspAtSongTime(distFromOrigin / walkSpeed)
///   startFallDSP = arrivalDSP - fallDuration
///
/// Configuración.
///   1. Se añade este componente al prefab o GameObject del obstáculo.
///   2. Se asignan BeatManager y BeatGridVisualizer.
///   3. Se ajustan _ceilingY y _fallGravity.
///   4. Opcionalmente, se añade un hijo con la sombra o el icono de aviso y se asigna a _warningIndicator.
/// </summary>
public class FallingObstacle : MonoBehaviour, IRewindable
{
    // -=+=- Inspector -=+=-

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

    // -=+=- Estado interno -=+=-

    private float  _landY;
    private double _warningDSP;
    private double _startFallDSP;
    private double _sfxDSP;          // instante DSP en el que suena el sonido, antes del impacto
    private bool   _initialized;
    private bool   _sfxPlayed;

    private enum Phase { Idle, Warning, Falling, Landed }
    private Phase _phase = Phase.Idle;

    // -=+=- Ciclo de vida de Unity -=+=-

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
            if (_beatManager == null || !_beatManager.HasClock) return;
            CalculateTiming();
            _initialized = true;
        }

        if (_phase == Phase.Landed) return;

        double now = BeatManager.DspNow;

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
                // Caída libre, y = ceilingY - ½ g t²
                float t = (float)(now - _startFallDSP);
                float y = _ceilingY - 0.5f * _fallGravity * t * t;

                // El sonido se adelanta y suena _sfxLeadTime segundos antes del impacto visual.
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

    // -=+=- Cálculo de los tiempos -=+=-

    private void CalculateTiming()
    {
        if (double.IsNaN(_arrivalOffset)) _arrivalOffset = ComputeArrivalOffset();

        double arrivalDSP   = _beatManager.DspAtSongTime(_arrivalOffset);
        double beatDuration = _beatManager.BeatDuration;

        _startFallDSP = arrivalDSP - (double)(_beatsBeforeArrival * beatDuration);
        _sfxDSP       = arrivalDSP - (double)_sfxLeadTime;
        _warningDSP   = _startFallDSP - (double)(_warningBeats * beatDuration);
        _sfxPlayed    = false;
    }

    private double _arrivalOffset = double.NaN;   // segundos de canción hasta que llega el jugador (se calcula una sola vez)

    private double ComputeArrivalOffset()
    {
        float distFromOrigin;
        float walkSpeed;

        if (_grid != null)
        {
            distFromOrigin = _grid.DistanceAlong(transform.position);
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

        return (double)(distFromOrigin / walkSpeed);
    }

    /// <summary>
    /// Rebobinado. Vuelve a calcular los tiempos con el reloj actual y se coloca directamente en la fase que toca (arriba
    /// esperando, con el aviso, cayendo o ya en el suelo), sin repetir el golpe si ya había caído.
    /// </summary>
    public void OnTimeRewind()
    {
        if (!_initialized || _beatManager == null) return;
        CalculateTiming();

        double now = BeatManager.DspNow;
        // Si el tutorial está frenando el tiempo, el sonido que todavía no ha sonado tiene que sonar al llegar, así que no se marca.
        _sfxPlayed = TutorialGate.Busy ? _sfxPlayed : now >= _sfxDSP;

        if (now < _warningDSP)
        {
            _phase = Phase.Idle;
            ApplyY(_ceilingY);
            if (_warningIndicator != null) _warningIndicator.SetActive(false);
        }
        else if (now < _startFallDSP)
        {
            _phase = Phase.Warning;
            ApplyY(_ceilingY);
            if (_warningIndicator != null) _warningIndicator.SetActive(true);
        }
        else
        {
            if (_warningIndicator != null) _warningIndicator.SetActive(false);
            float t = (float)(now - _startFallDSP);
            float y = _ceilingY - 0.5f * _fallGravity * t * t;
            if (y <= _landY) { _phase = Phase.Landed;  ApplyY(_landY); }
            else             { _phase = Phase.Falling; ApplyY(y); }
        }
    }

    // -=+=- Funciones auxiliares -=+=-

    private void ApplyY(float y)
    {
        Vector3 p = transform.position;
        p.y = y;
        transform.position = p;
    }

    private void OnLanded()
    {
        // El sonido ya habrá sonado antes (_sfxLeadTime segundos antes del impacto). Aquí solo se reproduce si por algún
        // motivo no llegó a sonar.
        if (!_sfxPlayed) PlayLandSfx();
    }

    private void PlayLandSfx()
    {
        if (_audioSource != null && _landSfx != null)
            _audioSource.PlayOneShot(_landSfx, _sfxVolume);

        if (_shakeTrauma > 0f)
            CameraShaker.Instance?.ShakeAt(_shakeTrauma, transform.position);
    }

    // -=+=- Gizmos del editor -=+=-

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
        // Se muestra si los beats configurados son suficientes para completar la caída.
        float beatDur      = BeatManager.BeatDurationOf(_beatManager);
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
