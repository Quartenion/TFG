using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gestiona la secuencia de inicio del nivel.
///
/// Flujo.
///   La primera vez se reproduce la intro, después aparece la pantalla con los círculos y el metrónomo y, al
///   completarla, empiezan la canción y la carrera. Al reiniciar o al morir, se salta la intro y se pasa directamente
///   a los círculos.
///
/// Metrónomo.
///   Usa los mismos tics generados por código que el Metrónomo de la tienda (MetronomeClicks), programados con
///   PlayScheduled en el instante DSP exacto de cada beat, por lo que no tienen latencia. Suena siempre, ya que no
///   depende de la tienda. El tic agudo indica que el jugador puede empezar, y suena en cada beat mientras no ha
///   empezado la racha. En cuanto acierta el "1", los siguientes suenan graves (2, 3, ¡YA!). Si falla, el siguiente
///   beat vuelve a ser agudo y puede empezar ahí mismo, sin esperar a ningún compás. Por ejemplo, con A para agudo y G
///   para grave, AAAAA (acierta) AGG (falla) AAGGG.
///
/// Aciertos y fallos.
///   - Con un acierto, el círculo late en verde.
///   - Con un fallo, todos los círculos acertados y el actual tiemblan y vuelven a gris, y la cuenta vuelve a 0.
///   - Solo late el círculo que toca pulsar (el "1" mientras no ha empezado la racha, y luego el 2, el 3 y el ¡YA!).
///   - El tic de cada beat se decide en el último momento (ClickLead). Si el jugador acierta dentro del margen, suena
///     el que toca (grave), y si falla o se pasa el margen, el siguiente vuelve a ser agudo.
///
/// Configuración en el Inspector.
///   - _introSource es el AudioSource con el clip de la intro asignado, sin bucle.
///   - _sfxSource es el AudioSource para los efectos de acierto y fallo.
///   - Circle Roots son los círculos, cuyos Image y TMP_Text se buscan automáticamente.
/// </summary>
public class StartSequenceManager : MonoBehaviour
{
    // -=+=- Audio -=+=-

    [Header("Audio — Intro (se omite en restart)")]
    [Tooltip("AudioSource con el clip de la intro ya asignado en su campo 'AudioClip'. Loop OFF.")]
    [SerializeField] private AudioSource _introSource;

    [Header("Audio — Metrónomo (tics generados, sin clip)")]
    [Tooltip("Volumen de los tics del metrónomo del 3-2-1.")]
    [Range(0f, 1f)]
    [SerializeField] private float _clickVolume = 0.7f;

    [Header("Audio — Efectos de UI")]
    [Tooltip("AudioSource para los efectos de acierto/error (puede ser el mismo que el metrónomo).")]
    [SerializeField] private AudioSource _sfxSource;
    [SerializeField] private AudioClip   _hitOkSound;
    [SerializeField] private AudioClip   _hitErrorSound;

    // -=+=- Juego -=+=-

    [Header("Referencias de juego")]
    [SerializeField] private BeatManager    _beatManager;
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private RhythmInput    _rhythmInput;
    [SerializeField] private EnergyManager  _energyManager;

    // -=+=- Interfaz -=+=-

    [Header("UI — Overlay gris")]
    [SerializeField] private CanvasGroup _overlay;

    [Header("UI — Círculos (arrastrar Circle1, Circle2, Circle3, CircleYA)")]
    [Tooltip("4 GameObjects: los 3 numéricos + el círculo ¡YA!. Image y TMP_Text se buscan dentro.")]
    [SerializeField] private GameObject[] _circleRoots = new GameObject[4];

    // -=+=- Beat -=+=-

    [Header("Beat")]
    [SerializeField] private float _bpm       = 180f;
    [SerializeField] private float _hitWindow = 0.12f;

    // -=+=- Animaciones -=+=-

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
    [Tooltip("Escala del pulso en el BEAT FUERTE (mientras se puede empezar el 3-2-1).")]
    [SerializeField] private float _highBeatPulseScale = 1.2f;

    [Header("Fade del overlay al salir")]
    [SerializeField] private float _fadeDuration = 0.4f;

