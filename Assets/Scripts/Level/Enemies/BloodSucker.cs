using UnityEngine;

/// <summary>
/// BLOODSUCKER — bicho volador que chupa la sangre del jugador.
///
///   • Mira al jugador en todo momento.
///   • Dibuja una LÍNEA DE SANGRE (LineRenderer) del jugador hasta él mientras chupa.
///   • Solo chupa si está POR DELANTE del jugador y dentro de rango. Si el jugador lo pasa
///     sin matarlo, deja de chupar PARA SIEMPRE (se queda atrás).
///   • Mientras chupa, drena sangre con EnergyManager.DrainBlood → la barra parpadea y baja rápido.
///   • Se DESINTEGRA cuando lo miras fijamente: usa un GazeSwitch en el mismo objeto; su
///     OnActivated se conecta solo a Disintegrate(). También puedes llamar a Disintegrate() a mano.
///
/// SETUP:
///   1. Crea el bicho (malla + un Collider, necesario para que la mirada lo detecte).
///   2. Añade este componente Y un GazeSwitch (ambos en el mismo objeto).
///        · GazeSwitch → Hold Duration = cuánto hay que mirarlo para matarlo.
///        · GazeSwitch → Max Distance ≈ Drain Range (para poder matarlo dentro del rango en que chupa).
///        · No hace falta cablear el OnActivated: BloodSucker lo engancha automáticamente.
///   3. Coloca el bicho POR DELANTE en el camino. Ajusta Drain Range y Drain Per Second.
///   4. (Opcional) VFX/SFX de desintegración y el punto del jugador de donde sale la línea.
/// </summary>
[AddComponentMenu("Level/Blood Sucker")]
public class BloodSucker : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Jugador. Se localiza solo por PlayerMovement si se deja vacío.")]
    [SerializeField] private Transform _player;
    [Tooltip("Punto del jugador de donde SALE la línea de sangre (pecho/mano). Si vacío, usa el jugador + offset.")]
    [SerializeField] private Transform _lineStartOnPlayer;
    [Tooltip("Offset sobre el jugador para el inicio de la línea si no hay 'Line Start On Player' (altura del pecho).")]
    [SerializeField] private Vector3 _lineStartOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Mirar al jugador")]
    [Tooltip("Gira el bicho para mirar al jugador continuamente.")]
    [SerializeField] private bool _lookAtPlayer = true;
    [Tooltip("Solo girar en horizontal (mantener el bicho recto). Desactívalo para que apunte también arriba/abajo.")]
    [SerializeField] private bool _yawOnly = false;
    [Tooltip("Suavidad del giro (0 = instantáneo). Más alto = gira más rápido.")]
    [SerializeField] private float _lookSpeed = 10f;

    [Header("Chupar sangre")]
    [Tooltip("Sangre por segundo que roba mientras chupa (súbelo para que baje más rápido la barra).")]
    [SerializeField] private float _drainPerSecond = 16f;
    [Tooltip("Rango en BEATS: empieza a chupar cuando el jugador está a esta distancia (en beats de\n" +
             "avance) por delante. Se convierte a metros con el BPM y la velocidad del jugador.")]
    [SerializeField] private float _drainRangeBeats = 6f;
    [Tooltip("Metros por delante para considerarse 'delante'. Cuando cae por debajo de 0 = ya lo pasaste.")]
    [SerializeField] private float _inFrontMargin = 0.5f;

    [Header("Vuelo (hover)")]
    [SerializeField] private bool _hover = true;
    [SerializeField] private float _hoverAmplitude = 0.3f;
    [SerializeField] private float _hoverSpeed = 2f;

    [Header("Línea de sangre")]
    [Tooltip("LineRenderer de la línea. Si se deja vacío, se crea uno automáticamente.")]
    [SerializeField] private LineRenderer _bloodLine;
    [Tooltip("Material de la línea (opcional). Si vacío, se crea uno básico.")]
    [SerializeField] private Material _lineMaterial;
    [SerializeField] private Color _bloodColor = new Color(0.6f, 0.02f, 0.03f, 1f);
    [SerializeField] private float _lineWidth = 0.12f;
    [Tooltip("Nº de puntos de la línea (más = serpenteo más suave).")]
    [Min(2)][SerializeField] private int _lineSegments = 12;
    [Tooltip("Amplitud del serpenteo de la línea (0 = recta).")]
    [SerializeField] private float _lineWobble = 0.25f;
    [SerializeField] private float _lineWobbleSpeed = 6f;

    [Header("Desintegración")]
    [Tooltip("VFX que aparece al desintegrarse (opcional).")]
    [SerializeField] private GameObject _disintegrateVFX;
    [SerializeField] private float _vfxLifetime = 3f;
    [Tooltip("Sonido al desintegrarse (opcional).")]
    [SerializeField] private AudioClip _disintegrateSound;
    [Range(0f, 1f)][SerializeField] private float _sfxVolume = 0.9f;

    // ── Estado ──────────────────────────────────────────────────────────────
    private PlayerMovement _pm;
    private GazeSwitch     _gaze;
    private Vector3        _basePos;
    private bool           _dead;
    private bool           _passed;      // el jugador ya lo pasó → no vuelve a chupar
    private float          _hoverPhase;

    /// <summary>True mientras está chupando sangre este frame (útil para animaciones externas).</summary>
    public bool IsSucking { get; private set; }

    /// <summary>
    /// Mueve el punto alrededor del que flota. Úsalo cada frame para que el bicho VIAJE con algo
    /// (p. ej. con el jefe en la Fase 3), en lugar de quedarse fijo donde apareció.
    /// </summary>
    public void SetBasePosition(Vector3 worldPos) => _basePos = worldPos;

    // ── Conversión (Fase 3 del jefe) ─────────────────────────────────────────
    private Transform _convertTarget;
    private bool      _converted;

    /// <summary>
    /// Si se asigna, al MIRARLO no se desintegra: se da la vuelta, mira a este objetivo (el jefe) y le chupa
    /// la sangre a ÉL (la línea de sangre va del objetivo al bicho). Null = comportamiento normal.
    /// </summary>
    public void SetConvertTarget(Transform target) => _convertTarget = target;

    /// <summary>¿Ya se ha vuelto contra el jefe?</summary>
    public bool IsConverted => _converted;

    /// <summary>¿Convertido y ya MIRANDO a su objetivo (ha terminado de girarse)?</summary>
    public bool IsFacingTarget
    {
        get
        {
            if (!_converted || _convertTarget == null) return _converted;
            Vector3 d = ConvertPoint() - transform.position;
            return d.sqrMagnitude < 0.01f || Vector3.Angle(transform.forward, d) < 12f;
        }
    }

    /// <summary>Se vuelve contra el jefe (lo llama la mirada si hay objetivo de conversión).</summary>
    public void Convert()
    {
        if (_dead || _converted) return;
        _converted = true;
        IsSucking  = false;
    }

    private Vector3 ConvertPoint()
    {
        if (_convertTarget == null) return transform.position + transform.forward;
        Renderer r = _convertTarget.GetComponent<Renderer>();
        return r != null ? r.bounds.center : _convertTarget.position;
    }

    private bool _forceDisintegrate;

    /// <summary>Se desintegra SÍ o SÍ (aunque tenga objetivo de conversión).</summary>
    public void DisintegrateNow()
    {
        _forceDisintegrate = true;
        Disintegrate();
    }

    private void OnGazed()
    {
        if (_convertTarget != null) Convert();
        else Disintegrate();
    }

    private void Awake()
    {
        _basePos    = transform.position;
        _hoverPhase = Random.value * Mathf.PI * 2f;   // que no floten todos igual
    }

    private void Start()
    {
        if (_player == null)
        {
            _pm = FindAnyObjectByType<PlayerMovement>();
            if (_pm != null) _player = _pm.transform;
        }
        else _pm = _player.GetComponent<PlayerMovement>();

        // Matar mirándolo: engancha el GazeSwitch → Disintegrate automáticamente.
        _gaze = GetComponent<GazeSwitch>();
        if (_gaze == null) _gaze = GetComponentInChildren<GazeSwitch>();
        if (_gaze != null) _gaze.OnActivated.AddListener(OnGazed);
        else Debug.LogWarning($"[BloodSucker] '{name}' no tiene GazeSwitch: no se podrá matar mirándolo.", this);

        EnsureLine();
        if (_bloodLine != null) _bloodLine.enabled = false;
    }

    private void OnDestroy()
    {
        if (_gaze != null) _gaze.OnActivated.RemoveListener(OnGazed);
    }

    private void Update()
    {
        if (_dead) return;

        // Hover alrededor de la posición base.
        if (_hover)
            transform.position = _basePos + Vector3.up * (Mathf.Sin(Time.time * _hoverSpeed + _hoverPhase) * _hoverAmplitude);

        // Convertido: mira al JEFE (girándose poco a poco) y le chupa la sangre a él.
        if (_converted)
        {
            Vector3 tgt = ConvertPoint();
            Vector3 dir = tgt - transform.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 180f * Time.deltaTime);
            }
            IsSucking = IsFacingTarget;
            UpdateBloodLine(IsSucking, tgt);
            return;
        }

        if (_player == null) return;

        // Mirar al jugador.
        if (_lookAtPlayer)
        {
            Vector3 dir = _player.position - transform.position;
            if (_yawOnly) dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
                transform.rotation = _lookSpeed > 0f
                    ? Quaternion.Slerp(transform.rotation, target, Time.deltaTime * _lookSpeed)
                    : target;
            }
        }

        // ¿Está por delante del jugador y dentro de rango?
        bool sucking = false;
        if (!_passed)
        {
            Vector3 fwd   = _pm != null ? _pm.RunForward : Vector3.forward;
            Vector3 toMe  = transform.position - _player.position;
            float   ahead = Vector3.Dot(toMe, fwd);       // >0 = por delante

            if (ahead < 0f)
                _passed = true;                            // lo pasó → nunca más chupa
            else
                sucking = _drainPerSecond > 0f &&                 // con drenaje 0 es solo una diana
                          ahead > _inFrontMargin && toMe.magnitude <= SuckRange();
        }

        IsSucking = sucking;

        // Chupar sangre + dibujar la línea.
        if (sucking && EnergyManager.Instance != null)
            EnergyManager.Instance.DrainBlood(_drainPerSecond * Time.deltaTime);

        UpdateBloodLine(sucking);
    }

    // ── Línea de sangre ───────────────────────────────────────────────────────

    private void EnsureLine()
    {
        if (_bloodLine == null)
        {
            var go = new GameObject("BloodLine");
            go.transform.SetParent(transform, false);
            _bloodLine = go.AddComponent<LineRenderer>();
        }

        _bloodLine.material      = _lineMaterial != null ? _lineMaterial : new Material(Shader.Find("Sprites/Default"));
        _bloodLine.useWorldSpace = true;
        _bloodLine.textureMode   = LineTextureMode.Stretch;
        _bloodLine.numCapVertices    = 4;
        _bloodLine.numCornerVertices = 2;
        _bloodLine.startWidth   = _lineWidth;
        _bloodLine.endWidth     = _lineWidth;
        _bloodLine.startColor   = _bloodColor;
        _bloodLine.endColor     = _bloodColor;
        _bloodLine.positionCount = Mathf.Max(2, _lineSegments);
        _bloodLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void UpdateBloodLine(bool visible)
    {
        if (_player == null) { UpdateBloodLine(false, Vector3.zero); return; }
        Vector3 start = _lineStartOnPlayer != null
            ? _lineStartOnPlayer.position
            : _player.position + _lineStartOffset;
        UpdateBloodLine(visible, start);
    }

    /// <summary>Línea de sangre desde 'start' (de quien chupa) hasta el bicho.</summary>
    private void UpdateBloodLine(bool visible, Vector3 start)
    {
        if (_bloodLine == null) return;

        if (!visible)
        {
            if (_bloodLine.enabled) _bloodLine.enabled = false;
            return;
        }
        if (!_bloodLine.enabled) _bloodLine.enabled = true;

        Vector3 end = transform.position;

        // Eje perpendicular para el serpenteo (se anula en los extremos con sin(pi·t)).
        Vector3 perp = Vector3.Cross((end - start).normalized, Vector3.up);
        if (perp.sqrMagnitude < 0.001f) perp = Vector3.right;
        perp.Normalize();

        int n = _bloodLine.positionCount;
        for (int i = 0; i < n; i++)
        {
            float   t = i / (float)(n - 1);
            Vector3 p = Vector3.Lerp(start, end, t);
            float   w = Mathf.Sin(t * Mathf.PI) * _lineWobble * Mathf.Sin(Time.time * _lineWobbleSpeed + t * 6f);
            _bloodLine.SetPosition(i, p + perp * w);
        }
    }

    // ── Desintegración ────────────────────────────────────────────────────────

    /// <summary>
    /// Mata el BloodSucker: apaga la línea, lanza VFX/SFX y se destruye.
    /// Se conecta solo a GazeSwitch.OnActivated; también puedes llamarlo a mano.
    /// </summary>
    public void Disintegrate()
    {
        // Con objetivo de conversión (Fase 3 del jefe), mirarlo NO lo mata: se vuelve contra el jefe. Así da igual
        // si el GazeSwitch del prefab tiene "Disintegrate" puesto a mano en su OnActivated.
        if (_convertTarget != null && !_forceDisintegrate) { Convert(); return; }
        if (_dead) return;
        _dead = true;
        IsSucking = false;

        if (_bloodLine != null) _bloodLine.enabled = false;

        if (_disintegrateVFX != null)
        {
            GameObject fx = Instantiate(_disintegrateVFX, transform.position, Quaternion.identity);
            if (_vfxLifetime > 0f) Destroy(fx, _vfxLifetime);
        }
        if (_disintegrateSound != null)
            AudioSource.PlayClipAtPoint(_disintegrateSound, transform.position, _sfxVolume);

        Destroy(gameObject);
    }

    // ── Rango en beats ──────────────────────────────────────────────────────────

    /// <summary>
    /// Distancia a la que empieza a chupar: el rango en beats, pero NUNCA más lejos de lo que se le puede mirar
    /// (GazeSwitch → Max Distance, con 2 m de margen). Antes chupaba desde ~60 m y solo se le podía matar a ≤40 m:
    /// el jugador perdía sangre sin poder hacer nada.
    /// </summary>
    private float SuckRange()
    {
        float r = DrainRangeMeters();
        if (_gaze != null && _gaze.MaxDistance > 0f) r = Mathf.Min(r, Mathf.Max(1f, _gaze.MaxDistance - 2f));
        return r;
    }

    /// <summary>Convierte el rango de beats a metros: beats × duración_de_beat × velocidad del jugador.</summary>
    private float DrainRangeMeters()
    {
        BeatManager    bm = BeatManager.Instance;
        PlayerMovement pm = _pm;
#if UNITY_EDITOR
        if (bm == null) bm = FindAnyObjectByType<BeatManager>();
        if (pm == null) pm = _player != null ? _player.GetComponent<PlayerMovement>() : FindAnyObjectByType<PlayerMovement>();
#endif
        float beatDur = BeatManager.BeatDurationOf(bm);
        float speed   = pm != null ? pm.walkSpeed    : 12f;
        return Mathf.Max(0f, _drainRangeBeats) * beatDur * speed;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 basePos = Application.isPlaying ? _basePos : transform.position;

        Gizmos.color = new Color(0.8f, 0.05f, 0.05f, 0.6f);
        if (_gaze == null) _gaze = GetComponent<GazeSwitch>();
        Gizmos.DrawWireSphere(basePos, SuckRange());

        if (_player != null)
        {
            Gizmos.color = new Color(0.7f, 0f, 0f, 0.5f);
            Vector3 start = _lineStartOnPlayer != null ? _lineStartOnPlayer.position : _player.position + _lineStartOffset;
            Gizmos.DrawLine(start, basePos);
        }
    }
#endif
}
