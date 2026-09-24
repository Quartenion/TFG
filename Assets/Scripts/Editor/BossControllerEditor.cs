using UnityEngine;
using UnityEditor;

/// <summary>
/// Inspector del BossController con botones de DEBUG: elegir en qué fase arranca el jefe (para testear
/// sin jugar toda la intro + Fase 1) o desactivarlo por completo.
/// Pulsa el botón de la fase y dale a Play. "Normal" = intro + Fase 1 como siempre.
/// </summary>
[CustomEditor(typeof(BossController))]
public class BossControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SerializedProperty startProp    = serializedObject.FindProperty("_debugStartPhase");
        SerializedProperty disabledProp = serializedObject.FindProperty("_disabled");
        if (startProp == null || disabledProp == null) return;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Debug — arranque rápido", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            PhaseButton(startProp, 0, "Normal");
            PhaseButton(startProp, 1, "Fase 1");
            PhaseButton(startProp, 2, "Fase 2");
            PhaseButton(startProp, 3, "Fase 3");
        }

        EditorGUILayout.Space(4);

        GUI.backgroundColor = disabledProp.boolValue ? new Color(0.9f, 0.4f, 0.3f) : Color.white;
        if (GUILayout.Button(disabledProp.boolValue ? "Jefe DESACTIVADO  (clic para activar)" : "Desactivar jefe",
                             GUILayout.Height(26)))
        {
            disabledProp.boolValue = !disabledProp.boolValue;
            serializedObject.ApplyModifiedProperties();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.HelpBox("Elige la fase de arranque y dale a Play. En debug el jefe aparece ya montado (sin escalada).",
                                MessageType.None);
    }

    private void PhaseButton(SerializedProperty startProp, int value, string label)
    {
        bool selected = startProp.enumValueIndex == value;
        GUI.backgroundColor = selected ? new Color(0.3f, 0.7f, 1f) : Color.white;
        if (GUILayout.Button(label, GUILayout.Height(26)))
        {
            startProp.enumValueIndex = value;
            serializedObject.ApplyModifiedProperties();
        }
        GUI.backgroundColor = Color.white;
    }
}
