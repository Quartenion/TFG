using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>Una casilla de la rejilla de un GridPanel (fondo + texto de estado). Cada panel la amplía con sus datos.</summary>
public class GridPanelCell
{
    public Image bg;
    public TextMeshProUGUI state;
}

/// <summary>
/// Base común de los paneles de pantalla completa con PESTAÑAS + REJILLA + FICHA (la tienda y los contratos).
/// Antes ShopUI y ContractUI tenían cada uno una copia de: abrir/cerrar, ratón, Esc, pestañas, rejilla de casillas,
/// pie con «Salir» y mensajes temporales. Ahora eso vive aquí y cada panel solo define su cabecera, sus pestañas,
/// qué casillas muestra, su ficha y sus acciones.
///
///   TÍTULO                                   (cabecera propia)
///   [Pestaña][Pestaña]                      ┌──── ficha (propia) ────┐
///   ┌───── rejilla (casillas) ─────┐        │                        │
///   └──────────────────────────────┘        └────────────────────────┘
///   aviso                 [Salir (Esc)]
///
/// HORNEADO: si el panel existe ya dentro del Canvas (Tools/Interfaz/Hornear…), Open() activa ESE objeto (editable en
/// el editor) en vez de crear uno nuevo, y Close() lo desactiva.
///
/// TSelf = el propio panel (para que cada tipo tenga su instancia, IsOpen y ClosedRecently independientes).
/// TCell = el tipo de casilla del panel.
/// </summary>
public abstract class GridPanel<TSelf, TCell> : MonoBehaviour
    where TSelf : GridPanel<TSelf, TCell>
    where TCell : GridPanelCell
{
    // ── LAYOUT (común a los dos paneles) ──────────────────────────────────────
    protected const float PanelW = 1800f, PanelH = 900f;
    protected const float Pad = 30f;              // margen interior
    protected const float HeaderH = 80f;          // cabecera
    protected const float TabH = 50f;             // pestañas (encima de la rejilla)
    protected const float FooterH = 90f;          // pie (salir + aviso)
    protected const float DetailW = 460f;         // ancho de la ficha
    protected const float Gap = 10f;
    protected const int   Columns = 6;            // columnas de la rejilla (cada pestaña ocupa Columns / nº pestañas)
    protected float _cell;                        // lado de cada casilla (= ancho de una columna)

    // ── Abrir / cerrar ────────────────────────────────────────────────────────
    public static bool IsOpen => _instance != null;
    private static TSelf _instance;
    private static int   _closedFrame = -10;
    private static float _closedTime  = -10f;

    /// <summary>¿Se acaba de cerrar? (para que el Esc que lo cierra no abra también la pausa).</summary>
    public static bool ClosedRecently => Time.frameCount - _closedFrame <= 5 || Time.unscaledTime - _closedTime < 0.6f;

    private Action _onClosed;
    private CursorLockMode _prevLock;
    private bool _prevVisible;

    /// <summary>Abre el panel (si ya está abierto, devuelve el existente). 'onClosed' se llama al cerrarlo.</summary>
    public static TSelf Open(Action onClosed = null)
    {
        if (_instance != null) return _instance;

        // ¿Está horneado en el Canvas de la escena? Entonces se usa ese (inactivo hasta ahora).
        foreach (TSelf p in FindObjectsByType<TSelf>(FindObjectsInactive.Include))
            if (p._baked)
            {
                _instance = p;
                p._onClosed = onClosed;
                p.gameObject.SetActive(true);   // → Awake (la 1.ª vez) + OnEnable
                return p;
            }

        _instance = new GameObject(typeof(TSelf).Name).AddComponent<TSelf>();
        _instance._onClosed = onClosed;
        return _instance;
    }

    public void Close()
    {
        if (!gameObject.activeSelf) return;
        OnClosing();
        CursorRestorer.Restore(_prevLock, _prevVisible);
        if (_instance == this) _instance = null;
        _closedFrame = Time.frameCount;
        _closedTime  = Time.unscaledTime;
        if (_baked) gameObject.SetActive(false);   // (horneado: se queda en el Canvas para la próxima vez)
        else Destroy(gameObject);
        Action cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }

    [SerializeField, HideInInspector] private bool _baked;

    /// <summary>Herramienta de horneado (editor): construye la interfaz dentro de este objeto, una sola vez.</summary>
    public void BakeBuild() { _baked = true; Build(); }

    // ── Estado común ──────────────────────────────────────────────────────────
    protected TCell _selected;
    protected readonly List<TCell> _entries = new List<TCell>();
    private readonly List<Image> _tabs = new List<Image>();
    protected RectTransform _grid;
    private TextMeshProUGUI _toast;
    private float _toastT;

    // ── Lo que define cada panel ──────────────────────────────────────────────
    protected abstract string CanvasName { get; }
    protected abstract string Title { get; }
    protected abstract float  ToastWidth { get; }
    /// <summary>Nombres de las pestañas (se reparten las columnas de la rejilla).</summary>
    protected abstract List<string> TabNames();
    /// <summary>Cabecera propia (monedero, botones…), a la derecha del título.</summary>
    protected abstract void BuildHeader(Transform panel);
    /// <summary>Contenido de la ficha de la derecha.</summary>
    protected abstract void BuildDetail(Transform detail);
    /// <summary>Añade con AddEntry las casillas de la pestaña 'tab'.</summary>
    protected abstract void FillTab(int tab);
    /// <summary>Actualiza textos, colores y botones (se llama también cuando cambia PlayerProgress).</summary>
    protected abstract void Refresh();
    /// <summary>Al terminar de abrirse / justo antes de cerrarse (p. ej. telemetría).</summary>
    protected virtual void OnOpened() { }
    protected virtual void OnClosing() { }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    private void Awake()
    {
        // Horneado: el mismo código «encuentra» lo que ya existe (y respeta lo editado); si no, lo crea.
        if (_baked) { using (UIBind.Scope()) Build(); }
        else Build();
    }

    private void OnEnable()
    {
        _prevLock    = Cursor.lockState;
        _prevVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        UIKit.EnsureEventSystem();
        PlayerProgress.OnChanged += Refresh;
        _toastT = 0f; if (_toast != null) _toast.text = "";
        ShowTab(0);
        OnOpened();
    }

    private void OnDisable()
    {
        PlayerProgress.OnChanged -= Refresh;
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (_toastT > 0f) { _toastT -= Time.unscaledDeltaTime; if (_toastT <= 0f) _toast.text = ""; }
    }

    // ── Construcción ──────────────────────────────────────────────────────────

    private void Build()
    {
        Canvas canvas = UIKit.CreateCanvas(CanvasName, 1000, transform);
        Stretch(Box("Back", canvas.transform, Back).rectTransform, 0f, 0f, 0f, 0f);

        var panel = Box("Panel", canvas.transform, PanelCol);
        Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));
        Transform p = panel.transform;

        // Cabecera
        var title = Lbl("Title", p, Title, FontBig, TextAlignmentOptions.Left);
        PlaceTopLeft(title.rectTransform, Pad, Pad, 400f, 50f);
        BuildHeader(p);

        // Columna izquierda: pestañas + rejilla (casillas cuadradas; cada pestaña ocupa justo sus columnas).
        float leftW = PanelW - Pad * 3f - DetailW;
        float top = Pad + HeaderH;
        _cell = (leftW - Gap * (Columns - 1)) / Columns;
        List<string> names = TabNames();
        int perTab = Columns / Mathf.Max(1, names.Count);
        float tabW = _cell * perTab + Gap * (perTab - 1);
        for (int i = 0; i < names.Count; i++)
        {
            int tab = i;
            var b = Btn("Tab" + i, p, names[i], out _);
            PlaceTopLeft((RectTransform)b.transform, Pad + i * (tabW + Gap), top, tabW, TabH);
            b.onClick.AddListener(() => ShowTab(tab));
            _tabs.Add(b.image);
        }
        var gridBox = UIKit.Node("GridBox", p);
        float gridTop = top + TabH + Gap;
        PlaceTopLeft(gridBox, Pad, gridTop, leftW, PanelH - gridTop - FooterH);
        _grid = MakeGrid(gridBox, _cell, Gap, Columns);

        // Columna derecha: ficha
        var detail = Box("Detail", p, BoxCol);
        PlaceTopRight(detail.rectTransform, Pad, top, DetailW, PanelH - top - FooterH);
        BuildDetail(detail.transform);

        // Pie
        var exit = Btn("Exit", p, Loc.T("Salir (Esc)", "Exit (Esc)"), out _);
        Place((RectTransform)exit.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(300f, 50f), new Vector2(0.5f, 0f));
        exit.onClick.AddListener(Close);
        _toast = Lbl("Toast", p, "", FontMid, TextAlignmentOptions.Left);
        PlaceBottomLeft(_toast.rectTransform, Pad, 20f, ToastWidth, 50f);
    }

    // ── Pestañas y rejilla ────────────────────────────────────────────────────

    protected void ShowTab(int tab)
    {
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].color = i == tab ? SelCol : BtnCol;
        foreach (Transform c in _grid) Destroy(c.gameObject);
        _entries.Clear();

        FillTab(tab);

        _selected = _entries.Count > 0 ? _entries[0] : null;
        Refresh();
    }

    /// <summary>Crea la casilla de 'e' en la rejilla con el título dado (al pulsarla queda seleccionada).</summary>
    protected void AddEntry(TCell e, string title)
    {
        e.bg = Box("Item", _grid, ItemCol);
        e.bg.gameObject.AddComponent<Button>().onClick.AddListener(() => { _selected = e; Refresh(); });

        var label = Lbl("Name", e.bg.transform, title, FontSmall + 2f, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 10f, 10f, 10f, 45f);
        e.state = Lbl("State", e.bg.transform, "", FontSmall, TextAlignmentOptions.Center);
        e.state.color = DimText;
        Stretch(e.state.rectTransform, 6f, 6f, _cell - 45f, 8f);
        _entries.Add(e);
    }

    /// <summary>Mensaje temporal abajo a la izquierda (2,5 s).</summary>
    protected void Toast(string msg) { _toast.text = msg; _toastT = 2.5f; }
}
