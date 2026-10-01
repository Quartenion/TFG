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

}