    // -=+=- Colores -=+=-

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

    // -=+=- Estado estático, que se mantiene al recargar la escena -=+=-

    /// <summary>
    /// Escenas en las que la intro ya se ha reproducido al menos una vez. La intro suena una vez por nivel cada vez que
    /// se abre el juego, ya que la lista se vacía al arrancar (también al pulsar Play en el editor, aunque esté
    /// desactivado el "domain reload").
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> s_introPlayedScenes
        = new System.Collections.Generic.HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetIntroMemory() => s_introPlayedScenes.Clear();

    /// <summary>Salta siempre la intro del nivel, para las mediciones y las pruebas. Lo activa ClockComparisonLogger.</summary>
    public static bool ForceSkipIntro { get; set; }

    // -=+=- Estado de la instancia -=+=-

    private float _testSongOffset = 0f;  // offset de la canción calculado en el modo test

    private const int TOTAL_CIRCLES = 4; // 1, 2, 3, ¡YA!

    private bool     _challengeActive;
    private bool     _launching;
    private int      _hitsOk;
    private double   _beatInterval;
    private double   _lastBeatDSP;
    private double   _nextBeatDSP;
    private MetronomeClicks _clicks;
    private double   _lastClickDSP;  // último beat con el tic ya programado
    private bool     _lastClickHigh; // ...y si era agudo
    private const double ClickLead = 0.05;   // antelación mínima con la que se programa cada tic, para decidirlo lo más tarde posible
    private Image[]      _imgs          = new Image[TOTAL_CIRCLES];
    private TMP_Text[]   _texts         = new TMP_Text[TOTAL_CIRCLES];
    private Coroutine[]  _activeAnims      = new Coroutine[TOTAL_CIRCLES];
    private Coroutine[]  _idleAnims        = new Coroutine[TOTAL_CIRCLES]; // pulso del metrónomo
    private Vector2[]    _originPos        = new Vector2[TOTAL_CIRCLES];
    // con la racha empezada, beat exacto en el que tiene que caer el siguiente acierto
    private double       _expectedBeatDSP;
    private double       _streakStartDSP = -1.0;   // beat (DSP) en el que empezó la racha actual

    // -=+=- Inicio -=+=-

    /// <summary>
    /// Reloj de la cuenta atrás, con el instante DSP de un beat del metrónomo y su intervalo (-1 si todavía no ha salido
    /// la cuenta). Lo usan los carteles del tutorial para latir solo desde que aparece la cuenta.
    /// </summary>
    public static double CountdownBeatDSP { get; private set; } = -1;
    public static double CountdownBeatInterval { get; private set; } = 0.5;

    private void OnDestroy() => CountdownBeatDSP = -1;

    private void Start()
    {
        CountdownBeatDSP = -1;
        // Si se pierde alguna referencia del Inspector (por ejemplo, entre prefabs), se busca en la escena.
        if (_beatManager    == null) _beatManager    = BeatManager.Instance != null ? BeatManager.Instance : FindAnyObjectByType<BeatManager>();
        if (_playerMovement == null) _playerMovement = FindAnyObjectByType<PlayerMovement>();
        if (_rhythmInput    == null) _rhythmInput    = FindAnyObjectByType<RhythmInput>();
        if (_energyManager  == null) _energyManager  = FindAnyObjectByType<EnergyManager>();
        Time.timeScale       = 1f;
        AudioListener.pause  = false;
        AudioListener.volume = GameSettings.Volume;   // el volumen elegido en los ajustes

        if (_rhythmInput   != null) _rhythmInput.enabled   = false;
        if (_energyManager != null) _energyManager.enabled = false;

        // Se guardan el Image, el TMP_Text y la posición en reposo de cada círculo.
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
        if (_beatManager != null) _beatManager.PreloadAudio();

        // Al reaparecer en un punto de control (tienda), no hay zona de inicio ni puerta y se pasa directamente a la cuenta atrás desde ese punto.
        if (CheckpointSystem.PendingRespawn)
        {
            StartCoroutine(CheckpointSequence());
            return;
        }

        if (_waitForInteraction)
        {
            // En la zona de inicio, el cursor se bloquea para poder moverse en primera persona.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            // Se activa el movimiento libre, y el jugador puede moverse hasta que pulse E.
            if (_playerMovement != null)
                _playerMovement.EnableFreeroam();
            // La barra de energía se oculta en la zona de inicio.
            if (_energyBarRoot != null)
                _energyBarRoot.SetActive(false);
            // La secuencia la lanza StartWall cuando el jugador pulsa E.
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            StartCoroutine(Sequence());
        }
    }

