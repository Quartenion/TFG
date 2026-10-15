using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector y Scene View para BeatOscillator:
///   · Handles arrastrables para mover A y B directamente en la escena.
///   · Botones para capturar la posición actual como A o B.
///   · Botones para teletransportar el objeto a A o B (para ajustar el mesh).
///   · Preview del ciclo en el Inspector (barra de progreso animada en play mode).
/// </summary>
[CustomEditor(typeof(BeatOscillator))]
public class BeatOscillatorEditor : Editor
{
    private SerializedProperty _pointA;
    private SerializedProperty _pointB;
    private SerializedProperty _beatsPerCycle;
    private SerializedProperty _phaseOffsetBeats;

    private void OnEnable()
    {
        _pointA           = serializedObject.FindProperty("pointA");
        _pointB           = serializedObject.FindProperty("pointB");
        _beatsPerCycle    = serializedObject.FindProperty("_beatsPerCycle");
        _phaseOffsetBeats = serializedObject.FindProperty("_phaseOffsetBeats");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        var osc = (BeatOscillator)target;

        // ── Botones de captura ────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Edición de puntos ──────────────────", EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("📌  Capturar A = pos. actual", GUILayout.Height(26)))
        {
            Undo.RecordObject(osc, "Capturar punto A");
            osc.CaptureCurrentAsA();
            serializedObject.Update();
            EditorUtility.SetDirty(osc);
        }
        if (GUILayout.Button("📌  Capturar B = pos. actual", GUILayout.Height(26)))
        {
            Undo.RecordObject(osc, "Capturar punto B");
            osc.CaptureCurrentAsB();
            serializedObject.Update();
            EditorUtility.SetDirty(osc);
        }
        EditorGUILayout.EndHorizontal();

        // ── Botones de navegación ─────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("↩  Ir a A"))
        {
            Undo.RecordObject(osc.transform, "Mover a punto A");
            osc.transform.position = _pointA.vector3Value;
        }
        if (GUILayout.Button("↪  Ir a B"))
        {
            Undo.RecordObject(osc.transform, "Mover a punto B");
            osc.transform.position = _pointB.vector3Value;
        }
        EditorGUILayout.EndHorizontal();

        // ── Preview del ciclo (play mode) ─────────────────────────────────────
        if (Application.isPlaying)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("── Ciclo actual ───────────────────────", EditorStyles.centeredGreyMiniLabel);

            var bm = BeatManager.Instance;
            if (bm != null && bm.ScheduledStartDSP > 0)
            {
                double elapsed = (AudioSettings.dspTime - bm.ScheduledStartDSP) / bm.BeatDuration;
                double shifted = elapsed + _phaseOffsetBeats.floatValue;
                double cycleT  = (shifted / _beatsPerCycle.floatValue) % 1.0;
                if (cycleT < 0) cycleT += 1.0;

                float t      = (float)cycleT;
                float pingpong = t < 0.5f ? t * 2f : (1f - t) * 2f;
                string phase = t < 0.5f ? "A → B" : "B → A";

                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(false, 18),
                    pingpong,
                    $"{phase}  ({(cycleT * _beatsPerCycle.floatValue):F2} beats / {_beatsPerCycle.floatValue:F2})");

                Repaint(); // animar en tiempo real
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCENE VIEW — handles arrastrables
    // ─────────────────────────────────────────────────────────────────────────

    private void OnSceneGUI()
    {
        serializedObject.Update();

        // Handle para A (verde)
        using (new Handles.DrawingScope(new Color(0.2f, 0.95f, 0.35f)))
        {
            EditorGUI.BeginChangeCheck();
            Vector3 newA = Handles.PositionHandle(_pointA.vector3Value, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Mover punto A");
                _pointA.vector3Value = newA;
                serializedObject.ApplyModifiedProperties();
            }
        }

        // Handle para B (rojo)
        using (new Handles.DrawingScope(new Color(0.95f, 0.25f, 0.25f)))
        {
            EditorGUI.BeginChangeCheck();
            Vector3 newB = Handles.PositionHandle(_pointB.vector3Value, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Mover punto B");
                _pointB.vector3Value = newB;
                serializedObject.ApplyModifiedProperties();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
