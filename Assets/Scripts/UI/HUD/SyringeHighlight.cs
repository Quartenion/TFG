using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RESALTA UN ELEMENTO DEL HUD mientras el tutorial habla de él (sin oscurecer la pantalla):
///   • JERINGUILLA izquierda (roja) o derecha (azul): su modelo 3D cambia a un material que brilla de su color y late,
///     gira sobre su eje (ver SyringeSpinner) y su imagen late (crece y encoge).
///   • MONITOR DEL CORAZÓN: late y se enciende un brillo dorado detrás.
///   • Encima del elemento, una FLECHA hecha con los pinchos del crosshair (el «hittable» de delante, un poco más
///     pequeño, sobre el de detrás) que rebota apuntándole.
/// Funciona con el juego parado (tiempo real). Mientras hay algo resaltado, la caja de texto se pone arriba
/// (DialogBox mira <see cref="Current"/>). Lo llama TutorialDirector con <see cref="Set"/>.
/// </summary>
public class SyringeHighlight : MonoBehaviour
{
    public enum Side { None = 0, Left = 1, Right = 2, Monitor = 3 }

    private const float SpinSpeed = 300f;        // grados por segundo de la jeringuilla resaltada
    private const float ReturnSpeed = 540f;      // al dejar de resaltarla, vuelve a su giro normal
    private const float PulseAmount = 0.1f;      // cuánto crece al latir
    private const float PulseSpeed = 7f;
    private const float ArrowSize = 70f;         // tamaño de la flecha (px de la referencia 1920×1080)
    private const float ArrowFront = 0.72f;      // el pincho de delante, este tanto del de detrás
    private static readonly Color RedGlow = new Color(1f, 0.1f, 0.06f);
    private static readonly Color BlueGlow = new Color(0.25f, 0.6f, 1f);
    private static readonly Color MonitorGlow = new Color(1f, 0.8f, 0.25f);

    private static SyringeHighlight _instance;
    private static Side _side;
    private static readonly float[] Extra = new float[2];   // giro extra (grados) de cada jeringuilla

    // Elementos del HUD (0 izquierda, 1 derecha, 2 monitor).
    private readonly RectTransform[] _ui = new RectTransform[3];
    private readonly Vector3[] _baseScale = new Vector3[3];
    private readonly bool[] _scaled = new bool[3];
    private readonly Transform[] _model = new Transform[2];       // jeringuillas 3D («SyringeRed» / «SyringeBlue»)
    private readonly Camera[] _modelCam = new Camera[2];          // cámaras que las pintan en su imagen

    // Materiales brillantes de la jeringuilla resaltada.
    private readonly List<(Renderer r, Material[] shared)> _orig = new List<(Renderer, Material[])>();
    private readonly List<Material> _glowMats = new List<Material>();
    private int _glowing = -1;

    private RectTransform _canvas;
    private RectTransform _arrow;
    private Image _arrowBack, _arrowFront, _monitorGlow;
    private CanvasGroup _arrowGroup;
    private float _fade;
    private static Sprite _glowSprite, _triSprite;

    /// <summary>Resalta un elemento (o ninguno).</summary>
    public static void Set(Side side)
    {
        _side = side;
        if (side != Side.None && _instance == null) _instance = new GameObject("HUDHighlight").AddComponent<SyringeHighlight>();
    }

    public static Side Current => _side;

