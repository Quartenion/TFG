using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// REBOBINADO (consumible «shield» de la tienda). Graba continuamente dónde estaba el jugador y hacia dónde miraba.
/// Si choca contra un obstáculo y tiene rebobinados, en vez de morir:
///   1. Todo se para y el jugador VUELVE HACIA ATRÁS por su propio recorrido (posición y mirada) en ~1 s, mientras
///      la música suena al revés (efecto cinta).
///   2. Aparece en el último momento SEGURO de hace al menos 4 beats: en el suelo y sin ninguna acción en curso
///      (ni salto, ni dash, ni slide, ni wallrun…), alineado a un beat entero.
///   3. La canción se retoma justo en ese punto y todo lo que va con el reloj de beats vuelve a ese instante.
/// En el KAYAK rebobina también el kayak (vuelve con él a un momento en que ibas sentado flotando).
/// En la TIROLINA te suelta y vuelves a un momento en que ibas a pie (antes de engancharte).
/// No funciona en la pelea del jefe (ahí no hay modificadores de la tienda).
/// Se añade solo al jugador en los niveles.
/// </summary>
public class TimeRewinder : MonoBehaviour
{
    [Tooltip("Beats que se retrocede como mínimo.")]
    public int rewindBeats = 4;
    [Tooltip("Segundos (reales) que dura la animación de volver atrás.")]
    public float rewindDuration = 1.1f;
    [Tooltip("Segundos de recorrido que se guardan.")]
    public float keepSeconds = 14f;

    public static TimeRewinder Instance { get; private set; }
    public static bool IsRewinding => Instance != null && Instance._rewinding;

    private struct Sample
    {
        public float t; public Vector3 pos; public float yaw, pitch; public bool safe;
        public KayakController kayak; public Vector3 kayakPos; public Quaternion kayakRot;   // (si ibas en kayak)
    }
    private readonly List<Sample> _buf = new List<Sample>();

    private PlayerMovement _pm;
    private bool _rewinding;

    // ── Aparición automática ──────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();

    private static void Ensure()
    {
        if (Instance != null) return;
        var pm = FindAnyObjectByType<PlayerMovement>();
        if (pm == null || FindAnyObjectByType<BeatManager>() == null) return;
        pm.gameObject.AddComponent<TimeRewinder>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _pm = GetComponent<PlayerMovement>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Grabación ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_rewinding || _pm == null || !_pm.enabled || _pm.IsFreeroam) return;
        BeatManager bm = BeatManager.Instance;
        if (bm == null || !bm.IsPlaying) return;

        float t = bm.SongTime;
        // Si el tiempo de la canción ha ido hacia atrás (rebobinado, checkpoint…), se olvida lo "futuro".
        while (_buf.Count > 0 && _buf[_buf.Count - 1].t >= t) _buf.RemoveAt(_buf.Count - 1);

        // Seguro = a pie en el suelo sin ninguna acción, o sentado en el kayak flotando.
        KayakController kayak = _pm.IsOnKayak ? CurrentKayak() : null;
        bool safe = kayak != null
            ? kayak.IsSeatedFloating
            : _pm.IsGroundedNow && !_pm.IsSliding && !_pm.IsDashing && !_pm.IsWallrunning &&
              !_pm.IsSlamming && !_pm.IsOnKayak && !_pm.IsOnZipline;
        _buf.Add(new Sample
        {
            t = t, pos = transform.position, yaw = _pm.LookYaw, pitch = _pm.LookPitch, safe = safe,
            kayak = kayak,
            kayakPos = kayak != null ? kayak.transform.position : Vector3.zero,
            kayakRot = kayak != null ? kayak.transform.rotation : Quaternion.identity,
        });

