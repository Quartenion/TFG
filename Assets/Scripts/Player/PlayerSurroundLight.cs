using UnityEngine;

/// <summary>
/// LINTERNA DE CASCO DE MINERO (solo la luz): un foco AMARILLO en la cabeza del jugador que apunta hacia donde mira,
/// más un pequeño halo alrededor (la luz que se escapa del foco). Proyecta sombras.
/// La batería «se está acabando»: de vez en cuando la luz PARPADEA, pero los parpadeos empiezan SIEMPRE EN EL BEAT
/// (y su forma dura una fracción de beat, así que van al ritmo de la canción, sea cual sea el tempo):
///   • titubeo: se apaga y enciende dos veces muy rápido,
///   • bajón: cae casi a la mitad y se recupera,
///   • apagón: se apaga un cuarto de beat.
/// A veces son rachas de 2-3 beats seguidos («se está muriendo»). Sin música (lobby) usa un pulso propio.
/// Va en el jugador (lo añade Tools/Estética/Aplicar «Dark Fantasy 80s»). Ajustable en el Inspector, también en Play.
/// </summary>
[AddComponentMenu("Player/Player Surround Light")]
public class PlayerSurroundLight : MonoBehaviour
{
    [Header("Linterna (foco del casco)")]
    public Color lampColor = new Color(1f, 0.84f, 0.42f);          // amarillo de bombilla vieja
    [Tooltip("Intensidad del foco. Súbela si se ve oscuro.")]
    public float lampIntensity = 40f;
    [Tooltip("Alcance del foco (m).")]
    public float lampRange = 50f;
    [Tooltip("Apertura total del foco (grados).")]
    [Range(20f, 150f)] public float lampAngle = 70f;
    [Tooltip("Apertura del centro brillante (grados).")]
    [Range(5f, 120f)] public float lampInnerAngle = 30f;
    [Tooltip("Posición en el casco respecto a la cámara (un poco por encima, para que se vean las sombras).")]
    public Vector3 helmetOffset = new Vector3(0f, 0.22f, 0.05f);

    [Header("Halo (luz que se escapa alrededor)")]
    public float spillIntensity = 3f;
    public float spillRange = 10f;

    [Header("Batería (parpadeos AL BEAT)")]
    [Tooltip("Probabilidad de que un beat cualquiera empiece un parpadeo.")]
    [Range(0f, 1f)] public float flickerChance = 0.1f;
    [Tooltip("Probabilidad de que el parpadeo sea una racha de 2-3 beats seguidos.")]
    [Range(0f, 1f)] public float burstChance = 0.3f;
    [Tooltip("Beats mínimos entre dos parpadeos (para que no sea molesto).")]
    public int minBeatsBetween = 4;
    [Tooltip("Pulso de reserva sin música (lobby), en segundos por «beat».")]
    public float fallbackBeatSeconds = 0.6f;

    [Header("Sombras")]
    public bool castShadows = true;
    [Range(0f, 1f)] public float shadowStrength = 0.9f;
    public float shadowNearPlane = 0.3f;

    private Light _lamp, _spill;
    private PlayerMovement _pm;
    private Transform _cam;
    private int _seed;

    // Estado del parpadeo en curso (en beats).
    private const long NoBeat = -1000000;
    private long _flickerStart = NoBeat, _lastFlickerEnd = NoBeat;
    private int  _flickerLen, _flickerType;
    private long _lastBeatChecked = long.MinValue;

    private void Start()
    {
        _pm   = GetComponent<PlayerMovement>();
        _cam  = _pm != null && _pm.playerCamera != null ? _pm.playerCamera.transform
              : Camera.main != null ? Camera.main.transform : transform;
        _seed = Random.Range(1, 100000);

        _lamp  = MakeLight("Helmet Lamp", LightType.Spot);
        _spill = MakeLight("Helmet Lamp Spill", LightType.Point);
        _spill.shadows = LightShadows.None;
    }

    // Desactivar el componente (casilla del Inspector o DarkFantasyAtmosphere) apaga también sus luces.
    private void OnEnable()  { if (_lamp != null) { _lamp.gameObject.SetActive(true);  _spill.gameObject.SetActive(true); } }
    private void OnDisable() { if (_lamp != null) { _lamp.gameObject.SetActive(false); _spill.gameObject.SetActive(false); } }

