using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// PUESTO DE CONTRATOS (pantalla, se construye sola por código). Se abre con ContractUI.Open() — normalmente desde el
/// puesto físico del freeroam (ContractStand).
/// Lista todos los contratos por dificultad (Encargo / Contrato / Gran contrato) con su pago:
///   • ELEGIR     → lo añades a la hoja de firma (abajo). Cuando tengas los que quieras, FIRMA con el ratón en el
///                  recuadro y pulsa FIRMAR: quedan activos (máx. ContractCatalog.MaxActive a la vez).
///   • RESCINDIR  → quitas uno ya firmado (gratis) para dejar hueco a otro.
///   • COBRAR     → cumplido: pulsa para llevarte las monedas (no se cobran solos).
///   • COBRADO    → ya lo cobraste.
/// Esc o «Salir» la cierran.
/// </summary>
public class ContractUI : MonoBehaviour
{
    public static bool IsOpen => _instance != null;
    private static ContractUI _instance;
    private static int _closedFrame = -10;
    private static float _closedTime = -10f;

    /// <summary>¿Se acaba de cerrar? (para que el Esc de cerrar no abra la pausa).</summary>
    public static bool ClosedRecently => Time.frameCount - _closedFrame <= 5 || Time.unscaledTime - _closedTime < 0.6f;

    private Action _onClosed;
    private CursorLockMode _prevLock;
    private bool _prevVisible;

    private TextMeshProUGUI _coinsText, _slotsText, _toast;
    private RectTransform _content;
    private float _toastT;
    private readonly List<Card> _cards = new List<Card>();

    // Hoja de firma
    private readonly List<string> _selected = new List<string>();   // elegidos, pendientes de firmar
    private SignaturePad _pad;
    private TextMeshProUGUI _summary, _padHint, _stamp, _signLabel;
    private Button _signButton;
    private float _stampT;
    private const float MinInk = 120f;   // trazo mínimo (px de textura) para que cuente como firma

    private class Card
    {
        public ContractDef def;
        public Image bg, band;
        public TextMeshProUGUI state, buttonLabel;
        public Button button;
    }

    private static readonly Color BgDim   = new Color(0f, 0f, 0f, 0.82f);
    private static readonly Color PanelBg = new Color(0.06f, 0.06f, 0.09f, 0.97f);
    private static readonly Color CardBg  = new Color(0.13f, 0.12f, 0.16f, 1f);
    private static readonly Color CardOn  = new Color(0.2f, 0.17f, 0.1f, 1f);
    private static readonly Color CardDone = new Color(0.09f, 0.12f, 0.09f, 1f);
    private static readonly Color Gold    = new Color(1f, 0.78f, 0.15f);
    private static readonly Color Paper   = new Color(0.93f, 0.86f, 0.7f);

    public static ContractUI Open(Action onClosed = null)
    {
        if (_instance != null) return _instance;
        var go = new GameObject("ContractUI");
        _instance = go.AddComponent<ContractUI>();
        _instance._onClosed = onClosed;
        return _instance;
    }

    public void Close()
    {
        // (Se reaplica unos frames: si se cerró con Esc, el editor desbloquearía el ratón justo después.)
        CursorRestorer.Restore(_prevLock, _prevVisible);
        if (_instance == this) _instance = null;
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
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (_toastT > 0f)
        {
            _toastT -= Time.unscaledDeltaTime;
            Color c = _toast.color; c.a = Mathf.Clamp01(_toastT / 0.4f); _toast.color = c;
        }
        if (_stampT > 0f)
        {
            // Sello «FIRMADO»: entra grande y se asienta; luego se desvanece y la hoja queda limpia.
            _stampT -= Time.unscaledDeltaTime;
            float age = 1.6f - _stampT;
            float s = Mathf.Lerp(1.8f, 1f, Mathf.Clamp01(age / 0.18f));
            _stamp.rectTransform.localScale = Vector3.one * s;
            Color c = _stamp.color; c.a = Mathf.Clamp01(_stampT / 0.5f); _stamp.color = c;
            if (_stampT <= 0f) { _stamp.gameObject.SetActive(false); _pad.Clear(); }
        }
    }

    // ── Construcción ──────────────────────────────────────────────────────────

