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
    [SerializeField] private string _gameSceneName = "SampleScene";

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
        // Asegurarse de que empieza en el menu principal
        ShowMain();

        // Cursor visible en el menu
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // Cargar valores guardados si existen
        if (_volumeSlider != null)
        {
            _volumeSlider.value = PlayerPrefs.GetFloat("MasterVolume", 1f);
            _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        }
        if (_sensitivitySlider != null)
        {
            _sensitivitySlider.value = PlayerPrefs.GetFloat("MouseSensitivity", 2f);
            _sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        }
    }

    // -------------------------------------------------------------------------
    // Botones del menu principal
    // -------------------------------------------------------------------------

    /// <summary>Boton PLAY: carga la escena del juego.</summary>
    public void Play()
    {
        if (string.IsNullOrEmpty(_gameSceneName))
        {
            Debug.LogError("[MainMenu] No has configurado el nombre de la escena del juego.");
            return;
        }
        AudioListener.pause  = false;
        AudioListener.volume = PlayerPrefs.GetFloat("MasterVolume", 1f);
        SceneManager.LoadScene(_gameSceneName);
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

    private void OnVolumeChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("MasterVolume", value);
    }

    private void OnSensitivityChanged(float value)
    {
        PlayerPrefs.SetFloat("MouseSensitivity", value);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void ShowMain()
    {
        if (_mainPanel     != null) _mainPanel.SetActive(true);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
    }
}
