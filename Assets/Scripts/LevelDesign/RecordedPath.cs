using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject que almacena el recorrido grabado del jugador:
/// frames de posición + eventos de acción (salto, dash, slide…).
///
/// CREAR EL ASSET:
///   Assets → clic derecho → Create → TFG → Recorded Path
///
/// Los datos sobreviven al salir del Play mode porque StopRecording()
/// llama a SetDirty + SaveAssets, escribiéndolos al .asset en disco.
/// Cada grabación sobreescribe la anterior.
/// </summary>
[CreateAssetMenu(fileName = "RecordedPath", menuName = "TFG/Recorded Path")]
public class RecordedPath : ScriptableObject
{
    // ── Frame de posición ─────────────────────────────────────────────────────

    [System.Serializable]
    public struct Frame
    {
        public Vector3 position;
        public float   songTime;
        public int     beatIndex;
        public bool    isGrounded;
    }

    // ── Evento de acción ──────────────────────────────────────────────────────

    [System.Serializable]
    public struct ActionEvent
    {
        public ActionType type;
        public Vector3    position;
        public Vector3    direction;   // útil para Dash (lateral) y Walljump
        public float      songTime;
        public int        beatIndex;
    }

    public enum ActionType
    {
        Jump,
        AirJump,    // doble salto
        Dash,
        Slide,
        SlideEnd,
        Wallrun,
        Walljump,
        Land,       // aterrizaje detectado por transición de frames
    }

    // ── Datos ─────────────────────────────────────────────────────────────────

    [Header("Metadatos de sesión")]
    public float bpm;
    public float walkSpeed;

    [Header("Frames de posición")]
    public List<Frame>       frames  = new List<Frame>();

    [Header("Eventos de acción")]
    public List<ActionEvent> actions = new List<ActionEvent>();

    // =========================================================================

    public void Clear()
    {
        frames.Clear();
        actions.Clear();
        bpm       = 0f;
        walkSpeed = 0f;
    }

    /// <summary>Posición interpolada en un songTime arbitrario.</summary>
    public bool TryGetPositionAtTime(float songTime, out Vector3 position)
    {
        position = Vector3.zero;
        if (frames == null || frames.Count == 0) return false;

        int lo = 0, hi = frames.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (frames[mid].songTime < songTime) lo = mid + 1;
            else hi = mid;
        }

        if (lo == 0) { position = frames[0].position; return true; }

        var a = frames[lo - 1];
        var b = frames[lo];
        float t = Mathf.InverseLerp(a.songTime, b.songTime, songTime);
        position = Vector3.Lerp(a.position, b.position, t);
        return true;
    }
}
