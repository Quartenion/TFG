using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// (SOLO DEBUG / DISEÑO DE NIVELES) Marca en la vista de escena, sobre el recorrido grabado del jugador
/// (RecordedPath, el que graba PlayerPathRecorder), DÓNDE estaba el jugador en cada beat de la canción. Con los
/// botones del Inspector coloca una moneda en cada uno de esos puntos (o cada N beats, en un rango) y las borra.
///
/// USO:
///   1. Graba una partida con PlayerPathRecorder (juega el nivel entero y sal del Play).
///   2. Crea un objeto vacío en la escena y añade este componente (o Tools → Level Design → Colocador de monedas).
///   3. Asigna el RecordedPath. Los marcadores (esferas numeradas) aparecen en la vista de escena.
///   4. Ajusta el rango de beats / cada cuántos beats / altura y pulsa «Colocar monedas en cada beat».
/// No hace nada en el juego: solo sirve en el editor.
/// </summary>
[AddComponentMenu("Level Design/Beat Coin Placer (debug)")]
public class BeatCoinPlacer : MonoBehaviour
{
    [Header("Recorrido")]
    [Tooltip("Recorrido grabado del jugador (lo rellena PlayerPathRecorder).")]
    public RecordedPath path;
    [Tooltip("BPM de la canción. 0 = el guardado en el recorrido o el del BeatManager de la escena.")]
    public float bpmOverride = 0f;

    [Header("Qué beats")]
    [Tooltip("Primer beat (incluido).")]
    [Min(0)] public int fromBeat = 0;
    [Tooltip("Último beat (incluido). -1 = hasta el final del recorrido.")]
    public int toBeat = -1;
    [Tooltip("Una moneda cada N beats: 1 = en todos, 0.5 = cada medio beat, 2 = uno sí y otro no…")]
    [Min(0.25f)] public float everyBeats = 1f;
    [Tooltip("Desfase en beats (0.5 = en los medios beats).")]
    [Range(0f, 0.99f)] public float beatOffset = 0f;

    [Header("Monedas")]
    [Tooltip("Prefab de moneda (opcional). Vacío = objeto con el componente Coin (moneda dorada por código).")]
    public GameObject coinPrefab;
    [Tooltip("Altura extra sobre la posición del jugador (su centro).")]
    public float heightOffset = 0f;
    [Tooltip("Solo en los beats en que el jugador iba por el SUELO.")]
    public bool onlyGrounded = false;
    [Tooltip("Objeto padre de las monedas generadas (se crea solo).")]
    public Transform coinsParent;

    [Header("Marcadores (vista de escena)")]
    public bool showMarkers = true;
    public bool showBeatNumbers = true;
    public Color markerColor = new Color(1f, 0.8f, 0.1f, 0.9f);
    [Min(0.05f)] public float markerSize = 0.3f;

    /// <summary>Un punto del recorrido en un beat.</summary>
    public struct BeatPoint { public float beat; public Vector3 pos; public bool grounded; }

    public float Bpm
    {
        get
        {
            if (bpmOverride > 0f) return bpmOverride;
            if (path != null && path.bpm > 0f) return path.bpm;
            var bm = FindAnyObjectByType<BeatManager>();
            return bm != null ? bm.Bpm : 120f;
        }
    }

    /// <summary>Frames limpios: solo con canción sonando y en orden de tiempo (quita lo "deshecho" por rebobinados).</summary>
    private List<RecordedPath.Frame> CleanFrames()
    {
        var list = new List<RecordedPath.Frame>();
        if (path == null) return list;
        foreach (RecordedPath.Frame f in path.frames)
        {
            if (f.songTime <= 0.001f) continue;
            while (list.Count > 0 && list[list.Count - 1].songTime >= f.songTime) list.RemoveAt(list.Count - 1);
            list.Add(f);
        }
        return list;
    }

    /// <summary>Posición del jugador en cada beat elegido (interpolando el recorrido).</summary>
    public List<BeatPoint> ComputeBeatPoints(bool applyFilters = true)
    {
        var pts = new List<BeatPoint>();
        List<RecordedPath.Frame> fr = CleanFrames();
        if (fr.Count < 2) return pts;
        float beatDur = 60f / Mathf.Max(1f, Bpm);
        float tEnd = fr[fr.Count - 1].songTime;
        int lastBeat = Mathf.FloorToInt(tEnd / beatDur);
        float b0 = applyFilters ? Mathf.Max(0, fromBeat) : 0;
        float b1 = applyFilters && toBeat >= 0 ? Mathf.Min(toBeat, lastBeat + 1) : lastBeat + 1;
        // (Los marcadores "de fondo" usan la rejilla más fina entre 1 beat y el paso elegido.)
        float step = applyFilters ? Mathf.Max(0.25f, everyBeats) : Mathf.Min(1f, Mathf.Max(0.25f, everyBeats));
        float off = applyFilters ? beatOffset : 0f;

        int j = 1;
        for (int i = 0; ; i++)
        {
            float b = b0 + i * step;
            if (b > b1 + 0.0001f) break;
            float t = (b + off) * beatDur;
            if (t < fr[0].songTime || t > tEnd) continue;
            while (j < fr.Count - 1 && fr[j].songTime < t) j++;
            RecordedPath.Frame a = fr[j - 1], c = fr[j];
            float k = c.songTime > a.songTime ? Mathf.Clamp01((t - a.songTime) / (c.songTime - a.songTime)) : 0f;
            var p = new BeatPoint { beat = b + off, pos = Vector3.Lerp(a.position, c.position, k), grounded = a.isGrounded && c.isGrounded };
            if (applyFilters && onlyGrounded && !p.grounded) continue;
            pts.Add(p);
        }
        return pts;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showMarkers || path == null) return;
        List<BeatPoint> all = ComputeBeatPoints(false);
        var chosen = new HashSet<int>();
        foreach (BeatPoint p in ComputeBeatPoints(true)) chosen.Add(Mathf.RoundToInt(p.beat * 100f));

        var style = new GUIStyle { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = markerColor } };
        foreach (BeatPoint p in all)
        {
            bool sel = chosen.Contains(Mathf.RoundToInt(p.beat * 100f));
            bool whole = Mathf.Abs(p.beat - Mathf.Round(p.beat)) < 0.01f;
            bool bar = whole && Mathf.RoundToInt(p.beat) % 4 == 0;          // primer beat del compás, más grande
            float size = markerSize * (bar ? 1.4f : whole ? 1f : 0.6f);     // medios beats, más pequeños
            Gizmos.color = sel ? markerColor : new Color(markerColor.r, markerColor.g, markerColor.b, 0.25f);
            Vector3 at = p.pos + Vector3.up * heightOffset;
            Gizmos.DrawSphere(at, size);
            if (showBeatNumbers && sel)
                UnityEditor.Handles.Label(at + Vector3.up * (markerSize * 2f), p.beat.ToString("0.#"), style);
        }
    }
#endif
}