        while (_buf.Count > 2 && _buf[0].t < t - keepSeconds) _buf.RemoveAt(0);
    }

    private KayakController _kayakCache;

    /// <summary>El kayak en el que va sentado el jugador (si va en uno).</summary>
    private KayakController CurrentKayak()
    {
        if (_kayakCache != null && _kayakCache.IsSeatedFloating) return _kayakCache;
        foreach (var k in FindObjectsByType<KayakController>())
            if (k.IsSeatedFloating) { _kayakCache = k; return k; }
        return null;
    }

    // ── Rebobinar ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Intenta rebobinar (gasta un rebobinado). Devuelve false si no se puede (kayak, tirolina, jefe, sin momento
    /// seguro grabado…): entonces el choque sigue su curso normal.
    /// </summary>
    public bool TryRewind()
    {
        if (_rewinding || _pm == null) return false;
        // (En la tirolina: se busca un momento a pie de antes de engancharse; los de la tirolina no son seguros.)
        KayakController nowKayak = _pm.IsOnKayak ? CurrentKayak() : null;
        if (_pm.IsOnKayak && nowKayak == null) return false;   // (en el kayak pero no flotando sentado)
        var boss = FindAnyObjectByType<BossController>();
        if (boss != null && boss.FightInProgress) return false;
        BeatManager bm = BeatManager.Instance;
        if (bm == null || !bm.IsPlaying || bm.BeatDuration <= 0f || _buf.Count < 2) return false;

        float now  = bm.SongTime;
        float beat = bm.BeatDuration;
        int   b    = Mathf.FloorToInt(now / beat) - Mathf.Max(1, rewindBeats);

        // Beat entero más reciente (≥ N beats atrás) en el que el jugador estaba en un estado seguro.
        float target = -1f;
        for (; b >= 0; b--)
        {
            float tb = b * beat;
            if (tb < _buf[0].t) break;
            if (IsSafeAround(tb, 0.12f, nowKayak)) { target = tb; break; }
        }
        if (target < 0f) return false;
        if (!PlayerProgress.TryUseConsumable(ShopIds.Shield)) return false;
        RunStats.HelpUsed();

        StartCoroutine(RewindRoutine(now, target, nowKayak));
        return true;
    }

    /// <summary>
    /// ¿Estado seguro alrededor de 't'? Vale el mismo modo que ahora (a pie / en ESTE kayak) y, si ahora vas en un
    /// kayak, también ir A PIE ANTES de haberte montado en él (el ataúd volverá a su sitio de diseño).
    /// </summary>
    private bool IsSafeAround(float t, float margin, KayakController kayak)
    {
        bool found = false;
        foreach (Sample s in _buf)
        {
            if (s.t < t - margin || s.t > t + margin) continue;
            found = true;
            if (!s.safe) return false;
            if (s.kayak == kayak) continue;
            bool footBeforeBoarding = s.kayak == null && kayak != null && kayak.HasBeenActivated &&
                                      s.t < kayak.ActivatedSongTime - 0.05f;
            if (!footBeforeBoarding) return false;
        }
        return found;
    }

    /// <summary>Posición y mirada del jugador en el segundo 't' de la canción (interpolando lo grabado).</summary>
    private Sample PoseAt(float t)
    {
        if (t <= _buf[0].t) return _buf[0];
        for (int i = 1; i < _buf.Count; i++)
        {
            if (_buf[i].t < t) continue;
            Sample a = _buf[i - 1], c = _buf[i];
            float k = c.t > a.t ? (t - a.t) / (c.t - a.t) : 0f;
            return new Sample
            {
                t = t, pos = Vector3.Lerp(a.pos, c.pos, k),
                yaw = Mathf.LerpAngle(a.yaw, c.yaw, k), pitch = Mathf.Lerp(a.pitch, c.pitch, k), safe = a.safe && c.safe,
                kayak = c.kayak != null ? c.kayak : a.kayak,
                kayakPos = a.kayak != null && c.kayak != null ? Vector3.Lerp(a.kayakPos, c.kayakPos, k)
                         : (c.kayak != null ? c.kayakPos : a.kayakPos),
                kayakRot = a.kayak != null && c.kayak != null ? Quaternion.Slerp(a.kayakRot, c.kayakRot, k)
                         : (c.kayak != null ? c.kayakRot : a.kayakRot),
            };
        }
        return _buf[_buf.Count - 1];
    }

    private IEnumerator RewindRoutine(float from, float to, KayakController kayak)
    {
        _rewinding = true;
        BeatManager   bm = BeatManager.Instance;
        EnergyManager em = EnergyManager.Instance;
        var rhythm = FindAnyObjectByType<RhythmInput>();

        _pm.enabled = false;
        _pm.ForceExitZipline();                   // (si ibas colgado de la tirolina, te suelta)
        if (kayak != null) kayak.BeginRewind();   // el kayak también vuelve atrás (sin física mientras tanto)
        if (rhythm != null) rhythm.enabled = false;
        if (em != null) em.SetPaused(true);

        int left = PlayerShield.Count;
        ReviveFX.ShowMessage("◀◀  REBOBINADO", left > 0 ? $"Te quedan {left}" : "Era el último", new Color(0.25f, 0.55f, 1f, 0.5f));
        Debug.Log($"[Rebobinado] {from:F2} s → {to:F2} s (quedan {left}).");

        // La música, hacia atrás (efecto cinta). Si el clip no admite pitch negativo, simplemente no suena.
        float dur = Mathf.Max(0.2f, rewindDuration);
        bm.SetPlaybackPitch(-Mathf.Clamp((from - to) / dur, 1f, 4f));

        // Obstáculos que van con el reloj de la canción: también vuelven atrás.
        List<IRewindable> rewindables = FindRewindables();

        // Volver por el propio recorrido (posición + mirada), acelerando al principio y frenando al final.
        // El reloj del nivel sigue al instante que se muestra → osciladores, bloques que caen, etc. retroceden a la vez.
        float el = 0f;
        while (el < dur)
        {
            el += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(el / dur);
            float e = u * u * (3f - 2f * u);
            float shown = Mathf.Lerp(from, to, e);
            bm.SetRewindClock(shown);
            RewindAll(rewindables);
            Sample p = PoseAt(shown);
            if (kayak != null)
            {
                if (p.kayak == kayak) kayak.ApplyRewindPose(p.kayakPos, p.kayakRot);
                else if (kayak.HasBeenActivated && shown < kayak.ActivatedSongTime)
                    kayak.ApplyRewindPose(kayak.StartPosition, kayak.StartRotation);   // (antes de montarte: en su sitio)
            }
            _pm.ApplyRewindPose(p.pos, p.yaw, p.pitch);
            yield return null;
        }
        Sample end = PoseAt(to);
        if (kayak != null && end.kayak == kayak) kayak.ApplyRewindPose(end.kayakPos, end.kayakRot);
        // ¿Se vuelve a un momento en que aún NO ibas en este kayak? Entonces el ataúd vuelve a su estado de diseño.
        bool kayakReset = kayak != null && end.kayak != kayak && kayak.HasBeenActivated && to < kayak.ActivatedSongTime;
        _pm.ApplyRewindPose(end.pos, end.yaw, end.pitch);
        while (_buf.Count > 0 && _buf[_buf.Count - 1].t > to) _buf.RemoveAt(_buf.Count - 1);

        // Retomar la canción justo en ese punto (y con ella el reloj de beats de todo el nivel).
        bm.SetPlaybackPitch(0f);
        double dspStart = AudioSettings.dspTime + 0.12;
        bm.StartMusicFromAt(to, dspStart);
        RewindAll(rewindables);                    // (reloj definitivo: cada obstáculo queda como estaba en 'to')
        while (AudioSettings.dspTime < dspStart) yield return null;

        // Otros ataúdes que te montaste DESPUÉS del punto al que vuelves: también a su sitio.
        foreach (KayakController k in FindObjectsByType<KayakController>())
            if (k != kayak && k.HasBeenActivated && to < k.ActivatedSongTime && !k.IsSeatedFloating) k.ResetToStart();

        if (kayakReset) kayak.ResetToStart();     // (te baja del kayak: vuelves a ir a pie)
        _pm.ResetMotionAfterRewind();
        if (kayak != null && !kayakReset) kayak.EndRewind();
        _pm.enabled = true;
        if (rhythm != null) rhythm.enabled = true;
        if (em != null) em.SetPaused(false);
        _rewinding = false;
    }

    /// <summary>Todos los IRewindable del nivel (también los desactivados: p. ej. bloques que se ocultaron al acabar).</summary>
    private static List<IRewindable> FindRewindables()
    {
        var list = new List<IRewindable>();
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            if (mb is IRewindable r && mb.gameObject.scene.IsValid()) list.Add(r);
        return list;
    }

    private static void RewindAll(List<IRewindable> list)
    {
        foreach (IRewindable r in list)
        {
            if (r is Object o && o == null) continue;   // (destruido entretanto)
            try { r.OnTimeRewind(); }
            catch (System.Exception ex) { Debug.LogException(ex); }
        }
    }
}
