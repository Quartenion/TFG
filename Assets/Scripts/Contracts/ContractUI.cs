using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
/// Lo común (abrir/cerrar, pestañas, rejilla, pie) está en GridPanel; colores y letra en SimpleUI.cs.
/// </summary>
public class ContractUI : GridPanel<ContractUI, ContractUI.Entry>
{
    // ── LAYOUT propio (el común está en GridPanel) ────────────────────────────
    private const float PadH = 150f;          // alto del recuadro de firma
    private const float MinInk = 120f;        // trazo mínimo para que cuente como firma

    // ── Estado ────────────────────────────────────────────────────────────────

    public class Entry : GridPanelCell { public ContractDef def; }

    private const int ClaimedTab = 1;         // 0 = Contratos (por hacer / firmados / por cobrar), 1 = Cobrados
    private readonly List<string> _chosen = new List<string>();   // elegidos, pendientes de firmar

    private TextMeshProUGUI _wallet;
    private TextMeshProUGUI _dName, _dInfo, _dDesc, _dState, _dActionLabel, _sheet, _signLabel;
    private Button _dAction, _sign;
    private SignaturePad _pad;

    // ── Construcción ──────────────────────────────────────────────────────────

    protected override string CanvasName => "ContractCanvas";
    protected override string Title      => Loc.T("CONTRATOS", "CONTRACTS");
    protected override float  ToastWidth => 650f;

    protected override List<string> TabNames() =>
        new List<string> { Loc.T("Contratos", "Contracts"), Loc.T("Cobrados", "Claimed") };

    protected override void BuildHeader(Transform p)
    {
        _wallet = Lbl("Wallet", p, "", FontMid, TextAlignmentOptions.Right);
        PlaceTopRight(_wallet.rectTransform, Pad, Pad, 1000f, 50f);
    }

    protected override void BuildDetail(Transform d)
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

    protected override void FillTab(int tab)
    {
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
    }

    private void AddEntry(ContractDef d)
    {
        string title = d.level == ContractCatalog.CurrentLevel ? d.title : Loc.T($"{d.title}\n(nivel {d.level})", $"{d.title}\n(level {d.level})");
        AddEntry(new Entry { def = d }, title);
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

    protected override void Refresh()
    {
        _chosen.RemoveAll(id => PlayerProgress.IsContractActive(id) || PlayerProgress.IsContractDone(id));
        int active = ActiveCount, toClaim = 0;
        foreach (ContractDef d in ContractCatalog.All) if (PlayerProgress.IsContractClaimable(d.id)) toClaim++;
        _wallet.text = Loc.T($"Nivel {ContractCatalog.CurrentLevel}     Monedas: {PlayerProgress.Coins}     Firmados: {active}/{ContractCatalog.MaxActive}     Por cobrar: {toClaim}",
                             $"Level {ContractCatalog.CurrentLevel}     Coins: {PlayerProgress.Coins}     Signed: {active}/{ContractCatalog.MaxActive}     To claim: {toClaim}");

        foreach (Entry e in _entries)
        {
            bool claimed = PlayerProgress.IsContractDone(e.def.id) && !PlayerProgress.IsContractClaimable(e.def.id);
            e.bg.color = e == _selected ? SelCol : claimed ? LockCol : ItemCol;
            e.state.text = State(e.def, _chosen.Contains(e.def.id));
        }
        RefreshDetail(active);
        RefreshSheet();
    }

    private void RefreshDetail(int active)
    {
        _dAction.gameObject.SetActive(_selected != null);
        if (_selected == null) { _dName.text = _dInfo.text = _dDesc.text = _dState.text = ""; return; }

        ContractDef d = _selected.def;
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
        if (_selected == null) return;
        ContractDef d = _selected.def;
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

}
