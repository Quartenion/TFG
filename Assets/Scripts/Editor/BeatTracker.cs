using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BEAT TRACKING (detección automática del tempo y del primer beat de una canción). Algoritmo:
///   1. Mezcla a mono y baja a ~11 kHz.
///   2. ENVOLVENTE DE ATAQUES (onset strength): flujo espectral = suma de las subidas del espectro log-magnitud
///      entre tramas (FFT de 1024 con ventana de Hann, salto de 256 → ~43 tramas/s). Se le resta su media local
///      (1 s), se rectifica, se normaliza y se suaviza un poco.
///   3. TEMPO: para cada BPM candidato (50-210, paso 0,25) se mide la energía media de la envolvente justo en las
///      posiciones de un tren de beats a ese tempo (probando 24 fases y quedándose con la mejor): «peine».
///   4. OCTAVA: el máximo suele caer en la mitad del tempo real (un peine lento acierta en los beats fuertes).
///      Se miran sus múltiplos métricos (×4/3, ×3/2, ×2, ×3) y se elige el MÁS RÁPIDO que conserve al menos el
///      80 % de la fuerza del máximo.
///   5. AJUSTE FINO: ±1 % en pasos de 0,01 BPM con 64 fases → BPM final y fase = segundos hasta el primer beat.
///   Confianza = fuerza del peine elegido / fuerza media de todos los candidatos (≈ 1 = no hay pulso claro).
/// </summary>
public static class BeatTracker
{
    public struct Result
    {
        public float bpm;            // tempo detectado
        public float firstBeat;      // desfase del primer beat respecto al inicio (s, entre −½ y +½ beat; ≈ 0 = empieza en beat)
        public float confidence;     // fuerza relativa del pulso (≥ 2 = claro; < 1.5 = dudoso)
        public float strongestBpm;   // máximo del peine antes de elegir octava (para explicar la decisión)
        public float analyzedSeconds;
    }

    public const float MinBpm = 50f, MaxBpm = 210f;
    private const int Win = 1024, Hop = 256;
    private const float OctaveAlpha = 0.8f;

    /// <summary>Analiza las muestras (entrelazadas, 'channels' canales). 'maxSeconds' limita el tramo analizado.</summary>
    public static Result Analyze(float[] samples, int channels, int sampleRate, float maxSeconds = 120f)
    {
        float[] env = OnsetEnvelope(samples, channels, sampleRate, maxSeconds, out float fps, out float seconds, out float frameCenter);

        // 3. Peine en toda la rejilla de tempos.
        var grid = new List<float>();
        for (float b = MinBpm; b <= MaxBpm + 0.001f; b += 0.25f) grid.Add(b);
        var S = new float[grid.Count];
        int i0 = 0;
        double sum = 0;
        for (int i = 0; i < grid.Count; i++)
        {
            S[i] = Comb(env, fps, grid[i], 24, out _);
            sum += S[i];
            if (S[i] > S[i0]) i0 = i;
        }
        float b0 = grid[i0], s0 = S[i0];

        // 4. Octava: el múltiplo métrico más rápido que mantenga ≥ 80 % de la fuerza.
        float best = b0;
        foreach (float mult in new[] { 4f / 3f, 1.5f, 2f, 3f })
        {
            float target = b0 * mult;
            int j = -1;
            for (int i = 0; i < grid.Count; i++)
                if (grid[i] >= target * 0.98f && grid[i] <= target * 1.02f && (j < 0 || S[i] > S[j])) j = i;
            if (j >= 0 && S[j] >= OctaveAlpha * s0 && grid[j] > best) best = grid[j];
        }

        // 5. Ajuste fino.
        float fb = best, fs = -1f, fo = 0f;
        for (float bb = best * 0.99f; bb <= best * 1.01f; bb += 0.01f)
        {
            float v = Comb(env, fps, bb, 64, out float o);
            if (v > fs) { fs = v; fb = bb; fo = o; }
        }

        float mean = (float)(sum / grid.Count);
        // Primer beat: la fase está en tramas desde el INICIO de la ventana; el ataque cae, de media, en su centro
        // (+½ ventana). Se expresa entre −½ y +½ beat (≈ 0 = la canción empieza justo en un beat).
        float beatSec = 60f / fb;
        float first = Mathf.Repeat(fo / fps + frameCenter, beatSec);
        if (first > beatSec * 0.5f) first -= beatSec;
        return new Result
        {
            bpm = fb,
            firstBeat = first,
            confidence = mean > 0f ? fs / mean : 0f,
            strongestBpm = b0,
            analyzedSeconds = seconds,
        };
    }

