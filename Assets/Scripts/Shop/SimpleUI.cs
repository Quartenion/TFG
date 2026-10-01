using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Estilo SIMPLE compartido por la tienda (ShopUI) y los contratos (ContractUI): cajas cuadradas en grises, texto
/// con la fuente por defecto y botones planos. Cambia aquí los colores y tamaños de letra de las dos pantallas.
/// Úsalo con «using static SimpleUI;».
/// </summary>
public static class SimpleUI
{
    // ── Colores ───────────────────────────────────────────────────────────────
    public static readonly Color Back     = new Color(0f, 0f, 0f, 0.85f);      // fondo que oscurece el juego
    public static readonly Color PanelCol = new Color(0.08f, 0.08f, 0.08f, 1f);
    public static readonly Color BoxCol   = new Color(0.13f, 0.13f, 0.13f, 1f); // ficha de la derecha
    public static readonly Color ItemCol  = new Color(0.22f, 0.22f, 0.22f, 1f); // casilla de la rejilla
    public static readonly Color SelCol   = new Color(0.45f, 0.45f, 0.45f, 1f); // casilla / pestaña seleccionada
    public static readonly Color LockCol  = new Color(0.12f, 0.12f, 0.12f, 1f); // casilla apagada (bloqueada / cobrada)
    public static readonly Color BtnCol   = new Color(0.3f, 0.3f, 0.3f, 1f);
    public static readonly Color TextCol  = Color.white;
    public static readonly Color DimText  = new Color(1f, 1f, 1f, 0.55f);

    // ── Letra ─────────────────────────────────────────────────────────────────
    public const float FontBig = 34f, FontMid = 22f, FontSmall = 17f;

    // ── Piezas ────────────────────────────────────────────────────────────────

    public static Image Box(string name, Transform parent, Color c)
    {
        var img = UIKit.Node(name, parent).gameObject.AddComponent<Image>();
        img.color = c;
        return img;
    }

    public static TextMeshProUGUI Lbl(string name, Transform parent, string s, float size, TextAlignmentOptions align)
    {
        var t = UIKit.Node(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        t.text = s; t.fontSize = size; t.color = TextCol; t.alignment = align;
        t.richText = false;
        t.raycastTarget = false;
        return t;
    }

    public static Button Btn(string name, Transform parent, string label, out TextMeshProUGUI text)
    {
        var img = Box(name, parent, BtnCol);
        var b = img.gameObject.AddComponent<Button>();
        text = Lbl("Label", img.transform, label, FontMid, TextAlignmentOptions.Center);
        Stretch(text.rectTransform, 6f, 6f, 4f, 4f);
        return b;
    }

    /// <summary>Quita etiquetas de formato (&lt;i&gt;…) de los textos de los catálogos.</summary>
    public static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<.*?>", "");

    // ── Colocación ────────────────────────────────────────────────────────────

    public static void Stretch(RectTransform rt, float l, float r, float t, float b)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
    }

    public static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot ?? anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    public static void PlaceTopLeft(RectTransform rt, float x, float y, float w, float h)    => Place(rt, new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));
    public static void PlaceTopRight(RectTransform rt, float x, float y, float w, float h)   => Place(rt, new Vector2(1f, 1f), new Vector2(-x, -y), new Vector2(w, h));
    public static void PlaceBottomLeft(RectTransform rt, float x, float y, float w, float h) => Place(rt, new Vector2(0f, 0f), new Vector2(x, y), new Vector2(w, h));

    /// <summary>Rejilla con scroll de casillas cuadradas (lado 'cell', 'columns' columnas). Devuelve el contenedor.</summary>
    public static RectTransform MakeGrid(Transform box, float cell, float gap, int columns)
    {
        var viewport = UIKit.Node("Viewport", box);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);   // recibe la rueda del ratón

        var content = UIKit.Node("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var g = content.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(cell, cell);
        g.spacing  = new Vector2(gap, gap);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = columns;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return content;
    }
}