    /// <summary>
    /// Lanza la secuencia de inicio. La llama StartWall cuando el jugador pulsa E, y solo funciona si
    /// _waitForInteraction está activo.
    /// </summary>
    public void TriggerSequence()
    {
        if (_challengeActive || _launching) return;
        // En la zona de inicio el cursor ya está bloqueado, y no se cambia aquí, ya que PauseMenuManager interpreta el paso
        // de Locked a None como si el jugador hubiese pulsado ESC. El cursor se mantiene bloqueado durante toda la intro y
        // se gestiona en LaunchGame.
        if (!_waitForInteraction)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
        // La barra de energía se muestra durante la introducción, antes de que empiece la partida.
        if (_energyBarRoot != null) _energyBarRoot.SetActive(true);
        StartCoroutine(Sequence());
    }

    // -=+=- Update -=+=-

    private void Update()
    {
        // Si el juego está pausado, no se procesa nada.
        if (!_challengeActive || _launching || AudioListener.pause) return;

        double now = AudioSettings.dspTime;

        // -=+=- Avance de los marcadores de beat -=+=-
        bool newBeat = false;
        while (now >= _nextBeatDSP)
        {
            _lastBeatDSP  = _nextBeatDSP;
            _nextBeatDSP += _beatInterval;
            newBeat = true;
        }

        // -=+=- Metrónomo, que programa el tic del próximo beat en su instante DSP exacto -=+=-
        if (_clicks != null && _nextBeatDSP > _lastClickDSP + 0.001 && _nextBeatDSP - now <= ClickLead)
        {
            // El tic es agudo si todavía no ha empezado la racha, o si la racha empieza justo en este beat, es decir, si el clic
            // llegó un poco adelantado, antes de programar su tic.
            bool high = _hitsOk == 0 || System.Math.Abs(_nextBeatDSP - _streakStartDSP) < 0.001;
            ScheduleClick(_nextBeatDSP, high);
        }

        // -=+=- Fallo automático por beat perdido -=+=-
        // Con la racha empezada, el siguiente acierto tiene que caer en el beat que toca, así que, en cuanto se cierra su
        // ventana sin ningún clic, la cuenta se reinicia y el tic del siguiente beat ya sale agudo.
        if (_hitsOk > 0 && now > _expectedBeatDSP + _hitWindow)
        {
            TriggerAutoFail();
            return;
        }

        // -=+=- Pulso visual del metrónomo, más grande en el beat fuerte -=+=-
        if (newBeat) PulseIdleCircles(_hitsOk == 0);

        // -=+=- Entrada -=+=-
        bool hit = Keybinds.Pressed(Keybinds.Bind.Jump);
        if (!hit && Mouse.current != null)
            hit = Mouse.current.leftButton.wasPressedThisFrame;

        if (hit) HandleHit();
#if UNITY_EDITOR
        if (Time.unscaledDeltaTime > 0.05f)
            Debug.Log($"[StartSequence] Tirón: fotograma de {Time.unscaledDeltaTime * 1000f:F0} ms (aciertos {_hitsOk})");
#endif
    }

    // -=+=- Secuencia principal -=+=-

