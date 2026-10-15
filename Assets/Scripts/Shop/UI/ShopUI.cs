using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// TIENDA (versión simple, se construye sola por código). Ábrela con ShopUI.Open().
///
///   TIENDA                               Monedas · Muescas   [Comprar muesca]
///   [Pestaña][Pestaña][Pestaña]...      ┌──────── ficha ────────┐
///   ┌───── rejilla (casillas) ─────┐    │ Nombre                │
///   │ [ ] [ ] [ ] [ ] [ ]          │    │ Descripción           │
///   │ [ ] [ ] [ ] ...              │    │ [Equipar]             │
///   └──────────────────────────────┘    │ [Comprar]             │
///               [Salir (Esc)]           └───────────────────────┘
///
/// Sin colores ni bordes redondeados a propósito. Lo común (abrir/cerrar, pestañas, rejilla, pie) está en
/// GridPanel; colores y letra en SimpleUI.cs.
/// </summary>
public class ShopUI : GridPanel<ShopUI, ShopUI.Entry>
{
    private float _openedAt;

    protected override void OnOpened()
    {
        _openedAt = Time.unscaledTime;
        StudyTelemetry.ShopEvent("abrir", "", 0);   // (modo prueba)
    }

    protected override void OnClosing() =>
        StudyTelemetry.ShopEvent("cerrar", "", 0, Time.unscaledTime - _openedAt);   // (modo prueba)

    // ── Estado ────────────────────────────────────────────────────────────────

    /// <summary>Una casilla de la rejilla: un objeto de la tienda.</summary>
    public class Entry : GridPanelCell
    {
        public ShopItem item;
    }

    private TextMeshProUGUI _wallet, _notchLabel;
    private Button _notchBuy;
    private TextMeshProUGUI _dName, _dInfo, _dDesc, _dState, _dEquipLabel, _dBuyLabel;
    private Button _dEquip, _dBuy;

    // ── Construcción ──────────────────────────────────────────────────────────

    protected override string CanvasName => "ShopCanvas";
    protected override string Title      => Loc.T("TIENDA", "SHOP");
    protected override float  ToastWidth => 600f;

    protected override List<string> TabNames()
    {
        return new List<string> { Loc.T("Mejoras", "Upgrades") };
    }

    protected override void BuildHeader(Transform p)
    {
        _wallet = Lbl("Wallet", p, "", FontMid, TextAlignmentOptions.Right);
        PlaceTopRight(_wallet.rectTransform, Pad + 300f, Pad, 700f, 50f);

        _notchBuy = Btn("BuyNotch", p, "", out _notchLabel);
        PlaceTopRight((RectTransform)_notchBuy.transform, Pad, Pad, 280f, 50f);
        _notchBuy.onClick.AddListener(BuyNotch);
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
        Stretch(_dDesc.rectTransform, 20f, 20f, 135f, 220f);

        _dState = Lbl("State", d, "", FontMid, TextAlignmentOptions.Left);
        PlaceBottomLeft(_dState.rectTransform, 20f, 160f, w, 40f);

        _dEquip = Btn("Equip", d, "", out _dEquipLabel);
        PlaceBottomLeft((RectTransform)_dEquip.transform, 20f, 90f, w, 55f);
        _dEquip.onClick.AddListener(ToggleEquip);

        _dBuy = Btn("Buy", d, "", out _dBuyLabel);
        PlaceBottomLeft((RectTransform)_dBuy.transform, 20f, 20f, w, 60f);
        _dBuy.onClick.AddListener(Buy);
    }

    // ── Pestañas y rejilla ────────────────────────────────────────────────────

    protected override void FillTab(int tab)
    {
        // Solo lo que se puede comprar ya (lo de niveles posteriores no aparece):
        // primero los consumibles y después las mejoras, por nivel de desbloqueo.
        var items = ShopCatalog.Load().items;
        foreach (ShopItem it in items)
            if (it != null && it.kind == ShopItemKind.Consumable && !it.IsLocked) AddEntry(new Entry { item = it }, it.Name);
        for (int level = 1; level <= 3; level++)
            foreach (ShopItem it in items)
                if (it != null && it.kind == ShopItemKind.Upgrade && !it.IsLocked && Mathf.Clamp(ShopUnlocks.LevelOf(it.id), 1, 3) == level)
                    AddEntry(new Entry { item = it }, it.Name);
    }

    // ── Refresco ──────────────────────────────────────────────────────────────

    protected override void Refresh()
    {
        _wallet.text = Loc.T($"Monedas: {PlayerProgress.Coins}     Muescas: {Notches.Used}/{Notches.Total} (máx. {Notches.MaxNotches})",
                             $"Coins: {PlayerProgress.Coins}     Notches: {Notches.Used}/{Notches.Total} (max. {Notches.MaxNotches})");
        if (Notches.AtMax) { _notchLabel.text = Loc.T("Muescas al máximo", "Notches maxed out"); _notchBuy.interactable = false; }
        else { _notchLabel.text = Loc.T($"Comprar muesca ({Notches.NextNotchPrice})", $"Buy notch ({Notches.NextNotchPrice})"); _notchBuy.interactable = PlayerProgress.Coins >= Notches.NextNotchPrice; }

        foreach (Entry e in _entries)
        {
            bool locked = e.item.IsLocked;
            e.bg.color = e == _selected ? SelCol : locked ? LockCol : ItemCol;
            e.state.text = ItemState(e.item);
        }
        RefreshDetail();
    }

