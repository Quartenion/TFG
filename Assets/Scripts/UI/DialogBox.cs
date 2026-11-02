using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// CAJA DE TEXTO de los avisos (nivel tutorial): abajo en la pantalla, sólida, con el texto apareciendo letra a letra.
///   • Letra GRANDE. Los textos largos se reparten en varias PÁGINAS (por frases), una detrás de otra.
///   • Cada página pasa sola cuando ha dado tiempo a leerla; tras la última, la caja se cierra sola.
///   • [Clic izquierdo] mientras se escribe → la página entera; [Clic] con la página entera → la siguiente; mantener el clic medio segundo
///     (con un aro que se va llenando) → se salta todo. Al cerrarse se llama a 'onClosed'.
/// Funciona en tiempo real (también con el juego parado). Se crea con <see cref="Create"/> dentro de un Canvas y hay
/// que llamar a <see cref="Tick"/> cada fotograma.
/// </summary>
public class DialogBox
{
    public const float CharsPerSecond = 55f;
    public const float HoldSeconds = 0.25f;   // (el aro se llena en este tiempo y se cierra a los 0,3 s: ver FullRingSeconds)
    /// <summary>Letras como máximo por página (con la letra grande caben unas tres líneas).</summary>
    public const int PageChars = 150;
    public const float BodySize = 36f;
    private const float TapSeconds = 0.15f;   // (pulsación corta = siguiente página; menos que lo que tarda el aro)
    private const float FullRingSeconds = 0.05f;  // (el aro se ve lleno este rato antes de cerrarse: 0,25 + 0,05 = 0,3 s)
    private bool _pressWhileTyping;               // (la pulsación empezó con el texto escribiéndose: soltar rápido solo lo completa)
    /// <summary>Tiempo de lectura tras escribirse: mínimo + por letra.</summary>
    public const float ReadMinSeconds = 2.5f, ReadPerChar = 0.045f;
    /// <summary>Posición de la caja (px de la referencia 1920×1080, desde abajo) y su alto.</summary>
    public const float BoxMargin = 24f, BoxHeight = 165f;   // (pegada al borde de la pantalla, con este margen)

    private readonly GameObject _root;
    private readonly TextMeshProUGUI _title, _body, _hint;
    private readonly Image _ring;
    private readonly TextMeshProUGUI _ringKey;
    private string _full = "";
    private float _shown;          // letras visibles (con decimales)
    private float _hold;           // segundos manteniendo Z
    private float _readLeft;       // segundos que quedan antes de pasar de página / cerrarse sola
    private bool _dismissable = true;
    private bool _autoClose = true;    // false = no pasa ni se cierra sola: solo con el clic (el aro lleno cierra)
    private bool _waitRelease;     // tras completar el texto con Z, hay que soltarla antes de poder mantenerla
    private Action _onClosed;
    private readonly List<string> _pages = new List<string>();
    private int _page;
    private bool _pressing;
    private readonly TextMeshProUGUI _pageLabel;

    public bool IsOpen => _root != null && _root.activeSelf;

    private RectTransform _boxRt;
    private bool _dockTop;

    /// <summary>
    /// Pegada al borde INFERIOR de la pantalla (encima de las jeringuillas) o, mientras se explica una jeringuilla
    /// (SyringeHighlight), al borde SUPERIOR para no taparla.
    /// </summary>
    private void Dock(bool top)
    {
        _dockTop = top;
        Vector2 a = new Vector2(0.5f, top ? 1f : 0f);
        Place(_boxRt, a, new Vector2(0f, top ? -BoxMargin : BoxMargin), new Vector2(1320f, BoxHeight), a);
    }

    /// <summary>
    /// ¿Vale el clic ahora para la caja? El clic izquierdo también es el latido del corazón: mientras se juega (y en el
    /// 3-2-1) NO se usa para los avisos, que entonces pasan solos. Lo pone CourseBuilder; null = siempre.
    /// </summary>
    public static Func<bool> ClickAllowed;
    public bool Typing => IsOpen && _shown < _full.Length;

