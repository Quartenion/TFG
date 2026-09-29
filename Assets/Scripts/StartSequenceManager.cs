using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gestiona la secuencia de inicio del nivel.
///
/// FLUJO:
///   Primera vez   → Intro → pantalla con 3 círculos + metrónomo → canción + correr.
///   Restart/muerte → salta la intro directamente a los 3 círculos.
///
/// METRÓNOMO:
///   Solo necesitas un AudioSource con el clip largo ya asignado.
///   El script lo arranca con PlayScheduled (loop ON) para precisión DSP.
///   No hay "Metronome Clip" separado porque el clip ya está en el AudioSource.
///
/// ACIERTO / FALLO:
///   · Acierto → el círculo pulsa en verde.
///   · Fallo   → TODOS los círculos correctos anteriores + el actual sacuden
///               y vuelven a gris. Se reinicia a 0.
///
/// SETUP:
///   · _introSource   → AudioSource con el clip de la intro asignado (loop OFF).
///   · _metroSource   → AudioSource con el clip del metrónomo asignado (loop ON).
///                       El script llama a PlayScheduled; no toques Play Manual.
///   · _sfxSource     → AudioSource para efectos de acierto/error (puede ser el mismo
///                       que _metroSource usando PlayOneShot sin interrumpir el loop).
///   · Circle Roots   → Circle1, Circle2, Circle3. Image y TMP_Text se buscan solos.
/// </summary>
public class StartSequenceManager : MonoBehaviour
{
    // ── Audio ─────────────────────────────────────────────────────────────

    [Header("Audio — Intro (se omite en restart)")]
    [Tooltip("AudioSource con el clip de la intro ya asignado en su campo 'AudioClip'. Loop OFF.")]
    [SerializeField] private AudioSource _introSource;

    [Header("Audio — Metrónomo")]
    [Tooltip("AudioSource con el clip del metrónomo ya asignado en su campo 'AudioClip'.\n" +
             "El clip es el track largo (1 min) que tienes. El script activa Loop y usa PlayScheduled.")]
    [SerializeField] private AudioSource _metroSource;

    [Header("Audio — Efectos de UI")]
    [Tooltip("AudioSource para los efectos de acierto/error (puede ser el mismo que el metrónomo).")]
    [SerializeField] private AudioSource _sfxSource;
    [SerializeField] private AudioClip   _hitOkSound;
    [SerializeField] private AudioClip   _hitErrorSound;

    // ── Juego ─────────────────────────────────────────────────────────────

    [Header("Referencias de juego")]
    [SerializeField] private BeatManager    _beatManager;
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private RhythmInput    _rhythmInput;
    [SerializeField] private EnergyManager  _energyManager;

    // ── UI ────────────────────────────────────────────────────────────────

    [Header("UI — Overlay gris")]
    [SerializeField] private CanvasGroup _overlay;

    [Header("UI — Círculos (arrastrar Circle1, Circle2, Circle3, CircleYA)")]
    [Tooltip("4 GameObjects: los 3 numéricos + el círculo ¡YA!. Image y TMP_Text se buscan dentro.")]
    [SerializeField] private GameObject[] _circleRoots = new GameObject[4];

    // ── Beat ──────────────────────────────────────────────────────────────

    [Header("Beat")]
    [SerializeField] private float _bpm       = 180f;
    [SerializeField] private float _hitWindow = 0.12f;

    // ── Animaciones ───────────────────────────────────────────────────────

    [Header("Animación — Pulso (acierto)")]
    [SerializeField] private float _pulseScale    = 1.35f;
    [SerializeField] private float _pulseDuration = 0.18f;

    [Header("Animación — Sacudida (error)")]
    [SerializeField] private float _shakeRange    = 22f;
    [SerializeField] private float _shakeDuration = 0.38f;
    [SerializeField] private int   _shakeWaves    = 5;

    [Header("Animación — Pulso idle (metrónomo)")]
    [Tooltip("Escala máxima del pulso visual al ritmo del metrónomo (mucho más sutil que el acierto).")]
    [SerializeField] private float _idlePulseScale    = 1.08f;
    [SerializeField] private float _idlePulseDuration = 0.15f;

    [Header("Fade del overlay al salir")]
    [SerializeField] private float _fadeDuration = 0.4f;

    // ── Colores ───────────────────────────────────────────────────────────

