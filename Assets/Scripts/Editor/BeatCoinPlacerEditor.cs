using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Botones del colocador de monedas por beat (solo debug / diseño de niveles).</summary>
[CustomEditor(typeof(BeatCoinPlacer))]
public class BeatCoinPlacerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var p = (BeatCoinPlacer)target;

        EditorGUILayout.Space(8);
        if (p.path == null)
        {
            EditorGUILayout.HelpBox("Asigna un RecordedPath (graba una partida con PlayerPathRecorder).", MessageType.Info);
            return;
        }

        List<BeatCoinPlacer.BeatPoint> pts = p.ComputeBeatPoints();
        EditorGUILayout.HelpBox($"BPM {p.Bpm:0.#}  ·  {p.path.frames.Count} frames grabados  ·  {pts.Count} monedas a colocar",
                                MessageType.None);

        GUI.backgroundColor = new Color(1f, 0.85f, 0.3f);
        if (GUILayout.Button($"Colocar monedas en cada beat ({pts.Count})", GUILayout.Height(32)))
            Place(p, pts);
        GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
        if (GUILayout.Button("Borrar monedas generadas", GUILayout.Height(24)))
            Clear(p);
        GUI.backgroundColor = Color.white;
    }

    private static Transform Parent(BeatCoinPlacer p)
    {
        if (p.coinsParent != null) return p.coinsParent;
        var go = new GameObject("Coins (por beat)");
        Undo.RegisterCreatedObjectUndo(go, "Crear padre de monedas");
        p.coinsParent = go.transform;
        EditorUtility.SetDirty(p);
        return go.transform;
    }

    private static void Place(BeatCoinPlacer p, List<BeatCoinPlacer.BeatPoint> pts)
    {
        Transform parent = Parent(p);
        Undo.SetCurrentGroupName("Colocar monedas por beat");
        int group = Undo.GetCurrentGroup();
        foreach (BeatCoinPlacer.BeatPoint pt in pts)
        {
            GameObject c = p.coinPrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(p.coinPrefab, parent)
                : new GameObject("Coin");
            if (p.coinPrefab == null)
            {
                c.transform.SetParent(parent, false);
                c.AddComponent<Coin>();
            }
            c.name = $"Coin_b{pt.beat:0.#}";
            c.transform.position = pt.pos + Vector3.up * p.heightOffset;
            Undo.RegisterCreatedObjectUndo(c, "Moneda");
        }
        Undo.CollapseUndoOperations(group);
        Debug.Log($"[BeatCoinPlacer] {pts.Count} monedas colocadas en '{parent.name}'.");
    }

    private static void Clear(BeatCoinPlacer p)
    {
        if (p.coinsParent == null) return;
        Undo.SetCurrentGroupName("Borrar monedas por beat");
        int group = Undo.GetCurrentGroup();
        for (int i = p.coinsParent.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(p.coinsParent.GetChild(i).gameObject);
        Undo.CollapseUndoOperations(group);
    }

    [MenuItem("Tools/Level Design/Colocador de monedas por beat (debug)")]
    private static void CreatePlacer()
    {
        var go = new GameObject("BeatCoinPlacer (debug)");
        var placer = go.AddComponent<BeatCoinPlacer>();
        string[] guids = AssetDatabase.FindAssets("t:RecordedPath");
        if (guids.Length > 0) placer.path = AssetDatabase.LoadAssetAtPath<RecordedPath>(AssetDatabase.GUIDToAssetPath(guids[0]));
        Undo.RegisterCreatedObjectUndo(go, "Crear colocador de monedas");
        Selection.activeGameObject = go;
    }
}
