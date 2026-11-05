using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// Notas que caen en el monitor del corazón, que usan el tutorial y las pistas. Cada nota es una tecla de teclado
/// dibujada (o varias, como "A + SHIFT"), con la cara clara, el canto oscuro debajo y un borde del color de la nota
/// (azul si tiene explicación y amarillo si es normal). Baja desde arriba y llega al recuadro del monitor justo en su
/// beat. Si se acierta, se pone verde y crece, y si pasa sin hacerla, se apaga. Se dibujan en un Canvas propio
/// ('canvas'), encima del HUD.
/// </summary>
public class HeartMonitorNotes
{
    public const float LeadBeats = 3f;   // la nota aparece 3 beats antes

    private class Note
    {
        public RectTransform rt;
        public CanvasGroup group;
        public readonly List<Image> faces = new List<Image>();
        public float beat;
        public float hitT = -1f;   // tiempo real desde que se acertó
        public bool fading;
    }

    private readonly RectTransform _canvas;
    private readonly Dictionary<int, Note> _notes = new Dictionary<int, Note>();
    private RectTransform _box;

    public HeartMonitorNotes(RectTransform canvas) { _canvas = canvas; }

    public bool Has(int id) => _notes.ContainsKey(id);
    public float BeatOf(int id) => _notes.TryGetValue(id, out Note n) ? n.beat : float.NaN;
    public IEnumerable<int> Ids => _notes.Keys;

    // Aspecto de las teclas
    private const float KeyH = 54f, KeyMinW = 54f, KeyEdge = 7f, KeyBorder = 4f, KeyGap = 10f;
    private static readonly Color KeyFace = new Color(0.93f, 0.92f, 0.9f, 1f);
    private static readonly Color KeyEdgeCol = new Color(0.42f, 0.4f, 0.38f, 1f);
    private static readonly Color KeyText = new Color(0.12f, 0.1f, 0.1f, 1f);

