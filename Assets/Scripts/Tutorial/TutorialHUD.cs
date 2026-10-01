using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using static SimpleUI;

/// <summary>
/// AYUDA EN PANTALLA (se crea sola en las escenas con jugador):
///   • (Los avisos del tutorial son carteles en el nivel: ver TutorialHint.)
///   • F1: panel con TODOS los controles (con el panel abierto, F2 activa/desactiva los consejos).
///   • Esquina: «F1 · Controles» como recordatorio.
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
        (Loc.T("ESPACIO", "SPACE"),           Loc.T("Saltar (otra vez en el aire: doble salto)", "Jump (again in the air: double jump)")),
        (Loc.T("SALTAR A UNA PARED", "JUMP AT A WALL"), Loc.T("Correr por la pared (wallrun); ESPACIO en la pared: walljump", "Run along the wall (wallrun); SPACE on the wall: walljump")),
        ("SHIFT",                             Loc.T("Deslizarse por debajo de obstáculos", "Slide under obstacles")),
        ("E (+ A / D)",                       Loc.T("Dash: impulso rápido", "Dash: quick burst")),
        (Loc.T("S (en el aire)", "S (in the air)"), Loc.T("Slam: caer en picado", "Slam: drop straight down")),
        ("Q",                                 Loc.T("Bloquear: rompe los bloques que tienes delante", "Block: breaks the blocks in front of you")),
        ("F",                                 Loc.T("Engancharse a la tirolina", "Grab the zipline")),
        ("W A S D",                           Loc.T("Moverse (en la zona de inicio) y apartarse de lado", "Move (in the start area) and sidestep")),
        ("ESC",                               Loc.T("Pausa", "Pause")),
    };

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
        new GameObject("TutorialHUD").AddComponent<TutorialHUD>();
    }

    private GameObject _canvas;

    private void Awake()
    {
        _instance = this;
        Build();
        Loc.OnChanged += Rebuild;
    }

    /// <summary>Al cambiar de idioma se vuelve a construir con los textos nuevos.</summary>
    private void Rebuild()
    {
        bool open = _panel != null && _panel.activeSelf;
        if (_canvas != null) Destroy(_canvas);
        Build();
        _panel.SetActive(open);
    }

    private void Build()
    {
        Transform c = UIKit.CreateCanvas("TutorialCanvas", 90, transform).transform;
        _canvas = c.gameObject;

        // Recordatorio
        var tip = Lbl("Tip", c, Loc.T("F1 · Controles", "F1 · Controls"), FontSmall, TextAlignmentOptions.TopRight);
        tip.color = DimText;
        PlaceTopRight(tip.rectTransform, 20f, 20f, 300f, 30f);

        // Panel de controles
        var panel = Box("Controls", c, PanelCol);
        Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 760f));
        _panel = panel.gameObject;
        var title = Lbl("Title", panel.transform, Loc.T("CONTROLES", "CONTROLS"), FontBig, TextAlignmentOptions.Left);
        PlaceTopLeft(title.rectTransform, 30f, 25f, 600f, 50f);
        float y = 95f;
        foreach (var (key, what) in Controls)
        {
            var k = Lbl("K", panel.transform, key, FontMid, TextAlignmentOptions.Left);
            PlaceTopLeft(k.rectTransform, 30f, y, 300f, 52f);
            var w = Lbl("W", panel.transform, what, FontMid, TextAlignmentOptions.Left);
            w.color = DimText;
            PlaceTopLeft(w.rectTransform, 340f, y, 730f, 52f);
            y += 54f;
        }
        _toggleLabel = Lbl("HintsToggle", panel.transform, "", FontMid, TextAlignmentOptions.BottomLeft);
        PlaceBottomLeft(_toggleLabel.rectTransform, 30f, 30f, 600f, 40f);
        var close = Lbl("Close", panel.transform, Loc.T("F1 para cerrar", "F1 to close"), FontSmall, TextAlignmentOptions.BottomRight);
        close.color = DimText;
        Place(close.rectTransform, new Vector2(1f, 0f), new Vector2(-30f, 35f), new Vector2(300f, 30f));
        _panel.SetActive(false);
        RefreshToggle();
    }

    private void RefreshToggle() =>
        _toggleLabel.text = StudySession.Active ? Loc.T("Consejos: los decide la prueba", "Hints: set by the test")
                          : PlayerPrefs.GetInt(PrefKey, 1) == 1 ? Loc.T("F2 · Consejos: ACTIVADOS", "F2 · Hints: ON") : Loc.T("F2 · Consejos: desactivados", "F2 · Hints: off");

    private void OnDestroy() { Loc.OnChanged -= Rebuild; if (_instance == this) _instance = null; }

    private void Update()
    {
        // (Si se reconstruye estando en pausa, que no aparezca encima del menú.)
        if (_canvas != null) { var cv = _canvas.GetComponent<Canvas>(); if (cv.enabled == HudHider.IsHidden) cv.enabled = !HudHider.IsHidden; }
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            _panel.SetActive(!_panel.activeSelf);   // (no toca el cursor: el juego sigue)
        if (_panel.activeSelf && !StudySession.Active && Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            PlayerPrefs.SetInt(PrefKey, PlayerPrefs.GetInt(PrefKey, 1) == 1 ? 0 : 1);
            RefreshToggle();
        }

    }
}
