using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ÁRBOL DE DESBLOQUEOS de la tienda (parte de ShopUI). Al entrar en una categoría, sus objetos se dibujan como un
/// árbol de izquierda a derecha: a la izquierda lo básico y, siguiendo las líneas, lo que desbloquea cada cosa hasta
/// llegar a lo más potente. Las líneas se iluminan cuando el requisito ya está comprado.
/// Al pulsar un nodo se ve su ficha a la derecha (descripción, requisitos, comprar y activar/desactivar).
/// </summary>
public partial class ShopUI
{
    private GameObject    _viewportGO;
    private RectTransform _treeRoot;

    private class TreeNode
    {
        public ShopItem item;
        public int depth, row;
        public Vector2 pos;                 // esquina sup. izq. dentro del árbol
        public Image bg, band;
        public Outline outline;
        public TextMeshProUGUI name, status;
    }
    private class TreeLink { public TreeNode from, to; public Image img; }

    private readonly List<TreeNode> _nodes = new List<TreeNode>();
    private readonly List<TreeLink> _links = new List<TreeLink>();
    private TreeNode _selected;
    private ShopCategory _treeCat;

    // Ficha de detalle
    private TextMeshProUGUI _dName, _dKind, _dDesc, _dReqs, _dOwned, _dBuyLabel, _dToggleLabel;
    private Button _dBuy;
    private Toggle _dToggle;
    private Image _dBand;

    private const float NodeW = 176f, NodeH = 92f;
    private const float TreeAreaW = 1080f, DetailW = 450f;

    private void ClearTree()
    {
        if (_treeRoot == null) return;
        for (int i = _treeRoot.childCount - 1; i >= 0; i--) Destroy(_treeRoot.GetChild(i).gameObject);
        _nodes.Clear();
        _links.Clear();
        _selected = null;
        _treeRoot.gameObject.SetActive(false);
    }

    private void BuildTree(ShopCategory cat)
    {
        ClearTree();
        _treeCat = cat;
        _treeRoot.gameObject.SetActive(true);
        Color col = ShopCategories.Tint(cat);

        // 1) Nodos de la categoría y su profundidad (nº de pasos de desbloqueo).
        var byId = new Dictionary<string, TreeNode>();
        foreach (ShopItem it in ShopCatalog.Load().items)
            if (it != null && ShopCategories.Of(it) == cat && !byId.ContainsKey(it.id))
            {
                var n = new TreeNode { item = it, depth = -1 };
                byId[it.id] = n;
                _nodes.Add(n);
            }
        foreach (TreeNode n in _nodes) Depth(n, byId, 0);

        // 2) Columnas por profundidad; filas ordenadas por la posición media de sus requisitos (menos cruces).
        int maxDepth = 0;
        foreach (TreeNode n in _nodes) maxDepth = Mathf.Max(maxDepth, n.depth);
        var cols = new List<List<TreeNode>>();
        for (int d = 0; d <= maxDepth; d++) cols.Add(new List<TreeNode>());
        foreach (TreeNode n in _nodes) cols[n.depth].Add(n);
        for (int d = 0; d <= maxDepth; d++)
        {
            if (d > 0)
                cols[d].Sort((a, b) => AvgReqRow(a, byId).CompareTo(AvgReqRow(b, byId)));
            for (int r = 0; r < cols[d].Count; r++) cols[d][r].row = r;
        }

        float areaH = _treeRoot.rect.height > 10f ? _treeRoot.rect.height : 660f;
        int maxRows = 1;
        foreach (var c in cols) maxRows = Mathf.Max(maxRows, c.Count);
        float colW = Mathf.Min(250f, (TreeAreaW - NodeW) / Mathf.Max(1, maxDepth) );
        if (maxDepth == 0) colW = 0f;
        float rowH = Mathf.Min(122f, (areaH - 20f) / maxRows);
        float x0 = (TreeAreaW - (maxDepth * colW + NodeW)) * 0.5f;
        for (int d = 0; d <= maxDepth; d++)
        {
            float colH = cols[d].Count * rowH;
            float y0 = (areaH - colH) * 0.5f + (rowH - NodeH) * 0.5f;
            foreach (TreeNode n in cols[d]) n.pos = new Vector2(x0 + d * colW, y0 + n.row * rowH);
        }

        // Fondo del árbol
        var treeBg = UIKit.Panel("TreeBg", _treeRoot, new Color(0f, 0f, 0f, 0.25f));
        UIKit.Place(treeBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(TreeAreaW, areaH));
        treeBg.raycastTarget = false;

        // 3) Líneas (detrás de los nodos): de cada requisito a lo que desbloquea.
        var linesRoot = UIKit.Node("Links", _treeRoot);
        UIKit.Place(linesRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(TreeAreaW, areaH));
        foreach (TreeNode n in _nodes)
            foreach (ShopUnlocks.Req r in ShopUnlocks.RequirementsOf(n.item.id))
            {
                if (!byId.TryGetValue(r.id, out TreeNode from)) continue;
                Vector2 a = new Vector2(from.pos.x + NodeW, -(from.pos.y + NodeH * 0.5f));
                Vector2 b = new Vector2(n.pos.x, -(n.pos.y + NodeH * 0.5f));
                var line = UIKit.Panel("Link", linesRoot, Color.gray, false);
                line.raycastTarget = false;
                RectTransform lr = line.rectTransform;
                lr.anchorMin = lr.anchorMax = new Vector2(0f, 1f);
                lr.pivot = new Vector2(0.5f, 0.5f);
                lr.anchoredPosition = (a + b) * 0.5f;
                lr.sizeDelta = new Vector2(Vector2.Distance(a, b), 5f);
                lr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                _links.Add(new TreeLink { from = from, to = n, img = line });
            }

        // 4) Nodos
        foreach (TreeNode n in _nodes) BuildNode(n, col);

        // 5) Ficha de detalle (derecha)
        BuildDetail(areaH, col);

        // Seleccionado por defecto: el primero que puedas comprar ya; si no, el primero.
        TreeNode pick = null;
        foreach (TreeNode n in _nodes)
            if (!n.item.IsLocked && !n.item.IsMaxed && n.item.CanAfford) { pick = n; break; }
        Select(pick ?? (_nodes.Count > 0 ? _nodes[0] : null));
    }

