using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// ATMÓSFERA «DARK FANTASY 80s» del nivel, aplicada SOLO AL JUGAR. En el editor la escena se ve con su iluminación
/// normal (clara) para poder editar el nivel cómodamente; al pulsar Play (y en la build) aplica:
///   • luz ambiente en degradado AZUL OSCURO (nada es negro del todo), sin cielo,
///   • niebla azul oscura y fondo de cámara azul muy oscuro,
///   • luces direccionales bajadas a una luz de luna fría y tenue,
///   • el posprocesado (grano, bloom, viñeta, color, tonos azul/ámbar, aberración, ACES),
///   • la linterna del casco y la luz del corazón del jugador.
/// Cada efecto tiene su CASILLA: se puede activar/desactivar cuando quieras, también en Play (se ve al momento y,
/// al desactivarlo, vuelve lo que había). Lo crea Tools/Estética/Aplicar «Dark Fantasy 80s».
/// </summary>
[DefaultExecutionOrder(-500)]
[AddComponentMenu("Level/Dark Fantasy Atmosphere")]
public class DarkFantasyAtmosphere : MonoBehaviour
{
    [Header("Activar / desactivar efectos (también en Play)")]
    [Tooltip("Luz ambiente azul oscura (sin cielo).")]                         public bool blueAmbient = true;
    [Tooltip("Niebla azul oscura.")]                                           public bool fogEnabled = true;
    [Tooltip("Luces direccionales convertidas en luna tenue y fría.")]        public bool moonLight = true;
    [Tooltip("Fondo de la cámara azul muy oscuro (en vez del cielo).")]       public bool darkBackground = true;
    [Tooltip("Sin fondo oscuro: la cámara muestra el SKYBOX de la escena (Lighting → Skybox Material), aunque la del " +
             "jugador venga con fondo de color sólido. Lo activa solo el nivel tutorial.")]
    public bool skyboxBackground = false;
    [Tooltip("Linterna de casco del jugador (con parpadeos al beat).")]       public bool helmetLamp = true;
    [Tooltip("Luz roja del corazón que late al beat.")]                       public bool heartLight = true;

    [Header("Posprocesado (cada efecto por separado)")]
    [Tooltip("Todo el posprocesado (si está apagado, ignora las casillas de abajo).")] public bool postProcessing = true;
    public bool filmGrain = true;
    public bool bloom = true;
    public bool vignette = true;
    [Tooltip("Contraste, saturación y exposición.")]                          public bool colorAdjustments = true;
    [Tooltip("Sombras azules / luces ámbar.")]                                public bool splitToning = true;
    public bool chromaticAberration = true;
    [Tooltip("Curva de tonos tipo película (ACES).")]                         public bool tonemapping = true;

    [Header("Luz ambiente (degradado)")]
    public Color ambientSky     = new Color(0.17f, 0.23f, 0.42f);
    public Color ambientEquator = new Color(0.11f, 0.15f, 0.30f);
    public Color ambientGround  = new Color(0.06f, 0.08f, 0.17f);
    [Range(0f, 1f)] public float reflectionIntensity = 0.25f;

    [Header("Niebla y fondo")]
    public Color fogColor = new Color(0.05f, 0.07f, 0.15f);
    [Range(0f, 0.1f)] public float fogDensity = 0.018f;
    public Color cameraBackground = new Color(0.035f, 0.05f, 0.11f);

    [Header("Luces direccionales (luna)")]
    public Color moonColor = new Color(0.55f, 0.65f, 0.85f);
    public float moonIntensity = 0.45f;

    [Header("Posprocesado")]
    [Tooltip("Volume con el perfil DarkFantasy80s. Se deja APAGADO en el editor y se enciende al jugar.")]
    public Volume volume;

    // ── Lo que había antes (para volver a ello al desactivar un efecto en Play) ──
    private Material   _skybox;
    private AmbientMode _ambMode;
    private Color      _ambLight, _ambSky, _ambEq, _ambGround;
    private float      _ambIntensity, _reflIntensity;
    private bool       _fog;
    private FogMode    _fogMode;
    private Color      _fogColor;
    private float      _fogDensity;
    private Light[]    _dirLights;
    private Color[]    _dirColors;
    private float[]    _dirIntensities;
    private Camera[]   _cams;
    private CameraClearFlags[] _camFlags;
    private Color[]    _camColors;
    private VolumeProfile _runtimeProfile;   // copia del perfil (así las casillas no modifican el archivo)

