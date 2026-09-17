using UnityEngine;
using UnityEditor;

/// <summary>
/// Ventana de editor para duplicar objetos seleccionados con un offset en Z.
/// Menú: Tools → Duplicate with Z Offset
/// </summary>
public class DuplicateWithOffsetEditor : EditorWindow
{
    private float _offsetZ    = 10f;
    private int   _copies     = 1;
    private bool  _selectNew  = true;

    [MenuItem("Tools/Duplicate with Z Offset")]
    public static void Open()
    {
        var win = GetWindow<DuplicateWithOffsetEditor>("Duplicate Z Offset");
        win.minSize = new Vector2(240f, 160f);
        win.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Duplicar con offset Z", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _offsetZ   = EditorGUILayout.FloatField("Offset Z (m)", _offsetZ);
        _copies    = Mathf.Max(1, EditorGUILayout.IntField("Número de copias", _copies));
        _selectNew = EditorGUILayout.Toggle("Seleccionar copias", _selectNew);

        EditorGUILayout.Space(8);

        int sel = Selection.gameObjects.Length;
        EditorGUI.BeginDisabledGroup(sel == 0);

        if (GUILayout.Button($"Duplicar ({sel} objeto{(sel != 1 ? "s" : "")} × {_copies})", GUILayout.Height(32)))
            Duplicate();

        EditorGUI.EndDisabledGroup();

        if (sel == 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Selecciona uno o más objetos en la Hierarchy.", MessageType.Info);
        }
    }

    private void Duplicate()
    {
        var originals = Selection.gameObjects;
        if (originals.Length == 0) return;

        var allNew = new System.Collections.Generic.List<GameObject>();

        Undo.SetCurrentGroupName("Duplicate with Z Offset");
        int group = Undo.GetCurrentGroup();

        foreach (var original in originals)
        {
            // Empezar la numeración después de la copia más alta que ya exista (p. ej. si ya hay
            // "Segment (5)", la siguiente será "Segment (6)"), para no repetir números al re-ejecutar.
            int start = NextIndex(original);

            for (int i = 1; i <= _copies; i++)
            {
                GameObject copy = Instantiate(original, original.transform.parent);
                copy.name = $"{original.name} ({start + i - 1})";   // Segment (1), Segment (2), ...

                Vector3 pos = original.transform.position;
                pos.z += _offsetZ * i;
                copy.transform.position = pos;
                copy.transform.rotation = original.transform.rotation;
                copy.transform.localScale = original.transform.localScale;

                Undo.RegisterCreatedObjectUndo(copy, "Duplicate with Z Offset");
                allNew.Add(copy);
            }
        }

        Undo.CollapseUndoOperations(group);

        if (_selectNew && allNew.Count > 0)
            Selection.objects = allNew.ToArray();
    }

    /// <summary>Siguiente número libre para "baseName (n)" entre los hermanos del objeto.</summary>
    private static int NextIndex(GameObject original)
    {
        string baseName = original.name;
        int    max      = 0;
        Transform parent = original.transform.parent;

        if (parent != null)
        {
            for (int c = 0; c < parent.childCount; c++)
                max = Mathf.Max(max, ExtractIndex(parent.GetChild(c).name, baseName));
        }
        else
        {
            foreach (var root in original.scene.GetRootGameObjects())
                max = Mathf.Max(max, ExtractIndex(root.name, baseName));
        }
        return max + 1;
    }

    /// <summary>Si 'name' es "baseName (n)" devuelve n; si no, 0.</summary>
    private static int ExtractIndex(string name, string baseName)
    {
        string prefix = baseName + " (";
        if (!name.StartsWith(prefix) || !name.EndsWith(")")) return 0;
        string num = name.Substring(prefix.Length, name.Length - prefix.Length - 1);
        return int.TryParse(num, out int n) ? n : 0;
    }
}
