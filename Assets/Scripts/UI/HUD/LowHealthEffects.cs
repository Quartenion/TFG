using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Efectos de pantalla en dos casos opuestos:
///  - LOW HEALTH (rojo): cuando la energía baja de _redActivationThreshold.
///    Bordes de la pantalla en rojo, música ahogada (filtro de graves), LATIDO al ritmo y la cámara «late».
///  - HIGH BONUS (azul): en modo azul, cuando la sangre azul supera _blueActivationThreshold.
///    Bordes en azul + latido + pulso de cámara (sin ahogar la música).
///
/// Se conecta SOLO: se suscribe al beat del BeatManager (antes dependía de un evento del prefab Managers que apuntaba
/// a este componente del prefab Player; esa referencia entre prefabs se perdía y el efecto dejó de funcionar), crea su
/// propio viñeteado en pantalla (no necesita el Volume de post-procesado) y busca el filtro de la música él mismo.
/// </summary>
public class LowHealthEffects : MonoBehaviour
{
    [Header("Activar / desactivar efectos (también en Play)")]
    [Tooltip("Bordes de la pantalla en ROJO cuando queda poca sangre.")]
    [SerializeField] private bool _redEdges = true;
    [Tooltip("Bordes de la pantalla en AZUL con la jeringuilla azul llena (bonus).")]
    [SerializeField] private bool _blueEdges = true;
    [Tooltip("Música apagada (filtro de graves y volumen más bajo) cuando queda poca sangre.")]
    [SerializeField] private bool _musicMuffle = true;
    [Tooltip("La cámara «late» (golpe de FOV) en cada beat.")]
    [SerializeField] private bool _cameraPulse = true;
    [Tooltip("Sonido de latido del corazón en cada beat.")]
    [SerializeField] private bool _heartbeatSound = true;

    [Header("Threshold rojo (low health)")]
    [Tooltip("Por debajo de este porcentaje de energía, los efectos rojos empiezan")]
    [Range(0f, 1f)]
    [SerializeField] private float _redActivationThreshold = 0.4f;

    [Header("Threshold azul (high bonus)")]
    [Tooltip("Por encima de este porcentaje de sangre azul (en bonus), los efectos azules empiezan")]
    [Range(0f, 1f)]
    [SerializeField] private float _blueActivationThreshold = 0.6f;

    [Header("Bordes de la pantalla")]
    [Tooltip("(Opcional) Volume de URP con Vignette. Si está vacío se usa el viñeteado propio (recomendado).")]
    [SerializeField] private Volume _volume;
    [SerializeField] private Color _redColor = new Color(0.65f, 0.0f, 0.0f, 1f);
    [SerializeField] private Color _blueColor = new Color(0.0f, 0.55f, 1.0f, 1f);
    [Range(0f, 1f)]
    [SerializeField] private float _maxVignetteIntensity = 0.55f;
    [Range(0f, 1f)]
    [SerializeField] private float _vignetteSmoothness = 0.4f;
    [Tooltip("Opacidad máxima de los bordes del viñeteado propio.")]
    [Range(0f, 1f)]
    [SerializeField] private float _overlayMaxAlpha = 0.75f;
    [Tooltip("Cuánto se intensifican los bordes en cada latido (0 = nada).")]
    [Range(0f, 1f)]
    [SerializeField] private float _overlayBeatFlash = 0.35f;

