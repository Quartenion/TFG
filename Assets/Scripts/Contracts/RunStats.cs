using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Estadísticas de la partida actual en un nivel, para los contratos: latidos, fallos, Perfect, rachas, movimientos,
/// modo azul y ayudas usadas.
///   - Se ponen a cero al cargar un nivel o al reiniciarlo después de morir.
///   - Si se reaparece en un punto de control (y el nivel se recarga), se conservan, ya que es la misma partida.
///   - Solo cuenta lo que pasa jugando, no en la zona de inicio antes de la puerta.
/// </summary>
public static class RunStats
{
    public static int HeartPresses { get; private set; }   // latidos (clics al corazón o remadas), acertados o no
    public static int Hits         { get; private set; }
    public static int Misses       { get; private set; }
    public static int Perfects     { get; private set; }
    public static int Streak       { get; private set; }   // aciertos seguidos en este momento
    public static int BestStreak   { get; private set; }
    public static int Jumps        { get; private set; }
    public static int Dashes       { get; private set; }
    public static int Slides       { get; private set; }
    public static int Wallruns     { get; private set; }
    public static int BlueEntries  { get; private set; }
    public static int HelpsUsed    { get; private set; }   // vidas extra, Adrenalina, Paracaídas, puntos de control y rebobinados
    /// <summary>
    /// Muertes en este nivel desde que se empezó. Se conservan al reintentar y se ponen a cero al completarlo o al cambiar
    /// de nivel. Las ayudas usadas (HelpsUsed) también se conservan entre intentos.
    /// </summary>
    public static int Deaths       { get; private set; }

    private static string _levelScene = "";
    private static bool   _levelCompleted;

    /// <summary>El jugador ha muerto (game over) en el nivel.</summary>
    public static void Death() { Deaths++; OnChanged?.Invoke(); }

    /// <summary>Nivel superado. Si se vuelve a jugar, todo empieza de cero (estadísticas, muertes, ayudas y monedas).</summary>
    public static void LevelCompleted() => _levelCompleted = true;

    public static event System.Action OnChanged;

    // -=+=- Registro (lo llaman RhythmInput, KayakController, GameOverManager, TimeRewinder y ContractRunner) -=+=-

    /// <summary>Un latido del jugador, ya sea un clic al corazón o una remada en el kayak.</summary>
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

    /// <summary>Indica si se está jugando el nivel, es decir, con la música sonando y fuera de la zona de inicio.</summary>
    private static bool Counting
    {
        get
        {
            BeatManager bm = BeatManager.Instance;
            if (bm == null || !bm.IsPlaying || LevelEndManager.Finished) return false;
            if (_pm == null) _pm = Object.FindAnyObjectByType<PlayerMovement>();
            return _pm == null || !_pm.IsFreeroam;
        }
    }
    private static PlayerMovement _pm;

    /// <summary>Pone todo a cero, en una partida o un nivel nuevos.</summary>
    public static void Reset()
    {
        Deaths = HelpsUsed = 0;
        _levelCompleted = false;
        ResetAttempt();
    }

    /// <summary>Reinicia solo lo del intento, al reintentar el mismo nivel después de morir. Las muertes y las ayudas se conservan.</summary>
    private static void ResetAttempt()
    {
        HeartPresses = Hits = Misses = Perfects = Streak = BestStreak = 0;
        Jumps = Dashes = Slides = Wallruns = BlueEntries = 0;
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
        if (CheckpointSystem.PendingRespawn) return;   // reaparición en un punto de control, es la misma partida
        // Si se reintenta el mismo nivel sin haberlo completado, es un intento nuevo y las muertes se siguen contando. Si
        // el nivel es distinto o ya se había completado, todo empieza de cero.
        if (s.name == _levelScene && !_levelCompleted) ResetAttempt();
        else { Reset(); _levelScene = s.name; }
    }
}
