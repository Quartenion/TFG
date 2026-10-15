using System.IO;
using UnityEditor;
using UnityEngine;

// MENÚS DE AYUDA del editor (Tools → Tienda / Contratos): depurar la partida.

/// <summary>Menú Tools → Tienda: herramientas de depuración de la partida.</summary>
public static class ShopEditorMenu
{
    [MenuItem("Tools/Tienda/Debug: +1000 monedas")]
    private static void GiveCoins()
    {
        PlayerProgress.AddCoins(1000);
        Debug.Log($"[Tienda] Monedas: {PlayerProgress.Coins}");
    }

    [MenuItem("Tools/Tienda/Debug: Borrar partida guardada")]
    private static void ResetSave()
    {
        if (!EditorUtility.DisplayDialog("Tienda", "¿Borrar monedas, mejoras y consumibles guardados?", "Borrar", "Cancelar"))
            return;
        PlayerProgress.ResetAll();
        Debug.Log("[Tienda] Partida borrada.");
    }

    [MenuItem("Tools/Tienda/Debug: Abrir carpeta de la partida")]
    private static void OpenSaveFolder()
    {
        EditorUtility.RevealInFinder(Path.Combine(Application.persistentDataPath, "save.json"));
    }
}

/// <summary>Menú de ayuda para los contratos (Tools/Contratos).</summary>
public static class ContractEditorMenu
{
    [MenuItem("Tools/Contratos/Debug: Olvidar contratos (firmados y cumplidos)")]
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

