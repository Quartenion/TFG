using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// TIENDA (se construye sola por código). Ábrela con ShopUI.Open():
///   • Menú principal → botón que llame a MainMenuManager.OpenShop().
///   • Entre niveles  → la TIENDA FÍSICA del área de freeroam (componente ShopStand): acércate, mírala y pulsa E.
/// Muestra el catálogo (ShopCatalog) en tarjetas: nombre, descripción, nivel/cantidad y botón de compra.
/// Esc o «Volver» la cierran.
/// </summary>
public partial class ShopUI : MonoBehaviour
{
    public static bool   IsOpen => _instance != null;
    private static ShopUI _instance;
    private static int    _closedFrame = -10;
    private static float _closedTime = -10f;

    /// <summary>¿Se acaba de cerrar (este frame o el anterior)? Para que el Esc de cerrar no abra la pausa.</summary>
    public static bool ClosedRecently => Time.frameCount - _closedFrame <= 5 || Time.unscaledTime - _closedTime < 0.6f;

    private Action _onClosed;
    private CursorLockMode _prevLock;
    private bool _prevVisible;

    private TextMeshProUGUI _coinsText, _toast, _title, _backLabel;
    private RectTransform   _content;
    private GridLayoutGroup _grid;
    private ScrollRect      _scroll;
    private ShopCategory    _category = ShopCategory.Auto;   // Auto = pantalla de categorías
    private readonly List<(ShopCategory cat, TextMeshProUGUI info)> _catCards = new List<(ShopCategory, TextMeshProUGUI)>();
    private float _toastT;
    private readonly List<Card> _cards = new List<Card>();

    private class Card
    {
        public ShopItem item;
        public TextMeshProUGUI owned, desc, buyLabel;
        public Button buy;
        public Image bg;
        public Toggle toggle;          // (solo mejoras) activar / desactivar
        public TextMeshProUGUI toggleLabel;
    }

    private static readonly Color BgDim   = new Color(0f, 0f, 0f, 0.82f);
    private static readonly Color PanelBg = new Color(0.08f, 0.05f, 0.07f, 0.97f);
    private static readonly Color CardBg  = new Color(0.15f, 0.1f, 0.12f, 1f);
    private static readonly Color Gold    = new Color(1f, 0.78f, 0.15f);

    /// <summary>Abre la tienda. 'onClosed' se llama al cerrarla.</summary>
    public static ShopUI Open(Action onClosed = null)
    {
        if (_instance != null) return _instance;
        var go = new GameObject("ShopUI");
        _instance = go.AddComponent<ShopUI>();
        _instance._onClosed = onClosed;
        return _instance;
    }

    public void Close()
    {
        // (Se reaplica unos frames: si se cerró con Esc, el editor desbloquearía el ratón justo después.)
        CursorRestorer.Restore(_prevLock, _prevVisible);
        if (_instance == this) _instance = null;   // IsOpen = false ya (no al final del frame)
        _closedFrame = Time.frameCount;
        _closedTime  = Time.unscaledTime;
        Destroy(gameObject);
        Action cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }

    private void Awake()
    {
        _prevLock    = Cursor.lockState;
        _prevVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        UIKit.EnsureEventSystem();
        Build();
        PlayerProgress.OnChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        PlayerProgress.OnChanged -= Refresh;
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Back(); return; }