    private int Depth(TreeNode n, Dictionary<string, TreeNode> byId, int guard)
    {
        if (n.depth >= 0) return n.depth;
        if (guard > 20) return 0;   // (por si hubiera un ciclo en la tabla)
        int d = 0;
        foreach (ShopUnlocks.Req r in ShopUnlocks.RequirementsOf(n.item.id))
            if (byId.TryGetValue(r.id, out TreeNode p)) d = Mathf.Max(d, Depth(p, byId, guard + 1) + 1);
        n.depth = d;
        return d;
    }

    private float AvgReqRow(TreeNode n, Dictionary<string, TreeNode> byId)
    {
        float sum = 0f; int k = 0;
        foreach (ShopUnlocks.Req r in ShopUnlocks.RequirementsOf(n.item.id))
            if (byId.TryGetValue(r.id, out TreeNode p)) { sum += p.row; k++; }
        return k > 0 ? sum / k : 0f;
    }

    private void BuildNode(TreeNode n, Color col)
    {
        n.bg = UIKit.Panel("Node_" + n.item.id, _treeRoot, CardBg);
        UIKit.Place(n.bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(n.pos.x, -n.pos.y), new Vector2(NodeW, NodeH));
        var btn = n.bg.gameObject.AddComponent<Button>();
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
        cb.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(() => Select(n));

        n.outline = n.bg.gameObject.AddComponent<Outline>();
        n.outline.effectColor = Color.white;
        n.outline.effectDistance = new Vector2(3f, -3f);
        n.outline.enabled = false;

        n.band = UIKit.Panel("Band", n.bg.transform, col);
        n.band.raycastTarget = false;
        UIKit.Place(n.band.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -5f), new Vector2(NodeW - 16f, 6f));

        n.name = UIKit.Label("Name", n.bg.transform, n.item.displayName, 20f, Color.white, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(n.name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -16f), new Vector2(NodeW - 24f, 50f));

