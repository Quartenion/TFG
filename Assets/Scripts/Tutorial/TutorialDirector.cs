using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// MODO TUTORIAL DEL NIVEL 1 (se crea solo en Level1 la primera vez que se juega, o si se activa en Ajustes).
///
/// Hasta el beat <see cref="TutorialLevel1Steps.EndBeat"/>, antes de CADA acción del guion
/// (<see cref="TutorialLevel1Steps.Steps"/>):
///   1. La acción cae como una NOTA en el monitor del corazón (llega justo en su beat).
///   2. Un beat antes, el jugador y la música se van FRENANDO hasta pararse justo en ese beat.
///   3. La pantalla se oscurece, se ILUMINA el obstáculo y sale el texto («Pulsa ESPACIO para saltar»).
///   4. El jugador hace la acción (ese instante, en el beat) y todo vuelve a arrancar poco a poco.
/// El movimiento lateral es libre siempre. Si el jugador hace la acción por su cuenta mientras frena, no se para.
///
/// Explicaciones que salen UNA SOLA VEZ (se recuerdan aunque mueras): la energía (al empezar), el primer clic fallado,
/// la primera vez que entra sangre azul y el primer fallo con sangre azul.
///
/// CÓMO SE FRENA EL TIEMPO: el reloj del BeatManager (todo el nivel depende de él) avanza a velocidad 'speed'
/// (1 = normal, 0 = parado), Time.timeScale = speed (jugador, físicas, animaciones) y el pitch de la música = speed.
/// Al volver a 1 se resincroniza el reloj con la posición real de la música.
/// </summary>
public class TutorialDirector : MonoBehaviour
{
    // ── Preferencias ──────────────────────────────────────────────────────────
    private const string PrefPending = "Tutorial.L1";          // 1 = el tutorial se hará la próxima vez
    private static readonly string[] OncePrefs = { "Tutorial.Energy", "Tutorial.Miss", "Tutorial.Blue", "Tutorial.BlueMiss" };
    private const string SceneName = "Level1";

    /// <summary>¿Se hará el tutorial al entrar en el nivel 1? (Ajustes → TUTORIAL). Al activarlo se repite todo.</summary>
    public static bool Pending
    {
        get => PlayerPrefs.GetInt(PrefPending, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(PrefPending, value ? 1 : 0);
            if (value) foreach (string k in OncePrefs) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }
    }

    /// <summary>¿Está en marcha el guion del tutorial (antes del beat final)? Las pistas no repiten sus notas.</summary>
    public static bool StepsRunning { get; private set; }

