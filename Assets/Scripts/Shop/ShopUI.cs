using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// TIENDA (versión simple, se construye sola por código). Ábrela con ShopUI.Open().
///
///   TIENDA                               Monedas · Iris · Muescas   [Comprar muesca]
///   [Pestaña][Pestaña][Pestaña]...      ┌──────── ficha ────────┐
///   ┌───── rejilla (casillas) ─────┐    │ Nombre                │
///   │ [ ] [ ] [ ] [ ] [ ]          │    │ Descripción           │
///   │ [ ] [ ] [ ] ...              │    │ [Equipar]             │
///   └──────────────────────────────┘    │ [Comprar]             │
///               [Salir (Esc)]           └───────────────────────┘
///
/// Sin colores ni bordes redondeados a propósito: tamaños en «LAYOUT» y colores/letra en SimpleUI.cs.
/// </summary>
public class ShopUI : MonoBehaviour
{
    // ── LAYOUT (cámbialo aquí) ────────────────────────────────────────────────
    private const float PanelW = 1800f, PanelH = 900f;
    private const float Pad = 30f;              // margen interior
    private const float HeaderH = 80f;          // cabecera (título, monedas, muescas)
    private const float TabH = 50f;             // pestañas (encima de la rejilla)
    private const float FooterH = 90f;          // pie (salir + aviso)
    private const float DetailW = 460f;         // ancho de la ficha
    // (Las casillas son cuadradas y del MISMO ancho que las pestañas: una columna por pestaña, alineadas.)
    private const float Gap = 10f;
    private float _cell;                        // lado de cada casilla (= ancho de una pestaña)
    // (Colores y letra: SimpleUI.cs, compartidos con los contratos.)

    // ── Abrir / cerrar ────────────────────────────────────────────────────────
    public static bool IsOpen => _instance != null;
    private static ShopUI _instance;
    private static int   _closedFrame = -10;
    private static float _closedTime  = -10f;

    /// <summary>¿Se acaba de cerrar? (para que el Esc que la cierra no abra también la pausa).</summary>
    public static bool ClosedRecently => Time.frameCount - _closedFrame <= 5 || Time.unscaledTime - _closedTime < 0.6f;

    private Action _onClosed;
    private CursorLockMode _prevLock;
    private bool _prevVisible;

    /// <summary>Abre la tienda. 'onClosed' se llama al cerrarla.</summary>
    public static ShopUI Open(Action onClosed = null)
    {
        if (_instance != null) return _instance;
        _instance = new GameObject("ShopUI").AddComponent<ShopUI>();
        _instance._onClosed = onClosed;
        return _instance;
    }

    private float _openedAt;

