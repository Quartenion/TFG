using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Flujo de la muerte.
///
///   Mientras el jugador está vivo, _visionDimImage (shader IrisWipe, parcial) cierra la visión desde los bordes a
///   medida que baja la energía. Progress va de 0 a _maxVisionProgress.
///
///   Al morir por quedarse sin energía, _irisWipeImage se cierra por completo (de 0 a 1) en _energyFadeDuration.
///   Cuando Progress llega a 1, se congela el juego y se muestra el panel.
///
///   Al morir por un obstáculo (TriggerGameOver), suena el choque y el iris se cierra rápido (de 0 a 1) en
///   _crashFadeDuration. Cuando Progress llega a 1, se congela el juego y se muestra el panel.
/// </summary>
public class GameOverManager : MonoBehaviour
{
    // -=+=- Referencias -=+=-

    [Header("Referencias logicas")]
    [SerializeField] private LowHealthEffects _lowHealthEffects;

    [Header("Scripts del jugador (se desactivan al morir)")]
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private RhythmInput    _rhythmInput;

    // -=+=- Interfaz -=+=-

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
    [Tooltip("Con poca sangre la pantalla se va OSCURECIENDO entera (casi negra al final), en vez de un círculo que se cierra.\n" +
             "Así, al morir, ya está casi a oscuras y el parón no se nota.")]
    [SerializeField] private bool _visionFadeToBlack = true;
    [Tooltip("(Oscurecer) Exponente: más alto = se oscurece sobre todo al final (deja jugar con poca sangre).")]
    [Range(1f, 4f)]
    [SerializeField] private float _visionFadePower = 2.2f;
    [Tooltip("(Oscurecer) Color al que se va la pantalla (casi negro, con un toque de rojo sangre).")]
    [SerializeField] private Color _visionFadeColor = new Color(0.1f, 0f, 0f, 1f);

    [Header("UI - Cierre de iris al morir (IrisWipe completo)")]
    [Tooltip("Image con Material IrisWipe. Cierra TODO la pantalla al morir (0->1).")]
    [SerializeField] private Image _irisWipeImage;

    [Header("UI - Panel de Game Over")]
    [SerializeField] private GameObject  _gameOverPanel;
    [SerializeField] private CanvasGroup _gameOverPanelGroup;

    // -=+=- Fundido de la muerte -=+=-

