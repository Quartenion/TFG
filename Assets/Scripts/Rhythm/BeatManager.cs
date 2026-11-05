using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Gestiona el beat de la música y evalúa la precisión de los clics del jugador.
///
///   0. Reloj musical único. SongTime, ClockSeconds y ClockBeats se calculan a partir del reloj de audio (dspTime)
///      y del instante en el que se programó la canción (ScheduledStartDSP). Todos los sistemas leen la posición
///      de la canción de aquí.
///   1. Compensación de latencia, con CorrectedTime = SongTime - _audioLatencyOffset. Si el jugador siempre llega
///      tarde, se aumenta el offset, y si llega pronto, se pone negativo. Si hay un valor guardado en PlayerPrefs
///      ("AudioLatencyOffset"), se usa ese.
///
///   2. Ventanas de acierto, Perfect (+-_perfectWindow) y Good (+-_goodWindow). EvaluateTiming() devuelve Perfect,
///      Good o Miss, que se usa para calcular la energía y para mostrar el resultado en pantalla.
///
///   3. OnBeat y OnHalfBeat son UnityEvents para conectar PulseToTheBeat u otros efectos desde el Inspector, sin
///      necesidad de crear un Interval con steps = 1.
///
///   4. Singleton de acceso global (BeatManager.Instance).
///
///   5. Intervals, que son UnityEvents que se lanzan cada N pasos de beat (los usa PulseToTheBeat en Managers).
///
///   6. Los eventos de beat no se lanzan con Time.timeScale == 0, es decir, durante la pausa.
/// </summary>
public class BeatManager : MonoBehaviour
{
    // Singleton
    public static BeatManager Instance { get; private set; }

    // -=+=- Audio -=+=-
    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private float _bpm = 120f;

    // -=+=- Latencia -=+=-
    [Header("Compensacion de latencia")]
    [Tooltip("Offset en segundos para compensar la latencia del dispositivo de audio. " +
             "Si el jugador siempre llega tarde, aumenta este valor (0.03-0.08). " +
             "Si llega pronto, usa un valor negativo. " +
             "Si hay un valor guardado en PlayerPrefs, se usa ese.")]
    [SerializeField] private float _audioLatencyOffset = 0f;

    [Tooltip("Si esta activo, al iniciar carga el offset guardado en PlayerPrefs por el calibrador.")]
    [SerializeField] private bool _loadOffsetFromPlayerPrefs = true;

    // -=+=- Ventanas de acierto -=+=-
    [Header("Ventanas de timing")]
    [Tooltip("Ventana Perfect en segundos (por ejemplo 0.07 = 70ms). Da 1.5x de energia.")]
    [SerializeField] private float _perfectWindow = 0.07f;

    [Tooltip("Ventana Good en segundos (por ejemplo 0.14 = 140ms). Da 1x de energia. " +
             "Fuera de esta ventana = Miss.")]
    [SerializeField] private float _goodWindow = 0.14f;

    // -=+=- Eventos de beat -=+=-
    [Header("Eventos de beat (nuevos)")]
    [Tooltip("Se dispara una vez por beat. Conecta PulseToTheBeat.Pulse() aqui.")]
    public UnityEvent OnBeat;

    [Tooltip("Se dispara dos veces por beat (cada corchea). Util para footsteps.")]
    public UnityEvent OnHalfBeat;

    // -=+=- Intervals, eventos cada N pasos de beat -=+=-
    [Header("Intervals")]
    [SerializeField] private Intervals[] _intervals;

    // -=+=- Propiedades públicas -=+=-

    /// <summary>BPM de la canción.</summary>
    public float Bpm => _bpm;

    /// <summary>Duración de un beat en segundos.</summary>
    public float BeatDuration => _bpm > 0f ? 60f / _bpm : 1f;

    /// <summary>Duración de medio beat en segundos.</summary>
    public float HalfBeatDuration => BeatDuration * 0.5f;

