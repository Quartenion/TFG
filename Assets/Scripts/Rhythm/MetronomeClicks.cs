using UnityEngine;

/// <summary>
/// «Tics» de metrónomo generados por código y programados con el reloj de audio (PlayScheduled), así que suenan
/// EXACTAMENTE en el instante DSP pedido, sin la latencia/silencio inicial de un clip de audio real.
/// Lo usan el Metronome de la tienda (durante la canción) y el 3-2-1-¡YA! de StartSequenceManager.
/// </summary>
public class MetronomeClicks
{
    private static AudioClip s_tick, s_accent;   // compartidos: se generan una sola vez

    private readonly AudioSource[] _sources;
    private int _next;

    /// <summary>Crea las AudioSources (2D) en 'host'.</summary>
    public MetronomeClicks(GameObject host, int voices = 4)
    {
        if (s_tick   == null) s_tick   = MakeClick("MetronomeTick",   1000f, 0.035f);
        if (s_accent == null) s_accent = MakeClick("MetronomeAccent", 1600f, 0.045f);

        _sources = new AudioSource[Mathf.Max(1, voices)];
        for (int i = 0; i < _sources.Length; i++)
        {
            var src = host.AddComponent<AudioSource>();
            src.playOnAwake  = false;
            src.spatialBlend = 0f;
            src.priority     = 16;
            _sources[i] = src;
        }
    }

    /// <summary>Programa un tic en el instante DSP 'at' (acento = primer tiempo del compás, más agudo).</summary>
    public void Schedule(double at, bool accent, float volume)
    {
        AudioSource src = _sources[_next];
        _next = (_next + 1) % _sources.Length;
        src.clip   = accent ? s_accent : s_tick;
        src.volume = volume * (accent ? 1f : 0.8f);
        src.PlayScheduled(at);
    }

    /// <summary>Si es true, suenan aunque el juego esté en pausa (AudioListener.pause).</summary>
    public bool IgnoreListenerPause { set { foreach (var s in _sources) if (s != null) s.ignoreListenerPause = value; } }

    /// <summary>Corta los tics que estén sonando o programados.</summary>
    public void StopAll()
    {
        foreach (var s in _sources) if (s != null) s.Stop();
    }

    /// <summary>«Tic» corto: seno con ataque de 1 ms y caída rápida.</summary>
    private static AudioClip MakeClick(string name, float freq, float seconds)
    {
        int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        int n = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-t * 90f) * Mathf.Clamp01(i / (rate * 0.001f));
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