    [Header("Cierre de iris - agotamiento de energia")]
    [SerializeField] private float          _energyFadeDuration = 1.0f;
    [SerializeField] private AnimationCurve _energyFadeCurve    = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Cierre de iris - obstaculo (crash)")]
    [SerializeField] private float          _crashFadeDuration = 0.3f;
    [SerializeField] private AnimationCurve _crashFadeCurve    = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("Al morir sin sangre, la pantalla se va OSCURECIENDO entera (en vez del círculo que se cierra).")]
    [SerializeField] private bool _energyFadeToBlack = true;
    [Tooltip("Al chocar, la pantalla se cierra como un PARPADEO (párpados difuminados) en vez de un círculo.")]
    [SerializeField] private bool _crashBlink = true;
    [SerializeField] private AudioClip   _crashSound;
    [SerializeField] private AudioSource _sfxSource;

    // -=+=- Audio -=+=-

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

    // -=+=- Panel -=+=-

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

    // -=+=- Estado -=+=-

    private bool     _isDead;
    private bool     _reviving;        // gastando una vida extra, hasta el siguiente beat
    private bool     _isObstacleDeath;
    private float    _baseAudioVolume          = 1f;

    private Material _visionMatInst;   // instancia del material del viñeteo
    private Material _irisMatInst;     // instancia del material del cierre de iris
    private float    _visionProgress;  // valor actual del progreso del viñeteo

    private int                 _waterMask = 0;    // máscara de la capa del agua
    private Transform           _playerTransform;  // transform del jugador, para el raycast
    private CharacterController _playerCC;          // CharacterController del jugador, para saber dónde están los pies

    public bool IsDead => _isDead;

    /// <summary>Indica si la última muerte fue por un obstáculo o una caída (true) o por quedarse sin sangre (false).</summary>
    public bool IsObstacleDeath => _isObstacleDeath;

    // -=+=- Inicio -=+=-

    private void Start()
    {
        // VisionDim: se instancia el material y se asegura que ocupe toda la pantalla.
        if (_visionDimImage != null)
        {
            _visionMatInst = new Material(_visionDimImage.material);
            _visionDimImage.material = _visionMatInst;
            _visionMatInst.SetFloat("_Progress", 0f);
            _visionMatInst.SetFloat("_Mode", _visionFadeToBlack ? 2f : 0f);   // 2 es el fundido a negro
            if (_visionFadeToBlack) _visionMatInst.SetColor("_Color", _visionFadeColor);
            _visionProgress = 0f;
            ForceStretch(_visionDimImage.gameObject);
        }

        // IrisWipe (muerte): se instancia el material y se asegura que ocupe toda la pantalla.
        if (_irisWipeImage != null)
        {
            _irisMatInst = new Material(_irisWipeImage.material);
            _irisWipeImage.material = _irisMatInst;
            _irisMatInst.SetFloat("_Progress", 0f);
            ForceStretch(_irisWipeImage.gameObject);
        }

        // El panel empieza desactivado e invisible.
        InitGroup(_gameOverPanelGroup, alpha: 0f);
        if (_gameOverPanel != null)
        {
            _gameOverPanel.SetActive(false);
            ForceStretch(_gameOverPanel);
        }

        _baseAudioVolume = AudioListener.volume;

        // Muerte por agua: se guardan la capa del agua y las referencias del jugador.
        int w = LayerMask.NameToLayer(_waterLayerName);
        _waterMask = w == -1 ? 0 : (1 << w);
        if (w == -1)
            Debug.LogError($"[GameOver] El layer \"{_waterLayerName}\" no existe. Créalo en Project Settings > Tags and Layers.", this);

        // Si el jugador tiene vidas extra, se muestran en el HUD.
        if (PlayerProgress.GetCount(ShopIds.ExtraLife) > 0 || PlayerProgress.GetCount(ShopIds.Shield) > 0 ||
            PlayerProgress.GetCount(ShopIds.Checkpoint) > 0) CoinHUD.Ensure();

        if (_playerMovement == null) _playerMovement = FindAnyObjectByType<PlayerMovement>();
        if (_playerMovement != null)
        {
            _playerTransform = _playerMovement.transform;
            _playerCC        = _playerMovement.GetComponent<CharacterController>();
        }
    }

    // -=+=- Update, con el viñeteo parcial mientras el jugador está vivo -=+=-

    private void Update()
    {
        if (_isDead) return;
        if (LevelEndManager.Finished) return;   // nivel terminado: la música sigue, pero ya no se puede morir

        // La muerte por caer al agua (el río de sangre) se comprueba siempre, quede energía o no.
        if (CheckWaterDeath())
        {
            if (TryParachute()) return;   // "Paracaídas": una vez por nivel, el jugador rebota
            SetReason(InLava ? Loc.T("Has caído a la lava", "You fell into the lava") : Loc.T("Has caído al río de sangre", "You fell into the river of blood"));
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

            targetProgress = _visionFadeToBlack
                ? Mathf.Pow(Mathf.Clamp01(depth), _visionFadePower) * _maxVisionProgress          // casi negra al final
                : _visionDimCurve.Evaluate(Mathf.Clamp01(depth)) * _maxVisionProgress;
        }

        // Se acerca suavemente al objetivo (el iris del viñeteo solo va de 0 a _maxVisionProgress).
        _visionProgress = Mathf.Lerp(_visionProgress, targetProgress, Time.deltaTime * _visionSmoothing);
        if (_visionMatInst != null)
            _visionMatInst.SetFloat("_Progress", _visionProgress);

        // El _irisWipeImage de la muerte no se toca aquí, ya que se queda en 0 hasta que el jugador muere.

        if (!isBlue && energy <= 0f)
        {
            SetReason(BloodReason());
            HandleDeath(isObstacle: false);
        }
    }

    // -=+=- Lógica de la muerte -=+=-

    /// <summary>
    /// Indica si el jugador ha caído al agua. Busca hacia abajo la superficie del agua (su cara superior, la misma sobre
    /// la que flota el kayak) y comprueba si los pies del jugador han llegado a esa altura. Funciona tanto si el jugador
    /// se queda encima del agua como si la atraviesa, y no se aplica en el kayak ni en la tirolina.
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

        // Si el jugador cae o aterriza encima de un kayak, que flota a ras del agua, sus pies quedan al nivel del agua
        // aunque esté sobre él, por lo que, mientras no se hunda de verdad, se deja que se suba al kayak.
        if (feetY > waterY - _kayakLandingTolerance && KayakController.IsKayakUnder(_playerTransform.position))
            return false;

        // Salpicadura opcional en la superficie.
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
        if (DebugInvulnerable) return;   // medición de relojes: la canción no debe pararse
        if (LevelEndManager.Finished) return;   // ya está en la meta

        // Si muere por quedarse sin sangre y tiene una vida extra, no hay game over y vuelve en el siguiente beat.
        if (!isObstacle && TryRevive()) return;
        _isDead          = true;
        _isObstacleDeath = isObstacle;
        RunStats.Death();   // intentos: la pantalla final los cuenta y bajan la puntuación
        StudyTelemetry.EndAttempt(isObstacle ? "muerte_obstaculo" : "muerte_energia");   // modo prueba

        if (_verbose)
            Debug.Log($"[GameOver] Muerte por {(isObstacle ? "obstaculo" : "energia")}.");

        if (_playerMovement   != null) _playerMovement.enabled   = false;
        if (_rhythmInput      != null) _rhythmInput.enabled      = false;
        if (_lowHealthEffects != null) _lowHealthEffects.enabled = false;

        // En el nivel tutorial y el estudio, que tienen puntos de control gratis, se muestra la pantalla de game over normal,
        // pero con el botón "Volver al último checkpoint" en lugar de "Reintentar" (ver ShowGameOverPanel y Restart).
        if (CheckpointSystem.FreeRespawn && CheckpointSystem.HasActive)
        {
            StartCoroutine(IrisCloseAndFreeze());
            return;
        }

        // Si tiene un punto de control (consumible de la tienda), se gasta uno y el nivel se recarga desde el último que pasó.
        if (CheckpointSystem.TryRespawn())
        {
            if (!CheckpointSystem.FreeRespawn) RunStats.HelpUsed();   // los puntos de control gratis del nivel tutorial no cuentan como ayuda
            StartCoroutine(CheckpointRespawn());
            return;
        }

        StartCoroutine(IrisCloseAndFreeze());
    }

    /// <summary>Cierre rápido del iris y recarga del nivel. StartSequenceManager coloca al jugador en el punto de control.</summary>
    private IEnumerator CheckpointRespawn()
    {
        // La música del nivel se corta en seco, ya que, si siguiera sonando durante el cierre del iris, sonaría con fallos.
        StopLevelMusic();
        int left = PlayerProgress.GetCount(ShopIds.Checkpoint);
        if (_verbose) Debug.Log($"[GameOver] Checkpoint: volviendo al último (quedan {left}).");
        string sub = CheckpointSystem.FreeRespawn ? Loc.T("Vuelves al último punto de control", "Back to the last checkpoint")   // en el nivel tutorial son infinitos
                   : left > 0 ? Loc.T($"Te quedan {left}", $"{left} left") : Loc.T("Era el último", "That was the last one");
        ReviveFX.ShowMessage("CHECKPOINT", sub, new Color(0.25f, 0.7f, 0.35f, 0.55f));

        // La escena se empieza a cargar en segundo plano mientras se cierra el iris, para que el juego no se congele al cargarla.
        AsyncOperation op = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);
        if (op != null) op.allowSceneActivation = false;

        if (_irisMatInst != null) _irisMatInst.SetFloat("_Mode", _crashBlink ? 1f : 0f);   // parpadeo
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

    /// <summary>Corta la música del nivel y el resto de pistas largas. La del game over llega después.</summary>
    private void StopLevelMusic()
    {
        BeatManager bm = BeatManager.Instance;
        if (bm != null) bm.StopMusic();
        if (_musicSourceToRestart != null) _musicSourceToRestart.Stop();
        // Se para cualquier otra pista de música que siga sonando (intro, bucle del jefe, final...).
        foreach (AudioSource src in FindObjectsByType<AudioSource>())
            if (src != null && src != _gameOverMusicSource && src.isPlaying && src.clip != null && src.clip.length > 15f)
                src.Stop();
    }

    /// <summary>Empieza la música del game over, subiendo de 0 a su volumen en _gameOverMusicFadeIn segundos.</summary>
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
        // Game over. El jugador se queda las monedas que había cogido, que pasan al monedero, y al reiniciar vuelven a estar todas en el nivel.
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

        // Al chocar, la pantalla parpadea (párpados difuminados, como al cerrar los ojos). Al quedarse sin sangre, se va oscureciendo.
        if (_irisMatInst != null)
            _irisMatInst.SetFloat("_Mode", _isObstacleDeath ? (_crashBlink ? 1f : 0f) : (_energyFadeToBlack ? 2f : 0f));

        // El iris de la muerte empieza siempre en 0, y el viñeteo sigue visible debajo durante el cierre.
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = curve.Evaluate(Mathf.Clamp01(elapsed / duration));

            if (_irisMatInst != null) _irisMatInst.SetFloat("_Progress", t);

            // Con música de game over no se baja el volumen general, ya que también se apagaría ella.
            if (_attenuateAudio && !hasGoMusic)
                AudioListener.volume = Mathf.Lerp(_baseAudioVolume, _finalAudioVolume, t);

            yield return null;
        }

        // Se cierra por completo.
        if (_irisMatInst != null) _irisMatInst.SetFloat("_Progress", 1f);
        if (_attenuateAudio && !hasGoMusic) AudioListener.volume = _finalAudioVolume;

        // Se congela el juego.
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

        HudHider.Hide(_gameOverPanel);   // se ocultan el punto de mira, la barra, las monedas, los avisos...
        if (_gameOverPanel != null) _gameOverPanel.SetActive(true);
        ShowReason();
        SetRestartLabel(CheckpointSystem.FreeRespawn && CheckpointSystem.HasActive);
        ShowSkipButton(SkipAvailable != null && SkipAvailable());

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

        // Con los botones activos, empieza la música del game over (de 0 a su volumen).
        StartCoroutine(PlayGameOverMusic());

        if (_verbose) Debug.Log("[GameOver] Panel visible.");
    }

    // -=+=- API pública -=+=-

    /// <summary>Solo para mediciones y pruebas. Mientras está activo, nada mata al jugador.</summary>
    public static bool DebugInvulnerable { get; set; }

    /// <summary>
    /// Golpe letal de un obstáculo o de un ataque. Si el jugador tiene escudo, lo para (el obstáculo se rompe) y devuelve
    /// false. Si no, hay game over (o se vuelve al punto de control) y devuelve true.
    /// </summary>
    public bool TryKillByObstacle(GameObject source)
    {
        if (_isDead) return true;
        if (DebugInvulnerable) return false;
        if (LevelEndManager.Finished) return false;
        if (TimeRewinder.IsRewinding) return false;   // mientras se vuelve atrás en el tiempo, nada mata

        // Rebobinado (tienda): al chocar contra un obstáculo, el tiempo vuelve 4 beats atrás, a un momento seguro.
        if (source != null && PlayerShield.Count > 0 && TimeRewinder.Instance != null && TimeRewinder.Instance.TryRewind())
            return false;

        // Si el golpe no viene de un obstáculo físico (por ejemplo, un ataque del jefe) o no hay un sitio seguro al que volver, lo para el escudo.
        if (source == null && PlayerShield.TryAbsorb(null)) return false;

        SetReason(source != null ? Loc.T("Te has chocado con un obstáculo", "You crashed into an obstacle")
                                 : Loc.T("Has recibido un golpe mortal", "You took a deadly hit"));
        HandleDeath(isObstacle: true);
        return true;
    }

    /// <summary>
    /// Game over directo, por ejemplo al incumplir el contrato "Contrarreloj", sin vidas extra, Adrenalina ni punto de control.
    /// </summary>
    public void ForceGameOver(string title = null, string subtitle = null)
    {
        if (_isDead) return;
        if (DebugInvulnerable) return;
        if (LevelEndManager.Finished) return;
        _isDead          = true;
        _isObstacleDeath = true;
        SetReason(!string.IsNullOrEmpty(subtitle) ? subtitle : title);
        StudyTelemetry.EndAttempt("game_over_forzado");   // modo prueba
        if (_verbose) Debug.Log($"[GameOver] Game over forzado: {title} {subtitle}");

        if (_playerMovement   != null) _playerMovement.enabled   = false;
        if (_rhythmInput      != null) _rhythmInput.enabled      = false;
        if (_lowHealthEffects != null) _lowHealthEffects.enabled = false;

        if (!string.IsNullOrEmpty(title))
            ReviveFX.ShowMessage(title, subtitle, new Color(0.85f, 0.1f, 0.1f, 0.55f));
        StartCoroutine(IrisCloseAndFreeze());
    }

    public void TriggerGameOver() => TriggerGameOver(null);

    /// <param name="reason">Razón que aparece en la pantalla de game over (null si se deduce).</param>
    /// <summary>
    /// Game over. Si llega con la jeringuilla llena (por ejemplo, desde EnergyManager.OnSyringeFull), cuenta como muerte
    /// por sangre, que se puede salvar con una vida extra. Si no, es un choque o una caída, que no se salva con vida extra.
    /// </summary>
    public void TriggerGameOver(string reason)
    {
        if (_isDead) return;
        EnergyManager em = EnergyManager.Instance;
        bool bloodDeath = em != null && !em.IsBlueMode && em.SyringeBlood >= em.MaxSyringe - 0.001f;
        SetReason(bloodDeath ? BloodReason() : (reason ?? Loc.T("Has caído", "You fell")));
        HandleDeath(isObstacle: !bloodDeath);
    }

    // -=+=- Razón de la muerte (texto de la pantalla de game over) -=+=-

    /// <summary>Por qué ha muerto el jugador. Se muestra debajo de "GAME OVER".</summary>
    public string DeathReason { get; private set; }

    /// <summary>Lo llama el jefe al golpear, para que, si el jugador se queda sin sangre justo después, la razón sea el jefe.</summary>
    public static float LastBossHitTime = -999f;

    private void SetReason(string r) { if (!string.IsNullOrEmpty(r)) DeathReason = r; }

    private static bool InLava => SceneManager.GetActiveScene().name == "Level3";

    private static string BloodReason()
    {
        if (Time.time - LastBossHitTime < 2.5f) return Loc.T("El jefe te ha dejado sin sangre", "The boss drained your blood");
        foreach (BloodSucker b in FindObjectsByType<BloodSucker>())
            if (b != null && b.IsSucking) return Loc.T("Un chupasangre te ha dejado seco", "A bloodsucker drained you dry");
        return Loc.T("Te has quedado sin sangre", "You ran out of blood");
    }

    private TMPro.TextMeshProUGUI _reasonText;

    // -=+=- "Saltar sección" (nivel tutorial y estudio, después de morir 3 veces en la misma sección) -=+=-
    /// <summary>Indica si se puede saltar a la siguiente sección. Lo activa CourseBuilder.</summary>
    public static System.Func<bool> SkipAvailable;
    /// <summary>Deja activo el siguiente punto de control (lo hace CourseBuilder), en el que se reaparece después.</summary>
    public static System.Action SkipSection;
    private Button _skipButton;

    /// <summary>
    /// Botón "Saltar sección" (una copia del de reintentar, entre él y el de salir), que lleva al siguiente punto de
    /// control. Sirve para que nadie abandone la prueba por quedarse atascado en un obstáculo.
    /// </summary>
    private void ShowSkipButton(bool show)
    {
        if (_gameOverPanel == null || !show || _skipButton != null) return;
        Button restart = null, quit = null;
        foreach (Button b in _gameOverPanel.GetComponentsInChildren<Button>(true))
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            {
                if (b.onClick.GetPersistentMethodName(i) == nameof(Restart)) restart = b;
                if (b.onClick.GetPersistentMethodName(i) == nameof(Quit)) quit = b;
            }
        if (restart == null) return;
        var rt0 = (RectTransform)restart.transform;
        float step = rt0.sizeDelta.y + 30f;
        _skipButton = Instantiate(restart, restart.transform.parent);
        _skipButton.name = "SkipSectionButton";
        _skipButton.onClick = new Button.ButtonClickedEvent();   // sin la llamada a Restart copiada
        _skipButton.onClick.AddListener(SkipSectionPressed);
        var rt = (RectTransform)_skipButton.transform;
        rt.anchoredPosition = rt0.anchoredPosition - new Vector2(0f, step);
        if (quit != null)
        {
            var qt = (RectTransform)quit.transform;
            if (qt.anchoredPosition.y > rt.anchoredPosition.y - step) qt.anchoredPosition = rt.anchoredPosition - new Vector2(0f, step);
        }
        const string es = "SALTAR SECCIÓN", en = "SKIP SECTION";
        var tmp = _skipButton.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (tmp != null)
        {
            var lt = tmp.GetComponent<LocalizedText>();
            if (lt != null) { lt.spanish = es; lt.english = en; lt.Apply(); }
            else tmp.text = Loc.T(es, en);
        }
        else
        {
            var legacy = _skipButton.GetComponentInChildren<Text>(true);
            if (legacy != null) legacy.text = Loc.T(es, en);
        }
    }

    private void SkipSectionPressed()
    {
        if (_restarting) return;
        _restarting = true;
        SkipSection?.Invoke();                 // el siguiente punto de control pasa a ser el activo
        CheckpointSystem.TryRespawn();         // y se reaparece en él
        if (_verbose) Debug.Log("[GameOver] Saltar sección.");
        StartCoroutine(RestartRoutine());
    }

    /// <summary>
    /// Texto del botón que llama a Restart, que es "Volver al último checkpoint" en el nivel tutorial y el estudio, y
    /// el de siempre en el resto. El botón se busca en el panel por su llamada a Restart.
    /// </summary>
    private void SetRestartLabel(bool checkpoint)
    {
        if (_gameOverPanel == null || !checkpoint) return;
        foreach (Button b in _gameOverPanel.GetComponentsInChildren<Button>(true))
        {
            bool isRestart = false;
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                if (b.onClick.GetPersistentMethodName(i) == nameof(Restart)) isRestart = true;
            if (!isRestart) continue;
            const string es = "VOLVER AL ÚLTIMO CHECKPOINT", en = "BACK TO THE LAST CHECKPOINT";
            var tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null)
            {
                var lt = tmp.GetComponent<LocalizedText>();
                if (lt != null) { lt.spanish = es; lt.english = en; lt.Apply(); }
                else tmp.text = Loc.T(es, en);
                tmp.enableAutoSizing = true;          // el texto es más largo, así que se ajusta para que quepa en el botón
                tmp.fontSizeMin = 8f;
                tmp.fontSizeMax = Mathf.Max(tmp.fontSize, 12f);
            }
            else
            {
                var legacy = b.GetComponentInChildren<Text>(true);
                if (legacy != null) { legacy.text = Loc.T(es, en); legacy.resizeTextForBestFit = true; }
            }
        }
    }

    /// <summary>Texto con la razón, debajo del título del game over y con la fuente del juego.</summary>
    private void ShowReason()
    {
        if (_gameOverPanel == null) return;
        if (_reasonText == null)
        {
            var go = new GameObject("DeathReason", typeof(RectTransform));
            go.layer = _gameOverPanel.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(_gameOverPanel.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 135f);   // entre el título y el botón de reintentar
            rt.sizeDelta = new Vector2(1400f, 80f);
            _reasonText = go.AddComponent<TMPro.TextMeshProUGUI>();
            UIFonts.Apply(_reasonText, false);
            _reasonText.alignment = TMPro.TextAlignmentOptions.Center;
            _reasonText.enableAutoSizing = true;
            _reasonText.fontSizeMin = 24f; _reasonText.fontSizeMax = 64f;
            _reasonText.color = new Color(1f, 0.82f, 0.78f);
            _reasonText.raycastTarget = false;
            // Se dibuja por encima del fondo de fuego.
            rt.SetAsLastSibling();
        }
        _reasonText.text = string.IsNullOrEmpty(DeathReason) ? "" : UIFonts.LettersOnly(DeathReason);
    }

    // -=+=- Vida extra -=+=-

    /// <summary>Comprueba si hay una vida extra que gastar y, si la hay, la gasta y empieza a revivir al jugador.</summary>
    private bool TryRevive()
    {
        if (_reviving) return true;   // ya se está reviviendo, así que se ignora
        if (EnergyManager.Instance == null) return false;

        // Antes de gastar una vida extra se usa la "Adrenalina" (gratis, una vez por nivel), con la que el jugador vuelve con un 30 %.
        if (!_lastStandUsed && PlayerProgress.GetLevel(ShopIds.LastStand) > 0)
        {
            _lastStandUsed = true;
            RunStats.HelpUsed();
            StartCoroutine(ReviveRoutine(0.3f, Loc.T("¡ADRENALINA!", "ADRENALINE!"), Loc.T("Vuelves con un 30 %", "You come back with 30 %")));
            return true;
        }

        if (!PlayerProgress.TryUseConsumable(ShopIds.ExtraLife)) return false;
        RunStats.HelpUsed();
        StartCoroutine(ReviveRoutine());
        return true;
    }

    private bool _lastStandUsed, _parachuteUsed;

    /// <summary>"Paracaídas". Una vez por nivel, al caer al agua o a la lava, el jugador sale despedido hacia arriba.</summary>
    private bool TryParachute()
    {
        if (_parachuteUsed || _playerMovement == null || PlayerProgress.GetLevel(ShopIds.Parachute) <= 0) return false;
        _parachuteUsed = true;
        RunStats.HelpUsed();
        _playerMovement.Bounce(24f);
        ReviveFX.ShowMessage(Loc.T("¡PARACAÍDAS!", "PARACHUTE!"), Loc.T("Solo una vez por nivel", "Only once per level"));
        if (_verbose) Debug.Log("[GameOver] ¡Paracaídas! Rebote en vez de caer.");
        return true;
    }

    /// <summary>
    /// Primero aparece el aviso "¡VIDA EXTRA!", mientras la música sigue y el jugador sigue corriendo. En el siguiente
    /// beat, el corazón vuelve a latir y la jeringuilla se vacía del todo (100 % de energía). No hay inmunidad, así que
    /// los obstáculos siguen matando como siempre.
    /// </summary>
    private IEnumerator ReviveRoutine(float energy = 1f, string title = null, string sub = null)
    {
        _reviving = true;
        EnergyManager em = EnergyManager.Instance;

        int left = PlayerProgress.GetCount(ShopIds.ExtraLife);
        if (_verbose) Debug.Log($"[GameOver] {(title ?? "¡Vida extra!")} (vidas: {left}). Vuelves en el siguiente beat.");
        if (_reviveSound != null && _sfxSource != null) _sfxSource.PlayOneShot(_reviveSound);
        ReviveFX fx = title != null ? ReviveFX.ShowMessage(title, sub) : ReviveFX.Show(left);

        // Se espera al siguiente beat.
        BeatManager bm = BeatManager.Instance;
        double beat = bm != null && bm.BeatDuration > 0f ? bm.BeatDuration : 0.5;
        double next = AudioSettings.dspTime + beat;
        if (bm != null && bm.HasClock)
        {
            double b = bm.ClockSeconds / beat;
            next = bm.DspAtBeat(System.Math.Floor(b) + 1.0);
        }
        while (AudioSettings.dspTime < next) yield return null;

        // Latido. Con la vida extra la jeringuilla se vacía (100 %), y con la Adrenalina se queda en el 30 %.
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
        if (_restarting) return;   // doble clic
        // En el nivel tutorial y el estudio, el botón es "Volver al último checkpoint", así que se recarga desde él, sin límite.
        if (CheckpointSystem.FreeRespawn && CheckpointSystem.TryRespawn())
        {
            _restarting = true;
            if (_verbose) Debug.Log("[GameOver] Volver al último checkpoint.");
            StartCoroutine(RestartRoutine());
            return;
        }
        if (StudySession.OnRestartRequested()) return;   // modo prueba: sin intentos, se pasa a la siguiente parte
        _restarting = true;
        CheckpointSystem.Clear();   // reinicio manual, desde el principio
        if (_verbose) Debug.Log("[GameOver] Restart.");
        StartCoroutine(RestartRoutine());
    }

    /// <summary>
    /// Reinicio limpio. Se silencia todo, el nivel se carga en segundo plano con el juego todavía congelado y, solo al
    /// final, se descongela y se cambia de escena.
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
        while (op.progress < 0.9f) yield return null;   // funciona con timeScale = 0

        Time.timeScale       = 1f;
        AudioListener.pause  = false;
        AudioListener.volume = GameSettings.Volume;   // el volumen elegido en los ajustes
        op.allowSceneActivation = true;
    }

    public void Quit()
    {
        // Modo prueba: salir a mitad descalifica, con el mismo aviso que al volver al menú desde la pausa.
        if (StudySession.InSession) { AbandonDialog.Show(Loc.T("Volver", "Back")); return; }   // también en el tutorial y en la sala
        // En la práctica libre del modo prueba, el botón de salir vuelve al menú, para elegir qué hacer, en lugar de cerrar el juego.
        if (StudySession.InFreePlay)
        {
            Time.timeScale = 1f; AudioListener.pause = false; AudioListener.volume = GameSettings.Volume;
            UnityEngine.SceneManagement.SceneManager.LoadScene(StudySession.MenuScene);
            return;
        }
        if (_gameOverMusicSource != null) _gameOverMusicSource.Stop();
        if (_verbose) Debug.Log("[GameOver] Quit.");
        Time.timeScale       = 1f;
        AudioListener.pause  = false;
        AudioListener.volume = GameSettings.Volume;   // el volumen elegido en los ajustes
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // -=+=- Funciones auxiliares -=+=-

    private static void InitGroup(CanvasGroup group, float alpha)
    {
        if (group == null) return;
        group.alpha          = alpha;
        group.interactable   = false;
        group.blocksRaycasts = false;
    }

    /// <summary>
    /// Hace que el RectTransform cubra toda la pantalla, quitando cualquier offset o anclaje incorrecto, para que no
    /// quede un hueco en el borde.
    /// </summary>
    private static void ForceStretch(GameObject go)
    {
        if (go == null) return;
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.offsetMin        = Vector2.zero;   // izquierda y abajo = 0
        rt.offsetMax        = Vector2.zero;   // derecha y arriba = 0
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero;
    }
}