    [Header("Colores")]
    [SerializeField] private Color _colorIdle = new Color(0.85f, 0.85f, 0.85f, 1f);
    [SerializeField] private Color _colorGood = new Color(0.25f, 0.90f, 0.35f, 1f);
    [SerializeField] private Color _colorBad  = new Color(0.90f, 0.18f, 0.18f, 1f);
    [Tooltip("Color del círculo ¡YA! en estado idle (para distinguirlo de los numéricos).")]
    [SerializeField] private Color _colorYaIdle = new Color(1f, 0.85f, 0.2f, 1f);

    [Header("Freeroam / Interacción")]
    [Tooltip("Si está activo, la secuencia NO arranca automáticamente al cargar la escena.\n" +
             "Espera a que algún StartWall llame a TriggerSequence().\n" +
             "El jugador puede moverse libremente hasta entonces.")]
    [SerializeField] private bool _waitForInteraction = false;

    [Tooltip("Root del HUD de energía (el GameObject padre de EnergyBarUI).\n" +
             "Se oculta durante el freeroam y vuelve a aparecer cuando empieza el juego.")]
    [SerializeField] private GameObject _energyBarRoot;

    [Header("Debug / Pruebas")]
    [Tooltip("(Solo para probar) Salta la introducción de audio y va directo a los círculos del ritmo.\n" +
             "Normalmente déjalo DESACTIVADO: la intro ya suena solo la primera vez que entras en cada nivel\n" +
             "desde que se abre el juego.")]
    [SerializeField] private bool _debugSkipIntro = false;

    [Tooltip("Modo test: salta toda la secuencia de inicio, teleporta al jugador a _testStartZ\n" +
             "y arranca la canción en el momento que corresponde a esa posición.")]
    [SerializeField] private bool  _testMode   = false;

    [Tooltip("Posición Z (metros desde el origen) desde la que empieza el test.\n" +
             "El beat equivalente es: _testStartZ / walkSpeed / BeatDuration.\n" +
             "Ejemplo: 800 m a walkSpeed=6 y 180 BPM → beat ≈ 200 (4 m/beat).")]
    [SerializeField] private float _testStartZ = 800f;

    [Tooltip("Si está activado, el test también fija la posición X e Y del jugador (no solo Z).\n" +
             "Útil cuando el jugador aparece a una altura o carril incorrectos en ese punto del nivel.")]
    [SerializeField] private bool  _testOverrideXY = false;

    [Tooltip("Posición X (carril) del test. Solo se aplica si 'Test Override XY' está activo.")]
    [SerializeField] private float _testStartX = 0f;

    [Tooltip("Posición Y (altura) del test. Solo se aplica si 'Test Override XY' está activo.")]
    [SerializeField] private float _testStartY = 1f;

    // ── Estado (static: sobrevive al reload de escena) ────────────────────

    /// <summary>Escenas en las que la intro ya se ha reproducido al menos una vez.</summary>
    /// La intro suena UNA vez por nivel cada vez que se abre el juego: la lista se vacía al arrancar (también al
    /// darle a Play en el editor, aunque esté desactivado el "domain reload").
    private static readonly System.Collections.Generic.HashSet<string> s_introPlayedScenes
        = new System.Collections.Generic.HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetIntroMemory() => s_introPlayedScenes.Clear();

    // ── Estado de instancia ───────────────────────────────────────────────

    private float _testSongOffset = 0f;  // offset de canción calculado en modo test

    private const int TOTAL_CIRCLES = 4; // 1, 2, 3, ¡YA!

    private bool     _challengeActive;
    private bool     _launching;
    private int      _hitsOk;
    private double   _beatInterval;
    private double   _lastBeatDSP;
    private double   _nextBeatDSP;
    private Image[]      _imgs          = new Image[TOTAL_CIRCLES];
    private TMP_Text[]   _texts         = new TMP_Text[TOTAL_CIRCLES];
    private Coroutine[]  _activeAnims      = new Coroutine[TOTAL_CIRCLES];
    private Coroutine[]  _idleAnims        = new Coroutine[TOTAL_CIRCLES]; // pulso metrónomo
    private Vector2[]    _originPos        = new Vector2[TOTAL_CIRCLES];
    private double       _autoFailTime     = double.MaxValue;  // DSP tras el que se auto-falla
    private bool         _hitSinceLastBeat;                    // acertó en este beat

    // =========================================================================
    // INICIO
    // =========================================================================