    private Light MakeLight(string name, LightType type)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_cam, false);
        var l = go.AddComponent<Light>();
        l.type = type;
        l.renderMode = LightRenderMode.ForcePixel;
        int mask = ~0;   // (sin iluminar el HUD 3D ni la UI)
        foreach (string layer in new[] { "UI", "SyringeHUD_Red", "SyringeHUD_Blue" })
        {
            int li = LayerMask.NameToLayer(layer);
            if (li >= 0) mask &= ~(1 << li);
        }
        l.cullingMask = mask;
        return l;
    }

    private void LateUpdate()
    {
        if (_lamp == null) return;

        // ── Reloj en beats (el de la música; sin música, uno propio) ──
        double beats;
        BeatManager bm = BeatManager.Instance;
        if (bm != null && bm.IsPlaying && bm.HasClock && bm.ClockSeconds >= 0.0) beats = bm.ClockBeats;
        else beats = Time.time / Mathf.Max(0.2f, fallbackBeatSeconds);
        long  beat  = (long)System.Math.Floor(beats);
        float phase = (float)(beats - beat);

        // ¿Empieza un parpadeo en este beat? (se decide una vez por beat, con una semilla fija por beat)
        if (beat != _lastBeatChecked)
        {
            if (beat < _lastBeatChecked) { _lastFlickerEnd = NoBeat; _flickerStart = NoBeat; }   // (la canción volvió atrás: rebobinado)
            _lastBeatChecked = beat;
            bool inFlicker = beat >= _flickerStart && beat < _flickerStart + _flickerLen;
            if (!inFlicker && beat - _lastFlickerEnd >= minBeatsBetween && Hash01(beat, 0) < flickerChance)
            {
                _flickerStart  = beat;
                _flickerLen    = Hash01(beat, 1) < burstChance ? 2 + (int)(Hash01(beat, 2) * 2f) : 1;
                _lastFlickerEnd = beat + _flickerLen;
            }
        }

        float k = 1f;
        if (beat >= _flickerStart && beat < _flickerStart + _flickerLen)
        {
            int type = (int)(Hash01(beat, 3) * 3f);   // cada beat de la racha puede ser distinto
            k = FlickerShape(type, phase);
        }
        // Vida: un temblor mínimo constante (bombilla con poca batería).
        k *= 1f - 0.03f * Mathf.PerlinNoise(Time.time * 9f, _seed * 0.001f);

        // ── Luces ──
        _lamp.transform.localPosition = helmetOffset;
        _lamp.transform.localRotation = Quaternion.identity;   // apunta hacia donde mira el jugador
        _lamp.color          = lampColor;
        _lamp.intensity      = lampIntensity * k;
        _lamp.range          = lampRange;
        _lamp.spotAngle      = lampAngle;
        _lamp.innerSpotAngle = Mathf.Min(lampInnerAngle, lampAngle);
        _lamp.shadows        = castShadows ? LightShadows.Soft : LightShadows.None;
        _lamp.shadowStrength = shadowStrength;
        _lamp.shadowNearPlane = shadowNearPlane;

        _spill.transform.localPosition = helmetOffset;
        _spill.color     = lampColor;
        _spill.intensity = spillIntensity * k;
        _spill.range     = spillRange;
    }

    /// <summary>Forma del parpadeo dentro de un beat (phase 0..1, 0 = justo en el beat). Devuelve el factor de luz.</summary>
    private static float FlickerShape(int type, float p)
    {
        switch (type)
        {
            case 0:   // titubeo: apagado-encendido-apagado-encendido, todo en el primer cuarto de beat
                if (p < 0.06f) return 0.05f;
                if (p < 0.11f) return 1f;
                if (p < 0.17f) return 0.08f;
                return 1f;
            case 1:   // bajón: cae de golpe y se recupera durante el beat
                return Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(p / 0.6f));
            default:  // apagón corto: un cuarto de beat a oscuras
                return p < 0.25f ? 0.03f : 1f;
        }
    }

    /// <summary>Número pseudoaleatorio estable [0,1) para un beat (el mismo beat da siempre lo mismo).</summary>
    private float Hash01(long beat, int salt)
    {
        unchecked
        {
            uint h = (uint)(beat * 73856093L) ^ (uint)(salt * 19349663) ^ (uint)(_seed * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
