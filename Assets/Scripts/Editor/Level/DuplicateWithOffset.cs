using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// DUPLICAR CON DESPLAZAMIENTO EN Z (Tools/Duplicar): copia lo seleccionado y mueve cada copia hacia delante en Z,
/// para construir el nivel por tramos (segmentos, vigas, monedas…). Mantiene los enlaces a prefabs (usa el Duplicar
/// normal de Unity) y se deshace con Ctrl+Z.
///   • Desplazamiento en METROS, o en BEATS (beats × lo que avanza el jugador por beat, según la cuadrícula del nivel).
///   • Varias copias seguidas (cada una un desplazamiento más allá).
///   • Si el nombre lleva «beats A-B» (p. ej. «Segmento 008 · beats 32-35») y el desplazamiento va en beats, el
///     nombre de la copia se actualiza («beats 36-39») y también el número del segmento.
/// </summary>
public class DuplicateWithOffset : EditorWindow
{
    private const string PrefZ = "Duplicar.OffsetZ", PrefBeats = "Duplicar.Beats", PrefUseBeats = "Duplicar.UseBeats",
                         PrefCopies = "Duplicar.Copies";

    private float _offsetZ;
    private float _beats;
    private bool  _useBeats;
    private int   _copies;

    [MenuItem("Tools/Duplicar")]
    private static void Open()
    {
        var w = GetWindow<DuplicateWithOffset>(true, "Duplicar", true);
        w.minSize = new Vector2(320f, 170f);
    }

    private void OnEnable()
    {
        _offsetZ  = EditorPrefs.GetFloat(PrefZ, 10f);
        _beats    = EditorPrefs.GetFloat(PrefBeats, 4f);
        _useBeats = EditorPrefs.GetBool(PrefUseBeats, true);
        _copies   = EditorPrefs.GetInt(PrefCopies, 1);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Duplica lo seleccionado desplazándolo en Z.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(4);

        _useBeats = EditorGUILayout.Toggle("Desplazar en beats", _useBeats);
        BeatGridVisualizer grid = FindAnyObjectByType<BeatGridVisualizer>();
        if (_useBeats)
        {
            _beats = EditorGUILayout.FloatField("Beats", _beats);
            if (grid != null) EditorGUILayout.LabelField("= metros", (_beats * grid.CellSize).ToString("0.###"));
            else EditorGUILayout.HelpBox("No hay BeatGridVisualizer en la escena: usa metros.", MessageType.Warning);
        }
        else _offsetZ = EditorGUILayout.FloatField("Desplazamiento Z (m)", _offsetZ);

        _copies = Mathf.Max(1, EditorGUILayout.IntField("Copias", _copies));

        EditorGUILayout.Space(6);
        using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0 || (_useBeats && grid == null)))
            if (GUILayout.Button($"Duplicar ({Selection.gameObjects.Length} seleccionado/s)", GUILayout.Height(28)))
            {
                float dz = _useBeats ? _beats * grid.CellSize : _offsetZ;
                Duplicate(dz, _copies, _useBeats ? Mathf.RoundToInt(_beats) : 0);
            }

        EditorPrefs.SetFloat(PrefZ, _offsetZ);
        EditorPrefs.SetFloat(PrefBeats, _beats);
        EditorPrefs.SetBool(PrefUseBeats, _useBeats);
        EditorPrefs.SetInt(PrefCopies, _copies);
    }

    private void OnSelectionChange() => Repaint();

    private static void Duplicate(float dz, int copies, int beatStep)
    {
        Undo.SetCurrentGroupName("Duplicar con desplazamiento en Z");
        int group = Undo.GetCurrentGroup();

        var created = new List<Object>();
        for (int i = 0; i < copies; i++)
        {
            // Duplicar normal de Unity (respeta prefabs y lo deja seleccionado), y luego mover.
            Unsupported.DuplicateGameObjectsUsingPasteboard();   // (como Ctrl+D, aunque esta ventana tenga el foco)
            foreach (GameObject go in Selection.gameObjects)
            {
                Undo.RecordObject(go.transform, "Mover copia");
                go.transform.position += Vector3.forward * dz;
                if (beatStep != 0) go.name = RenameBeats(go.name, beatStep);
            }
            created.AddRange(Selection.gameObjects);
        }

        Undo.CollapseUndoOperations(group);
        Selection.objects = created.ToArray();
    }

    /// <summary>«Segmento 008 · beats 32-35» + 4 beats → «Segmento 009 · beats 36-39». Quita el « (1)» de Unity.</summary>
    private static string RenameBeats(string name, int step)
    {
        name = Regex.Replace(name, @"\s*\(\d+\)$", "");
        var m = Regex.Match(name, @"beats\s+(\d+)\s*-\s*(\d+)");
        if (!m.Success) return name;
        int a = int.Parse(m.Groups[1].Value) + step, b = int.Parse(m.Groups[2].Value) + step;
        name = name.Substring(0, m.Index) + $"beats {a}-{b}" + name.Substring(m.Index + m.Length);
        var seg = Regex.Match(name, @"(Segmento\s+)(\d+)");
        if (seg.Success)
        {
            int span = int.Parse(m.Groups[2].Value) - int.Parse(m.Groups[1].Value) + 1;
            int n = int.Parse(seg.Groups[2].Value) + (span > 0 ? Mathf.Max(1, step / span) : 1);
            name = name.Substring(0, seg.Groups[2].Index) + n.ToString(new string('0', seg.Groups[2].Length)) +
                   name.Substring(seg.Groups[2].Index + seg.Groups[2].Length);
        }
        return name;
    }
}