    private IEnumerator Sequence()
    {
        // -=+=- Modo test: se teletransporta al jugador, se calcula el offset y se sigue el flujo normal -=+=-
        if (_testMode)
        {
            if (_playerMovement == null || _beatManager == null)
            {
                Debug.LogError("[StartSequenceManager] Modo test: faltan referencias.", this);
                yield break;
            }

            // Teletransporte. La Z se cambia siempre, y la X y la Y solo si se ha activado el override.
            Vector3 pos = _playerMovement.transform.position;
            pos.z = _testStartZ;
            if (_testOverrideXY)
            {
                pos.x = _testStartX;
                pos.y = _testStartY;
            }

            // El CharacterController puede impedir el cambio de posición, por lo que se desactiva un instante para que el
            // teletransporte (sobre todo la altura) se aplique bien.
            CharacterController cc = _playerMovement.GetComponent<CharacterController>();
            bool ccWasEnabled = cc != null && cc.enabled;
            if (ccWasEnabled) cc.enabled = false;
            _playerMovement.transform.position = pos;
            if (ccWasEnabled) cc.enabled = true;

            // Se guarda el offset de la canción, que se usará en LaunchGame().
            _testSongOffset = _testStartZ / _playerMovement.walkSpeed;

            float beatApprox = _testSongOffset / _beatManager.BeatDuration;
            string posInfo = _testOverrideXY
                ? $"pos=({pos.x:F1}, {pos.y:F1}, {_testStartZ:F0})"
                : $"Z={_testStartZ:F0} m";
            Debug.Log($"[StartSequenceManager] TEST  {posInfo}  |  beat ≈{beatApprox:F0}  |  offset={_testSongOffset:F2} s");

            // En el modo test se salta siempre la intro y después se hace la cuenta atrás normal.
            BeginChallenge();
            yield break;
        }

        // -=+=- Flujo normal, con la intro y la secuencia -=+=-
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!_debugSkipIntro && !ForceSkipIntro && !s_introPlayedScenes.Contains(scene) && _introSource != null && _introSource.clip != null)
        {
            _introSource.Play();
            yield return null;
            yield return new WaitUntil(() => !_introSource.isPlaying && !AudioListener.pause);
            s_introPlayedScenes.Add(scene);
        }

        // La cuenta atrás no aparece hasta que el jugador ha llegado al Starting Point, para que no pueda salir antes.
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
    /// Reaparición en el punto de control. Coloca al jugador, recupera las monedas que llevaba y lanza la cuenta atrás
    /// normal. Al completarla, la canción empieza justo en el segundo del punto de control, como en el modo test.
    /// </summary>
    private IEnumerator CheckpointSequence()
    {
        CheckpointSystem.State st = CheckpointSystem.ConsumePending();
        yield return null;   // para que el resto de Start (puertas, etc.) ya se haya ejecutado

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
        // Las monedas de la partida se conservan solas al reaparecer (ver LevelCoins).

        _checkpointMode = true;
        _testSongOffset = st.songOffset;
        Debug.Log($"[StartSequenceManager] CHECKPOINT  pos={st.pos}  |  canción {st.songOffset:F2} s");
        BeginChallenge();
    }

    private void BeginChallenge()
    {
        _hitsOk           = 0;
        _beatInterval     = 60.0 / (_beatManager != null && _beatManager.Bpm > 0f ? _beatManager.Bpm : _bpm);
        _expectedBeatDSP  = 0.0;
        _ticks.Clear();

        // Metrónomo. El primer tic, que es agudo, se programa en el reloj DSP, y los demás desde Update.
        double metStart = AudioSettings.dspTime + 0.08;
        if (_clicks == null) _clicks = new MetronomeClicks(gameObject);
        ScheduleClick(metStart, true);

        _lastBeatDSP = metStart - _beatInterval;
        _nextBeatDSP = metStart;
        CountdownBeatDSP = metStart;
        CountdownBeatInterval = _beatInterval;

        SetOverlay(true);
        ShowCircles();
        _challengeActive = true;
    }

    // -=+=- Aciertos -=+=-