    public static DialogBox Create(Transform canvas) => new DialogBox(canvas);

    private DialogBox(Transform canvas)
    {
        var box = Box("Caja de texto", canvas, new Color(0.06f, 0.05f, 0.05f, 1f));
        box.raycastTarget = false;
        // Pegada al borde inferior (tapa las jeringuillas y el monitor); arriba mientras se explica una jeringuilla.
        _boxRt = box.rectTransform;
        Dock(top: false);
        _root = box.gameObject;
        var border = Box("Borde", box.transform, new Color(0.55f, 0.42f, 0.25f, 1f));
        border.raycastTarget = false;
        Stretch(border.rectTransform, -4f, -4f, -4f, -4f);
        border.transform.SetAsFirstSibling();
        var inner = Box("Fondo", box.transform, new Color(0.06f, 0.05f, 0.05f, 1f));
        inner.raycastTarget = false;
        Stretch(inner.rectTransform, 0f, 0f, 0f, 0f);
        inner.transform.SetSiblingIndex(1);

        _title = Lbl("Título", box.transform, "", FontBig, TextAlignmentOptions.Left);
        _title.color = new Color(0.35f, 0.85f, 1f);
        Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -6f), new Vector2(1100f, 36f), new Vector2(0f, 1f));

        _body = Lbl("Texto", box.transform, "", BodySize, TextAlignmentOptions.TopLeft);
        _body.textWrappingMode = TextWrappingModes.Normal;
        _body.enableAutoSizing = false;            // (siempre grande: los textos largos van en varias páginas)
        _body.fontSize = BodySize;
        _body.lineSpacing = 6f;

        _pageLabel = Lbl("Página", box.transform, "", FontSmall + 2f, TextAlignmentOptions.BottomLeft);
        _pageLabel.color = DimText;
        Place(_pageLabel.rectTransform, new Vector2(0f, 0f), new Vector2(30f, 6f), new Vector2(200f, 28f), new Vector2(0f, 0f));
        Place(_body.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -42f), new Vector2(1160f, 92f), new Vector2(0f, 1f));

        _hint = Lbl("Pista", box.transform, "", FontSmall + 1f, TextAlignmentOptions.BottomRight);
        _hint.color = DimText;
        Place(_hint.rectTransform, new Vector2(1f, 0f), new Vector2(-90f, 6f), new Vector2(600f, 28f), new Vector2(1f, 0f));

        // Aro de «mantener para cerrar» con la tecla dentro.
        var ringBack = new GameObject("Aro (fondo)", typeof(RectTransform), typeof(Image));
        ringBack.transform.SetParent(box.transform, false);
        var rb = ringBack.GetComponent<Image>();
        rb.sprite = GazeChargeUI.RingSprite(); rb.color = new Color(1f, 1f, 1f, 0.18f); rb.raycastTarget = false;
        Place((RectTransform)ringBack.transform, new Vector2(1f, 0f), new Vector2(-16f, 8f), new Vector2(58f, 58f), new Vector2(1f, 0f));
        var ring = new GameObject("Aro", typeof(RectTransform), typeof(Image));
        ring.transform.SetParent(ringBack.transform, false);
        _ring = ring.GetComponent<Image>();
        _ring.sprite = GazeChargeUI.RingSprite();
        _ring.type = Image.Type.Filled; _ring.fillMethod = Image.FillMethod.Radial360;
        _ring.fillOrigin = (int)Image.Origin360.Top; _ring.fillClockwise = true; _ring.fillAmount = 0f;
        _ring.color = new Color(1f, 0.85f, 0.35f);
        _ring.raycastTarget = false;
        Stretch(_ring.rectTransform, 0f, 0f, 0f, 0f);
        _ringKey = Lbl("Tecla", ringBack.transform, Loc.T("Clic", "Click"), FontSmall, TextAlignmentOptions.Center);
        Stretch(_ringKey.rectTransform, 0f, 0f, 0f, 0f);

        _root.SetActive(false);
    }

    /// <summary>
    /// Muestra un aviso. dismissable = se puede cerrar manteniendo el clic (si no, solo se completa el texto).
    /// autoClose = false (explicaciones con el juego parado): no pasa de página ni se cierra sola; se pasa de página con
    /// un clic y se cierra SOLO cuando se llena el aro (manteniendo el clic).
    /// </summary>
    public void Show(string title, string text, bool dismissable = true, Action onClosed = null, bool autoClose = true,
                     bool singlePage = false)
    {
        _autoClose = autoClose;
        _pages.Clear();
        // singlePage: todo el texto en un solo bloque (la letra se encoge lo justo para que quepa). Para los avisos que
        // salen mientras no se puede hacer clic (p. ej. el consejo durante el 3-2-1): así no hay que pasar de página.
        if (singlePage) _pages.Add((text ?? "").Replace("\n", " ").Trim());
        else _pages.AddRange(Paginate(text ?? ""));
        _body.enableAutoSizing = singlePage;
        _body.fontSizeMin = 22f;
        _body.fontSizeMax = BodySize;
        if (!singlePage) _body.fontSize = BodySize;
        if (_pages.Count == 0) _pages.Add("");
        _page = 0;
        _hold = 0f;
        _pressing = false;
        _waitRelease = Mouse.current != null && Mouse.current.leftButton.isPressed;
        _dismissable = dismissable;
        _onClosed = onClosed;
        _title.text = title ?? "";
        _title.gameObject.SetActive(!string.IsNullOrEmpty(title));
        Place(_body.rectTransform, new Vector2(0f, 1f), new Vector2(30f, string.IsNullOrEmpty(title) ? -14f : -42f),
              new Vector2(1180f, string.IsNullOrEmpty(title) ? 120f : 92f), new Vector2(0f, 1f));
        _ring.fillAmount = 0f;
        _root.SetActive(true);
        StartPage();
    }

    private void StartPage()
    {
        _full = _pages[_page];
        _shown = 0f;
        _readLeft = ReadMinSeconds + _full.Length * ReadPerChar;
        _body.text = _full;
        _body.maxVisibleCharacters = 0;
        _pageLabel.text = _pages.Count > 1 ? $"{_page + 1}/{_pages.Count}" : "";
        UpdateHint();
    }

    /// <summary>Siguiente página, o se cierra si era la última.</summary>
    private void NextPage()
    {
        if (_page + 1 < _pages.Count) { _page++; StartPage(); return; }
        if (!_dismissable || !_autoClose) return;   // (sin cierre automático: solo con el aro lleno)
        Close();
    }

    private void Close()
    {
        Action cb = _onClosed;
        Hide();
        cb?.Invoke();
    }

    /// <summary>Reparte el texto en páginas de como mucho PageChars letras, cortando por párrafos y frases.</summary>
    private static List<string> Paginate(string text)
    {
        var pages = new List<string>();
        foreach (string para in text.Split('\n'))
        {
            string p = para.Trim();
            if (p.Length == 0) continue;
            string cur = "";
            foreach (string sentence in Regex.Split(p, @"(?<=[\.!\?…:;])\s+"))
            {
                foreach (string piece in SplitLong(sentence.Trim()))
                {
                    if (cur.Length > 0 && cur.Length + 1 + piece.Length > PageChars) { pages.Add(cur); cur = ""; }
                    cur = cur.Length == 0 ? piece : cur + " " + piece;
                }
            }
            if (cur.Length > 0) pages.Add(cur);
        }
        return pages;
    }

    /// <summary>Una frase demasiado larga se corta por palabras.</summary>
    private static IEnumerable<string> SplitLong(string s)
    {
        if (s.Length <= PageChars) { yield return s; yield break; }
        string cur = "";
        foreach (string w in s.Split(' '))
        {
            if (cur.Length > 0 && cur.Length + 1 + w.Length > PageChars) { yield return cur; cur = ""; }
            cur = cur.Length == 0 ? w : cur + " " + w;
        }
        if (cur.Length > 0) yield return cur;
    }

    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
        _onClosed = null;
    }

    /// <summary>Cada fotograma: escritura letra a letra, paso de página y la tecla Z.</summary>
    public void Tick()
    {
        if (!IsOpen) return;
        bool top = SyringeHighlight.Current != SyringeHighlight.Side.None;
        if (top != _dockTop) Dock(top);
        bool allowed = ClickAllowed == null || ClickAllowed();
        if (allowed != _clickWasAllowed) { _clickWasAllowed = allowed; UpdateHint(); }   // (sin clic: sin «Mantén el clic…» ni aro)
        bool down = allowed && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool held = allowed && Mouse.current != null && Mouse.current.leftButton.isPressed;
        if (!held) _waitRelease = false;

        if (_shown < _full.Length)
        {
            _shown += Time.unscaledDeltaTime * CharsPerSecond;
            // Clic: la página entera. Y si se MANTIENE, ya cuenta para saltar (0,5 s desde que se pulsa, sin soltar).
            if (down) { _shown = _full.Length; _pressing = true; _hold = 0f; _pressWhileTyping = true; }
            if (_pressing && held) _hold += Time.unscaledDeltaTime;
            _body.maxVisibleCharacters = Mathf.Min(_full.Length, Mathf.FloorToInt(_shown));
            if (_shown >= _full.Length) UpdateHint();
            _ring.fillAmount = 0f;
            return;
        }
        _body.maxVisibleCharacters = _full.Length;

        // Z con la página entera: pulsación corta → siguiente página; mantenida → se salta todo.
        if (down && !_waitRelease) { _pressing = true; _hold = 0f; }
        if (_pressing && held) _hold += Time.unscaledDeltaTime;
        if (_pressing && !held)
        {
            _pressing = false;
            bool completedOnly = _pressWhileTyping;
            _pressWhileTyping = false;
            if (_hold < TapSeconds && !completedOnly) { _hold = 0f; _ring.fillAmount = 0f; NextPage(); return; }
            _hold = 0f;
        }
        _ring.fillAmount = _pressing && _dismissable ? Mathf.Clamp01(_hold / HoldSeconds) : 0f;
        // (Se cierra cuando el aro se ha visto LLENO un momento; si se suelta antes, se vacía y no pasa nada.)
        if (_pressing && _dismissable && _hold >= HoldSeconds + FullRingSeconds) { Close(); return; }

        if (!_autoClose) return;
        _readLeft -= Time.unscaledDeltaTime;   // (pasa sola cuando ha dado tiempo a leerla)
        if (_readLeft <= 0f && !_pressing) NextPage();
    }

    private bool _clickWasAllowed = true;

    private void UpdateHint()
    {
        // Ahora no vale el clic (se está jugando o en el 3-2-1): no se enseña cómo pasarlo; se cierra solo.
        if (!(ClickAllowed == null || ClickAllowed()))
        {
            _clickWasAllowed = false;
            _hint.text = "";
            _ring.transform.parent.gameObject.SetActive(false);
            return;
        }
        _clickWasAllowed = true;
        bool typing = _shown < _full.Length;
        bool more = _page + 1 < _pages.Count;
        _hint.text = typing ? Loc.T("[Clic] ver todo", "[Click] show all")
                   : more ? Loc.T("[Clic] siguiente · mantén el clic para saltar", "[Click] next · hold click to skip")
                   : !_dismissable ? ""
                   : _autoClose ? Loc.T("Mantén el clic para saltar", "Hold click to skip")
                   : Loc.T("Mantén el clic para continuar", "Hold click to continue");
        _ring.transform.parent.gameObject.SetActive(typing || _dismissable);
    }
}
