using UnityEngine;

/// <summary>
/// Qué caras de un obstáculo son letales para el jugador.
/// La clasificación Front/Back/Left/Right se hace en relación a la dirección
/// de avance del jugador (o del kayak) en el momento del impacto.
/// </summary>
[System.Flags]
public enum LethalFaces
{
    None   = 0,
    Top    = 1 << 0,   // El jugador aterriza encima del obstáculo
    Bottom = 1 << 1,   // El obstáculo golpea al jugador por abajo / cae sobre él
    Front  = 1 << 2,   // El jugador/kayak choca de frente contra el obstáculo
    Back   = 1 << 3,   // Choque por detrás (poco común)
    Left   = 1 << 4,   // Lateral izquierdo (respecto al avance)
    Right  = 1 << 5,   // Lateral derecho
    All    = ~0
}

/// <summary>
/// Componente de DATOS de un obstáculo. Sólo declara qué caras son letales y si
/// el kayak se rompe al embestirlo. No detecta nada por sí mismo: la detección la
/// hacen el jugador (<see cref="ObstacleCollision"/>) y el kayak
/// (<see cref="KayakController"/>), que leen esta configuración del obstáculo golpeado.
///
/// SETUP (cada obstáculo del nivel):
///   1. Layer del objeto = "Obstacle".
///   2. Un collider SÓLIDO (Box Collider recomendado, o Mesh Collider) que cubra
///      todo el volumen del obstáculo, desde el agua hacia arriba.
///   3. Añade este componente y ajusta "Lethal Faces".
///
///   NO añadas CharacterController ni ObstacleCollision a los obstáculos:
///   ObstacleCollision va SÓLO en el jugador (detección a pie).
/// </summary>
[AddComponentMenu("Level/Obstacle Info")]
[DisallowMultipleComponent]
public class ObstacleInfo : MonoBehaviour
{
    [Header("Caras letales")]
    [Tooltip("Qué caras de este obstáculo matan/rompen al jugador.\n\n" +
             "• Front  — cara contra la que se embiste de frente (la más común).\n" +
             "• Bottom — cara inferior: mata si el obstáculo cae encima o el kayak se mete por debajo.\n" +
             "• Top    — cara superior: desactivada por defecto para poder pasar/saltar por encima.\n" +
             "• Left/Right — laterales: desactivados para poder rozarlos sin morir.\n\n" +
             "La dirección 'Front' se calcula en cada impacto según el avance en ese instante.")]
    public LethalFaces lethalFaces = LethalFaces.Front | LethalFaces.Bottom;

#if UNITY_EDITOR
    [Header("Gizmo (solo editor)")]
    [Tooltip("Dibuja en la escena las caras letales (rojo). Al seleccionar el obstáculo\n" +
             "también muestra las caras seguras (verde tenue), etiquetas y la flecha de avance.")]
    public bool showFaceGizmo = true;

    // El sistema clasifica las caras según la dirección de AVANCE del jugador/kayak,
    // que en este nivel es el eje +Z del mundo. Por eso:
    //   Front = cara -Z (la que se embiste)   Back  = cara +Z
    //   Right = cara +X                        Left  = cara -X
    //   Top   = cara +Y                        Bottom= cara -Y
    // Si algún tramo avanzara en otra dirección, el gizmo (mundo +Z) no coincidiría
    // con la clasificación real de ese tramo.

    private static readonly Color _lethalFill = new Color(1f,   0.15f, 0.1f, 0.45f);
    private static readonly Color _lethalLine = new Color(1f,   0.1f,  0.1f, 0.9f);
    private static readonly Color _safeFill   = new Color(0.2f, 1f,    0.3f, 0.06f);
    private static readonly Color _safeLine   = new Color(0.2f, 1f,    0.3f, 0.30f);

    private void OnDrawGizmos()         { if (showFaceGizmo) DrawFaces(false); }
    private void OnDrawGizmosSelected() { if (showFaceGizmo) DrawFaces(true);  }

    private void DrawFaces(bool detailed)
    {
        if (!TryGetWorldBounds(out Bounds b)) return;
        Vector3 c = b.center, e = b.extents;

        DrawFace(c, e, Vector3.back,    LethalFaces.Front,  "FRONT", detailed);
        DrawFace(c, e, Vector3.forward, LethalFaces.Back,   "Back",  detailed);
        DrawFace(c, e, Vector3.right,   LethalFaces.Right,  "Right", detailed);
        DrawFace(c, e, Vector3.left,    LethalFaces.Left,   "Left",  detailed);
        DrawFace(c, e, Vector3.up,      LethalFaces.Top,    "Top",   detailed);
        DrawFace(c, e, Vector3.down,    LethalFaces.Bottom, "Bottom",detailed);

        if (detailed)
        {
            // Flecha de avance (+Z): el jugador/kayak llega desde -Z hacia la cara FRONT.
            Gizmos.color = Color.cyan;
            Vector3 tail = c + Vector3.back * (e.z + 2f);
            Vector3 head = c + Vector3.back * (e.z + 0.4f);
            Gizmos.DrawLine(tail, head);
            Gizmos.DrawLine(head, head + new Vector3( 0.35f, 0f, -0.35f));
            Gizmos.DrawLine(head, head + new Vector3(-0.35f, 0f, -0.35f));
            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.Label(tail, "avance →");
        }
    }

    private void DrawFace(Vector3 c, Vector3 e, Vector3 dir, LethalFaces face, string label, bool detailed)
    {
        bool lethal = (lethalFaces & face) != 0;
        if (!lethal && !detailed) return;   // sin seleccionar, solo se ven las letales

        Vector3 faceCenter = c + Vector3.Scale(dir, e);
        Vector3 size = new Vector3(
            Mathf.Abs(dir.x) > 0f ? 0.06f : e.x * 2f,
            Mathf.Abs(dir.y) > 0f ? 0.06f : e.y * 2f,
            Mathf.Abs(dir.z) > 0f ? 0.06f : e.z * 2f);

        Gizmos.color = lethal ? _lethalFill : _safeFill;
        Gizmos.DrawCube(faceCenter, size);
        Gizmos.color = lethal ? _lethalLine : _safeLine;
        Gizmos.DrawWireCube(faceCenter, size);

        if (detailed)
        {
            UnityEditor.Handles.color = lethal ? Color.red : new Color(0.4f, 1f, 0.5f, 0.9f);
            UnityEditor.Handles.Label(faceCenter, lethal ? label + " (LETAL)" : label);
        }
    }

    private bool TryGetWorldBounds(out Bounds bounds)
    {
        bounds = new Bounds(transform.position, Vector3.one);
        bool found = false;
        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            if (col == null) continue;
            if (!found) { bounds = col.bounds; found = true; }
            else          bounds.Encapsulate(col.bounds);
        }
        if (!found)
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!found) { bounds = r.bounds; found = true; }
                else          bounds.Encapsulate(r.bounds);
            }
        return found;
    }
#endif
}
