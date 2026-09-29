using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MARCA DE COMPÁS (mejora de la tienda): el corazón del punto de mira destella (en cian) en el PRIMER beat de cada
/// compás de 4, justo en el beat. Ayuda a no perderse en el compás. Se crea solo en los niveles si lo tienes.
/// </summary>
public class BarFlash : MonoBehaviour
{
    public Color flashColor = new Color(0.35f, 0.9f, 1f, 1f);
    public int beatsPerBar = 4;

    private static BarFlash _instance;
    private long _lastBeat = long.MinValue;
    private HeartBeat _heart;

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
        if (_instance != null || PlayerProgress.GetLevel(ShopIds.BarFlash) <= 0) return;
        if (FindAnyObjectByType<BeatManager>() == null) return;
        new GameObject("BarFlash").AddComponent<BarFlash>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        BeatManager bm = BeatManager.Instance;
        if (PlayerProgress.GetLevel(ShopIds.BarFlash) <= 0) return;   // (suspendido en la pelea del jefe)
        if (bm == null || !bm.IsPlaying || bm.ScheduledStartDSP <= 0.0 || bm.BeatDuration <= 0f) return;
        if (_heart == null) _heart = FindAnyObjectByType<HeartBeat>();

        if (TimeRewinder.IsRewinding) return;
        long beat = (long)System.Math.Floor((AudioSettings.dspTime - bm.ScheduledStartDSP) / bm.BeatDuration);
        if (beat == _lastBeat) return;
        _lastBeat = beat;
        if (beat >= 0 && beatsPerBar > 0 && beat % beatsPerBar == 0 && _heart != null) _heart.Flash(flashColor);
    }
}