    private void Awake()
    {
        if (!Application.isPlaying) return;

        _skybox = RenderSettings.skybox;           _ambMode = RenderSettings.ambientMode;
        _ambLight = RenderSettings.ambientLight;   _ambSky = RenderSettings.ambientSkyColor;
        _ambEq = RenderSettings.ambientEquatorColor; _ambGround = RenderSettings.ambientGroundColor;
        _ambIntensity = RenderSettings.ambientIntensity; _reflIntensity = RenderSettings.reflectionIntensity;
        _fog = RenderSettings.fog; _fogMode = RenderSettings.fogMode;
        _fogColor = RenderSettings.fogColor; _fogDensity = RenderSettings.fogDensity;

        var dl = new System.Collections.Generic.List<Light>();
        foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Include))
            if (l.type == LightType.Directional) dl.Add(l);
        _dirLights = dl.ToArray();
        _dirColors = new Color[_dirLights.Length]; _dirIntensities = new float[_dirLights.Length];
        for (int i = 0; i < _dirLights.Length; i++) { _dirColors[i] = _dirLights[i].color; _dirIntensities[i] = _dirLights[i].intensity; }

        var cl = new System.Collections.Generic.List<Camera>();
        foreach (Camera c in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            if (c.CompareTag("MainCamera") && c.targetTexture == null) cl.Add(c);
        _cams = cl.ToArray();
        _camFlags = new CameraClearFlags[_cams.Length]; _camColors = new Color[_cams.Length];
        for (int i = 0; i < _cams.Length; i++) { _camFlags[i] = _cams[i].clearFlags; _camColors[i] = _cams[i].backgroundColor; }

        if (volume != null) _runtimeProfile = volume.profile;   // (instancia propia en juego)
        ApplyAll();
    }

    private void Update()
    {
        if (Application.isPlaying) ApplyAll();   // (barato: así las casillas y los colores responden al momento)
    }

    private void ApplyAll()
    {
        // ── Ambiente ──
        if (blueAmbient)
        {
            RenderSettings.skybox              = null;
            RenderSettings.ambientMode         = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor  = ambientGround;
            RenderSettings.ambientIntensity    = 1f;
            RenderSettings.reflectionIntensity = reflectionIntensity;
        }
        else
        {
            RenderSettings.skybox = _skybox;               RenderSettings.ambientMode = _ambMode;
            RenderSettings.ambientLight = _ambLight;       RenderSettings.ambientSkyColor = _ambSky;
            RenderSettings.ambientEquatorColor = _ambEq;   RenderSettings.ambientGroundColor = _ambGround;
            RenderSettings.ambientIntensity = _ambIntensity; RenderSettings.reflectionIntensity = _reflIntensity;
        }

        // ── Niebla ──
        if (fogEnabled)
        {
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity;
        }
        else
        {
            RenderSettings.fog = _fog; RenderSettings.fogMode = _fogMode;
            RenderSettings.fogColor = _fogColor; RenderSettings.fogDensity = _fogDensity;
        }

        // ── Luna ──
        for (int i = 0; i < _dirLights.Length; i++)
        {
            if (_dirLights[i] == null) continue;
            _dirLights[i].color     = moonLight ? moonColor     : _dirColors[i];
            _dirLights[i].intensity = moonLight ? moonIntensity : _dirIntensities[i];
        }

        // ── Fondo ──
        for (int i = 0; i < _cams.Length; i++)
        {
            if (_cams[i] == null) continue;
            _cams[i].clearFlags      = darkBackground ? CameraClearFlags.SolidColor
                                     : skyboxBackground && RenderSettings.skybox != null ? CameraClearFlags.Skybox
                                     : _camFlags[i];
            _cams[i].backgroundColor = darkBackground ? cameraBackground : _camColors[i];
        }

        // ── Posprocesado ──
        if (volume != null)
        {
            volume.enabled = postProcessing;
            if (_runtimeProfile != null)
            {
                SetActive<FilmGrain>(filmGrain);
                SetActive<Bloom>(bloom);
                SetActive<Vignette>(vignette);
                SetActive<ColorAdjustments>(colorAdjustments);
                SetActive<SplitToning>(splitToning);
                SetActive<ChromaticAberration>(chromaticAberration);
                SetActive<Tonemapping>(tonemapping);
            }
        }

        // ── Luces del jugador ──
        var lamp = PlayerLight<PlayerSurroundLight>();
        if (lamp != null && lamp.enabled != helmetLamp) lamp.enabled = helmetLamp;
        var heart = PlayerLight<HeartLight>();
        if (heart != null && heart.enabled != heartLight) heart.enabled = heartLight;
    }

    private void SetActive<T>(bool on) where T : VolumeComponent
    {
        if (_runtimeProfile.TryGet(out T c) && c.active != on) c.active = on;
    }

    private PlayerSurroundLight _lampCache;
    private HeartLight _heartCache;
    private float _nextSearch;   // (si no hay luces en el jugador, se vuelve a buscar solo cada segundo)
    private T PlayerLight<T>() where T : Behaviour
    {
        if ((_lampCache == null || _heartCache == null) && Time.unscaledTime >= _nextSearch)
        {
            _nextSearch = Time.unscaledTime + 1f;
            if (_lampCache == null)  _lampCache  = FindAnyObjectByType<PlayerSurroundLight>(FindObjectsInactive.Include);
            if (_heartCache == null) _heartCache = FindAnyObjectByType<HeartLight>(FindObjectsInactive.Include);
        }
        if (typeof(T) == typeof(PlayerSurroundLight)) return _lampCache as T;
        return _heartCache as T;
    }
}