    public void Close()
    {
        StudyTelemetry.ShopEvent("cerrar", "", 0, Time.unscaledTime - _openedAt);   // (modo prueba)
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

    /// <summary>Una casilla de la rejilla: un objeto de la tienda o un cosmético.</summary>
    private class Entry
    {
        public ShopItem item;        // objeto de la tienda…
        public bool isCosmetic;      // …o cosmético (cos == null → «Original»)
        public CosmeticDef cos;
        public CosmeticSlot slot;
        public Image bg;
        public TextMeshProUGUI state;
    }

    // Pestañas: 0 = Mejoras (consumibles + mejoras), 1 = Cosméticos.
    private const int CosmeticsTab = 1;
    private const int Columns = 6;            // columnas de la rejilla (cada pestaña ocupa la mitad: 3 columnas)
    private Entry _selected;
    private readonly List<Entry> _entries = new List<Entry>();
    private readonly List<Image> _tabs = new List<Image>();

    private TextMeshProUGUI _wallet, _notchLabel, _toast;
    private Button _notchBuy;
    private RectTransform _grid;
    private TextMeshProUGUI _dName, _dInfo, _dDesc, _dState, _dEquipLabel, _dBuyLabel;
    private Button _dEquip, _dBuy;
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
        _openedAt = Time.unscaledTime;
        StudyTelemetry.ShopEvent("abrir", "", 0);   // (modo prueba)
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
        Canvas canvas = UIKit.CreateCanvas("ShopCanvas", 1000, transform);
        Stretch(Box("Back", canvas.transform, Back).rectTransform, 0f, 0f, 0f, 0f);

        var panel = Box("Panel", canvas.transform, PanelCol);
        Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));
        Transform p = panel.transform;

        // Cabecera
        var title = Lbl("Title", p, Loc.T("TIENDA", "SHOP"), FontBig, TextAlignmentOptions.Left);
        PlaceTopLeft(title.rectTransform, Pad, Pad, 400f, 50f);

        _wallet = Lbl("Wallet", p, "", FontMid, TextAlignmentOptions.Right);
        PlaceTopRight(_wallet.rectTransform, Pad + 300f, Pad, 700f, 50f);

        _notchBuy = Btn("BuyNotch", p, "", out _notchLabel);
        PlaceTopRight((RectTransform)_notchBuy.transform, Pad, Pad, 280f, 50f);
        _notchBuy.onClick.AddListener(BuyNotch);

        // Columna izquierda: pestañas + rejilla
        float leftW = PanelW - Pad * 3f - DetailW;
        float top = Pad + HeaderH;
        var names = new List<string> { Loc.T("Mejoras", "Upgrades"), Loc.T("Cosméticos", "Cosmetics") };
        // Casillas cuadradas en 'Columns' columnas; cada pestaña ocupa justo 'Columns / 2' casillas (bordes alineados).
        _cell = (leftW - Gap * (Columns - 1)) / Columns;
        int perTab = Columns / names.Count;
        float tabW = _cell * perTab + Gap * (perTab - 1);
        for (int i = 0; i < names.Count; i++)
        {
            int tab = i;
            var b = Btn("Tab" + i, p, names[i], out _);
            PlaceTopLeft((RectTransform)b.transform, Pad + i * (tabW + Gap), top, tabW, TabH);
            b.onClick.AddListener(() => ShowTab(tab));
            _tabs.Add(b.image);
        }

        // Rejilla justo debajo, con el mismo ancho que las pestañas.
        var gridBox = UIKit.Node("GridBox", p);
        float gridTop = top + TabH + Gap;
        float gridH = PanelH - gridTop - FooterH;
        PlaceTopLeft(gridBox, Pad, gridTop, leftW, gridH);
        _grid = MakeGrid(gridBox, _cell, Gap, Columns);

        // Columna derecha: ficha
        var detail = Box("Detail", p, BoxCol);
        PlaceTopRight(detail.rectTransform, Pad, top, DetailW, PanelH - top - FooterH);
        BuildDetail(detail.transform);

        // Pie
        var exit = Btn("Exit", p, Loc.T("Salir (Esc)", "Exit (Esc)"), out _);
        Place((RectTransform)exit.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(300f, 50f), new Vector2(0.5f, 0f));
        exit.onClick.AddListener(Close);
        _toast = Lbl("Toast", p, "", FontMid, TextAlignmentOptions.Left);
        PlaceBottomLeft(_toast.rectTransform, Pad, 20f, 600f, 50f);
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

    private void ShowTab(int tab)
    {
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].color = i == tab ? SelCol : BtnCol;

        foreach (Transform c in _grid) Destroy(c.gameObject);
        _entries.Clear();

        if (tab == CosmeticsTab)
        {
            foreach (CosmeticSlot slot in Cosmetics.Slots)
            {
                AddEntry(new Entry { isCosmetic = true, slot = slot }, "Original\n(" + Cosmetics.SlotName(slot) + ")");
                foreach (CosmeticDef c in Cosmetics.All)
                    if (c.slot == slot) AddEntry(new Entry { isCosmetic = true, slot = slot, cos = c }, c.name);
            }
        }
        else
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

        _selected = _entries.Count > 0 ? _entries[0] : null;
        Refresh();
    }

    private void AddEntry(Entry e, string title)
    {
        e.bg = Box("Item", _grid, ItemCol);
        var btn = e.bg.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => { _selected = e; Refresh(); });

        var label = Lbl("Name", e.bg.transform, title, FontSmall + 2f, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 10f, 10f, 10f, 45f);
        e.state = Lbl("State", e.bg.transform, "", FontSmall, TextAlignmentOptions.Center);
        e.state.color = DimText;
        Stretch(e.state.rectTransform, 6f, 6f, _cell - 45f, 8f);
        _entries.Add(e);
    }

    // ── Refresco ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        _wallet.text = Loc.T($"Monedas: {PlayerProgress.Coins}     Iris: {PlayerProgress.GazeCoins}     Muescas: {Notches.Used}/{Notches.Total} (máx. {Notches.MaxNotches})",
                             $"Coins: {PlayerProgress.Coins}     Iris: {PlayerProgress.GazeCoins}     Notches: {Notches.Used}/{Notches.Total} (max. {Notches.MaxNotches})");
        if (Notches.AtMax) { _notchLabel.text = Loc.T("Muescas al máximo", "Notches maxed out"); _notchBuy.interactable = false; }
        else { _notchLabel.text = Loc.T($"Comprar muesca ({Notches.NextNotchPrice})", $"Buy notch ({Notches.NextNotchPrice})"); _notchBuy.interactable = PlayerProgress.Coins >= Notches.NextNotchPrice; }

        foreach (Entry e in _entries)
        {
            bool locked = !e.isCosmetic && e.item.IsLocked;
            e.bg.color = e == _selected ? SelCol : locked ? LockCol : ItemCol;
            e.state.text = e.isCosmetic ? CosmeticState(e) : ItemState(e.item);
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

    private static bool IsWorn(Entry e) => e.cos == null ? Cosmetics.Equipped(e.slot) == null : Cosmetics.Equipped(e.slot) == e.cos;

    private static string CosmeticState(Entry e)
    {
        if (IsWorn(e)) return Loc.T("Puesto", "Worn");
        if (e.cos == null || Cosmetics.Owns(e.cos)) return Loc.T("Comprado", "Owned");
        return $"{e.cos.price} iris";
    }

    private void RefreshDetail()
    {
        Entry e = _selected;
        _dEquip.gameObject.SetActive(false);
        _dBuy.gameObject.SetActive(e != null);
        if (e == null) { _dName.text = _dInfo.text = _dDesc.text = _dState.text = ""; return; }

        if (e.isCosmetic)
        {
            _dName.text  = e.cos != null ? e.cos.name : "Original";
            _dInfo.text  = Loc.T("Cosmético · ", "Cosmetic · ") + Cosmetics.SlotName(e.slot) + Loc.T(" · se paga con iris", " · paid with iris");
            _dDesc.text  = e.cos != null ? e.cos.description : Loc.T("El aspecto de siempre.", "The usual look.");
            _dState.text = CosmeticState(e);
            bool owned = e.cos == null || Cosmetics.Owns(e.cos);
            if (IsWorn(e))  { _dBuyLabel.text = Loc.T("Puesto", "Worn"); _dBuy.interactable = false; }
            else if (owned) { _dBuyLabel.text = Loc.T("Poner", "Wear");  _dBuy.interactable = true; }
            else            { _dBuyLabel.text = Loc.T($"Comprar ({e.cos.price} iris)", $"Buy ({e.cos.price} iris)"); _dBuy.interactable = PlayerProgress.GazeCoins >= e.cos.price; }
            return;
        }

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
        if (e.isCosmetic)
        {
            if (e.cos == null) Cosmetics.Equip(e.slot, null);
            else if (Cosmetics.Owns(e.cos)) Cosmetics.Equip(e.slot, e.cos.id);
            else if (!Cosmetics.TryBuy(e.cos)) Toast(Loc.T("Te faltan iris.", "Not enough iris."));
            Refresh();
            return;
        }
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

    private void Toast(string msg) { _toast.text = msg; _toastT = 2.5f; }
}
