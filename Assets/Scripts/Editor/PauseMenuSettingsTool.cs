using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Añade al MENÚ DE PAUSA (prefab Assets/Prefabs/Canvas.prefab, usado por todos los niveles) el botón SETTINGS y su
/// panel de ajustes (volumen + sensibilidad + BACK), como objetos reales del prefab:
///   PauseMenuPanel/SettingsButton
///   PauseMenuPanel/SettingsPanel/{Titulo, Volume(+Label), Sensitivity(+Label), BackButton}
/// También recoloca RESUME / SETTINGS / MENU para que quepan los tres. PauseMenuManager los conecta al arrancar.
/// Se puede volver a ejecutar: actualiza lo que ya exista en vez de duplicarlo.
/// </summary>
public static class PauseMenuSettingsTool
{
    private const string PrefabPath = "Assets/Prefabs/Canvas.prefab";

    // (Lo llama «Tools/Menús/Rediseñar menús» si aún no existe el panel de ajustes.)
    internal static void Run()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform panel = FindDeep(root.transform, "PauseMenuPanel");
            Transform resume = panel != null ? panel.Find("ResumeButton") : null;
            Transform menu   = panel != null ? panel.Find("MainMenuButton") : null;
            Transform title  = panel != null ? panel.Find("PauseText") : null;
            if (panel == null || resume == null || menu == null)
            {
                EditorUtility.DisplayDialog("Pausa", "No se encontró PauseMenuPanel/ResumeButton/MainMenuButton en " + PrefabPath, "OK");
                return;
            }

            // 1. Botón SETTINGS (copia de RESUME) entre RESUME y MENU.
            Transform settings = panel.Find("SettingsButton");
            if (settings == null)
            {
                settings = Object.Instantiate(resume.gameObject, panel).transform;
                settings.name = "SettingsButton";
            }
            settings.SetSiblingIndex(resume.GetSiblingIndex() + 1);
            settings.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();   // (se conecta en PauseMenuManager)
            SetText(settings, "SETTINGS");

            var size = new Vector2(500f, 170f);
            Pos(resume,   -60f,  size);
            Pos(settings, -250f, size);
            Pos(menu,     -440f, size);

            // 2. Panel de ajustes (oculto).
            Transform sp = panel.Find("SettingsPanel");
            if (sp == null)
            {
                var go = new GameObject("SettingsPanel", typeof(RectTransform));
                go.layer = panel.gameObject.layer;
                sp = go.transform;
                sp.SetParent(panel, false);
            }
            var spRt = (RectTransform)sp;
            spRt.anchorMin = Vector2.zero; spRt.anchorMax = Vector2.one;
            spRt.offsetMin = Vector2.zero; spRt.offsetMax = Vector2.zero;
            sp.SetAsLastSibling();

            TMP_FontAsset font = title != null ? title.GetComponent<TMP_Text>()?.font : null;

            if (title != null && sp.Find("Titulo") == null)
            {
                var t = Object.Instantiate(title.gameObject, sp).transform;
                t.name = "Titulo";
            }
            if (sp.Find("Titulo") != null) sp.Find("Titulo").GetComponent<TMP_Text>().text = "SETTINGS";

            Slider vol  = MakeSlider(sp, "Volume",      40f,  "VOLUMEN",      font);
            Slider sens = MakeSlider(sp, "Sensitivity", -150f, "SENSIBILIDAD", font);
            vol.minValue = 0f; vol.maxValue = 1f; vol.value = 1f;
            sens.minValue = GameSettings.SensMin; sens.maxValue = GameSettings.SensMax; sens.value = 1f;

            Transform back = sp.Find("BackButton");
            if (back == null)
            {
                back = Object.Instantiate(menu.gameObject, sp).transform;
                back.name = "BackButton";
            }
            back.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
            SetText(back, "BACK");
            Pos(back, -400f, size);

            sp.gameObject.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[Pausa] Botón SETTINGS y panel de ajustes añadidos a " + PrefabPath + " (vale para todos los niveles).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── Piezas ────────────────────────────────────────────────────────────────

    private static Slider MakeSlider(Transform parent, string name, float y, string label, TMP_FontAsset font)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing != null) go = existing.gameObject;
        else
        {
            go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = name;
            go.layer = parent.gameObject.layer;
            foreach (Transform c in go.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
        }

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(700f, 36f);

        // Colores: barra oscura, relleno rojo, tirador blanco.
        SetColor(go.transform, "Background", new Color(0.15f, 0.15f, 0.15f, 1f));
        SetColor(go.transform, "Fill Area/Fill", new Color(0.85f, 0.05f, 0.05f, 1f));
        SetColor(go.transform, "Handle Slide Area/Handle", Color.white);
        var handle = go.transform.Find("Handle Slide Area/Handle") as RectTransform;
        if (handle != null) handle.sizeDelta = new Vector2(36f, 0f);

        // Rótulo encima.
        Transform lt = go.transform.Find("Label");
        TextMeshProUGUI tmp;
        if (lt == null)
        {
            var l = new GameObject("Label", typeof(RectTransform));
            l.layer = go.layer;
            l.transform.SetParent(go.transform, false);
            tmp = l.AddComponent<TextMeshProUGUI>();
        }
        else tmp = lt.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = label;
        tmp.fontSize = 60f;
        tmp.color = new Color(0.9f, 0.05f, 0.05f, 1f);
        tmp.alignment = TextAlignmentOptions.Bottom;
        tmp.raycastTarget = false;
        var lrt = tmp.rectTransform;
        lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f);
        lrt.pivot = new Vector2(0.5f, 0f);
        lrt.anchoredPosition = new Vector2(0f, 10f);
        lrt.sizeDelta = new Vector2(0f, 80f);

        return go.GetComponent<Slider>();
    }

    private static void SetColor(Transform root, string path, Color c)
    {
        var t = root.Find(path);
        if (t != null && t.TryGetComponent(out Image img)) img.color = c;
    }

    private static void SetText(Transform button, string text)
    {
        var tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) tmp.text = text;
    }

    private static void Pos(Transform t, float y, Vector2 size)
    {
        var rt = (RectTransform)t;
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
        rt.sizeDelta = size;
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
