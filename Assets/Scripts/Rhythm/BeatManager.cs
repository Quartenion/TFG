using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Gestiona el beat de la musica y evalua la precision de los inputs del jugador.
///
///   0. RELOJ MUSICAL ÚNICO: SongTime / ClockSeconds / ClockBeats salen del reloj de audio (dspTime) y del
///      instante programado (ScheduledStartDSP). Todos los sistemas leen la posición de aquí.
///   1. Compensacion de latencia: CorrectedTime = SongTime - _audioLatencyOffset.
///      Si el jugador siempre llega tarde, aumenta el offset. Si llega pronto, ponlo negativo.
///      Si existe un valor guardado en PlayerPrefs ("AudioLatencyOffset"), se usa ese.
///
///   2. Ventanas de timing: Perfect (+-_perfectWindow) y Good (+-_goodWindow).
///      EvaluateTiming() devuelve Perfect, Good o Miss para escalar energia y mostrar feedback.
///
///   3. OnBeat y OnHalfBeat: UnityEvents directos para conectar PulseToTheBeat u otros
///      efectos desde el Inspector, sin necesitar un Interval con steps=1.
///
///   4. Singleton de acceso global (BeatManager.Instance).
///
///   5. Intervals: UnityEvents cada N pasos de beat (los usa PulseToTheBeat en Managers).
///
///   6. Los eventos de beat no se disparan con Time.timeScale == 0 (pausa).
/// </summary>
public class BeatManager : MonoBehaviour
{
    // Singleton
    public static BeatManager Instance { get; private set; }

    // --- Audio ---
    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private float _bpm = 120f;

    // --- Latencia ---
    [Header("Compensacion de latencia")]
    [Tooltip("Offset en segundos para compensar la latencia del dispositivo de audio. " +
             "Si el jugador siempre llega tarde, aumenta este valor (0.03-0.08). " +
             "Si llega pronto, usa un valor negativo. " +
             "Si hay un valor guardado en PlayerPrefs, se usa ese.")]
    [SerializeField] private float _audioLatencyOffset = 0f;

    [Tooltip("Si esta activo, al iniciar carga el offset guardado en PlayerPrefs por el calibrador.")]
    [SerializeField] private bool _loadOffsetFromPlayerPrefs = true;

    // --- Ventanas de timing ---
    [Header("Ventanas de timing")]
    [Tooltip("Ventana Perfect en segundos (por ejemplo 0.07 = 70ms). Da 1.5x de energia.")]
    [SerializeField] private float _perfectWindow = 0.07f;

    [Tooltip("Ventana Good en segundos (por ejemplo 0.14 = 140ms). Da 1x de energia. " +
             "Fuera de esta ventana = Miss.")]
    [SerializeField] private float _goodWindow = 0.14f;

    // --- Eventos directos de beat ---
    [Header("Eventos de beat (nuevos)")]
    [Tooltip("Se dispara una vez por beat. Conecta PulseToTheBeat.Pulse() aqui.")]
    public UnityEvent OnBeat;

    [Tooltip("Se dispara dos veces por beat (cada corchea). Util para footsteps.")]
    public UnityEvent OnHalfBeat;

    // --- Intervals: eventos cada N pasos de beat ---
    [Header("Intervals")]
    [SerializeField] private Intervals[] _intervals;

    // --- Propiedades publicas ---

    /// <summary>BPM de la cancion.</summary>
    public float Bpm => _bpm;

    /// <summary>Duracion de un beat en segundos.</summary>
    public float BeatDuration => _bpm > 0f ? 60f / _bpm : 1f;

    /// <summary>Duracion de medio beat en segundos.</summary>
    public float HalfBeatDuration => BeatDuration * 0.5f;

