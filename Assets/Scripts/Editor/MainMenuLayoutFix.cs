using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Arregla la maqueta del MENÚ PRINCIPAL (escena abierta «MainMenu»):
///   • El Canvas estaba en «Constant Pixel Size»: con otra resolución todo se montaba encima (título sobre botones).
///     Pasa a «Scale With Screen Size» con referencia 1920×1080 (como el resto de la interfaz del juego).
///   • Recoloca título y botones para que no se solapen (en el panel principal y en el de ajustes).
/// Se puede deshacer con Ctrl+Z. Después guarda la escena.
/// </summary>
public static class MainMenuLayoutFix
{
    // (Sustituido por «Tools/Menús/Rediseñar menús», MenuDesignTool.)
    private static void Fix()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "MainMenu" &&
            !EditorUtility.DisplayDialog("Menú principal", $"La escena abierta es «{scene.name}», no MainMenu. ¿Seguir igualmente?", "Seguir", "Cancelar"))
            return;

        Undo.SetCurrentGroupName("Arreglar maqueta del menú");
        int group = Undo.GetCurrentGroup();

        // 1. Escalado del Canvas
        foreach (var root in scene.GetRootGameObjects())
            foreach (var scaler in root.GetComponentsInChildren<CanvasScaler>(true))
            {
                if (scaler.GetComponent<Canvas>() == null || !scaler.GetComponent<Canvas>().isRootCanvas) continue;
                Undo.RecordObject(scaler, "Canvas Scaler");
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

        // 2. Posiciones (centro de la pantalla = 0,0; pantalla de referencia 1920×1080)
        int moved = 0;
        moved += Set("MainPanel/Titulo",          290f);
        moved += Set("MainPanel/Play",            -40f);
        moved += Set("MainPanel/Settings",       -200f);
        moved += Set("MainPanel/Quit",           -360f);
        moved += Set("SettingsPanel/Titulo",      290f);
        moved += Set("SettingsPanel/Volume",      -20f);
        moved += Set("SettingsPanel/Sensitivity", -160f);
        if (Find("SettingsPanel/LanguageButton") != null) moved += Set("SettingsPanel/LanguageButton", -290f);   // (Tools/Idioma)
        moved += Set("SettingsPanel/Apply",      -420f);

        // 3. Rótulos de los sliders (objetos reales en la jerarquía)
        moved += AddLabel("SettingsPanel/Volume", "VOLUMEN");
        moved += AddLabel("SettingsPanel/Sensitivity", "SENSIBILIDAD");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Menú] Canvas en «Scale With Screen Size» (1920×1080) y {moved} elementos recolocados. Guarda la escena (Ctrl+S).");
    }

    [MenuItem("Tools/Menú principal/Añadir fondo de fuego")]
    private static void AddFireBackground()
    {
        RectTransform main = Find("Canvas/MainPanel") ?? FindAny("MainPanel");
        if (main == null) { Debug.LogWarning("[Menú] No se encontró «MainPanel»."); return; }
        Transform canvas = main.parent;

        Undo.SetCurrentGroupName("Fondo de fuego");
        int group = Undo.GetCurrentGroup();

        var fire = canvas.Find("FondoFuego");
        if (fire == null)
        {
            var go = new GameObject("FondoFuego", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Fondo de fuego");
            go.layer = main.gameObject.layer;
            go.transform.SetParent(canvas, false);
            go.AddComponent<MenuFireBackground>();
            fire = go.transform;
        }
        var rt = (RectTransform)fire;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        Undo.SetTransformParent(fire, canvas, "Fondo de fuego");
        fire.SetAsFirstSibling();   // detrás de los paneles

        // Los paneles tenían un fondo negro opaco que taparía el fuego: se vuelve transparente.
        foreach (string panel in new[] { "MainPanel", "SettingsPanel" })
        {
            var p = canvas.Find(panel);
            if (p != null && p.TryGetComponent(out Image img))
            {
                Undo.RecordObject(img, "Panel transparente");
                var c = img.color; c.a = 0f; img.color = c;
            }
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = fire.gameObject;
        Debug.Log("[Menú] «FondoFuego» añadido detrás de los paneles (los paneles quedan transparentes). Ajusta colores/chispas en su Inspector y guarda la escena.");
    }

    private static RectTransform FindAny(string name)
    {
        foreach (var rt in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include))
            if (rt.name == name) return rt;
        return null;
    }

    // (Sustituido por el prefab SettingsMenu.)
    private static void AddLabelsOnly()
    {
        Undo.SetCurrentGroupName("Rótulos de los sliders");
        int n = AddLabel("SettingsPanel/Volume", "VOLUMEN") + AddLabel("SettingsPanel/Sensitivity", "SENSIBILIDAD");
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Menú] {n} rótulos creados/actualizados. Guarda la escena (Ctrl+S).");
    }

    /// <summary>Crea (o actualiza) un hijo «Label» con TextMeshPro encima de la barra del slider 'panel/hijo'.</summary>
    private static int AddLabel(string path, string text)
    {
        RectTransform slider = Find(path);
        if (slider == null) { Debug.LogWarning($"[Menú] No se encontró «{path}»."); return 0; }

        Transform existing = slider.Find("Label");
        TextMeshProUGUI label;
        if (existing != null && existing.TryGetComponent(out label))
            Undo.RecordObject(label, "Rótulo");
        else
        {
            var go = new GameObject("Label", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Rótulo");
            go.layer = slider.gameObject.layer;
            go.transform.SetParent(slider, false);
            label = go.AddComponent<TextMeshProUGUI>();
        }

        // Misma fuente que el título del panel (o cualquier texto del panel).
        var sample = slider.parent.GetComponentInChildren<TMP_Text>(true);
        if (sample != null && sample != label) label.font = sample.font;

        label.text = text;
        label.fontSize = 36f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Bottom;
        label.raycastTarget = false;

        // La barra ocupa la franja central (25 %-75 %) del slider: el rótulo va justo encima.
        var rt = label.rectTransform;
        Undo.RecordObject(rt, "Rótulo");
        rt.anchorMin = new Vector2(0f, 0.75f);
        rt.anchorMax = new Vector2(1f, 0.75f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 8f);
        rt.sizeDelta = new Vector2(0f, 50f);
        return 1;
    }

    private static RectTransform Find(string path)
    {
        string[] parts = path.Split('/');
        foreach (var rt in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include))
            if (rt.name == parts[1] && rt.parent != null && rt.parent.name == parts[0]) return rt;
        return null;
    }

    /// <summary>Pone la Y (anchoredPosition) del elemento 'panel/hijo' si existe.</summary>
    private static int Set(string path, float y)
    {
        string[] parts = path.Split('/');
        foreach (var rt in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include))
        {
            if (rt.name != parts[1] || rt.parent == null || rt.parent.name != parts[0]) continue;
            Undo.RecordObject(rt, "Recolocar");
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
            return 1;
        }
        Debug.LogWarning($"[Menú] No se encontró «{path}».");
        return 0;
    }
}