    private void Start()
    {
        Time.timeScale       = 1f;
        AudioListener.pause  = false;
        AudioListener.volume = 1f;

        if (_rhythmInput   != null) _rhythmInput.enabled   = false;
        if (_energyManager != null) _energyManager.enabled = false;

        // Cachear Image, TMP_Text y posición en reposo de cada círculo
        for (int i = 0; i < TOTAL_CIRCLES; i++)
        {
            if (i < _circleRoots.Length && _circleRoots[i] != null)
            {
                _imgs[i]  = _circleRoots[i].GetComponent<Image>();
                _texts[i] = _circleRoots[i].GetComponentInChildren<TMP_Text>();
                var rt = _circleRoots[i].GetComponent<RectTransform>();
                if (rt != null) _originPos[i] = rt.anchoredPosition;
            }
        }

        SetOverlay(false);
        HideCircles();

        PreloadClip(_introSource);
        PreloadClip(_metroSource);
        if (_beatManager != null) _beatManager.PreloadAudio();

        // Reaparición en un CHECKPOINT (tienda): sin freeroam ni puerta; directos al 3-2-1 desde ese punto.
        if (CheckpointSystem.PendingRespawn)
        {
            StartCoroutine(CheckpointSequence());
            return;
        }

        if (_waitForInteraction)
        {
            // Freeroam: cursor bloqueado para poder moverse en primera persona
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            // Activar movimiento libre: el jugador puede moverse hasta que pulse E
            if (_playerMovement != null)
                _playerMovement.EnableFreeroam();
            // Ocultar barra de energía durante freeroam
            if (_energyBarRoot != null)
                _energyBarRoot.SetActive(false);
            // La secuencia la lanza StartWall al pulsar E
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            StartCoroutine(Sequence());
        }
    }

    /// <summary>
    /// Lanza la secuencia de inicio manualmente (llamado por StartWall cuando
    /// el jugador pulsa E). Solo funciona si _waitForInteraction está activo.
    /// </summary>
    public void TriggerSequence()
    {
        if (_challengeActive || _launching) return;
        // En modo freeroam el cursor ya está bloqueado; no lo cambiamos aquí porque
        // PauseMenuManager interpreta el paso Locked→None como si el jugador pulsara ESC.
        // El cursor se mantiene bloqueado durante toda la intro y se gestiona en LaunchGame.
        if (!_waitForInteraction)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
        // Mostrar barra de energía durante la introducción (antes de que arranque el juego)
        if (_energyBarRoot != null) _energyBarRoot.SetActive(true);
        StartCoroutine(Sequence());
    }

    // =========================================================================
    // UPDATE
    // =========================================================================

    private void Update()
    {
        // No procesar nada si el juego está pausado
        if (!_challengeActive || _launching || AudioListener.pause) return;

        double now = AudioSettings.dspTime;

        // ── Avanzar marcadores de beat ─────────────────────────────────────
        bool newBeat = false;
        while (now >= _nextBeatDSP)
        {
            _lastBeatDSP  = _nextBeatDSP;
            _nextBeatDSP += _beatInterval;
            newBeat = true;
        }

        // ── Auto-fallo por beat perdido ────────────────────────────────────
        // Cuando llega un nuevo beat y el jugador ya había empezado la racha
        // pero NO acertó en este beat, se le da hasta el final de la ventana post-beat.
        if (newBeat && _hitsOk > 0)
        {
            if (!_hitSinceLastBeat)
                _autoFailTime = _lastBeatDSP + _hitWindow; // margen post-beat
            _hitSinceLastBeat = false;
        }
        if (_hitsOk > 0 && now > _autoFailTime)
        {
            TriggerAutoFail();
            return;
        }

        // ── Pulso visual del metrónomo ─────────────────────────────────────
        if (newBeat) PulseIdleCircles();

        // ── Input ──────────────────────────────────────────────────────────
        bool hit = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        if (!hit && Mouse.current != null)
            hit = Mouse.current.leftButton.wasPressedThisFrame;

        if (hit) HandleHit();
    }

    // =========================================================================
    // SECUENCIA PRINCIPAL
    // =========================================================================

