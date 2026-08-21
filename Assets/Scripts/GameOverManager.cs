using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// FLUJO:
///
///   Mientras vive:
///     _visionDimImage (IrisWipe shader, parcial) se cierra desde los bordes
///     segun baja la energia. Progress va de 0 a _maxVisionProgress.
///
///   Al morir por energia (energia = 0):
///     _irisWipeImage cierra completamente (0->1) en _energyFadeDuration.
///     Cuando Progress=1: congelar juego, mostrar panel.
///
///   Al morir por obstaculo (TriggerGameOver):
///     Crash sound + cierre rapido (0->1) en _crashFadeDuration.
///     Cuando Progress=1: congelar juego, mostrar panel.
/// </summary>
public class GameOverManager : MonoBehaviour
{
    // ── Referencias ──────────────────────────────────────────────────────────

    [Header("Referencias logicas")]
    [SerializeField] private LowHealthEffects _lowHealthEffects;

    [Header("Scripts del jugador (se desactivan al morir)")]
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private RhythmInput    _rhythmInput;

    // ── UI ──────────────────────────────────────────────────────────────────

    [Header("UI - Vineteo de poca vida (IrisWipe parcial, se anima en Update)")]
    [Tooltip("Image con Material IrisWipe. Se cierra PARCIALMENTE segun la energia.")]
    [SerializeField] private Image _visionDimImage;
    [Tooltip("Hasta que valor de Progress llega el iris al tener energia 0. " +
             "0.5 = vineteo de bordes. 1.0 = negro total (no recomendado aqui).")]
    [Range(0f, 0.95f)]
    [SerializeField] private float _maxVisionProgress = 0.5f;
    [SerializeField] private float _visionSmoothing = 5f;
    [Range(0f, 1f)]
    [SerializeField] private float _visionDimStartThreshold = 0.4f;
    [SerializeField] private AnimationCurve _visionDimCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("UI - Cierre de iris al morir (IrisWipe completo)")]
    [Tooltip("Image con Material IrisWipe. Cierra TODO la pantalla al morir (0->1).")]
    [SerializeField] private Image _irisWipeImage;

    [Header("UI - Panel de Game Over")]
    [SerializeField] private GameObject  _gameOverPanel;
    [SerializeField] private CanvasGroup _gameOverPanelGroup;

    // ── Fade de muerte ────────────────────────────────────────────────────────

    [Header("Cierre de iris - agotamiento de energia")]
    [SerializeField] private float          _energyFadeDuration = 1.0f;
    [SerializeField] private AnimationCurve _energyFadeCurve    = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Cierre de iris - obstaculo (crash)")]
    [SerializeField] private float          _crashFadeDuration = 0.3f;
    [SerializeField] private AnimationCurve _crashFadeCurve    = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [SerializeField] private AudioClip   _crashSound;
    [SerializeField] private AudioSource _sfxSource;

    // ── Audio ─────────────────────────────────────────────────────────────────

    [Header("Audio")]
    [SerializeField] private bool  _attenuateAudio = true;
    [Range(0f, 1f)]
    [SerializeField] private float _finalAudioVolume = 0f;

    [Header("Restart - restaurar musica")]
    [SerializeField] private AudioSource        _musicSourceToRestart;
    [SerializeField] private AudioLowPassFilter _musicLowPassToReset;
    [SerializeField] private float              _resetLowPassCutoff = 22000f;

    // ── Panel ─────────────────────────────────────────────────────────────────

    [Header("Panel de Game Over")]
    [SerializeField] private float _panelAppearDelay = 0f;
    [SerializeField] private float _panelFadeInTime  = 0.4f;
    [SerializeField] private bool  _showCursorOnDeath = true;

    [Header("Debug")]
    [SerializeField] private bool _verbose = true;

    // ── Estado ───────────────────────────────────────────────────────────────

    private bool     _isDead;
    private bool     _isObstacleDeath;
    private float    _baseAudioVolume          = 1f;
    private float    _musicSourceOriginalVolume = 1f;

    private Material _visionMatInst;   // instancia del material del vineteo
    private Material _irisMatInst;     // instancia del material del cierre de iris
    private float    _visionProgress;  // valor actual del progress del vineteo

    public bool IsDead => _isDead;

    // =========================================================================
    // INICIO
    // =========================================================================

    private void Start()
    {
        // VisionDim: instanciar material y asegurar fullscreen
        if (_visionDimImage != null)
        {
            _visionMatInst = new Material(_visionDimImage.material);
            _visionDimImage.material = _visionMatInst;
            _visionMatInst.SetFloat("_Progress", 0f);
            _visionProgress = 0f;
            ForceStretch(_visionDimImage.gameObject);
        }

        // IrisWipe (muerte): instanciar material y asegurar fullscreen
        if (_irisWipeImage != null)
        {
            _irisMatInst = new Material(_irisWipeImage.material);
            _irisWipeImage.material = _irisMatInst;
            _irisMatInst.SetFloat("_Progress", 0f);
            ForceStretch(_irisWipeImage.gameObject);
        }

        // Panel: desactivado e invisible
        InitGroup(_gameOverPanelGroup, alpha: 0f);
        if (_gameOverPanel != null)
        {
            _gameOverPanel.SetActive(false);
            ForceStretch(_gameOverPanel);
        }

        _baseAudioVolume = AudioListener.volume;
        if (_musicSourceToRestart != null)
            _musicSourceOriginalVolume = _musicSourceToRestart.volume;
    }

    // =========================================================================
    // UPDATE — vineteo parcial mientras vive
    // =========================================================================

    private void Update()
    {
        if (_isDead || EnergyManager.Instance == null) return;

        bool  isBlue = EnergyManager.Instance.IsBlueMode;
        float energy = EnergyManager.Instance.EnergyPercent;

        float targetProgress;

        if (isBlue)
        {
            targetProgress = 0f;
        }
        else
        {
            float depth = (energy < _visionDimStartThreshold && _visionDimStartThreshold > 0f)
                ? 1f - (energy / _visionDimStartThreshold) : 0f;

            targetProgress = _visionDimCurve.Evaluate(Mathf.Clamp01(depth)) * _maxVisionProgress;
        }

        // Lerp suave hacia el target (el iris del vineteo solo va de 0 a _maxVisionProgress)
        _visionProgress = Mathf.Lerp(_visionProgress, targetProgress, Time.deltaTime * _visionSmoothing);
        if (_visionMatInst != null)
            _visionMatInst.SetFloat("_Progress", _visionProgress);

        // El _irisWipeImage (muerte) NO se toca aqui: siempre en 0 hasta que muere

        if (!isBlue && energy <= 0f)
            HandleDeath(isObstacle: false);
    }

    // =========================================================================
    // LOGICA DE MUERTE
    // =========================================================================

    private void HandleDeath(bool isObstacle)
    {
        if (_isDead) return;
        _isDead          = true;
        _isObstacleDeath = isObstacle;

        if (_verbose)
            Debug.Log($"[GameOver] Muerte por {(isObstacle ? "obstaculo" : "energia")}.");

        if (_playerMovement   != null) _playerMovement.enabled   = false;
        if (_rhythmInput      != null) _rhythmInput.enabled      = false;
        if (_lowHealthEffects != null) _lowHealthEffects.enabled = false;

        StartCoroutine(IrisCloseAndFreeze());
    }

    private IEnumerator IrisCloseAndFreeze()
    {
        float duration;
        AnimationCurve curve;

        if (_isObstacleDeath)
        {
            if (_crashSound != null && _sfxSource != null)
                _sfxSource.PlayOneShot(_crashSound);
            duration = _crashFadeDuration;
            curve    = _crashFadeCurve;
        }
        else
        {
            duration = _energyFadeDuration;
            curve    = _energyFadeCurve;
        }

        // El iris de muerte arranca siempre desde 0
        // (el vineteo sigue visible debajo durante el cierre, creando capas)
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = curve.Evaluate(Mathf.Clamp01(elapsed / duration));

            if (_irisMatInst != null) _irisMatInst.SetFloat("_Progress", t);

            if (_attenuateAudio)
                AudioListener.volume = Mathf.Lerp(_baseAudioVolume, _finalAudioVolume, t);

            yield return null;
        }

        // Cerrar completamente
        if (_irisMatInst != null) _irisMatInst.SetFloat("_Progress", 1f);
        if (_attenuateAudio) AudioListener.volume = _finalAudioVolume;

        // Congelar juego
        Time.timeScale      = 0f;
        AudioListener.pause = true;

        if (_showCursorOnDeath)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        if (_verbose) Debug.Log("[GameOver] Iris cerrado. Juego congelado.");

        yield return StartCoroutine(ShowGameOverPanel());
    }

    private IEnumerator ShowGameOverPanel()
    {
        if (_panelAppearDelay > 0f)
            yield return new WaitForSecondsRealtime(_panelAppearDelay);

        if (_gameOverPanel != null) _gameOverPanel.SetActive(true);

        if (_gameOverPanelGroup != null)
        {
            if (_panelFadeInTime > 0f)
            {
                float elapsed = 0f;
                while (elapsed < _panelFadeInTime)
                {
                    elapsed += Time.unscaledDeltaTime;
                    _gameOverPanelGroup.alpha = Mathf.Clamp01(elapsed / _panelFadeInTime);
                    yield return null;
                }
            }

            _gameOverPanelGroup.alpha          = 1f;
            _gameOverPanelGroup.interactable   = true;
            _gameOverPanelGroup.blocksRaycasts = true;
        }

        if (_verbose) Debug.Log("[GameOver] Panel visible.");
    }

    // =========================================================================
    // API PUBLICA
    // =========================================================================

    public void TriggerGameOver() => HandleDeath(isObstacle: true);

    public void Restart()
    {
        if (_verbose) Debug.Log("[GameOver] Restart.");
        Time.timeScale       = 1f;
        AudioListener.pause  = false;
        AudioListener.volume = 1f;

        if (_musicSourceToRestart != null)
        {
            _musicSourceToRestart.volume = _musicSourceOriginalVolume;
            _musicSourceToRestart.Stop();
            _musicSourceToRestart.time = 0f;
            _musicSourceToRestart.Play();
        }
        if (_musicLowPassToReset != null)
            _musicLowPassToReset.cutoffFrequency = _resetLowPassCutoff;

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void Quit()
    {
        if (_verbose) Debug.Log("[GameOver] Quit.");
        Time.timeScale       = 1f;
        AudioListener.pause  = false;
        AudioListener.volume = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static void InitGroup(CanvasGroup group, float alpha)
    {
        if (group == null) return;
        group.alpha          = alpha;
        group.interactable   = false;
        group.blocksRaycasts = false;
    }

    /// <summary>
    /// Fuerza el RectTransform a cubrir toda la pantalla eliminando
    /// cualquier offset o anchor incorrecto. Resuelve el "sliver" gap.
    /// </summary>
    private static void ForceStretch(GameObject go)
    {
        if (go == null) return;
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.offsetMin        = Vector2.zero;   // left / bottom = 0
        rt.offsetMax        = Vector2.zero;   // right / top   = 0
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero;
    }
}