    // -=+=- Reloj musical único -=+=-
    // Todo el juego lee de aquí la posición de la canción, que se calcula con el reloj de audio (AudioSettings.dspTime)
    // y el instante en el que se programó la canción (ScheduledStartDSP). Esto tiene varias ventajas.
    //   - No acumula error, ya que no suma deltaTime, y avanza de forma continua, no a saltos de bloque como AudioSource.time.
    //   - Sigue siendo continuo aunque cambie la pista, por ejemplo del nivel al bucle del jefe y al final (SwapSource).
    //   - El rebobinado lo mueve con SetRewindClock, y StartMusicFromAt lo vuelve a colocar al retomar la partida.

    /// <summary>Tempo por defecto si no hay BeatManager, por ejemplo para los gizmos del editor sin una escena cargada.</summary>
    public const float DefaultBpm = 120f;

    /// <summary>Indica si hay una canción programada, es decir, si ScheduledStartDSP es válido.</summary>
    public bool HasClock => ScheduledStartDSP > 0.0;

    /// <summary>
    /// Segundos de canción según el reloj de audio, sin recortar y sin compensar la latencia, por lo que es negativo antes
    /// de que empiece la canción. Es la base de todo lo demás. Para programar sonidos o eventos en un beat concreto se usa DspAtBeat.
    /// </summary>
    public double ClockSeconds => HasClock ? (_clockHeld ? _heldSongTime : AudioSettings.dspTime - ScheduledStartDSP) : 0.0;

    /// <summary>
    /// Instante actual en el reloj DSP, coherente con ClockSeconds. Normalmente es AudioSettings.dspTime, pero mientras el
    /// tutorial controla el reloj (cámara lenta o parado) es ScheduledStartDSP más la posición fijada. Así, dspTime, que avanza
    /// a saltos por bloques de audio dentro del mismo fotograma, no hace temblar a los objetos que se calculan con él.
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

    /// <summary>Tutorial. Mientras está activo, ClockSeconds y DspNow valen exactamente lo fijado con SetClockSongTime.</summary>
    public void HoldClock(bool hold)
    {
        if (hold && !_clockHeld) _heldSongTime = AudioSettings.dspTime - ScheduledStartDSP;
        _clockHeld = hold;
    }

    /// <summary>Posición de la canción en beats (con decimales) según el reloj de audio.</summary>
    public double ClockBeats => BeatDuration > 0f ? ClockSeconds / BeatDuration : 0.0;

    /// <summary>Instante DSP en el que cae el beat 'beat' (0 es el inicio de la canción).</summary>
    public double DspAtBeat(double beat) => ScheduledStartDSP + beat * BeatDuration;

    /// <summary>Instante DSP en el que la canción llega al segundo 'seconds'.</summary>
    public double DspAtSongTime(double seconds) => ScheduledStartDSP + seconds;

    private bool  _paused;
    private bool  _stopped;
    private float _pausedSongTime;
    private double _pausedAtDsp;

    /// <summary>
    /// Segundos de canción, sin compensar la latencia y nunca menores que 0. En pausa se congela, y después de StopMusic
    /// (o sin una canción programada) vale AudioSource.time. Entre dos pistas, como en el relevo al bucle del jefe, sigue avanzando.
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
    /// Como SongTime, pero sin recortar a 0, por lo que es negativo mientras la canción está programada y todavía no ha
    /// empezado (por ejemplo, en el beat que separa el ¡YA! del inicio). Lo usan las notas del HUD para llegar justo al beat 0.
    /// </summary>
    public float SongTimeUnclamped => HasClock && !_stopped && !_paused ? (float)ClockSeconds : SongTime;

    /// <summary>Tiempo percibido, con compensación de latencia, sin recortar a 0 (ver SongTimeUnclamped).</summary>
    public float CorrectedTimeUnclamped => SongTimeUnclamped - _audioLatencyOffset;

    /// <summary>Duración de un beat de 'bm', o del BeatManager de la escena, o la de DefaultBpm si no hay ninguno.</summary>
    public static float BeatDurationOf(BeatManager bm) => 60f / BpmOf(bm);

    /// <summary>BPM de 'bm', o del BeatManager de la escena, o DefaultBpm. También funciona en el editor (gizmos).</summary>
    public static float BpmOf(BeatManager bm)
    {
        if (bm == null) bm = Instance;
#if UNITY_EDITOR
        if (bm == null && !Application.isPlaying) bm = FindAnyObjectByType<BeatManager>();
#endif
        return bm != null && bm.Bpm > 0f ? bm.Bpm : DefaultBpm;
    }

