using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AUTO-LATIDO (mejora de la tienda, 4 niveles): un latido ACERTADO automático cada 8 / 4 / 2 / 1 beats, justo en
/// el beat. Devuelve sangre como un acierto «Good» (en modo azul suma azul) y se ve: la mano pulsa y el corazón del
/// punto de mira destella en morado. No toca la ventana de acierto del jugador: tus clics cuentan igual que siempre.
/// Se crea solo en los niveles si lo tienes (también si lo compras en la tienda del freeroam).
/// </summary>
public class AutoBeater : MonoBehaviour
{
    [Tooltip("Color del destello del corazón en cada auto-latido.")]
    public Color flashColor = new Color(0.7f, 0.4f, 1f, 1f);

    private static AutoBeater _instance;
    private long _lastBeat = long.MinValue;
    private HeartBeat _heart;
    private HandAnimationController _hand;
    private GameOverManager _gameOver;
    private PlayerMovement _pm;

    public static int Level => PlayerProgress.GetLevel(ShopIds.AutoBeater);

    /// <summary>Cada cuántos beats late solo (8, 4, 2, 1). 0 = no comprado.</summary>
    public static int Every
    {
        get
        {
            switch (Level)
            {
                case 1:  return 8;
                case 2:  return 4;
                case 3:  return 2;
                default: return Level >= 4 ? 1 : 0;
            }
        }
    }

    // ── Aparición automática ──────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        AutoSpawn.Register(EnsureIfOwned, onProgressChange: true);
    }

    private static void EnsureIfOwned()
    {
        if (_instance != null || Every <= 0) return;
        if (BeatManager.Instance == null || FindAnyObjectByType<EnergyManager>() == null) return;
        new GameObject("AutoBeater").AddComponent<AutoBeater>();
    }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    private void Start()
    {
        _heart    = FindAnyObjectByType<HeartBeat>();
        _hand     = FindAnyObjectByType<HandAnimationController>();
        _gameOver = FindAnyObjectByType<GameOverManager>();
        _pm       = FindAnyObjectByType<PlayerMovement>();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// ¿Hay que IGNORAR un clic del jugador ahora? Sí si el beat más cercano es uno de los que da el Auto-latido:
    /// ese latido ya lo pone él, y si el jugador también pulsaba, la animación de la mano/corazón se reiniciaba a
    /// la vez dos veces y se veía rota. (El clic ni suma ni cuenta como fallo.)
    /// </summary>
    public static bool SuppressPlayerClickNow()
    {
        if (_instance == null) return false;
        int every = Every;
        if (every <= 0 || ContractRunner.AutoBeatsBlocked) return false;
        BeatManager   bm = BeatManager.Instance;
        EnergyManager em = EnergyManager.Instance;
        if (bm == null || !bm.IsPlaying || !bm.HasClock || bm.BeatDuration <= 0f) return false;
        if (em == null || em.godMode) return false;
        if (_instance._pm != null && _instance._pm.IsFreeroam) return false;
        if (_instance._gameOver != null && _instance._gameOver.IsDead) return false;

        double b = bm.ClockBeats;
        long nearest = (long)System.Math.Round(b);
        return nearest > 0 && nearest % every == 0;
    }

    private void Update()
    {
        int every = Every;
        BeatManager   bm = BeatManager.Instance;
        EnergyManager em = EnergyManager.Instance;
        if (every <= 0 || bm == null || em == null || !bm.IsPlaying || !bm.HasClock || bm.BeatDuration <= 0f)
            return;
        if (em.godMode) return;
        if (ContractRunner.AutoBeatsBlocked) { _lastBeat = long.MinValue; return; }   // (contrato «Contrarreloj»)
        if (TimeRewinder.IsRewinding) { _lastBeat = long.MinValue; return; }   // (no late mientras se rebobina)                                   // en God Mode ya se juega solo
        if (_gameOver != null && _gameOver.IsDead) return;
        if (_pm != null && _pm.IsFreeroam) return;

        long beat = (long)System.Math.Floor(bm.ClockBeats);
        if (beat == _lastBeat) return;
        bool first = _lastBeat == long.MinValue;
        _lastBeat = beat;
        if (first || beat < 0 || beat % every != 0) return;       // (no dispara en el primer frame)

        // ¡Latido automático!
        if (em.IsBlueMode) em.HitBlue();
        else               em.HitRed(TimingResult.Good);

        if (_heart != null) _heart.Flash(flashColor);
        if (_hand  != null && !HandAnimationController.IsHandHeld) _hand.PlayPress();
    }
}
