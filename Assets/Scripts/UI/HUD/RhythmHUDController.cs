using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controlador principal del HUD rítmico.
/// Spawna círculos concéntricos que viajan hacia el crosshair al ritmo de la música.
/// Cuando el jugador acierta, genera una onda roja que se contrae hacia el corazón.
///
/// SETUP EN UNITY:
/// 1. Canvas (Screen Space - Overlay) → añade este componente.
/// 2. Dentro del Canvas, crea un GameObject vacío "CrosshairCenter" en posición (0,0).
/// 3. Dentro de CrosshairCenter:
///      - "TargetRing"   → Image con sprite de anillo, 120x120 px, color gris oscuro (referencia visual).
///      - "CrosshairImg" → tu crosshair actual (cambia su color a gris oscuro en el Inspector).
///      - "RingContainer" → GameObject vacío, aquí se spawnean los anillos entrantes.
/// 4. Crea el prefab del anillo (ver RhythmRingVisual) y asígnalo aquí.
/// 5. Asigna BeatManager y RhythmInput.
/// </summary>
public class RhythmHUDController : MonoBehaviour
{
    [Header("Referencias")]
    public BeatManager  beatManager;
    public RhythmInput  rhythmInput;

    [Tooltip("Crosshair. Si se asigna, los spikes de cada nota copian su rotación para coincidir al converger.")]
    public CrosshairController crosshair;

    [Header("UI — contenedor central")]
    [Tooltip("GameObject vacío centrado en pantalla donde van los anillos. " +
             "Debe ser hijo del Canvas y tener posición (0,0).")]
    public RectTransform ringContainer;

    [Header("Prefabs")]
    [Tooltip("Prefab: Image con sprite de anillo + componente RhythmRingVisual.")]
    public RhythmRingVisual ringPrefab;

    [Header("Tamaños (píxeles en Canvas)")]
    [Tooltip("Diámetro del anillo objetivo fijo (TargetRing). Los anillos entrantes llegan aquí.")]
    public float targetDiameter = 120f;

    [Tooltip("Diámetro inicial de cada anillo cuando aparece. Cuanto mayor, más anticipación visual.")]
    public float startDiameter  = 300f;

    [Header("Anticipación")]
    [Tooltip("Cuántos beats de anticipación. 2 = el jugador ve el anillo 2 beats antes de tener que pulsarlo.")]
    [Range(1, 4)]
    public int beatsAhead = 2;

    [Tooltip("Al acertar sólo se absorbe un anillo si su Progress está dentro de esta ventana\n" +
             "alrededor de 1.0 (el centro). Evita 'comerse' un anillo que aún se acerca.\n" +
             "~0.6 ≈ un beat alrededor del centro.")]
    [Range(0.1f, 1.5f)]
    public float absorbProgressWindow = 0.6f;

    // ─────────────────────────────────────────────────────────────────────────

    private readonly List<RhythmRingVisual> _activeRings = new List<RhythmRingVisual>();

    // =========================================================================
    // UNITY
    // =========================================================================

    private void Start()
    {
        // Por si se pierde la referencia entre prefabs (Canvas ↔ Managers): se buscan en la escena.
        if (beatManager == null) beatManager = BeatManager.Instance != null ? BeatManager.Instance : FindAnyObjectByType<BeatManager>();
        if (rhythmInput == null) rhythmInput = FindAnyObjectByType<RhythmInput>();
        if (beatManager == null || rhythmInput == null)
        {
            Debug.LogError("[RhythmHUDController] Faltan referencias. Asigna BeatManager y RhythmInput.", this);
            enabled = false;
            return;
        }

        rhythmInput.OnTimingResult.AddListener(HandleTimingResult);
    }

    private void OnDestroy()
    {
        if (rhythmInput  != null) rhythmInput.OnTimingResult.RemoveListener(HandleTimingResult);
    }

    // =========================================================================
    // SPAWN DE ANILLOS (guiado por el TIEMPO DE LA CANCIÓN, no por eventos)
    // =========================================================================
    //
    // Cada nota sabe en qué segundo de la canción tiene que llegar (su beat) y su posición se calcula con el reloj
    // de la música. Cada frame se asegura de que existen las notas de los próximos 'beatsAhead' beats:
    //   • Al empezar la canción (tras el 3-2-1-¡YA!) ya salen bien desde el primer beat, sin notas "de más" ni
    //     huecos (antes, un evento de beat disparado con la canción aún programada lanzaba una nota a destiempo).
    //   • Tras un rebobinado se rehacen solas desde el nuevo punto.

