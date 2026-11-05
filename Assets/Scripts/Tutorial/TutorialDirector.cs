using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// Modo tutorial del nivel 1. Se crea solo en Level1 la primera vez que se juega, o si se activa en los ajustes.
///
/// Hasta el beat <see cref="S.endBeat"/>, antes de cada acción del guion (<see cref="TutorialLevel1Steps.Steps"/>)
/// ocurre lo siguiente.
///   1. La acción cae como una nota en el monitor del corazón, y llega justo en su beat.
///   2. Un beat antes, el jugador y la música se van frenando hasta pararse justo en ese beat.
///   3. La pantalla se oscurece, se ilumina el obstáculo y aparece el texto ("Pulsa ESPACIO para saltar").
///   4. El jugador hace la acción en ese instante, en el beat, y todo vuelve a arrancar poco a poco.
/// El movimiento lateral siempre es libre. Si el jugador hace la acción por su cuenta mientras el juego frena, no se para.
///
/// Algunas explicaciones salen una sola vez y se recuerdan aunque el jugador muera: la energía (al empezar), el primer
/// clic fallado, la primera vez que entra sangre azul y el primer fallo con sangre azul.
///
/// Para frenar el tiempo, el reloj del BeatManager (del que depende todo el nivel) avanza a velocidad 'speed' (1 es
/// normal y 0, parado), Time.timeScale = speed (jugador, física y animaciones) y el pitch de la música también es
/// speed. Al volver a 1, el reloj se vuelve a sincronizar con la posición real de la música.
/// </summary>
public class TutorialDirector : MonoBehaviour
{
    // -=+=- Preferencias -=+=-
    private const string PrefPending = "Tutorial.L1";          // 1 significa que el tutorial se hará la próxima vez
    private static readonly string[] OncePrefs = { "Tutorial.Energy", "Tutorial.Miss", "Tutorial.Blue", "Tutorial.BlueMiss" };

