using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CONTRATOS EN JUEGO. Se crea solo en los niveles (escenas con BeatManager y jugador):
///   • Cuenta saltos, dashes, slides, wallruns y entradas en modo azul (RunStats).
///   • Aplica las REGLAS de los contratos firmados mientras se juega (fuera del freeroam):
///       – «Al compás»: salto, dash, slide y remo solo en el beat.
///       – «A pelo»: sin mejoras ni consumibles de la tienda.
///   • Muestra a la izquierda los contratos firmados con su progreso en directo.
///   • Al llegar a la meta, LevelEndManager llama a EvaluateLevelEnd(): los cumplidos quedan por cobrar.
/// Solo cuentan los contratos firmados DEL NIVEL ACTUAL (cada contrato tiene una versión por nivel). En el nivel 3
/// cuentan durante todo el nivel, también en la pelea del jefe.
/// </summary>
public class ContractRunner : MonoBehaviour
{
    private static ContractRunner _instance;

    /// <summary>True mientras un contrato prohíbe los latidos automáticos (Auto-latido). Lo consulta AutoBeater.</summary>
    public static bool AutoBeatsBlocked { get; private set; }

    private PlayerMovement _pm;
    private EnergyManager  _em;

    // Valores originales de las reglas que se tocan (para devolverlos)
    private bool _origJump, _origDash, _origSlide;
    private readonly Dictionary<KayakController, bool> _origPaddle = new Dictionary<KayakController, bool>();
    private bool _suspendedByUs;

    // HUD
    private CanvasGroup _hudGroup;
    private TextMeshProUGUI _hudText;
    private float _hudRefresh;

    // ── Aparición automática ──────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();

    private static void Ensure()
    {
        if (_instance != null) return;
        if (FindAnyObjectByType<BeatManager>() == null || FindAnyObjectByType<PlayerMovement>() == null) return;
        new GameObject("ContractRunner").AddComponent<ContractRunner>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();
        _em = EnergyManager.Instance != null ? EnergyManager.Instance : FindAnyObjectByType<EnergyManager>();

        if (_pm != null)
        {
            _origJump  = _pm.jumpRequiresBeat;
            _origDash  = _pm.dashRequiresBeat;
            _origSlide = _pm.slideRequiresBeat;
            _pm.OnJumpExecuted?.AddListener(RunStats.Jump);
            _pm.OnDashStarted?.AddListener(OnDash);
            _pm.OnSlideStarted?.AddListener(RunStats.Slide);
            _pm.OnWallrunStarted?.AddListener(RunStats.Wallrun);
        }
        if (_em != null) _em.OnBlueModeEntered?.AddListener(OnBlue);
        foreach (KayakController k in FindObjectsByType<KayakController>()) _origPaddle[k] = k.paddleRequiresBeat;

        BuildHud();
    }

