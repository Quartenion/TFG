using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// IDIOMA: prepara los menús hechos a mano para el cambio español / inglés.
///   1. Añade LocalizedText (Español / English) a los textos conocidos de la escena MainMenu y del prefab Canvas
///      (JUGAR/PLAY, CONTINUAR/RESUME, AJUSTES/SETTINGS…).
///   2. Añade el botón de IDIOMA (LanguageButton) a los Ajustes del menú principal y del menú de pausa.
/// Se puede volver a ejecutar sin duplicar nada. Guarda la escena MainMenu al terminar.
/// </summary>
public static class LanguageTool
{
    private const string CanvasPrefab = "Assets/Prefabs/Canvas.prefab";
    private const string MenuScene    = "Assets/Scenes/MainMenu.unity";

    /// <summary>Texto actual (en cualquiera de los dos idiomas) → (español, inglés).</summary>
    private static readonly Dictionary<string, (string es, string en)> Pairs = new Dictionary<string, (string, string)>
    {
        { "JUGAR", ("JUGAR", "PLAY") },             { "PLAY", ("JUGAR", "PLAY") },
        { "CONFIGURACION", ("CONFIGURACION", "SETTINGS") },
        { "SETTINGS", ("AJUSTES", "SETTINGS") },    { "AJUSTES", ("AJUSTES", "SETTINGS") },
        { "SALIR", ("SALIR", "QUIT") },             { "QUIT", ("SALIR", "QUIT") },
        { "APLICAR", ("APLICAR", "APPLY") },        { "APPLY", ("APLICAR", "APPLY") },
        { "VOLUMEN", ("VOLUMEN", "VOLUME") },       { "VOLUME", ("VOLUMEN", "VOLUME") },
        { "SENSIBILIDAD", ("SENSIBILIDAD", "SENSITIVITY") }, { "SENSITIVITY", ("SENSIBILIDAD", "SENSITIVITY") },
        { "RESUME", ("CONTINUAR", "RESUME") },      { "CONTINUAR", ("CONTINUAR", "RESUME") },
        { "MENU", ("MENU", "MENU") },
        { "PAUSE", ("PAUSA", "PAUSE") },            { "PAUSA", ("PAUSA", "PAUSE") },
        { "RESTART", ("REINTENTAR", "RESTART") },   { "REINTENTAR", ("REINTENTAR", "RESTART") },
        { "BACK", ("VOLVER", "BACK") },             { "VOLVER", ("VOLVER", "BACK") },
        { "GAME OVER", ("GAME OVER", "GAME OVER") },
    };

    // (Ahora lo hace «Tools/Menús/Rediseñar menús»; LocalizeTexts se reutiliza desde allí.)
    private static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // ── Prefab Canvas (pausa, game over) ──
        int n = 0;
        GameObject root = PrefabUtility.LoadPrefabContents(CanvasPrefab);
        try
        {
            n += LocalizeTexts(root.transform);
            Transform sp = FindDeep(root.transform, "SettingsPanel");
            if (sp != null)
            {
                Transform back = sp.Find("BackButton");
                if (back != null)
                {
                    AddLanguageButton(sp, back, new Vector2(0f, -250f), new Vector2(700f, 90f));
                    Pos(back, -400f);
                }
            }
            else Debug.LogWarning("[Idioma] El menú de pausa no tiene SettingsPanel: ejecuta antes «Tools/Pausa/Añadir ajustes al menú de pausa».");
            PrefabUtility.SaveAsPrefabAsset(root, CanvasPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        // ── Escena MainMenu ──
        var scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
        foreach (GameObject go in scene.GetRootGameObjects()) n += LocalizeTexts(go.transform);
        Transform menuSettings = null;
        foreach (GameObject go in scene.GetRootGameObjects()) { menuSettings = FindDeep(go.transform, "SettingsPanel"); if (menuSettings != null) break; }
        if (menuSettings != null)
        {
            Transform apply = menuSettings.Find("Apply");
            Pos(menuSettings.Find("Volume"), -20f);
            Pos(menuSettings.Find("Sensitivity"), -160f);
            if (apply != null)
            {
                AddLanguageButton(menuSettings, apply, new Vector2(0f, -290f), null);
                Pos(apply, -420f);
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[Idioma] {n} textos con LocalizedText y botón de idioma añadido (menú principal y pausa).");
    }

    internal static int LocalizeTexts(Transform root)
    {
        int n = 0;
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.GetComponentInParent<LanguageButton>(true) != null) continue;
            string key = t.text.Trim().ToUpperInvariant();
            if (!Pairs.TryGetValue(key, out var pair)) continue;
            var lt = t.GetComponent<LocalizedText>();
            if (lt == null) lt = t.gameObject.AddComponent<LocalizedText>();
            lt.spanish = pair.es;
            lt.english = pair.en;
            t.text = pair.es;
            EditorUtility.SetDirty(lt);
            EditorUtility.SetDirty(t);
            n++;
        }
        return n;
    }

    internal static void AddLanguageButton(Transform parent, Transform template, Vector2 pos, Vector2? size)
    {
        Transform b = parent.Find("LanguageButton");
        if (b == null)
        {
            b = Object.Instantiate(template.gameObject, parent).transform;
            b.name = "LanguageButton";
        }
        b.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
        foreach (var lt in b.GetComponentsInChildren<LocalizedText>(true)) Object.DestroyImmediate(lt);
        if (b.GetComponent<LanguageButton>() == null) b.gameObject.AddComponent<LanguageButton>();
        var tmp = b.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { tmp.text = "IDIOMA: CASTELLANO"; tmp.enableAutoSizing = true; tmp.fontSizeMin = 8f; tmp.fontSizeMax = Mathf.Max(tmp.fontSize, 40f); }
        var rt = (RectTransform)b;
        rt.anchoredPosition = pos;
        if (size.HasValue) rt.sizeDelta = size.Value;
        b.SetSiblingIndex(template.GetSiblingIndex());
    }

    private static void Pos(Transform t, float y)
    {
        if (t == null) return;
        var rt = (RectTransform)t;
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }
}
