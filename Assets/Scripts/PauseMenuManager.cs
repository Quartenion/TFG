using UnityEngine;
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
    }

    private void Update()
    {
        _handledThisFrame = false;

        // --- Bloquear pausa si el jugador ya esta muerto ---
        if (_ignoreWhenDead && IsPlayerDead())
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
        if (_isPaused) Resume();
        else Pause();
    }

    /// <summary>Pausa el juego y muestra el menu.</summary>
    public void Pause()
    {
        if (_isPaused) return;
        _isPaused = true;

        if (_verbose) Debug.Log("[Pause] Pausado");

        Time.timeScale = 0f;
        AudioListener.pause = true;
        if (_beatManager != null) _beatManager.PauseMusic();

        // FIX 2: Desactivar el input del jugador para que no se mueva ni gire la camara.
        if (_playerMovement != null) _playerMovement.enabled = false;
        if (_rhythmInput    != null) _rhythmInput.enabled    = false;

        if (_pauseMenuPanel != null) _pauseMenuPanel.SetActive(true);
        if (_pauseMenuGroup != null)
        {
            _pauseMenuGroup.alpha = 1f;
            _pauseMenuGroup.interactable = true;
            _pauseMenuGroup.blocksRaycasts = true;
        }

        // Esconder HUD
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
        _isPaused = false;

        if (_verbose) Debug.Log("[Pause] Reanudado");

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

        // Restaurar cursor
        Cursor.lockState = _previousCursorLockState;
        Cursor.visible   = _previousCursorVisible;
        _weJustChangedCursor = true;
    }

    private void OnDisable()
    {
        // Defensivo: si destruyen el objeto mientras esta pausado, restauramos todo
        if (_isPaused)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }

    /// <summary>Engancha al boton MENU PRINCIPAL.</summary>
    public void GoToMainMenu()
    {
        if (_verbose) Debug.Log("[Pause] Volver al menu principal");
        Time.timeScale = 1f;
        AudioListener.pause = false;
        AudioListener.volume = 1f;

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
}