    // ── RELOJ MUSICAL ÚNICO ───────────────────────────────────────────────────
    // Todo el juego lee la posición de la canción de aquí. Sale del reloj de audio (AudioSettings.dspTime) y del
    // instante en que se programó la canción (ScheduledStartDSP), así que:
    //   • no acumula error (no suma deltaTime) y avanza suave (no a saltos de buffer como AudioSource.time),
    //   • sigue siendo continuo aunque cambie la pista (relevo nivel → bucle del jefe → final: SwapSource),
    //   • el rebobinado lo mueve con SetRewindClock y StartMusicFromAt lo recoloca al retomar.
    // Antes los obstáculos calculaban (dspTime − ScheduledStartDSP) cada uno por su cuenta y los clics usaban
    // AudioSource.time: eran dos relojes distintos.

    /// <summary>Tempo por defecto si no hay BeatManager (p. ej. gizmos en el editor sin escena cargada).</summary>
    public const float DefaultBpm = 120f;

    /// <summary>¿Hay una canción programada (ScheduledStartDSP válido)?</summary>
    public bool HasClock => ScheduledStartDSP > 0.0;

    /// <summary>
    /// Segundos de canción según el reloj de audio, SIN recortar ni compensar latencia (negativo antes de empezar).
    /// Es la base de todo lo demás. Para programar sonidos/eventos en un beat concreto usa DspAtBeat.
    /// </summary>
    public double ClockSeconds => HasClock ? (_clockHeld ? _heldSongTime : AudioSettings.dspTime - ScheduledStartDSP) : 0.0;

    /// <summary>
    /// «Ahora» en el reloj DSP, coherente con ClockSeconds. Normalmente es AudioSettings.dspTime; mientras el tutorial
    /// controla el reloj (cámara lenta / parado) es ScheduledStartDSP + la posición fijada, para que dspTime (que avanza a
    /// saltos por bloques de audio dentro del mismo frame) no haga temblar a los objetos que se calculan con él.
    /// </summary>
    public static double DspNow
    {
        get
        {
            BeatManager bm = Instance;
            return bm != null && bm._clockHeld ? bm.ScheduledStartDSP + bm._heldSongTime : AudioSettings.dspTime;
        }
    }

    private bool _clockHeld;
    private double _heldSongTime;

    /// <summary>Tutorial: mientras está activo, ClockSeconds y DspNow valen EXACTAMENTE lo fijado con SetClockSongTime.</summary>
    public void HoldClock(bool hold)
    {
        if (hold && !_clockHeld) _heldSongTime = AudioSettings.dspTime - ScheduledStartDSP;
        _clockHeld = hold;
    }

    /// <summary>Posición de la canción en beats (con decimales) según el reloj de audio.</summary>
    public double ClockBeats => BeatDuration > 0f ? ClockSeconds / BeatDuration : 0.0;

    /// <summary>Instante DSP en el que cae el beat 'beat' (0 = inicio de la canción).</summary>
    public double DspAtBeat(double beat) => ScheduledStartDSP + beat * BeatDuration;

    /// <summary>Instante DSP en el que la canción llega al segundo 'seconds'.</summary>
    public double DspAtSongTime(double seconds) => ScheduledStartDSP + seconds;

    private bool  _paused;
    private bool  _stopped;
    private float _pausedSongTime;
    private double _pausedAtDsp;

    /// <summary>
    /// Segundos de canción (sin compensar latencia), ≥ 0. En pausa se congela; tras StopMusic (o sin canción
    /// programada) vale AudioSource.time, como antes. Entre dos pistas (relevo al bucle del jefe) sigue avanzando.
    /// </summary>
    public float SongTime
    {
        get
        {
            if (_audioSource == null) return 0f;
            if (_paused) return _pausedSongTime;
            if (!HasClock || _stopped) return _audioSource.time;
            return Mathf.Max(0f, (float)ClockSeconds);
        }
    }

    /// <summary>
    /// Como SongTime pero SIN recortar a 0: negativo mientras la canción está programada y aún no ha empezado
    /// (p. ej. el beat que separa el ¡YA! del inicio). Lo usan las notas del HUD para llegar justo al beat 0.
    /// </summary>
    public float SongTimeUnclamped => HasClock && !_stopped && !_paused ? (float)ClockSeconds : SongTime;

