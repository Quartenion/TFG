using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Coloca este componente en cualquier obstáculo que quieras alinear a un beat.
/// Al mover el obstáculo en la escena, el gizmo muestra en tiempo real en qué beat
/// está y si está alineado correctamente.
///
/// FLUJO DE TRABAJO:
///   1. Pon el obstáculo aproximadamente donde quieres.
///   2. Mira el gizmo:
///        VERDE  = está en un beat exacto ✓
///        AMARILLO = casi, arrástralo un poco
///        ROJO   = desalineado, usa "Snap to nearest beat"
///   3. Clic derecho sobre el componente → "Snap to nearest beat".
///      El obstáculo se moverá exactamente al beat más cercano.
///
/// SETUP:
///   1. Añade este componente al obstáculo (o a su prefab).
///   2. Asigna el BeatGridVisualizer de la escena en el campo _visualizer.
///      Eso le da acceso al BPM, walkSpeed y al punto de origen.
/// </summary>
[ExecuteAlways]
public class BeatSnapHelper : MonoBehaviour
{
    [Header("Referencia a la cuadrícula")]
    [Tooltip("BeatGridVisualizer de la escena. Define el origen, la dirección y el tamaño de celda.")]
    [SerializeField] private BeatGridVisualizer _visualizer;

    [Header("Info (solo lectura)")]
    [Tooltip("Beat al que corresponde esta posición actualmente. " +
             "Si es decimal, el obstáculo no está alineado.")]
    [SerializeField] private float _currentBeat;

    [Tooltip("Beat exacto al que se irá si usas 'Snap to nearest beat'.")]
    [SerializeField] private int   _snappedBeat;

    [Tooltip("Error de alineación actual en metros. " +
             "< 0.05 m = verde; < 0.3 m = amarillo; resto = rojo.")]
    [SerializeField] private float _alignmentError;

    // =========================================================================
    // ACTUALIZACIÓN CONTINUA (solo para actualizar los campos Info en Inspector)
    // =========================================================================

    private void Update()
    {
        if (_visualizer == null) return;

        float cell = _visualizer.CellSize;
        if (cell < 0.001f) return;

        float proj        = ProjectedDistance();
        _currentBeat      = proj / cell;
        _snappedBeat      = Mathf.RoundToInt(_currentBeat);
        float snapDist    = _snappedBeat * cell;
        _alignmentError   = Mathf.Abs(proj - snapDist);
    }

    // =========================================================================
    // SNAP  (click derecho → "Snap to nearest beat")
    // =========================================================================

    /// <summary>Mueve el obstáculo al beat ENTERO más cercano, conservando su lateral y su altura.</summary>
    [ContextMenu("Snap to nearest beat")]
    public void SnapToNearestBeat() => SnapToIncrement(1f, "Snap to beat");

    /// <summary>Mueve el obstáculo al MEDIO beat más cercano (0, 0.5, 1, 1.5…), conservando lateral y altura.</summary>
    [ContextMenu("Snap to nearest half beat")]
    public void SnapToNearestHalfBeat() => SnapToIncrement(0.5f, "Snap to half beat");

    /// <summary>
    /// Encaja el obstáculo al múltiplo de beat más cercano según <paramref name="beatFraction"/>
    /// (1 = beat entero, 0.5 = medio beat), conservando su posición lateral y vertical.
    /// </summary>
    private void SnapToIncrement(float beatFraction, string undoLabel)
    {
        if (_visualizer == null)
        {
            Debug.LogWarning("[BeatSnapHelper] Asigna un BeatGridVisualizer primero.", this);
            return;
        }

        float cell = _visualizer.CellSize;
        if (cell < 0.001f) return;

        float increment = cell * beatFraction;
        if (increment < 0.001f) return;

        Vector3 origin  = _visualizer.Origin;
        Vector3 forward = _visualizer.Forward;

        // Proyección del obstáculo sobre el eje del nivel
        float proj        = ProjectedDistance();
        int   steps       = Mathf.RoundToInt(proj / increment);
        float snappedDist = steps * increment;

        // Componente lateral (perpendicular al eje, incluye Y) — se conserva
        Vector3 lateral = transform.position - origin - forward * proj;

        // Nueva posición
        Vector3 newPos  = origin + forward * snappedDist + lateral;

#if UNITY_EDITOR
        Undo.RecordObject(transform, undoLabel);
#endif
        transform.position = newPos;

        float beat = snappedDist / cell;
        Debug.Log($"[BeatSnapHelper] '{name}' encajado en Beat {beat:0.##} " +
                  $"({snappedDist:F2} m desde el origen)", this);
    }

