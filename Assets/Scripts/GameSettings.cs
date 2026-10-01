using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// AJUSTES del jugador (volumen general y sensibilidad del ratón), guardados en PlayerPrefs.
/// Los usan el menú principal y el menú de pausa; PlayerMovement lee la sensibilidad.
/// </summary>
public static class GameSettings
{
    private const string VolumeKey = "MasterVolume";
    private const string SensKey   = "LookSensitivity";   // (multiplicador; 1 = la sensibilidad de siempre)

    public const float SensMin = 0.2f, SensMax = 3f;

    private static float _sens = -1f;

    public static float Volume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 1f);
        set { value = Mathf.Clamp01(value); AudioListener.volume = value; PlayerPrefs.SetFloat(VolumeKey, value); }
    }

    public static float Sensitivity
    {
        get { if (_sens < 0f) _sens = Mathf.Clamp(PlayerPrefs.GetFloat(SensKey, 1f), SensMin, SensMax); return _sens; }
        set { _sens = Mathf.Clamp(value, SensMin, SensMax); PlayerPrefs.SetFloat(SensKey, _sens); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot() => AudioListener.volume = Volume;

    /// <summary>Prepara los dos sliders (rango y valor guardado) y engancha sus cambios.</summary>
    public static void BindSliders(Slider volume, Slider sensitivity, VolumePreview preview)
    {
        if (volume != null)
        {
            volume.minValue = 0f; volume.maxValue = 1f;
            volume.SetValueWithoutNotify(Volume);
            volume.onValueChanged.AddListener(v => { Volume = v; preview?.Ping(); });
        }
        if (sensitivity != null)
        {
            sensitivity.minValue = SensMin; sensitivity.maxValue = SensMax;
            sensitivity.SetValueWithoutNotify(Sensitivity);
            sensitivity.onValueChanged.AddListener(v => Sensitivity = v);
        }
    }
}

/// <summary>
/// Metrónomo de prueba: mientras se mueve el slider de volumen (y un rato después) suenan los «tics» generados por
/// código, para oír el volumen elegido. Suena aunque el juego esté en pausa (AudioListener.pause).
///   • Bpm        : tempo de los tics (el de la música de fondo).
///   • SyncTo     : (opcional) AudioSource con la música que suena: los tics caen EN sus beats.
///   • SyncOffset : segundos desde el inicio de la canción hasta su primer beat.
/// Llama a Ping() al cambiar el volumen y a Update() cada frame.
/// </summary>
public class VolumePreview
{
    public float Bpm = 120f;
    public AudioSource SyncTo;
    public float SyncOffset;
    public float Hold = 1.5f;

    private readonly GameObject _host;
    private MetronomeClicks _clicks;
    private float _lastPing = -10f;
    private double _next = -1, _last = -1;
    private long _count;

    public VolumePreview(GameObject host) { _host = host; }

    public void Ping() => _lastPing = Time.unscaledTime;

    private double Beat => 60.0 / Mathf.Max(30f, Bpm);

    private bool Synced => SyncTo != null && SyncTo.clip != null && SyncTo.isPlaying;

    /// <summary>Instante DSP en el que la canción sincronizada estaría en su beat 0.</summary>
    private double Anchor(double now) => now - SyncTo.timeSamples / (double)SyncTo.clip.frequency + SyncOffset;

    /// <summary>Primer beat (sincronizado o libre) a partir de 'after'.</summary>
    private double NextBeat(double after, double now)
    {
        if (!Synced) return _last < 0 ? after : _last + Beat;
        double a = Anchor(now);
        return a + System.Math.Ceiling((after - a) / Beat) * Beat;
    }

    public void Update()
    {
        if (Time.unscaledTime - _lastPing >= Hold) { _next = _last = -1; _count = 0; return; }

        double now = AudioSettings.dspTime;
        if (_next < now) _next = NextBeat(now + 0.05, now);       // empieza en el próximo beat
        if (_next - now < 0.1)                                    // programar el siguiente tic
        {
            if (_clicks == null) { _clicks = new MetronomeClicks(_host); _clicks.IgnoreListenerPause = true; }
            long index = Synced ? (long)System.Math.Round((_next - Anchor(now)) / Beat) : _count++;
            bool accent = index % 4 == 0;
            _clicks.Schedule(_next, accent, 0.8f);
            _last = _next;
            _next = NextBeat(_last + Beat * 0.5, now);
        }
    }
}
