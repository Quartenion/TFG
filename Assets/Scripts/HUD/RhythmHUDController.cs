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
/// 4. Crea los dos prefabs (ver RhythmRingVisual y RhythmShockwave) y asígnalos aquí.
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
        if (beatManager == null || rhythmInput == null)
        {
            Debug.LogError("[RhythmHUDController] Faltan referencias. Asigna BeatManager y RhythmInput.", this);
            enabled = false;
            return;
        }

        beatManager.OnBeat.AddListener(SpawnRing);
        beatManager.OnHalfBeat.AddListener(SpawnHalfRing);
        rhythmInput.OnTimingResult.AddListener(HandleTimingResult);
    }

    private void OnDestroy()
    {
        if (beatManager != null) beatManager.OnBeat.RemoveListener(SpawnRing);
        if (beatManager != null) beatManager.OnHalfBeat.RemoveListener(SpawnHalfRing);
        if (rhythmInput  != null) rhythmInput.OnTimingResult.RemoveListener(HandleTimingResult);
    }

    // =========================================================================
    // SPAWN DE ANILLOS
    // =========================================================================

    private void SpawnRing()
    {
        if (ringPrefab == null || ringContainer == null) return;
        // Las notas entrantes solo se ven con el «Crosshair rítmico» de la tienda.
        if (PlayerProgress.GetLevel(ShopIds.RhythmSight) <= 0) return;

        float travelDuration = beatsAhead * beatManager.BeatDuration;

        RhythmRingVisual ring = Instantiate(ringPrefab, ringContainer);
        ring.Init(startDiameter, targetDiameter, travelDuration, beatManager, crosshair);
        _activeRings.Add(ring);

        // Limpiar de la lista cuando se destruya
        ring.OnExpired += () => _activeRings.Remove(ring);
    }

    [Header("Medios beats (contrato «Al compás»)")]
    [Tooltip("Tamaño de los spikes de las notas de MEDIO beat respecto a las normales.")]
    [Range(0.2f, 1f)]
    public float halfBeatSpikeScale = 0.55f;

    /// <summary>
    /// Con el contrato «Al compás» (acciones también en los medios beats) y el «Crosshair rítmico», entra una nota
    /// más pequeña que llega en cada MEDIO beat (a contratiempo).
    /// </summary>
    private void SpawnHalfRing()
    {
        if (!BeatManager.HalfBeatMoves) return;
        if (ringPrefab == null || ringContainer == null) return;
        if (PlayerProgress.GetLevel(ShopIds.RhythmSight) <= 0) return;
        float halfD = beatManager.HalfBeatDuration;
        if (halfD <= 0f) return;
        int halfIdx = Mathf.FloorToInt(beatManager.CorrectedTime / halfD);
        if (halfIdx % 2 == 0) return;   // (los beats enteros ya tienen su nota normal)

        RhythmRingVisual ring = Instantiate(ringPrefab, ringContainer);
        ring.Init(startDiameter, targetDiameter, beatsAhead * beatManager.BeatDuration, beatManager, crosshair);
        ring.MakeHalfBeat(halfBeatSpikeScale);
        _activeRings.Add(ring);
        ring.OnExpired += () => _activeRings.Remove(ring);
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
