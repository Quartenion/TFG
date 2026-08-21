using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PivotOscillator))]
public class PivotOscillatorEditor : Editor
{
    private SerializedProperty _rotationAxis;
    private SerializedProperty _oscillate;
    private SerializedProperty _oscillationAngle;
    private SerializedProperty _beatsPerCycle;

    private void OnEnable()
    {
        _rotationAxis     = serializedObject.FindProperty("_rotationAxis");
        _oscillate        = serializedObject.FindProperty("_oscillate");
        _oscillationAngle = serializedObject.FindProperty("_oscillationAngle");
        _beatsPerCycle    = serializedObject.FindProperty("_beatsPerCycle");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        var osc = (PivotOscillator)target;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Herramientas ──────────────────────────", EditorStyles.centeredGreyMiniLabel);

        // Botones de eje rápido
        EditorGUILayout.LabelField("Eje rápido:", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("X (YZ)"))  SetAxis(Vector3.right);
        if (GUILayout.Button("Y (XZ)"))  SetAxis(Vector3.up);
        if (GUILayout.Button("Z (XY)"))  SetAxis(Vector3.forward);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // Warning si no hay hijos
        if (osc.transform.childCount == 0)
        {
            EditorGUILayout.HelpBox(
                "Este objeto no tiene hijos.\n" +
                "Haz el obstáculo HIJO de este pivote para que orbite alrededor de él.\n\n" +
                "Setup:\n" +
                "  1. Este GameObject = centro de rotación (con este script).\n" +
                "  2. Arrastra el obstáculo como hijo y desplázalo (brazo).",
                MessageType.Warning);
        }
        else
        {
            // Info de los hijos
            EditorGUILayout.LabelField("Hijos / brazos:", EditorStyles.miniLabel);
            foreach (Transform child in osc.transform)
            {
                float dist = (child.position - osc.transform.position).magnitude;
                EditorGUILayout.LabelField($"  • {child.name}  —  brazo: {dist:F2} m",
                    EditorStyles.miniLabel);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void SetAxis(Vector3 v)
    {
        _rotationAxis.vector3Value = v;
        serializedObject.ApplyModifiedProperties();
    }
}
