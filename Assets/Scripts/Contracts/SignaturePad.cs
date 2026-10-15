using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// RECUADRO DE FIRMA: se dibuja con el ratón (clic izquierdo mantenido) sobre una textura, como con un bolígrafo.
/// Mide la "tinta" (longitud total del trazo en píxeles de la textura) para saber si de verdad se ha firmado.
/// Se crea por código con SignaturePad.Create().
/// </summary>
[RequireComponent(typeof(RawImage))]
public class SignaturePad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
{
    public Color inkColor   = new Color(0.08f, 0.1f, 0.35f, 1f);   // tinta azul oscura
    public Color paperColor = new Color(0.97f, 0.93f, 0.82f, 1f);
    public float penRadius  = 2.2f;                                  // grosor (píxeles de textura)

    /// <summary>Longitud total dibujada (px de textura).</summary>
    public float Ink { get; private set; }
    public bool  IsEmpty => Ink <= 0f;
    public event Action OnChanged;

    private RawImage  _img;
    private Texture2D _tex;
    private Color32[] _px;
    private int _w, _h;
    private bool _drawing, _dirty;
    private Vector2 _last;

    public static SignaturePad Create(Transform parent, Vector2 size, int texWidth = 640)
    {
        var rt = UIKit.Node("SignaturePad", parent);
        if (!UIBind.IsReused(rt)) rt.sizeDelta = size;
        UIBind.GetOrAdd<RawImage>(rt.gameObject);
        var pad = UIBind.GetOrAdd<SignaturePad>(rt.gameObject);
        if (UIBind.IsReused(rt)) size = rt.rect.size.sqrMagnitude > 1f ? rt.rect.size : size;   // (horneado: su tamaño)
        pad.Init(texWidth, Mathf.Max(8, Mathf.RoundToInt(texWidth * size.y / Mathf.Max(1f, size.x))));
        return pad;
    }

    private void Init(int w, int h)
    {
        _img = GetComponent<RawImage>();
        _w = w; _h = h;
        _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        _px = new Color32[w * h];
        _img.texture = _tex;
        _img.raycastTarget = true;
        Clear();
    }

    public void Clear()
    {
        if (_px == null) return;
        Color32 paper = paperColor;
        for (int i = 0; i < _px.Length; i++) _px[i] = paper;
        // Línea guía para firmar (gris claro, a 1/4 de la altura)
        Color32 guide = Color.Lerp(paperColor, Color.gray, 0.45f);
        int gy = Mathf.RoundToInt(_h * 0.25f);
        for (int x = Mathf.RoundToInt(_w * 0.05f); x < _w * 0.95f; x++) _px[gy * _w + x] = guide;
        Ink = 0f;
        _dirty = true;
        OnChanged?.Invoke();
    }

    private void LateUpdate()
    {
        if (!_dirty || _tex == null) return;
        _tex.SetPixels32(_px);
        _tex.Apply(false);
        _dirty = false;
    }

    private void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
    }

    // ── Dibujo ────────────────────────────────────────────────────────────────

    private bool ToTex(PointerEventData e, out Vector2 p)
    {
        p = Vector2.zero;
        var rt = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out Vector2 local))
            return false;
        Rect r = rt.rect;
        p = new Vector2((local.x - r.xMin) / r.width * _w, (local.y - r.yMin) / r.height * _h);
        return true;
    }

    // Sin umbral de arrastre: el trazo empieza desde el primer píxel que mueves el ratón.
    public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (!ToTex(e, out _last)) return;
        _drawing = true;
        Stamp(_last);
        Ink += 0.5f;
        _dirty = true;
        OnChanged?.Invoke();
    }

    public void OnDrag(PointerEventData e)
    {
        if (!_drawing || !ToTex(e, out Vector2 p)) return;
        float d = Vector2.Distance(_last, p);
        int steps = Mathf.Max(1, Mathf.CeilToInt(d / (penRadius * 0.5f)));
        for (int i = 1; i <= steps; i++) Stamp(Vector2.Lerp(_last, p, i / (float)steps));
        Ink += d;
        _last = p;
        _dirty = true;
        OnChanged?.Invoke();
    }

    public void OnPointerUp(PointerEventData e) => _drawing = false;

    /// <summary>Punto redondo con borde suave.</summary>
    private void Stamp(Vector2 c)
    {
        float r = penRadius;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - r - 1)), x1 = Mathf.Min(_w - 1, Mathf.CeilToInt(c.x + r + 1));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(c.y - r - 1)), y1 = Mathf.Min(_h - 1, Mathf.CeilToInt(c.y + r + 1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                float a = Mathf.Clamp01(r + 0.5f - d);
                if (a <= 0f) continue;
                int i = y * _w + x;
                _px[i] = Color32.Lerp(_px[i], (Color32)inkColor, a);
            }
    }
}