        if (_toastT > 0f)
        {
            _toastT -= Time.unscaledDeltaTime;
            Color c = _toast.color; c.a = Mathf.Clamp01(_toastT / 0.4f); _toast.color = c;
        }
    }

    // ── Construcción ──────────────────────────────────────────────────────────

    private void Build()
    {
        Canvas canvas = UIKit.CreateCanvas("ShopCanvas", 1000, transform);

        var dim = UIKit.Panel("Dim", canvas.transform, BgDim, false);
        UIKit.Stretch(dim.rectTransform);
        dim.raycastTarget = true;   // bloquea clics a lo de detrás

        var panel = UIKit.Panel("Panel", canvas.transform, PanelBg);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1640f, 940f));
        Transform p = panel.transform;

        _title = UIKit.Label("Title", p, "TIENDA", 64f, Color.white, TextAlignmentOptions.Left, true);
        UIKit.Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -30f), new Vector2(1000f, 80f));

        // Monedas del monedero.
        var coinIcon = UIKit.CoinIcon(p, 48f);
        UIKit.Place(coinIcon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -46f), new Vector2(48f, 48f));
        _coinsText = UIKit.Label("Coins", p, "0", 50f, Gold, TextAlignmentOptions.Right, true);
        UIKit.Place(_coinsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-112f, -36f), new Vector2(400f, 70f));

        // Zona con scroll para las tarjetas.
        var viewport = UIKit.Node("Viewport", p);
        UIKit.Stretch(viewport, 40, 40, 130, 120);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();   // para poder arrastrar/rueda sobre el hueco
        vpImg.color = new Color(0, 0, 0, 0.001f);

        var content = UIKit.Node("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        _content = content;
        _grid = content.gameObject.AddComponent<GridLayoutGroup>();
        _grid.spacing  = new Vector2(22f, 22f);
        _grid.padding  = new RectOffset(8, 8, 8, 8);
        _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _grid.childAlignment = TextAnchor.UpperCenter;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll = scroll;
        _viewportGO = viewport.gameObject;

        // Zona del ÁRBOL de desbloqueos (se usa al entrar en una categoría).
        _treeRoot = UIKit.Node("Tree", p);
        UIKit.Stretch(_treeRoot, 40, 40, 130, 150);
        _treeRoot.gameObject.SetActive(false);

        // Volver (a las categorías o cerrar).
        var back = UIKit.MakeButton("Back", p, "VOLVER  (Esc)", 30f, new Color(0.3f, 0.3f, 0.35f), Color.white, out _backLabel);
        UIKit.Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(420f, 66f));
        back.onClick.AddListener(Back);

        // Aviso breve (compra hecha / sin monedas).
        _toast = UIKit.Label("Toast", p, "", 28f, Color.white, TextAlignmentOptions.Center, true);
        UIKit.Place(_toast.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 104f), new Vector2(900f, 40f));

        ShowCategories();
    }

    // ── Pantallas: categorías ↔ objetos de una categoría ─────────────────────

    /// <summary>Esc / Volver: dentro de una categoría vuelve a las categorías; en las categorías cierra la tienda.</summary>
    private void Back()
    {
        if (_category != ShopCategory.Auto || _inCosmetics) ShowCategories();
        else Close();
    }

    private void ClearContent()
    {
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            GameObject g = _content.GetChild(i).gameObject;
            g.SetActive(false);
            Destroy(g);
        }
        _cards.Clear();
        _catCards.Clear();
        _cosCards.Clear();
        _cosmeticsCardInfo = null;
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
    }

    private void ShowCategories()
    {
        _category = ShopCategory.Auto;
        _inCosmetics = false;
        ClearContent();
        ClearTree();
        _viewportGO.SetActive(true);
        _title.text = "TIENDA";
        _backLabel.text = "SALIR  (Esc)";
        _grid.cellSize = new Vector2(490f, 300f);
        _grid.constraintCount = 3;

        var items = ShopCatalog.Load().items;
        foreach (ShopCategory cat in ShopCategories.Order)
        {
            bool any = false;
            foreach (ShopItem it in items) if (it != null && ShopCategories.Of(it) == cat) { any = true; break; }
            if (any) BuildCategoryCard(cat);
        }
        BuildCosmeticsCategoryCard();   // (ShopUICosmetics.cs)
        Refresh();
    }

    private void ShowCategory(ShopCategory cat)
    {
        _category = cat;
        ClearContent();
        _viewportGO.SetActive(false);
        _title.text = "TIENDA  ·  " + ShopCategories.Name(cat).ToUpper();
        _backLabel.text = "←  CATEGORÍAS  (Esc)";
        BuildTree(cat);   // (ShopUITree.cs)
        Refresh();
    }

    private void BuildCategoryCard(ShopCategory cat)
    {
        Color col = ShopCategories.Tint(cat);
        var bg = UIKit.Panel(cat.ToString(), _content, CardBg);
        var btn = bg.gameObject.AddComponent<Button>();
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        cb.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(() => ShowCategory(cat));
        Transform t = bg.transform;

        var band = UIKit.Panel("Band", t, col);
        band.raycastTarget = false;
        UIKit.Place(band.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(16f, 270f));

        var nameTxt = UIKit.Label("Name", t, ShopCategories.Name(cat).ToUpper(), 44f, col, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(nameTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -30f), new Vector2(420f, 70f));

        var desc = UIKit.Label("Desc", t, ShopCategories.Description(cat), 26f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
        UIKit.Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -110f), new Vector2(420f, 90f));

        var info = UIKit.Label("Info", t, "", 21f, Gold, TextAlignmentOptions.BottomLeft, true);
        UIKit.Place(info.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(50f, 66f), new Vector2(420f, 60f));
        _catCards.Add((cat, info));

        var enter = UIKit.Label("Enter", t, "ENTRAR  →", 26f, Color.white, TextAlignmentOptions.Right, true);
        UIKit.Place(enter.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 26f), new Vector2(300f, 36f));
    }

    private Card BuildCard(Transform parent, ShopItem it)
    {
        var card = new Card { item = it };
        Color col = ShopCategories.Tint(ShopCategories.Of(it));   // mismo color que su categoría
        var bg = UIKit.Panel(it.id, parent, CardBg);
        card.bg = bg;
        Transform t = bg.transform;

        var band = UIKit.Panel("Band", t, col);
        UIKit.Place(band.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(340f, 10f));

        if (it.icon != null)
        {
            var ic = UIKit.Node("Icon", t).gameObject.AddComponent<Image>();
            ic.sprite = it.icon; ic.preserveAspect = true; ic.raycastTarget = false;
            UIKit.Place(ic.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -26f), new Vector2(56f, 56f));
        }

        var nameTxt = UIKit.Label("Name", t, it.displayName, 30f, Color.white, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(nameTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -26f), new Vector2(it.icon != null ? 270f : 330f, 44f));

        string kind = it.kind == ShopItemKind.Upgrade ? "MEJORA" : "CONSUMIBLE";
        var kindTxt = UIKit.Label("Kind", t, kind, 16f, col, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(kindTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -70f), new Vector2(330f, 22f));

        card.desc = UIKit.Label("Desc", t, "", 20f, new Color(1f, 1f, 1f, 0.82f), TextAlignmentOptions.TopLeft);
        UIKit.Place(card.desc.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -98f), new Vector2(330f, 130f));

        card.owned = UIKit.Label("Owned", t, "", 20f, Gold, TextAlignmentOptions.Left, true);
        card.owned.richText = true;
        UIKit.Place(card.owned.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 84f), new Vector2(190f, 28f));

        // Mejoras permanentes: casilla para ACTIVARLA / DESACTIVARLA (sin perder lo comprado).
        if (it.kind == ShopItemKind.Upgrade)
        {
            card.toggle = UIKit.MakeToggle("Enabled", t, "Activa", 20f, col, out card.toggleLabel);
            UIKit.Place((RectTransform)card.toggle.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 82f), new Vector2(130f, 30f));
            card.toggle.onValueChanged.AddListener(on =>
            {
                if (_refreshing) return;
                PlayerProgress.SetUpgradeEnabled(it.id, on);
                ShowToast(on ? $"{it.displayName}: ACTIVADA" : $"{it.displayName}: DESACTIVADA", on ? new Color(0.5f, 1f, 0.5f) : Color.white);
            });
        }

        card.buy = UIKit.MakeButton("Buy", t, "", 24f, col * 0.85f + new Color(0f, 0f, 0f, 0.15f), Color.white, out card.buyLabel);
        UIKit.Place((RectTransform)card.buy.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(330f, 58f));
        card.buy.onClick.AddListener(() => OnBuy(card));
        return card;
    }

    // ── Compra / refresco ─────────────────────────────────────────────────────

    private bool _refreshing;

    private void OnBuy(Card c)
    {
        if (c.item.IsLocked)
        {
            ShowToast(ShopUnlocks.MissingText(c.item.id), new Color(1f, 0.6f, 0.4f));
            return;
        }
        if (c.item.TryBuy())
            ShowToast($"¡{c.item.displayName} comprado!", new Color(0.5f, 1f, 0.5f));
        else if (c.item.IsMaxed)
            ShowToast("Ya lo tienes al máximo.", Color.white);
        else
            ShowToast("No tienes suficientes monedas.", new Color(1f, 0.5f, 0.5f));
    }

    private void Refresh()
    {
        if (_coinsText != null) _coinsText.text = PlayerProgress.Coins.ToString();

        // Tarjetas de categoría: cuántos objetos hay, cuántos tienes y cuántos te puedes permitir ahora.
        var items = ShopCatalog.Load().items;
        foreach (var (cat, info) in _catCards)
        {
            int total = 0, owned = 0, affordable = 0, locked = 0;
            foreach (ShopItem it in items)
            {
                if (it == null || ShopCategories.Of(it) != cat) continue;
                total++;
                if (it.Owned > 0) owned++;
                if (it.IsLocked) { locked++; continue; }
                if (!it.IsMaxed && it.CanAfford) affordable++;
            }
            info.text = $"{total} objetos  ·  {owned} con algo comprado" +
                        (locked > 0 ? $"  ·  <color=#FF9B6B>{locked} bloqueados</color>" : "") +
                        (affordable > 0 ? $"  ·  <color=#7CFF7C>{affordable} al alcance</color>" : "");
        }

        _refreshing = true;
        foreach (Card c in _cards)
        {
            ShopItem it = c.item;
            bool locked = it.IsLocked;
            c.desc.text  = locked ? it.description + "\n<color=#FF9B6B><b>" + ShopUnlocks.MissingText(it.id) + "</b></color>"
                                  : it.NextDescription;
            c.owned.text = locked ? "<color=#FF9B6B>BLOQUEADO</color>" : it.OwnedLabel;
            if (c.bg != null) c.bg.color = locked ? new Color(0.09f, 0.07f, 0.08f, 1f) : CardBg;

            // Casilla de activar/desactivar: solo si la tienes comprada.
            if (c.toggle != null)
            {
                bool show = it.kind == ShopItemKind.Upgrade && it.Owned > 0;
                c.toggle.gameObject.SetActive(show);
                if (show)
                {
                    bool on = PlayerProgress.IsUpgradeEnabled(it.id);
                    c.toggle.SetIsOnWithoutNotify(on);
                    c.toggleLabel.text = on ? "Activa" : "Apagada";
                    c.toggleLabel.color = on ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                }
            }

            if (locked)
            {
                c.buyLabel.text = "BLOQUEADO";
                c.buy.interactable = false;
            }
            else if (it.IsMaxed)
            {
                c.buyLabel.text = it.kind == ShopItemKind.Upgrade ? "AL MÁXIMO" : "LLENO";
                c.buy.interactable = false;
            }
            else
            {
                int price = it.NextPrice;
                string verb = it.kind == ShopItemKind.Upgrade ? (it.Owned == 0 ? "COMPRAR" : "MEJORAR") : "COMPRAR";
                c.buyLabel.text = $"{verb} · {price}";
                c.buy.interactable = PlayerProgress.Coins >= price;
            }
        }
        _refreshing = false;
        RefreshTree();        // (ShopUITree.cs)
        RefreshCosmetics();   // (ShopUICosmetics.cs)
    }

    private void ShowToast(string msg, Color color)
    {
        _toast.text  = msg;
        _toast.color = color;
        _toastT = 1.6f;
    }
}
