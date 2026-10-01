using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// PUESTO DE CONTRATOS (versión simple, mismo estilo que la tienda). Se abre con ContractUI.Open() desde el
/// puesto físico del freeroam (ContractStand).
///
///   CONTRATOS                               Monedas · Firmados · Por cobrar
///   [Contratos][Cobrados]                  ┌──────── ficha ────────┐
///   ┌───── rejilla (casillas) ─────┐       │ Nombre / Descripción  │
///   │ [ ] [ ] [ ] [ ] [ ] [ ]      │       │ [Elegir / Cobrar…]    │
///   │ ...                          │       │ Hoja de firma         │
///   └──────────────────────────────┘       │ [Firmar] [Borrar]     │
///               [Salir (Esc)]              └───────────────────────┘
///
///   • ELEGIR → va a la hoja de firma. Firma con el ratón en el recuadro y pulsa FIRMAR: quedan activos
///              (máx. ContractCatalog.MaxActive a la vez).
///   • RESCINDIR → quitas uno firmado.   • COBRAR → cumplido: te llevas las monedas.
/// Tamaños en «LAYOUT»; colores y letra en SimpleUI.cs.
/// </summary>
public class ContractUI : MonoBehaviour
{
    // ── LAYOUT (cámbialo aquí) ────────────────────────────────────────────────
    private const float PanelW = 1800f, PanelH = 900f;
    private const float Pad = 30f;
    private const float HeaderH = 80f;
    private const float TabH = 50f;
    private const float FooterH = 90f;
    private const float DetailW = 460f;
    private const float Gap = 10f;
    private const int   Columns = 6;          // cada pestaña ocupa la mitad (3 columnas)
    private const float PadH = 150f;          // alto del recuadro de firma
    private const float MinInk = 120f;        // trazo mínimo para que cuente como firma

    // ── Abrir / cerrar ────────────────────────────────────────────────────────
    public static bool IsOpen => _instance != null;
    private static ContractUI _instance;
    private static int   _closedFrame = -10;
    private static float _closedTime  = -10f;

    /// <summary>¿Se acaba de cerrar? (para que el Esc de cerrar no abra la pausa).</summary>
    public static bool ClosedRecently => Time.frameCount - _closedFrame <= 5 || Time.unscaledTime - _closedTime < 0.6f;

    private Action _onClosed;
    private CursorLockMode _prevLock;
    private bool _prevVisible;

    public static ContractUI Open(Action onClosed = null)
    {
        if (_instance != null) return _instance;
        _instance = new GameObject("ContractUI").AddComponent<ContractUI>();
        _instance._onClosed = onClosed;
        return _instance;
    }

    public void Close()
    {
        CursorRestorer.Restore(_prevLock, _prevVisible);
        if (_instance == this) _instance = null;
        _closedFrame = Time.frameCount;
        _closedTime  = Time.unscaledTime;
        Destroy(gameObject);
        Action cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }

    // ── Estado ────────────────────────────────────────────────────────────────

    private class Entry { public ContractDef def; public Image bg; public TextMeshProUGUI state; }

    private const int ClaimedTab = 1;         // 0 = Contratos (por hacer / firmados / por cobrar), 1 = Cobrados
    private float _cell;
    private Entry _sel;
    private readonly List<Entry> _entries = new List<Entry>();
    private readonly List<Image> _tabs = new List<Image>();
    private readonly List<string> _chosen = new List<string>();   // elegidos, pendientes de firmar

    private TextMeshProUGUI _wallet, _toast;
    private RectTransform _grid;
    private TextMeshProUGUI _dName, _dInfo, _dDesc, _dState, _dActionLabel, _sheet, _signLabel;
    private Button _dAction, _sign;
    private SignaturePad _pad;
    private float _toastT;

    private void Awake()
    {
        _prevLock    = Cursor.lockState;
        _prevVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        UIKit.EnsureEventSystem();
        Build();
        PlayerProgress.OnChanged += Refresh;
        ShowTab(0);
    }

    private void OnDestroy()
    {
        PlayerProgress.OnChanged -= Refresh;
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (_toastT > 0f) { _toastT -= Time.unscaledDeltaTime; if (_toastT <= 0f) _toast.text = ""; }
    }

    // ── Construcción ──────────────────────────────────────────────────────────

