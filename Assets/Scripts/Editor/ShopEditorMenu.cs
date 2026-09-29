using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Menú Tools → Tienda: crear el catálogo editable y herramientas de depuración de la partida.</summary>
public static class ShopEditorMenu
{
    private const string ResourcesDir = "Assets/Resources";
    private const string ItemsDir     = "Assets/Resources/ShopItems";
    private const string CatalogPath  = "Assets/Resources/ShopCatalog.asset";

    [MenuItem("Tools/Tienda/Crear catálogo por defecto (editable)")]
    private static void CreateDefaultCatalog()
    {
        if (AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath) != null &&
            !EditorUtility.DisplayDialog("Tienda", "Ya existe un ShopCatalog en Resources. ¿Reemplazarlo por el de por defecto?",
                                         "Reemplazar", "Cancelar"))
            return;

        if (!AssetDatabase.IsValidFolder(ResourcesDir)) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(ItemsDir))     AssetDatabase.CreateFolder(ResourcesDir, "ShopItems");

        ShopCatalog def = ShopCatalog.BuildDefault();
        var cat = ScriptableObject.CreateInstance<ShopCatalog>();
        foreach (ShopItem it in def.items)
        {
            string p = $"{ItemsDir}/{it.id}.asset";
            AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(it, p);
            cat.items.Add(it);
        }
        AssetDatabase.DeleteAsset(CatalogPath);
        AssetDatabase.CreateAsset(cat, CatalogPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = cat;
        EditorGUIUtility.PingObject(cat);
        Debug.Log($"[Tienda] Catálogo creado en {CatalogPath} con {cat.items.Count} objetos (edítalos en {ItemsDir}).");
    }

    [MenuItem("Tools/Tienda/Debug · +1000 monedas")]
    private static void GiveCoins()
    {
        PlayerProgress.AddCoins(1000);
        Debug.Log($"[Tienda] Monedas: {PlayerProgress.Coins}");
    }

    [MenuItem("Tools/Tienda/Debug · Borrar partida guardada")]
    private static void ResetSave()
    {
        if (!EditorUtility.DisplayDialog("Tienda", "¿Borrar monedas, mejoras y consumibles guardados?", "Borrar", "Cancelar"))
            return;
        PlayerProgress.ResetAll();
        Debug.Log("[Tienda] Partida borrada.");
    }

    [MenuItem("Tools/Tienda/Debug · Abrir carpeta de la partida")]
    private static void OpenSaveFolder()
    {
        EditorUtility.RevealInFinder(Path.Combine(Application.persistentDataPath, "save.json"));
    }
}