    /// <summary>Giro extra de la jeringuilla (0 = la roja / izquierda, 1 = la azul / derecha). Lo suma SyringeSpinner.</summary>
    public static float ExtraAngle(int index) => index >= 0 && index < 2 ? Extra[index] : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _side = Side.None; Extra[0] = Extra[1] = 0f; _instance = null; }

    private void Awake()
    {
        _canvas = (RectTransform)UIKit.CreateCanvas("ResalteCanvas", 720, transform).transform;
        // Flecha: pincho de detrás (oscuro) y, encima, el de delante (más pequeño, del color del elemento).
        var go = new GameObject("Flecha", typeof(RectTransform), typeof(CanvasGroup));
        _arrow = (RectTransform)go.transform;
        _arrow.SetParent(_canvas, false);
        _arrow.anchorMin = _arrow.anchorMax = new Vector2(0.5f, 0.5f);
        _arrow.pivot = new Vector2(0.5f, 0f);
        _arrow.sizeDelta = new Vector2(ArrowSize, ArrowSize);
        _arrowGroup = go.GetComponent<CanvasGroup>();
        _arrowGroup.alpha = 0f; _arrowGroup.blocksRaycasts = false;
        _arrowBack = Spike(_arrow, "BackSpikeHittable", 1f, new Color(0.15f, 0.15f, 0.15f));
        _arrowFront = Spike(_arrow, "FrontSpikeHittable", ArrowFront, Color.white);
        // Brillo del monitor.
        var mg = new GameObject("Brillo monitor", typeof(RectTransform), typeof(Image));
        _monitorGlow = mg.GetComponent<Image>();
        _monitorGlow.rectTransform.SetParent(_canvas, false);
        _monitorGlow.rectTransform.anchorMin = _monitorGlow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _monitorGlow.sprite = GlowSprite();
        _monitorGlow.raycastTarget = false;
        _monitorGlow.color = Color.clear;
        _monitorGlow.transform.SetAsFirstSibling();
    }

    private static Image Spike(RectTransform parent, string sprite, float scale, Color color)
    {
        var go = new GameObject(sprite, typeof(RectTransform), typeof(Image));
        var img = go.GetComponent<Image>();
        var rt = img.rectTransform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        // El de delante, CONCÉNTRICO con el de detrás: los dos triángulos comparten el centro (incentro), que en un
        // triángulo con la punta abajo está a 2/3 de su altura desde la punta. Así el margen es igual por los tres lados.
        float h = ArrowSize * 0.86f;   // (alto del de detrás: el sprite es un poco más ancho que alto)
        rt.sizeDelta = new Vector2(ArrowSize * scale, h * scale);
        rt.anchoredPosition = new Vector2(0f, (1f - scale) * h * (2f / 3f));
        img.sprite = FindSprite(sprite) ?? TriangleSprite();
        img.preserveAspect = true;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Sprite del proyecto ya cargado (los pinchos del crosshair los usan los anillos del ritmo).</summary>
    private static Sprite FindSprite(string name)
    {
        foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>()) if (s != null && s.name == name) return s;
        return null;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < 3; i++) RestoreScale(i);
        StopGlow();
        if (_instance == this) { _instance = null; _side = Side.None; Extra[0] = Extra[1] = 0f; }
    }

    // ── Elementos ────────────────────────────────────────────────────────────

    private RectTransform UI(int i)
    {
        if (_ui[i] != null) return _ui[i];
        if (i < 2)
        {
            string name = i == 0 ? "SyringeLeftRed" : "SyringeRightBlue";
            foreach (RawImage r in FindObjectsByType<RawImage>(FindObjectsInactive.Include))
                if (r.name == name) { _ui[i] = r.rectTransform; break; }
        }
        else
        {
            RhythmHeartMonitor m = FindAnyObjectByType<RhythmHeartMonitor>();
            if (m != null) _ui[i] = m.transform.parent as RectTransform;
        }
        if (_ui[i] != null) _baseScale[i] = _ui[i].localScale;
        return _ui[i];
    }

    private Transform Model(int i)
    {
        if (_model[i] != null) return _model[i];
        string name = i == 0 ? "SyringeRed" : "SyringeBlue";
        foreach (SyringeSpinner s in FindObjectsByType<SyringeSpinner>(FindObjectsInactive.Include))
            if (s.name == name) { _model[i] = s.transform; break; }
        string cam = i == 0 ? "SyringeRedCamera" : "SyringeBlueCamera";
        foreach (Camera c in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            if (c.name == cam) { _modelCam[i] = c; break; }
        return _model[i];
    }

    private static Color ColorOf(int i) => i == 0 ? RedGlow : i == 1 ? BlueGlow : MonitorGlow;

    /// <summary>Rectángulo en pantalla de un elemento de UI.</summary>
    private static Rect ScreenRectOf(RectTransform t)
    {
        Canvas cv = t.GetComponentInParent<Canvas>();
        Camera cam = cv != null && cv.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? cv.rootCanvas.worldCamera : null;
        var c = new Vector3[4];
        t.GetWorldCorners(c);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>
    /// Punto de pantalla encima del elemento. Jeringuillas: lo de arriba de su MODELO 3D tal y como sale en su imagen
    /// (la imagen es más grande que la jeringuilla); monitor: el borde de arriba de su recuadro.
    /// </summary>
    private bool TopOf(int i, out Vector2 screen, out float width)
    {
        screen = default; width = 0f;
        RectTransform ui = UI(i);
        if (ui == null || !ui.gameObject.activeInHierarchy) return false;
        Rect r = ScreenRectOf(ui);
        if (i < 2 && Model(i) != null && _modelCam[i] != null)
        {
            Bounds b = default; bool any = false;
            foreach (Renderer rd in _model[i].GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = rd.bounds; any = true; } else b.Encapsulate(rd.bounds);
            }
            if (any)
            {
                // Proyección de la caja del modelo con su cámara → coordenadas dentro de la imagen → pantalla.
                Vector2 vmin = new Vector2(1f, 1f), vmax = new Vector2(0f, 0f);
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = new Vector3(k % 2 == 0 ? b.min.x : b.max.x, (k / 2) % 2 == 0 ? b.min.y : b.max.y, k / 4 == 0 ? b.min.z : b.max.z);
                    Vector3 v = _modelCam[i].WorldToViewportPoint(p);
                    vmin = Vector2.Min(vmin, v); vmax = Vector2.Max(vmax, v);
                }
                vmin = Vector2.Max(vmin, Vector2.zero); vmax = Vector2.Min(vmax, Vector2.one);
                float sx = Mathf.Lerp(r.xMin, r.xMax, (vmin.x + vmax.x) * 0.5f);
                float sy = Mathf.Lerp(r.yMin, r.yMax, vmax.y);
                screen = new Vector2(sx, sy);
                width = (vmax.x - vmin.x) * r.width;
                return true;
            }
        }
        screen = new Vector2(r.center.x, r.yMax);
        width = r.width;
        return true;
    }

    // ── Cada fotograma ───────────────────────────────────────────────────────

    private void RestoreScale(int i)
    {
        if (_scaled[i] && _ui[i] != null) _ui[i].localScale = _baseScale[i];
        _scaled[i] = false;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime, time = Time.unscaledTime;
        _fade = Mathf.MoveTowards(_fade, _side == Side.None ? 0f : 1f, dt * 4f);
        float beat = 0.5f + 0.5f * Mathf.Sin(time * PulseSpeed);
        int active = (int)_side - 1;

        for (int i = 0; i < 3; i++)
        {
            bool on = active == i;
            if (i < 2)
            {
                if (on) Extra[i] = Mathf.Repeat(Extra[i] + SpinSpeed * dt, 360f);
                else Extra[i] = Mathf.MoveTowardsAngle(Extra[i], 0f, ReturnSpeed * dt);
            }
            RectTransform t = on ? UI(i) : _ui[i];
            if (t != null)
            {
                if (on) { _scaled[i] = true; t.localScale = _baseScale[i] * (1f + PulseAmount * beat * _fade); }
                else RestoreScale(i);
            }
        }

        // Material brillante en la jeringuilla resaltada.
        int want = active >= 0 && active < 2 ? active : -1;
        if (want != _glowing) { StopGlow(); if (want >= 0) StartGlow(want); }
        if (_glowing >= 0)
            foreach (Material m in _glowMats)
                if (m != null) m.SetColor("_EmissionColor", ColorOf(_glowing) * (0.8f + 2.2f * beat));

        // Brillo del monitor.
        Color mc = MonitorGlow;
        mc.a = active == 2 ? _fade * (0.5f + 0.4f * beat) : Mathf.MoveTowards(_monitorGlow.color.a, 0f, dt * 4f);
        _monitorGlow.color = mc;
        if (active == 2 && TopOf(2, out _, out _)) PlaceOver(_monitorGlow.rectTransform, ScreenRectOf(_ui[2]), 1.8f + 0.15f * beat);

        // Flecha.
        _arrowGroup.alpha = _fade;
        if (active >= 0 && TopOf(active, out Vector2 top, out _))
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, top, null, out Vector2 local);
            float bounce = Mathf.Abs(Mathf.Sin(time * 5f)) * 22f;
            _arrow.anchoredPosition = local + new Vector2(0f, 14f + bounce);
            _arrowFront.color = Color.Lerp(Color.white, ColorOf(active), 0.6f);
        }
    }

    private void PlaceOver(RectTransform rt, Rect screen, float scale)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, screen.min, null, out Vector2 a);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, screen.max, null, out Vector2 b);
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.sizeDelta = (b - a) * scale;
    }

    private void StartGlow(int i)
    {
        _glowing = i;
        if (Model(i) == null) return;
        foreach (Renderer r in _model[i].GetComponentsInChildren<Renderer>())
        {
            _orig.Add((r, r.sharedMaterials));
            foreach (Material m in r.materials)   // (copias: no se tocan los materiales del proyecto)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.Lerp(m.GetColor("_BaseColor"), ColorOf(i), 0.5f));
                _glowMats.Add(m);
            }
        }
    }

    private void StopGlow()
    {
        foreach (var (r, shared) in _orig) if (r != null) r.sharedMaterials = shared;
        _orig.Clear();
        foreach (Material m in _glowMats) if (m != null) Destroy(m);
        _glowMats.Clear();
        _glowing = -1;
    }

    // ── Sprites de reserva ───────────────────────────────────────────────────

    private static Sprite GlowSprite()
    {
        if (_glowSprite != null) return _glowSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * a * a));
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        _glowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return _glowSprite;
    }

    /// <summary>(Si no se encuentran los pinchos del crosshair.) Triángulo apuntando abajo.</summary>
    private static Sprite TriangleSprite()
    {
        if (_triSprite != null) return _triSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float fy = (y + 0.5f) / n, fx = Mathf.Abs((x + 0.5f) / n - 0.5f) * 2f;
            px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01((fy - fx) * n * 0.5f)));
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        _triSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0f));
        return _triSprite;
    }
}