    private void OnDash(Vector3 _) => RunStats.Dash();
    private void OnBlue(float _)   => RunStats.BlueEntered();

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_pm != null)
        {
            _pm.OnJumpExecuted?.RemoveListener(RunStats.Jump);
            _pm.OnDashStarted?.RemoveListener(OnDash);
            _pm.OnSlideStarted?.RemoveListener(RunStats.Slide);
            _pm.OnWallrunStarted?.RemoveListener(RunStats.Wallrun);
        }
        if (_em != null) _em.OnBlueModeEntered?.RemoveListener(OnBlue);
        if (_suspendedByUs) PlayerProgress.Suspended = false;
        BeatManager.HalfBeatMoves = false;
        AutoBeatsBlocked = false;
    }

    // ── Cumplidos a mitad de nivel ────────────────────────────────────────────

    private float _midCheckT;
    private string _bannerText;
    private float  _bannerT;
    private TMPro.TextMeshProUGUI _banner;

    private void CheckMidLevel()
    {
        _midCheckT -= Time.deltaTime;
        if (_midCheckT > 0f) return;
        _midCheckT = 0.2f;
        var now = new LevelResult { runCoins = LevelCoins.RunCoins, songBeats = ContractCatalog.SongBeatsNow() };
        foreach (ContractDef c in ContractCatalog.ActiveHere())
        {
            if (!c.midLevel || c.check == null) continue;
            bool ok;
            try { ok = c.check(now); } catch { ok = false; }
            if (!ok) continue;
            PlayerProgress.CompleteContract(c.id);   // (se cobra en el puesto de contratos)
            ShowBanner(Loc.T($"¡CONTRATO CUMPLIDO!  «{c.title}»\n<size=70%>Cobra tus <color=#FFC83A>{c.reward} monedas</color> en el puesto de contratos</size>",
                             $"CONTRACT COMPLETE!  «{c.title}»\n<size=70%>Claim your <color=#FFC83A>{c.reward} coins</color> at the contract stand</size>"));
            Debug.Log($"[Contratos] Cumplido a mitad de nivel «{c.title}» (pendiente de cobrar: {c.reward}).");
        }
    }

    private void ShowBanner(string text)
    {
        _bannerText = _bannerT > 0f && !string.IsNullOrEmpty(_bannerText) ? _bannerText + "\n" + text : text;
        _bannerT = 3.5f;
    }

    private void UpdateBanner()
    {
        if (_banner == null) return;
        if (_bannerT > 0f) _bannerT -= Time.unscaledDeltaTime;
        _banner.text  = _bannerT > 0f ? _bannerText : "";
        Color col = _banner.color; col.a = Mathf.Clamp01(_bannerT / 0.5f); _banner.color = col;
        if (_bannerT <= 0f) _bannerText = null;
    }

    // ── Reglas ────────────────────────────────────────────────────────────────

    /// <summary>Reglas de los contratos firmados de este nivel.</summary>
    public static ContractRule ActiveRules()
    {
        ContractRule r = ContractRule.None;
        foreach (ContractDef c in ContractCatalog.ActiveHere()) r |= c.rules;
        return r;
    }

    private bool Playing => _pm != null && !_pm.IsFreeroam;

    private void Update()
    {
        ContractRule rules = Playing ? ActiveRules() : ContractRule.None;

        // «Al compás»: movimientos solo en el beat (solo mientras se juega: en el freeroam no hay música).
        bool onBeat = (rules & ContractRule.MovesOnBeat) != 0;
        if (_pm != null)
        {
            _pm.jumpRequiresBeat  = _origJump  || onBeat;
            _pm.dashRequiresBeat  = _origDash  || onBeat;
            _pm.slideRequiresBeat = _origSlide || onBeat;
        }
        foreach (var kv in _origPaddle)
            if (kv.Key != null) kv.Key.paddleRequiresBeat = kv.Value || onBeat;
        // Con «Al compás» las acciones también valen en los MEDIOS beats.
        BeatManager.HalfBeatMoves = onBeat;
        // «Contrarreloj»: sin latidos automáticos (Auto-latido apagado); cada latido lo tiene que dar el jugador.
        AutoBeatsBlocked = (rules & ContractRule.PressLimit) != 0;

        // Contratos de "consigue N…": se cobran EN CUANTO se cumplen, sin esperar al final del nivel.
        if (Playing) CheckMidLevel();

        // «A pelo»: la tienda cuenta como si no tuvieras nada.
        bool noShop = (rules & ContractRule.NoShop) != 0;
        if (noShop != _suspendedByUs)
        {
            PlayerProgress.Suspended = noShop;
            _suspendedByUs = noShop;
        }

        // «Contrarreloj»: una pulsación de más → GAME OVER directo (sin vidas extra ni checkpoints).
        int pressLimit = ContractCatalog.PressLimitFor(ContractCatalog.CurrentLevel);
        if ((rules & ContractRule.PressLimit) != 0 && !_pressLimitHit && RunStats.HeartPresses > pressLimit)
        {
            _pressLimitHit = true;
            var gom = FindAnyObjectByType<GameOverManager>();
            if (gom != null && !gom.IsDead)
                gom.ForceGameOver(Loc.T("¡CONTRARRELOJ!", "OUT OF PRESSES!"), Loc.T($"Te has pasado de {pressLimit} pulsaciones", $"You went over {pressLimit} presses"));
        }

        UpdateHud();
    }

    private bool _pressLimitHit;

    // ── Final del nivel ───────────────────────────────────────────────────────

    /// <summary>
    /// Comprueba los contratos firmados al llegar a la meta. Los cumplidos se marcan y se PAGAN (al monedero).
    /// Devuelve los cumplidos (para enseñarlos en los resultados).
    /// </summary>
    public static List<ContractDef> EvaluateLevelEnd(LevelResult result)
    {
        var done = new List<ContractDef>();
        // (Firmados que ya no existen en el catálogo, p. ej. de una versión anterior: se quitan.)
        foreach (string id in new List<string>(PlayerProgress.ActiveContracts))
            if (ContractCatalog.Get(id) == null) PlayerProgress.SetContractActive(id, false);

        foreach (ContractDef c in ContractCatalog.ActiveHere())
        {
            bool ok;
            try { ok = c.check == null || c.check(result); }
            catch (System.Exception e) { Debug.LogException(e); ok = false; }
            if (!ok) continue;
            done.Add(c);
        }
        foreach (ContractDef c in done)
        {
            PlayerProgress.CompleteContract(c.id);   // (se cobra en el puesto de contratos)
            Debug.Log($"[Contratos] Cumplido «{c.title}» (pendiente de cobrar: {c.reward}).");
        }
        return done;
    }

    // ── HUD ───────────────────────────────────────────────────────────────────

    private void BuildHud()
    {
        Canvas c = UIKit.CreateCanvas("ContractHUDCanvas", 85, transform);
        var panel = UIKit.Panel("ContractHUD", c.transform, new Color(0f, 0f, 0f, 0.45f));
        UIKit.Place(panel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 60f), new Vector2(430f, 250f));
        panel.raycastTarget = false;
        _hudGroup = panel.gameObject.AddComponent<CanvasGroup>();
        _hudGroup.blocksRaycasts = false;
        _hudText = UIKit.Label("Text", panel.transform, "", 22f, Color.white, TextAlignmentOptions.TopLeft);
        UIKit.Stretch(_hudText.rectTransform, 16, 12, 10, 10);
        _hudText.richText = true;

        // Aviso de contrato cumplido (arriba, en el centro)
        _banner = UIKit.Label("Banner", c.transform, "", 34f, new Color(0.6f, 1f, 0.6f), TMPro.TextAlignmentOptions.Top, true);
        _banner.richText = true;
        _banner.outlineWidth = 0.2f; _banner.outlineColor = new Color(0f, 0f, 0f, 0.85f);
        UIKit.Place(_banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 140f));
    }

    private void UpdateHud()
    {
        UpdateBanner();
        if (_hudGroup == null) return;
        List<ContractDef> here = ContractCatalog.ActiveHere();
        bool show = Playing && here.Count > 0 && !ContractUI.IsOpen && !ShopUI.IsOpen;
        _hudGroup.alpha = show ? 1f : 0f;
        if (!show) return;

        _hudRefresh -= Time.unscaledDeltaTime;
        if (_hudRefresh > 0f) return;
        _hudRefresh = 0.1f;

        var sb = new System.Text.StringBuilder(Loc.T("<b><color=#FFC83A>CONTRATOS</color></b>\n", "<b><color=#FFC83A>CONTRACTS</color></b>\n"));
        foreach (ContractDef cd in here)
        {
            bool broken = cd.broken != null && cd.broken();
            string col = broken ? "#FF6B6B" : "#" + ColorUtility.ToHtmlStringRGB(ContractDef.TierColor(cd.tier));
            sb.Append($"<color={col}>•</color> <b>{cd.title}</b>");
            if (broken) sb.Append(Loc.T("  <color=#FF6B6B>(fallado)</color>", "  <color=#FF6B6B>(failed)</color>"));
            sb.Append('\n');
            string p = cd.progress != null ? cd.progress() : Loc.T("al llegar a la meta", "on reaching the goal");
            sb.Append($"<size=18><color=#{(broken ? "FF9B9B" : "CFCFCF")}>   {p}</color></size>\n");
        }
        _hudText.text = sb.ToString();
    }
}