    /// <summary>Tiempo percibido (con compensación de latencia) sin recortar a 0. Ver SongTimeUnclamped.</summary>
    public float CorrectedTimeUnclamped => SongTimeUnclamped - _audioLatencyOffset;

    /// <summary>Duración de un beat de 'bm' (o del BeatManager de la escena, o a DefaultBpm si no hay ninguno).</summary>
    public static float BeatDurationOf(BeatManager bm) => 60f / BpmOf(bm);

    /// <summary>BPM de 'bm' (o del BeatManager de la escena, o DefaultBpm). Vale también en el editor (gizmos).</summary>
    public static float BpmOf(BeatManager bm)
    {
        if (bm == null) bm = Instance;
#if UNITY_EDITOR
        if (bm == null && !Application.isPlaying) bm = FindAnyObjectByType<BeatManager>();
#endif
        return bm != null && bm.Bpm > 0f ? bm.Bpm : DefaultBpm;
    }

    /// <summary>
    /// Tiempo percibido por el jugador (SongTime - offset de latencia).
    /// Lo usan la evaluación de clics, los eventos de beat y el HUD rítmico.
    /// Todos los calculos de hit detection usan este valor.
    /// </summary>
    public float CorrectedTime => Mathf.Max(0f, SongTime - _audioLatencyOffset);

    /// <summary>Indice del beat actual desde el inicio de la cancion.</summary>
    public int CurrentBeatIndex => BeatDuration > 0f ? Mathf.FloorToInt(CorrectedTime / BeatDuration) : 0;

    /// <summary>Ventana Perfect en segundos.</summary>
    public float PerfectWindow => _perfectWindow * WindowMultiplier * EnergyManager.GraceWindowMultiplier;   // («Respiro»)

    /// <summary>Ventana Good en segundos.</summary>
    public float GoodWindow => _goodWindow * WindowMultiplier * EnergyManager.GraceWindowMultiplier;

    /// <summary>Mejora «Oído fino» de la tienda: +15 % de ventana por nivel (Perfect y Good).</summary>
    public static float WindowMultiplier => 1f + 0.15f * PlayerProgress.GetLevel(ShopIds.WideWindow);

    /// <summary>True si la musica esta sonando.</summary>
    public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;

    /// <summary>AudioSource que el BeatManager usa ahora mismo como reloj (para relevos de pista).</summary>
    public AudioSource CurrentSource => _audioSource;

    // --- Estado interno ---
    private int _lastBeatIndex   = -1;
    private int _lastHalfBeatIdx = -1;