    private IEnumerator Sequence()
    {
        // ── Modo test: teleportar al jugador y calcular offset, luego flujo normal ──
        if (_testMode)
        {
            if (_playerMovement == null || _beatManager == null)
            {
                Debug.LogError("[StartSequenceManager] Modo test: faltan referencias.", this);
                yield break;
            }

            // Teleportar. Z siempre; X e Y solo si se ha activado el override.
            Vector3 pos = _playerMovement.transform.position;
            pos.z = _testStartZ;
            if (_testOverrideXY)
            {
                pos.x = _testStartX;
                pos.y = _testStartY;
            }

            // El CharacterController puede resistir el cambio de posicion; se desactiva
            // un instante para que el teleport (sobre todo la altura Y) se aplique limpio.
            CharacterController cc = _playerMovement.GetComponent<CharacterController>();
            bool ccWasEnabled = cc != null && cc.enabled;
            if (ccWasEnabled) cc.enabled = false;
            _playerMovement.transform.position = pos;
            if (ccWasEnabled) cc.enabled = true;

            // Guardar el offset de canción; se usará en LaunchGame()
            _testSongOffset = _testStartZ / _playerMovement.walkSpeed;

            float beatApprox = _testSongOffset / _beatManager.BeatDuration;
            string posInfo = _testOverrideXY
                ? $"pos=({pos.x:F1}, {pos.y:F1}, {_testStartZ:F0})"
                : $"Z={_testStartZ:F0} m";
            Debug.Log($"[StartSequenceManager] TEST  {posInfo}  |  beat ≈{beatApprox:F0}  |  offset={_testSongOffset:F2} s");

            // Saltar intro siempre en test mode, luego la secuencia 3-2-1-YA! normal
            BeginChallenge();
            yield break;
        }

        // ── Flujo normal: intro + secuencia ──────────────────────────────────
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!_debugSkipIntro && !s_introPlayedScenes.Contains(scene) && _introSource != null && _introSource.clip != null)
        {
            _introSource.Play();
            yield return null;
            yield return new WaitUntil(() => !_introSource.isPlaying && !AudioListener.pause);
            s_introPlayedScenes.Add(scene);
        }

        // El 3-2-1-¡YA! no sale hasta que el jugador ha llegado al Starting Point (así no puede salir antes).
        if (_playerMovement == null) _playerMovement = FindAnyObjectByType<PlayerMovement>();
        float waited = 0f;
        while (_playerMovement != null && _playerMovement.IsMovingToStart && waited < 15f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        BeginChallenge();
    }

    private bool _checkpointMode;

    /// <summary>
    /// Reaparición en el checkpoint: coloca al jugador, recupera las monedas que llevaba y lanza el 3-2-1 normal;
    /// al completarlo la canción arranca justo en el segundo del checkpoint (como el modo test).
    /// </summary>
    private IEnumerator CheckpointSequence()
    {
        CheckpointSystem.State st = CheckpointSystem.ConsumePending();
        yield return null;   // que el resto de Start (puertas, etc.) ya se hayan ejecutado

        foreach (var d in FindObjectsByType<DoorInteraction>()) d.SkipForCheckpoint();
        foreach (var w in FindObjectsByType<StartWall>())       w.SkipForCheckpoint();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        if (_energyBarRoot != null) _energyBarRoot.SetActive(true);

        if (_playerMovement != null)
        {
            CharacterController cc = _playerMovement.GetComponent<CharacterController>();
            bool ccWasEnabled = cc != null && cc.enabled;
            if (ccWasEnabled) cc.enabled = false;
            _playerMovement.transform.position = st.pos;
            if (ccWasEnabled) cc.enabled = true;
        }
        // (Las monedas de la partida se conservan solas al reaparecer: ver LevelCoins.)

        _checkpointMode = true;
        _testSongOffset = st.songOffset;
        Debug.Log($"[StartSequenceManager] CHECKPOINT  pos={st.pos}  |  canción {st.songOffset:F2} s");
        BeginChallenge();
    }

    private void BeginChallenge()
    {
        _hitsOk           = 0;
        _beatInterval     = 60.0 / _bpm;
        _autoFailTime     = double.MaxValue;
        _hitSinceLastBeat = false;

        // Arrancar metrónomo con PlayScheduled → precisión DSP, sin latencia
        double metStart = AudioSettings.dspTime + 0.08;
        if (_metroSource != null)
        {
            _metroSource.loop = true;
            _metroSource.PlayScheduled(metStart);
        }

        // Los beats del tracking coinciden con el primer beat del clip
        _lastBeatDSP = metStart - _beatInterval;
        _nextBeatDSP = metStart;

        SetOverlay(true);
        ShowCircles();
        _challengeActive = true;
    }

    // =========================================================================
    // LÓGICA DE HIT
    // =========================================================================