    [Header("Bordes AZULES (bonus) — más discretos")]
    [Tooltip("Opacidad máxima de los bordes en azul. Menor que la del rojo: el azul es un premio, no un peligro, y " +
             "con el nivel oscuro unos bordes azules intensos deslumbraban y distraían.")]
    [Range(0f, 1f)]
    [SerializeField] private float _blueOverlayMaxAlpha = 0.32f;
    [Tooltip("Brillo del color azul de los bordes (1 = el color tal cual; menos = más oscuro).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float _blueBrightness = 0.55f;
    [Tooltip("Cuánto se intensifican los bordes azules en cada latido, respecto al rojo (1 = igual).")]
    [Range(0f, 1f)]
    [SerializeField] private float _blueFlashScale = 0.4f;

    [Header("Filtro low-pass de la musica (solo en rojo)")]
    [Tooltip("Vacío = se usa (o se añade) el de la fuente de música del BeatManager.")]
    [SerializeField] private AudioLowPassFilter _musicLowPass;
    [SerializeField] private float _normalCutoff = 22000f;
    [SerializeField] private float _lowHealthCutoff = 900f;
    [Range(0f, 1f)]
    [SerializeField] private float _musicDuckAmount = 0.4f;
    [Tooltip("(Opcional) Si se asigna, además se BAJA su volumen en rojo.")]
    [SerializeField] private AudioSource _musicSource;

    [Header("Pulso de camara (latido)")]
    [SerializeField] private Camera _camera;
    [SerializeField] private float _maxPulseFOV = 5f;
    [Tooltip("En modo azul (sangre azul / sobrecarga) el latido de la cámara es más suave: × este factor.")]
    [SerializeField] private float _bluePulseScale = 0.35f;
    [SerializeField] private float _pulseInTime = 0.06f;
    [SerializeField] private float _pulseOutTime = 0.22f;

    [Header("Audio del corazon")]
    [SerializeField] private AudioSource _heartbeatSource;
    [SerializeField] private AudioClip _heartbeatClip;
    [Tooltip("Clip distinto para los pulsos en bonus azul (opcional). Si null, usa el de rojo.")]
    [SerializeField] private AudioClip _bonusPulseClip;
    [Tooltip("Volumen maximo del latido. Por encima de 1 puede clipear, ajusta segun el clip.")]
    [Range(0f, 5f)]
    [SerializeField] private float _heartbeatMaxVolume = 2.5f;

    [Header("Suavizado")]
    [SerializeField] private float _lerpSpeed = 4f;

    private Vignette _vignette;
    private float _baseFOV;
    private float _baseMusicVolume;
    private float _currentStrength;   // 0..1, intensidad del efecto activo
    private bool  _currentIsBlue;     // que color esta activo
    private Color _currentColor;
    private Coroutine _pulseRoutine;

    private BeatManager _subscribedTo;
    private RawImage _overlay;
    private GameObject _overlayRoot;   // canvas del viñeteado propio
    private float _flash;             // brillo extra del latido (se apaga solo)
    private bool _autoLowPass;        // filtro buscado/añadido por código

    private void Start()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera != null) _baseFOV = _camera.fieldOfView;
        if (_musicSource != null) _baseMusicVolume = _musicSource.volume;

        if (_volume != null && _volume.profile != null)
        {
            _volume.profile.TryGet(out _vignette);
            if (_vignette != null)
            {
                _vignette.intensity.overrideState = true;
                _vignette.color.overrideState = true;
                _vignette.smoothness.overrideState = true;
                _vignette.smoothness.value = _vignetteSmoothness;
            }
        }
        if (_vignette == null) BuildOverlay();

        _currentColor = _redColor;
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (_overlay != null) _overlay.color = new Color(_currentColor.r, _currentColor.g, _currentColor.b, 0f);
        if (_pulseRoutine != null) { StopCoroutine(_pulseRoutine); _pulseRoutine = null; }
        SetPulse(0f);
    }

    private void OnEnable() { if (_overlay != null) Subscribe(); }

    private void OnDestroy()
    {
        Unsubscribe();
        if (_overlayRoot != null) Destroy(_overlayRoot);   // (al cerrar la escena puede haberse destruido ya)
    }

    // ── Beat ──────────────────────────────────────────────────────────────────

    private void Subscribe()
    {
        BeatManager bm = BeatManager.Instance;
        if (bm == null || bm == _subscribedTo) return;
        Unsubscribe();
        bm.OnBeat.AddListener(OnBeat);
        _subscribedTo = bm;
    }