    // --- Unity lifecycle ---

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_loadOffsetFromPlayerPrefs && PlayerPrefs.HasKey("AudioLatencyOffset"))
        {
            _audioLatencyOffset = PlayerPrefs.GetFloat("AudioLatencyOffset");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_audioSource == null || !_audioSource.isPlaying) return;
        if (Time.timeScale == 0f) return;
        // Rebobinando (la canción suena al revés): sin eventos de beat. Si no, cada beat que se "desanda" dispararía
        // OnBeat y el HUD rítmico lanzaría montones de notas a la vez.
        if (TimeRewinder.IsRewinding) return;

        float corrected = CorrectedTime;
        float d = BeatDuration;
        if (d <= 0f) return;

        // Beat entero
        int beatIdx = Mathf.FloorToInt(corrected / d);
        if (beatIdx != _lastBeatIndex)
        {
            _lastBeatIndex = beatIdx;
            OnBeat?.Invoke();
        }

        // Medio beat
        float halfD = HalfBeatDuration;
        if (halfD > 0f)
        {
            int halfIdx = Mathf.FloorToInt(corrected / halfD);
            if (halfIdx != _lastHalfBeatIdx)
            {
                _lastHalfBeatIdx = halfIdx;
                OnHalfBeat?.Invoke();
            }
        }

        // Intervals (usan CorrectedTime)
        if (_intervals != null && _intervals.Length > 0)
        {
            foreach (Intervals interval in _intervals)
            {
                float intervalLen = interval.GetIntervalLength(_bpm);
                if (intervalLen <= 0f) continue;
                float sampledTime = corrected / intervalLen;
                interval.CheckForNewInterval(sampledTime);
            }
        }
    }

    // --- API de timing ---

    /// <summary>
    /// Distancia en segundos al beat mas cercano usando CorrectedTime.
    /// </summary>
    public float GetSecondsToNearestBeat()
    {
        if (_audioSource == null || _bpm <= 0f) return float.MaxValue;
        float d = BeatDuration;
        float mod = CorrectedTime % d;
        return Mathf.Min(mod, d - mod);
    }

    /// <summary>Mejora «Doble tempo»: los clics también cuentan en los medios beats.</summary>
    public static bool HalfBeatClicks => PlayerProgress.GetLevel(ShopIds.HalfBeat) > 0;

    /// <summary>Paso de la rejilla de clics: un beat, o medio beat con «Doble tempo».</summary>
    public float ClickStep => HalfBeatClicks ? BeatDuration * 0.5f : BeatDuration;

    /// <summary>Beat (o medio beat, con «Doble tempo») MÁS CERCANO al instante actual. Lo usa el anti-spam.</summary>
    public int NearestClickSlot => ClickStep > 0f ? Mathf.RoundToInt(CorrectedTime / ClickStep) : 0;

    /// <summary>Desfase (s) del instante actual respecto al beat (o medio beat) más cercano: + = tarde, − = pronto.</summary>
    public float ClickOffset => ClickStep > 0f ? CorrectedTime - NearestClickSlot * ClickStep : 0f;

    /// <summary>Timing de un CLIC del jugador (con «Doble tempo» también vale el medio beat más cercano).</summary>
    public TimingResult EvaluateClickTiming()
    {
        if (!HalfBeatClicks) return EvaluateTiming();
        if (_audioSource == null || _bpm <= 0f) return TimingResult.Miss;
        float d = ClickStep;
        float mod = CorrectedTime % d;
        float dist = Mathf.Min(mod, d - mod);
        if (dist <= PerfectWindow) return TimingResult.Perfect;
        if (dist <= GoodWindow)    return TimingResult.Good;
        return TimingResult.Miss;
    }

    /// <summary>
    /// Evalua el timing del input actual y devuelve Perfect, Good o Miss.
    /// Usado por RhythmInput para escalar la energia y mostrar feedback visual.
    /// </summary>
    public TimingResult EvaluateTiming()
    {
        float dist = GetSecondsToNearestBeat();
        if (dist <= PerfectWindow) return TimingResult.Perfect;
        if (dist <= GoodWindow)    return TimingResult.Good;
        return TimingResult.Miss;
    }

    /// <summary>
    /// True si el input actual esta dentro de la ventana Good o mejor.
    /// Usado por PlayerMovement para el beat gating de saltos y dashes.
    /// </summary>
    public bool IsOnBeat() => (HalfBeatMoves ? GetSecondsToNearestHalfBeat() : GetSecondsToNearestBeat()) <= GoodWindow;

    /// <summary>
    /// Con el contrato «Al compás» (acciones solo a ritmo) también valen los MEDIOS beats para saltar, hacer dash,
    /// slide o romper bloques. Lo activa ContractRunner mientras dura el contrato.
    /// </summary>
    public static bool HalfBeatMoves { get; set; }

    /// <summary>Distancia en segundos al beat O medio beat más cercano.</summary>
    public float GetSecondsToNearestHalfBeat()
    {
        if (_audioSource == null || _bpm <= 0f) return float.MaxValue;
        float d = HalfBeatDuration;
        float mod = CorrectedTime % d;
        return Mathf.Min(mod, d - mod);
    }

    // --- API de control de musica ---

    /// <summary>
    /// DSP timestamp en el que se ha programado el inicio de la musica.
    /// Valido solo despues de llamar a StartMusic().
    /// </summary>
    public double ScheduledStartDSP { get; private set; } = -1;

    /// <summary>
    /// Inicia la musica usando PlayScheduled() para precision perfecta de DSP.
    /// Despues de llamar a este metodo, usa ScheduledStartDSP para sincronizar
    /// cualquier otro sistema (p.ej. PlayerMovement) al mismo instante exacto.
    /// </summary>
    public void StartMusic()
    {
        _paused = false; _stopped = false;
        if (_audioSource == null) return;
        _audioSource.time = 0f;

        // 80 ms de margen: imperceptible para el jugador pero suficiente
        // para que el hilo de audio prepare el buffer antes del inicio.
        double dspStart = AudioSettings.dspTime + 0.08;
        _audioSource.PlayScheduled(dspStart);
        ScheduledStartDSP = dspStart;

        ResetBeatState();
    }

    /// <summary>
    /// Igual que StartMusic() pero programa el inicio en un DSP timestamp concreto.
    /// Útil para arrancar la canción exactamente en el siguiente beat del metrónomo.
    /// </summary>
    /// <summary>
    /// Pre-carga el clip de audio en memoria para evitar el spike de
    /// decompresión la primera vez que se llama a PlayScheduled.
    /// Llama a este método durante la intro (antes de que el jugador empiece).
    /// </summary>
    public void PreloadAudio()
    {
        if (_audioSource != null && _audioSource.clip != null)
            _audioSource.clip.LoadAudioData();
    }

    /// <summary>
    /// Cambia la AudioSource que el BeatManager usa como reloj, SIN resetear ScheduledStartDSP.
    /// Para relevos de pista (nivel → loop del jefe → final): la nueva fuente debe estar ya sonando
    /// y ALINEADA al beat (arrancada con PlayScheduled en un downbeat y con longitud múltiplo de beat),
    /// para que la fase (time % beatDuration) siga cuadrando con la rejilla global.
    /// </summary>
    public void SwapSource(AudioSource newSource)
    {
        if (newSource != null) _audioSource = newSource;
    }

    public void StartMusicAt(double dspTime)
    {
        _paused = false; _stopped = false;
        if (_audioSource == null) return;
        _audioSource.Stop();
        _audioSource.time = 0f;
        _audioSource.PlayScheduled(dspTime);
        ScheduledStartDSP = dspTime;
        ResetBeatState();
    }

    /// <summary>
    /// Como StartMusicAt pero arrancando el clip desde songTimeOffset segundos.
    /// Útil para el modo test con la secuencia de inicio: el jugador hace el 3-2-1-YA!
    /// y la música arranca en el beat correcto pero desde la posición correspondiente
    /// a la distancia de test.
    /// ScheduledStartDSP = dspTime - songTimeOffset  →  beat 0 queda en el pasado correcto.
    /// </summary>
    public void StartMusicFromAt(float songTimeOffset, double dspTime)
    {
        _paused = false; _stopped = false;
        if (_audioSource == null) return;

        // Canción EN BUCLE (nivel tutorial / estudio): el reloj sigue contando sin cortes (beat 300, 400…) y el clip
        // empieza en la posición equivalente dentro de la vuelta. La duración del clip debe ser un número exacto de beats.
        bool looping = _audioSource.loop && _audioSource.clip != null && _audioSource.clip.length > 0.5f;
        float safeOffset = looping ? Mathf.Max(0f, songTimeOffset)
            : _audioSource.clip != null
                ? Mathf.Clamp(songTimeOffset, 0f, _audioSource.clip.length - 0.1f)
                : Mathf.Max(0f, songTimeOffset);
        float clipOffset = looping ? Mathf.Repeat(safeOffset, _audioSource.clip.length) : safeOffset;

        _audioSource.Stop();
        _audioSource.pitch = 1f;   // (por si venía de un rebobinado con pitch negativo)
        // (Posición en MUESTRAS: con «time» en clips comprimidos el salto no es exacto y la música quedaba unos
        //  milisegundos desfasada del reloj al reaparecer lejos del principio, p. ej. al saltar una sección.)
        if (_audioSource.clip != null && _audioSource.clip.frequency > 0)
            _audioSource.timeSamples = Mathf.Clamp((int)System.Math.Round((double)clipOffset * _audioSource.clip.frequency), 0, _audioSource.clip.samples - 1);
        else _audioSource.time = clipOffset;
        _audioSource.PlayScheduled(dspTime);

        ScheduledStartDSP = dspTime - (double)safeOffset;
        _lastBeatIndex    = Mathf.FloorToInt(safeOffset / BeatDuration) - 1;
        _lastHalfBeatIdx  = Mathf.FloorToInt(safeOffset / HalfBeatDuration) - 1;
    }

    /// <summary>Velocidad de reproducción (negativa = hacia atrás; lo usa el Rebobinado). StartMusicFromAt la devuelve a 1.</summary>
    public void SetPlaybackPitch(float pitch)
    {
        if (_audioSource != null) _audioSource.pitch = pitch;
    }

    /// <summary>
    /// Solo para el Rebobinado: mueve el reloj DSP de todo el nivel para que "ahora" sea el segundo 'songTime' de la
    /// canción. Así todo lo que se calcula a partir de ScheduledStartDSP (osciladores, bloques que caen…) se ve
    /// volviendo atrás junto con el jugador. StartMusicFromAt lo deja bien al retomar.
    /// </summary>
    public void SetRewindClock(float songTime)
    {
        ScheduledStartDSP = AudioSettings.dspTime - (double)Mathf.Max(0f, songTime);
    }

    /// <summary>
    /// Para el tutorial (cámara lenta hasta pararse): fija el reloj para que «ahora» sea el segundo 'songSeconds' de la
    /// canción. Como todo se calcula a partir de ScheduledStartDSP, el nivel entero se frena y se para con él.
    /// </summary>
    public void SetClockSongTime(double songSeconds)
    {
        ScheduledStartDSP = AudioSettings.dspTime - songSeconds;
        _heldSongTime = songSeconds;
    }

    /// <summary>Para la musica y resetea el estado.</summary>
    public void StopMusic()
    {
        _stopped = true;
        if (_audioSource != null) _audioSource.Stop();
        ResetBeatState();
    }

    /// <summary>Pausa la musica sin perder la posicion.</summary>
    public void PauseMusic()
    {
        if (_paused) return;
        _pausedSongTime = SongTime;
        _pausedAtDsp    = AudioSettings.dspTime;
        _paused = true;
        if (_audioSource != null) _audioSource.Pause();
    }

    /// <summary>Reanuda la musica desde donde se pauso.</summary>
    public void ResumeMusic()
    {
        // Si el reloj de audio siguió corriendo durante la pausa, se desplaza el inicio para que la posición
        // siga donde se quedó la música (si el reloj se congeló, el desplazamiento es 0 y no cambia nada).
        if (_paused && HasClock) ScheduledStartDSP += AudioSettings.dspTime - _pausedAtDsp;
        _paused = false;
        if (_audioSource != null) _audioSource.UnPause();
    }

    // --- Internals ---

    private void ResetBeatState()
    {
        _lastBeatIndex   = -1;
        _lastHalfBeatIdx = -1;
    }
}

// Intervals: dispara _trigger cada 1/_steps beats.
[System.Serializable]
public class Intervals
{
    [SerializeField] private float _steps;
    [SerializeField] private UnityEvent _trigger;
    private int _lastInterval;

    public float GetIntervalLength(float bpm)
    {
        return bpm > 0f && _steps > 0f ? 60f / (bpm * _steps) : 0f;
    }

    public void CheckForNewInterval(float sampledTime)
    {
        int current = Mathf.FloorToInt(sampledTime);
        if (current != _lastInterval)
        {
            _lastInterval = current;
            _trigger?.Invoke();
        }
    }
}

/// <summary>
/// Resultado de evaluar si un input del jugador cayo dentro de una ventana de ritmo.
/// </summary>
public enum TimingResult
{
    /// <summary>Dentro de la ventana Perfect (muy preciso). Da bonus de energia.</summary>
    Perfect,

    /// <summary>Dentro de la ventana Good (aceptable). Da energia normal.</summary>
    Good,

    /// <summary>Fuera de cualquier ventana. Penalizacion de energia.</summary>
    Miss
}
