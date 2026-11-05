using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Experimento de sincronización para la memoria. Compara, fotograma a fotograma, cuatro formas de saber en qué
/// segundo va la canción en Unity.
///   1. Reloj DSP (AudioSettings.dspTime menos el instante de inicio programado), que es el que usa el juego (BeatManager).
///   2. AudioSource.time.
///   3. AudioSource.timeSamples dividido entre la frecuencia, que es lo mismo, pero en muestras.
///   4. Reloj de fotogramas, que suma Time.deltaTime en cada fotograma, como haría un juego que no usa el reloj de audio.
/// Para cada uno se guarda la diferencia con el reloj DSP y si se ha quedado congelado, es decir, si tiene el mismo
/// valor que en el fotograma anterior. Al terminar, se escribe un CSV y un resumen en la consola.
///
/// Uso. Desde el menú "Tools/Debug: Medir relojes" se activa (aparece marcado) y se añade solo en cada nivel. También
/// se puede añadir a mano a cualquier objeto. Empieza cuando arranca la canción y termina a los 'maxSeconds' o al salir
/// del modo Play. Mientras mide, el jugador es invulnerable y está en modo Dios, por lo que nada lo mata, la energía no
/// se acaba y la canción no se corta, así que basta con dejarlo correr.
/// El archivo se llama "Sincronizacion_Relojes_&lt;escena&gt;_&lt;tipo de carga&gt;_&lt;fecha-hora&gt;.csv" y se crea uno por medición,
/// sin sobrescribir los anteriores. Durante la medición no se debe pausar el juego ni activar el tutorial.
/// </summary>
public class ClockComparisonLogger : MonoBehaviour
{
    [SerializeField] private float maxSeconds = 120f;
    [Tooltip("Principio del nombre del archivo; se le añaden la escena y la fecha-hora.")]
    [SerializeField] private string fileName = "Sincronizacion_Relojes";
    [Tooltip("Mientras mide, nada mata al jugador (para que la canción suene entera sin tener que jugar).")]
    [SerializeField] private bool invulnerable = true;
    private bool _tutorialWarned;
    private bool _prevGodMode, _godModeSet;

#if UNITY_EDITOR
    private const string PrefKey = "ClockLogger.On";
    private const string MenuPath = "Tools/Debug: Medir relojes";

    [UnityEditor.MenuItem(MenuPath)]
    private static void Toggle()
    {
        bool on = !UnityEditor.EditorPrefs.GetBool(PrefKey, false);
        UnityEditor.EditorPrefs.SetBool(PrefKey, on);
        UnityEditor.Menu.SetChecked(MenuPath, on);
        Debug.Log(on ? "[Relojes] Medición ACTIVADA: se añade sola en cada nivel." : "[Relojes] Medición desactivada.");
    }

    [UnityEditor.MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        UnityEditor.Menu.SetChecked(MenuPath, UnityEditor.EditorPrefs.GetBool(PrefKey, false));
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (!UnityEditor.EditorPrefs.GetBool(PrefKey, false) || !s.name.StartsWith("Level")) return;
        if (FindAnyObjectByType<ClockComparisonLogger>() != null) return;
        new GameObject("ClockComparisonLogger").AddComponent<ClockComparisonLogger>();
    }
#endif

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
            if (invulnerable)
            {
                GameOverManager.DebugInvulnerable = true;
                EnergyManager em = EnergyManager.Instance;   // modo Dios: la energía no se gasta ni se acaba
                if (em != null) { _prevGodMode = em.godMode; em.godMode = true; _godModeSet = true; }
            }
            Debug.Log($"[Relojes] Midiendo {maxSeconds:0} s en «{SceneManager.GetActiveScene().name}»…");
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

        if (TutorialGate.Busy && !_tutorialWarned)
        {
            _tutorialWarned = true;
            Debug.LogWarning("[Relojes] El tutorial está controlando el reloj: esta medición NO es válida. Desactiva el tutorial (Ajustes → TUTORIAL: NO).");
        }

        if (dsp >= maxSeconds) Finish();
    }

    private void OnDisable() { if (_running && !_done) Finish(); }
    private void Awake() => StartSequenceManager.ForceSkipIntro = true;   // en la medición no se reproduce la intro del nivel
    private void OnDestroy()
    {
        StartSequenceManager.ForceSkipIntro = false;
        RestoreProtection();
    }

    private void RestoreProtection()
    {
        if (!invulnerable) return;
        GameOverManager.DebugInvulnerable = false;
        if (_godModeSet && EnergyManager.Instance != null) EnergyManager.Instance.godMode = _prevGodMode;
        _godModeSet = false;
    }

    private void Finish()
    {
        _done = true;
        RestoreProtection();
        string dir = Application.isEditor ? Path.GetDirectoryName(Application.dataPath) : Application.persistentDataPath;
        string load = _bm != null && _bm.CurrentSource != null && _bm.CurrentSource.clip != null ? _bm.CurrentSource.clip.loadType.ToString() : "SinClip";
        string outName = $"{Path.GetFileNameWithoutExtension(fileName)}_{SceneManager.GetActiveScene().name}_{load}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(dir, outName);
        try { File.WriteAllText(path, _csv.ToString()); }
        catch (IOException e) { Debug.LogWarning($"[Relojes] No se pudo guardar el CSV: {e.Message}"); return; }

        var sb = new StringBuilder($"[Relojes] {_frames} fotogramas medidos. CSV: {path}\n");
        sb.AppendLine("Reloj | error medio | error máximo | error al final | fotogramas congelado");
        for (int i = 0; i < 4; i++)
            sb.AppendLine($"{Names[i]} | {_sumAbsErr[i] / _frames:0.00} ms | {_maxAbsErr[i]:0.00} ms | " +
                          $"{_lastErr[i]:+0.00;-0.00} ms | {100.0 * _frozen[i] / _frames:0.0} %");
        Debug.Log(sb.ToString());
    }
}
