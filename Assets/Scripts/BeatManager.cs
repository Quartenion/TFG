using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Gestiona el beat de la musica y evalua la precision de los inputs del jugador.
///
///   1. Compensacion de latencia: CorrectedTime = audioSource.time - _audioLatencyOffset.
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

    /// <summary>Tiempo crudo del AudioSource (sin compensar latencia).</summary>
    public float SongTime => _audioSource != null ? _audioSource.time : 0f;

    /// <summary>
    /// Tiempo percibido por el jugador (SongTime - offset de latencia).
    /// Todos los calculos de hit detection usan este valor.
    /// </summary>
    public float CorrectedTime => Mathf.Max(0f, SongTime - _audioLatencyOffset);

    /// <summary>Indice del beat actual desde el inicio de la cancion.</summary>
    public int CurrentBeatIndex => BeatDuration > 0f ? Mathf.FloorToInt(CorrectedTime / BeatDuration) : 0;

    /// <summary>Ventana Perfect en segundos.</summary>
    public float PerfectWindow => _perfectWindow * WindowMultiplier;

    /// <summary>Ventana Good en segundos.</summary>
    public float GoodWindow => _goodWindow * WindowMultiplier;

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
        if (_audioSource == null) return;

        float safeOffset = _audioSource.clip != null
            ? Mathf.Clamp(songTimeOffset, 0f, _audioSource.clip.length - 0.1f)
            : Mathf.Max(0f, songTimeOffset);

        _audioSource.Stop();
        _audioSource.pitch = 1f;   // (por si venía de un rebobinado con pitch negativo)
        _audioSource.time = safeOffset;
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

    /// <summary>Para la musica y resetea el estado.</summary>
    public void StopMusic()
    {
        if (_audioSource != null) _audioSource.Stop();
        ResetBeatState();
    }

    /// <summary>Pausa la musica sin perder la posicion.</summary>
    public void PauseMusic()
    {
        if (_audioSource != null) _audioSource.Pause();
    }

    /// <summary>Reanuda la musica desde donde se pauso.</summary>
    public void ResumeMusic()
    {
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
