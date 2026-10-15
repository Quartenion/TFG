using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

/// <summary>
/// PANEL DE AJUSTES compartido (prefab Assets/Prefabs/SettingsMenu.prefab): el MISMO en el menú principal y en la pausa.
///   Título · Volumen · Sensibilidad · Idioma · Controles · Volver.
/// Se conecta solo (volumen/sensibilidad → GameSettings; idioma → LanguageButton; controles → Keybinds). Quien lo usa pone:
///   • onBack  : qué hacer al pulsar VOLVER (o Esc).
///   • Preview : BPM del metrónomo de prueba del volumen (y, si hay música sonando, a qué AudioSource sincronizarlo).
///
/// CONTROLES: abre una página (dentro del mismo panel) con cada acción y su tecla. Clic en una tecla → «PULSA UNA
/// TECLA» → la siguiente tecla que pulses queda asignada (Esc cancela). «Restablecer» vuelve a las de siempre.
/// Los botones de esa página son copias del botón CONTROLES (mismo estilo y letra).
/// </summary>
[AddComponentMenu("UI/Settings Menu")]
public class SettingsMenu : MonoBehaviour
{
    public Slider volume;
    public Slider sensitivity;
    public Button back;
    [Tooltip("Botón CONTROLES (abre la página de teclas).")]
    public Button controls;
    [Tooltip("Botón CÁMARA CON RATÓN: SÍ / NO.")]
    public Button cameraLook;

    /// <summary>Al pulsar VOLVER.</summary>
    public Action onBack;

    /// <summary>¿Está esperando a que se pulse una tecla? (Los menús no deben reaccionar a Esc mientras.)</summary>
    public static bool CapturingKey { get; private set; }
    /// <summary>Frame en el que terminó la captura (para que el Esc que la cancela no cierre también el menú).</summary>
    public static int CaptureEndedFrame { get; private set; } = -1;

    private VolumePreview _preview;
    private bool _bound;

    /// <summary>Metrónomo que suena al mover el volumen (ajusta su BPM / sincronización desde fuera).</summary>
    public VolumePreview Preview => _preview ??= new VolumePreview(gameObject);

    private void Awake() => Bind();

    private void Bind()
    {
        if (_bound) return;
        _bound = true;
        if (volume == null)      volume      = transform.Find("Volume")?.GetComponent<Slider>();
        if (sensitivity == null) sensitivity = transform.Find("Sensitivity")?.GetComponent<Slider>();
        if (back == null)        back        = transform.Find("BackButton")?.GetComponent<Button>();
        if (controls == null)    controls    = transform.Find("ControlsButton")?.GetComponent<Button>();
        if (cameraLook == null)  cameraLook  = transform.Find("CameraButton")?.GetComponent<Button>();
        if (cameraLook != null) cameraLook.onClick.AddListener(() => { GameSettings.CameraLook = !GameSettings.CameraLook; RefreshTexts(); });
        GameSettings.BindSliders(volume, sensitivity, Preview);
        if (back != null) back.onClick.AddListener(() => { if (!CloseControls()) onBack?.Invoke(); });
        if (controls != null) controls.onClick.AddListener(OpenControls);
        _tutorial = ExtraButton("TutorialButton", new Vector2(-470f, -340f),
            () => TutorialDirector.Pending = !TutorialDirector.Pending);
        _hints = ExtraButton("PistasButton", new Vector2(470f, -340f),
            () => ActionHints.On = !ActionHints.On);
    }

    // ── TUTORIAL: SÍ / NO (repetir el tutorial del nivel 1) · PISTAS: SÍ / NO (notas de todas las acciones) ──
    private Button _tutorial, _hints;