    /// <summary>
    /// Tiempo que percibe el jugador (SongTime menos el offset de latencia). Lo usan la evaluación de los clics, los
    /// eventos de beat y el HUD rítmico.
    /// </summary>
    public float CorrectedTime => Mathf.Max(0f, SongTime - _audioLatencyOffset);

    /// <summary>Índice del beat actual desde el inicio de la canción.</summary>
    public int CurrentBeatIndex => BeatDuration > 0f ? Mathf.FloorToInt(CorrectedTime / BeatDuration) : 0;

    /// <summary>Ventana Perfect en segundos.</summary>
    public float PerfectWindow => _perfectWindow * WindowMultiplier * EnergyManager.GraceWindowMultiplier;   // "Respiro"

    /// <summary>Ventana Good en segundos.</summary>
    public float GoodWindow => _goodWindow * WindowMultiplier * EnergyManager.GraceWindowMultiplier;

    /// <summary>Mejora "Oído fino" de la tienda, que amplía un 15 % las ventanas Perfect y Good por cada nivel.</summary>
    public static float WindowMultiplier => 1f + 0.15f * PlayerProgress.GetLevel(ShopIds.WideWindow);

    /// <summary>Indica si la música está sonando.</summary>
    public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;

    /// <summary>AudioSource que el BeatManager usa ahora mismo como reloj (cambia en los relevos de pista).</summary>
    public AudioSource CurrentSource => _audioSource;

    // -=+=- Estado interno -=+=-
    private int _lastBeatIndex   = -1;
    private int _lastHalfBeatIdx = -1;

    // -=+=- Ciclo de vida de Unity -=+=-

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
        // Mientras se rebobina, la canción suena al revés y no se lanzan eventos de beat, ya que cada beat que se
        // deshace lanzaría OnBeat y el HUD rítmico mostraría muchas notas a la vez.
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

        // Intervals, que usan CorrectedTime
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

    // -=+=- Evaluación de los clics -=+=-

    /// <summary>Distancia en segundos al beat más cercano, usando CorrectedTime.</summary>
    public float GetSecondsToNearestBeat()
    {
        if (_audioSource == null || _bpm <= 0f) return float.MaxValue;
        float d = BeatDuration;
        float mod = CorrectedTime % d;
        return Mathf.Min(mod, d - mod);
    }

    /// <summary>Mejora "Doble tempo". Los clics también cuentan en los medios beats.</summary>
    public static bool HalfBeatClicks => PlayerProgress.GetLevel(ShopIds.HalfBeat) > 0;

    /// <summary>Paso de la rejilla de clics, que es un beat, o medio beat con "Doble tempo".</summary>
    public float ClickStep => HalfBeatClicks ? BeatDuration * 0.5f : BeatDuration;

    /// <summary>Beat (o medio beat, con "Doble tempo") más cercano al instante actual. Lo usa el anti-spam.</summary>
    public int NearestClickSlot => ClickStep > 0f ? Mathf.RoundToInt(CorrectedTime / ClickStep) : 0;

    /// <summary>Desfase en segundos del instante actual respecto al beat (o medio beat) más cercano. Es positivo si es tarde y negativo si es pronto.</summary>
    public float ClickOffset => ClickStep > 0f ? CorrectedTime - NearestClickSlot * ClickStep : 0f;

    /// <summary>Resultado de un clic del jugador. Con "Doble tempo" también vale el medio beat más cercano.</summary>
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
    /// Evalúa el momento actual y devuelve Perfect, Good o Miss. Lo usa RhythmInput para calcular la energía y mostrar
    /// el resultado en pantalla.
    /// </summary>
    public TimingResult EvaluateTiming()
    {
        float dist = GetSecondsToNearestBeat();
        if (dist <= PerfectWindow) return TimingResult.Perfect;
        if (dist <= GoodWindow)    return TimingResult.Good;
        return TimingResult.Miss;
    }

    /// <summary>
    /// Indica si el momento actual está dentro de la ventana Good o mejor. Lo usa PlayerMovement para permitir los saltos
    /// y los dashes solo en el beat.
    /// </summary>
    public bool IsOnBeat() => (HalfBeatMoves ? GetSecondsToNearestHalfBeat() : GetSecondsToNearestBeat()) <= GoodWindow;

