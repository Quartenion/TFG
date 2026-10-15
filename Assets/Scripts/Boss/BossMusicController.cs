using UnityEngine;

/// <summary>
/// Gestiona los RELEVOS de música del boss fight sin romper el reloj de beats:
///   1. Cuando TERMINA la canción del nivel, arranca LDNM-BOSS-LOOP (en bucle) justo en ese instante.
///   2. Al derrotar al jefe (BossController.OnDefeated) el loop NO se corta: termina la vuelta que está sonando
///      y justo al acabar entra LDNM-END (una vez). EndFinishDSP indica cuándo acaba la canción.
///
/// El relevo se programa con PlayScheduled en el instante EXACTO en que acaba la pista del nivel
/// (ScheduledStartDSP + duración del clip), y el BeatManager cambia su fuente de reloj con SwapSource
/// SIN resetear ScheduledStartDSP → los beats siguen siendo continuos (la Fase 1 no se entera).
///
/// ⚠️ Para que el ritmo no se desincronice, LDNM-BOSS-LOOP debe durar un número EXACTO de beats
///    (a 70 BPM: beats × 0.857143 s). Como tus audios están hechos para empalmar, con eso basta.
///
/// SETUP:
///   1. Crea 2 AudioSource (pueden ir en este objeto):
///        · Loop Source → clip LDNM-BOSS-LOOP · Loop = ON  · Play On Awake = OFF.
///        · End Source  → clip LDNM-END       · Loop = OFF · Play On Awake = OFF.
///   2. Asigna BeatManager, BossController y las dos fuentes aquí. ¡Nada de números a mano!
/// </summary>
[AddComponentMenu("Boss/Boss Music Controller")]
public class BossMusicController : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private BeatManager _beatManager;
    [SerializeField] private BossController _boss;

    [Header("Fuentes de audio (AudioSource aparte, Play On Awake OFF)")]
    [Tooltip("AudioSource con LDNM-BOSS-LOOP (Loop = ON).")]
    [SerializeField] private AudioSource _loopSource;
    [Tooltip("AudioSource con LDNM-END (Loop = OFF).")]
    [SerializeField] private AudioSource _endSource;

    [Header("Ajuste fino (opcional)")]
    [Tooltip("Desplaza el relevo unos beats respecto al final exacto de la canción del nivel. 0 = justo al acabar.")]
    [SerializeField] private float _handoffOffsetBeats = 0f;
    [Tooltip("Beats de antelación con los que se programa cada relevo (margen para PlayScheduled).")]
    [SerializeField] private float _scheduleLeadBeats = 1f;

    [Header("Debug")]
    [SerializeField] private bool _verbose = true;

    // ── Estado ────────────────────────────────────────────────────────────────
    private float  _levelClipLength = -1f;   // duración de la pista del nivel (capturada antes del relevo)
    private double _handoffDSP = -1;
    private bool   _loopScheduled, _loopSwapped;
    private bool   _endScheduled, _endSwapped;
    private double _endDSP = -1;

    private float BeatDur => _beatManager != null ? _beatManager.BeatDuration : 0.5f;

    private void Start()
    {
        if (_beatManager == null) _beatManager = BeatManager.Instance;
        if (_boss == null)        _boss = FindAnyObjectByType<BossController>();

        if (_loopSource != null) { _loopSource.loop = true;  _loopSource.playOnAwake = false; Preload(_loopSource); }
        if (_endSource  != null) { _endSource.loop  = false; _endSource.playOnAwake  = false; Preload(_endSource); }

        if (_boss != null) _boss.OnDefeated.AddListener(OnBossDefeated);
    }

    private void OnDestroy()
    {
        if (_boss != null) _boss.OnDefeated.RemoveListener(OnBossDefeated);
    }

    private void Update()
    {
        if (_beatManager == null || !_beatManager.HasClock) return;
        double now = AudioSettings.dspTime;

        // Capturar la duración de la pista del nivel UNA vez (antes de relevar la fuente).
        if (_levelClipLength < 0f)
        {
            AudioSource lvl = _beatManager.CurrentSource;
            if (lvl != null && lvl.clip != null) _levelClipLength = lvl.clip.length;
        }
        if (_levelClipLength > 0f)
            _handoffDSP = _beatManager.ScheduledStartDSP + _levelClipLength + _handoffOffsetBeats * BeatDur;

        // ── Relevo al LOOP del jefe, justo cuando acaba la canción del nivel ──
        if (!_loopScheduled && _loopSource != null && _handoffDSP > 0.0 &&
            now >= _handoffDSP - _scheduleLeadBeats * BeatDur && now < _handoffDSP)
        {
            ScheduleHandoff(_loopSource, _handoffDSP);
            _loopScheduled = true;
            if (_verbose) Debug.Log($"[BossMusic] Loop programado para el final del nivel (DSP {_handoffDSP:F2}).");
        }
        if (_loopScheduled && !_loopSwapped && now >= _handoffDSP)
        {
            _beatManager.SwapSource(_loopSource);
            _loopSwapped = true;
            if (_verbose) Debug.Log("[BossMusic] Loop del jefe en marcha (reloj relevado).");
        }

        // ── Relevo al FINAL (tras derrota) ──
        if (_endScheduled && !_endSwapped && _endDSP > 0.0 && now >= _endDSP)
        {
            _beatManager.SwapSource(_endSource);
            _endSwapped = true;
            if (_verbose) Debug.Log("[BossMusic] LDNM-END en marcha.");
        }
    }

    private void OnBossDefeated()
    {
        if (_endSource == null || _beatManager == null) return;

        double now = AudioSettings.dspTime;

        if (_loopSwapped && _loopSource != null && _loopSource.clip != null && _handoffDSP > 0.0)
        {
            // El LOOP del jefe NO se corta: sigue hasta el FINAL de la vuelta que está sonando y ahí entra LDNM-END.
            AudioClip lc = _loopSource.clip;
            double loopLen = lc.frequency > 0 ? lc.samples / (double)lc.frequency : lc.length;
            double passes  = System.Math.Ceiling((now - _handoffDSP) / loopLen);
            _endDSP = _handoffDSP + passes * loopLen;
            if (_endDSP - now < 0.1) _endDSP += loopLen;   // demasiado justo para programarlo: la siguiente vuelta
            // (ScheduleHandoff le pone SetScheduledEndTime justo ahí: termina la vuelta entera y se para.)
        }
        else
        {
            // (Sin loop en marcha) siguiente downbeat, como antes.
            double startDSP = _beatManager.ScheduledStartDSP;
            double beatsNow = (now - startDSP) / BeatDur;
            int    nextDown = (Mathf.FloorToInt((float)beatsNow) / 4 + 1) * 4;
            _endDSP = startDSP + nextDown * BeatDur;
        }

        ScheduleHandoff(_endSource, _endDSP);
        _endScheduled = true;

        AudioClip ec = _endSource.clip;
        double endLen = ec != null ? (ec.frequency > 0 ? ec.samples / (double)ec.frequency : ec.length) : 0.0;
        EndFinishDSP = _endDSP + endLen;
        if (_verbose) Debug.Log($"[BossMusic] El loop sigue hasta el final de su vuelta; LDNM-END empieza en " +
                                $"{_endDSP - now:F1} s y termina en {EndFinishDSP - now:F1} s.");
    }

    /// <summary>DSP en el que TERMINA LDNM-END (−1 si aún no se ha programado).</summary>
    public double EndFinishDSP { get; private set; } = -1;


    /// <summary>Arranca 'next' en 'atDSP' (desde el principio) y hace terminar la fuente actual ahí mismo.</summary>
    private void ScheduleHandoff(AudioSource next, double atDSP)
    {
        next.time = 0f;
        next.PlayScheduled(atDSP);

        AudioSource cur = _beatManager.CurrentSource;
        if (cur != null && cur != next && cur.isPlaying)
            cur.SetScheduledEndTime(atDSP);
    }

    private static void Preload(AudioSource src)
    {
        if (src != null && src.clip != null) src.clip.LoadAudioData();
    }
}