        n.status = UIKit.Label("Status", n.bg.transform, "", 17f, Gold, TextAlignmentOptions.BottomLeft, true);
        n.status.richText = true;
        UIKit.Place(n.status.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 8f), new Vector2(NodeW - 24f, 26f));
    }

    private void BuildDetail(float areaH, Color col)
    {
        var d = UIKit.Panel("Detail", _treeRoot, CardBg);
        UIKit.Place(d.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(DetailW, areaH));
        Transform t = d.transform;

        _dBand = UIKit.Panel("Band", t, col);
        UIKit.Place(_dBand.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(DetailW - 30f, 10f));

        _dName = UIKit.Label("Name", t, "", 36f, Color.white, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(_dName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -30f), new Vector2(DetailW - 44f, 50f));

        _dKind = UIKit.Label("Kind", t, "", 17f, col, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(_dKind.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -82f), new Vector2(DetailW - 44f, 24f));

        _dDesc = UIKit.Label("Desc", t, "", 22f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
        _dDesc.richText = true;
        UIKit.Place(_dDesc.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -114f), new Vector2(DetailW - 44f, 190f));

        _dReqs = UIKit.Label("Reqs", t, "", 20f, Color.white, TextAlignmentOptions.TopLeft);
        _dReqs.richText = true;
        UIKit.Place(_dReqs.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -310f), new Vector2(DetailW - 44f, 120f));

        _dOwned = UIKit.Label("Owned", t, "", 22f, Gold, TextAlignmentOptions.Left, true);
        _dOwned.richText = true;
        UIKit.Place(_dOwned.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(22f, 96f), new Vector2(240f, 30f));

        _dToggle = UIKit.MakeToggle("Enabled", t, "Activa", 22f, col, out _dToggleLabel);
        UIKit.Place((RectTransform)_dToggle.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-22f, 96f), new Vector2(150f, 32f));
        _dToggle.onValueChanged.AddListener(on =>
        {
            if (_refreshing || _selected == null) return;
            PlayerProgress.SetUpgradeEnabled(_selected.item.id, on);
            ShowToast(on ? $"{_selected.item.displayName}: ACTIVADA" : $"{_selected.item.displayName}: DESACTIVADA",
                      on ? new Color(0.5f, 1f, 0.5f) : Color.white);
        });

        _dBuy = UIKit.MakeButton("Buy", t, "", 28f, col * 0.85f + new Color(0f, 0f, 0f, 0.15f), Color.white, out _dBuyLabel);
        UIKit.Place((RectTransform)_dBuy.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(DetailW - 44f, 64f));
        _dBuy.onClick.AddListener(() => { if (_selected != null) BuyItem(_selected.item); });
    }

    private void Select(TreeNode n)
    {
        _selected = n;
        RefreshTree();
    }

    private void BuyItem(ShopItem it)
    {
        if (it.IsLocked) { ShowToast(ShopUnlocks.MissingText(it.id), new Color(1f, 0.6f, 0.4f)); return; }
        if (it.TryBuy())       ShowToast($"¡{it.displayName} comprado!", new Color(0.5f, 1f, 0.5f));
        else if (it.IsMaxed)   ShowToast("Ya lo tienes al máximo.", Color.white);
        else                   ShowToast("No tienes suficientes monedas.", new Color(1f, 0.5f, 0.5f));
    }

    /// <summary>Colores y textos de nodos, líneas y ficha según lo comprado.</summary>
    private void RefreshTree()
    {
        if (_treeRoot == null || !_treeRoot.gameObject.activeSelf || _nodes.Count == 0) return;
        Color col = ShopCategories.Tint(_treeCat);

        foreach (TreeNode n in _nodes)
        {
            ShopItem it = n.item;
            bool locked = it.IsLocked, owned = it.Owned > 0, maxed = it.IsMaxed;
            bool off = it.kind == ShopItemKind.Upgrade && owned && !PlayerProgress.IsUpgradeEnabled(it.id);

            n.bg.color = locked ? new Color(0.07f, 0.06f, 0.07f, 1f)
                       : maxed  ? Color.Lerp(CardBg, col, 0.45f)
                       : owned  ? Color.Lerp(CardBg, col, 0.25f)
                                : CardBg;
            n.band.color = locked ? new Color(0.3f, 0.3f, 0.3f) : col;
            n.name.color = locked ? new Color(1f, 1f, 1f, 0.4f) : off ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            n.outline.enabled = n == _selected;

            if (locked) n.status.text = "<color=#FF9B6B>BLOQUEADO</color>";
            else if (maxed) n.status.text = it.kind == ShopItemKind.Upgrade ? (off ? "AL MÁXIMO · <color=#AAAAAA>apagada</color>" : "AL MÁXIMO") : $"LLENO ({it.Owned})";
            else
            {
                string have = it.kind == ShopItemKind.Upgrade ? (owned ? $"Nv {it.Owned}/{it.MaxLevel}" : "")
                                                              : $"x{it.Owned}";
                string price = it.CanAfford ? $"{it.NextPrice}" : $"<color=#FF7070>{it.NextPrice}</color>";
                n.status.text = (have.Length > 0 ? have + "  ·  " : "") + price + " mon." + (off ? "  <color=#AAAAAA>(apagada)</color>" : "");
            }
        }

        foreach (TreeLink l in _links)
        {
            bool met = false;
            foreach (ShopUnlocks.Req r in ShopUnlocks.RequirementsOf(l.to.item.id))
                if (r.id == l.from.item.id) met = PlayerProgress.GetOwnedLevel(r.id) >= r.level;
            l.img.color = met ? col : new Color(0.35f, 0.35f, 0.35f, 0.8f);
        }

        // Ficha
        if (_selected == null) return;
        ShopItem s = _selected.item;
        bool sLocked = s.IsLocked;
        _dName.text = s.displayName;
        _dKind.text = (s.kind == ShopItemKind.Upgrade ? "MEJORA PERMANENTE" : "CONSUMIBLE") + (sLocked ? "  ·  BLOQUEADA" : "");
        _dDesc.text = s.NextDescription;

        var reqs = ShopUnlocks.RequirementsOf(s.id);
        if (reqs.Length == 0) _dReqs.text = "<color=#9A9A9A>Disponible desde el principio.</color>";
        else
        {
            var sb = new System.Text.StringBuilder("<b>Requisitos</b>\n");
            ShopCatalog cat = ShopCatalog.Load();
            foreach (ShopUnlocks.Req r in reqs)
            {
                ShopItem ri = cat.Find(r.id);
                bool ok = PlayerProgress.GetOwnedLevel(r.id) >= r.level;
                string nm = (ri != null ? ri.displayName : r.id) + (r.level > 1 ? $" (nv. {r.level})" : "");
                sb.Append(ok ? $"<color=#7CFF7C>•  {nm}</color>\n" : $"<color=#FF9B6B>•  {nm}</color>\n");
            }
            _dReqs.text = sb.ToString();
        }

        _dOwned.text = sLocked ? "<color=#FF9B6B>BLOQUEADO</color>" : s.OwnedLabel;

        bool showToggle = s.kind == ShopItemKind.Upgrade && s.Owned > 0;
        _dToggle.gameObject.SetActive(showToggle);
        if (showToggle)
        {
            bool on = PlayerProgress.IsUpgradeEnabled(s.id);
            bool prev = _refreshing; _refreshing = true;
            _dToggle.SetIsOnWithoutNotify(on);
            _refreshing = prev;
            _dToggleLabel.text  = on ? "Activa" : "Apagada";
            _dToggleLabel.color = on ? Color.white : new Color(1f, 1f, 1f, 0.5f);
        }

        if (sLocked)       { _dBuyLabel.text = "BLOQUEADO"; _dBuy.interactable = false; }
        else if (s.IsMaxed){ _dBuyLabel.text = s.kind == ShopItemKind.Upgrade ? "AL MÁXIMO" : "LLENO"; _dBuy.interactable = false; }
        else
        {
            string verb = s.kind == ShopItemKind.Upgrade ? (s.Owned == 0 ? "COMPRAR" : "MEJORAR") : "COMPRAR";
            _dBuyLabel.text = $"{verb} · {s.NextPrice}";
            _dBuy.interactable = PlayerProgress.Coins >= s.NextPrice;
        }
    }
}