    /// <summary>Indica si se hará el tutorial al entrar en el nivel 1 (Ajustes > Tutorial). Al activarlo, se repite todo.</summary>
    public static bool Pending
    {
        get => PlayerPrefs.GetInt(PrefPending, 0) == 1;   // por defecto no, ya que el tutorial se hace en la escena Tutorial
        set
        {
            PlayerPrefs.SetInt(PrefPending, value ? 1 : 0);
            if (value) foreach (string k in OncePrefs) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Indica si el guion del tutorial está en marcha (antes del beat final). Las pistas no repiten sus notas.</summary>
    public static bool StepsRunning { get; private set; }

    private static bool Shown(int i) => PlayerPrefs.GetInt(OncePrefs[i], 0) == 1;

    /// <summary>Indica si ya se ha dado la explicación de la energía.</summary>
    public static void MarkEnergyShown() => MarkShown(0);

    /// <summary>Indica si ya se ha dado la explicación de una vez 'i' (0 energía, 1 primer fallo, 2 sangre azul y 3 fallo con azul).</summary>
    public static bool OnceShown(int i) => Shown(i);

    /// <summary>Indica si ya se han explicado todas las interacciones con la barra de energía.</summary>
    public static bool EnergyAllExplained => Shown(0) && Shown(1) && Shown(2) && Shown(3);

    /// <summary>Vuelve a dejar pendientes las explicaciones de la energía, para hacer el tutorial desde el principio.</summary>
    public static void ResetOnceExplanations()
    {
        foreach (string k in OncePrefs) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.Save();
    }
    private static void MarkShown(int i) { PlayerPrefs.SetInt(OncePrefs[i], 1); PlayerPrefs.Save(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        TutorialScript script = TutorialScript.Current;
        if (s.name != script.scene) return;
        if (StudySession.Active && !script.allowInStudy) return;
        // La escena del nivel tutorial tiene su propio guion, que se usa siempre, sin mirar los ajustes.
        if (script.alwaysRun)
        {
            if (FindAnyObjectByType<TutorialDirector>() == null) new GameObject("TutorialDirector").AddComponent<TutorialDirector>();
            return;
        }
        // Interruptor en la escena (objeto "Tutorial"). Si está apagado, no se hace nada, y con "Forzar siempre" se hace el tutorial completo.
        TutorialSwitch sw = FindAnyObjectByType<TutorialSwitch>(FindObjectsInactive.Include);
        if (sw != null && !sw.tutorialActivo) return;
        if (sw != null && sw.forzarSiempre) Pending = true;
        // Se crea si toca el tutorial o si queda alguna explicación de una vez por salir (fallo, sangre azul...).
        bool onceLeft = script.eventExplanations && (!Shown(1) || !Shown(2) || !Shown(3));
        if (!Pending && !onceLeft) return;
        if (FindAnyObjectByType<TutorialDirector>() != null) return;
        new GameObject("TutorialDirector").AddComponent<TutorialDirector>();
    }

    // -=+=- Ajustes del efecto -=+=-
    private const float SlowLeadBeats   = 1f;     // empieza a frenar 1 beat antes de la acción
    private const float NoteLeadBeats   = HeartMonitorNotes.LeadBeats;   // la nota aparece 3 beats antes
    private const float ExplainLeadBeat = 0.4f;   // en las explicaciones por evento, la frenada es corta
    private const float ResumeSeconds   = 0.45f;  // vuelta a la velocidad normal, en tiempo real
    private const float DimAlpha        = 0.8f;

    // -=+=- Referencias -=+=-
    private BeatManager _bm;
    private PlayerMovement _pm;
    private EnergyManager _em;
    private RhythmInput _rhythm;
    private BlockAbility _block;
    private GameOverManager _gameOver;
    private bool _hooked;

    // -=+=- Control del tiempo -=+=-
    private enum Mode { Run, Slow, Frozen, Resume }
    private Mode _mode = Mode.Run;
    private bool _controlling;
    private double _songT, _lastDsp;      // reloj propio (segundos de canción) mientras se controla el tiempo
    private double _slowTarget, _slowR0;  // frenada: instante de la parada y "distancia" inicial
    private float _speed = 1f, _resumeT;
    private double _audioK; private int _audioKn;   // desfase medio entre el reloj y la posición de la música

    // -=+=- Pasos -=+=-
    private class Request
    {
        public int step = -1;          // índice en Steps (-1 si es una explicación sin acción)
        public int once = -1;          // índice en OncePrefs (explicaciones de una vez)
        public float beat;
        public TutorialKey key;
        public string text, prompt;
        public Transform highlight;
        public bool look;              // al pararse, la vista gira hacia 'highlight'
        public SyringeHighlight.Side hud;   // jeringuilla del HUD que se resalta mientras el tiempo está parado
    }
    private Request _req;
    private int _nextStep;
    private TutorialScript S;      // guion (nivel 1 o nivel tutorial)
    private bool[] _done;
    private float _lockUntil;      // al pararse, la acción no se puede hacer hasta este instante (en tiempo real)
    private readonly Queue<int> _onceQueue = new Queue<int>();
    private bool _finished;
    private bool _playedFromStart;
    private bool _locksApplied;
    private TutorialKey _actionThisFrame = TutorialKey.None;
    private bool _blockedThisFrame;
    private int _pressFrame = -1;
    private int _failedPresses;

    // -=+=- Interfaz -=+=-
    private RectTransform _canvasRt;
    private RawImage _dim;
    private Texture2D _dimTex;
    private const int DimW = 192, DimH = 108;
    private Color32[] _dimPx;
    private float _dimFade;
    private GameObject _panel;
    private TextMeshProUGUI _text, _prompt, _hint;
    private float _toastT;
    private HeartMonitorNotes _lane;

    /// <summary>Director de la escena (null si no hay tutorial).</summary>
    public static TutorialDirector Instance { get; private set; }
    private void Awake() => Instance = this;

    // -=+=- Explicaciones pedidas desde fuera (por ejemplo, moverse a los lados en la tirolina) -=+=-
    private readonly Queue<string> _extraQueue = new Queue<string>();

    /// <summary>En cuanto se pueda, frena, para el juego y explica 'text'. No hay acción que hacer, así que el juego sigue al cerrar la caja.</summary>
    public void Explain(string text) { if (!string.IsNullOrEmpty(text)) _extraQueue.Enqueue(text); }


    // -=+=-

    private void Start()
    {
        S = TutorialScript.Current;
        _done = new bool[S.steps.Length];
        _finished = !S.alwaysRun && !Pending;   // sin tutorial pendiente, solo las explicaciones de una vez
        StepsRunning = !_finished;
        TutorialGate.UnlockAll();   // los bloqueos se ponen al empezar el nivel, ya que en la sala de inicio se puede saltar
        BuildUI();
    }

    private void OnDestroy()
    {
        RestoreLoopingSounds();
        RestoreFixedDelta();
        if (Instance == this) Instance = null;
        EndGuide(false);
        StepsRunning = false;
        TutorialGate.UnlockAll();
        TutorialGate.HoldBack = TutorialKey.None;
        if (_controlling) { TutorialGate.Frozen = false; TutorialGate.Busy = false; TutorialGate.Expected = TutorialKey.None; if (_bm != null) _bm.HoldClock(false); }
        if (_controlling && (_gameOver == null || !_gameOver.IsDead)) { Time.timeScale = 1f; if (_bm != null && _bm.CurrentSource != null) _bm.CurrentSource.pitch = 1f; }
        if (_dimTex != null) Destroy(_dimTex);
    }

    private void Hook()
    {
        if (_hooked) return;
        _bm = BeatManager.Instance;
        _pm = FindAnyObjectByType<PlayerMovement>();
        _em = EnergyManager.Instance;
        _rhythm = FindAnyObjectByType<RhythmInput>();
        _block = FindAnyObjectByType<BlockAbility>();
        _gameOver = FindAnyObjectByType<GameOverManager>();
        if (_bm == null || _pm == null) return;
        _hooked = true;

        // Un salto desde el suelo es Jump y en el aire, DoubleJump, para que el salto anterior no cuente como el doble salto.
        Ensure(ref _pm.OnJumpExecuted).AddListener(() => Act(_groundedLastFrame ? TutorialKey.Jump : TutorialKey.DoubleJump));
        Ensure(ref _pm.OnWalljumped).AddListener(() => Act(TutorialKey.Jump));
        Ensure(ref _pm.OnKayakJump).AddListener(() => Act(TutorialKey.Jump));   // salto desde el kayak
        if (_pm.OnDashStarted == null) _pm.OnDashStarted = new UnityEngine.Events.UnityEvent<Vector3>();
        _pm.OnDashStarted.AddListener(_ => Act(TutorialKey.Dash));
        Ensure(ref _pm.OnSlideStarted).AddListener(() => Act(TutorialKey.Slide));
        Ensure(ref _pm.OnSlamStarted).AddListener(() => Act(TutorialKey.Slam));
        Ensure(ref _pm.OnZiplineAttached).AddListener(() => Act(TutorialKey.Zipline));
        Ensure(ref _pm.OnJumpBlocked).AddListener(() => _blockedThisFrame = true);
        Ensure(ref _pm.OnDashBlocked).AddListener(() => _blockedThisFrame = true);
        Ensure(ref _pm.OnSlideBlocked).AddListener(() => _blockedThisFrame = true);
        if (_block != null) Ensure(ref _block.OnBlockStarted).AddListener(() => Act(TutorialKey.Block));

        if (_rhythm != null)
        {
            if (_rhythm.OnTimingResult == null) _rhythm.OnTimingResult = new UnityEngine.Events.UnityEvent<TimingResult>();
            _rhythm.OnTimingResult.AddListener(r =>
            {
                if (r == TimingResult.Miss && _em != null && !_em.IsBlueMode) QueueOnce(1);
            });
        }
        if (_em != null)
        {
            if (_em.OnBlueModeEntered == null) _em.OnBlueModeEntered = new UnityEngine.Events.UnityEvent<float>();
            _em.OnBlueModeEntered.AddListener(_ => QueueOnce(2));
            if (_em.OnBlueLost == null) _em.OnBlueLost = new UnityEngine.Events.UnityEvent<float>();
            _em.OnBlueLost.AddListener(_ => QueueOnce(3));
        }
    }

    private static UnityEngine.Events.UnityEvent Ensure(ref UnityEngine.Events.UnityEvent e)
        => e ??= new UnityEngine.Events.UnityEvent();

    private void Act(TutorialKey k) { _actionThisFrame = k; _nextSoundScan = 0f; }   // una acción puede reproducir un sonido, así que se revisan ya

    private void QueueOnce(int i)
    {
        if (S != null && !S.eventExplanations) return;   // en el nivel tutorial, la energía la explica CourseBuilder, cada 4 beats
        if (Shown(i) || _onceQueue.Contains(i)) return;
        if (_req != null && _req.once == i) return;
        _onceQueue.Enqueue(i);
    }

    // -=+=- Bucle -=+=-

    private void Update()
    {
        Hook();
        // Si el juego está frenando hacia una explicación, su acción espera a que salga el mensaje.
        TutorialGate.HoldBack = _mode == Mode.Slow && _req != null && _req.step >= 0 && !string.IsNullOrEmpty(_req.text)
                                ? _req.key : TutorialKey.None;
        UpdateToast();
        _dialog?.Tick();
        if (!_hooked || _bm == null) { _actionThisFrame = TutorialKey.None; return; }

        // Si el jugador ha muerto, se suelta el control, ya que el game over gestiona el tiempo y la escena se recarga.
        if (_gameOver != null && _gameOver.IsDead)
        {
            if (_controlling) { _controlling = false; TutorialGate.Frozen = false; TutorialGate.Busy = false; _bm.HoldClock(false); }
            _mode = Mode.Run; HidePanel(); _dialog?.Hide(); _dimFade = 0f; UpdateDim(); EndGuide(false);
            SyringeHighlight.Set(SyringeHighlight.Side.None);
            _actionThisFrame = TutorialKey.None;
            return;
        }
        // Al final del recorrido (balanza, resultados, sala) se quitan los avisos, las notas del monitor y la caja de texto.
        if ((LevelEndManager.Finished || _pm.IsFreeroam) && !_controlling)
        {
            if (_panel != null && _panel.activeSelf) { _toastT = 0f; _panel.SetActive(false); }
            if (_dialog != null && _dialog.IsOpen) _dialog.Hide();
            _lane?.Clear();
            _liveShown = -1;
        }
        if (PauseMenuManager.IsPaused || !_bm.HasClock || _pm.IsFreeroam || LevelEndManager.Finished) { _actionThisFrame = TutorialKey.None; return; }

        // Al empezar el nivel, las acciones que todavía no se han enseñado quedan bloqueadas hasta su primera explicación.
        if (!_locksApplied)
        {
            _locksApplied = true;
            // Solo se bloquea una tecla si la primera vez que se pide es con explicación. Por ejemplo, el salto hacia la pared
            // tiene explicación, pero el salto ya se ha enseñado antes y no debe quedar bloqueado.
            if (!_finished)
            {
                var seen = new HashSet<TutorialKey>();
                foreach (var st in S.steps)
                {
                    if (st.key == TutorialKey.None || !seen.Add(st.key)) continue;
                    if (Pauses(st)) TutorialGate.Lock(st.key);
                }
            }
        }

        float bd = _bm.BeatDuration;
        double latency = _bm.SongTimeUnclamped - _bm.CorrectedTimeUnclamped;   // compensación de latencia
        // Vigilancia del desfase al empezar a sonar la canción (también al reaparecer en un punto de control o después de
        // "Saltar sección"). Está desactivada, ya que sin haber medido antes el desfase normal entre el reloj y la música
        // (unos 110 ms en el equipo de desarrollo) se corregía algo que no estaba mal y la música quedaba desplazada. Por
        // eso, el desfase solo se vigila después de las paradas.
        if (_bm.IsPlaying && !_sawMusic) _sawMusic = true;
        if (_mode == Mode.Run && _bm.IsPlaying && !_controlling)
        {
            // Justo después de una parada, el desfase se vigila y se corrige, y el resto del tiempo solo se mide.
            if (Time.unscaledTime < _driftWatchUntil) CheckDrift();
            else MeasureAudioOffset();
        }

        TickClock();
        float beatNow = (float)((_controlling ? _songT : _bm.ClockSeconds) - latency) / bd;

        switch (_mode)
        {
            case Mode.Run:
            case Mode.Resume:
                if (_mode == Mode.Resume)
                {
                    _resumeT += Time.unscaledDeltaTime;
                    _speed = Mathf.Clamp01(_resumeT / ResumeSeconds);
                    _dimFade = Mathf.Min(_dimFade, 1f - _speed);
                    if (_speed >= 1f) { EndControl(); _mode = Mode.Run; }
                }
                UpdateLive(beatNow);
                SkipPassedSteps(beatNow);
                TryStartNext(beatNow, bd, latency);
                break;

            case Mode.Slow:
                if (_req.step >= 0 && Matches(_actionThisFrame, _req.key))
                {
                    // Si el jugador ha hecho la acción por su cuenta mientras frenaba, no hace falta parar (salvo que tenga una explicación posterior).
                    CompleteStep();
                    if (!ChainFollow()) BeginResume();
                    break;
                }
                double r = _slowTarget - _songT;
                _speed = r <= 0.0005 ? 0f : Mathf.Clamp01((float)System.Math.Sqrt(r / _slowR0));
                _dimFade = Mathf.Max(_dimFade, 1f - _speed);
                if (_speed < 0.03f || r <= 0.0015)
                {
                    _songT = _slowTarget; _speed = 0f;
                    Freeze();
                }
                break;

            case Mode.Frozen:
                _speed = 0f;
                _dimFade = 1f;
                FrozenInput();
                break;
        }

        ApplySpeed();
        UpdateNotes(beatNow);
        UpdateDim();
        PulseGlow();

        // Fin del tutorial
        if (beatNow < S.energyBeat || !S.requireFromStart) _playedFromStart = true;
        // Solo cuenta como completado si se ha jugado desde el principio, no si se ha empezado en un punto de control posterior.
        if (!_finished && beatNow >= S.endBeat && _mode == Mode.Run && _playedFromStart)
        {
            _finished = true;
            StepsRunning = false;
            TutorialGate.UnlockAll();
            if (!string.IsNullOrEmpty(S.pendingPref)) { PlayerPrefs.SetInt(S.pendingPref, 0); PlayerPrefs.Save(); }   // las explicaciones de una vez siguen pendientes
            S.onCompleted?.Invoke();
            Toast(Loc.T("¡Tutorial completado!", "Tutorial complete!"));
        }

        _actionThisFrame = TutorialKey.None;
        _blockedThisFrame = false;
    }

    // -=+=- Pasos -=+=-

    /// <summary>Indica si este paso frena el juego, lo que solo ocurre la primera vez de cada movimiento (los pasos con explicación).</summary>
    private static bool Pauses(TutorialLevel1Steps.Step s) => !string.IsNullOrEmpty(s.text);

    /// <summary>Indica si la acción hecha vale para la pedida. El doble salto se detecta como un salto más.</summary>
    private static bool Matches(TutorialKey done, TutorialKey wanted)
        => done != TutorialKey.None && (done == wanted || (wanted == TutorialKey.Jump && done == TutorialKey.DoubleJump));

    private bool _groundedLastFrame = true;

    private void LateUpdate()
    {
        if (_pm != null) _groundedLastFrame = _pm.IsGroundedNow || _pm.IsOnZipline;
    }

    // Colores. Las acciones que frenan el juego (la primera vez) van en azul, y las demás, en amarillo.
    private static readonly Color PauseCol = new Color(0.35f, 0.85f, 1f);
    private static readonly Color LiveCol  = new Color(1f, 0.85f, 0.25f);

    /// <summary>
    /// Pasos sin frenada. El aviso ("Pulsa ESPACIO para saltar") aparece arriba mientras la nota cae, y el paso se da por
    /// hecho cuando el jugador hace la acción cerca de su beat (la nota se pone verde) o cuando pasa el beat.
    /// </summary>
    private void UpdateLive(float beatNow)
    {
        var steps = S.steps;
        int i = _nextStep;
        bool live = !_finished && _req == null && i < steps.Length && !Pauses(steps[i]) && !_done[i]
                    && beatNow >= steps[i].beat - NoteLeadBeats;
        if (live && Matches(_actionThisFrame, steps[i].key) && beatNow >= steps[i].beat - SlowLeadBeats)
        {
            CompleteStepIndex(i);
            live = false;
        }
        if (live && beatNow > steps[i].beat + 0.35f) { _done[i] = true; _nextStep++; live = false; }

        if (live && steps[i].key == TutorialKey.None) live = false;
        if (live)
        {
            if (_liveShown != i) { if (S.livePrompts) ShowLive(PromptFor(steps[i].key, steps[i].side)); _liveShown = i; }
        }
        else if (_liveShown >= 0) { _liveShown = -1; HidePanel(); }
    }
    private int _liveShown = -1;

    private void SkipPassedSteps(float beatNow)
    {
        var steps = S.steps;
        while (_nextStep < steps.Length && (_done[_nextStep] || steps[_nextStep].beat < beatNow - (Pauses(steps[_nextStep]) ? 0.05f : 0.4f)))
        {
            if (Pauses(steps[_nextStep])) TutorialGate.Unlock(steps[_nextStep].key);   // por ejemplo, al empezar en un punto de control
            _done[_nextStep] = true;
            _nextStep++;
        }
    }

    private void TryStartNext(float beatNow, float bd, double latency)
    {
        var steps = S.steps;
        bool stepsLeft = !_finished && _nextStep < steps.Length && beatNow < S.endBeat;
        float nextBeat = stepsLeft ? steps[_nextStep].beat : float.MaxValue;

        // 1) La energía se explica antes del primer salto.
        if (S.energyBeat >= 0f && !Shown(0) && !_finished && beatNow >= S.energyBeat - SlowLeadBeats && beatNow < S.energyBeat)
        {
            StartSlow(new Request { once = 0, beat = S.energyBeat, key = TutorialKey.None,
                                    text = TutorialLevel1Steps.EnergyText }, bd, latency);
            return;
        }

        // 2) Las explicaciones por evento (fallo, sangre azul...) salen si el siguiente paso no está demasiado cerca.
        bool energyFirst = S.energyBeat >= 0f && !_finished && !Shown(0) && beatNow < S.energyBeat;   // la energía va antes
        if (_onceQueue.Count > 0 && !energyFirst && nextBeat - beatNow > SlowLeadBeats + ExplainLeadBeat + 0.6f)
        {
            int i = _onceQueue.Dequeue();
            if (!Shown(i))
            {
                string t = i == 1 ? TutorialLevel1Steps.MissText : i == 2 ? TutorialLevel1Steps.BlueText : TutorialLevel1Steps.BlueMissText;
                StartSlow(new Request { once = i, beat = beatNow + ExplainLeadBeat, key = TutorialKey.None, text = t }, bd, latency);
                return;
            }
        }

        // 2b) Explicaciones pedidas desde fuera (en la tirolina, moverse a los lados).
        if (_extraQueue.Count > 0 && nextBeat - beatNow > SlowLeadBeats + ExplainLeadBeat + 0.6f)
        {
            // Se para justo en un beat, como las demás explicaciones del guion.
            float at = Mathf.Ceil(beatNow + ExplainLeadBeat);
            StartSlow(new Request { beat = at, key = TutorialKey.None, text = _extraQueue.Dequeue() }, bd, latency);
            return;
        }

        // 3) Siguiente acción del guion. Solo se frena la primera vez de cada movimiento (los pasos con texto), y el resto
        //    solo muestran el aviso y la nota (ver UpdateLive).
        if (stepsLeft && beatNow >= nextBeat - SlowLeadBeats && Pauses(steps[_nextStep]))
        {
            StartSlow(MakeRequest(_nextStep), bd, latency);
        }
    }

    private Request MakeRequest(int i)
    {
        var s = S.steps[i];
        return new Request
        {
            step = i, beat = s.beat, key = s.key,
            text = Loc.T(s.text, s.en),
            prompt = PromptFor(s.key, s.side),
            highlight = FindHighlight(s),
            look = s.look,
            hud = s.hud,
        };
    }

    // -=+=- Paradas seguidas (por ejemplo, un salto hacia un lado y un dash un beat después) -=+=-
    private const float GlideMaxBeats = 1.6f;    // si la siguiente parada está a menos de esto, no se vuelve a la velocidad normal
    private const float GlideStartSpeed = 0.8f;  // velocidad con la que arranca hacia la siguiente parada

    /// <summary>
    /// Una vez hecha la acción con el juego parado, si la siguiente parada está muy cerca (un dash justo después del
    /// salto), en lugar de volver a la velocidad normal y frenar otra vez enseguida, el juego arranca suave y va frenando
    /// directamente hasta pararse en ella. Devuelve true si lo ha hecho.
    /// </summary>
    private bool GlideToNext()
    {
        if (_finished || _nextStep >= S.steps.Length || !Pauses(S.steps[_nextStep])) return false;
        float bd = _bm.BeatDuration;
        double latency = _bm.SongTimeUnclamped - _bm.CorrectedTimeUnclamped;
        float beatNow = (float)((_songT - latency) / bd);
        float ahead = S.steps[_nextStep].beat - beatNow;
        if (ahead <= 0.05f || ahead > GlideMaxBeats) return false;

        TutorialGate.Frozen = false;
        TutorialGate.Expected = TutorialKey.None;
        HidePanel();
        _dialog?.Hide();
        EndGuide(true);
        SyringeHighlight.Set(SyringeHighlight.Side.None);
        _dimFade = Mathf.Min(_dimFade, 0.6f);
        _speed = GlideStartSpeed;   // StartSlow empieza a esta velocidad y frena hasta pararse en el beat
        StartSlow(MakeRequest(_nextStep), bd, latency);
        return true;
    }

    private void StartSlow(Request req, float bd, double latency)
    {
        _req = req;
        BeginControl();
        _slowTarget = req.beat * bd + latency;            // segundos del reloj en los que cae el beat
        double r = System.Math.Max(0.0005, _slowTarget - _songT);
        float v0 = Mathf.Max(_speed, 0.1f);
        _slowR0 = r / (v0 * v0);                          // para que haya continuidad, empieza a la velocidad actual
        _mode = Mode.Slow;
    }

    private bool _sideJumpSent;   // salto hacia un lado ya pedido en esta parada

    private void Freeze()
    {
        _sideJumpSent = false;
        _mode = Mode.Frozen;
        TutorialGate.Frozen = true;
        // Unos segundos para leer, durante los que la acción no sale (con Expected = None no se puede hacer nada).
        _lockUntil = _req.key != TutorialKey.None && S.lockSeconds > 0f ? Time.unscaledTime + S.lockSeconds : 0f;
        TutorialGate.Expected = _lockUntil > 0f ? TutorialKey.None : _req.key;
        if (_req.key != TutorialKey.None) TutorialGate.Unlock(_req.key);   // a partir de ahora ya está enseñada
        if (_req.step >= 0) S.onExplained?.Invoke(_req.step);
        _pressFrame = -1;
        _failedPresses = 0;
        _liveShown = -1;
        if (_req.once >= 0) MarkShown(_req.once);
        ShowPanel(_req);
        if (_req.look && _req.highlight != null) BeginGuide(_req.highlight);
        // El obstáculo del que se habla se pone azul y brilla mientras dura la explicación (la tirolina ya ilumina su
        // cuerda en BeginGuide).
        if (_req.highlight != null && !string.IsNullOrEmpty(_req.text) && _req.highlight.GetComponentInParent<Zipline>() == null
            && _req.highlight.GetComponentInChildren<Zipline>() == null)
            GlowObstacle(_req.highlight);
        SyringeHighlight.Set(_req.hud);   // la jeringuilla de la que se habla gira y brilla
    }

    /// <summary>
    /// Si el paso que se acaba de hacer tiene una explicación posterior (Step.follow), sale ya, con el juego parado y sin
    /// volver a frenar, por ejemplo al engancharse a la tirolina ("muévete a los lados con A y D"). Devuelve true si la hay.
    /// </summary>
    private bool ChainFollow()
    {
        if (_req == null || _req.step < 0 || _req.step >= S.steps.Length) return false;
        string follow = S.steps[_req.step].follow;
        if (string.IsNullOrEmpty(follow)) return false;
        int step = _req.step;
        EndGuide(true);
        HidePanel();
        _dialog?.Hide();
        TutorialGate.Frozen = false;
        TutorialGate.Expected = TutorialKey.None;
        S.onFollowShown?.Invoke(step);
        // Sale un beat después de la acción (por ejemplo, ya colgado de la tirolina). El juego arranca suave y va frenando
        // hasta pararse en ese beat, sin volver a la velocidad normal entre medias. Si ese beat ya ha pasado, se para en el momento.
        float bd = _bm.BeatDuration;
        double latency = _bm.SongTimeUnclamped - _bm.CorrectedTimeUnclamped;
        float beatNow = (float)((_songT - latency) / bd);
        var req = new Request { beat = S.steps[step].beat + FollowDelayBeats, key = TutorialKey.None, text = follow };
        if (req.beat - beatNow > 0.05f)
        {
            _dimFade = Mathf.Min(_dimFade, 0.6f);
            _speed = GlideStartSpeed;
            StartSlow(req, bd, latency);
        }
        else
        {
            _req = req;
            _speed = 0f;
            Freeze();
        }
        return true;
    }

    private const float FollowDelayBeats = 1f;   // explicación posterior (Step.follow), este tiempo después de la acción

    // -=+=- Mirada guiada y cuerda iluminada (interruptor de mirada y tirolina) -=+=-
    private bool _guiding, _guideReturn;
    private readonly List<(Renderer r, Material[] mats)> _glow = new List<(Renderer, Material[])>();
    private Material _glowMat;

    private void BeginGuide(Transform t)
    {
        Zipline z = t.GetComponentInChildren<Zipline>();
        if (z == null) z = t.GetComponentInParent<Zipline>();
        Vector3 point;
        if (z != null)
        {
            // En la tirolina, la vista se dirige a la cuerda, un poco por delante del jugador, y la cuerda se ilumina.
            float k = Mathf.Clamp01(z.GetT(_pm.transform.position) + 0.12f);
            point = z.GetPosition(k);
            GlowRope(z);
            _guideReturn = true;   // después vuelve a mirar al frente
        }
        else
        {
            Bounds b = new Bounds(t.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            point = b.center;
            _guideReturn = false;   // en el interruptor de mirada hay que seguir mirándolo
        }
        _guiding = true;
        _pm.SetGuideLook(point);
    }

    private void EndGuide(bool returnAfter)
    {
        if (_guiding && _pm != null) _pm.SetGuideLook(null, returnAfter && _guideReturn);
        _guiding = false;
        foreach (var (r, mats) in _glow) if (r != null) r.sharedMaterials = mats;
        _glow.Clear();
    }

    /// <summary>La cuerda de la tirolina (TubeRenderer o "RopeMesh") brilla mientras dura la explicación.</summary>
    private void GlowRope(Zipline z)
    {
        if (_glowMat == null) _glowMat = RuntimeMaterials.Colored(new Color(1f, 0.85f, 0.3f), 4f);
        foreach (Renderer r in z.GetComponentsInChildren<Renderer>(true))
        {
            bool rope = r.GetComponent<TubeRenderer>() != null || r.name.Contains("Rope") || r.name.Contains("Cuerda") || r is LineRenderer;
            if (!rope) continue;
            _glow.Add((r, r.sharedMaterials));
            var m = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < m.Length; i++) m[i] = _glowMat;
            r.sharedMaterials = m;
        }
    }

    /// <summary>La luz de la cuerda late mientras está iluminada.</summary>
    private void PulseGlow()
    {
        if (_glow.Count == 0) return;
        float k = 2.5f + 2f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
        if (_glowMat != null) _glowMat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.3f) * k);
        if (_obstacleMat != null) _obstacleMat.SetColor("_EmissionColor", ObstacleGlow * (0.6f + 0.25f * k));
    }

    // -=+=- Obstáculo resaltado con un material azul brillante solo durante la explicación -=+=-
    private static readonly Color ObstacleGlow = new Color(0.25f, 0.6f, 1f);
    private Material _obstacleMat;

    /// <summary>Pinta de azul, con un brillo que late, el obstáculo de la explicación. EndGuide lo devuelve a como estaba.</summary>
    private void GlowObstacle(Transform t)
    {
        if (_obstacleMat == null) _obstacleMat = RuntimeMaterials.Colored(ObstacleGlow, 1.5f);
        foreach (MeshRenderer r in t.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled) continue;   // las paredes invisibles se quedan invisibles
            _glow.Add((r, r.sharedMaterials));
            var m = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < m.Length; i++) m[i] = _obstacleMat;
            r.sharedMaterials = m;
        }
    }

    private void FrozenInput()
    {
        if (_req.key == TutorialKey.None)
        {
            if (_dialogClosed) { CompleteStep(); BeginResume(); }   // hay que mantener el clic en la caja de texto, o se cierra sola
            return;
        }

        // Tiempo de lectura, durante el que la acción todavía no se puede hacer.
        if (_lockUntil > 0f)
        {
            float left = _dialogClosed ? 0f : _lockUntil - Time.unscaledTime;   // cerrar la caja significa que ya se ha leído
            if (left > 0f)
            {
                _hint.text = Loc.T($"Lee el aviso…  {Mathf.CeilToInt(left)}", $"Read the tip…  {Mathf.CeilToInt(left)}");
                _pressFrame = -1;
                return;
            }
            _lockUntil = 0f;
            TutorialGate.Expected = _req.key;
            _hint.text = Loc.T("¡Ahora!", "Now!");
        }

        // Salto hacia un lado (parte guiada del dash). Solo vale si se salta con la dirección pulsada. Sin ella, el salto
        // no sale (Expected = None) y se avisa al jugador.
        if (_req.key == TutorialKey.Jump && _req.step >= 0 && S.steps[_req.step].side != 0)
        {
            Keybinds.Bind dirBind = S.steps[_req.step].side < 0 ? Keybinds.Bind.Left : Keybinds.Bind.Right;
            bool held = Keybinds.Held(dirBind);
            TutorialGate.Expected = held ? TutorialKey.Jump : TutorialKey.None;
            // El orden da igual, ya que si se pulsó primero el salto y después la dirección, con las dos pulsadas se salta en ese momento.
            if (held && Keybinds.Held(Keybinds.Bind.Jump) && !Keybinds.Pressed(Keybinds.Bind.Jump) && !_sideJumpSent)
            {
                _sideJumpSent = true;
                _pm.BufferJump();
            }
            if (!held)
            {
                if (Keybinds.Pressed(Keybinds.Bind.Jump))
                    _hint.text = Loc.T($"Mantén {Keybinds.Name(dirBind)} pulsado mientras saltas",
                                       $"Hold {Keybinds.Name(dirBind)} while you jump");
                _pressFrame = -1;
                return;
            }
        }

        if (Matches(_actionThisFrame, _req.key)) { CompleteStep(); if (!ChainFollow() && !GlideToNext()) BeginResume(); return; }

        if (Keybinds.Pressed(BindOf(_req.key))) _pressFrame = Time.frameCount;
        if (_blockedThisFrame)
        {
            _pressFrame = -1;
            if (_req.key == TutorialKey.Dash && _req.step >= 0)
            {
                string dir = Keybinds.Name(S.steps[_req.step].side < 0 ? Keybinds.Bind.Left : Keybinds.Bind.Right);
                _hint.text = Loc.T($"Mantén {dir} pulsado mientras pulsas {Keybinds.Name(Keybinds.Bind.Dash)}",
                                   $"Hold {dir} while you press {Keybinds.Name(Keybinds.Bind.Dash)}");
            }
        }
        // Si el jugador ha pulsado la tecla pero la acción no ha salido, no se sigue, ya que chocaría. Se le avisa y, solo
        // después de varios intentos, se continúa igualmente, por si la acción es imposible en ese momento, para no quedarse atascado.
        if (_pressFrame >= 0 && Time.frameCount > _pressFrame + 3)
        {
            _pressFrame = -1;
            _failedPresses++;
            if (_failedPresses >= 4) { CompleteStep(); BeginResume(); return; }
            if (_req.key != TutorialKey.Dash)
                _hint.text = Loc.T("No ha funcionado: vuelve a pulsarla", "That didn't work: press it again");
        }
    }

    private void CompleteStep()
    {
        if (_req == null || _req.step < 0) return;
        CompleteStepIndex(_req.step);
    }

    private void CompleteStepIndex(int i)
    {
        _done[i] = true;
        _lane.Hit(i);
        if (i == _nextStep) _nextStep++;
    }

    private void BeginResume()
    {
        TutorialGate.Frozen = false;
        TutorialGate.Expected = TutorialKey.None;
        HidePanel();
        _dialog?.Hide();
        EndGuide(true);
        SyringeHighlight.Set(SyringeHighlight.Side.None);
        _resumeT = _speed * ResumeSeconds;   // si venía frenando, sigue desde su velocidad
        _mode = Mode.Resume;
        _req = null;
    }

    // -=+=- Reloj -=+=-

    private static double AudioPos(AudioSource s) => s != null && s.clip != null ? s.timeSamples / (double)s.clip.frequency : 0.0;

    /// <summary>Media del desfase entre el reloj y la posición real de la música, que se mide jugando a velocidad normal.</summary>
    private void MeasureAudioOffset()
    {
        AudioSource s = _bm.CurrentSource;
        if (s == null || s.clip == null || s.pitch != 1f || _bm.ClockSeconds < 0.5) return;
        double x = _bm.ClockSeconds - AudioPos(s);
        // Si la canción está en bucle, al dar la vuelta la posición vuelve a 0, así que se cuenta en la misma vuelta que la media.
        if (s.loop && _audioKn > 0) x += s.clip.length * System.Math.Round((_audioK - x) / s.clip.length);
        _audioKn = Mathf.Min(_audioKn + 1, 600);
        _audioK += (x - _audioK) / _audioKn;
    }

    // -=+=- Corrección del desfase después de una parada -=+=-
    // Al cambiar el pitch, la música no avanza exactamente lo que indica el reloj, ya que el audio aplica el pitch por
    // bloques. La resincronización de EndControl usa una sola lectura de la posición de la música, que avanza a saltos de
    // un bloque de audio, por lo que, si la parada fue corta o hubo dos seguidas, el reloj podía quedar unos milisegundos
    // desfasado y el metrónomo dejaba de coincidir con la canción. Por eso, durante unos segundos después de cada parada
    // se compara el reloj con la música (con la mediana de muchas lecturas) y se corrige. Esas lecturas no entran en la
    // media normal (_audioK), para no alterarla con el desfase.
    private const float DriftWatchSeconds = 3f;
    private const int DriftSamples = 24;
    private const double DriftTolerance = 0.006;   // segundos
    private float _driftWatchUntil;
    private bool _sawMusic;
    private const float StartWatchSeconds = 5f;
    private readonly List<double> _drift = new List<double>();

    private void CheckDrift()
    {
        AudioSource s = _bm.CurrentSource;
        if (_audioKn <= 10 || s == null || s.clip == null || s.pitch != 1f || !s.isPlaying) return;   // sin una medida fiable, no se hace nada
        double x = _bm.ClockSeconds - AudioPos(s);
        if (s.loop) x += s.clip.length * System.Math.Round((_audioK - x) / s.clip.length);
        _drift.Add(x);
        if (_drift.Count < DriftSamples) return;
        _drift.Sort();
        double d = _drift[_drift.Count / 2] - _audioK;   // la mediana elimina los saltos de bloque del audio
        _drift.Clear();
        if (System.Math.Abs(d) < DriftTolerance) return;
        // Se mueve la música, no el reloj, ya que el reloj es el que manda sobre todo lo demás (dónde está el jugador,
        // cuándo llegan las acciones, el punto de mira...). Si se moviera el reloj y la música hubiera empezado tarde, el
        // jugador quedaría descolocado respecto a los obstáculos.
        double target = _bm.ClockSeconds - _audioK;
        double len = s.clip.length;
        if (s.loop && len > 0.5) target -= len * System.Math.Floor(target / len);
        int samples = (int)System.Math.Round(target * s.clip.frequency);
        if (samples < 0 || samples >= s.clip.samples) return;
        s.timeSamples = samples;
        Debug.Log($"[Tutorial] Música recolocada con el reloj ({d * 1000.0:0} ms).");
    }

    private void BeginControl()
    {
        if (_controlling) return;
        RefreshRewindables();
        _controlling = true;
        _bm.HoldClock(true);   // el reloj queda fijo durante el fotograma, para que no haya temblores
        _songT = _bm.ClockSeconds;
        _lastDsp = AudioSettings.dspTime;
        _speed = 1f;
        TutorialGate.Busy = true;
    }

    private void TickClock()
    {
        if (!_controlling) return;
        double dsp = AudioSettings.dspTime;
        double dd = System.Math.Max(0.0, dsp - _lastDsp);
        _lastDsp = dsp;
        if (_mode == Mode.Slow)
        {
            // La frenada tiene una velocidad proporcional a la raíz de la distancia, para llegar a la parada justo en el beat.
            double r = _slowTarget - _songT;
            double v = r <= 0 ? 0 : System.Math.Min(1.0, System.Math.Sqrt(r / _slowR0));
            _songT = System.Math.Min(_slowTarget, _songT + dd * v);
        }
        else if (_mode == Mode.Resume) _songT += dd * _speed;
    }

    private void ApplySpeed()
    {
        if (!_controlling) return;
        if (!float.IsFinite(_speed)) _speed = 0f;   // nunca se pasa un valor no válido al motor (tiempo y pitch)
        _bm.SetClockSongTime(_songT);
        Time.timeScale = _speed;
        AudioSource s = _bm.CurrentSource;
        if (s != null) s.pitch = _speed;   // con pitch 0 la música está parada pero "sonando", así que el resto del juego no nota nada
        SlowLoopingSounds();
        // Física a cámara lenta. Con el paso fijo normal, a velocidad 0,1 la física solo daría un paso cada muchos
        // fotogramas y el kayak (y lo que lleva) iría a saltos, por lo que el paso fijo se reduce con la velocidad.
        if (_baseFixedDelta < 0f) _baseFixedDelta = Time.fixedDeltaTime;
        if (_speed > 0f) Time.fixedDeltaTime = _baseFixedDelta * Mathf.Max(0.05f, _speed);
        SyncRewindables();
    }
    private float _baseFixedDelta = -1f;

    private void RestoreFixedDelta() { if (_baseFixedDelta > 0f) Time.fixedDeltaTime = _baseFixedDelta; }

    // -=+=- Efectos que suenan mientras se frena (tirolina, kayak...), también a cámara lenta -=+=-
    // Time.timeScale no afecta al audio, así que los sonidos en bucle que suenan mientras se frena (como el
    // deslizamiento de la tirolina) se ralentizan con el pitch, igual que la música. Al terminar, vuelven a su pitch.
    private readonly Dictionary<AudioSource, float> _slowedPitch = new Dictionary<AudioSource, float>();

    private void SlowLoopingSounds()
    {
        AudioSource music = _bm != null ? _bm.CurrentSource : null;
        // La lista de fuentes se revisa cada cuarto de segundo, no en cada fotograma, ya que buscar en toda la escena es costoso.
        if (Time.unscaledTime >= _nextSoundScan)
        {
            _nextSoundScan = Time.unscaledTime + 0.25f;
            ScanSounds(music);
        }
        foreach (var kv in _slowedPitch)
            if (kv.Key != null) kv.Key.pitch = SfxPitch(kv.Value);
    }

    /// <summary>
    /// Pitch de un efecto a cámara lenta, que nunca es 0 (como mínimo 0,05) ni un valor no válido, ya que con pitch 0
    /// algunos efectos (por ejemplo, los programados con PlayScheduled) podían hacer que el motor de audio cerrase el juego en la build.
    /// </summary>
    private float SfxPitch(float basePitch)
    {
        float p = basePitch * Mathf.Clamp01(float.IsFinite(_speed) ? _speed : 0f);
        if (!float.IsFinite(p)) p = 1f;
        return Mathf.Sign(p == 0f ? 1f : p) * Mathf.Max(0.05f, Mathf.Abs(p));
    }

    private float _nextSoundScan;

    private void ScanSounds(AudioSource music)
    {
        foreach (AudioSource a in FindObjectsByType<AudioSource>())
        {
            // Todos los efectos que estén sonando, ya que el deslizamiento de la tirolina puede no estar marcado como bucle.
            if (a == null || a == music || !a.isPlaying || a.ignoreListenerPause) continue;
            // No se cambian los que todavía no han empezado (programados con PlayScheduled, como los tics del metrónomo) ni
            // los sonidos cortos, ya que cambiarles el pitch no aporta nada y, con los programados, es arriesgado.
            if (a.clip == null || a.clip.length < 0.3f || a.timeSamples == 0) continue;
            // La música del nivel y la de las salas no se cambian.
            if (a.GetComponentInParent<BeatManager>() != null || a.GetComponentInParent<LobbyMusic>() != null) continue;
            if (!_slowedPitch.ContainsKey(a)) _slowedPitch[a] = a.pitch;
        }
        foreach (var kv in _slowedPitch)
            if (kv.Key != null) kv.Key.pitch = SfxPitch(kv.Value);
    }

    private void RestoreLoopingSounds()
    {
        foreach (var kv in _slowedPitch) if (kv.Key != null) kv.Key.pitch = kv.Value;
        _slowedPitch.Clear();
    }

    // -=+=- Obstáculos que calculan sus instantes con el reloj (bloques que caen, osciladores, bolas de fuego...) -=+=-
    // Guardan instantes DSP calculados al empezar, por lo que, al frenar el reloj, se quedarían con los antiguos y
    // caerían a velocidad normal. Igual que en el Rebobinado, en cada fotograma se les pide que se recoloquen con el reloj
    // actual (OnTimeRewind da el mismo resultado aunque se llame varias veces). El reloj del tutorial solo avanza, así que
    // nada vuelve atrás.
    private readonly List<IRewindable> _rewindables = new List<IRewindable>();

    private void RefreshRewindables()
    {
        _rewindables.Clear();
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            // Los bloques rompibles y las monedas solo se deshacen al ir hacia atrás, por lo que aquí no hacen falta, y un
            // pequeño reajuste del reloj al resincronizar podría devolver un bloque ya roto.
            if (mb is IRewindable r && !(mb is BreakableBlock) && !(mb is Coin) && mb.gameObject.scene.IsValid()) _rewindables.Add(r);
    }

    private void SyncRewindables()
    {
        foreach (IRewindable r in _rewindables)
        {
            if (r is Object o && o == null) continue;
            try { r.OnTimeRewind(); }
            catch (System.Exception ex) { Debug.LogException(ex); }
        }
    }

    private void EndControl()
    {
        if (!_controlling) return;
        _controlling = false;
        _speed = 1f;
        Time.timeScale = 1f;
        AudioSource s = _bm.CurrentSource;
        if (s != null) s.pitch = 1f;
        RestoreLoopingSounds();
        RestoreFixedDelta();
        _nextSoundScan = 0f;
        // Se vuelve a sincronizar el reloj con la posición real de la música, corrigiendo lo que haya derivado al cambiar
        // el pitch. Solo se hace con el desfase normal ya medido, ya que suponerlo 0 movería el reloj unos 110 ms sin motivo.
        if (_audioKn > 10 && s != null && s.clip != null)
        {
            double t = AudioPos(s) + _audioK;
            if (s.loop) t += s.clip.length * System.Math.Round((_songT - t) / s.clip.length);   // misma vuelta del bucle
            _bm.SetClockSongTime(t);
        }
        SyncRewindables();   // para que los obstáculos usen ya el reloj resincronizado
        _bm.HoldClock(false);
        _drift.Clear();
        _driftWatchUntil = Time.unscaledTime + DriftWatchSeconds;   // y se vigila durante un rato (ver CheckDrift)
        TutorialGate.Busy = false;
        TutorialGate.Frozen = false;
    }

    // -=+=- Textos -=+=-

    public static Keybinds.Bind BindOf(TutorialKey k) => k switch
    {
        TutorialKey.Jump    => Keybinds.Bind.Jump,
        TutorialKey.DoubleJump => Keybinds.Bind.Jump,
        TutorialKey.Dash    => Keybinds.Bind.Dash,
        TutorialKey.Slide   => Keybinds.Bind.Slide,
        TutorialKey.Slam    => Keybinds.Bind.Slam,
        TutorialKey.Zipline => Keybinds.Bind.Zipline,
        _                   => Keybinds.Bind.Block,
    };

    private static string PromptFor(TutorialKey k, int side)
    {
        string K(Keybinds.Bind b) => Keybinds.Name(b);
        switch (k)
        {
            case TutorialKey.Jump:
                if (side != 0)
                {
                    // Salto hacia un lado (dash). Hay que mantener la dirección al saltar.
                    string d = K(side < 0 ? Keybinds.Bind.Left : Keybinds.Bind.Right);
                    string es = side < 0 ? "a la izquierda" : "a la derecha", en = side < 0 ? "left" : "right";
                    return Loc.T($"Mantén {d} y pulsa {K(Keybinds.Bind.Jump)}: salta {es}", $"Hold {d} and press {K(Keybinds.Bind.Jump)}: jump {en}");
                }
                return Loc.T($"Pulsa {K(Keybinds.Bind.Jump)} para saltar", $"Press {K(Keybinds.Bind.Jump)} to jump");
            case TutorialKey.DoubleJump: return Loc.T($"Pulsa {K(Keybinds.Bind.Jump)} en el aire: doble salto", $"Press {K(Keybinds.Bind.Jump)} in the air: double jump");
            case TutorialKey.Slide:   return Loc.T($"Pulsa {K(Keybinds.Bind.Slide)} para deslizarte", $"Press {K(Keybinds.Bind.Slide)} to slide");
            case TutorialKey.Slam:    return Loc.T($"Pulsa {K(Keybinds.Bind.Slam)} para caer en picado", $"Press {K(Keybinds.Bind.Slam)} to slam down");
            case TutorialKey.Zipline: return Loc.T($"Pulsa {K(Keybinds.Bind.Zipline)} para engancharte", $"Press {K(Keybinds.Bind.Zipline)} to grab on");
            case TutorialKey.Block:   return Loc.T($"Pulsa {K(Keybinds.Bind.Block)} para romperla", $"Press {K(Keybinds.Bind.Block)} to break it");
            case TutorialKey.Dash:
            {
                string dir = K(side < 0 ? Keybinds.Bind.Left : Keybinds.Bind.Right);
                string es = side < 0 ? "a la izquierda" : "a la derecha", en = side < 0 ? "left" : "right";
                return Loc.T($"Mantén {dir} y pulsa {K(Keybinds.Bind.Dash)}: dash {es}", $"Hold {dir} and press {K(Keybinds.Bind.Dash)}: dash {en}");
            }
        }
        return "";
    }

    public static string NoteLabel(TutorialKey k, int side)
    {
        string key = Keybinds.Name(BindOf(k));
        if (k == TutorialKey.Dash) key = Keybinds.Name(side < 0 ? Keybinds.Bind.Left : Keybinds.Bind.Right) + "+" + key;
        // Salto hacia un lado (antes del dash): ESPACIO + A (izquierda) o ESPACIO + D (derecha).
        else if (k == TutorialKey.Jump && side != 0) key = key + "+" + Keybinds.Name(side < 0 ? Keybinds.Bind.Left : Keybinds.Bind.Right);
        return key;
    }

    // -=+=- Objeto iluminado -=+=-

    private Transform FindHighlight(TutorialLevel1Steps.Step s)
    {
        if (s.target != null) return s.target;
        if (!string.IsNullOrEmpty(s.highlight))
        {
            GameObject go = GameObject.Find(s.highlight);
            if (go != null) return go.transform;
        }
        Vector3 p = _pm.transform.position;
        if (s.key == TutorialKey.Zipline)
        {
            Zipline best = null; float bd = float.MaxValue;
            foreach (Zipline z in FindObjectsByType<Zipline>())
            {
                float d = (z.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = z; }
            }
            return best != null ? best.transform : null;
        }
        // Sin ruta, se usa el obstáculo más cercano que tiene delante.
        int layer = LayerMask.NameToLayer("Obstacle");
        if (layer < 0) return null;
        Collider bestC = null; float bestS = float.MaxValue;
        foreach (Collider h in Physics.OverlapBox(p + Vector3.forward * 12f, new Vector3(6f, 8f, 12f), Quaternion.identity, 1 << layer, QueryTriggerInteraction.Collide))
        {
            Bounds b = h.bounds;
            if (b.max.z < p.z) continue;
            float score = Mathf.Max(0f, b.min.z - p.z) + Mathf.Max(0f, Mathf.Abs(b.center.x - p.x) - b.extents.x) * 3f;
            if (score < bestS) { bestS = score; bestC = h; }
        }
        return bestC != null ? bestC.transform : null;
    }

    /// <summary>Rectángulo en pantalla (en píxeles) que ocupan las mallas del objeto, o false si no se ve.</summary>
    private static bool ScreenRect(Transform t, out Rect rect)
    {
        rect = default;
        Camera cam = Camera.main;
        if (t == null || cam == null) return false;
        bool any = false; Bounds b = default;
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any)
        {
            Collider c = t.GetComponentInChildren<Collider>();
            if (c == null) return false;
            b = c.bounds;
        }
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        int front = 0;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z);
            Vector3 sp = cam.WorldToScreenPoint(corner);
            if (sp.z <= 0.05f) continue;
            front++;
            min = Vector2.Min(min, sp); max = Vector2.Max(max, sp);
        }
        if (front == 0) return false;
        min = Vector2.Max(min, Vector2.zero);
        max = Vector2.Min(max, new Vector2(Screen.width, Screen.height));
        if (max.x <= min.x || max.y <= min.y) return false;
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    // -=+=- Interfaz -=+=-

    private void BuildUI()
    {
        Canvas c = UIKit.CreateCanvas("TutorialOverlay", 700, transform);
        _canvasRt = (RectTransform)c.transform;
        _lane = new HeartMonitorNotes(_canvasRt);

        _dimTex = new Texture2D(DimW, DimH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        _dimPx = new Color32[DimW * DimH];
        var dimGo = new GameObject("Oscurecer", typeof(RectTransform), typeof(RawImage));
        dimGo.transform.SetParent(c.transform, false);
        _dim = dimGo.GetComponent<RawImage>();
        _dim.texture = _dimTex;
        _dim.raycastTarget = false;
        Stretch(_dim.rectTransform, 0f, 0f, 0f, 0f);
        _dim.enabled = false;

        var panel = Box("Aviso", c.transform, new Color(0f, 0f, 0f, 0.55f));
        panel.raycastTarget = false;
        Place(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1300f, 250f), new Vector2(0.5f, 1f));
        _panel = panel.gameObject;

        _text = Lbl("Texto", panel.transform, "", FontMid + 8f, TextAlignmentOptions.Center);
        _text.textWrappingMode = TextWrappingModes.Normal;
        _text.enableAutoSizing = true; _text.fontSizeMin = 18f; _text.fontSizeMax = FontMid + 8f;   // para que quepan los textos largos
        Place(_text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(1240f, 120f), new Vector2(0.5f, 1f));

        _prompt = Lbl("Accion", panel.transform, "", FontBig + 10f, TextAlignmentOptions.Center);
        _prompt.color = LiveCol;
        Place(_prompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(1240f, 70f), new Vector2(0.5f, 0f));

        _hint = Lbl("Pista", panel.transform, "", FontSmall + 2f, TextAlignmentOptions.Center);
        _hint.color = DimText;
        Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(1240f, 34f), new Vector2(0.5f, 0f));

        _panel.SetActive(false);
        _dialog = DialogBox.Create(c.transform);
    }

    /// <summary>
    /// Aviso al pararse. La explicación va en la caja de texto de abajo, letra a letra (un clic la completa y mantenerlo la
    /// cierra) y, si se pide una acción, la tecla va arriba, con la cuenta atrás de lectura. En las explicaciones sin
    /// acción, el juego sigue al cerrar la caja, y en las que tienen acción, cerrarla termina la espera de lectura.
    /// </summary>
    private void ShowPanel(Request r)
    {
        _toastT = 0f;
        _dialogClosed = false;
        bool hasText = !string.IsNullOrEmpty(r.text);
        if (hasText || r.key == TutorialKey.None)
            _dialog.Show("", Keybinds.Fix(r.text ?? ""), true, () => _dialogClosed = true, autoClose: false);   // parado, hasta que se llene el aro
        if (r.key == TutorialKey.None) { _panel.SetActive(false); return; }
        _panel.SetActive(true);
        _text.text = "";
        _prompt.text = r.prompt;
        _prompt.color = PauseCol;
        _hint.text = "";
        ((RectTransform)_panel.transform).sizeDelta = new Vector2(1300f, 140f);
    }

    private DialogBox _dialog;
    private bool _dialogClosed;

    private void HidePanel() { if (_toastT <= 0f && _panel != null) _panel.SetActive(false); }

    /// <summary>Aviso sin frenar, que solo muestra la acción en una franja más pequeña.</summary>
    private void ShowLive(string prompt)
    {
        if (_toastT > 0f) return;
        _panel.SetActive(true);
        _text.text = ""; _hint.text = "";
        _prompt.text = prompt;
        _prompt.color = LiveCol;
        ((RectTransform)_panel.transform).sizeDelta = new Vector2(1300f, 140f);
    }

    private void Toast(string msg)
    {
        _panel.SetActive(true);
        _text.text = ""; _hint.text = "";
        _prompt.text = msg;
        _prompt.color = LiveCol;
        ((RectTransform)_panel.transform).sizeDelta = new Vector2(1300f, 140f);
        _toastT = 2.5f;
    }

    private void UpdateToast()
    {
        if (_toastT <= 0f) return;
        _toastT -= Time.unscaledDeltaTime;
        if (_toastT <= 0f && _mode != Mode.Frozen) _panel.SetActive(false);
    }

    // -=+=- Oscurecer la pantalla con "agujeros" de luz (obstáculo y monitor del corazón) -=+=-

    private readonly List<Rect> _holes = new List<Rect>();
    private Request _dimReq;

    private void UpdateDim()
    {
        if (_dim == null) return;
        // En la explicación de un elemento del HUD no se oscurece la pantalla, y SyringeHighlight lo resalta con brillo, latido y una flecha.
        bool hudExplain = (_req ?? _dimReq) != null && (_req ?? _dimReq).hud != SyringeHighlight.Side.None;
        bool show = _dimFade > 0.01f && !HudHider.IsHidden && !hudExplain;
        if (_dim.enabled != show) _dim.enabled = show;
        if (!show) { if (_req == null) _dimReq = null; return; }

        if (_req != null) _dimReq = _req;   // al reanudar, la luz se sigue viendo mientras la pantalla se aclara
        Request q = _dimReq;
        _holes.Clear();
        if (q != null && q.highlight != null && ScreenRect(q.highlight, out Rect r)) _holes.Add(Pad(r, 0.35f, 40f));
        if (q != null && q.step >= 0 && _lane.ScreenRect(out Rect ecg)) _holes.Add(Pad(ecg, 0.25f, 30f));

        float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f);
        byte baseA = (byte)(255f * DimAlpha * Mathf.Clamp01(_dimFade));
        float sx = Screen.width / (float)DimW, sy = Screen.height / (float)DimH;
        for (int y = 0; y < DimH; y++)
        for (int x = 0; x < DimW; x++)
        {
            float px = (x + 0.5f) * sx, py = (y + 0.5f) * sy;
            float light = 0f;
            foreach (Rect h in _holes)
            {
                float dx = (px - h.center.x) / (h.width * 0.5f * pulse);
                float dy = (py - h.center.y) / (h.height * 0.5f * pulse);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                light = Mathf.Max(light, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1.1f, d)));
            }
            _dimPx[y * DimW + x] = new Color32(0, 0, 0, (byte)(baseA * (1f - light)));
        }
        _dimTex.SetPixels32(_dimPx);
        _dimTex.Apply(false);
    }

    private static Rect Pad(Rect r, float frac, float minPx)
    {
        float px = Mathf.Max(minPx, r.width * frac), py = Mathf.Max(minPx, r.height * frac);
        return Rect.MinMaxRect(r.xMin - px, r.yMin - py, r.xMax + px, r.yMax + py);
    }

    // -=+=- Notas en el monitor del corazón (ver HeartMonitorNotes) -=+=-

    private void UpdateNotes(float beatNow)
    {
        var steps = S.steps;
        if (!_finished)
            for (int i = _nextStep; i < steps.Length && steps[i].beat <= beatNow + HeartMonitorNotes.LeadBeats; i++)
                if (!_done[i] && !_lane.Has(i) && steps[i].key != TutorialKey.None)
                    _lane.Spawn(i, steps[i].beat, NoteLabel(steps[i].key, steps[i].side), Pauses(steps[i]) ? PauseCol : LiveCol, 1);
        foreach (int id in new List<int>(_lane.Ids)) if (_done[id]) _lane.Fade(id);   // las acertadas ya están en verde
        _lane.Update(beatNow);
    }
}