    private void HandleHit()
    {
        if (_hitsOk >= TOTAL_CIRCLES) return;

        // Beat al que corresponde el clic, que es el más cercano. Con la racha empezada, tiene que ser el que toca, por lo
        // que un clic de más en el mismo beat, o acertar después de saltarse uno, cuenta como fallo.
        double now       = AudioSettings.dspTime;
        double clickBeat = (_nextBeatDSP - now) < (now - _lastBeatDSP) ? _nextBeatDSP : _lastBeatDSP;
        // La racha solo puede empezar en un beat con tic agudo, para que al llegar al ¡YA! no falte el último tic grave.
        bool ok = IsOnBeat() && (_hitsOk == 0 ? TickWasHigh(clickBeat)
                                              : System.Math.Abs(clickBeat - _expectedBeatDSP) < 0.001);
        if (ok)
        {
            // -=+=- Acierto -=+=-
            PlaySfx(_hitOkSound);
            if (_hitsOk == 0) _streakStartDSP = clickBeat;
            StopAnim(_hitsOk);
            _activeAnims[_hitsOk] = StartCoroutine(AnimatePulse(_hitsOk));
            _hitsOk++;
            _expectedBeatDSP = clickBeat + _beatInterval;   // el siguiente tiene que caer justo en el beat de después
            Debug.Log($"[StartSequence] Acierto {_hitsOk}/{TOTAL_CIRCLES}");
            if (_hitsOk >= TOTAL_CIRCLES) StartCoroutine(LaunchGame());
        }
        else
        {
            // -=+=- Fallo manual: tiemblan todos los círculos acertados y el actual -=+=-
            PlaySfx(_hitErrorSound);
            int affected = _hitsOk + 1;
            _hitsOk           = 0;
            for (int i = 0; i < affected && i < TOTAL_CIRCLES; i++)
            {
                StopAnim(i);
                _activeAnims[i] = StartCoroutine(AnimateShakeAndReset(i));
            }
            ForceNextClickHigh();
            Debug.Log("[StartSequence] Fallo manual — reset a 0.");
        }
    }

    private void TriggerAutoFail()
    {
        PlaySfx(_hitErrorSound);
        int affected      = _hitsOk;
        _hitsOk           = 0;
        for (int i = 0; i < affected; i++)
        {
            StopAnim(i);
            _activeAnims[i] = StartCoroutine(AnimateShakeAndReset(i));
        }
        ForceNextClickHigh();
        Debug.Log("[StartSequence] Auto-fallo — beat perdido.");
    }

    private void ScheduleClick(double at, bool high)
    {
        _clicks.Schedule(at, high, _clickVolume);
        _lastClickDSP  = at;
        _lastClickHigh = high;
        _ticks.RemoveAll(k => System.Math.Abs(k.t - at) < 0.001);   // por si se vuelve a programar el mismo beat
        _ticks.Add((at, high));
        if (_ticks.Count > 8) _ticks.RemoveAt(0);
    }

    /// <summary>Tics programados recientemente (instante DSP y si son agudos).</summary>
    private readonly System.Collections.Generic.List<(double t, bool high)> _ticks =
        new System.Collections.Generic.List<(double t, bool high)>();

    /// <summary>Indica si el beat de 'beatDSP' tuvo (o va a tener) un tic agudo. Si no hay tic registrado, se acepta.</summary>
    private bool TickWasHigh(double beatDSP)
    {
        foreach (var k in _ticks)
            if (System.Math.Abs(k.t - beatDSP) < 0.01) return k.high;
        return true;
    }

    /// <summary>
    /// Después de un fallo, si el tic del próximo beat ya estaba programado como grave, se cambia por uno agudo, ya que
    /// se puede volver a empezar en ese mismo beat.
    /// </summary>
    private void ForceNextClickHigh()
    {
        if (_clicks == null || _lastClickHigh || _lastClickDSP < _nextBeatDSP - 0.001) return;
        _clicks.StopAll();
        ScheduleClick(_lastClickDSP, true);
    }

    private bool IsOnBeat()
    {
        double now       = AudioSettings.dspTime;
        double toNext    = _nextBeatDSP - now;
        double sinceLast = now - _lastBeatDSP;
        return System.Math.Min(toNext, sinceLast) <= _hitWindow;
    }

    // -=+=- Inicio de la partida -=+=-

