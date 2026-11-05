using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controlador principal del HUD rítmico. Crea círculos concéntricos que viajan hacia el punto de mira al ritmo de
/// la música, y cuando el jugador acierta, genera una onda roja que se contrae hacia el corazón.
///
/// Configuración en Unity.
/// 1. Se añade este componente a un Canvas (Screen Space - Overlay).
/// 2. Dentro del Canvas se crea un GameObject vacío, "CrosshairCenter", en la posición (0,0).
/// 3. Dentro de CrosshairCenter se crean estos objetos.
///      - "TargetRing", un Image con el sprite de un anillo, de 120x120 px y gris oscuro, que sirve de referencia.
///      - "CrosshairImg", el punto de mira, con su color cambiado a gris oscuro en el Inspector.
///      - "RingContainer", un GameObject vacío en el que se crean los anillos que llegan.
/// 4. Se crea el prefab del anillo (ver RhythmRingVisual) y se asigna aquí.
/// 5. Se asignan BeatManager y RhythmInput.
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

    // -=+=-

    private readonly List<RhythmRingVisual> _activeRings = new List<RhythmRingVisual>();

    // -=+=- Unity -=+=-

    private void Start()
    {
        // Por si se pierde la referencia entre prefabs (Canvas y Managers), se buscan en la escena.
        if (beatManager == null) beatManager = BeatManager.Instance != null ? BeatManager.Instance : FindAnyObjectByType<BeatManager>();
        if (rhythmInput == null) rhythmInput = FindAnyObjectByType<RhythmInput>();
        if (beatManager == null || rhythmInput == null)
        {
            Debug.LogError("[RhythmHUDController] Faltan referencias. Asigna BeatManager y RhythmInput.", this);
            enabled = false;
            return;
        }

        rhythmInput.OnTimingResult.AddListener(HandleTimingResult);

        // Orden de dibujo. Las notas se dibujan justo encima del aro gris del punto de mira ("Ring"), ya que, si no, el
        // aro blanco de una nota que llega al punto de mira ("hittable") quedaría tapado por el gris.
        if (crosshair != null && ringContainer != null)
        {
            Transform xr = crosshair.transform.Find("Ring");
            if (xr != null && xr.parent == ringContainer.parent && ringContainer.GetSiblingIndex() < xr.GetSiblingIndex())
                ringContainer.SetSiblingIndex(xr.GetSiblingIndex());   // queda justo después del aro
        }
    }

    private void OnDestroy()
    {
        if (rhythmInput  != null) rhythmInput.OnTimingResult.RemoveListener(HandleTimingResult);
    }

    // -=+=- Creación de los anillos, guiada por el tiempo de la canción y no por eventos -=+=-
    // Cada nota sabe en qué segundo de la canción tiene que llegar (su beat), y su posición se calcula con el reloj de
    // la música. En cada fotograma se comprueba que existan las notas de los próximos 'beatsAhead' beats. Así, al empezar
    // la canción después de la cuenta atrás, las notas salen bien desde el primer beat, sin notas de más ni huecos, y
    // después de un rebobinado se vuelven a crear solas desde el nuevo punto.

    [Header("Medios beats (contrato «Al compás»)")]
    [Tooltip("Tamaño de los spikes de las notas de MEDIO beat respecto a las normales.")]
    [Range(0.2f, 1f)]
    public float halfBeatSpikeScale = 0.55f;

    private bool _wasRewinding;

    private void Update()
    {
        bool rew = TimeRewinder.IsRewinding;
        if (rew && !_wasRewinding) ClearRings();   // empieza el rebobinado, así que se quitan todas las notas que están en camino
        _wasRewinding = rew;
        if (rew) return;

        EnsureUpcomingRings();
        HoldRingWhileFrozen();
    }

    /// <summary>
    /// Con el tutorial parado justo en un beat, la nota de ese beat se queda en el punto de mira en estado "hittable"
    /// (blanca), para que se vea que es el momento del latido. Si todavía no existe, se crea.
    /// </summary>

    private bool NearBeat()
    {
        float bd = beatManager.BeatDuration;
        if (bd <= 0f) return false;
        float now = beatManager.CorrectedTimeUnclamped;
        return Mathf.Abs(now - Mathf.Round(now / bd) * bd) <= beatManager.GoodWindow;
    }


    private void HoldRingWhileFrozen()
    {
        bool hold = TutorialGate.Frozen && beatManager != null && ringPrefab != null && ringContainer != null;
        if (!hold) return;
        if (PlayerProgress.GetLevel(ShopIds.RhythmSight) <= 0) return;
        float beatDur = beatManager.BeatDuration;
        if (beatDur <= 0f) return;
        float now = beatManager.CorrectedTimeUnclamped;
        float arrival = Mathf.Round(now / beatDur) * beatDur;
        if (Mathf.Abs(now - arrival) > beatManager.GoodWindow) return;   // parado lejos de un beat, no se hace nada
        RhythmRingVisual ring = null;
        foreach (RhythmRingVisual r in _activeRings)
            if (r != null && Mathf.Abs(r.ArrivalSongTime - arrival) < 0.01f) { ring = r; break; }
        if (ring == null)
        {
            ring = Instantiate(ringPrefab, ringContainer);
            ring.Init(startDiameter, targetDiameter, beatsAhead * beatDur, beatManager, crosshair);
            ring.SetArrival(arrival);
            _activeRings.Add(ring);
            RhythmRingVisual created = ring;
            ring.OnExpired += () => _activeRings.Remove(created);
        }
        if (ring.transform.GetSiblingIndex() != ring.transform.parent.childCount - 1) ring.transform.SetAsLastSibling();   // encima de la siguiente nota
        ring.HoldHittable();
    }

    private void ClearRings()
    {
        foreach (RhythmRingVisual r in _activeRings) if (r != null) Destroy(r.gameObject);
        _activeRings.Clear();
    }

    /// <summary>Crea las notas que falten para los próximos 'beatsAhead' beats (y medios beats con "Al compás").</summary>
    private void EnsureUpcomingRings()
    {
        if (ringPrefab == null || ringContainer == null || beatManager == null) return;
        // También mientras la canción está programada y todavía no suena, para que salga la nota del primer beat, el 0.
        if (!beatManager.IsPlaying && !(beatManager.HasClock && beatManager.ClockSeconds < 0.0)) return;
        // Las notas solo se ven con el "Crosshair rítmico" de la tienda.
        if (PlayerProgress.GetLevel(ShopIds.RhythmSight) <= 0) return;
        float beatDur = beatManager.BeatDuration;
        if (beatDur <= 0f) return;

        float travel = beatsAhead * beatDur;
        float bt     = beatManager.CorrectedTimeUnclamped / beatDur;     // posición actual en beats (negativa antes de empezar)
        bool  halves = BeatManager.HalfBeatMoves;
        float step   = halves ? 0.5f : 1f;

        for (float n = Mathf.Floor(bt / step) * step + step; n <= bt + beatsAhead + 0.0001f; n += step)
        {
            float arrival = n * beatDur;
            if (HasRingFor(arrival)) continue;
            bool half = Mathf.Abs(n - Mathf.Round(n)) > 0.01f;
            RhythmRingVisual ring = Instantiate(ringPrefab, ringContainer);
            // La nota nueva va debajo de las anteriores, ya que su aro (gris, lejos) tiene el tamaño del punto de mira y, si
            // se dibujara encima, taparía el aro blanco de la nota que está llegando ("hittable").
            ring.transform.SetAsFirstSibling();
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

    // -=+=- Indicación del resultado -=+=-

    private void HandleTimingResult(TimingResult result)
    {
        if (result == TimingResult.Miss) return;

        // Se absorbe el anillo más cercano a la posición objetivo.
        RhythmRingVisual targetRing = FindNearestToTarget();
        targetRing?.TriggerHitAbsorption();

    }

    /// <summary>Devuelve el anillo cuyo progreso está más cerca de 1, es decir, del objetivo.</summary>
    private RhythmRingVisual FindNearestToTarget()
    {
        RhythmRingVisual best    = null;
        float            bestErr = float.MaxValue;

        foreach (RhythmRingVisual r in _activeRings)
        {
            if (r == null || r.IsConsumed || r.IsHalfBeat) continue;   // se saltan los destruidos, los absorbidos y los de medio beat
            float err = Mathf.Abs(r.Progress - 1f);
            if (err < bestErr) { bestErr = err; best = r; }
        }

        // Solo se absorbe si hay un anillo razonablemente cerca del centro, para no quitar uno lejano que todavía se está
        // acercando.
        return bestErr <= absorbProgressWindow ? best : null;
    }

}
