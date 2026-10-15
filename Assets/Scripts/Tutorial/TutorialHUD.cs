using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using static SimpleUI;

/// <summary>
/// AYUDA EN PANTALLA (se crea sola en las escenas con jugador):
///   • (Los avisos del tutorial son carteles en el nivel: ver TutorialHint.)
///   • TAB: panel lateral (sale por la IZQUIERDA) con TODOS los controles; TAB otra vez lo esconde.
///     Con el panel abierto, F2 activa/desactiva los consejos.
///   • Esquina superior izquierda: «TAB · Controles» como recordatorio.
/// </summary>
public class TutorialHUD : MonoBehaviour
{
    private const string PrefKey = "TutorialHints";

    /// <summary>¿Se muestran los avisos? En el modo prueba lo decide la sesión (solo en la práctica).</summary>
    public static bool HintsOn => StudySession.Active ? StudySession.TutorialStep : PlayerPrefs.GetInt(PrefKey, 1) == 1;

    /// <summary>Controles del juego (tecla, qué hace). Cámbialos aquí si cambian las teclas.</summary>
    public static (string key, string what)[] Controls => new[]
    {
        (Loc.T("CLIC IZQ. / DCHO.", "LEFT / RIGHT CLICK"),
         Loc.T("Latido: haz clic AL RITMO para recuperar energía (fallar o pulsar de más la gasta)",
               "Heartbeat: click ON THE BEAT to recover energy (missing or extra clicks drain it)")),
        (K(Keybinds.Bind.Jump),               Loc.T("Saltar (otra vez en el aire: doble salto)", "Jump (again in the air: double jump)")),
        (Loc.T("SALTAR A UNA PARED", "JUMP AT A WALL"), Loc.T($"Correr por la pared (wallrun); {K(Keybinds.Bind.Jump)} en la pared: walljump", $"Run along the wall (wallrun); {K(Keybinds.Bind.Jump)} on the wall: walljump")),
        (K(Keybinds.Bind.Slide),              Loc.T("Deslizarse por debajo de obstáculos", "Slide under obstacles")),
        ($"{K(Keybinds.Bind.Dash)} (+ {K(Keybinds.Bind.Left)} / {K(Keybinds.Bind.Right)})", Loc.T("Dash: impulso rápido", "Dash: quick burst")),
        (Loc.T($"{K(Keybinds.Bind.Slam)} (en el aire)", $"{K(Keybinds.Bind.Slam)} (in the air)"), Loc.T("Slam: caer en picado", "Slam: drop straight down")),
        (K(Keybinds.Bind.Block),              Loc.T("Bloquear: rompe los bloques que tienes delante", "Block: breaks the blocks in front of you")),
        (K(Keybinds.Bind.Zipline),            Loc.T("Engancharse a la tirolina", "Grab the zipline")),
        ($"{K(Keybinds.Bind.Forward)} {K(Keybinds.Bind.Left)} {K(Keybinds.Bind.Back)} {K(Keybinds.Bind.Right)}", Loc.T("Moverse (en la zona de inicio) y apartarse de lado", "Move (in the start area) and sidestep")),
        (K(Keybinds.Bind.Interact),           Loc.T("Interactuar: tienda, contratos, empezar", "Interact: shop, contracts, start")),
        (K(Keybinds.Bind.Metronome),          Loc.T("Metrónomo: activar / desactivar (si lo tienes)", "Metronome: on / off (if you have it)")),
        (Loc.T("CLIC EN EL KAYAK", "CLICK IN THE KAYAK"), Loc.T("Clic izq.: remar a la derecha · clic dcho.: a la izquierda", "Left click: paddle right · right click: paddle left")),
        ("ESC / P",                           Loc.T("Pausa (Ajustes → Controles: cambiar teclas)", "Pause (Settings → Controls: change keys)")),
        ("TAB",                               Loc.T("Mostrar / esconder este panel", "Show / hide this panel")),
    };

    private static string K(Keybinds.Bind b) => Keybinds.Name(b);

