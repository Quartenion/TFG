using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor personalizado para BeatGridVisualizer.
///
/// FLUJO:
///   1. Selecciona el GameObject con BeatGridVisualizer en la Jerarquía.
///   2. En la Scene View verás los marcadores de compás (rojos) con un aro amarillo
///      cuando pasas el ratón por encima — son clicables.
///   3. Al hacer clic en uno, el beat queda seleccionado en el Inspector.
///   4. Pulsa "▶ Escuchar segmento (4 beats)" para escuchar los 4 beats desde ahí.
///
/// También puedes escribir el beat directamente en el campo "Beat a previsualizar".
/// </summary>
[CustomEditor(typeof(BeatGridVisualizer))]
public class BeatGridVisualizerEditor : Editor
{
    // ─── estado de selección ────────────────────────────────────────────────
    private int _selectedBeat = -1;   // -1 = ninguno seleccionado

    // ─── estado del preview (estático: sobrevive al repintado) ──────────────
    private static AudioSource s_src;
    private static double      s_stopTime;
    private static bool        s_previewing;

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var grid = (BeatGridVisualizer)target;
        BeatManager bm = grid.beatManager;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("── Preview de audio ──────────────────", EditorStyles.centeredGreyMiniLabel);

        // ── campo editable de beat ─────────────────────────────────────────
        EditorGUI.BeginChangeCheck();
        int input = EditorGUILayout.IntField(
            new GUIContent("Beat a previsualizar",
                           "Edita el número directamente o haz clic en un marcador rojo de la Scene View."),
            Mathf.Max(0, _selectedBeat));
        if (EditorGUI.EndChangeCheck())
            _selectedBeat = Mathf.Max(0, input);

        if (_selectedBeat < 0)
        {
            EditorGUILayout.HelpBox(
                "Haz clic en un marcador rojo (compás) en la Scene View,\no escribe el número de beat arriba.",
                MessageType.Info);
        }
        else
        {
            // Info rápida sobre el beat seleccionado
            if (bm != null)
            {
                float beatDur   = 60f / bm.Bpm;
                float songSec   = _selectedBeat * beatDur;
                float distM     = _selectedBeat * grid.CellSize;
                EditorGUILayout.HelpBox(
                    $"Beat {_selectedBeat}  →  {songSec:F2} s  |  {distM:F1} m desde el origen",
                    MessageType.None);
            }
        }

        EditorGUILayout.Space(2);

        // ── botón de preview / stop ────────────────────────────────────────
        bool missingBM  = bm == null;
        bool inPlayMode = Application.isPlaying;
        bool canPlay    = !missingBM && !inPlayMode && _selectedBeat >= 0;

        if (missingBM)
            EditorGUILayout.HelpBox("Asigna BeatManager para usar el preview.", MessageType.Warning);
        if (inPlayMode)
            EditorGUILayout.HelpBox("Preview disponible solo en Edit Mode.", MessageType.Info);

        EditorGUI.BeginDisabledGroup(!canPlay);

        string btnLabel = s_previewing
            ? "⏹  Detener preview"
            : $"▶  Escuchar beat {Mathf.Max(0, _selectedBeat)} (4 beats)";

        if (GUILayout.Button(btnLabel, GUILayout.Height(28)))
        {
            if (s_previewing) StopPreview();
            else              StartPreview(bm, _selectedBeat);
        }

        EditorGUI.EndDisabledGroup();

        // Repintar mientras suena para actualizar el botón
        if (s_previewing) Repaint();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCENE VIEW — handles clicables
    // ─────────────────────────────────────────────────────────────────────────

    private void OnSceneGUI()
    {
        var grid = (BeatGridVisualizer)target;
        if (grid.beatManager == null) return;

        float   cell   = grid.CellSize;
        int     beats  = grid.BeatCount;
        Vector3 fwd    = grid.transform.forward;
        Vector3 origin = grid.Origin;
        Camera  cam    = SceneView.currentDrawingSceneView?.camera;

        // Solo hacemos clicables los marcadores de compás (múltiplo de 4)
        // y filtramos por distancia a la cámara para no iterar miles de handles
        float cullDist = cam != null ? Mathf.Max(300f, cam.farClipPlane * 0.5f) : float.MaxValue;

        int step = Mathf.Max(4, (grid.showEveryNBeats / 4) * 4); // múltiplo de 4
        if (step % 4 != 0) step = 4;

        for (int i = 0; i <= beats; i += 4)
        {
            Vector3 pos = origin + fwd * (i * cell);
            pos.y = origin.y;

            // Culling de distancia
            if (cam != null && Vector3.Distance(cam.transform.position, pos) > cullDist)
                continue;

            float size = HandleUtility.GetHandleSize(pos) * 0.22f;

            // Color: amarillo brillante si está seleccionado, naranja si no
            bool selected = (i == _selectedBeat);
            Handles.color = selected
                ? new Color(1f, 0.92f, 0.02f, 1f)   // amarillo = seleccionado
                : new Color(1f, 0.35f, 0.1f, 0.7f);  // naranja translúcido = clicable

            if (Handles.Button(pos, Quaternion.identity, size, size * 1.4f, Handles.SphereHandleCap))
            {
                _selectedBeat = i;
                Repaint();  // actualizar Inspector
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUDIO PREVIEW
    // ─────────────────────────────────────────────────────────────────────────

    private void StartPreview(BeatManager bm, int beat)
    {
        // Acceder al AudioSource privado de BeatManager
        var bmSo     = new SerializedObject(bm);
        var audioSrc = bmSo.FindProperty("_audioSource").objectReferenceValue as AudioSource;

        if (audioSrc == null || audioSrc.clip == null)
        {
            Debug.LogWarning("[BeatGridVisualizer] BeatManager no tiene AudioSource o clip asignado.");
            return;
        }

        float beatDur   = 60f / bm.Bpm;
        float songOffset = beat * beatDur;
        float duration  = beatDur * 4f;

        float maxOffset = Mathf.Max(0f, audioSrc.clip.length - duration - 0.05f);
        songOffset = Mathf.Clamp(songOffset, 0f, maxOffset);

        audioSrc.time = songOffset;
        audioSrc.Play();

        s_src       = audioSrc;
        s_stopTime  = EditorApplication.timeSinceStartup + duration;
        s_previewing = true;

        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;

        Debug.Log($"[BeatGridVisualizer Preview] Beat {beat}  →  {songOffset:F2} s  |  {duration:F2} s de duración");
    }

    private static void StopPreview()
    {
        if (s_src != null && s_src.isPlaying) s_src.Stop();
        s_src        = null;
        s_previewing = false;
        EditorApplication.update -= OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
        if (s_previewing && EditorApplication.timeSinceStartup >= s_stopTime)
            StopPreview();
    }
}