    /// <summary>
    /// Con el contrato "Al compás" (acciones solo al ritmo) también valen los medios beats para saltar, hacer dash,
    /// deslizarse o romper bloques. Lo activa ContractRunner mientras dura el contrato.
    /// </summary>
    public static bool HalfBeatMoves { get; set; }

    /// <summary>Distancia en segundos al beat o al medio beat más cercano.</summary>
    public float GetSecondsToNearestHalfBeat()
    {
        if (_audioSource == null || _bpm <= 0f) return float.MaxValue;
        float d = HalfBeatDuration;
        float mod = CorrectedTime % d;
        return Mathf.Min(mod, d - mod);
    }

    // -=+=- Control de la música -=+=-

    /// <summary>
    /// Instante DSP en el que se ha programado el inicio de la música. Solo es válido después de llamar a StartMusic().
    /// </summary>
    public double ScheduledStartDSP { get; private set; } = -1;

    /// <summary>
    /// Inicia la música con PlayScheduled() para que empiece en un instante exacto del reloj DSP. Después, el resto de
    /// sistemas (por ejemplo, PlayerMovement) usan ScheduledStartDSP para sincronizarse con el mismo instante.
    /// </summary>
    public void StartMusic()
    {
        _paused = false; _stopped = false;
        if (_audioSource == null) return;
        _audioSource.time = 0f;

        // Se deja un margen de 80 ms, que el jugador no nota pero que es suficiente para que el hilo de audio
        // prepare el buffer antes de que empiece la canción.
        double dspStart = AudioSettings.dspTime + 0.08;
        _audioSource.PlayScheduled(dspStart);
        ScheduledStartDSP = dspStart;

        ResetBeatState();
    }

    /// <summary>
    /// Carga el clip de audio en memoria para evitar el pico de descompresión la primera vez que se llama a
    /// PlayScheduled. Se llama durante la intro, antes de que el jugador empiece.
    /// </summary>
    public void PreloadAudio()
    {
        if (_audioSource != null && _audioSource.clip != null)
            _audioSource.clip.LoadAudioData();
    }

    /// <summary>
    /// Cambia el AudioSource que el BeatManager usa como reloj, sin reiniciar ScheduledStartDSP. Sirve para los relevos de
    /// pista (del nivel al bucle del jefe y al final). La nueva fuente tiene que estar ya sonando y alineada con el beat,
    /// es decir, arrancada con PlayScheduled en el primer beat de un compás y con una duración múltiplo del beat, para
    /// que la fase (time % beatDuration) siga coincidiendo con la rejilla global.
    /// </summary>
    public void SwapSource(AudioSource newSource)
    {
        if (newSource != null) _audioSource = newSource;
    }

    /// <summary>
    /// Igual que StartMusic(), pero programa el inicio en un instante DSP concreto. Se usa para que la canción empiece
    /// justo en el siguiente beat del metrónomo.
    /// </summary>
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
    /// Como StartMusicAt, pero el clip empieza en el segundo songTimeOffset. Se usa en el modo test con la secuencia de
    /// inicio, en la que el jugador hace la cuenta atrás y la música empieza en el beat correcto, pero desde la posición
    /// que corresponde a la distancia de test. Como ScheduledStartDSP = dspTime - songTimeOffset, el beat 0 queda en el
    /// pasado, en el instante correcto.
    /// </summary>
    public void StartMusicFromAt(float songTimeOffset, double dspTime)
    {
        _paused = false; _stopped = false;
        if (_audioSource == null) return;

        // Si la canción está en bucle (nivel tutorial o estudio), el reloj sigue contando sin cortes (beat 300, 400...) y
        // el clip empieza en la posición equivalente dentro de la vuelta. La duración del clip tiene que ser un número exacto de beats.
        bool looping = _audioSource.loop && _audioSource.clip != null && _audioSource.clip.length > 0.5f;
        float safeOffset = looping ? Mathf.Max(0f, songTimeOffset)
            : _audioSource.clip != null
                ? Mathf.Clamp(songTimeOffset, 0f, _audioSource.clip.length - 0.1f)
                : Mathf.Max(0f, songTimeOffset);
        float clipOffset = looping ? Mathf.Repeat(safeOffset, _audioSource.clip.length) : safeOffset;

        _audioSource.Stop();
        _audioSource.pitch = 1f;   // por si venía de un rebobinado con pitch negativo
        // La posición se fija en muestras, ya que con "time" el salto no es exacto en los clips comprimidos y la música
        // quedaría unos milisegundos desfasada del reloj al empezar lejos del principio, por ejemplo al saltar una sección.
        if (_audioSource.clip != null && _audioSource.clip.frequency > 0)
            _audioSource.timeSamples = Mathf.Clamp((int)System.Math.Round((double)clipOffset * _audioSource.clip.frequency), 0, _audioSource.clip.samples - 1);
        else _audioSource.time = clipOffset;
        _audioSource.PlayScheduled(dspTime);

        ScheduledStartDSP = dspTime - (double)safeOffset;
        _lastBeatIndex    = Mathf.FloorToInt(safeOffset / BeatDuration) - 1;
        _lastHalfBeatIdx  = Mathf.FloorToInt(safeOffset / HalfBeatDuration) - 1;
    }

