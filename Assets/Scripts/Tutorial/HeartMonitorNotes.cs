using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// NOTAS QUE CAEN EN EL MONITOR DEL CORAZÓN (las usan el tutorial y las pistas).
/// Cada nota es un recuadro con la tecla («ESPACIO», «A+SHIFT»…) que baja desde arriba y llega al recuadro del monitor
/// justo en su beat. Acertada → se pone verde y crece; pasada sin hacerla → se apaga.
/// Se dibujan en un Canvas propio ('canvas'), encima del HUD.
/// </summary>
public class HeartMonitorNotes
{
    public const float LeadBeats = 3f;   // la nota aparece 3 beats antes

    private class Note
    {
        public RectTransform rt;
        public Image bg;
        public TextMeshProUGUI label;
        public float beat;
        public float hitT = -1f;   // tiempo (real) desde que se acertó
        public bool fading;
    }

    private readonly RectTransform _canvas;
    private readonly Dictionary<int, Note> _notes = new Dictionary<int, Note>();
    private RectTransform _box;

    public HeartMonitorNotes(RectTransform canvas) { _canvas = canvas; }

    public bool Has(int id) => _notes.ContainsKey(id);
    public float BeatOf(int id) => _notes.TryGetValue(id, out Note n) ? n.beat : float.NaN;
    public IEnumerable<int> Ids => _notes.Keys;

    /// <summary>Crea la nota 'id' (si no existe). 'sibling' ≥ 0 la coloca en esa posición dentro del Canvas.</summary>
    public void Spawn(int id, float beat, string label, Color color, int sibling = -1)
    {
        if (_notes.ContainsKey(id)) return;
        var bg = Box("Nota", _canvas, new Color(color.r, color.g, color.b, 0.95f));
        bg.sprite = UIKit.RoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = false;
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(Mathf.Max(110f, 22f * label.Length + 40f), 46f);
        var t = Lbl("Tecla", bg.transform, label, FontMid, TextAlignmentOptions.Center);
        t.color = new Color(0.1f, 0.05f, 0.05f);
        Stretch(t.rectTransform, 4f, 4f, 2f, 2f);
        if (sibling >= 0) rt.SetSiblingIndex(sibling);
        _notes[id] = new Note { rt = rt, bg = bg, label = t, beat = beat };
    }

    /// <summary>Acertada: verde y desaparece.</summary>
    public void Hit(int id) { if (_notes.TryGetValue(id, out Note n) && n.hitT < 0f) n.hitT = 0f; }

    /// <summary>Pasada sin hacerla: se apaga.</summary>
    public void Fade(int id) { if (_notes.TryGetValue(id, out Note n) && n.hitT < 0f) n.fading = true; }

    public void Clear()
    {
        foreach (Note n in _notes.Values) if (n.rt != null) Object.Destroy(n.rt.gameObject);
        _notes.Clear();
    }

    // ── Monitor ──────────────────────────────────────────────────────────────

    private RectTransform MonitorBox()
    {
        if (_box != null) return _box;
        RhythmHeartMonitor m = Object.FindAnyObjectByType<RhythmHeartMonitor>();
        if (m != null) _box = m.transform.parent as RectTransform;
        return _box;
    }

    /// <summary>Recuadro del monitor del corazón en píxeles de pantalla (vale para cualquier modo de Canvas).</summary>
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

    // ── Cada frame ───────────────────────────────────────────────────────────

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

            float u = Mathf.Clamp01((n.beat - beatNow) / LeadBeats);   // 1 = arriba, 0 = en el monitor
            n.rt.anchoredPosition = center + new Vector2(0f, fall * u);

            bool gone = false;
            if (n.hitT >= 0f)
            {
                n.hitT += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(n.hitT / 0.35f);
                n.rt.localScale = Vector3.one * (1f + 0.6f * k);
                n.bg.color = new Color(0.3f, 1f, 0.4f, 1f - k);
                n.label.color = new Color(1f, 1f, 1f, 1f - k);
                gone = k >= 1f;
            }
            else if (n.fading)
            {
                Color c = n.bg.color; c.a = Mathf.MoveTowards(c.a, 0f, Time.unscaledDeltaTime * 3f);
                n.bg.color = c; n.label.color = new Color(n.label.color.r, n.label.color.g, n.label.color.b, c.a);
                gone = c.a <= 0f;
            }
            else
            {
                // Al llegar al monitor late para llamar la atención.
                float s = u <= 0.001f ? 1f + 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
                n.rt.localScale = Vector3.one * s;
            }
            if (gone) (dead ??= new List<int>()).Add(kv.Key);
        }
        if (dead == null) return;
        foreach (int id in dead) { if (_notes[id].rt != null) Object.Destroy(_notes[id].rt.gameObject); _notes.Remove(id); }
    }
}
