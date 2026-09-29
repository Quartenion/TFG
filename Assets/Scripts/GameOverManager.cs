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

    [Header("Música de Game Over")]
    [Tooltip("Tema que suena al morir (game over de verdad, no al reaparecer en un checkpoint ni al revivir).\n" +
             "La música del nivel se corta en seco en ese mismo instante.")]
    [SerializeField] private AudioClip _gameOverMusic;
    [Range(0f, 1f)]
    [SerializeField] private float _gameOverMusicVolume = 0.8f;
    [SerializeField] private bool  _gameOverMusicLoop   = true;
    [Tooltip("Segundos de SILENCIO (pantalla negra) entre el cierre del iris y la aparición de la pantalla de game over.")]
    [Min(0f)]
    [SerializeField] private float _silenceBeforeGameOver = 0f;
    [Tooltip("Segundos que tarda la música de game over en subir de 0 a su volumen (empieza al activarse los botones).")]
    [Min(0f)]
    [SerializeField] private float _gameOverMusicFadeIn = 2f;

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
    [Tooltip("Encima de un kayak NO se muere por agua mientras los pies no bajen más de esto (m) por debajo de la\n" +
             "superficie: así el kayak tiene tiempo de montarte al aterrizar sobre él.")]
    [SerializeField] private float _kayakLandingTolerance = 1f;
    [Tooltip("(Opcional) AudioSource y sonido de splash al caer al agua.")]
    [SerializeField] private AudioSource _splashAudio;
    [SerializeField] private AudioClip   _splashSound;
    [Tooltip("(Opcional) Prefab de partículas de splash en la superficie.")]
    [SerializeField] private GameObject  _splashPrefab;
    [SerializeField] private float       _splashLifetime = 3f;

    [Header("Vida extra (tienda)")]
    [Tooltip("(Opcional) Sonido al gastar la vida extra.")]
    [SerializeField] private AudioClip _reviveSound;

    [Header("Debug")]
    [SerializeField] private bool _verbose = true;

    // ── Estado ───────────────────────────────────────────────────────────────

    private bool     _isDead;
    private bool     _reviving;        // gastando una vida extra (hasta el siguiente beat)
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

        // Si tienes vidas extra, que se vean en el HUD.
        if (PlayerProgress.GetCount(ShopIds.ExtraLife) > 0 || PlayerProgress.GetCount(ShopIds.Shield) > 0 ||
            PlayerProgress.GetCount(ShopIds.Checkpoint) > 0) CoinHUD.Ensure();

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
            if (TryParachute()) return;   // («Paracaídas»: una vez por nivel rebotas)
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

        // Cayendo / aterrizando ENCIMA de un kayak: el kayak flota a ras del agua, así que los pies quedan "al nivel
        // del agua" aunque estés sobre él. Mientras no te hundas de verdad, se deja que el kayak te monte.
        if (feetY > waterY - _kayakLandingTolerance && KayakController.IsKayakUnder(_playerTransform.position))
            return false;

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

        // Muerte por SANGRE con vida extra comprada → no hay game over: se vuelve en el siguiente beat.
        if (!isObstacle && TryRevive()) return;
        _isDead          = true;
        _isObstacleDeath = isObstacle;

        if (_verbose)
            Debug.Log($"[GameOver] Muerte por {(isObstacle ? "obstaculo" : "energia")}.");

        if (_playerMovement   != null) _playerMovement.enabled   = false;
        if (_rhythmInput      != null) _rhythmInput.enabled      = false;
        if (_lowHealthEffects != null) _lowHealthEffects.enabled = false;

        // ¿Checkpoint? (consumible de la tienda): se gasta uno y se recarga el nivel desde el último que pasaste.
        if (CheckpointSystem.TryRespawn())
        {
            RunStats.HelpUsed();
            StartCoroutine(CheckpointRespawn());
            return;
        }

        StartCoroutine(IrisCloseAndFreeze());
    }

    /// <summary>Cierre de iris rápido y recarga del nivel: StartSequenceManager te coloca en el checkpoint.</summary>
    private IEnumerator CheckpointRespawn()
    {
        // La música del nivel se corta EN SECO (si seguía sonando durante el cierre del iris, sonaba "bugeada").
        StopLevelMusic();
        int left = PlayerProgress.GetCount(ShopIds.Checkpoint);
        if (_verbose) Debug.Log($"[GameOver] Checkpoint: volviendo al último (quedan {left}).");
        ReviveFX.ShowMessage("CHECKPOINT", left > 0 ? $"Te quedan {left}" : "Era el último", new Color(0.25f, 0.7f, 0.35f, 0.55f));

        // La escena se empieza a cargar YA, en segundo plano, mientras se cierra el iris (antes: primero el iris y
        // luego una carga de golpe que congelaba el juego unos segundos).
        AsyncOperation op = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);
        if (op != null) op.allowSceneActivation = false;

        float elapsed = 0f, dur = 0.4f;
        while (elapsed < dur || (op != null && op.progress < 0.9f))
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            if (_irisMatInst != null) _irisMatInst.SetFloat("_Progress", t);
            if (_attenuateAudio) AudioListener.volume = Mathf.Lerp(_baseAudioVolume, 0f, t);
            yield return null;
        }

        Time.timeScale       = 1f;
        AudioListener.pause  = false;
        AudioListener.volume = _baseAudioVolume > 0f ? _baseAudioVolume : 1f;
        if (op != null) op.allowSceneActivation = true;
        else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private AudioSource _gameOverMusicSource;

    /// <summary>Corta YA la música del nivel (y el resto de pistas largas). La del game over llega después.</summary>
    private void StopLevelMusic()
    {
        BeatManager bm = BeatManager.Instance;
        if (bm != null) bm.StopMusic();
        if (_musicSourceToRestart != null) _musicSourceToRestart.Stop();
        // Cualquier otra pista de música que siga sonando (intro, loop del jefe, final…): fuera.
        foreach (AudioSource src in FindObjectsByType<AudioSource>())
            if (src != null && src != _gameOverMusicSource && src.isPlaying && src.clip != null && src.clip.length > 15f)
                src.Stop();
    }

    /// <summary>Arranca la música del game over subiendo de 0 a su volumen en _gameOverMusicFadeIn segundos.</summary>
    private IEnumerator PlayGameOverMusic()
    {
        if (_gameOverMusic == null) yield break;
        if (_gameOverMusicSource == null)
        {
            _gameOverMusicSource = gameObject.AddComponent<AudioSource>();
            _gameOverMusicSource.playOnAwake  = false;
            _gameOverMusicSource.spatialBlend = 0f;
            _gameOverMusicSource.priority     = 0;
            _gameOverMusicSource.ignoreListenerPause = true;   // sigue sonando con el juego congelado
        }
        _gameOverMusicSource.clip   = _gameOverMusic;
        _gameOverMusicSource.loop   = _gameOverMusicLoop;
        _gameOverMusicSource.volume = 0f;
        _gameOverMusicSource.pitch  = 1f;
        _gameOverMusicSource.Play();

        float el = 0f;
        while (el < _gameOverMusicFadeIn && _gameOverMusicSource != null && _gameOverMusicSource.isPlaying)
        {
            el += Time.unscaledDeltaTime;
            _gameOverMusicSource.volume = _gameOverMusicVolume * Mathf.Clamp01(el / _gameOverMusicFadeIn);
            yield return null;
        }
        if (_gameOverMusicSource != null && _gameOverMusicSource.isPlaying) _gameOverMusicSource.volume = _gameOverMusicVolume;
    }

    private IEnumerator IrisCloseAndFreeze()
    {
        StopLevelMusic();
        // GAME OVER: te quedas las monedas que habías cogido (pasan al monedero); al reiniciar vuelven a estar todas.
        int kept = LevelCoins.BankOnDeath();
        if (_verbose && kept > 0) Debug.Log($"[GameOver] +{kept} monedas guardadas en el monedero.");
        bool hasGoMusic = _gameOverMusic != null;

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

            // (Con música de game over no se baja el volumen general: si no, se apagaría también ella.)
            if (_attenuateAudio && !hasGoMusic)
                AudioListener.volume = Mathf.Lerp(_baseAudioVolume, _finalAudioVolume, t);

            yield return null;
        }

        // Cerrar completamente
        if (_irisMatInst != null) _irisMatInst.SetFloat("_Progress", 1f);
        if (_attenuateAudio && !hasGoMusic) AudioListener.volume = _finalAudioVolume;

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
        // Unos segundos de silencio con la pantalla en negro antes de que aparezca el game over.
        float wait = _panelAppearDelay + _silenceBeforeGameOver;
        if (wait > 0f)
            yield return new WaitForSecondsRealtime(wait);

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

        // Botones activos → empieza la música del game over (de 0 a su volumen).
        StartCoroutine(PlayGameOverMusic());

        if (_verbose) Debug.Log("[GameOver] Panel visible.");
    }

    // =========================================================================
    // API PUBLICA
    // =========================================================================

    /// <summary>
    /// Game over. Si llega con la jeringuilla LLENA (p. ej. desde EnergyManager.OnSyringeFull) cuenta como muerte por
    /// sangre (se puede salvar con una vida extra); si no, es un choque/caída (no se salva con vida extra).
    /// </summary>
    /// <summary>
    /// Golpe LETAL de un obstáculo / ataque. Si tienes ESCUDO lo para (el obstáculo se rompe) y devuelve false;
    /// si no, game over (o checkpoint) y devuelve true.
    /// </summary>
    public bool TryKillByObstacle(GameObject source)
    {
        if (_isDead) return true;
        if (TimeRewinder.IsRewinding) return false;   // (volviendo atrás en el tiempo: nada mata)

        // REBOBINADO (tienda): chocar contra un obstáculo → el tiempo vuelve 4 beats atrás, a un momento seguro.
        if (source != null && PlayerShield.Count > 0 && TimeRewinder.Instance != null && TimeRewinder.Instance.TryRewind())
            return false;

        // Golpe sin obstáculo físico (ataque del jefe…) o sin sitio seguro al que volver: el escudo lo para.
        if (source == null && PlayerShield.TryAbsorb(null)) return false;

        HandleDeath(isObstacle: true);
        return true;
    }

    /// <summary>
    /// GAME OVER DIRECTO (p. ej. contrato «Contrarreloj» incumplido): sin vidas extra, ni Adrenalina, ni checkpoint.
    /// </summary>
    public void ForceGameOver(string title = null, string subtitle = null)
    {
        if (_isDead) return;
        _isDead          = true;
        _isObstacleDeath = true;
        if (_verbose) Debug.Log($"[GameOver] Game over forzado: {title} {subtitle}");

        if (_playerMovement   != null) _playerMovement.enabled   = false;
        if (_rhythmInput      != null) _rhythmInput.enabled      = false;
        if (_lowHealthEffects != null) _lowHealthEffects.enabled = false;

        if (!string.IsNullOrEmpty(title))
            ReviveFX.ShowMessage(title, subtitle, new Color(0.85f, 0.1f, 0.1f, 0.55f));
        StartCoroutine(IrisCloseAndFreeze());
    }

    public void TriggerGameOver()
    {
        EnergyManager em = EnergyManager.Instance;
        bool bloodDeath = em != null && !em.IsBlueMode && em.SyringeBlood >= em.MaxSyringe - 0.001f;
        HandleDeath(isObstacle: !bloodDeath);
    }

    // =========================================================================
    // VIDA EXTRA
    // =========================================================================

    /// <summary>¿Hay una vida extra que gastar? Si la hay, la gasta y empieza a revivir.</summary>
    private bool TryRevive()
    {
        if (_reviving) return true;   // ya se está reviviendo: ignorar
        if (EnergyManager.Instance == null) return false;

        // «Adrenalina» (gratis, una vez por nivel) antes que gastar una vida extra: vuelves con un 30 %.
        if (!_lastStandUsed && PlayerProgress.GetLevel(ShopIds.LastStand) > 0)
        {
            _lastStandUsed = true;
            RunStats.HelpUsed();
            StartCoroutine(ReviveRoutine(0.3f, "¡ADRENALINA!", "Vuelves con un 30 %"));
            return true;
        }

        if (!PlayerProgress.TryUseConsumable(ShopIds.ExtraLife)) return false;
        RunStats.HelpUsed();
        StartCoroutine(ReviveRoutine());
        return true;
    }

    private bool _lastStandUsed, _parachuteUsed;

    /// <summary>«Paracaídas»: una vez por nivel, al caer al agua/lava sales despedido hacia arriba.</summary>
    private bool TryParachute()
    {
        if (_parachuteUsed || _playerMovement == null || PlayerProgress.GetLevel(ShopIds.Parachute) <= 0) return false;
        _parachuteUsed = true;
        RunStats.HelpUsed();
        _playerMovement.Bounce(24f);
        ReviveFX.ShowMessage("¡PARACAÍDAS!", "Solo una vez por nivel");
        if (_verbose) Debug.Log("[GameOver] ¡Paracaídas! Rebote en vez de caer.");
        return true;
    }

    /// <summary>
    /// 1) Aparece el aviso «¡VIDA EXTRA!» (la música nunca se para y sigues corriendo).
    /// 2) En el SIGUIENTE BEAT el corazón vuelve a latir: la jeringuilla se VACÍA del todo (100 % de energía).
    /// Sin inmunidad: los obstáculos siguen matando como siempre.
    /// </summary>
    private IEnumerator ReviveRoutine(float energy = 1f, string title = null, string sub = null)
    {
        _reviving = true;
        EnergyManager em = EnergyManager.Instance;

        int left = PlayerProgress.GetCount(ShopIds.ExtraLife);
        if (_verbose) Debug.Log($"[GameOver] {(title ?? "¡Vida extra!")} (vidas: {left}). Vuelves en el siguiente beat.");
        if (_reviveSound != null && _sfxSource != null) _sfxSource.PlayOneShot(_reviveSound);
        ReviveFX fx = title != null ? ReviveFX.ShowMessage(title, sub) : ReviveFX.Show(left);

        // Esperar al siguiente beat.
        BeatManager bm = BeatManager.Instance;
        double beat = bm != null && bm.BeatDuration > 0f ? bm.BeatDuration : 0.5;
        double next = AudioSettings.dspTime + beat;
        if (bm != null && bm.ScheduledStartDSP > 0.0)
        {
            double b = (AudioSettings.dspTime - bm.ScheduledStartDSP) / beat;
            next = bm.ScheduledStartDSP + (System.Math.Floor(b) + 1.0) * beat;
        }
        while (AudioSettings.dspTime < next) yield return null;

        // ¡Latido! Vida extra = jeringuilla vacía (100 %); Adrenalina = 30 %.
        if (!_isDead && em != null)
        {
            float target = em.MaxSyringe * (1f - Mathf.Clamp01(energy));
            if (em.SyringeBlood > target) em.Restore(em.SyringeBlood - target);
        }
        if (fx != null) fx.Beat();
        _reviving = false;
    }

    private bool _restarting;

    public void Restart()
    {
        if (_restarting) return;   // (doble clic)
        _restarting = true;
        CheckpointSystem.Clear();   // reinicio manual: desde el principio
        if (_verbose) Debug.Log("[GameOver] Restart.");
        StartCoroutine(RestartRoutine());
    }

    /// <summary>
    /// Reinicio limpio: silencio total, carga del nivel EN SEGUNDO PLANO (sin tirón) con el juego aún congelado, y
    /// solo al final se descongela y se cambia de escena. Antes se volvía a poner la canción del nivel aquí mismo y
    /// sonaba unos segundos durante la carga, antes de la cuenta del metrónomo: ya no.
    /// </summary>
    private IEnumerator RestartRoutine()
    {
        if (_gameOverMusicSource != null) _gameOverMusicSource.Stop();
        StopLevelMusic();
        if (_musicLowPassToReset != null)
            _musicLowPassToReset.cutoffFrequency = _resetLowPassCutoff;
        if (_gameOverPanelGroup != null) _gameOverPanelGroup.interactable = false;

        AsyncOperation op = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);
        if (op == null) yield break;
        op.allowSceneActivation = false;
        while (op.progress < 0.9f) yield return null;   // (funciona con timeScale = 0)

        Time.timeScale       = 1f;
        AudioListener.pause  = false;
        AudioListener.volume = 1f;
        op.allowSceneActivation = true;
    }

    public void Quit()
    {
        if (_gameOverMusicSource != null) _gameOverMusicSource.Stop();
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