    private static TutorialHUD _instance;
    private GameObject _panel;
    private TextMeshProUGUI _toggleLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (_instance != null || FindAnyObjectByType<PlayerMovement>() == null) return;
        // ¿Horneado en el Canvas de la escena? (Tools/Interfaz/Hornear…) → se activa ese, editable en el editor.
        foreach (TutorialHUD h in FindObjectsByType<TutorialHUD>(FindObjectsInactive.Include))
            if (h._baked) { h.gameObject.SetActive(true); return; }
        new GameObject("TutorialHUD").AddComponent<TutorialHUD>();
    }

    private GameObject _canvas;
    [SerializeField, HideInInspector] private bool _baked;

    /// <summary>Herramienta de horneado (editor): construye la interfaz dentro de este objeto, una sola vez.</summary>
    public void BakeBuild() { _baked = true; Build(); }

    private void Awake()
    {
        _instance = this;
        if (_baked) { using (UIBind.Scope()) Build(); }
        else Build();
        Loc.OnChanged += Rebuild;
        Keybinds.Changed += Rebuild;
    }

    /// <summary>Al cambiar de idioma (o las teclas) se vuelven a poner los textos.</summary>
    private void Rebuild()
    {
        if (_baked) { using (UIBind.Scope()) Build(); return; }   // (horneado: mismos objetos, textos nuevos)
        if (_canvas != null) Destroy(_canvas);
        Build();   // (conserva _open/_slide: si estaba abierto, sigue abierto)
    }

    private const float PanelWidth = 640f;

    /// <summary>¿Cuánto está abierto el panel de TAB? (0 = escondido, 1 = fuera). Lo usa el panel de contratos.</summary>
    public static float OpenAmount => _instance != null && !HudHider.IsHidden ? _instance._slide : 0f;
    public static float Width => PanelWidth;
    private RectTransform _panelRt;
    private bool _open;   // (panel de controles fuera)
    // TAB va rotando: 1.ª pulsación = controles, 2.ª = contratos (si hay firmados en este nivel), 3.ª = cerrar.
    private int _mode;    // 0 = cerrado · 1 = controles · 2 = contratos
    private float _contractsSlide;
    private TextMeshProUGUI _closeLabel;
    private PlayerMovement _pm;

    /// <summary>¿Cuánto está fuera el panel de CONTRATOS (2.ª pulsación de TAB)? 0..1.</summary>
    public static float ContractsAmount => _instance != null && !HudHider.IsHidden ? _instance._contractsSlide : 0f;

    /// <summary>¿Hay contratos firmados que enseñar ahora (jugando el nivel, no en la zona de inicio)?</summary>
    private bool HasContracts()
    {
        if (_pm == null) _pm = FindAnyObjectByType<PlayerMovement>();
        if (_pm == null || _pm.IsFreeroam) return false;
        return ContractCatalog.ActiveHere().Count > 0;
    }
    private float _slide;   // 0 = escondido, 1 = fuera

    private void Build()
    {
        Transform c = UIKit.CreateCanvas("TutorialCanvas", 90, transform).transform;
        _canvas = c.gameObject;

        // Recordatorio
        // (Arriba a la IZQUIERDA; el panel, al salir, lo tapa.)
        var tip = Lbl("Tip", c, Loc.T("TAB · Controles", "TAB · Controls"), FontSmall, TextAlignmentOptions.TopLeft);
        if (!UIBind.IsReused(tip)) tip.color = DimText;
        PlaceTopLeft(tip.rectTransform, 20f, 20f, 300f, 30f);

        // Panel de controles: columna a la IZQUIERDA, de arriba a abajo; entra y sale deslizándose.
        var panel = Box("Controls", c, new Color(PanelCol.r, PanelCol.g, PanelCol.b, 0.94f));
        _panelRt = panel.rectTransform;
        if (!UIBind.IsReused(panel))
        {
            _panelRt.anchorMin = new Vector2(0f, 0f); _panelRt.anchorMax = new Vector2(0f, 1f);
            _panelRt.pivot = new Vector2(0f, 0.5f);
            _panelRt.sizeDelta = new Vector2(PanelWidth, 0f);
        }
        _panel = panel.gameObject;

        var title = Lbl("Title", panel.transform, Loc.T("CONTROLES", "CONTROLS"), FontBig, TextAlignmentOptions.Left);
        PlaceTopLeft(title.rectTransform, 30f, 25f, 580f, 50f);
        float y = 90f;
        foreach (var (key, what) in Controls)
        {
            var k = Lbl("K", panel.transform, key, FontSmall + 1f, TextAlignmentOptions.TopLeft);
            if (!UIBind.IsReused(k)) k.fontStyle = FontStyles.Bold;
            PlaceTopLeft(k.rectTransform, 30f, y, 200f, 60f);
            var w = Lbl("W", panel.transform, what, FontSmall, TextAlignmentOptions.TopLeft);
            if (!UIBind.IsReused(w)) w.color = DimText;
            PlaceTopLeft(w.rectTransform, 240f, y, 370f, 60f);
            y += Mathf.Max(48f, w.GetPreferredValues(what, 370f, 0f).y + 14f);
        }
        _toggleLabel = Lbl("HintsToggle", panel.transform, "", FontSmall, TextAlignmentOptions.BottomLeft);
        PlaceBottomLeft(_toggleLabel.rectTransform, 30f, 60f, 580f, 30f);
        var close = Lbl("Close", panel.transform, Loc.T("TAB para cerrar", "TAB to close"), FontSmall, TextAlignmentOptions.BottomLeft);
        if (!UIBind.IsReused(close)) close.color = DimText;
        _closeLabel = close;
        PlaceBottomLeft(close.rectTransform, 30f, 25f, 580f, 30f);
        if (!Application.isPlaying) { _panel.SetActive(false); return; }   // (al hornear: el panel empieza escondido)
        ApplySlide();
        RefreshToggle();
    }

    private void ApplySlide()
    {
        if (_panelRt == null) return;
        float e = 1f - Mathf.Pow(1f - _slide, 3f);   // sale rápido y frena al final
        _panelRt.anchoredPosition = new Vector2(Mathf.Lerp(-PanelWidth - 20f, 0f, e), 0f);
        _panel.SetActive(_slide > 0.001f);
    }

    private void RefreshToggle() =>
        _toggleLabel.text = StudySession.Active ? Loc.T("Consejos: los decide la prueba", "Hints: set by the test")
                          : PlayerPrefs.GetInt(PrefKey, 1) == 1 ? Loc.T("F2 · Consejos: ACTIVADOS", "F2 · Hints: ON") : Loc.T("F2 · Consejos: desactivados", "F2 · Hints: off");

    private void OnDestroy() { Loc.OnChanged -= Rebuild; Keybinds.Changed -= Rebuild; if (_instance == this) _instance = null; }

    private void Update()
    {
        // (Si se reconstruye estando en pausa, que no aparezca encima del menú.)
        if (_canvas != null) { var cv = _canvas.GetComponent<Canvas>(); if (cv.enabled == HudHider.IsHidden) cv.enabled = !HudHider.IsHidden; }
        bool contracts = HasContracts();
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame && !SettingsMenu.CapturingKey)
        {
            // (no toca el cursor: el juego sigue)
            _mode = _mode == 0 ? 1 : (_mode == 1 && contracts) ? 2 : 0;
        }
        if (_mode == 2 && !contracts) _mode = 0;
        _open = _mode == 1;
        if (_closeLabel != null)
            _closeLabel.text = contracts ? Loc.T("TAB: ver contratos", "TAB: show contracts") : Loc.T("TAB para cerrar", "TAB to close");

        float target = _open ? 1f : 0f;
        if (!Mathf.Approximately(_slide, target))
        {
            _slide = Mathf.MoveTowards(_slide, target, Time.unscaledDeltaTime / 0.22f);
            ApplySlide();
        }
        _contractsSlide = Mathf.MoveTowards(_contractsSlide, _mode == 2 ? 1f : 0f, Time.unscaledDeltaTime / 0.22f);
        if (_open && !StudySession.Active && Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            PlayerPrefs.SetInt(PrefKey, PlayerPrefs.GetInt(PrefKey, 1) == 1 ? 0 : 1);
            RefreshToggle();
        }

    }
}