    private void Build()
    {
        Canvas canvas = UIKit.CreateCanvas("ContractCanvas", 1000, transform);

        var dim = UIKit.Panel("Dim", canvas.transform, BgDim, false);
        UIKit.Stretch(dim.rectTransform);
        dim.raycastTarget = true;

        var panel = UIKit.Panel("Panel", canvas.transform, PanelBg);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1640f, 960f));
        Transform p = panel.transform;

        var title = UIKit.Label("Title", p, "CONTRATOS", 64f, Paper, TextAlignmentOptions.Left, true);
        UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -26f), new Vector2(800f, 80f));

        var sub = UIKit.Label("Sub", p,
            "Elige retos, fírmalos abajo y cúmplelos al completar un nivel para cobrarlos. Cada contrato se paga una sola vez. " +
            "No cuentan en la pelea del jefe.", 22f, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.TopLeft);
        UIKit.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(52f, -104f), new Vector2(1100f, 60f));

        var coinIcon = UIKit.CoinIcon(p, 48f);
        UIKit.Place(coinIcon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -40f), new Vector2(48f, 48f));
        _coinsText = UIKit.Label("Coins", p, "0", 50f, Gold, TextAlignmentOptions.Right, true);
        UIKit.Place(_coinsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-112f, -30f), new Vector2(400f, 70f));

        _slotsText = UIKit.Label("Slots", p, "", 26f, Paper, TextAlignmentOptions.Right, true);
        UIKit.Place(_slotsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -104f), new Vector2(500f, 40f));

        // Zona con scroll
        var viewport = UIKit.Node("Viewport", p);
        UIKit.Stretch(viewport, 40, 40, 160, 290);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0.001f);

        var content = UIKit.Node("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        _content = content;
        var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 14f; vl.padding = new RectOffset(8, 8, 8, 8);
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        // Secciones por dificultad
        for (int tier = 1; tier <= 3; tier++)
        {
            BuildHeader(tier);
            var gridRt = UIKit.Node("Grid" + tier, content);
            var grid = gridRt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(500f, 250f);
            grid.spacing  = new Vector2(20f, 20f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperLeft;
            foreach (ContractDef d in ContractCatalog.All)
                if (d.tier == tier) _cards.Add(BuildCard(gridRt, d));
        }

        BuildSignSheet(p);

        _toast = UIKit.Label("Toast", p, "", 26f, Color.white, TextAlignmentOptions.Center, true);
        UIKit.Place(_toast.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 246f), new Vector2(1300f, 38f));
    }

    /// <summary>Hoja de firma (abajo): resumen de lo elegido, recuadro para firmar con el ratón y botones.</summary>
    private void BuildSignSheet(Transform p)
    {
        var sheet = UIKit.Panel("SignSheet", p, new Color(0.16f, 0.14f, 0.12f, 1f));
        UIKit.Place(sheet.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1560f, 214f));
        Transform t = sheet.transform;

        var head = UIKit.Label("Head", t, "HOJA DE FIRMA", 24f, Paper, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -14f), new Vector2(540f, 32f));

        _summary = UIKit.Label("Summary", t, "", 20f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
        _summary.richText = true;
        UIKit.Place(_summary.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -50f), new Vector2(560f, 96f));

        var back = UIKit.MakeButton("Back", t, "SALIR  (Esc)", 24f, new Color(0.3f, 0.3f, 0.35f), Color.white, out _);
        UIKit.Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 14f), new Vector2(240f, 50f));
        back.onClick.AddListener(Close);

        // Recuadro de firma (papel) con marco
        var frame = UIKit.Panel("PadFrame", t, new Color(0.55f, 0.45f, 0.3f, 1f));
        UIKit.Place(frame.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(610f, 0f), new Vector2(660f, 186f));
        _pad = SignaturePad.Create(frame.transform, new Vector2(648f, 174f));
        UIKit.Place((RectTransform)_pad.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(648f, 174f));
        _pad.OnChanged += Refresh;

        _padHint = UIKit.Label("Hint", _pad.transform, "Firma aquí con el ratón", 22f, new Color(0.3f, 0.25f, 0.2f, 0.55f),
                               TextAlignmentOptions.Center, false);
        UIKit.Place(_padHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(600f, 32f));

        _stamp = UIKit.Label("Stamp", frame.transform, "FIRMADO", 72f, new Color(0.8f, 0.15f, 0.15f, 0.9f), TextAlignmentOptions.Center, true);
        UIKit.Place(_stamp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 120f));
        _stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 12f);
        _stamp.gameObject.SetActive(false);

        // Botones: firmar / borrar
        _signButton = UIKit.MakeButton("Sign", t, "FIRMAR", 30f, new Color(0.2f, 0.55f, 0.3f), Color.white, out _signLabel);
        UIKit.Place((RectTransform)_signButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -20f), new Vector2(250f, 96f));
        _signButton.onClick.AddListener(Sign);

        var clear = UIKit.MakeButton("Clear", t, "BORRAR FIRMA", 22f, new Color(0.35f, 0.3f, 0.28f), Color.white, out _);
        UIKit.Place((RectTransform)clear.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 14f), new Vector2(250f, 56f));
        clear.onClick.AddListener(() => { if (_stampT <= 0f) _pad.Clear(); });
    }

    private void BuildHeader(int tier)
    {
        Color col = ContractDef.TierColor(tier);
        var h = UIKit.Label("Header" + tier, _content, ContractDef.TierName(tier).ToUpper() +
                            (tier == 1 ? "  ·  fáciles" : tier == 2 ? "  ·  medios" : "  ·  difíciles"),
                            34f, col, TextAlignmentOptions.Left, true);
        var le = h.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 52f;
    }

    private Card BuildCard(Transform parent, ContractDef d)
    {
        var card = new Card { def = d };
        Color col = ContractDef.TierColor(d.tier);
        card.bg = UIKit.Panel(d.id, parent, CardBg);
        Transform t = card.bg.transform;

        card.band = UIKit.Panel("Band", t, col);
        UIKit.Place(card.band.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(10f, 226f));

        var nameTxt = UIKit.Label("Name", t, d.title, 30f, Color.white, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(nameTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -16f), new Vector2(310f, 44f));

        // Pago
        var coin = UIKit.CoinIcon(t, 28f);
        UIKit.Place(coin.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -22f), new Vector2(28f, 28f));
        var pay = UIKit.Label("Reward", t, "+" + d.reward, 28f, Gold, TextAlignmentOptions.Right, true);
        UIKit.Place(pay.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-54f, -16f), new Vector2(140f, 40f));

        string ruleTxt = d.rules != ContractRule.None ? "  ·  <color=#FF9B6B>CAMBIA LAS REGLAS</color>" : "";
        var kind = UIKit.Label("Kind", t, ContractDef.TierName(d.tier).ToUpper() + ruleTxt, 16f, col, TextAlignmentOptions.TopLeft, true);
        kind.richText = true;
        UIKit.Place(kind.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -60f), new Vector2(460f, 22f));

        var desc = UIKit.Label("Desc", t, d.description, 21f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
        UIKit.Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -88f), new Vector2(460f, 90f));

        card.state = UIKit.Label("State", t, "", 22f, Paper, TextAlignmentOptions.Left, true);
        UIKit.Place(card.state.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 22f), new Vector2(230f, 40f));

        card.button = UIKit.MakeButton("Action", t, "", 24f, col * 0.85f + new Color(0f, 0f, 0f, 0.15f), Color.white, out card.buttonLabel);
        UIKit.Place((RectTransform)card.button.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 16f), new Vector2(230f, 54f));
        card.button.onClick.AddListener(() => OnAction(card));
        return card;
    }

    // ── Acciones ──────────────────────────────────────────────────────────────

    private static int ActiveCount
    {
        get
        {
            int n = 0;
            foreach (string id in PlayerProgress.ActiveContracts) if (ContractCatalog.Get(id) != null) n++;
            return n;
        }
    }

    private void OnAction(Card c)
    {
        string id = c.def.id;
        if (PlayerProgress.IsContractClaimable(id))
        {
            if (PlayerProgress.ClaimContract(id, c.def.reward))
                ShowToast($"¡Cobrado «{c.def.title}»!  +{c.def.reward} monedas", new Color(1f, 0.85f, 0.3f));
            return;
        }
        if (PlayerProgress.IsContractDone(id)) return;

        if (PlayerProgress.IsContractActive(id))
        {
            PlayerProgress.SetContractActive(id, false);
            ShowToast($"Has rescindido «{c.def.title}».", Color.white);
            return;
        }
        if (_selected.Contains(id))
        {
            _selected.Remove(id);
            Refresh();
            return;
        }
        if (ActiveCount + _selected.Count >= ContractCatalog.MaxActive)
        {
            ShowToast($"Solo puedes tener {ContractCatalog.MaxActive} contratos a la vez (firmados + elegidos).", new Color(1f, 0.5f, 0.5f));
            return;
        }
        _selected.Add(id);
        Refresh();
    }

    /// <summary>Firma la hoja: todos los elegidos pasan a estar activos.</summary>
    private void Sign()
    {
        if (_stampT > 0f) return;
        if (_selected.Count == 0)
        {
            ShowToast("Elige primero algún contrato (botón ELEGIR).", new Color(1f, 0.5f, 0.5f));
            return;
        }
        if (_pad.Ink < MinInk)
        {
            ShowToast("Tienes que firmar en el recuadro con el ratón.", new Color(1f, 0.5f, 0.5f));
            return;
        }

        // (Copia: al activar cada uno salta PlayerProgress.OnChanged → Refresh, que limpia _selected.)
        var toSign = new List<string>(_selected);
        _selected.Clear();
        var names = new List<string>();
        foreach (string id in toSign)
        {
            if (ActiveCount >= ContractCatalog.MaxActive) break;
            PlayerProgress.SetContractActive(id, true);
            ContractDef d = ContractCatalog.Get(id);
            if (d != null) names.Add("«" + d.title + "»");
        }
        _selected.Clear();

        _stamp.gameObject.SetActive(true);
        _stampT = 1.6f;
        ShowToast($"¡Firmado! {string.Join(", ", names)}. Cúmplelo en el próximo nivel.", new Color(0.5f, 1f, 0.5f));
        Refresh();
    }

    private void Refresh()
    {
        if (_coinsText != null) _coinsText.text = PlayerProgress.Coins.ToString();
        int active = ActiveCount, done = 0;
        foreach (ContractDef d in ContractCatalog.All) if (PlayerProgress.IsContractDone(d.id)) done++;
        if (_slotsText != null)
        {
            int toClaim = 0;
            foreach (ContractDef d in ContractCatalog.All) if (PlayerProgress.IsContractClaimable(d.id)) toClaim++;
            _slotsText.text = $"Firmados {active}/{ContractCatalog.MaxActive}   ·   Cumplidos {done}/{ContractCatalog.All.Count}" +
                              (toClaim > 0 ? $"   ·   <color=#FFC83A>{toClaim} por cobrar</color>" : "");
            _slotsText.richText = true;
        }

        _selected.RemoveAll(id => PlayerProgress.IsContractActive(id) || PlayerProgress.IsContractDone(id));
        bool full = active + _selected.Count >= ContractCatalog.MaxActive;

        // Hoja de firma
        if (_summary != null)
        {
            if (_selected.Count == 0)
                _summary.text = "Elige uno o varios contratos con <b>ELEGIR</b>, firma en el recuadro y pulsa <b>FIRMAR</b>.";
            else
            {
                int total = 0;
                var sb = new System.Text.StringBuilder();
                foreach (string id in _selected)
                {
                    ContractDef d = ContractCatalog.Get(id);
                    if (d == null) continue;
                    total += d.reward;
                    sb.Append($"• {d.title}  <color=#FFC83A>+{d.reward}</color>\n");
                }
                sb.Append($"<b>Total: <color=#FFC83A>{total}</color> monedas</b>");
                _summary.text = sb.ToString();
            }
            bool signed = _pad.Ink >= MinInk;
            _padHint.gameObject.SetActive(_pad.IsEmpty);
            _signButton.interactable = _selected.Count > 0 && signed && _stampT <= 0f;
            _signLabel.text = _selected.Count > 0 ? $"FIRMAR ({_selected.Count})" : "FIRMAR";
        }

        foreach (Card c in _cards)
        {
            Color col = ContractDef.TierColor(c.def.tier);
            if (PlayerProgress.IsContractClaimable(c.def.id))
            {
                // Cumplido y SIN cobrar: hay que pulsar para llevarse el dinero.
                c.bg.color = new Color(0.22f, 0.18f, 0.06f, 1f);
                c.state.text = "<color=#FFC83A>¡CUMPLIDO!</color>";
                c.buttonLabel.text = $"COBRAR  +{c.def.reward}";
                c.button.interactable = true;
                c.button.image.color = new Color(0.85f, 0.65f, 0.1f);
            }
            else if (PlayerProgress.IsContractDone(c.def.id))
            {
                c.bg.color = CardDone;
                c.state.text = "<color=#7CFF7C>CUMPLIDO</color>";
                c.buttonLabel.text = "COBRADO";
                c.button.interactable = false;
                c.button.image.color = new Color(0.3f, 0.3f, 0.32f);
            }
            else if (PlayerProgress.IsContractActive(c.def.id))
            {
                c.bg.color = CardOn;
                c.state.text = "<color=#FFC83A>FIRMADO</color>";
                c.buttonLabel.text = "RESCINDIR";
                c.button.interactable = true;
                c.button.image.color = new Color(0.45f, 0.2f, 0.2f);
            }
            else if (_selected.Contains(c.def.id))
            {
                c.bg.color = new Color(0.14f, 0.2f, 0.26f, 1f);
                c.state.text = "<color=#8FD3FF>ELEGIDO</color>";
                c.buttonLabel.text = "QUITAR";
                c.button.interactable = true;
                c.button.image.color = new Color(0.3f, 0.35f, 0.45f);
            }
            else
            {
                c.bg.color = CardBg;
                c.state.text = "";
                c.buttonLabel.text = "ELEGIR";
                c.button.interactable = !full;
                c.button.image.color = col * 0.85f + new Color(0f, 0f, 0f, 0.15f);
            }
            c.state.richText = true;
        }
    }

    private void ShowToast(string msg, Color color)
    {
        _toast.text  = msg;
        _toast.color = color;
        _toastT = 2f;
    }
}