    private void Build()
    {
        Canvas canvas = UIKit.CreateCanvas("ContractCanvas", 1000, transform);
        Stretch(Box("Back", canvas.transform, Back).rectTransform, 0f, 0f, 0f, 0f);

        var panel = Box("Panel", canvas.transform, PanelCol);
        Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));
        Transform p = panel.transform;

        var title = Lbl("Title", p, Loc.T("CONTRATOS", "CONTRACTS"), FontBig, TextAlignmentOptions.Left);
        PlaceTopLeft(title.rectTransform, Pad, Pad, 400f, 50f);
        _wallet = Lbl("Wallet", p, "", FontMid, TextAlignmentOptions.Right);
        PlaceTopRight(_wallet.rectTransform, Pad, Pad, 1000f, 50f);

        // Pestañas + rejilla (izquierda)
        float leftW = PanelW - Pad * 3f - DetailW;
        float top = Pad + HeaderH;
        _cell = (leftW - Gap * (Columns - 1)) / Columns;
        string[] names = { Loc.T("Contratos", "Contracts"), Loc.T("Cobrados", "Claimed") };
        int perTab = Columns / names.Length;
        float tabW = _cell * perTab + Gap * (perTab - 1);
        for (int i = 0; i < names.Length; i++)
        {
            int tab = i;
            var b = Btn("Tab" + i, p, names[i], out _);
            PlaceTopLeft((RectTransform)b.transform, Pad + i * (tabW + Gap), top, tabW, TabH);
            b.onClick.AddListener(() => ShowTab(tab));
            _tabs.Add(b.image);
        }
        var gridBox = UIKit.Node("GridBox", p);
        float gridTop = top + TabH + Gap;
        PlaceTopLeft(gridBox, Pad, gridTop, leftW, PanelH - gridTop - FooterH);
        _grid = MakeGrid(gridBox, _cell, Gap, Columns);

        // Ficha + hoja de firma (derecha)
        var detail = Box("Detail", p, BoxCol);
        PlaceTopRight(detail.rectTransform, Pad, top, DetailW, PanelH - top - FooterH);
        BuildDetail(detail.transform);

        // Pie
        var exit = Btn("Exit", p, Loc.T("Salir (Esc)", "Exit (Esc)"), out _);
        Place((RectTransform)exit.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(300f, 50f), new Vector2(0.5f, 0f));
        exit.onClick.AddListener(Close);
        _toast = Lbl("Toast", p, "", FontMid, TextAlignmentOptions.Left);
        PlaceBottomLeft(_toast.rectTransform, Pad, 20f, 650f, 50f);
    }

    private void BuildDetail(Transform d)
    {
        float w = DetailW - 40f;
        _dName = Lbl("Name", d, "", FontBig, TextAlignmentOptions.TopLeft);
        PlaceTopLeft(_dName.rectTransform, 20f, 20f, w, 50f);
        _dInfo = Lbl("Info", d, "", FontSmall, TextAlignmentOptions.TopLeft);
        _dInfo.color = DimText;
        PlaceTopLeft(_dInfo.rectTransform, 20f, 75f, w, 50f);
        _dDesc = Lbl("Desc", d, "", FontMid, TextAlignmentOptions.TopLeft);
        Stretch(_dDesc.rectTransform, 20f, 20f, 130f, 400f);
        _dState = Lbl("State", d, "", FontMid, TextAlignmentOptions.Left);
        PlaceBottomLeft(_dState.rectTransform, 20f, 355f, w, 40f);
        _dAction = Btn("Action", d, "", out _dActionLabel);
        PlaceBottomLeft((RectTransform)_dAction.transform, 20f, 295f, w, 55f);
        _dAction.onClick.AddListener(OnAction);

        // Hoja de firma
        _sheet = Lbl("Sheet", d, "", FontSmall, TextAlignmentOptions.BottomLeft);
        PlaceBottomLeft(_sheet.rectTransform, 20f, 90f + PadH + 10f, w, 40f);
        _pad = SignaturePad.Create(d, new Vector2(w, PadH));
        _pad.paperColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        _pad.inkColor   = Color.black;
        _pad.Clear();
        PlaceBottomLeft((RectTransform)_pad.transform, 20f, 90f, w, PadH);
        _pad.OnChanged += Refresh;

        float half = (w - Gap) / 2f;
        _sign = Btn("Sign", d, Loc.T("Firmar", "Sign"), out _signLabel);
        PlaceBottomLeft((RectTransform)_sign.transform, 20f, 20f, half, 60f);
        _sign.onClick.AddListener(Sign);
        var clear = Btn("Clear", d, Loc.T("Borrar firma", "Clear signature"), out _);
        PlaceBottomLeft((RectTransform)clear.transform, 20f + half + Gap, 20f, half, 60f);
        clear.onClick.AddListener(() => _pad.Clear());
    }

    // ── Pestañas y rejilla ────────────────────────────────────────────────────

    private void ShowTab(int tab)
    {
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].color = i == tab ? SelCol : BtnCol;
        foreach (Transform c in _grid) Destroy(c.gameObject);
        _entries.Clear();

        // «Contratos»: los de ESTE nivel + los cumplidos de otros niveles que falten por cobrar.
        // «Cobrados»: todos los ya cobrados (de cualquier nivel).
        int lv = ContractCatalog.CurrentLevel;
        for (int tier = 1; tier <= 3; tier++)
            foreach (ContractDef d in ContractCatalog.All)
            {
                if (d.tier != tier) continue;
                bool claimable = PlayerProgress.IsContractClaimable(d.id);
                bool claimed = PlayerProgress.IsContractDone(d.id) && !claimable;
                bool show = tab == ClaimedTab ? claimed : !claimed && (d.level == lv || claimable);
                if (show) AddEntry(d);
            }

        _sel = _entries.Count > 0 ? _entries[0] : null;
        Refresh();
    }

    private void AddEntry(ContractDef d)
    {
        var e = new Entry { def = d, bg = Box("Item", _grid, ItemCol) };
        e.bg.gameObject.AddComponent<Button>().onClick.AddListener(() => { _sel = e; Refresh(); });
        string title = d.level == ContractCatalog.CurrentLevel ? d.title : Loc.T($"{d.title}\n(nivel {d.level})", $"{d.title}\n(level {d.level})");
        var label = Lbl("Name", e.bg.transform, title, FontSmall + 2f, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 10f, 10f, 10f, 45f);
        e.state = Lbl("State", e.bg.transform, "", FontSmall, TextAlignmentOptions.Center);
        e.state.color = DimText;
        Stretch(e.state.rectTransform, 6f, 6f, _cell - 45f, 8f);
        _entries.Add(e);
    }

    // ── Refresco ──────────────────────────────────────────────────────────────

    /// <summary>Firmados de este nivel (el máximo es por nivel).</summary>
    private static int ActiveCount => ContractCatalog.ActiveHere().Count;

    private static string State(ContractDef d, bool chosen)
    {
        if (PlayerProgress.IsContractClaimable(d.id)) return Loc.T("¡Cumplido! Cobrar", "Done! Claim");
        if (PlayerProgress.IsContractDone(d.id))      return Loc.T("Cobrado", "Claimed");
        if (PlayerProgress.IsContractActive(d.id))    return Loc.T("Firmado", "Signed");
        if (chosen)                                   return Loc.T("Elegido", "Chosen");
        return "+" + d.reward;
    }

    private void Refresh()
    {
        _chosen.RemoveAll(id => PlayerProgress.IsContractActive(id) || PlayerProgress.IsContractDone(id));
        int active = ActiveCount, toClaim = 0;
        foreach (ContractDef d in ContractCatalog.All) if (PlayerProgress.IsContractClaimable(d.id)) toClaim++;
        _wallet.text = Loc.T($"Nivel {ContractCatalog.CurrentLevel}     Monedas: {PlayerProgress.Coins}     Firmados: {active}/{ContractCatalog.MaxActive}     Por cobrar: {toClaim}",
                             $"Level {ContractCatalog.CurrentLevel}     Coins: {PlayerProgress.Coins}     Signed: {active}/{ContractCatalog.MaxActive}     To claim: {toClaim}");

        foreach (Entry e in _entries)
        {
            bool claimed = PlayerProgress.IsContractDone(e.def.id) && !PlayerProgress.IsContractClaimable(e.def.id);
            e.bg.color = e == _sel ? SelCol : claimed ? LockCol : ItemCol;
            e.state.text = State(e.def, _chosen.Contains(e.def.id));
        }
        RefreshDetail(active);
        RefreshSheet();
    }

    private void RefreshDetail(int active)
    {
        _dAction.gameObject.SetActive(_sel != null);
        if (_sel == null) { _dName.text = _dInfo.text = _dDesc.text = _dState.text = ""; return; }

        ContractDef d = _sel.def;
        bool chosen = _chosen.Contains(d.id);
        _dName.text = d.title;
        _dInfo.text = Loc.T($"Nivel {d.level} · {ContractDef.TierName(d.tier)} · paga {d.reward} monedas", $"Level {d.level} · {ContractDef.TierName(d.tier)} · pays {d.reward} coins")
                      + (d.rules != ContractRule.None ? Loc.T(" · cambia las reglas", " · changes the rules") : "");
        _dDesc.text = StripTags(d.description) + (d.midLevel ? Loc.T("\n\nSe cumple en cuanto lo consigas, a mitad de nivel.", "\n\nIt is completed as soon as you achieve it, mid-level.") : "");
        _dState.text = State(d, chosen);

        if (PlayerProgress.IsContractClaimable(d.id)) { _dActionLabel.text = Loc.T($"Cobrar (+{d.reward})", $"Claim (+{d.reward})"); _dAction.interactable = true; }
        else if (PlayerProgress.IsContractDone(d.id)) { _dActionLabel.text = Loc.T("Cobrado", "Claimed");   _dAction.interactable = false; }
        else if (PlayerProgress.IsContractActive(d.id)) { _dActionLabel.text = Loc.T("Rescindir", "Cancel"); _dAction.interactable = true; }
        else if (chosen)                              { _dActionLabel.text = Loc.T("Quitar de la hoja", "Remove from sheet"); _dAction.interactable = true; }
        else
        {
            _dActionLabel.text = Loc.T("Elegir", "Choose");
            _dAction.interactable = d.level == ContractCatalog.CurrentLevel && active + _chosen.Count < ContractCatalog.MaxActive;
        }
    }

    private void RefreshSheet()
    {
        int total = 0;
        foreach (string id in _chosen) { ContractDef d = ContractCatalog.Get(id); if (d != null) total += d.reward; }
        _sheet.text = _chosen.Count == 0
            ? Loc.T("Hoja de firma: elige contratos y firma aquí.", "Signature sheet: choose contracts and sign here.")
            : Loc.T($"Hoja de firma: {_chosen.Count} elegido(s), {total} monedas en total.", $"Signature sheet: {_chosen.Count} chosen, {total} coins in total.");
        _sign.interactable = _chosen.Count > 0 && _pad.Ink >= MinInk;
        _signLabel.text = _chosen.Count > 0 ? Loc.T($"Firmar ({_chosen.Count})", $"Sign ({_chosen.Count})") : Loc.T("Firmar", "Sign");
    }

    // ── Acciones ──────────────────────────────────────────────────────────────

    private void OnAction()
    {
        if (_sel == null) return;
        ContractDef d = _sel.def;
        string id = d.id;

        if (PlayerProgress.IsContractClaimable(id))
        {
            if (PlayerProgress.ClaimContract(id, d.reward)) { Toast(Loc.T($"Cobrado «{d.title}»: +{d.reward} monedas.", $"Claimed «{d.title}»: +{d.reward} coins.")); ShowTab(0); }
            return;
        }
        if (PlayerProgress.IsContractDone(id)) return;
        if (PlayerProgress.IsContractActive(id)) { PlayerProgress.SetContractActive(id, false); Toast(Loc.T($"Has rescindido «{d.title}».", $"You cancelled «{d.title}».")); return; }
        if (_chosen.Contains(id)) { _chosen.Remove(id); Refresh(); return; }
        if (ActiveCount + _chosen.Count >= ContractCatalog.MaxActive)
        {
            Toast(Loc.T($"Solo puedes tener {ContractCatalog.MaxActive} contratos a la vez.", $"You can only have {ContractCatalog.MaxActive} contracts at a time."));
            return;
        }
        _chosen.Add(id);
        Refresh();
    }

    /// <summary>Firma la hoja: todos los elegidos pasan a estar activos.</summary>
    private void Sign()
    {
        if (_chosen.Count == 0) { Toast(Loc.T("Elige primero algún contrato.", "Choose a contract first.")); return; }
        if (_pad.Ink < MinInk)  { Toast(Loc.T("Firma en el recuadro con el ratón.", "Sign in the box with the mouse.")); return; }

        // (Copia: al activar cada uno salta PlayerProgress.OnChanged → Refresh, que limpia _chosen.)
        var toSign = new List<string>(_chosen);
        _chosen.Clear();
        int n = 0;
        foreach (string id in toSign)
        {
            if (ActiveCount >= ContractCatalog.MaxActive) break;
            PlayerProgress.SetContractActive(id, true);
            n++;
        }
        _pad.Clear();
        Toast(Loc.T($"Firmado{(n == 1 ? "" : "s")} {n} contrato{(n == 1 ? "" : "s")}. Cúmplelo{(n == 1 ? "" : "s")} en el próximo nivel.",
                    $"Signed {n} contract{(n == 1 ? "" : "s")}. Complete {(n == 1 ? "it" : "them")} in the next level."));
        Refresh();
    }

    private void Toast(string msg) { _toast.text = msg; _toastT = 2.5f; }
}