    private static bool Shown(int i) => PlayerPrefs.GetInt(OncePrefs[i], 0) == 1;
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
        if (s.name != SceneName || StudySession.Active) return;
        // Interruptor en la escena (objeto «Tutorial»): apagado → nada; «Forzar siempre» → tutorial completo.
        TutorialSwitch sw = FindAnyObjectByType<TutorialSwitch>(FindObjectsInactive.Include);
        if (sw != null && !sw.tutorialActivo) return;
        if (sw != null && sw.forzarSiempre) Pending = true;
        // Se crea si toca el tutorial o si queda alguna explicación de una vez por salir (fallo, sangre azul…).
        bool onceLeft = !Shown(1) || !Shown(2) || !Shown(3);
        if (!Pending && !onceLeft) return;
        if (FindAnyObjectByType<TutorialDirector>() != null) return;
        new GameObject("TutorialDirector").AddComponent<TutorialDirector>();
    }

    // ── Ajustes del efecto ───────────────────────────────────────────────────
    private const float SlowLeadBeats   = 1f;     // empieza a frenar 1 beat antes de la acción
    private const float NoteLeadBeats   = HeartMonitorNotes.LeadBeats;   // la nota aparece 3 beats antes
    private const float ExplainLeadBeat = 0.4f;   // explicaciones por evento: frenada corta
    private const float ResumeSeconds   = 0.45f;  // vuelta a velocidad normal (tiempo real)
    private const float DimAlpha        = 0.8f;

    // ── Referencias ──────────────────────────────────────────────────────────
    private BeatManager _bm;
    private PlayerMovement _pm;
    private EnergyManager _em;
    private RhythmInput _rhythm;
    private BlockAbility _block;
    private GameOverManager _gameOver;
    private bool _hooked;

    // ── Control del tiempo ───────────────────────────────────────────────────
    private enum Mode { Run, Slow, Frozen, Resume }
    private Mode _mode = Mode.Run;
    private bool _controlling;
    private double _songT, _lastDsp;      // reloj propio (segundos de canción) mientras se controla
    private double _slowTarget, _slowR0;  // frenada: instante de parada y «distancia» inicial
    private float _speed = 1f, _resumeT;
    private double _audioK; private int _audioKn;   // desfase medio reloj − posición de la música

    // ── Pasos ────────────────────────────────────────────────────────────────
    private class Request
    {
        public int step = -1;          // índice en Steps (−1 = explicación sin acción)
        public int once = -1;          // índice en OncePrefs (explicaciones de una vez)
        public float beat;
        public TutorialKey key;
        public string text, prompt;
        public Transform highlight;
    }
    private Request _req;
    private int _nextStep;
    private readonly bool[] _done = new bool[TutorialLevel1Steps.Steps.Length];
    private readonly Queue<int> _onceQueue = new Queue<int>();
    private bool _finished;
    private bool _playedFromStart;
    private bool _locksApplied;
    private TutorialKey _actionThisFrame = TutorialKey.None;
    private bool _blockedThisFrame;
    private int _pressFrame = -1;
    private int _failedPresses;

    // ── Interfaz ─────────────────────────────────────────────────────────────
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


    // =========================================================================

    private void Start()
    {
        _finished = !Pending;   // (sin tutorial pendiente: solo las explicaciones de una vez)
        StepsRunning = !_finished;
        TutorialGate.UnlockAll();   // (los bloqueos se ponen al empezar el nivel: en la sala de inicio se puede saltar)
        BuildUI();
    }

    private void OnDestroy()
    {
        StepsRunning = false;
        TutorialGate.UnlockAll();
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

        Ensure(ref _pm.OnJumpExecuted).AddListener(() => Act(TutorialKey.Jump));
        Ensure(ref _pm.OnWalljumped).AddListener(() => Act(TutorialKey.Jump));
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
            if (_em.OnBlueInjection == null) _em.OnBlueInjection = new UnityEngine.Events.UnityEvent<float>();
            _em.OnBlueInjection.AddListener(_ => QueueOnce(3));
        }
    }

    private static UnityEngine.Events.UnityEvent Ensure(ref UnityEngine.Events.UnityEvent e)
        => e ??= new UnityEngine.Events.UnityEvent();

    private void Act(TutorialKey k) => _actionThisFrame = k;

    private void QueueOnce(int i)
    {
        if (Shown(i) || _onceQueue.Contains(i)) return;
        if (_req != null && _req.once == i) return;
        _onceQueue.Enqueue(i);
    }

    // =========================================================================
    // BUCLE
    // =========================================================================

    private void Update()
    {
        Hook();
        UpdateToast();
        if (!_hooked || _bm == null) { _actionThisFrame = TutorialKey.None; return; }

        // Muerto: soltar el control (el game over gestiona el tiempo y la escena se recarga).
        if (_gameOver != null && _gameOver.IsDead)
        {
            if (_controlling) { _controlling = false; TutorialGate.Frozen = false; TutorialGate.Busy = false; _bm.HoldClock(false); }
            _mode = Mode.Run; HidePanel(); _dimFade = 0f; UpdateDim();
            _actionThisFrame = TutorialKey.None;
            return;
        }
        if (PauseMenuManager.IsPaused || !_bm.HasClock || _pm.IsFreeroam) { _actionThisFrame = TutorialKey.None; return; }

        // Al empezar el nivel: las acciones que aún no se han enseñado quedan bloqueadas hasta su primera explicación.
        if (!_locksApplied)
        {
            _locksApplied = true;
            if (!_finished)
                foreach (var st in TutorialLevel1Steps.Steps)
                    if (Pauses(st)) TutorialGate.Lock(st.key);
        }

        float bd = _bm.BeatDuration;
        double latency = _bm.SongTimeUnclamped - _bm.CorrectedTimeUnclamped;   // (compensación de latencia)
        if (_mode == Mode.Run && _bm.IsPlaying && !_controlling) MeasureAudioOffset();

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
                    // Lo ha hecho él solo mientras frenaba: no hace falta parar.
                    CompleteStep();
                    BeginResume();
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

        // Fin del tutorial.
        if (beatNow < TutorialLevel1Steps.EnergyBeat) _playedFromStart = true;
        // (Solo cuenta como completado si se ha jugado desde el principio: empezando en un checkpoint pasado el
        //  beat 192 antes se daba por hecho sin haberlo visto.)
        if (!_finished && beatNow >= TutorialLevel1Steps.EndBeat && _mode == Mode.Run && _playedFromStart)
        {
            _finished = true;
            StepsRunning = false;
            TutorialGate.UnlockAll();
            Pending = false;   // (las explicaciones de una vez que no hayan salido siguen pendientes)
            Toast(Loc.T("¡Tutorial completado!", "Tutorial complete!"));
        }

        _actionThisFrame = TutorialKey.None;
        _blockedThisFrame = false;
    }

    // ── Pasos ────────────────────────────────────────────────────────────────

    /// <summary>¿Este paso frena el juego? Solo la primera vez de cada movimiento (los que llevan explicación).</summary>
    private static bool Pauses(TutorialLevel1Steps.Step s) => !string.IsNullOrEmpty(s.text);

    /// <summary>¿La acción hecha vale para la pedida? (el doble salto se detecta como un salto más)</summary>
    private static bool Matches(TutorialKey done, TutorialKey wanted)
        => done != TutorialKey.None && (done == wanted || (wanted == TutorialKey.DoubleJump && done == TutorialKey.Jump));

    // Colores: las acciones que FRENAN el juego (primera vez) van en azul; las demás, en amarillo.
    private static readonly Color PauseCol = new Color(0.35f, 0.85f, 1f);
    private static readonly Color LiveCol  = new Color(1f, 0.85f, 0.25f);

    /// <summary>
    /// Pasos SIN frenada: el aviso («Pulsa ESPACIO para saltar») sale arriba mientras la nota cae, y el paso se da por
    /// hecho al hacer la acción cerca de su beat (la nota se pone verde) o cuando pasa el beat.
    /// </summary>
    private void UpdateLive(float beatNow)
    {
        var steps = TutorialLevel1Steps.Steps;
        int i = _nextStep;
        bool live = !_finished && _req == null && i < steps.Length && !Pauses(steps[i]) && !_done[i]
                    && beatNow >= steps[i].beat - NoteLeadBeats;
        if (live && Matches(_actionThisFrame, steps[i].key) && beatNow >= steps[i].beat - SlowLeadBeats)
        {
            CompleteStepIndex(i);
            live = false;
        }
        if (live && beatNow > steps[i].beat + 0.35f) { _done[i] = true; _nextStep++; live = false; }

        if (live)
        {
            if (_liveShown != i) { ShowLive(PromptFor(steps[i].key, steps[i].side)); _liveShown = i; }
        }
        else if (_liveShown >= 0) { _liveShown = -1; HidePanel(); }
    }
    private int _liveShown = -1;

    private void SkipPassedSteps(float beatNow)
    {
        var steps = TutorialLevel1Steps.Steps;
        while (_nextStep < steps.Length && (_done[_nextStep] || steps[_nextStep].beat < beatNow - (Pauses(steps[_nextStep]) ? 0.05f : 0.4f)))
        {
            if (Pauses(steps[_nextStep])) TutorialGate.Unlock(steps[_nextStep].key);   // (p. ej. empezando en un checkpoint)
            _done[_nextStep] = true;
            _nextStep++;
        }
    }

    private void TryStartNext(float beatNow, float bd, double latency)
    {
        var steps = TutorialLevel1Steps.Steps;
        bool stepsLeft = !_finished && _nextStep < steps.Length && beatNow < TutorialLevel1Steps.EndBeat;
        float nextBeat = stepsLeft ? steps[_nextStep].beat : float.MaxValue;

        // 1) Energía: antes del primer salto.
        if (!Shown(0) && !_finished && beatNow >= TutorialLevel1Steps.EnergyBeat - SlowLeadBeats && beatNow < TutorialLevel1Steps.EnergyBeat)
        {
            StartSlow(new Request { once = 0, beat = TutorialLevel1Steps.EnergyBeat, key = TutorialKey.None,
                                    text = TutorialLevel1Steps.EnergyText }, bd, latency);
            return;
        }

        // 2) Explicaciones por evento (fallo, sangre azul…): si el siguiente paso no está encima.
        bool energyFirst = !_finished && !Shown(0) && beatNow < TutorialLevel1Steps.EnergyBeat;   // (la energía va antes)
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

        // 3) Siguiente acción del guion: solo se FRENA en la primera vez de cada movimiento (los pasos con texto).
        //    El resto solo muestran el aviso y la nota (ver UpdateLive).
        if (stepsLeft && beatNow >= nextBeat - SlowLeadBeats && Pauses(steps[_nextStep]))
        {
            var s = steps[_nextStep];
            StartSlow(new Request
            {
                step = _nextStep, beat = s.beat, key = s.key,
                text = Loc.T(s.text, s.en),
                prompt = PromptFor(s.key, s.side),
                highlight = FindHighlight(s),
            }, bd, latency);
        }
    }

    private void StartSlow(Request req, float bd, double latency)
    {
        _req = req;
        BeginControl();
        _slowTarget = req.beat * bd + latency;            // (segundos del reloj en los que cae el beat)
        double r = System.Math.Max(0.0005, _slowTarget - _songT);
        float v0 = Mathf.Max(_speed, 0.1f);
        _slowR0 = r / (v0 * v0);                          // continuidad: empieza a la velocidad actual
        _mode = Mode.Slow;
    }

    private void Freeze()
    {
        _mode = Mode.Frozen;
        TutorialGate.Frozen = true;
        TutorialGate.Expected = _req.key;
        if (_req.key != TutorialKey.None) TutorialGate.Unlock(_req.key);   // (desde ahora ya está enseñada)
        _pressFrame = -1;
        _failedPresses = 0;
        _liveShown = -1;
        if (_req.once >= 0) MarkShown(_req.once);
        ShowPanel(_req);
    }

    private void FrozenInput()
    {
        if (_req.key == TutorialKey.None)
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) BeginResume();
            return;
        }

        if (Matches(_actionThisFrame, _req.key)) { CompleteStep(); BeginResume(); return; }

        if (Keybinds.Pressed(BindOf(_req.key))) _pressFrame = Time.frameCount;
        if (_blockedThisFrame)
        {
            _pressFrame = -1;
            if (_req.key == TutorialKey.Dash && _req.step >= 0)
            {
                string dir = Keybinds.Name(TutorialLevel1Steps.Steps[_req.step].side < 0 ? Keybinds.Bind.Left : Keybinds.Bind.Right);
                _hint.text = Loc.T($"Mantén {dir} pulsado mientras pulsas {Keybinds.Name(Keybinds.Bind.Dash)}",
                                   $"Hold {dir} while you press {Keybinds.Name(Keybinds.Bind.Dash)}");
            }
        }
        // Ha pulsado la tecla pero la acción no ha salido: NO se sigue (chocaría). Se avisa, y solo tras varios
        // intentos se continúa igualmente (por si la acción es imposible en ese momento y no quedarse atascado).
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
        _resumeT = _speed * ResumeSeconds;   // (si venía frenando, sigue desde su velocidad)
        _mode = Mode.Resume;
        _req = null;
    }

    // ── Reloj ────────────────────────────────────────────────────────────────

    private static double AudioPos(AudioSource s) => s != null && s.clip != null ? s.timeSamples / (double)s.clip.frequency : 0.0;

    /// <summary>Media del desfase entre el reloj y la posición real de la música (se mide jugando a velocidad normal).</summary>
    private void MeasureAudioOffset()
    {
        AudioSource s = _bm.CurrentSource;
        if (s == null || s.clip == null || s.pitch != 1f || _bm.ClockSeconds < 0.5) return;
        double x = _bm.ClockSeconds - AudioPos(s);
        _audioKn = Mathf.Min(_audioKn + 1, 600);
        _audioK += (x - _audioK) / _audioKn;
    }

    private void BeginControl()
    {
        if (_controlling) return;
        RefreshRewindables();
        _controlling = true;
        _bm.HoldClock(true);   // (reloj fijo durante el frame: sin temblores)
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
            // Frenada «de velocidad ∝ √distancia»: llega a la parada justo en el beat.
            double r = _slowTarget - _songT;
            double v = r <= 0 ? 0 : System.Math.Min(1.0, System.Math.Sqrt(r / _slowR0));
            _songT = System.Math.Min(_slowTarget, _songT + dd * v);
        }
        else if (_mode == Mode.Resume) _songT += dd * _speed;
    }

    private void ApplySpeed()
    {
        if (!_controlling) return;
        _bm.SetClockSongTime(_songT);
        Time.timeScale = _speed;
        AudioSource s = _bm.CurrentSource;
        if (s != null) s.pitch = _speed;   // (pitch 0 = música parada pero «sonando»: el resto del juego no se entera)
        SyncRewindables();
    }

    // ── Obstáculos que calculan sus instantes con el reloj (bloques que caen, osciladores, bolas de fuego…) ──
    // Guardan instantes DSP calculados al empezar; al frenar el reloj se quedarían con los antiguos y caerían a
    // velocidad normal. Igual que hace el Rebobinado, se les pide cada frame que se recoloquen con el reloj actual
    // (OnTimeRewind es idempotente). El reloj del tutorial solo avanza, así que nada «vuelve atrás».
    private readonly List<IRewindable> _rewindables = new List<IRewindable>();

    private void RefreshRewindables()
    {
        _rewindables.Clear();
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            // (Bloques rompibles y monedas solo «deshacen» al ir hacia atrás: aquí no hacen falta, y un pequeño
            //  reajuste del reloj al resincronizar podría devolver un bloque ya roto.)
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
        // Resincroniza el reloj con la posición real de la música (lo que haya derivado al cambiar el pitch).
        if (_audioKn > 10) _bm.SetClockSongTime(AudioPos(s) + _audioK);
        SyncRewindables();   // (que los obstáculos usen ya el reloj resincronizado)
        _bm.HoldClock(false);
        TutorialGate.Busy = false;
        TutorialGate.Frozen = false;
    }

    // ── Textos ───────────────────────────────────────────────────────────────

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
            case TutorialKey.Jump:    return Loc.T($"Pulsa {K(Keybinds.Bind.Jump)} para saltar", $"Press {K(Keybinds.Bind.Jump)} to jump");
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
        return key;
    }

    // ── Objeto iluminado ─────────────────────────────────────────────────────

    private Transform FindHighlight(TutorialLevel1Steps.Step s)
    {
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
        // Sin ruta: el obstáculo más cercano que tiene delante.
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

    /// <summary>Rectángulo en pantalla (píxeles) que ocupa el objeto (sus mallas), o false si no se ve.</summary>
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

    // =========================================================================
    // INTERFAZ
    // =========================================================================

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
        Place(_text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(1240f, 120f), new Vector2(0.5f, 1f));

        _prompt = Lbl("Accion", panel.transform, "", FontBig + 10f, TextAlignmentOptions.Center);
        _prompt.color = LiveCol;
        Place(_prompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(1240f, 70f), new Vector2(0.5f, 0f));

        _hint = Lbl("Pista", panel.transform, "", FontSmall + 2f, TextAlignmentOptions.Center);
        _hint.color = DimText;
        Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(1240f, 34f), new Vector2(0.5f, 0f));

        _panel.SetActive(false);
    }

    private void ShowPanel(Request r)
    {
        _panel.SetActive(true);
        _toastT = 0f;
        bool hasText = !string.IsNullOrEmpty(r.text);
        _text.text = r.text ?? "";
        if (r.key == TutorialKey.None)
        {
            _prompt.text = "";
            _hint.text = Loc.T("Pulsa ENTER para continuar", "Press ENTER to continue");
            _text.rectTransform.sizeDelta = new Vector2(1240f, 190f);
        }
        else
        {
            _prompt.text = r.prompt;
            _prompt.color = PauseCol;
            _hint.text = "";
            _text.rectTransform.sizeDelta = new Vector2(1240f, 120f);
        }
        var prt = (RectTransform)_panel.transform;
        prt.sizeDelta = new Vector2(1300f, r.key == TutorialKey.None ? 260f : hasText ? 250f : 140f);
    }

    private void HidePanel() { if (_toastT <= 0f && _panel != null) _panel.SetActive(false); }

    /// <summary>Aviso sin frenar: solo la acción, en una franja más pequeña.</summary>
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

    // ── Oscurecer con «agujeros» de luz (obstáculo y monitor del corazón) ────

    private readonly List<Rect> _holes = new List<Rect>();
    private Request _dimReq;

    private void UpdateDim()
    {
        if (_dim == null) return;
        bool show = _dimFade > 0.01f && !HudHider.IsHidden;
        if (_dim.enabled != show) _dim.enabled = show;
        if (!show) { if (_req == null) _dimReq = null; return; }

        if (_req != null) _dimReq = _req;   // (al reanudar se sigue viendo la luz mientras se aclara)
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

    // ── Notas en el monitor del corazón (ver HeartMonitorNotes) ─────────────

    private void UpdateNotes(float beatNow)
    {
        var steps = TutorialLevel1Steps.Steps;
        if (!_finished)
            for (int i = _nextStep; i < steps.Length && steps[i].beat <= beatNow + HeartMonitorNotes.LeadBeats; i++)
                if (!_done[i] && !_lane.Has(i))
                    _lane.Spawn(i, steps[i].beat, NoteLabel(steps[i].key, steps[i].side), Pauses(steps[i]) ? PauseCol : LiveCol, 1);
        foreach (int id in new List<int>(_lane.Ids)) if (_done[id]) _lane.Fade(id);   // (las acertadas ya están en verde)
        _lane.Update(beatNow);
    }
}
