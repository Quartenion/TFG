using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sección de COSMÉTICOS de la tienda (parte de ShopUI). Se pagan con IRIS — las monedas que se consiguen MIRANDO
/// los ojos escondidos por los niveles (GazeCoin) —, nunca con monedas normales. Solo cambian el aspecto.
/// En cada hueco (corazón, mano, monedas, kayak) se lleva uno puesto; el «Original» siempre está disponible.
/// </summary>
public partial class ShopUI
{
    private static readonly Color IrisColor = new Color(0.7f, 0.45f, 1f);

    private bool _inCosmetics;
    private TextMeshProUGUI _cosmeticsCardInfo;

    private class CosCard
    {
        public CosmeticSlot slot;
        public CosmeticDef def;            // null = Original
        public Image bg;
        public TextMeshProUGUI state, buttonLabel;
        public Button button;
    }
    private readonly List<CosCard> _cosCards = new List<CosCard>();

    // ── Tarjeta en la pantalla de categorías ──────────────────────────────────

    private void BuildCosmeticsCategoryCard()
    {
        var bg = UIKit.Panel("Cosmetics", _content, CardBg);
        var btn = bg.gameObject.AddComponent<Button>();
        var cb = btn.colors;
        cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        cb.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(ShowCosmetics);
        Transform t = bg.transform;

        var band = UIKit.Panel("Band", t, IrisColor);
        band.raycastTarget = false;
        UIKit.Place(band.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(16f, 270f));

        var nameTxt = UIKit.Label("Name", t, "COSMÉTICOS", 44f, IrisColor, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(nameTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -30f), new Vector2(420f, 70f));

        var desc = UIKit.Label("Desc", t, "Cambia el aspecto. Se pagan con IRIS: los ojos escondidos por los niveles que se recogen mirándolos.",
                               22f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
        UIKit.Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -100f), new Vector2(420f, 100f));

        _cosmeticsCardInfo = UIKit.Label("Info", t, "", 21f, IrisColor, TextAlignmentOptions.BottomLeft, true);
        _cosmeticsCardInfo.richText = true;
        UIKit.Place(_cosmeticsCardInfo.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(50f, 66f), new Vector2(420f, 60f));

        var enter = UIKit.Label("Enter", t, "ENTRAR  →", 26f, Color.white, TextAlignmentOptions.Right, true);
        UIKit.Place(enter.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 26f), new Vector2(300f, 36f));
    }

    // ── Pantalla de cosméticos ────────────────────────────────────────────────

    private void ShowCosmetics()
    {
        _category = ShopCategory.Auto;
        _inCosmetics = true;
        ClearContent();
        ClearTree();
        _viewportGO.SetActive(true);
        _backLabel.text = "←  CATEGORÍAS  (Esc)";
        _grid.cellSize = new Vector2(365f, 250f);
        _grid.constraintCount = 4;

        foreach (CosmeticSlot slot in Cosmetics.Slots)
        {
            _cosCards.Add(BuildCosCard(slot, null));
            foreach (CosmeticDef d in Cosmetics.All) if (d.slot == slot) _cosCards.Add(BuildCosCard(slot, d));
        }
        Refresh();
    }

    private CosCard BuildCosCard(CosmeticSlot slot, CosmeticDef d)
    {
        var card = new CosCard { slot = slot, def = d };
        card.bg = UIKit.Panel(d != null ? d.id : "original_" + slot, _content, CardBg);
        Transform t = card.bg.transform;

        // Muestra del color
        var sw = UIKit.Node("Swatch", t).gameObject.AddComponent<Image>();
        sw.sprite = UIKit.CircleSprite();
        sw.color = d != null ? d.color : new Color(1f, 1f, 1f, 0.25f);
        sw.raycastTarget = false;
        UIKit.Place(sw.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(56f, 56f));

        var nameTxt = UIKit.Label("Name", t, d != null ? d.name : "Original", 28f, Color.white, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(nameTxt.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -18f), new Vector2(270f, 40f));

        var kind = UIKit.Label("Slot", t, Cosmetics.SlotName(slot).ToUpper(), 15f, IrisColor, TextAlignmentOptions.TopLeft, true);
        UIKit.Place(kind.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -58f), new Vector2(270f, 22f));

        var desc = UIKit.Label("Desc", t, d != null ? d.description : "El aspecto de siempre.", 19f,
                               new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.TopLeft);
        UIKit.Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -84f), new Vector2(330f, 70f));

        card.state = UIKit.Label("State", t, "", 19f, IrisColor, TextAlignmentOptions.Left, true);
        card.state.richText = true;
        UIKit.Place(card.state.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 78f), new Vector2(330f, 26f));

        card.button = UIKit.MakeButton("Action", t, "", 22f, IrisColor * 0.8f + new Color(0f, 0f, 0f, 0.2f), Color.white, out card.buttonLabel);
        UIKit.Place((RectTransform)card.button.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(330f, 54f));
        card.button.onClick.AddListener(() => OnCosmeticAction(card));
        return card;
    }

    private void OnCosmeticAction(CosCard c)
    {
        if (c.def == null) { Cosmetics.Equip(c.slot, null); ShowToast("Aspecto original puesto.", Color.white); return; }
        if (Cosmetics.Owns(c.def))
        {
            Cosmetics.Equip(c.slot, c.def.id);
            ShowToast($"{c.def.name}: ¡puesto!", new Color(0.8f, 0.65f, 1f));
            return;
        }
        if (Cosmetics.TryBuy(c.def)) ShowToast($"¡{c.def.name} comprado y puesto!", new Color(0.8f, 0.65f, 1f));
        else ShowToast($"Te faltan iris ({PlayerProgress.GazeCoins}/{c.def.price}). Búscalos por los niveles y míralos.",
                       new Color(1f, 0.5f, 0.5f));
    }

    private void RefreshCosmetics()
    {
        int iris = PlayerProgress.GazeCoins;

        if (_cosmeticsCardInfo != null)
        {
            int owned = 0;
            foreach (CosmeticDef d in Cosmetics.All) if (Cosmetics.Owns(d)) owned++;
            _cosmeticsCardInfo.text = $"Tienes <b>{iris} iris</b>  ·  {owned}/{Cosmetics.All.Count} cosméticos";
        }

        if (!_inCosmetics) return;
        _title.text = $"TIENDA  ·  COSMÉTICOS   <size=60%><color=#B98CFF>{iris} iris</color></size>";
        _title.richText = true;

        foreach (CosCard c in _cosCards)
        {
            CosmeticDef eq = Cosmetics.Equipped(c.slot);
            bool equipped = c.def == null ? eq == null : eq == c.def;
            bool owned = c.def == null || Cosmetics.Owns(c.def);

            c.bg.color = equipped ? Color.Lerp(CardBg, IrisColor, 0.3f) : CardBg;
            if (equipped)
            {
                c.state.text = "<color=#7CFF7C>PUESTO</color>";
                c.buttonLabel.text = "PUESTO";
                c.button.interactable = false;
            }
            else if (owned)
            {
                c.state.text = c.def == null ? "" : "Comprado";
                c.buttonLabel.text = "PONER";
                c.button.interactable = true;
            }
            else
            {
                bool can = iris >= c.def.price;
                c.state.text = can ? $"{c.def.price} iris" : $"<color=#FF7070>{c.def.price} iris</color>";
                c.buttonLabel.text = $"COMPRAR · {c.def.price} IRIS";
                c.button.interactable = can;
            }
        }
    }
}