    /// <summary>Botón extra (copia del de la cámara), a un lado de VOLVER.</summary>
    private Button ExtraButton(string name, Vector2 pos, Action toggle)
    {
        if (cameraLook == null) return null;
        Transform existing = transform.Find(name);
        GameObject go = existing != null ? existing.gameObject : Instantiate(cameraLook.gameObject, transform, false);
        go.name = name;
        if (existing == null)
        {
            foreach (var lt in go.GetComponentsInChildren<LocalizedText>(true)) Destroy(lt);   // (el texto lo pone RefreshTexts)
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(380f, 80f);
            rt.localScale = Vector3.one;
        }
        Button b = go.GetComponent<Button>();
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() => { toggle(); RefreshTexts(); });
        TMP_Text t = go.GetComponentInChildren<TMP_Text>(true);
        if (t != null) { t.enableAutoSizing = true; t.fontSizeMin = 14f; t.fontSizeMax = Mathf.Max(20f, t.fontSize); }
        return b;
    }

    private void SetExtraText(Button b, string text)
    {
        if (b == null) return;
        TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = text;
        var img = b.targetGraphic as Image;
        if (img != null && _camBtnColorSaved) img.color = _camBtnColor;
    }

    private void OnEnable()
    {
        Bind();
        // Al abrir: valores guardados (por si se cambiaron desde el otro menú).
        if (volume != null)      volume.SetValueWithoutNotify(GameSettings.Volume);
        if (sensitivity != null) sensitivity.SetValueWithoutNotify(GameSettings.Sensitivity);
        Loc.OnChanged += RefreshTexts;
        RefreshTexts();
    }

    private void Update()
    {
        _preview?.Update();
        if (_listening) Listen();
    }

    private void OnDisable()
    {
        Loc.OnChanged -= RefreshTexts;
        CloseControls();
        PlayerPrefs.Save();
    }

    // =========================================================================
    // PÁGINA DE CONTROLES
    // =========================================================================

    private GameObject _page;
    private readonly List<(Keybinds.Bind bind, TMP_Text label, TMP_Text key)> _rows = new List<(Keybinds.Bind, TMP_Text, TMP_Text)>();
    private TMP_Text _resetLabel;
    private TMP_Text _pageTitle;
    private Color _keyColor = Color.red;   // color del texto de los botones (el mismo que CONTROLES)
    private bool _listening;
    private Keybinds.Bind _listenBind;

    public bool ControlsOpen => _page != null && _page.activeSelf;

    /// <summary>Esc / VOLVER: si está abierta la página de controles, vuelve a los ajustes. Devuelve true si lo ha hecho.</summary>
    public bool CloseControls()
    {
        StopListening();
        if (!ControlsOpen) return false;
        _page.SetActive(false);
        SetMainItems(true);
        RefreshTexts();
        return true;
    }

    private void OpenControls()
    {
        if (controls == null) return;
        if (_page == null) BuildPage();
        SetMainItems(false);
        _page.SetActive(true);
        RefreshTexts();
    }

    private TMP_Text _camWarning;
    private static readonly Color CamOffColor = new Color(1f, 0.58f, 0.12f);
    private Color _camBtnColor = Color.white;
    private bool _camBtnColorSaved;

    /// <summary>Aviso de la cámara desactivada (copia del texto de CONTROLES: misma letra), arriba a la derecha.</summary>
    private TMP_Text CameraWarning()
    {
        if (_camWarning != null) return _camWarning;
        _camWarning = CloneText((RectTransform)transform, "AvisoCamara", Vector2.zero, new Vector2(560f, 110f), TextAlignmentOptions.TopRight);
        var wrt = _camWarning.rectTransform;
        wrt.anchorMin = wrt.anchorMax = new Vector2(1f, 1f);
        wrt.pivot = new Vector2(1f, 1f);
        wrt.anchoredPosition = new Vector2(-40f, -40f);
        _camWarning.fontSizeMin = 18f;
        _camWarning.fontSizeMax = 40f;
        _camWarning.color = CamOffColor;
        _camWarning.text = "";
        return _camWarning;
    }

    private void SetMainItems(bool on)
    {
        foreach (Transform c in transform)
        {
            if (c.gameObject == _page) continue;
            if (back != null && c == back.transform) continue;
            // El título grande «AJUSTES» se esconde en la página de Controles (tiene su propio título, más pequeño).
            if (c.name.StartsWith("Titulo") || c.name.StartsWith("Title")) { c.gameObject.SetActive(on); continue; }
            if (c.GetComponent<Slider>() != null || c.GetComponent<Button>() != null) c.gameObject.SetActive(on);
        }
    }

    private void BuildPage()
    {
        _page = new GameObject("ControlsPage", typeof(RectTransform));
        var prt = (RectTransform)_page.transform;
        prt.SetParent(transform, false);
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
        _page.layer = gameObject.layer;

        Keybinds.Bind[] all = Keybinds.All;
        // Título de la página: copia del título «AJUSTES» (misma letra), más pequeño, arriba.
        Transform bigTitle = null;
        foreach (Transform c in transform) if (c.name.StartsWith("Titulo") || c.name.StartsWith("Title")) { bigTitle = c; break; }
        if (bigTitle != null)
        {
            GameObject tg = Instantiate(bigTitle.gameObject, prt, false);
            tg.name = "TituloControles";
            foreach (var lt in tg.GetComponentsInChildren<LocalizedText>(true)) Destroy(lt);
            var trt = (RectTransform)tg.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -40f);
            trt.sizeDelta = new Vector2(1600f, 190f);
            trt.localScale = Vector3.one;
            _pageTitle = tg.GetComponentInChildren<TMP_Text>(true);
            if (_pageTitle != null) { _pageTitle.enableAutoSizing = true; _pageTitle.fontSizeMin = 30f; _pageTitle.fontSizeMax = 170f; }
        }

        // Lista con SCROLL (rueda del ratón o la barra de la derecha): una acción por fila, con aire.
        const float viewW = 1100f, viewH = 460f, rowH = 96f;
        var view = new GameObject("Lista", typeof(RectTransform), typeof(RectMask2D), typeof(Image)).GetComponent<RectTransform>();
        view.SetParent(prt, false);
        view.anchorMin = view.anchorMax = view.pivot = new Vector2(0.5f, 0.5f);
        view.anchoredPosition = new Vector2(0f, 30f);
        view.sizeDelta = new Vector2(viewW, viewH);
        view.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);   // fondo suave (y recibe la rueda del ratón)

        var content = new GameObject("Contenido", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(view, false);
        float contentH = all.Length * rowH + 20f;
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(viewW, contentH);

        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = view; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45f;
        scroll.verticalScrollbar = BuildScrollbar(prt, new Vector2(viewW * 0.5f + 22f, 30f), new Vector2(18f, viewH));
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalNormalizedPosition = 1f;   // empieza arriba del todo

        for (int i = 0; i < all.Length; i++)
        {
            Keybinds.Bind b = all[i];
            float y = contentH * 0.5f - 10f - rowH * (i + 0.5f);   // (posiciones respecto al centro del contenido)

            // Nombre de la acción (copia del texto del botón CONTROLES: misma letra).
            TMP_Text lbl = CloneText(content, "Accion_" + b, new Vector2(-170f, y), new Vector2(620f, 70f), TextAlignmentOptions.MidlineLeft);

            // Botón con la tecla.
            Button kb = CloneButton(content, "Tecla_" + b, new Vector2(340f, y), new Vector2(300f, 70f), out TMP_Text key);
            Keybinds.Bind captured = b;
            kb.onClick.AddListener(() => StartListening(captured));
            if (key != null) _keyColor = key.color;
            _rows.Add((b, lbl, key));
        }

        Button reset = CloneButton(prt, "Restablecer", new Vector2(0f, -245f), new Vector2(560f, 60f), out _resetLabel);
        reset.onClick.AddListener(() => { StopListening(); Keybinds.ResetAll(); RefreshTexts(); });
    }

    /// <summary>Barra de scroll fina (fondo oscuro + asa con el color de los botones).</summary>
    private Scrollbar BuildScrollbar(RectTransform parent, Vector2 pos, Vector2 size)
    {
        var bar = new GameObject("BarraScroll", typeof(RectTransform), typeof(Image), typeof(Scrollbar)).GetComponent<RectTransform>();
        bar.SetParent(parent, false);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
        bar.anchoredPosition = pos; bar.sizeDelta = size;
        bar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

        var area = new GameObject("Area", typeof(RectTransform)).GetComponent<RectTransform>();
        area.SetParent(bar, false);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = area.offsetMax = Vector2.zero;

        var handle = new GameObject("Asa", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        handle.SetParent(area, false);
        handle.offsetMin = handle.offsetMax = Vector2.zero;
        var himg = handle.GetComponent<Image>();
        var src = controls != null ? controls.targetGraphic as Image : null;
        himg.color = src != null ? src.color : Color.white;

        var sb = bar.GetComponent<Scrollbar>();
        sb.handleRect = handle;
        sb.targetGraphic = himg;
        sb.direction = Scrollbar.Direction.BottomToTop;
        return sb;
    }

    private Button CloneButton(RectTransform parent, string name, Vector2 pos, Vector2 size, out TMP_Text label)
    {
        GameObject go = Instantiate(controls.gameObject, parent, false);
        go.name = name;
        go.SetActive(true);
        var rt = (RectTransform)go.transform;
        rt.localScale = Vector3.one;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null) { label.enableAutoSizing = true; label.fontSizeMin = 14f; label.fontSizeMax = Mathf.Max(20f, label.fontSize * 0.8f); }
        return go.GetComponent<Button>();
    }

    private TMP_Text CloneText(RectTransform parent, string name, Vector2 pos, Vector2 size, TextAlignmentOptions align)
    {
        TMP_Text src = controls.GetComponentInChildren<TMP_Text>(true);
        GameObject go = Instantiate(src.gameObject, parent, false);
        go.name = name;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        TMP_Text t = go.GetComponent<TMP_Text>();
        t.alignment = align;
        t.enableAutoSizing = true; t.fontSizeMin = 14f; t.fontSizeMax = Mathf.Max(20f, src.fontSize * 0.8f);
        t.raycastTarget = false;
        return t;
    }

    private void RefreshTexts()
    {
        if (controls != null)
        {
            TMP_Text t = controls.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = Loc.T("CONTROLES", "CONTROLS");
        }
        if (cameraLook != null)
        {
            TMP_Text t = cameraLook.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = GameSettings.CameraLook ? Loc.T("CÁMARA CON RATÓN: SÍ", "MOUSE CAMERA: ON") : Loc.T("CÁMARA CON RATÓN: NO", "MOUSE CAMERA: OFF");
            TMP_Text w = CameraWarning();
            w.text = Loc.T("Se recomienda activarla.\nEn el Nivel 3 es obligatoria.", "Recommended: ON.\nRequired in Level 3.");
            w.gameObject.SetActive(!GameSettings.CameraLook && !ControlsOpen);
            // Botón en naranja mientras está desactivada.
            var img = cameraLook.targetGraphic as Image;
            if (img != null)
            {
                if (!_camBtnColorSaved) { _camBtnColor = img.color; _camBtnColorSaved = true; }
                img.color = GameSettings.CameraLook ? _camBtnColor : CamOffColor;
            }
        }
        SetExtraText(_tutorial, TutorialDirector.Pending ? Loc.T("TUTORIAL: SÍ", "TUTORIAL: ON") : Loc.T("TUTORIAL: NO", "TUTORIAL: OFF"));
        SetExtraText(_hints, ActionHints.On ? Loc.T("PISTAS: SÍ", "HINTS: ON") : Loc.T("PISTAS: NO", "HINTS: OFF"));
        if (_pageTitle != null) _pageTitle.text = Loc.T("CONTROLES", "CONTROLS");
        if (_resetLabel != null) _resetLabel.text = Loc.T("RESTABLECER TECLAS", "RESET KEYS");
        foreach (var (bind, label, key) in _rows)
        {
            if (label != null) label.text = Keybinds.Label(bind);
            if (key == null) continue;
            bool waiting = _listening && _listenBind == bind;
            key.text = waiting ? Loc.T("PULSA UNA TECLA", "PRESS A KEY") : Keybinds.Name(bind);
            // (Los botones son claros: texto con el color de los demás botones; esperando = negro; repetida = naranja.)
            key.color = waiting ? new Color(0.08f, 0.08f, 0.08f) : Keybinds.Conflicts(bind) ? new Color(1f, 0.5f, 0f) : _keyColor;
        }
    }

    private void StartListening(Keybinds.Bind b)
    {
        if (CaptureEndedFrame == Time.frameCount) return;   // (el Espacio/Enter que asignó la tecla también «pulsa» el botón)
        // Sin botón seleccionado: si no, Espacio/Enter volverían a pulsarlo.
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        _listening = true;
        _listenBind = b;
        CapturingKey = true;
        _listenStartFrame = Time.frameCount;
        RefreshTexts();
    }

    private int _listenStartFrame;

    private void StopListening()
    {
        if (!_listening) return;
        _listening = false;
        CapturingKey = false;
        CaptureEndedFrame = Time.frameCount;
        RefreshTexts();
    }

    private void Listen()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || Time.frameCount == _listenStartFrame) return;
        if (kb.escapeKey.wasPressedThisFrame) { StopListening(); return; }   // cancelar
        foreach (KeyControl k in kb.allKeys)
        {
            if (k == null || !k.wasPressedThisFrame) continue;
            if (Keybinds.Reserved(k.keyCode)) continue;   // (P, TAB, F2: reservadas)
            Keybinds.Set(_listenBind, k.keyCode);
            StopListening();
            return;
        }
    }
}