    private void HandleHit()
    {
        if (_hitsOk >= TOTAL_CIRCLES) return;

        if (IsOnBeat())
        {
            // ── Acierto ──────────────────────────────────────────────────
            PlaySfx(_hitOkSound);
            StopAnim(_hitsOk);
            _activeAnims[_hitsOk] = StartCoroutine(AnimatePulse(_hitsOk));
            _hitsOk++;
            _hitSinceLastBeat = true;       // este beat ya está cubierto
            _autoFailTime     = double.MaxValue;
            Debug.Log($"[StartSequence] Acierto {_hitsOk}/{TOTAL_CIRCLES}");
            if (_hitsOk >= TOTAL_CIRCLES) StartCoroutine(LaunchGame());
        }
        else
        {
            // ── Fallo manual: sacudir todos los círculos completados + el actual ──
            PlaySfx(_hitErrorSound);
            int affected = _hitsOk + 1;
            _hitsOk           = 0;
            _hitSinceLastBeat = false;
            _autoFailTime     = double.MaxValue;
            for (int i = 0; i < affected && i < TOTAL_CIRCLES; i++)
            {
                StopAnim(i);
                _activeAnims[i] = StartCoroutine(AnimateShakeAndReset(i));
            }
            Debug.Log("[StartSequence] Fallo manual — reset a 0.");
        }
    }

    private void TriggerAutoFail()
    {
        PlaySfx(_hitErrorSound);
        int affected      = _hitsOk;
        _hitsOk           = 0;
        _autoFailTime     = double.MaxValue;
        _hitSinceLastBeat = false;
        for (int i = 0; i < affected; i++)
        {
            StopAnim(i);
            _activeAnims[i] = StartCoroutine(AnimateShakeAndReset(i));
        }
        Debug.Log("[StartSequence] Auto-fallo — beat perdido.");
    }

    private bool IsOnBeat()
    {
        double now       = AudioSettings.dspTime;
        double toNext    = _nextBeatDSP - now;
        double sinceLast = now - _lastBeatDSP;
        return System.Math.Min(toNext, sinceLast) <= _hitWindow;
    }

    // =========================================================================
    // LANZAR JUEGO
    // =========================================================================

    private IEnumerator LaunchGame()
    {
        _challengeActive = false;
        _launching       = true;

        if (_metroSource != null) _metroSource.Stop();

        // Programar canción y jugador en el siguiente beat (el 4º círculo acaba de pulsar)
        // Los círculos siguen visibles durante el pulse animation (~0.18 s) y se ocultan
        // cuando llega el beat de inicio (0.33 s después), quedando una transición limpia.
        double startDSP = _nextBeatDSP;

        if ((_testMode || _checkpointMode) && _testSongOffset > 0f)
            _beatManager.StartMusicFromAt(_testSongOffset, startDSP);  // arranca desde el offset de test
        else
            _beatManager.StartMusicAt(startDSP);

        _playerMovement.ScheduleStart(startDSP);

        if (_rhythmInput   != null) _rhythmInput.enabled   = true;
        if (_energyManager != null) _energyManager.enabled = true;
        if (_energyBarRoot != null) _energyBarRoot.SetActive(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // Esperar el beat de inicio (la animación de pulso del 4º círculo ocurre aquí)
        while (AudioSettings.dspTime < startDSP)
            yield return null;

        // Parar todas las animaciones antes de ocultar
        for (int i = 0; i < TOTAL_CIRCLES; i++) StopAnim(i);
        HideCircles();

        // Desvanecer overlay
        float t = 0f;
        while (t < _fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_overlay != null)
                _overlay.alpha = 1f - Mathf.Clamp01(t / _fadeDuration);
            yield return null;
        }
        SetOverlay(false);
    }

    // =========================================================================
    // ANIMACIONES
    // =========================================================================

