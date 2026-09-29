using UnityEditor;
using UnityEngine;

/// <summary>Menú de ayuda para los iris (monedas de mirada) y los cosméticos.</summary>
public static class GazeCoinEditorMenu
{
    [MenuItem("Tools/Tienda/Crear iris (moneda de mirada) en la escena")]
    private static void Create()
    {
        var go = new GameObject("Iris");
        var view = SceneView.lastActiveSceneView;
        if (view != null) go.transform.position = view.pivot;
        go.AddComponent<GazeCoin>();
        Undo.RegisterCreatedObjectUndo(go, "Crear iris");
        Selection.activeGameObject = go;
    }

    [MenuItem("Tools/Tienda/Debug · +10 iris")]
    private static void GiveIris()
    {
        PlayerProgress.CollectGazeCoin("debug:" + System.Guid.NewGuid(), 10);
        Debug.Log($"[Tienda] Iris: {PlayerProgress.GazeCoins}");
    }
}