    /// <summary>Velocidad de reproducción. Es negativa hacia atrás, para el Rebobinado, y StartMusicFromAt la devuelve a 1.</summary>
    public void SetPlaybackPitch(float pitch)
    {
        if (_audioSource != null) _audioSource.pitch = pitch;
    }

    /// <summary>
    /// Solo para el Rebobinado. Mueve el reloj DSP de todo el nivel para que el instante actual sea el segundo 'songTime'
    /// de la canción. Así, todo lo que se calcula a partir de ScheduledStartDSP (osciladores, bloques que caen...) vuelve
    /// atrás junto con el jugador. StartMusicFromAt lo corrige al retomar la partida.
    /// </summary>
    public void SetRewindClock(float songTime)
    {
        ScheduledStartDSP = AudioSettings.dspTime - (double)Mathf.Max(0f, songTime);
    }

    /// <summary>
    /// Para el tutorial, que ralentiza el juego hasta pararlo. Fija el reloj para que el instante actual sea el segundo
    /// 'songSeconds' de la canción. Como todo se calcula a partir de ScheduledStartDSP, el nivel entero se frena y se para con él.
    /// </summary>
    public void SetClockSongTime(double songSeconds)
    {
        ScheduledStartDSP = AudioSettings.dspTime - songSeconds;
        _heldSongTime = songSeconds;
    }

    /// <summary>Para la música y reinicia el estado.</summary>
    public void StopMusic()
    {
        _stopped = true;
        if (_audioSource != null) _audioSource.Stop();
        ResetBeatState();
    }

    /// <summary>Pausa la música sin perder la posición.</summary>
    public void PauseMusic()
    {
        if (_paused) return;
        _pausedSongTime = SongTime;
        _pausedAtDsp    = AudioSettings.dspTime;
        _paused = true;
        if (_audioSource != null) _audioSource.Pause();
    }

    /// <summary>Reanuda la música desde donde se pausó.</summary>
    public void ResumeMusic()
    {
        // Si el reloj de audio siguió avanzando durante la pausa, se desplaza el inicio para que la posición siga
        // donde se quedó la música. Si el reloj se congeló, el desplazamiento es 0 y no cambia nada.
        if (_paused && HasClock) ScheduledStartDSP += AudioSettings.dspTime - _pausedAtDsp;
        _paused = false;
        if (_audioSource != null) _audioSource.UnPause();
    }

    // -=+=- Funciones internas -=+=-

    private void ResetBeatState()
    {
        _lastBeatIndex   = -1;
        _lastHalfBeatIdx = -1;
    }
}

// Intervals: lanza _trigger cada 1/_steps beats.
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

/// <summary>Resultado de evaluar si un clic del jugador cae dentro de una ventana de acierto.</summary>
public enum TimingResult
{
    /// <summary>Dentro de la ventana Perfect (muy preciso). Da un bonus de energía.</summary>
    Perfect,

    /// <summary>Dentro de la ventana Good (aceptable). Da la energía normal.</summary>
    Good,

    /// <summary>Fuera de las dos ventanas. Penaliza la energía.</summary>
    Miss
}
