using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ESTADÍSTICAS DE LA PARTIDA ACTUAL de un nivel (para los contratos): latidos, fallos, Perfect, rachas,
/// movimientos, modo azul y ayudas usadas.
///   • Se ponen a cero al cargar un nivel (o reiniciarlo tras morir).
///   • Si se reaparece en un CHECKPOINT (el nivel se recarga) se conservan: es la misma partida.
///   • Solo cuenta lo que pasa jugando (no en el freeroam de antes de la puerta).
/// </summary>
public static class RunStats
{
    public static int HeartPresses { get; private set; }   // latidos (clics al corazón / remadas), acertados o no
    public static int Hits         { get; private set; }
    public static int Misses       { get; private set; }
    public static int Perfects     { get; private set; }
    public static int Streak       { get; private set; }   // aciertos seguidos ahora mismo
    public static int BestStreak   { get; private set; }
    public static int Jumps        { get; private set; }
    public static int Dashes       { get; private set; }
    public static int Slides       { get; private set; }
    public static int Wallruns     { get; private set; }
    public static int BlueEntries  { get; private set; }
    public static int HelpsUsed    { get; private set; }   // vidas extra, Adrenalina, Paracaídas, checkpoints, rebobinados

    public static event System.Action OnChanged;

    // ── Registro (lo llaman RhythmInput, KayakController, GameOverManager, TimeRewinder, ContractRunner) ──

    /// <summary>Un latido del jugador (clic real al corazón o remada en el kayak).</summary>
    public static void HeartPress(TimingResult r)
    {
        if (!Counting) return;
        HeartPresses++;
        if (r == TimingResult.Miss)
        {
            Misses++;
            Streak = 0;
        }
        else
        {
            Hits++;
            if (r == TimingResult.Perfect) Perfects++;
            Streak++;
            if (Streak > BestStreak) BestStreak = Streak;
        }
        OnChanged?.Invoke();
    }

    public static void Jump()       { if (Counting) { Jumps++;       OnChanged?.Invoke(); } }
    public static void Dash()       { if (Counting) { Dashes++;      OnChanged?.Invoke(); } }
    public static void Slide()      { if (Counting) { Slides++;      OnChanged?.Invoke(); } }
    public static void Wallrun()    { if (Counting) { Wallruns++;    OnChanged?.Invoke(); } }
    public static void BlueEntered(){ if (Counting) { BlueEntries++; OnChanged?.Invoke(); } }

    /// <summary>Se ha usado una ayuda de la tienda para no morir.</summary>
    public static void HelpUsed() { HelpsUsed++; OnChanged?.Invoke(); }

    /// <summary>¿Se está jugando el nivel? (música sonando y fuera del freeroam).</summary>
    private static bool Counting
    {
        get
        {
            BeatManager bm = BeatManager.Instance;
            if (bm == null || !bm.IsPlaying) return false;
            if (_pm == null) _pm = Object.FindAnyObjectByType<PlayerMovement>();
            return _pm == null || !_pm.IsFreeroam;
        }
    }
    private static PlayerMovement _pm;

    public static void Reset()
    {
        HeartPresses = Hits = Misses = Perfects = Streak = BestStreak = 0;
        Jumps = Dashes = Slides = Wallruns = BlueEntries = HelpsUsed = 0;
        OnChanged?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        Reset();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (m != LoadSceneMode.Single) return;
        _pm = null;
        if (CheckpointSystem.PendingRespawn) return;   // reaparición en checkpoint: misma partida
        Reset();
    }
}