    [Header("Medios beats (contrato «Al compás»)")]
    [Tooltip("Tamaño de los spikes de las notas de MEDIO beat respecto a las normales.")]
    [Range(0.2f, 1f)]
    public float halfBeatSpikeScale = 0.55f;

    private bool _wasRewinding;

    private void Update()
    {
        bool rew = TimeRewinder.IsRewinding;
        if (rew && !_wasRewinding) ClearRings();   // empieza el rebobinado: fuera todas las notas en vuelo
        _wasRewinding = rew;
        if (rew) return;

        EnsureUpcomingRings();
    }

    private void ClearRings()
    {
        foreach (RhythmRingVisual r in _activeRings) if (r != null) Destroy(r.gameObject);
        _activeRings.Clear();
    }

    /// <summary>Crea las notas que falten para los beats (y medios beats con «Al compás») de los próximos 'beatsAhead'.</summary>
    private void EnsureUpcomingRings()
    {
        if (ringPrefab == null || ringContainer == null || beatManager == null) return;
        // (También mientras la canción está programada y aún no suena: así sale la nota del PRIMER beat, el 0.
        //  Antes se usaba el tiempo recortado a 0 y el bucle empezaba en el beat 1: la primera nota no aparecía.)
        if (!beatManager.IsPlaying && !(beatManager.HasClock && beatManager.ClockSeconds < 0.0)) return;
        // Las notas entrantes solo se ven con el «Crosshair rítmico» de la tienda.
        if (PlayerProgress.GetLevel(ShopIds.RhythmSight) <= 0) return;
        float beatDur = beatManager.BeatDuration;
        if (beatDur <= 0f) return;

        float travel = beatsAhead * beatDur;
        float bt     = beatManager.CorrectedTimeUnclamped / beatDur;     // posición actual, en beats (< 0 antes de empezar)
        bool  halves = BeatManager.HalfBeatMoves;
        float step   = halves ? 0.5f : 1f;

        for (float n = Mathf.Floor(bt / step) * step + step; n <= bt + beatsAhead + 0.0001f; n += step)
        {
            float arrival = n * beatDur;
            if (HasRingFor(arrival)) continue;
            bool half = Mathf.Abs(n - Mathf.Round(n)) > 0.01f;
            RhythmRingVisual ring = Instantiate(ringPrefab, ringContainer);
            ring.Init(startDiameter, targetDiameter, travel, beatManager, crosshair);
            if (half) ring.MakeHalfBeat(halfBeatSpikeScale);
            ring.SetArrival(arrival);
            _activeRings.Add(ring);
            ring.OnExpired += () => _activeRings.Remove(ring);
        }
    }

    private bool HasRingFor(float arrival)
    {
        foreach (RhythmRingVisual r in _activeRings)
            if (r != null && Mathf.Abs(r.ArrivalSongTime - arrival) < 0.01f) return true;
        return false;
    }

    // =========================================================================
    // FEEDBACK DE TIMING
    // =========================================================================

    private void HandleTimingResult(TimingResult result)
    {
        if (result == TimingResult.Miss) return;

        // Absorber el anillo más cercano a la posición objetivo
        RhythmRingVisual targetRing = FindNearestToTarget();
        targetRing?.TriggerHitAbsorption();

    }

    /// <summary>
    /// Devuelve el anillo cuyo progreso está más cerca de 1.0 (= en el objetivo).
    /// </summary>
    private RhythmRingVisual FindNearestToTarget()
    {
        RhythmRingVisual best    = null;
        float            bestErr = float.MaxValue;

        foreach (RhythmRingVisual r in _activeRings)
        {
            if (r == null || r.IsConsumed || r.IsHalfBeat) continue;   // saltar destruidos, absorbidos y los de medio beat
            float err = Mathf.Abs(r.Progress - 1f);
            if (err < bestErr) { bestErr = err; best = r; }
        }

        // Sólo absorber si hay un anillo razonablemente cerca del centro; si no,
        // no comerse uno lejano que todavía se está acercando.
        return bestErr <= absorbProgressWindow ? best : null;
    }

}
