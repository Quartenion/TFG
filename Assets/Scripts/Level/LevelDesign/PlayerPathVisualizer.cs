using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Dibuja el recorrido grabado y los eventos de acción como gizmos en el Scene view.
/// Funciona en Edit mode y Play mode ([ExecuteAlways]).
///
/// LEYENDA:
///   Cyan          → línea del camino
///   Amarillo      → beat
///   Naranja       → inicio de compás (cada 4 beats)
///   Blanco  ↑     → salto
///   Magenta ↑↑    → doble salto (air jump)
///   Azul    →/←   → dash
///   Morado        → inicio de slide
///   Naranja claro → inicio de wallrun
///   Cyan claro ↗  → walljump
///   Verde   ↓     → aterrizaje
/// </summary>
[ExecuteAlways]
public class PlayerPathVisualizer : MonoBehaviour
{
    [Header("Datos")]
    public RecordedPath recordedPath;

    // ── Camino ────────────────────────────────────────────────────────────────
    [Header("Camino")]
    public bool  showPath  = true;
    public Color pathColor = new Color(0f, 0.9f, 1f, 0.45f);

    // ── Beats / Compases ──────────────────────────────────────────────────────
    [Header("Beats")]
    public bool  showBeats      = true;
    public Color beatColor      = new Color(1f, 0.95f, 0.1f, 1f);
    [Range(0.05f, 0.4f)]
    public float beatRadius     = 0.10f;
    public bool  showBeatLabels = true;

    [Header("Compases (cada 4 beats)")]
    public bool  showBars    = true;
    public Color barColor    = new Color(1f, 0.45f, 0f, 1f);
    [Range(0.1f, 0.6f)]
    public float barRadius   = 0.22f;
    public bool  showBarDropLine = true;

    // ── Estado tierra / aire ──────────────────────────────────────────────────
    [Header("Estado suelo / aire (opcional)")]
    public bool  showGroundState   = false;
    public Color groundedColor     = new Color(0.2f, 1f, 0.3f, 0.7f);
    public Color airborneColor     = new Color(1f, 0.2f, 0.2f, 0.7f);
    [Range(0.03f, 0.15f)]
    public float groundStateRadius = 0.05f;

    // ── Acciones ──────────────────────────────────────────────────────────────
    [Header("Acciones — visibilidad")]
    public bool showJumps    = true;
    public bool showAirJumps = true;
    public bool showDashes   = true;
    public bool showSlides   = true;
    public bool showWallruns = true;
    public bool showWalljumps = true;
    public bool showLandings = false;   // mucho ruido, off por defecto
    public bool showActionLabels = true;

    [Header("Acciones — tamaños")]
    [Range(0.05f, 0.5f)]
    public float actionSphereRadius = 0.18f;
    [Range(0.1f, 2f)]
    public float arrowLength        = 0.8f;

    // ── Filtro ────────────────────────────────────────────────────────────────
    [Header("Filtro de beats")]
    public int fromBeat = 0;
    public int toBeat   = -1;   // -1 = hasta el final

    // =========================================================================
    // Colores por tipo de acción
    // =========================================================================

    private static Color ColorFor(RecordedPath.ActionType type) => type switch
    {
        RecordedPath.ActionType.Jump      => new Color(1f,   1f,   1f,   1f),
        RecordedPath.ActionType.AirJump   => new Color(0.7f, 0.4f, 1f,   1f),
        RecordedPath.ActionType.Dash      => new Color(0.2f, 0.5f, 1f,   1f),
        RecordedPath.ActionType.Slide     => new Color(0.6f, 0.2f, 0.9f, 1f),
        RecordedPath.ActionType.SlideEnd  => new Color(0.5f, 0.15f, 0.7f, 0.5f),
        RecordedPath.ActionType.Wallrun   => new Color(1f,   0.6f, 0.1f, 1f),
        RecordedPath.ActionType.Walljump  => new Color(0.1f, 1f,   0.9f, 1f),
        RecordedPath.ActionType.Land      => new Color(0.3f, 1f,   0.4f, 0.8f),
        _                                 => Color.white,
    };

    private static string LabelFor(RecordedPath.ActionType type) => type switch
    {
        RecordedPath.ActionType.Jump      => "JUMP",
        RecordedPath.ActionType.AirJump   => "AIR JUMP",
        RecordedPath.ActionType.Dash      => "DASH",
        RecordedPath.ActionType.Slide     => "SLIDE",
        RecordedPath.ActionType.SlideEnd  => "SLIDE END",
        RecordedPath.ActionType.Wallrun   => "WALLRUN",
        RecordedPath.ActionType.Walljump  => "WALLJUMP",
        RecordedPath.ActionType.Land      => "LAND",
        _                                 => "?",
    };