    private static string ItemState(ShopItem it)
    {
        if (it.IsLocked) return Loc.T($"Nivel {ShopUnlocks.LevelOf(it.id)}", $"Level {ShopUnlocks.LevelOf(it.id)}");
        if (it.kind == ShopItemKind.Upgrade && it.Owned > 0)
            return (it.IsMaxed ? Loc.T("Máx.", "Max.") : Loc.T($"Nv {it.Owned}/{it.MaxLevel}", $"Lv {it.Owned}/{it.MaxLevel}")) + (PlayerProgress.IsUpgradeEnabled(it.id) ? Loc.T(" · Equipada", " · Equipped") : "");
        if (it.IsMaxed) return Loc.T($"Lleno ({it.Owned})", $"Full ({it.Owned})");
        return (it.kind == ShopItemKind.Consumable && it.Owned > 0 ? $"x{it.Owned} · " : "") + it.NextPrice;
    }

    private void RefreshDetail()
    {
        Entry e = _selected;
        _dEquip.gameObject.SetActive(false);
        _dBuy.gameObject.SetActive(e != null);
        if (e == null) { _dName.text = _dInfo.text = _dDesc.text = _dState.text = ""; return; }

        ShopItem it = e.item;
        int cost = Notches.CostOf(it.id);
        _dName.text = it.Name;
        _dInfo.text = (it.kind == ShopItemKind.Upgrade
                          ? Loc.T($"Mejora · ocupa {cost} muesca{(cost == 1 ? "" : "s")}", $"Upgrade · uses {cost} notch{(cost == 1 ? "" : "es")}")
                          : Loc.T("Consumible", "Consumable"))
                      + Loc.T($" · disponible desde el nivel {ShopUnlocks.LevelOf(it.id)}", $" · available from level {ShopUnlocks.LevelOf(it.id)}");
        _dDesc.text = StripTags(it.NextDescription);
        _dState.text = it.IsLocked ? ShopUnlocks.MissingText(it.id) : it.OwnedLabel;

        if (it.kind == ShopItemKind.Upgrade && it.Owned > 0)
        {
            _dEquip.gameObject.SetActive(true);
            _dEquipLabel.text = PlayerProgress.IsUpgradeEnabled(it.id) ? Loc.T("Quitar (libera muescas)", "Unequip (frees notches)")
                : Loc.T($"Equipar ({cost} muesca{(cost == 1 ? "" : "s")})", $"Equip ({cost} notch{(cost == 1 ? "" : "es")})");
        }

        if (it.IsLocked)     { _dBuyLabel.text = Loc.T($"Bloqueado (nivel {ShopUnlocks.LevelOf(it.id)})", $"Locked (level {ShopUnlocks.LevelOf(it.id)})"); _dBuy.interactable = false; }
        else if (it.IsMaxed) { _dBuyLabel.text = it.kind == ShopItemKind.Upgrade ? Loc.T("Al máximo", "Maxed out") : Loc.T("Lleno", "Full"); _dBuy.interactable = false; }
        else
        {
            _dBuyLabel.text = $"{(it.kind == ShopItemKind.Upgrade && it.Owned > 0 ? Loc.T("Mejorar", "Upgrade") : Loc.T("Comprar", "Buy"))} ({it.NextPrice})";
            _dBuy.interactable = it.CanAfford;
        }
    }

    // ── Acciones ──────────────────────────────────────────────────────────────

    private void Buy()
    {
        Entry e = _selected;
        if (e == null) return;
        ShopItem it = e.item;
        if (it.IsLocked)      Toast(ShopUnlocks.MissingText(it.id) + ".");
        else if (it.TryBuy()) Toast(Loc.T($"{it.Name} comprado.", $"{it.Name} bought."));
        else if (!it.IsMaxed) Toast(Loc.T("No tienes suficientes monedas.", "Not enough coins."));
    }

    private void ToggleEquip()
    {
        if (_selected == null || _selected.item == null) return;
        ShopItem it = _selected.item;
        bool on = !PlayerProgress.IsUpgradeEnabled(it.id);
        if (!PlayerProgress.SetUpgradeEnabled(it.id, on))
            Toast(Loc.T($"No hay muescas libres ({it.Name} ocupa {Notches.CostOf(it.id)}, te quedan {Notches.Free}).",
                         $"No free notches ({it.Name} uses {Notches.CostOf(it.id)}, you have {Notches.Free} left)."));
        else StudyTelemetry.ShopEvent(on ? "equipar" : "quitar", it.id, 0);   // (modo prueba)
        Refresh();
    }

    private void BuyNotch()
    {
        int price = Notches.NextNotchPrice;
        if (Notches.TryBuyNotch()) { Toast(Loc.T($"Muesca nueva: ahora tienes {Notches.Total}.", $"New notch: you now have {Notches.Total}.")); StudyTelemetry.ShopEvent("muesca", "muesca", price); }
        else Toast(Notches.AtMax ? Loc.T("Ya tienes el máximo de muescas.", "You already have the maximum notches.") : Loc.T("No tienes suficientes monedas.", "Not enough coins."));
    }

}