    private void Unsubscribe()
    {
        if (_subscribedTo != null) _subscribedTo.OnBeat.RemoveListener(OnBeat);
        _subscribedTo = null;
    }

    // ── Por frame ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_subscribedTo == null) Subscribe();   // (el BeatManager puede aparecer después)
        var em = EnergyManager.Instance;

        // Calculamos los dos targets posibles
        float redTarget = 0f;
        float blueTarget = 0f;

        if (em != null)
        {
            if (em.IsBlueMode)
            {
                // Solo el azul puede activarse en modo bonus
                float syringe = em.SyringePercent;
                if (syringe > _blueActivationThreshold && _blueActivationThreshold < 1f)
                    blueTarget = (syringe - _blueActivationThreshold) / (1f - _blueActivationThreshold);
            }
            else
            {
                // Solo el rojo puede activarse en modo normal
                float energy = em.EnergyPercent;
                if (energy < _redActivationThreshold && _redActivationThreshold > 0f)
                    redTarget = 1f - (energy / _redActivationThreshold);
            }
        }

        // Elegimos cual de los dos efectos (no se solapan, son exclusivos)
        bool useBlue = blueTarget > redTarget;
        float targetStrength = Mathf.Clamp01(useBlue ? blueTarget : redTarget);

        _currentIsBlue = useBlue;
        _currentStrength = Mathf.Lerp(_currentStrength, targetStrength, Time.deltaTime * _lerpSpeed);
        _blueMix = Mathf.Lerp(_blueMix, useBlue ? 1f : 0f, Time.deltaTime * _lerpSpeed);

        // Color objetivo (lerp tambien para que la transicion azul/rojo sea suave)
        Color targetColor = useBlue ? _blueColor : _redColor;
        _currentColor = Color.Lerp(_currentColor, targetColor, Time.deltaTime * _lerpSpeed);