    private bool IsVisible(RecordedPath.ActionType type) => type switch
    {
        RecordedPath.ActionType.Jump      => showJumps,
        RecordedPath.ActionType.AirJump   => showAirJumps,
        RecordedPath.ActionType.Dash      => showDashes,
        RecordedPath.ActionType.Slide     => showSlides,
        RecordedPath.ActionType.SlideEnd  => showSlides,
        RecordedPath.ActionType.Wallrun   => showWallruns,
        RecordedPath.ActionType.Walljump  => showWalljumps,
        RecordedPath.ActionType.Land      => showLandings,
        _                                 => true,
    };

    // =========================================================================
    // OnDrawGizmos
    // =========================================================================

    private void OnDrawGizmos()
    {
        if (recordedPath == null) return;

        int lo = fromBeat;
        int hi = toBeat < 0 ? int.MaxValue : toBeat;

        DrawPath(lo, hi);
        DrawGroundState(lo, hi);
        DrawBeatMarkers(lo, hi);
        DrawActions(lo, hi);
    }

    // ── Camino ────────────────────────────────────────────────────────────────

    private void DrawPath(int lo, int hi)
    {
        if (!showPath || recordedPath.frames == null) return;
        var frames = recordedPath.frames;
        Gizmos.color = pathColor;
        for (int i = 1; i < frames.Count; i++)
        {
            var a = frames[i - 1];
            var b = frames[i];
            if (a.beatIndex < lo || a.beatIndex > hi) continue;
            Gizmos.DrawLine(a.position, b.position);
        }
    }

    // ── Estado suelo / aire ───────────────────────────────────────────────────

    private void DrawGroundState(int lo, int hi)
    {
        if (!showGroundState || recordedPath.frames == null) return;
        foreach (var f in recordedPath.frames)
        {
            if (f.beatIndex < lo || f.beatIndex > hi) continue;
            Gizmos.color = f.isGrounded ? groundedColor : airborneColor;
            Gizmos.DrawSphere(f.position, groundStateRadius);
        }
    }

    // ── Beats y compases ──────────────────────────────────────────────────────

    private void DrawBeatMarkers(int lo, int hi)
    {
        if (!showBeats && !showBars) return;
        if (recordedPath.frames == null)        return;

        int lastBeat = -1;
        foreach (var f in recordedPath.frames)
        {
            if (f.beatIndex == lastBeat) continue;
            lastBeat = f.beatIndex;
            if (f.beatIndex < lo || f.beatIndex > hi) continue;

            bool isBar = f.beatIndex % 4 == 0;

            if (isBar && showBars)
            {
                Gizmos.color = barColor;
                Gizmos.DrawSphere(f.position, barRadius);
                if (showBarDropLine)
                {
                    Gizmos.color = new Color(barColor.r, barColor.g, barColor.b, 0.3f);
                    Gizmos.DrawLine(f.position, new Vector3(f.position.x, 0f, f.position.z));
                }
#if UNITY_EDITOR
                if (showBeatLabels)
                    Handles.Label(f.position + Vector3.up * (barRadius + 0.25f),
                                  $"B{f.beatIndex}  (bar {f.beatIndex / 4})");
#endif
            }
            else if (!isBar && showBeats)
            {
                Gizmos.color = beatColor;
                Gizmos.DrawSphere(f.position, beatRadius);
#if UNITY_EDITOR
                if (showBeatLabels)
                    Handles.Label(f.position + Vector3.up * (beatRadius + 0.12f),
                                  $"B{f.beatIndex}");
#endif
            }
        }
    }

    // ── Acciones ──────────────────────────────────────────────────────────────

    private void DrawActions(int lo, int hi)
    {
        if (recordedPath.actions == null) return;

        foreach (var a in recordedPath.actions)
        {
            if (!IsVisible(a.type)) continue;
            if (a.beatIndex < lo || a.beatIndex > hi) continue;

            Color c = ColorFor(a.type);
            Gizmos.color = c;

            // Esfera base
            Gizmos.DrawSphere(a.position, actionSphereRadius);

            // Flecha de dirección (si la acción tiene una)
            if (a.direction != Vector3.zero)
            {
                Vector3 tip = a.position + a.direction * arrowLength;
                Gizmos.DrawLine(a.position, tip);

#if UNITY_EDITOR
                // Punta de flecha con Handles
                Handles.color = c;
                Handles.ArrowHandleCap(
                    0, a.position, Quaternion.LookRotation(a.direction),
                    arrowLength, EventType.Repaint);
#endif
            }

#if UNITY_EDITOR
            if (showActionLabels)
            {
                string lbl = $"{LabelFor(a.type)}\nB{a.beatIndex}";
                Handles.color = c;
                Vector3 offset = a.direction != Vector3.zero
                    ? a.direction * (arrowLength + 0.15f)
                    : Vector3.up * (actionSphereRadius + 0.15f);
                Handles.Label(a.position + offset, lbl);
            }
#endif
        }
    }
}
