using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// REDISEÑO DE TODOS LOS MENÚS (un solo clic: «Tools/Menús/Rediseñar menús»):
///   • Prefab COMPARTIDO Assets/Prefabs/SettingsMenu.prefab (Ajustes: título, volumen, sensibilidad, idioma, volver),
///     colocado en la pausa (prefab Canvas) y en el menú principal: los dos son EXACTAMENTE el mismo.
///   • Maqueta con aire (pantalla de referencia 1920×1080): pausa, game over, menú principal y ajustes.
///   • Todos los textos de botones/títulos con autoajuste y sin saltos de línea (nada se sale del botón).
///   • Textos en español / inglés (LocalizedText).
/// Se puede volver a ejecutar: recoloca y actualiza sin duplicar. Para retocar posiciones, cambia las constantes de abajo.
/// </summary>
public static class MenuDesignTool
{
    private const string CanvasPrefab   = "Assets/Prefabs/Canvas.prefab";
    private const string SettingsPrefab = "Assets/Prefabs/SettingsMenu.prefab";
    private const string MenuScene      = "Assets/Scenes/MainMenu.unity";

    // ── MAQUETA (centro de la pantalla = 0,0; arriba = +540) ─────────────────
    private static readonly Vector2 TitleSize  = new Vector2(1600f, 330f);   // títulos (PAUSA, AJUSTES, GAME OVER)
    private const float TitleTop = 30f;                                      // margen superior del título
    private const float TitleMaxFont = 280f, ButtonMaxFont = 72f;
    private static readonly Vector2 ButtonSize = new Vector2(480f, 120f);
    private const float ButtonStep = 150f;                                   // distancia entre botones

    private const float PauseFirstButton    = 30f;     // CONTINUAR, AJUSTES, MENU
    private const float GameOverFirstButton = 20f;     // REINTENTAR, SALIR
    private const float MainFirstButton     = -40f;    // JUGAR, CONFIGURACION, SALIR (debajo del logo)
    private static readonly Vector2 LogoSize = new Vector2(1400f, 420f);     // «INF HARTO» del menú principal

    private const float VolumeY = 70f, SensitivityY = -70f, LanguageY = -195f, BackY = -330f;
    private static readonly Vector2 SliderSize   = new Vector2(700f, 36f);
    private static readonly Vector2 LanguageSize = new Vector2(620f, 90f);