    /// <summary>
    /// Crea la nota 'id', si no existe, con una tecla por cada parte de 'label' separada por "+" ("A+SHIFT" da las
    /// teclas A y SHIFT). Si 'sibling' es mayor o igual que 0, la coloca en esa posición dentro del Canvas.
    /// </summary>
    public void Spawn(int id, float beat, string label, Color color, int sibling = -1)
    {
        if (_notes.ContainsKey(id)) return;
        var rootGo = new GameObject("Nota", typeof(RectTransform), typeof(CanvasGroup));
        var rt = (RectTransform)rootGo.transform;
        rt.SetParent(_canvas, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        var note = new Note { rt = rt, group = rootGo.GetComponent<CanvasGroup>(), beat = beat };
        note.group.blocksRaycasts = false; note.group.interactable = false;

        string[] keys = (label ?? "").Split('+');
        var widths = new float[keys.Length];
        float total = 0f;
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = keys[i].Trim().ToUpperInvariant();
            widths[i] = Mathf.Max(KeyMinW, 17f * keys[i].Length + 26f);
            if (keys[i] == "ESPACIO" || keys[i] == "SPACE") widths[i] = Mathf.Max(widths[i], 180f);   // la barra espaciadora es más larga
            total += widths[i] + (i > 0 ? KeyGap + 22f : 0f);
        }
        rt.sizeDelta = new Vector2(total, KeyH + KeyEdge);

        float x = -total * 0.5f;
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                // Signo "+" entre las teclas
                var plus = Lbl("Mas", rt, "+", FontMid, TextAlignmentOptions.Center);
                plus.color = Color.white;
                plus.raycastTarget = false;
                Place(plus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x + (KeyGap + 22f) * 0.5f, 0f), new Vector2(22f, KeyH), new Vector2(0.5f, 0.5f));
                x += KeyGap + 22f;
            }
            float w = widths[i], cx = x + w * 0.5f;
            // Borde del color de la nota, canto oscuro (para que la tecla parezca tener altura) y cara de la tecla con su letra.
            Img(rt, "Borde", new Color(color.r, color.g, color.b, 1f), new Vector2(cx, -KeyEdge * 0.5f), new Vector2(w + KeyBorder * 2f, KeyH + KeyEdge + KeyBorder * 2f));
            Img(rt, "Canto", KeyEdgeCol, new Vector2(cx, -KeyEdge * 0.5f), new Vector2(w, KeyH + KeyEdge));
            Image face = Img(rt, "Tecla", KeyFace, new Vector2(cx, 0f), new Vector2(w, KeyH));   // el canto asoma por debajo
            note.faces.Add(face);
            var t = Lbl("Letra", face.transform, keys[i], keys[i].Length <= 2 ? FontMid + 6f : FontMid, TextAlignmentOptions.Center);
            t.color = KeyText;
            t.raycastTarget = false;
            t.enableAutoSizing = true; t.fontSizeMin = 10f; t.fontSizeMax = keys[i].Length <= 2 ? FontMid + 6f : FontMid;
            Stretch(t.rectTransform, 4f, 4f, 2f, 2f);
            x += w;
        }
        if (sibling >= 0) rt.SetSiblingIndex(sibling);
        _notes[id] = note;
    }

    private static Image Img(RectTransform parent, string name, Color c, Vector2 pos, Vector2 size)
    {
        var img = Box(name, parent, c);
        img.sprite = UIKit.RoundedSprite();
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        Place(img.rectTransform, new Vector2(0.5f, 0.5f), pos, size, new Vector2(0.5f, 0.5f));
        return img;
    }

    /// <summary>Nota acertada, que se pone verde y desaparece.</summary>
    public void Hit(int id) { if (_notes.TryGetValue(id, out Note n) && n.hitT < 0f) n.hitT = 0f; }

    /// <summary>Nota que ha pasado sin hacerla, que se apaga.</summary>
    public void Fade(int id) { if (_notes.TryGetValue(id, out Note n) && n.hitT < 0f) n.fading = true; }

    public void Clear()
    {
        foreach (Note n in _notes.Values) if (n.rt != null) Object.Destroy(n.rt.gameObject);
        _notes.Clear();
    }

    // -=+=- Monitor -=+=-

    private RectTransform MonitorBox()
    {
        if (_box != null) return _box;
        RhythmHeartMonitor m = Object.FindAnyObjectByType<RhythmHeartMonitor>();
        if (m != null) _box = m.transform.parent as RectTransform;
        return _box;
    }

    /// <summary>Recuadro del monitor del corazón en píxeles de pantalla. Funciona con cualquier modo de Canvas.</summary>
    public bool ScreenRect(out Rect rect)
    {
        rect = default;
        RectTransform box = MonitorBox();
        if (box == null || !box.gameObject.activeInHierarchy) return false;
        Canvas cv = box.GetComponentInParent<Canvas>();
        Camera cam = cv != null && cv.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? cv.rootCanvas.worldCamera : null;
        var corners = new Vector3[4];
        box.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        return rect.width > 1f;
    }

    // -=+=- En cada fotograma -=+=-

    public void Update(float beatNow)
    {
        if (!ScreenRect(out Rect ecg)) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, ecg.center, null, out Vector2 center);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, ecg.max, null, out Vector2 topRight);
        float fall = Mathf.Max(220f, (topRight.y - center.y) * 6f);

        List<int> dead = null;
        foreach (var kv in _notes)
        {
            Note n = kv.Value;
            bool visible = !HudHider.IsHidden;
            if (n.rt.gameObject.activeSelf != visible) n.rt.gameObject.SetActive(visible);

            float u = Mathf.Clamp01((n.beat - beatNow) / LeadBeats);   // 1 es arriba y 0, en el monitor
            n.rt.anchoredPosition = center + new Vector2(0f, fall * u);

            bool gone = false;
            if (n.hitT >= 0f)
            {
                n.hitT += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(n.hitT / 0.35f);
                n.rt.localScale = Vector3.one * (1f + 0.6f * k);
                foreach (Image f in n.faces) if (f != null) f.color = Color.Lerp(KeyFace, new Color(0.3f, 1f, 0.4f, 1f), Mathf.Clamp01(k * 3f));
                n.group.alpha = 1f - k;
                gone = k >= 1f;
            }
            else if (n.fading)
            {
                n.group.alpha = Mathf.MoveTowards(n.group.alpha, 0f, Time.unscaledDeltaTime * 3f);
                gone = n.group.alpha <= 0f;
            }
            else
            {
                // Al llegar al monitor, la nota late para llamar la atención.
                float s = u <= 0.001f ? 1f + 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
                n.rt.localScale = Vector3.one * s;
            }
            if (gone) (dead ??= new List<int>()).Add(kv.Key);
        }
        if (dead == null) return;
        foreach (int id in dead) { if (_notes[id].rt != null) Object.Destroy(_notes[id].rt.gameObject); _notes.Remove(id); }
    }
}
