using UnityEngine;

/// <summary>
/// Sonido de CRISTAL ROTO (escudo azul que se rompe). Si no se da un clip, se genera uno por código:
/// un chasquido de ruido brillante + tintineos agudos que caen y se apagan.
/// </summary>
public static class GlassShatterSfx
{
    private static AudioClip _generated;
    private static AudioSource _source;

    public static void Play(AudioClip clip, float volume)
    {
        if (clip == null) clip = _generated ??= Generate();
        if (_source == null)
        {
            var go = new GameObject("GlassShatterSfx");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.priority = 32;
        }
        _source.PlayOneShot(clip, volume);
    }

    private static AudioClip Generate()
    {
        int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        const float seconds = 0.9f;
        int n = Mathf.RoundToInt(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(1234);

        // 1. Chasquido inicial: ruido «brillante» (diferencia de muestras = paso alto) con caída rápida.
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            float bright = white - prev; prev = white;
            data[i] += bright * 0.55f * Mathf.Exp(-t * 28f);
        }

        // 2. Tintineos: trozos de cristal que suenan agudos y caen escalonados.
        for (int k = 0; k < 22; k++)
        {
            float start = (float)(rng.NextDouble() * 0.45) * (k < 6 ? 0.15f : 1f);
            float freq  = 2500f + (float)rng.NextDouble() * 5500f;
            float amp   = 0.12f + (float)rng.NextDouble() * 0.18f;
            float decay = 18f + (float)rng.NextDouble() * 30f;
            int s0 = Mathf.RoundToInt(start * rate);
            for (int i = s0; i < n; i++)
            {
                float t = (i - s0) / (float)rate;
                float env = Mathf.Exp(-t * decay);
                if (env < 0.001f) break;
                data[i] += amp * env * Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Sin(2f * Mathf.PI * freq * 1.5f * t);
            }
        }

        // Normalizar y suavizar el final.
        float peak = 0.0001f;
        for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
        for (int i = 0; i < n; i++)
        {
            float fade = Mathf.Clamp01((n - i) / (rate * 0.05f));
            data[i] = data[i] / peak * 0.9f * fade;
        }

        var clip = AudioClip.Create("GlassShatter", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
