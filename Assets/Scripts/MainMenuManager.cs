using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controla el menu principal: Play, Tienda (OpenShop), Settings y Quit.
///
/// SETUP:
///   1. Crea una escena nueva llamada exactamente "MainMenu".
///   2. Anade un Canvas (Screen Space - Overlay) con un Panel de fondo.
///   3. Anade este script a un GameObject vacio "MainMenuManager" en la escena.
///   4. Conecta los botones y paneles desde el Inspector.
///   5. Anade AMBAS escenas en File -> Build Profiles (MainMenu primero, luego la de juego).
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    [Header("Nombre de la escena del juego")]
    [Tooltip("Debe coincidir exactamente con el nombre del archivo .unity de tu escena de juego")]
    [SerializeField] private string _gameSceneName = "Level1";

    [Header("Prueba con jugadores")]
    [Tooltip("Versión para la PRUEBA CON JUGADORES (la que se reparte para recoger feedback): el botón Play pasa a ser\n" +
             "«Participar en la prueba» y abre el consentimiento del modo prueba (ver StudySession / StudyHub).")]
    [SerializeField] private bool _playtest = false;
    [Tooltip("(Opcional) Botón Play. Si está vacío se busca un hijo llamado «Play» dentro del panel principal.")]
    [SerializeField] private Button _playButton;

    [Header("Música de fondo")]
    [Tooltip("Canción que suena en bucle en el menú (por defecto, la de Game Over).")]
    [SerializeField] private AudioClip _menuMusic;
    [Range(0f, 1f)] [SerializeField] private float _menuMusicVolume = 0.6f;
    [Tooltip("BPM de la canción del menú (el metrónomo del volumen suena a este tempo y en sus beats). GameOver.wav ≈ 60.")]
    [SerializeField] private float _menuMusicBpm = 60f;
    [Tooltip("Segundos desde el inicio de la canción hasta su primer beat (para cuadrar los tics con la música).")]
    [SerializeField] private float _menuMusicFirstBeat = 0.967f;
    [Tooltip("Segundos de fundido al entrar.")]
    [SerializeField] private float _menuMusicFadeIn = 2f;
    private AudioSource _musicSource;

    public static bool PlaytestBuild { get; private set; }

    [Header("Paneles")]
    [Tooltip("Panel raiz del menu principal (botones Play, Settings, Quit)")]
    [SerializeField] private GameObject _mainPanel;
    [Tooltip("Panel de ajustes (se muestra al pulsar Settings, oculto al inicio)")]
    [SerializeField] private GameObject _settingsPanel;

    [Header("Settings - Sliders opcionales")]
    [Tooltip("Slider de volumen master (opcional)")]
    [SerializeField] private Slider _volumeSlider;
    [Tooltip("Slider de sensibilidad del raton (opcional)")]
    [SerializeField] private Slider _sensitivitySlider;

    private void Start()
    {
        StartMenuMusic();
        PlaytestBuild = _playtest;
        if (_playtest) SetupPlaytestButton();
        // Asegurarse de que empieza en el menu principal
        ShowMain();

        // Cursor visible en el menu
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // Ajustes: el panel compartido SettingsMenu (el mismo que en la pausa). Al mover el volumen suena el
        // metrónomo al tempo de la música del menú y en sus beats.
        _settingsMenu = _settingsPanel != null ? _settingsPanel.GetComponentInChildren<SettingsMenu>(true) : null;
        if (_settingsMenu != null)
        {
            _settingsMenu.onBack = CloseSettings;
            _settingsMenu.Preview.Bpm = _menuMusicBpm;
            _settingsMenu.Preview.SyncTo = _musicSource;
            _settingsMenu.Preview.SyncOffset = _menuMusicFirstBeat;
        }
        else
        {
            // (Escena antigua, sin el prefab: sliders sueltos.)
            _preview = new VolumePreview(gameObject) { Bpm = _menuMusicBpm, SyncTo = _musicSource, SyncOffset = _menuMusicFirstBeat };
            GameSettings.BindSliders(_volumeSlider, _sensitivitySlider, _preview);
        }
    }

    // ── Metrónomo al ajustar el volumen ───────────────────────────────────────

    private VolumePreview _preview;
    private SettingsMenu _settingsMenu;

    private void StartMenuMusic()
    {
        if (_menuMusic == null) return;
        _musicSource = gameObject.AddComponent<AudioSource>();
        _musicSource.clip = _menuMusic;
        _musicSource.loop = true;
        _musicSource.playOnAwake = false;
        _musicSource.spatialBlend = 0f;
        _musicSource.volume = _menuMusicFadeIn > 0f ? 0f : _menuMusicVolume;
        _musicSource.Play();
    }

    private void Update()
    {
        // Fundido de entrada de la música del menú.
        if (_musicSource != null && _musicSource.volume < _menuMusicVolume)
            _musicSource.volume = Mathf.MoveTowards(_musicSource.volume, _menuMusicVolume,
                                                    _menuMusicVolume * Time.unscaledDeltaTime / Mathf.Max(0.01f, _menuMusicFadeIn));

        _preview?.Update();
    }

    // -------------------------------------------------------------------------
    // Botones del menu principal
    // -------------------------------------------------------------------------

    /// <summary>Versión de prueba: el texto del botón Play pasa a «PARTICIPAR EN LA PRUEBA».</summary>
    private void SetupPlaytestButton()
    {
        if (_playButton == null && _mainPanel != null)
            foreach (Button b in _mainPanel.GetComponentsInChildren<Button>(true))
                if (b.name == "Play") { _playButton = b; break; }
        if (_playButton == null) { Debug.LogWarning("[MainMenu] Modo prueba: no se encontró el botón Play."); return; }

        var tmp = _playButton.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (tmp != null)
        {
            // (El botón puede tener LocalizedText: se cambian sus dos textos para que el idioma no lo pise.)
            var lt = tmp.GetComponent<LocalizedText>();
            if (lt != null) { lt.spanish = "PARTICIPAR EN LA PRUEBA"; lt.english = "TAKE PART IN THE TEST"; lt.Apply(); }
            else tmp.text = Loc.T("PARTICIPAR EN LA PRUEBA", "TAKE PART IN THE TEST");
            tmp.enableAutoSizing = true;          // (texto más largo: que quepa en el botón)
            tmp.fontSizeMin = 4f;
            tmp.fontSizeMax = Mathf.Max(tmp.fontSize, 8f);
        }
        else
        {
            var legacy = _playButton.GetComponentInChildren<Text>(true);
            if (legacy != null) { legacy.text = Loc.T("PARTICIPAR EN LA PRUEBA", "TAKE PART IN THE TEST"); legacy.resizeTextForBestFit = true; }
        }
    }

    /// <summary>Boton PLAY: carga la escena del juego (en la versión de prueba, abre el consentimiento).</summary>
    public void Play()
    {
        if (_playtest)
        {
            StudyHub.OpenConsent();
            return;
        }
        if (string.IsNullOrEmpty(_gameSceneName))
        {
            Debug.LogError("[MainMenu] No has configurado el nombre de la escena del juego.");
            return;
        }
        AudioListener.pause  = false;
        AudioListener.volume = GameSettings.Volume;
        // Si el nombre del Inspector no es una escena de Build Settings (p. ej. el antiguo «SampleScene»), al Nivel 1.
        string scene = Application.CanStreamedLevelBeLoaded(_gameSceneName) ? _gameSceneName : "Level1";
        if (scene != _gameSceneName)
            Debug.LogWarning($"[MainMenu] La escena «{_gameSceneName}» no está en Build Settings: se carga «{scene}». " +
                             "Cambia «Game Scene Name» en el MainMenuManager.");
        SceneManager.LoadScene(scene);
    }

    /// <summary>Boton TIENDA: abre la tienda (se construye sola) y al cerrarla vuelve al menu.</summary>
    public void OpenShop()
    {
        if (_mainPanel    != null) _mainPanel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        ShopUI.Open(ShowMain);
    }

    /// <summary>Boton SETTINGS: muestra el panel de ajustes.</summary>
    public void OpenSettings()
    {
        if (_mainPanel    != null) _mainPanel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(true);
    }

    /// <summary>Boton BACK dentro de Settings: vuelve al menu principal.</summary>
    public void CloseSettings()
    {
        ShowMain();
    }

    /// <summary>Boton QUIT: cierra la aplicacion.</summary>
    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // -------------------------------------------------------------------------
    // Sliders de settings
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void ShowMain()
    {
        if (_mainPanel     != null) _mainPanel.SetActive(true);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
    }
}
