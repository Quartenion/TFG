using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// METRÓNOMO (mejora de la tienda): un «tic» en cada beat mientras suena la canción del nivel, con el primer beat
/// de cada compás (cada 4) más agudo. Los tics se programan con el reloj de audio (PlayScheduled) en el mismo
/// instante DSP que los beats del BeatManager, así que van clavados a la música.
///   • Se crea solo en los niveles si lo has comprado (también si lo compras en la tienda del freeroam).
///   • [M] lo enciende / apaga (se recuerda entre partidas).
/// </summary>
public class Metronome : MonoBehaviour
{
    private const string PrefKey = "MetronomeOn";

    [Tooltip("Volumen de los tics.")]
    [Range(0f, 1f)] public float volume = 0.55f;
    [Tooltip("Beats por compás (el primero suena más agudo).")]
    public int beatsPerBar = 4;
    [Tooltip("Adelanto (s) con el que se programa cada tic.")]
    public float scheduleLead = 0.15f;

    private static Metronome _instance;
    private AudioSource[] _sources;
    private int    _nextSource;
    private long   _lastScheduledBeat = long.MinValue;
    private AudioClip _tick, _accent;
    private bool   _on;
    private float  _toastT;

    public static bool Owned => PlayerProgress.GetLevel(ShopIds.Metronome) > 0;

    // ── Aparición automática ──────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        PlayerProgress.OnChanged -= EnsureIfOwned;
        PlayerProgress.OnChanged += EnsureIfOwned;
        EnsureIfOwned();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => EnsureIfOwned();

    private static void EnsureIfOwned()
    {
        if (_instance != null || !Owned) return;
        if (FindAnyObjectByType<BeatManager>() == null) return;   // solo en escenas con música (niveles)
        new GameObject("Metronome").AddComponent<Metronome>();
    }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        _on = PlayerPrefs.GetInt(PrefKey, 1) == 1;

        _tick   = MakeClick("MetronomeTick",   1000f, 0.035f);
        _accent = MakeClick("MetronomeAccent", 1600f, 0.045f);

        _sources = new AudioSource[4];
        for (int i = 0; i < _sources.Length; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake  = false;
            src.spatialBlend = 0f;
            src.priority     = 16;
            _sources[i] = src;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            _on = !_on;
            PlayerPrefs.SetInt(PrefKey, _on ? 1 : 0);
            _toastT = 1.5f;
            if (!_on) foreach (var s in _sources) s.Stop();
        }
        if (_toastT > 0f) _toastT -= Time.unscaledDeltaTime;

        BeatManager bm = BeatManager.Instance;
        if (!Owned) return;   // (p. ej. modificadores suspendidos en la pelea del jefe)
        if (!_on || bm == null || !bm.IsPlaying || bm.ScheduledStartDSP <= 0.0 || bm.BeatDuration <= 0f) return;

        // Programar el PRÓXIMO beat con un pelín de antelación (en su instante DSP exacto).
        double beatDur = bm.BeatDuration;
        double now     = AudioSettings.dspTime;
        long   next    = (long)System.Math.Floor((now - bm.ScheduledStartDSP) / beatDur) + 1;
        double at      = bm.ScheduledStartDSP + next * beatDur;
        if (TimeRewinder.IsRewinding) { _lastScheduledBeat = long.MinValue; return; }   // (reloj yendo hacia atrás)
        if (_lastScheduledBeat != long.MinValue && next < _lastScheduledBeat - 1) _lastScheduledBeat = next - 1;              // (la canción volvió atrás)
        if (next <= _lastScheduledBeat || at - now > scheduleLead) return;

        AudioSource src = _sources[_nextSource];
        _nextSource = (_nextSource + 1) % _sources.Length;
        bool accent = beatsPerBar > 0 && next % beatsPerBar == 0;
        src.clip   = accent ? _accent : _tick;
        src.volume = volume * (accent ? 1f : 0.8f);
        src.PlayScheduled(at);
        _lastScheduledBeat = next;
    }

    private void OnGUI()
    {
        if (_toastT <= 0f) return;
        var st = new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = new Color(1f, 1f, 1f, Mathf.Clamp01(_toastT)) } };
        GUI.Label(new Rect(0f, Screen.height * 0.12f, Screen.width, 30f),
                  _on ? "Metrónomo: ACTIVADO  [M]" : "Metrónomo: DESACTIVADO  [M]", st);
    }

    /// <summary>«Tic» corto generado por código: seno con caída rápida.</summary>
    private static AudioClip MakeClick(string name, float freq, float seconds)
    {
        int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        int n = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-t * 90f) * Mathf.Clamp01(i / (rate * 0.001f));   // ataque de 1 ms + caída
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