    [MenuItem("Tools/Menús/Rediseñar menús (pausa, game over, ajustes y menú principal)")]
    private static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // 0. Si nunca se creó el panel de ajustes de la pausa, se crea primero (botón SETTINGS + panel).
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefab) == null)
        {
            if (!PrefabHas(CanvasPrefab, "SettingsPanel")) PauseMenuSettingsTool.Run();
            CreateSettingsPrefab();
        }

        DesignSettingsPrefab();
        DesignCanvasPrefab();
        DesignMainMenu();

        Debug.Log("[Menús] Rediseño hecho: pausa, game over, ajustes (prefab compartido SettingsMenu) y menú principal.");
    }

    // ── 1. Prefab SettingsMenu ────────────────────────────────────────────────

    /// <summary>Saca el panel de ajustes que ya había en la pausa (el que gustaba) a su propio prefab.</summary>
    private static void CreateSettingsPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(CanvasPrefab);
        try
        {
            Transform old = FindDeep(root.transform, "SettingsPanel");
            var copy = Object.Instantiate(old.gameObject);
            copy.name = "SettingsMenu";
            copy.SetActive(true);
            // El botón de idioma, si aún no existía.
            Transform back = copy.transform.Find("BackButton");
            if (copy.transform.Find("LanguageButton") == null && back != null)
                LanguageTool.AddLanguageButton(copy.transform, back, new Vector2(0f, LanguageY), LanguageSize);
            PrefabUtility.SaveAsPrefabAsset(copy, SettingsPrefab);
            Object.DestroyImmediate(copy);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void DesignSettingsPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(SettingsPrefab);
        try
        {
            Transform t = root.transform;
            Stretch((RectTransform)t);

            var menu = root.GetComponent<SettingsMenu>();
            if (menu == null) menu = root.AddComponent<SettingsMenu>();
            menu.volume      = t.Find("Volume")?.GetComponent<Slider>();
            menu.sensitivity = t.Find("Sensitivity")?.GetComponent<Slider>();
            menu.back        = t.Find("BackButton")?.GetComponent<Button>();

            Title(t.Find("Titulo"));
            SliderAt(t.Find("Volume"), VolumeY);
            SliderAt(t.Find("Sensitivity"), SensitivityY);
            Place(t.Find("LanguageButton"), LanguageY, LanguageSize);
            Place(t.Find("BackButton"), BackY, ButtonSize);
            foreach (var b in root.GetComponentsInChildren<Button>(true)) FitButtonText(b);

            LanguageTool.LocalizeTexts(t);
            PrefabUtility.SaveAsPrefabAsset(root, SettingsPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ── 2. Prefab Canvas: pausa y game over ───────────────────────────────────

    private static void DesignCanvasPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(CanvasPrefab);
        try
        {
            // Pausa
            Transform pause = FindDeep(root.transform, "PauseMenuPanel");
            if (pause != null)
            {
                // El panel de ajustes propio se sustituye por el prefab compartido.
                Transform old = pause.Find("SettingsPanel");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                PutSettingsMenu(pause, false);

                Title(pause.Find("PauseText"));
                Transform resume = pause.Find("ResumeButton");
                if (resume != null)
                {
                    var img = resume.GetComponent<Image>();
                    var txt = resume.GetComponentInChildren<TMP_Text>(true);
                    if (img != null && txt != null)
                    {
                        _sprite = img.sprite; _imgType = img.type; _imgColor = img.color;
                        _font = txt.font; _fontMat = txt.fontSharedMaterial; _txtColor = txt.color;
                        _hasStyle = true;
                    }
                }
                float y = PauseFirstButton;
                foreach (string b in new[] { "ResumeButton", "SettingsButton", "MainMenuButton" })
                {
                    Transform bt = pause.Find(b);
                    if (bt == null) continue;
                    Place(bt, y, ButtonSize);
                    y -= ButtonStep;
                }
            }

            // Game over
            Transform go = FindDeep(root.transform, "GameOverPanel");
            if (go != null)
            {
                Title(go.Find("GameOverText"));
                AddFireBackground(go);
                Place(go.Find("RestartButton"), GameOverFirstButton, ButtonSize);
                Place(go.Find("QuitButton"), GameOverFirstButton - ButtonStep, ButtonSize);
            }

            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b.GetComponentInParent<SettingsMenu>(true) == null) FitButtonText(b);
            LanguageTool.LocalizeTexts(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, CanvasPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ── 3. Escena MainMenu ────────────────────────────────────────────────────

    private static void DesignMainMenu()
    {
        var scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
        Transform main = null, settings = null;
        foreach (GameObject g in scene.GetRootGameObjects())
        {
            main     ??= FindDeep(g.transform, "MainPanel");
            settings ??= FindDeep(g.transform, "SettingsPanel");
        }

        if (main != null)
        {
            Transform logo = main.Find("Titulo");
            if (logo != null)
            {
                var rt = (RectTransform)logo;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
                rt.localScale = Vector3.one;
                rt.anchoredPosition = new Vector2(0f, -TitleTop + 10f);
                rt.sizeDelta = LogoSize;
                FitText(logo.GetComponent<TMP_Text>(), 20f, false, TitleMaxFont);
            }
            float y = MainFirstButton;
            foreach (string b in new[] { "Play", "Settings", "Quit" })
            {
                Transform bt = main.Find(b);
                if (bt == null) continue;
                Place(bt, y, ButtonSize);
                CopyButtonStyle(bt);
                FitButtonText(bt.GetComponent<Button>());
                y -= ButtonStep;
            }
        }

        if (settings != null)
        {
            // Fuera lo antiguo (logo, sliders, APLICAR…): queda solo el prefab compartido, igual que en la pausa.
            for (int i = settings.childCount - 1; i >= 0; i--)
                if (settings.GetChild(i).GetComponent<SettingsMenu>() == null)
                    Object.DestroyImmediate(settings.GetChild(i).gameObject);
            PutSettingsMenu(settings, true);
            settings.gameObject.SetActive(false);   // (lo abre el botón CONFIGURACION; así no tapa el menú en el editor)
        }

        // El MainMenuManager ya no usa sliders sueltos (los lleva el prefab).
        var mm = Object.FindAnyObjectByType<MainMenuManager>(FindObjectsInactive.Include);
        if (mm != null)
        {
            var so = new SerializedObject(mm);
            so.FindProperty("_volumeSlider").objectReferenceValue = null;
            so.FindProperty("_sensitivitySlider").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (GameObject g in scene.GetRootGameObjects()) LanguageTool.LocalizeTexts(g.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ── Piezas ────────────────────────────────────────────────────────────────

    /// <summary>Pone (o reutiliza) una instancia del prefab SettingsMenu dentro de 'parent', a pantalla completa.</summary>
    private static void PutSettingsMenu(Transform parent, bool active)
    {
        SettingsMenu existing = parent.GetComponentInChildren<SettingsMenu>(true);
        GameObject inst = existing != null ? existing.gameObject
            : (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefab), parent);
        inst.name = "SettingsMenu";
        inst.layer = parent.gameObject.layer;
        Stretch((RectTransform)inst.transform);
        inst.transform.SetAsLastSibling();
        inst.SetActive(active);
    }

    /// <summary>Fondo infernal animado (el del menú principal) detrás de todo lo del panel.</summary>
    private static void AddFireBackground(Transform panel)
    {
        Transform fire = panel.Find("FondoFuego");
        if (fire == null)
        {
            var g = new GameObject("FondoFuego", typeof(RectTransform));
            g.layer = panel.gameObject.layer;
            g.transform.SetParent(panel, false);
            g.AddComponent<MenuFireBackground>();
            fire = g.transform;
        }
        Stretch((RectTransform)fire);
        fire.SetAsFirstSibling();
    }

    private static void Title(Transform t)
    {
        if (t == null) return;
        var rt = (RectTransform)t;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.localScale = Vector3.one;
        rt.anchoredPosition = new Vector2(0f, -TitleTop);
        rt.sizeDelta = TitleSize;
        FitText(t.GetComponent<TMP_Text>(), 30f, false, TitleMaxFont);
    }

    private static void Place(Transform t, float y, Vector2 size)
    {
        if (t == null) return;
        var rt = (RectTransform)t;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;   // (los botones del menú principal estaban escalados ×3,7)
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = size;
    }

    // Aspecto de los botones de la pausa (se copia a los del menú principal).
    private static bool _hasStyle;
    private static Sprite _sprite;
    private static Image.Type _imgType;
    private static Color _imgColor, _txtColor;
    private static TMP_FontAsset _font;
    private static Material _fontMat;

    /// <summary>Los botones del menú principal, con el mismo aspecto que los de la pausa (fondo, letra y color).</summary>
    private static void CopyButtonStyle(Transform button)
    {
        if (!_hasStyle) return;
        var img = button.GetComponent<Image>();
        if (img != null) { img.sprite = _sprite; img.type = _imgType; img.color = _imgColor; EditorUtility.SetDirty(img); }
        var tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            if (_font != null) tmp.font = _font;
            if (_fontMat != null) tmp.fontSharedMaterial = _fontMat;
            tmp.color = _txtColor;
            EditorUtility.SetDirty(tmp);
        }
    }

    private static void SliderAt(Transform t, float y)
    {
        Place(t, y, SliderSize);
        if (t == null) return;
        Transform label = t.Find("Label");
        if (label == null) return;
        var lrt = (RectTransform)label;
        lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f);
        lrt.pivot = new Vector2(0.5f, 0f);
        lrt.anchoredPosition = new Vector2(0f, 12f);
        lrt.sizeDelta = new Vector2(0f, 64f);
        FitText(label.GetComponent<TMP_Text>(), 16f, false, 56f);
    }

    /// <summary>El texto del botón ocupa el botón con un margen y se encoge si no cabe (sin saltos de línea).</summary>
    private static void FitButtonText(Button b)
    {
        if (b == null) return;
        var tmp = b.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) return;
        var rt = tmp.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(24f, 10f);
        rt.offsetMax = new Vector2(-24f, -10f);
        FitText(tmp, 12f, false, ButtonMaxFont);
    }

    private static void FitText(TMP_Text tmp, float min, bool wrap, float max = -1f)
    {
        if (tmp == null) return;
        if (max < 0f) max = Mathf.Max(tmp.enableAutoSizing ? tmp.fontSizeMax : tmp.fontSize, min + 1f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = min;
        tmp.fontSizeMax = max;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.Center;
        EditorUtility.SetDirty(tmp);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static bool PrefabHas(string path, string child)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try { return FindDeep(root.transform, child) != null; }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }
}
