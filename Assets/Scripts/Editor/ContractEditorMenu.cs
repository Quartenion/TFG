using UnityEditor;
using UnityEngine;

/// <summary>Menú de ayuda para los contratos (Tools/Contratos).</summary>
public static class ContractEditorMenu
{
    [MenuItem("Tools/Contratos/Crear puesto de contratos en la escena")]
    private static void CreateStand()
    {
        var go = new GameObject("ContractStand");
        var view = SceneView.lastActiveSceneView;
        if (view != null) go.transform.position = view.pivot;
        go.AddComponent<ContractStand>();
        Undo.RegisterCreatedObjectUndo(go, "Crear puesto de contratos");
        Selection.activeGameObject = go;
    }

    [MenuItem("Tools/Contratos/Debug · Olvidar contratos (firmados y cumplidos)")]
    private static void ResetContracts()
    {
        foreach (ContractDef c in ContractCatalog.All)
        {
            PlayerProgress.SetContractActive(c.id, false);
        }
        PlayerProgress.ClearDoneContracts();
        Debug.Log("[Contratos] Borrados todos los contratos firmados y cumplidos.");
    }
}
