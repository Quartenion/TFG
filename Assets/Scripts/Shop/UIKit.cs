using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Pequeña fábrica de UI (uGUI + TextMeshPro) para construir la tienda y el HUD por código.</summary>
public static class UIKit
{
    private static Sprite _circle, _rounded;

    public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        if (parent != null) go.transform.SetParent(parent, false);
        var c = go.GetComponent<Canvas>();
        c.renderMode   = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = sortingOrder;
        var s = go.GetComponent<CanvasScaler>();
        s.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920f, 1080f);
        s.matchWidthOrHeight  = 0.5f;
        return c;
    }

    /// <summary>Asegura que hay un EventSystem (con el módulo del nuevo Input System) para poder pulsar botones.</summary>
    public static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    public static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
    }

    public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    public static Image Panel(string name, Transform parent, Color color, bool rounded = true)
    {
        var rt = Node(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        if (rounded) { img.sprite = RoundedSprite(); img.type = Image.Type.Sliced; }
        return img;
    }

    public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color,
                                       TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false)
    {
        var rt = Node(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        return t;
    }

    public static Button MakeButton(string name, Transform parent, string label, float fontSize, Color bg, Color fg,
                                out TextMeshProUGUI labelText)
    {
        var img = Panel(name, parent, bg);
        var b = img.gameObject.AddComponent<Button>();
        var cb = b.colors;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.disabledColor    = new Color(0.45f, 0.45f, 0.45f, 0.8f);
        b.colors = cb;
        labelText = Label("Label", img.transform, label, fontSize, fg, TextAlignmentOptions.Center, true);
        Stretch(labelText.rectTransform, 8, 8, 4, 4);
        return b;
    }

    /// <summary>Casilla (Toggle) con texto a la derecha.</summary>
    public static Toggle MakeToggle(string name, Transform parent, string label, float fontSize, Color accent,
                                    out TextMeshProUGUI labelText)
    {
        var root = Node(name, parent);
        var t = root.gameObject.AddComponent<Toggle>();

        var box = Panel("Box", root, new Color(1f, 1f, 1f, 0.9f));
        box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        box.rectTransform.pivot = new Vector2(0f, 0.5f);
        box.rectTransform.anchoredPosition = Vector2.zero;
        box.rectTransform.sizeDelta = new Vector2(fontSize * 1.2f, fontSize * 1.2f);

        var check = Panel("Check", box.transform, accent);
        Stretch(check.rectTransform, 4, 4, 4, 4);
        check.raycastTarget = false;

        labelText = Label("Label", root, label, fontSize, Color.white, TextAlignmentOptions.Left, true);
        labelText.rectTransform.anchorMin = new Vector2(0f, 0f); labelText.rectTransform.anchorMax = new Vector2(1f, 1f);
        labelText.rectTransform.offsetMin = new Vector2(fontSize * 1.6f, 0f); labelText.rectTransform.offsetMax = Vector2.zero;

        t.targetGraphic = box;
        t.graphic = check;
        t.isOn = true;
        return t;
    }

    /// <summary>Icono de moneda (círculo dorado).</summary>
    public static Image CoinIcon(Transform parent, float size)
    {
        var rt = Node("CoinIcon", parent);
        rt.sizeDelta = new Vector2(size, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = CircleSprite();
        img.color  = new Color(1f, 0.78f, 0.15f);
        img.raycastTarget = false;
        return img;
    }

    public static Sprite CircleSprite()
    {
        if (_circle != null) return _circle;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = n * 0.5f - 1f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
                float a = Mathf.Clamp01(r - d + 0.5f);
                float ring = d > r - 7f ? 0.8f : 1f;   // borde un poco más oscuro (parece moneda)
                tex.SetPixel(x, y, new Color(ring, ring, ring, a));
            }
        tex.Apply();
        _circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return _circle;
    }

    /// <summary>Sprite blanco con esquinas redondeadas (9-slice) para paneles y botones.</summary>
    public static Sprite RoundedSprite()
    {
        if (_rounded != null) return _rounded;
        const int n = 32; const float rad = 10f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, rad, n - rad), cy = Mathf.Clamp(y + 0.5f, rad, n - rad);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(rad - d + 0.5f)));
            }
        tex.Apply();
        _rounded = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(rad, rad, rad, rad));
        return _rounded;
    }
}