        _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime / Mathf.Max(0.05f, _pulseInTime + _pulseOutTime));
        ApplyEffects();
    }

    private float _blueMix;   // 0 = rojo · 1 = azul (suavizado, como el color)

    private void ApplyEffects()
    {
        float flash = _flash * Mathf.Lerp(1f, _blueFlashScale, _blueMix);
        // Bordes: cada color solo si su casilla está activada.
        float edgesOn = Mathf.Lerp(_redEdges ? 1f : 0f, _blueEdges ? 1f : 0f, _blueMix);
        float shown = Mathf.Clamp01(_currentStrength * (1f + flash)) * edgesOn;
        float maxA  = Mathf.Lerp(_overlayMaxAlpha, _blueOverlayMaxAlpha, _blueMix);
        Color col   = _currentColor * Mathf.Lerp(1f, _blueBrightness, _blueMix);

        // Bordes
        if (_vignette != null)
        {
            _vignette.intensity.value = shown * _maxVignetteIntensity * Mathf.Lerp(1f, _blueOverlayMaxAlpha / Mathf.Max(0.01f, _overlayMaxAlpha), _blueMix);
            _vignette.color.value = col;
            _vignette.smoothness.value = _vignetteSmoothness;
        }
        if (_overlay != null)
            _overlay.color = new Color(col.r, col.g, col.b, shown * maxA);

        // Low pass y bajada de volumen solo en rojo (peligro real). En azul la música suena limpia.
        float audioStrength = _currentIsBlue || !_musicMuffle ? 0f : _currentStrength;

        AudioLowPassFilter lp = MusicLowPass();
        if (lp != null) lp.cutoffFrequency = Mathf.Lerp(_normalCutoff, _lowHealthCutoff, audioStrength);

        if (_musicSource != null && _musicDuckAmount > 0f)
            _musicSource.volume = _baseMusicVolume * (1f - audioStrength * _musicDuckAmount);
    }

    /// <summary>Filtro de graves de la música: el asignado o el de la fuente actual del BeatManager (se añade si falta).</summary>
    private AudioLowPassFilter MusicLowPass()
    {
        if (_musicLowPass != null && !_autoLowPass) return _musicLowPass;
        AudioSource src = BeatManager.Instance != null ? BeatManager.Instance.CurrentSource : null;
        if (src == null) return _musicLowPass;
        if (_musicLowPass == null || _musicLowPass.gameObject != src.gameObject)
        {
            if (_musicLowPass != null) _musicLowPass.cutoffFrequency = _normalCutoff;   // (la fuente cambió: jefe)
            _musicLowPass = src.GetComponent<AudioLowPassFilter>();
            if (_musicLowPass == null)
            {
                _musicLowPass = src.gameObject.AddComponent<AudioLowPassFilter>();
                _musicLowPass.cutoffFrequency = _normalCutoff;
            }
            _autoLowPass = true;
        }
        return _musicLowPass;
    }

    /// <summary>En cada beat: latido + la cámara «late» + los bordes se intensifican.</summary>
    public void OnBeat()
    {
        if (!isActiveAndEnabled || _currentStrength < 0.05f) return;

        _flash = _overlayBeatFlash;
        if (_cameraPulse)
        {
            if (_pulseRoutine != null) StopCoroutine(_pulseRoutine);
            _pulseRoutine = StartCoroutine(PulseRoutine());
        }

        if (_heartbeatSound && _heartbeatSource != null)
        {
            AudioClip clipToPlay = (_currentIsBlue && _bonusPulseClip != null) ? _bonusPulseClip : _heartbeatClip;
            if (clipToPlay != null) _heartbeatSource.PlayOneShot(clipToPlay, _currentStrength * _heartbeatMaxVolume);
        }
    }

    private float _pulseOffset;

    private void SetPulse(float offset)
    {
        _pulseOffset = offset;
        if (CameraDashEffect.Active) CameraDashEffect.ExternalFovOffset = offset;
        else if (_camera != null) _camera.fieldOfView = _baseFOV + offset;
    }

    private IEnumerator PulseRoutine()
    {
        if (_camera == null) yield break;

        // El pulso se SUMA al FOV (lo aplica CameraDashEffect), así no pisa el golpe de FOV del dash ni de la Q.
        float pulseDelta = -_currentStrength * _maxPulseFOV * (_currentIsBlue ? _bluePulseScale : 1f);
        float start = _pulseOffset;

        float t = 0f;
        while (t < _pulseInTime)
        {
            t += Time.deltaTime;
            SetPulse(Mathf.Lerp(start, pulseDelta, t / _pulseInTime));
            yield return null;
        }

        t = 0f;
        while (t < _pulseOutTime)
        {
            t += Time.deltaTime;
            SetPulse(Mathf.Lerp(pulseDelta, 0f, t / _pulseOutTime));
            yield return null;
        }

        SetPulse(0f);
        _pulseRoutine = null;
    }

    // ── Viñeteado propio (UI a pantalla completa) ─────────────────────────────

    private void BuildOverlay()
    {
        var canvasGo = new GameObject("LowHealthVignette", typeof(Canvas));
        _overlayRoot = canvasGo;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -10;   // (debajo del HUD)

        var img = new GameObject("Bordes", typeof(RectTransform)).AddComponent<RawImage>();
        img.transform.SetParent(canvasGo.transform, false);
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        img.raycastTarget = false;
        img.texture = MakeVignetteTexture(_vignetteSmoothness);
        img.color = new Color(_redColor.r, _redColor.g, _redColor.b, 0f);
        _overlay = img;
    }

    /// <summary>Degradado radial: transparente en el centro, opaco en los bordes ('smooth' = lo ancho de la transición).</summary>
    private static Texture2D MakeVignetteTexture(float smooth)
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LowHealthVignette" };
        var px = new Color[S * S];
        float inner = Mathf.Lerp(0.75f, 0.35f, Mathf.Clamp01(smooth));   // donde empieza a teñirse
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);                      // 0 centro · 1 mitad de un borde · 1.41 esquina
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, 1.2f, d));
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }
}
