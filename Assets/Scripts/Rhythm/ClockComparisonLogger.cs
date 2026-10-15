using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// EXPERIMENTO DE SINCRONIZACIÓN (para la memoria). Compara, fotograma a fotograma, cuatro formas de saber
/// «por qué segundo va la canción» en Unity:
///   1. Reloj DSP (AudioSettings.dspTime − instante de inicio programado) → el que usa el juego (BeatManager).
///   2. AudioSource.time.
///   3. AudioSource.timeSamples / frecuencia (lo mismo, pero en muestras).
///   4. Reloj de fotogramas: sumar Time.deltaTime cada frame (lo que haría un juego «ingenuo»).
/// Para cada uno guarda la diferencia con el reloj DSP y si se ha quedado CONGELADO (mismo valor que en el
/// fotograma anterior). Al terminar escribe un CSV y un resumen en la consola.
///
/// USO: pon este componente en cualquier objeto de un nivel y juega. Empieza solo cuando arranca la canción y
/// termina a los 'maxSeconds' (o al salir del modo Play). No hagas pausa durante la medición.
/// </summary>
public class ClockComparisonLogger : MonoBehaviour
{
    [SerializeField] private float maxSeconds = 120f;
    [SerializeField] private string fileName = "Sincronizacion_Relojes.csv";

    private BeatManager _bm;
    private bool _running, _done;
    private double _frameClock;                 // reloj de fotogramas (suma de deltaTime)
    private double _prevDsp, _prevAudio, _prevSamples;
    private readonly StringBuilder _csv = new StringBuilder();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Estadísticas: [0] DSP, [1] AudioSource.time, [2] timeSamples, [3] fotogramas
    private static readonly string[] Names = { "DSP", "AudioSource.time", "timeSamples", "Fotogramas (deltaTime)" };
    private readonly double[] _sumAbsErr = new double[4];
    private readonly double[] _maxAbsErr = new double[4];
    private readonly double[] _lastErr = new double[4];
    private readonly int[] _frozen = new int[4];
    private int _frames;

    private void Update()
    {
        if (_done) return;
        if (_bm == null) { _bm = BeatManager.Instance; if (_bm == null) return; }

        AudioSource src = _bm.CurrentSource;
        if (src == null || src.clip == null || !_bm.HasClock || !_bm.IsPlaying) return;
        if (Time.timeScale == 0f) return;   // en pausa no se mide

        double dsp = _bm.ClockSeconds;
        if (!_running)
        {
            if (dsp <= 0.0) return;          // la canción aún no ha empezado
            _running = true;
            _frameClock = dsp;               // los cuatro relojes empiezan alineados
            _prevDsp = _prevAudio = _prevSamples = -1.0;
            _csv.AppendLine("frame;delta_ms;dsp_s;audio_time_s;samples_s;frames_s;err_audio_ms;err_samples_ms;err_frames_ms");
        }
        else _frameClock += Time.deltaTime;

        double audio   = src.time;
        double samples = src.timeSamples / (double)src.clip.frequency;
        double[] values = { dsp, audio, samples, _frameClock };
        double[] prevs  = { _prevDsp, _prevAudio, _prevSamples, -1.0 };

        _frames++;
        for (int i = 0; i < 4; i++)
        {
            double err = (values[i] - dsp) * 1000.0;   // ms respecto al DSP
            _lastErr[i] = err;
            _sumAbsErr[i] += System.Math.Abs(err);
            if (System.Math.Abs(err) > _maxAbsErr[i]) _maxAbsErr[i] = System.Math.Abs(err);
            if (values[i] == prevs[i]) _frozen[i]++;    // no ha avanzado desde el fotograma anterior
        }
        _prevDsp = dsp; _prevAudio = audio; _prevSamples = samples;

        _csv.AppendLine(string.Join(";",
            _frames.ToString(Inv), (Time.deltaTime * 1000f).ToString("0.00", Inv),
            dsp.ToString("0.0000", Inv), audio.ToString("0.0000", Inv), samples.ToString("0.0000", Inv),
            _frameClock.ToString("0.0000", Inv),
            _lastErr[1].ToString("0.00", Inv), _lastErr[2].ToString("0.00", Inv), _lastErr[3].ToString("0.00", Inv)));

        if (dsp >= maxSeconds) Finish();
    }

    private void OnDisable() { if (_running && !_done) Finish(); }

    private void Finish()
    {
        _done = true;
        string dir = Application.isEditor ? Path.GetDirectoryName(Application.dataPath) : Application.persistentDataPath;
        string path = Path.Combine(dir, fileName);
        File.WriteAllText(path, _csv.ToString());

        var sb = new StringBuilder($"[Relojes] {_frames} fotogramas medidos. CSV: {path}\n");
        sb.AppendLine("Reloj | error medio | error máximo | error al final | fotogramas congelado");
        for (int i = 0; i < 4; i++)
            sb.AppendLine($"{Names[i]} | {_sumAbsErr[i] / _frames:0.00} ms | {_maxAbsErr[i]:0.00} ms | " +
                          $"{_lastErr[i]:+0.00;-0.00} ms | {100.0 * _frozen[i] / _frames:0.0} %");
        Debug.Log(sb.ToString());
    }
}
