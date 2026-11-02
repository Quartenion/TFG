using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu de pausa. P / ESC abren y cierran el panel.
///
/// FIXES aplicados:
///   1. Musica: AudioListener.pause = true/false. Time.timeScale no afecta al audio.
///   2. Movimiento: se desactivan PlayerMovement y RhythmInput al pausar.
///   3. Muerte: se detecta con EnergyManager.Instance.IsDead en vez de
///      _gameOverManager.enabled (que solo era false tras un Restart, no al morir).
/// </summary>
public class PauseMenuManager : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("GameObject raiz del panel de pausa (se activa/desactiva)")]
    [SerializeField] private GameObject _pauseMenuPanel;
    [Tooltip("CanvasGroup del panel para gestionar alpha y raycasts")]
    [SerializeField] private CanvasGroup _pauseMenuGroup;

    [Header("UI que se oculta al pausar")]
    [Tooltip("GameObjects de HUD que se ocultan mientras esta el menu (Crosshair, EnergyBar, etc.)")]
    [SerializeField] private GameObject[] _hideWhilePaused;

    [Header("Scripts que se desactivan al pausar")]
    [Tooltip("Normalmente el PlayerMovement. Se desactiva para que el jugador no pueda moverse ni girar la camara.")]
    [SerializeField] private PlayerMovement _playerMovement;
    [Tooltip("RhythmInput: evita que los clicks del jugador registren hits mientras esta pausado.")]
    [SerializeField] private RhythmInput _rhythmInput;

    [Header("Escena del menu principal")]
    [Tooltip("Nombre exacto de la escena del menu principal (debe estar en Build Settings)")]
    [SerializeField] private string _mainMenuSceneName = "MainMenu";

    [Header("Beat Manager")]
    [Tooltip("Para pausar y reanudar la música correctamente.")]
    [SerializeField] private BeatManager _beatManager;

    [Header("Bloquear pausa cuando el jugador esta muerto")]
    [Tooltip("Si esta a true, P/ESC no funcionan cuando la energia llega a cero.")]
    [SerializeField] private bool _ignoreWhenDead = true;
    [Tooltip("Referencia al GameOverManager para detectar muerte por obstaculo (sin drain de energia).")]
    [SerializeField] private GameOverManager _gameOverManager;

    [Header("Editor workaround")]
    [Tooltip("En el editor, ESC libera el cursor sin disparar el evento de teclado. " +
             "Con esta opcion detectamos ese cambio y lo tratamos como pausa.")]
    [SerializeField] private bool _detectEscViaCursorChange = true;

    [Header("Debug")]
    [SerializeField] private bool _verbose = true;

    private bool _isPaused;

    /// <summary>¿Está abierto el menú de pausa? (los avisos OnGUI y los carteles del tutorial se esconden).</summary>
    public static bool IsPaused { get; private set; }
    private CursorLockMode _previousCursorLockState;
    private bool _previousCursorVisible;

    // Anti-doble-trigger: si ya procesamos una pausa este frame, ignoramos el resto
    private bool _handledThisFrame;

    // Para el workaround del cursor
    private CursorLockMode _lastFrameCursorLock;
    private bool _weJustChangedCursor;

    private void Start()
    {
        if (_beatManager == null)
            _beatManager = BeatManager.Instance;

        if (_pauseMenuPanel != null) _pauseMenuPanel.SetActive(false);

        if (_pauseMenuGroup != null)
        {
            _pauseMenuGroup.alpha = 0f;
            _pauseMenuGroup.interactable = false;
            _pauseMenuGroup.blocksRaycasts = false;
        }

        _lastFrameCursorLock = Cursor.lockState;
        SetupSettings();
    }

    private void Update()
    {
        _handledThisFrame = false;

        // --- Bloquear pausa si el jugador ya esta muerto, o si el nivel ya ha terminado (paseo final y pantalla de
        //     resultados: hasta que se carga la escena siguiente no se puede pausar) ---
        if ((_ignoreWhenDead && IsPlayerDead()) || (LevelEndManager.Finished && !_isPaused))
        {
            _lastFrameCursorLock = Cursor.lockState;
            _weJustChangedCursor = false;
            return;
        }

        // --- La tienda abierta (o recién cerrada con Esc) gestiona su propio Esc y el cursor ---
        if (ShopUI.IsOpen || ShopUI.ClosedRecently || ContractUI.IsOpen || ContractUI.ClosedRecently)
        {
            _lastFrameCursorLock = Cursor.lockState;
            _weJustChangedCursor = false;
            return;
        }

        // --- Deteccion de tecla ---
        if (Keyboard.current != null)
        {
            bool escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;
            bool pPressed   = Keyboard.current.pKey.wasPressedThisFrame;

            // (Ajustes → Controles esperando una tecla: esa tecla, Esc incluido, es para la captura.)
            if (SettingsMenu.CapturingKey || SettingsMenu.CaptureEndedFrame >= Time.frameCount - 1) { escPressed = pPressed = false; }

            if ((escPressed || pPressed) && !_handledThisFrame)
            {
                _handledThisFrame = true;
                if (_verbose) Debug.Log("[Pause] Tecla detectada: " + (escPressed ? "ESC" : "P"));
                TogglePause();
            }
        }

        // --- Workaround editor: ESC libera el cursor sin disparar el evento de teclado ---
        if (_detectEscViaCursorChange && !_isPaused && !_handledThisFrame)
        {
            if (_lastFrameCursorLock == CursorLockMode.Locked
                && Cursor.lockState == CursorLockMode.None
                && !_weJustChangedCursor)
            {
                _handledThisFrame = true;
                if (_verbose) Debug.Log("[Pause] ESC detectado via cambio de cursor (editor)");
                Pause();
            }
        }

        _lastFrameCursorLock = Cursor.lockState;
        _weJustChangedCursor = false;
    }

    /// <summary>Alterna entre pausado y reanudado.</summary>
    public void TogglePause()
    {
        if (_isPaused && IsSettingsOpen && _settings.CloseControls()) return;   // Esc en Controles: vuelve a Ajustes
        if (_isPaused && (AbandonDialog.IsOpen || IsSettingsOpen)) { CloseConfirm(); CloseSettings(); return; }   // Esc: atrás
        if (_isPaused) Resume();
        else Pause();
    }

    /// <summary>Pausa el juego y muestra el menu.</summary>
    public void Pause()
    {
        if (_isPaused) return;
        if (TutorialGate.Busy) return;   // (el tutorial está controlando el tiempo: no se puede pausar a la vez)
        _isPaused = true;
        IsPaused = true;

        if (_verbose) Debug.Log("[Pause] Pausado");

        Time.timeScale = 0f;
        AudioListener.pause = true;
        if (_beatManager != null) _beatManager.PauseMusic();
        MuteGameAudio();

        // FIX 2: Desactivar el input del jugador para que no se mueva ni gire la camara.
        if (_playerMovement != null) _playerMovement.enabled = false;
        if (_rhythmInput    != null) _rhythmInput.enabled    = false;

        if (_pauseMenuPanel != null) _pauseMenuPanel.SetActive(true);
        UpdateMenuButtonLabel();
        if (_pauseMenuGroup != null)
        {
            _pauseMenuGroup.alpha = 1f;
            _pauseMenuGroup.interactable = true;
            _pauseMenuGroup.blocksRaycasts = true;
        }

        // Esconder TODO el HUD (barra, mirilla, monedas, avisos…): solo queda el menú de pausa.
        HideHud();
        if (_hideWhilePaused != null)
            foreach (var go in _hideWhilePaused)
                if (go != null) go.SetActive(false);

        // Liberar cursor
        _previousCursorLockState = Cursor.lockState;
        _previousCursorVisible   = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        _weJustChangedCursor = true;
    }

    /// <summary>Reanuda el juego. Enganchalo al boton REANUDAR.</summary>
    public void Resume()
    {
        if (!_isPaused) return;
        CloseConfirm();
        CloseSettings();
        _isPaused = false;
        IsPaused = false;

        if (_verbose) Debug.Log("[Pause] Reanudado");

        UnmuteGameAudio();
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (_beatManager != null) _beatManager.ResumeMusic();

        // Reactivar input del jugador
        if (_playerMovement != null) _playerMovement.enabled = true;
        if (_rhythmInput    != null) _rhythmInput.enabled    = true;

        if (_pauseMenuGroup != null)
        {
            _pauseMenuGroup.alpha = 0f;
            _pauseMenuGroup.interactable = false;
            _pauseMenuGroup.blocksRaycasts = false;
        }
        if (_pauseMenuPanel != null) _pauseMenuPanel.SetActive(false);

        // Restaurar HUD
        if (_hideWhilePaused != null)
            foreach (var go in _hideWhilePaused)
                if (go != null) go.SetActive(true);
        RestoreHud();

        // Restaurar cursor
        Cursor.lockState = _previousCursorLockState;
        Cursor.visible   = _previousCursorVisible;
        _weJustChangedCursor = true;
    }

    private void LateUpdate()
    {
        // Mientras está en pausa, NINGÚN audio del juego puede sonar (canción del nivel, intro del nivel, relevos
        // programados del jefe o del lobby…): aunque algo arranque o estuviera programado para arrancar en mitad de
        // la pausa, se silencia.
        if (_isPaused)
        {
            _muteScan -= Time.unscaledDeltaTime;
            if (_muteScan <= 0f) { _muteScan = 0.1f; MuteGameAudio(); }
        }
    }

    // ── Silenciar el audio del juego durante la pausa ─────────────────────────
    private readonly List<AudioSource> _mutedByPause = new List<AudioSource>();
    private float _muteScan;

    private void MuteGameAudio()
    {
        foreach (AudioSource s in FindObjectsByType<AudioSource>())
        {
            if (s == null || s.mute || s.ignoreListenerPause) continue;   // (música de pausa, game over, clics de menú…)
            s.mute = true;
            _mutedByPause.Add(s);
        }
    }

    private void UnmuteGameAudio()
    {
        foreach (AudioSource s in _mutedByPause) if (s != null) s.mute = false;
        _mutedByPause.Clear();
    }

    private void OnDisable()
    {
        UnmuteGameAudio();
        // Defensivo: si destruyen el objeto mientras esta pausado, restauramos todo
        if (_isPaused)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
        IsPaused = false;
    }

    /// <summary>
    /// (Nivel tutorial) Si devuelve true, el botón MENÚ PRINCIPAL pasa a ser «VOLVER A LA SALA»: al repetir el tutorial
    /// o una parte desde una puerta de la sala, se puede dejar a medias y volver a ella. Lo ponen CourseBuilder.
    /// </summary>
    public static System.Func<bool> BackToRoomAvailable;
    public static System.Action BackToRoom;

    /// <summary>Texto del botón de menú principal según se pueda volver a la sala o no.</summary>
    private void UpdateMenuButtonLabel()
    {
        if (_pauseMenuPanel == null) return;
        bool room = BackToRoomAvailable != null && BackToRoomAvailable();
        foreach (Button b in _pauseMenuPanel.GetComponentsInChildren<Button>(true))
        {
            bool isMenu = false;
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                if (b.onClick.GetPersistentMethodName(i) == nameof(GoToMainMenu)) isMenu = true;
            if (!isMenu) continue;
            var tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp == null) continue;
            var lt = tmp.GetComponent<LocalizedText>();
            if (!_menuLabelSaved) { _menuLabelSaved = true; _menuEs = lt != null ? lt.spanish : tmp.text; _menuEn = lt != null ? lt.english : tmp.text; }
            string es = room ? "VOLVER A LA SALA" : _menuEs, en = room ? "BACK TO THE ROOM" : _menuEn;
            if (lt != null) { lt.spanish = es; lt.english = en; lt.Apply(); }
            else tmp.text = Loc.T(es, en);
        }
    }
    private bool _menuLabelSaved;
    private string _menuEs, _menuEn;

    /// <summary>Engancha al boton MENU PRINCIPAL.</summary>
    public void GoToMainMenu()
    {
        // Nivel tutorial, repitiendo una parte desde la sala: se vuelve a la sala (no al menú ni se abandona la prueba).
        if (BackToRoomAvailable != null && BackToRoomAvailable() && BackToRoom != null)
        {
            CloseConfirm();
            IsPaused = false;
            _isPaused = false;
            UnmuteGameAudio();
            Time.timeScale = 1f;
            AudioListener.pause = false;
            BackToRoom();
            return;
        }
        // Modo prueba: salir a mitad descalifica. Se avisa antes.
        if (StudySession.InSession && !AbandonDialog.IsOpen) { ShowAbandonConfirm(); return; }   // (también en el tutorial y la sala)
        LoadMainMenu();
    }

    private void LoadMainMenu()
    {
        if (_verbose) Debug.Log("[Pause] Volver al menu principal");
        IsPaused = false;
        UnmuteGameAudio();
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (string.IsNullOrEmpty(_mainMenuSceneName))
        {
            Debug.LogError("[Pause] No has configurado el nombre de la escena del menu principal.");
            return;
        }
        SceneManager.LoadScene(_mainMenuSceneName);
    }

    // Detecta muerte via energia (drain natural) O via GameOverManager (obstaculos, etc.)
    private bool IsPlayerDead()
    {
        if (_gameOverManager != null && _gameOverManager.IsDead) return true;
        if (EnergyManager.Instance == null) return false;
        return EnergyManager.Instance.IsDead;
    }

    // ── Esconder el HUD ───────────────────────────────────────────────────────

    private void HideHud() => HudHider.Hide(_pauseMenuPanel);
    private void RestoreHud() => HudHider.Restore();

    // ── Ajustes (Settings) ────────────────────────────────────────────────────
    // PauseMenuPanel/SettingsButton + el prefab compartido SettingsMenu (el mismo que en el menú principal).
    // (Se crean con «Tools/Menús/Rediseñar menús».)

    private SettingsMenu _settings;
    private readonly List<GameObject> _hiddenForSettings = new List<GameObject>();

    private bool IsSettingsOpen => _settings != null && _settings.gameObject.activeSelf;

    private void SetupSettings()
    {
        if (_pauseMenuPanel == null) return;
        _settings = _pauseMenuPanel.GetComponentInChildren<SettingsMenu>(true);
        Transform btn = _pauseMenuPanel.transform.Find("SettingsButton");
        if (_settings == null || btn == null)
        {
            Debug.LogWarning("[Pause] Falta el botón/panel de ajustes en el menú de pausa. Ejecuta «Tools/Menús/Rediseñar menús».");
            return;
        }
        _settings.gameObject.SetActive(false);
        _settings.onBack = CloseSettings;
        btn.GetComponent<Button>()?.onClick.AddListener(OpenSettings);

        // Metrónomo del volumen al tempo de la canción del nivel.
        BeatManager bm = _beatManager != null ? _beatManager : BeatManager.Instance;
        if (bm != null) _settings.Preview.Bpm = bm.Bpm;
    }

    /// <summary>Muestra los ajustes en lugar de los botones de pausa.</summary>
    public void OpenSettings()
    {
        if (_settings == null || IsSettingsOpen) return;
        _hiddenForSettings.Clear();
        foreach (Transform c in _pauseMenuPanel.transform)
            if (c.gameObject != _settings.gameObject && c.name != "BackgroundDim" && c.gameObject.activeSelf)
            {
                c.gameObject.SetActive(false);
                _hiddenForSettings.Add(c.gameObject);
            }
        _settings.gameObject.SetActive(true);
    }

    public void CloseSettings()
    {
        if (!IsSettingsOpen) return;
        _settings.gameObject.SetActive(false);
        foreach (var go in _hiddenForSettings) if (go != null) go.SetActive(true);
        _hiddenForSettings.Clear();
    }

    // ── Aviso: abandonar la prueba (AbandonDialog, compartido con el game over) ──

    private void ShowAbandonConfirm() => AbandonDialog.Show(Loc.T("Seguir jugando", "Keep playing"));
    private void CloseConfirm() => AbandonDialog.Close();
}
