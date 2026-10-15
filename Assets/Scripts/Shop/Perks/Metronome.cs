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
    private MetronomeClicks _clicks;
    private long   _lastScheduledBeat = long.MinValue;
    private bool   _on;
    private float  _toastT;

    public static bool Owned => PlayerProgress.GetLevel(ShopIds.Metronome) > 0;

    // ── Aparición automática ──────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        AutoSpawn.Register(EnsureIfOwned, onProgressChange: true);
    }

    private static void EnsureIfOwned()
    {
        if (_instance != null || !Owned) return;
        if (BeatManager.Instance == null) return;   // solo en escenas con música (niveles)
        new GameObject("Metronome").AddComponent<Metronome>();
    }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        _on = PlayerPrefs.GetInt(PrefKey, 1) == 1;

        _clicks = new MetronomeClicks(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (Keybinds.Pressed(Keybinds.Bind.Metronome))
        {
            _on = !_on;
            PlayerPrefs.SetInt(PrefKey, _on ? 1 : 0);
            _toastT = 1.5f;
            if (!_on) _clicks.StopAll();
        }
        if (_toastT > 0f) _toastT -= Time.unscaledDeltaTime;

        BeatManager bm = BeatManager.Instance;
        if (!Owned) return;   // (p. ej. modificadores suspendidos en la pelea del jefe)
        if (!_on || bm == null || !bm.IsPlaying || !bm.HasClock || bm.BeatDuration <= 0f) return;

        // Programar el PRÓXIMO beat con un pelín de antelación (en su instante DSP exacto).
        double beatDur = bm.BeatDuration;
        double now     = AudioSettings.dspTime;
        long   next    = (long)System.Math.Floor(bm.ClockBeats) + 1;
        double at      = bm.DspAtBeat(next);
        if (TimeRewinder.IsRewinding) { _lastScheduledBeat = long.MinValue; return; }   // (reloj yendo hacia atrás)
        if (_lastScheduledBeat != long.MinValue && next < _lastScheduledBeat - 1) _lastScheduledBeat = next - 1;              // (la canción volvió atrás)
        if (next <= _lastScheduledBeat || at - now > scheduleLead) return;

        _clicks.Schedule(at, beatsPerBar > 0 && next % beatsPerBar == 0, volume);
        _lastScheduledBeat = next;
    }

    private void OnGUI()
    {
        if (HudHider.IsHidden) return;   // (con la pausa o el game over no se ve ningún aviso)
        if (_toastT <= 0f) return;
        var st = new GUIStyle { font = UIFonts.LegacyBody, fontSize = 22, alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = new Color(1f, 1f, 1f, Mathf.Clamp01(_toastT)) } };
        GUI.Label(new Rect(0f, Screen.height * 0.12f, Screen.width, 30f),
                  Keybinds.Fix(_on ? Loc.T("Metrónomo: ACTIVADO  [M]", "Metronome: ON  [M]") : Loc.T("Metrónomo: DESACTIVADO  [M]", "Metronome: OFF  [M]")), st);
    }
}