    private IEnumerator AnimatePulse(int idx)
    {
        if (!Valid(idx)) yield break;
        var rt = _circleRoots[idx].GetComponent<RectTransform>();
        if (rt == null) yield break;

        if (_imgs[idx] != null) _imgs[idx].color = _colorGood;

        float e = 0f;
        while (e < _pulseDuration)
        {
            e += Time.unscaledDeltaTime;
            float t = e / _pulseDuration;
            float s = t < 0.5f
                ? Mathf.Lerp(1f, _pulseScale, t * 2f)
                : Mathf.Lerp(_pulseScale, 1f, (t - 0.5f) * 2f);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    /// <summary>Sacude el círculo y lo devuelve a color idle (usado en reset).</summary>
    private IEnumerator AnimateShakeAndReset(int idx)
    {
        if (!Valid(idx)) yield break;
        var rt = _circleRoots[idx].GetComponent<RectTransform>();
        if (rt == null) yield break;

        if (_imgs[idx] != null) _imgs[idx].color = _colorBad;

        Vector2 origin  = rt.anchoredPosition;
        float   elapsed = 0f;
        while (elapsed < _shakeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t      = elapsed / _shakeDuration;
            float offset = Mathf.Sin(t * Mathf.PI * 2f * _shakeWaves)
                           * _shakeRange * (1f - t);
            rt.anchoredPosition = origin + new Vector2(offset, 0f);
            yield return null;
        }
        rt.anchoredPosition = origin;
        rt.localScale       = Vector3.one;

        yield return new WaitForSecondsRealtime(0.1f);
        if (_imgs[idx] != null) _imgs[idx].color = _colorIdle;
    }

    // =========================================================================
    // PULSO IDLE (METRÓNOMO)
    // =========================================================================

    /// <summary>
    /// Lanza un pulso sutil en todos los círculos pendientes (aún no acertados)
    /// al ritmo del metrónomo. No reemplaza animaciones de acierto/error activas.
    /// </summary>
    private void PulseIdleCircles()
    {
        for (int i = _hitsOk; i < TOTAL_CIRCLES; i++)
        {
            if (!Valid(i) || _activeAnims[i] != null) continue; // respetar acierto/error en curso
            if (_idleAnims[i] != null) { StopCoroutine(_idleAnims[i]); _idleAnims[i] = null; }
            _idleAnims[i] = StartCoroutine(AnimateIdlePulse(i));
        }
    }

    private IEnumerator AnimateIdlePulse(int idx)
    {
        if (!Valid(idx)) yield break;
        var rt = _circleRoots[idx].GetComponent<RectTransform>();
        if (rt == null) { _idleAnims[idx] = null; yield break; }

        float e = 0f;
        while (e < _idlePulseDuration)
        {
            e += Time.unscaledDeltaTime;
            float t = e / _idlePulseDuration;
            float s = t < 0.5f
                ? Mathf.Lerp(1f, _idlePulseScale, t * 2f)
                : Mathf.Lerp(_idlePulseScale, 1f, (t - 0.5f) * 2f);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        rt.localScale    = Vector3.one;
        _idleAnims[idx]  = null;
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    /// <summary>
    /// Cancela la corrutina activa de un círculo (si la hay) y restaura su transform
    /// al estado en reposo (escala 1, posición original).
    /// </summary>
    private void StopAnim(int idx)
    {
        if (!Valid(idx)) return;
        if (_activeAnims[idx] != null) { StopCoroutine(_activeAnims[idx]); _activeAnims[idx] = null; }
        if (_idleAnims[idx]   != null) { StopCoroutine(_idleAnims[idx]);   _idleAnims[idx]   = null; }
        var rt = _circleRoots[idx].GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localScale       = Vector3.one;
            rt.anchoredPosition = _originPos[idx];
        }
    }

    private void SetOverlay(bool on)
    {
        if (_overlay == null) return;
        _overlay.alpha          = on ? 1f : 0f;
        _overlay.interactable   = on;
        _overlay.blocksRaycasts = on;
    }

    private void ShowCircles()
    {
        for (int i = 0; i < TOTAL_CIRCLES; i++)
        {
            StopAnim(i);      // cancelar cualquier animación residual antes de mostrar
            if (!Valid(i)) continue;
            _circleRoots[i].SetActive(true);
            bool isYa = (i == TOTAL_CIRCLES - 1);
            if (_imgs[i]  != null) _imgs[i].color = isYa ? _colorYaIdle : _colorIdle;
            if (_texts[i] != null) _texts[i].text = isYa ? "YA" : (i + 1).ToString();
        }
    }

    private void HideCircles()
    {
        foreach (var c in _circleRoots)
            if (c != null) c.SetActive(false);
    }

    private bool Valid(int i)
        => i >= 0 && i < _circleRoots.Length && _circleRoots[i] != null;

    private static void PreloadClip(AudioSource src)
    {
        if (src != null && src.clip != null)
            src.clip.LoadAudioData();
    }

    private void PlaySfx(AudioClip clip)
    {
        if (_sfxSource != null && clip != null)
            _sfxSource.PlayOneShot(clip);
    }
}
