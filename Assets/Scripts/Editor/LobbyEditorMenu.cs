using UnityEditor;
using UnityEngine;

/// <summary>Menú de ayuda para la música del lobby (Tools/Lobby).</summary>
public static class LobbyEditorMenu
{
    [MenuItem("Tools/Lobby/Crear música del lobby en la escena")]
    private static void Create()
    {
        var existing = Object.FindAnyObjectByType<LobbyMusic>();
        if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
        var go = new GameObject("LobbyMusic");
        go.AddComponent<LobbyMusic>();
        Undo.RegisterCreatedObjectUndo(go, "Crear música del lobby");
        Selection.activeGameObject = go;
    }
}
