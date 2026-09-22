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

    [Header("Muerte por agua (caer al río)")]
    [Tooltip("Layer del agua (el mismo agua sólida del kayak). Al caer en ella = game over.")]
    [SerializeField] private string _waterLayerName = "Water";
    [Tooltip("Distancia máxima a la que se busca la superficie del agua por debajo del jugador.")]
    [SerializeField] private float _waterCheckDistance = 200f;
    [Tooltip("Margen sobre la superficie: muere cuando los pies quedan a esta distancia (o menos) del agua.")]
    [SerializeField] private float _waterSurfaceThreshold = 0.2f;
    [Tooltip("(Opcional) AudioSource y sonido de splash al caer al agua.")]
    [SerializeField] private AudioSource _splashAudio;
    [SerializeField] private AudioClip   _splashSound;
    [Tooltip("(Opcional) Prefab de partículas de splash en la superficie.")]
    [SerializeField] private GameObject  _splashPrefab;
    [SerializeField] private float       _splashLifetime = 3f;

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

    private int                 _waterMask = 0;    // mask del layer del agua
    private Transform           _playerTransform;  // transform del jugador (para el raycast)
    private CharacterController _playerCC;          // CC del jugador (para saber dónde están los pies)

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

        // Muerte por agua: cachear el layer del agua y las referencias del jugador.
        int w = LayerMask.NameToLayer(_waterLayerName);
        _waterMask = w == -1 ? 0 : (1 << w);
        if (w == -1)
            Debug.LogError($"[GameOver] El layer \"{_waterLayerName}\" no existe. Créalo en Project Settings > Tags and Layers.", this);

        if (_playerMovement == null) _playerMovement = FindAnyObjectByType<PlayerMovement>();
        if (_playerMovement != null)
        {
            _playerTransform = _playerMovement.transform;
            _playerCC        = _playerMovement.GetComponent<CharacterController>();
        }
    }

    // =========================================================================
    // UPDATE — vineteo parcial mientras vive
    // =========================================================================

    private void Update()
    {
        if (_isDead) return;

        // Muerte por caer al agua (río de sangre): se comprueba siempre, viva o no la energía.
        if (CheckWaterDeath())
        {
            HandleDeath(isObstacle: true);
            return;
        }

        if (EnergyManager.Instance == null) return;

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

    /// <summary>
    /// True si el jugador ha caído al agua. Busca la superficie del agua HACIA ABAJO (su cara superior,
    /// la misma sobre la que flota el kayak) y comprueba si los PIES del jugador han llegado a esa altura.
    /// Funciona tanto si el jugador se queda encima del agua como si la atraviesa. No aplica en kayak/tirolina.
    /// </summary>
    private bool CheckWaterDeath()
    {
        if (_waterMask == 0 || _playerTransform == null) return false;
        if (_playerMovement != null && (_playerMovement.IsOnKayak || _playerMovement.IsOnZipline)) return false;

        Vector3 origin  = _playerTransform.position + Vector3.up * _waterCheckDistance;
        float   maxDist = _waterCheckDistance * 2f;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                             maxDist, _waterMask, QueryTriggerInteraction.Ignore))
            return false;

        float waterY = hit.point.y;
        float feetY  = _playerCC != null ? _playerCC.bounds.min.y : _playerTransform.position.y;
        if (feetY > waterY + _waterSurfaceThreshold) return false;

        // Splash opcional en la superficie.
        Vector3 splashPos = new Vector3(_playerTransform.position.x, waterY, _playerTransform.position.z);
        if (_splashAudio != null && _splashSound != null) _splashAudio.PlayOneShot(_splashSound);
        if (_splashPrefab != null)
        {
            GameObject fx = Instantiate(_splashPrefab, splashPos, Quaternion.identity);
            if (_splashLifetime > 0f) Destroy(fx, _splashLifetime);
        }

        if (_verbose) Debug.Log($"[GameOver] Muerte por agua. feetY={feetY:F2} waterY={waterY:F2}");
        return true;
    }

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