    private IEnumerator LaunchGame()
    {
        _challengeActive = false;
        _launching       = true;

        _clicks?.StopAll();   // el beat de inicio ya lo marca la canción
        // Si el ¡YA! se pulsó un poco antes de su beat, StopAll también quitaría el tic grave de ese beat, que ya estaba
        // programado, y el ¡YA! sonaría sin tic, por lo que se vuelve a programar si todavía no ha sonado.
        double yaBeat = _expectedBeatDSP - _beatInterval;
        if (_clicks != null && yaBeat > AudioSettings.dspTime + 0.005) ScheduleClick(yaBeat, false);

        // La canción y el jugador se programan en el siguiente beat, ya que el cuarto círculo acaba de latir. Los círculos
        // siguen visibles durante la animación del latido (unos 0,18 s) y se ocultan cuando llega el beat de inicio (0,33 s
        // después), para que la transición quede limpia.
        double startDSP = _expectedBeatDSP;   // el beat justo después del ¡YA!, aunque se haya pulsado un poco antes

        if ((_testMode || _checkpointMode) && _testSongOffset > 0f)
            _beatManager.StartMusicFromAt(_testSongOffset, startDSP);  // empieza desde el offset de test
        else
            _beatManager.StartMusicAt(startDSP);

        _playerMovement.ScheduleStart(startDSP);

        if (_rhythmInput   != null) _rhythmInput.enabled   = true;
        if (_energyManager != null) _energyManager.enabled = true;
        if (_energyBarRoot != null) _energyBarRoot.SetActive(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // Se espera al beat de inicio. Mientras tanto, se reproduce la animación del latido del cuarto círculo.
        while (AudioSettings.dspTime < startDSP)
            yield return null;

        // Se paran todas las animaciones antes de ocultar los círculos.
        for (int i = 0; i < TOTAL_CIRCLES; i++) StopAnim(i);
        HideCircles();

        // Se desvanece la capa superpuesta.
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

    // -=+=- Animaciones -=+=-

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
        _activeAnims[idx] = null;
    }

    /// <summary>Hace temblar el círculo y lo devuelve al color en reposo. Se usa al reiniciar la cuenta.</summary>
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
        if (_imgs[idx] != null) _imgs[idx].color = idx == TOTAL_CIRCLES - 1 ? _colorYaIdle : _colorIdle;
        _activeAnims[idx] = null;   // así el círculo sigue latiendo con el metrónomo después de un fallo
    }

    // -=+=- Pulso en reposo (metrónomo) -=+=-

    /// <summary>
    /// Hace latir solo el círculo que toca pulsar, con más fuerza si todavía no ha empezado la racha. No sustituye a las
    /// animaciones de acierto o fallo que estén en marcha.
    /// </summary>
    private void PulseIdleCircles(bool highBeat)
    {
        int i = _hitsOk;   // solo late el círculo que toca pulsar ahora
        if (!Valid(i) || _activeAnims[i] != null) return;   // no se interrumpe un acierto o un fallo en curso
        if (_idleAnims[i] != null) { StopCoroutine(_idleAnims[i]); _idleAnims[i] = null; }
        _idleAnims[i] = StartCoroutine(AnimateIdlePulse(i, highBeat ? _highBeatPulseScale : _idlePulseScale));
    }

    private IEnumerator AnimateIdlePulse(int idx, float pulseScale)
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
                ? Mathf.Lerp(1f, pulseScale, t * 2f)
                : Mathf.Lerp(pulseScale, 1f, (t - 0.5f) * 2f);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        rt.localScale    = Vector3.one;
        _idleAnims[idx]  = null;
    }

    // -=+=- Funciones auxiliares -=+=-

    /// <summary>
    /// Cancela la corrutina activa de un círculo, si la hay, y devuelve su transform al estado en reposo (escala 1 y
    /// posición original).
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
            StopAnim(i);      // se cancela cualquier animación que quede antes de mostrar los círculos
            if (!Valid(i)) continue;
            _circleRoots[i].SetActive(true);
            bool isYa = (i == TOTAL_CIRCLES - 1);
            if (_imgs[i]  != null) _imgs[i].color = isYa ? _colorYaIdle : _colorIdle;
            if (_texts[i] != null) _texts[i].text = isYa ? Loc.T("YA", "GO") : (i + 1).ToString();
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
