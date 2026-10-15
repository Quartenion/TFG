using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector personalizado para StartSequenceManager.
/// Añade un botón "▶ Escuchar segmento (4 beats)" que reproduce, en Edit Mode,
/// los 4 beats de la canción que corresponden a la posición _testStartZ.
///
/// Cómo funciona:
///   songOffset = _testStartZ / walkSpeed          → segundo de la canción
///   duration   = 4 × (60 / BPM)                  → duración de 4 beats
///   AudioSource.time = songOffset → Play() → parar tras 'duration' segundos
/// </summary>
[CustomEditor(typeof(StartSequenceManager))]
public class StartSequenceManagerEditor : Editor
{
    // Estado del preview (estático: sobrevive al repintado del Inspector)
    private static AudioSource s_previewSource;
    private static double      s_stopTime;
    private static bool        s_previewing;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SerializedProperty testModeProp    = serializedObject.FindProperty("_testMode");
        SerializedProperty testStartZProp  = serializedObject.FindProperty("_testStartZ");
        SerializedProperty beatManagerProp = serializedObject.FindProperty("_beatManager");
        SerializedProperty playerMoveProp  = serializedObject.FindProperty("_playerMovement");

        // Solo mostrar el panel si el modo test está activo
        if (!testModeProp.boolValue) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("── Preview de audio ──────────────────", EditorStyles.centeredGreyMiniLabel);

        var bm = beatManagerProp.objectReferenceValue as BeatManager;
        var pm = playerMoveProp.objectReferenceValue  as PlayerMovement;

        bool missingRefs = bm == null || pm == null;
        bool inPlayMode  = Application.isPlaying;

        if (missingRefs)
            EditorGUILayout.HelpBox("Asigna BeatManager y PlayerMovement para usar el preview.", MessageType.Info);
        else if (inPlayMode)
            EditorGUILayout.HelpBox("Preview disponible solo en Edit Mode.", MessageType.Info);

        EditorGUI.BeginDisabledGroup(missingRefs || inPlayMode);

        if (s_previewing)
        {
            if (GUILayout.Button("⏹  Detener preview"))
                StopPreview();
        }
        else
        {
            if (GUILayout.Button("▶  Escuchar segmento (4 beats)"))
                StartPreview(bm, pm, testStartZProp.floatValue);
        }

        EditorGUI.EndDisabledGroup();

        // Repintar el Inspector mientras el preview está activo (actualiza el botón)
        if (s_previewing)
            Repaint();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void StartPreview(BeatManager bm, PlayerMovement pm, float testStartZ)
    {
        // Obtener _audioSource de BeatManager via SerializedObject (campo privado)
        var bmSo     = new SerializedObject(bm);
        var audioSrc = bmSo.FindProperty("_audioSource").objectReferenceValue as AudioSource;

        if (audioSrc == null || audioSrc.clip == null)
        {
            Debug.LogWarning("[StartSequence Preview] BeatManager no tiene AudioSource o clip asignado.");
            return;
        }

        float walkSpeed  = pm.walkSpeed;
        float bpm        = bm.Bpm;
        float beatDur    = 60f / bpm;
        float songOffset = testStartZ / walkSpeed;
        float duration   = beatDur * 4f;

        // Clamp para no salirse del clip
        float maxOffset = Mathf.Max(0f, audioSrc.clip.length - duration - 0.05f);
        songOffset = Mathf.Clamp(songOffset, 0f, maxOffset);

        audioSrc.time = songOffset;
        audioSrc.Play();

        s_previewSource = audioSrc;
        s_stopTime      = EditorApplication.timeSinceStartup + duration;
        s_previewing    = true;

        // Registrar callback de actualización para parar el audio a tiempo
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;

        Debug.Log($"[StartSequence Preview] " +
                  $"Z={testStartZ:F0} m  |  offset={songOffset:F2} s  |  " +
                  $"duración={duration:F2} s  ({4} beats a {bpm} BPM)");
    }

    private static void StopPreview()
    {
        if (s_previewSource != null && s_previewSource.isPlaying)
            s_previewSource.Stop();

        s_previewSource = null;
        s_previewing    = false;
        EditorApplication.update -= OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
        if (!s_previewing) return;
        if (EditorApplication.timeSinceStartup >= s_stopTime)
            StopPreview();
    }
}