    // ── Envolvente de ataques ────────────────────────────────────────────────

    private static float[] OnsetEnvelope(float[] samples, int channels, int sampleRate, float maxSeconds,
                                         out float fps, out float seconds, out float frameCenter)
    {
        channels = Mathf.Max(1, channels);
        int frames = samples.Length / channels;
        int maxFrames = Mathf.Min(frames, Mathf.RoundToInt(maxSeconds * sampleRate));
        seconds = maxFrames / (float)sampleRate;

        // Mono + diezmado a ~11 kHz (media de 'dec' muestras).
        int dec = Mathf.Max(1, Mathf.RoundToInt(sampleRate / 11025f));
        int n = maxFrames / dec;
        var mono = new float[n];
        for (int i = 0; i < n; i++)
        {
            float acc = 0f;
            for (int k = 0; k < dec; k++)
            {
                int f = (i * dec + k) * channels;
                for (int c = 0; c < channels; c++) acc += samples[f + c];
            }
            mono[i] = acc / (dec * channels);
        }
        float sr2 = sampleRate / (float)dec;
        fps = sr2 / Hop;
        frameCenter = Win * 0.5f / sr2;

        // Flujo espectral.
        int count = n >= Win ? 1 + (n - Win) / Hop : 0;
        var flux = new float[Mathf.Max(1, count)];
        var window = new float[Win];
        for (int i = 0; i < Win; i++) window[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / Win);   // Hann (np.hanning ≈)
        var re = new double[Win];
        var im = new double[Win];
        var prev = new float[Win / 2 + 1];
        var cur = new float[Win / 2 + 1];
        for (int t = 0; t < count; t++)
        {
            int start = t * Hop;
            for (int i = 0; i < Win; i++) { re[i] = mono[start + i] * window[i]; im[i] = 0; }
            Fft(re, im);
            float fl = 0f;
            for (int k = 0; k <= Win / 2; k++)
            {
                float mag = (float)Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                cur[k] = Mathf.Log(1f + 100f * mag);
                if (t > 0) fl += Mathf.Max(0f, cur[k] - prev[k]);
            }
            flux[t] = fl;
            var tmp = prev; prev = cur; cur = tmp;
        }

        // Quitar la media local (1 s), rectificar, normalizar y suavizar.
        int w = Mathf.Max(1, (int)fps);
        var env = new float[flux.Length];
        double run = 0;
        int half = w / 2;
        // media móvil centrada (como np.convolve 'same' con ventana uniforme)
        var prefix = new double[flux.Length + 1];
        for (int i = 0; i < flux.Length; i++) prefix[i + 1] = prefix[i] + flux[i];
        for (int i = 0; i < flux.Length; i++)
        {
            int a = i - half, b = a + w;
            int ca = Mathf.Clamp(a, 0, flux.Length), cb = Mathf.Clamp(b, 0, flux.Length);
            double ma = (prefix[cb] - prefix[ca]) / w;
            env[i] = Mathf.Max(0f, flux[i] - (float)ma);
            run += env[i];
        }
        double mean = run / env.Length, var = 0;
        for (int i = 0; i < env.Length; i++) var += (env[i] - mean) * (env[i] - mean);
        float std = (float)Math.Sqrt(var / Math.Max(1, env.Length)) + 1e-9f;
        var smooth = new float[env.Length];
        for (int i = 0; i < env.Length; i++)
        {
            float l = i > 0 ? env[i - 1] : 0f, r = i + 1 < env.Length ? env[i + 1] : 0f;
            smooth[i] = (0.25f * l + 0.5f * env[i] + 0.25f * r) / std;
        }
        return smooth;
    }

    /// <summary>Fuerza media de la envolvente en un tren de beats a 'bpm' (mejor de 'phases' fases). 'offset' en tramas.</summary>
    private static float Comb(float[] env, float fps, float bpm, int phases, out float offset)
    {
        float per = fps * 60f / bpm;
        int n = (int)((env.Length - 1) / per) - 1;
        offset = 0f;
        if (n <= 1) return 0f;
        float best = -1f;
        for (int p = 0; p < phases; p++)
        {
            float o = per * p / phases;
            double acc = 0;
            for (int k = 0; k < n; k++)
            {
                float pos = o + per * k;
                int i = (int)pos;
                float fr = pos - i;
                acc += env[i] * (1f - fr) + env[i + 1] * fr;
            }
            float v = (float)(acc / n);
            if (v > best) { best = v; offset = o; }
        }
        return best;
    }

    /// <summary>FFT compleja in situ (radix-2, longitud potencia de 2).</summary>
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    double t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                }
            }
        }
    }
}