    // =========================================================================
    // UTILIDAD
    // =========================================================================

    /// <summary>
    /// Distancia proyectada del obstáculo sobre el eje de avance del nivel,
    /// medida desde el origen del BeatGridVisualizer.
    /// </summary>
    private float ProjectedDistance()
    {
        if (_visualizer == null) return 0f;
        return Vector3.Dot(transform.position - _visualizer.Origin, _visualizer.Forward);
    }

    // =========================================================================
    // GIZMOS
    // =========================================================================

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (_visualizer == null) return;

        float cell = _visualizer.CellSize;
        if (cell < 0.001f) return;

        float proj       = ProjectedDistance();
        float beatF      = proj / cell;
        int   nearBeat   = Mathf.RoundToInt(beatF);
        float err        = Mathf.Abs(beatF - nearBeat);   // 0 = perfecto, 0.5 = máximo error

        // Color del gizmo: verde (alineado) → amarillo (cerca) → rojo (desalineado)
        Color gizmoColor;
        if (err < 0.05f)
            gizmoColor = new Color(0.1f, 0.9f, 0.1f, 0.85f);   // verde
        else if (err < 0.2f)
            gizmoColor = new Color(1f, 0.8f, 0f, 0.85f);        // amarillo
        else
            gizmoColor = new Color(0.95f, 0.15f, 0.1f, 0.85f); // rojo

        Gizmos.color = gizmoColor;

        // Aro alrededor del obstáculo
        Vector3 center = transform.position;
        DrawCircle(center, _visualizer.Forward, 0.45f, 32);

        // Línea vertical indicadora
        Gizmos.DrawLine(center, center + Vector3.up * 0.9f);
        Gizmos.DrawSphere(center + Vector3.up * 0.95f, 0.06f);

        // (etiqueta de texto eliminada — ver Inspector para info de beat)
    }

    /// <summary>Dibuja un círculo en el espacio world usando Gizmos.DrawLine.</summary>
    private void DrawCircle(Vector3 center, Vector3 normal, float radius, int segments)
    {
        normal = normal.normalized;
        Vector3 worldUp = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f
            ? Vector3.forward : Vector3.up;
        Vector3 right = Vector3.Cross(normal, worldUp).normalized;
        Vector3 up    = Vector3.Cross(right, normal).normalized;

        Vector3 prev = center + right * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}

// =============================================================================
// CUSTOM EDITOR — botón visible directamente en el Inspector
// =============================================================================
#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(BeatSnapHelper))]
public class BeatSnapHelperEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        // Dibuja todos los campos normales del componente
        DrawDefaultInspector();

        BeatSnapHelper helper = (BeatSnapHelper)target;

        UnityEditor.EditorGUILayout.Space(8);

        // Color del botón según alineación
        var vizField = typeof(BeatSnapHelper)
            .GetField("_alignmentError",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cellField = typeof(BeatSnapHelper)
            .GetField("_visualizer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        BeatGridVisualizer viz = cellField?.GetValue(helper) as BeatGridVisualizer;
        float cell = viz != null ? viz.CellSize : 1f;
        float alignErr = vizField != null ? (float)vizField.GetValue(helper) : 1f;
        float errFraction = cell > 0.001f ? alignErr / (cell * 0.5f) : 1f;

        Color btnColor = errFraction < 0.1f
            ? new Color(0.3f, 0.8f, 0.3f)
            : (errFraction < 0.4f ? new Color(0.9f, 0.75f, 0.1f) : new Color(0.9f, 0.3f, 0.2f));

        GUI.backgroundColor = btnColor;
        GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize   = 13,
            fontStyle  = FontStyle.Bold,
            fixedHeight = 32
        };

        if (GUILayout.Button("♪  Snap to nearest beat", btnStyle))
        {
            helper.SnapToNearestBeat();
        }

        UnityEditor.EditorGUILayout.Space(4);

        // Botón de medio beat (color neutro: la posición alineada a medio beat se vería
        // "roja" contra los beats enteros, así que no reutilizamos el color de error).
        GUI.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        if (GUILayout.Button("♫  Snap to nearest half beat", btnStyle))
        {
            helper.SnapToNearestHalfBeat();
        }

        GUI.backgroundColor = Color.white;

        if (viz == null)
        {
            UnityEditor.EditorGUILayout.HelpBox(
                "Asigna un BeatGridVisualizer para poder hacer snap.",
                UnityEditor.MessageType.Warning);
        }
    }
}
#endif
