#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Herramienta de Editor: crea el material de cuerda PBR con todas las texturas.
/// Menu: Tools -> Build Rope Material
///
/// Que hace:
///   1. Invierte rope_roughness para obtener rope_smoothness (Unity usa Smoothness, no Roughness).
///   2. Crea un material URP/Lit con todas las texturas asignadas correctamente.
///   3. Guarda el material en Assets/Materials/RopeMaterial.mat
///
/// ANTES DE USAR: importa todas las texturas en Assets/Materials/
///   rope_basecolor, rope_normal, rope_AO, rope_roughness, rope_height, rope_cavity
/// </summary>
public class RopeMaterialBuilder : EditorWindow
{
    private Texture2D _baseColor;
    private Texture2D _normal;
    private Texture2D _ao;
    private Texture2D _roughness;
    private Texture2D _height;
    private Texture2D _cavity;

    private string _materialPath = "Assets/Materials/RopeMaterial.mat";
    private float  _heightScale  = 0.02f;
    private float  _tiling       = 4f;

    [MenuItem("Tools/Build Rope Material")]
    public static void OpenWindow()
    {
        GetWindow<RopeMaterialBuilder>("Rope Material Builder");
    }

    private void OnGUI()
    {
        GUILayout.Label("Rope PBR Material Builder", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        GUILayout.Label("Asigna tus texturas:", EditorStyles.miniLabel);
        _baseColor = (Texture2D)EditorGUILayout.ObjectField("Base Color",  _baseColor, typeof(Texture2D), false);
        _normal    = (Texture2D)EditorGUILayout.ObjectField("Normal",      _normal,    typeof(Texture2D), false);
        _ao        = (Texture2D)EditorGUILayout.ObjectField("AO",          _ao,        typeof(Texture2D), false);
        _roughness = (Texture2D)EditorGUILayout.ObjectField("Roughness",   _roughness, typeof(Texture2D), false);
        _height    = (Texture2D)EditorGUILayout.ObjectField("Height",      _height,    typeof(Texture2D), false);
        _cavity    = (Texture2D)EditorGUILayout.ObjectField("Cavity",      _cavity,    typeof(Texture2D), false);

        EditorGUILayout.Space();
        _heightScale  = EditorGUILayout.Slider("Height Scale",  _heightScale, 0f,    0.1f);
        _tiling       = EditorGUILayout.FloatField("Tiling",   _tiling);
        _materialPath = EditorGUILayout.TextField("Guardar en", _materialPath);

        EditorGUILayout.Space();

        if (GUILayout.Button("Construir Material", GUILayout.Height(36)))
            BuildMaterial();

        EditorGUILayout.HelpBox(
            "Roughness se invertira automaticamente para obtener Smoothness.\n" +
            "Cavity se multiplicara en el canal AO si esta asignada.\n" +
            "El material resultante usa URP/Lit.",
            MessageType.Info);
    }

    private void BuildMaterial()
    {
        // 1. Invertir Roughness -> Smoothness y guardarlo como textura nueva
        Texture2D smoothnessTex = null;
        if (_roughness != null)
        {
            smoothnessTex = InvertTexture(_roughness, "rope_smoothness");
            if (smoothnessTex == null)
            {
                Debug.LogError("[RopeMaterialBuilder] No se pudo leer rope_roughness. " +
                               "Asegurate de que Read/Write esta activado en su Import Settings.");
                return;
            }
        }

        // 2. Combinar AO con Cavity si existe (Cavity refina las sombras en micro-detalles)
        Texture2D aoFinal = _ao;
        if (_ao != null && _cavity != null)
        {
            aoFinal = MultiplyCavityIntoAO(_ao, _cavity, "rope_ao_cavity");
        }

        // 3. Crear o reemplazar el material
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[RopeMaterialBuilder] No se encontro el shader URP/Lit. " +
                           "Asegurate de tener URP instalado.");
            return;
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(_materialPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, _materialPath);
        }
        else
        {
            mat.shader = shader;
        }

        // --- Asignar texturas ---

        // Base Color
        if (_baseColor != null)
        {
            mat.SetTexture("_BaseMap", _baseColor);
            mat.SetColor("_BaseColor", Color.white);
        }

        // Normal Map
        if (_normal != null)
        {
            EnsureNormalMapImportType(_normal);
            mat.SetTexture("_BumpMap", _normal);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }

        // Metallic = 0 (la cuerda no es metalica)
        // Smoothness viene de la textura invertida de roughness
        mat.SetFloat("_Metallic", 0f);
        if (smoothnessTex != null)
        {
            // En URP Lit, el Smoothness puede ir en el canal Alpha del Metallic map.
            // Lo mas sencillo es usar el slider y poner Smoothness Source en BaseMap Alpha,
            // o simplemente ajustar el slider. Aqui ponemos la textura en el Metallic slot
            // y el canal A contiene el smoothness.
            Texture2D metallicSmoothness = BuildMetallicSmoothnessMap(smoothnessTex, "rope_metallic_smooth");
            if (metallicSmoothness != null)
            {
                mat.SetTexture("_MetallicGlossMap", metallicSmoothness);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_GlossMapScale", 1f);
                mat.SetFloat("_Smoothness", 1f);
            }
        }
        else
        {
            // Sin roughness: cuerda semi-rugosa por defecto
            mat.SetFloat("_Smoothness", 0.2f);
        }

        // Occlusion (AO o AO*Cavity)
        if (aoFinal != null)
        {
            mat.SetTexture("_OcclusionMap", aoFinal);
            mat.SetFloat("_OcclusionStrength", 1f);
        }

        // Height (Parallax)
        if (_height != null)
        {
            mat.SetTexture("_ParallaxMap", _height);
            mat.SetFloat("_Parallax", _heightScale);
            mat.EnableKeyword("_PARALLAXMAP");
        }

        // Tiling
        mat.SetTextureScale("_BaseMap", new Vector2(_tiling, _tiling));

        // Surface type: opaco
        mat.SetFloat("_Surface", 0f);
        mat.SetOverrideTag("RenderType", "Opaque");

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[RopeMaterialBuilder] Material creado en: " + _materialPath);
        Selection.activeObject = mat;
        EditorGUIUtility.PingObject(mat);
    }

    // -------------------------------------------------------------------------
    // Helpers de textura
    // -------------------------------------------------------------------------

    private Texture2D InvertTexture(Texture2D src, string saveName)
    {
        string srcPath    = AssetDatabase.GetAssetPath(src);
        string dir        = Path.GetDirectoryName(srcPath);
        string outputPath = Path.Combine(dir, saveName + ".png").Replace("\\", "/");

        // Necesitamos Read/Write en la textura fuente
        TextureImporter ti = AssetImporter.GetAtPath(srcPath) as TextureImporter;
        bool wasReadable   = false;
        if (ti != null)
        {
            wasReadable       = ti.isReadable;
            ti.isReadable     = true;
            ti.textureType    = TextureImporterType.Default;
            ti.sRGBTexture    = false;   // Roughness es lineal
            AssetDatabase.ImportAsset(srcPath, ImportAssetOptions.ForceUpdate);
        }

        Color[] pixels = src.GetPixels();
        if (pixels == null) return null;

        for (int i = 0; i < pixels.Length; i++)
        {
            float inv = 1f - pixels[i].r;
            pixels[i] = new Color(inv, inv, inv, 1f);
        }

        Texture2D result = new Texture2D(src.width, src.height, TextureFormat.RGB24, false);
        result.SetPixels(pixels);
        result.Apply();

        File.WriteAllBytes(outputPath, result.EncodeToPNG());

        // Restaurar estado del import
        if (ti != null && !wasReadable)
        {
            ti.isReadable = false;
            AssetDatabase.ImportAsset(srcPath, ImportAssetOptions.ForceUpdate);
        }

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    /// <summary>
    /// Construye un mapa MetallicSmoothness: RGB=negro (metallic=0), A=smoothness.
    /// URP/Lit lee el Smoothness del canal Alpha del MetallicGlossMap.
    /// </summary>
    private Texture2D BuildMetallicSmoothnessMap(Texture2D smoothness, string saveName)
    {
        string smoothPath = AssetDatabase.GetAssetPath(smoothness);
        string dir        = Path.GetDirectoryName(smoothPath);
        string outputPath = Path.Combine(dir, saveName + ".png").Replace("\\", "/");

        TextureImporter ti = AssetImporter.GetAtPath(smoothPath) as TextureImporter;
        bool wasReadable   = false;
        if (ti != null)
        {
            wasReadable   = ti.isReadable;
            ti.isReadable = true;
            AssetDatabase.ImportAsset(smoothPath, ImportAssetOptions.ForceUpdate);
        }

        Color[] src = smoothness.GetPixels();
        if (src == null) return null;

        Color[] dst = new Color[src.Length];
        for (int i = 0; i < src.Length; i++)
            dst[i] = new Color(0f, 0f, 0f, src[i].r);  // metallic=0, alpha=smoothness

        Texture2D result = new Texture2D(smoothness.width, smoothness.height, TextureFormat.RGBA32, false);
        result.SetPixels(dst);
        result.Apply();

        File.WriteAllBytes(outputPath, result.EncodeToPNG());

        if (ti != null && !wasReadable)
        {
            ti.isReadable = false;
            AssetDatabase.ImportAsset(smoothPath, ImportAssetOptions.ForceUpdate);
        }

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

        // Importar como textura lineal (sin sRGB) y con alpha
        TextureImporter ti2 = AssetImporter.GetAtPath(outputPath) as TextureImporter;
        if (ti2 != null)
        {
            ti2.sRGBTexture = false;
            ti2.alphaSource = TextureImporterAlphaSource.FromInput;
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    private Texture2D MultiplyCavityIntoAO(Texture2D ao, Texture2D cavity, string saveName)
    {
        string aoPath     = AssetDatabase.GetAssetPath(ao);
        string cavPath    = AssetDatabase.GetAssetPath(cavity);
        string dir        = Path.GetDirectoryName(aoPath);
        string outputPath = Path.Combine(dir, saveName + ".png").Replace("\\", "/");

        // Activar Read/Write en ambas
        TextureImporter tiAO  = AssetImporter.GetAtPath(aoPath)  as TextureImporter;
        TextureImporter tiCav = AssetImporter.GetAtPath(cavPath) as TextureImporter;
        bool aoRead  = tiAO  != null ? tiAO.isReadable  : true;
        bool cavRead = tiCav != null ? tiCav.isReadable : true;

        if (tiAO  != null) { tiAO.isReadable  = true; AssetDatabase.ImportAsset(aoPath,  ImportAssetOptions.ForceUpdate); }
        if (tiCav != null) { tiCav.isReadable = true; AssetDatabase.ImportAsset(cavPath, ImportAssetOptions.ForceUpdate); }

        Color[] aoPixels  = ao.GetPixels();
        Color[] cavPixels = cavity.GetPixels();

        // Escalar cavity al mismo tamano que AO si difieren
        if (aoPixels.Length != cavPixels.Length)
        {
            // Simplificacion: ignorar cavity si los tamanos no coinciden
            Debug.LogWarning("[RopeMaterialBuilder] AO y Cavity tienen tamanos distintos. Se usa solo AO.");
            return ao;
        }

        Color[] result = new Color[aoPixels.Length];
        for (int i = 0; i < aoPixels.Length; i++)
            result[i] = aoPixels[i] * cavPixels[i];

        Texture2D tex = new Texture2D(ao.width, ao.height, TextureFormat.RGB24, false);
        tex.SetPixels(result);
        tex.Apply();

        File.WriteAllBytes(outputPath, tex.EncodeToPNG());

        if (tiAO  != null && !aoRead)  { tiAO.isReadable  = false; AssetDatabase.ImportAsset(aoPath,  ImportAssetOptions.ForceUpdate); }
        if (tiCav != null && !cavRead) { tiCav.isReadable = false; AssetDatabase.ImportAsset(cavPath, ImportAssetOptions.ForceUpdate); }

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    private void EnsureNormalMapImportType(Texture2D normal)
    {
        string path = AssetDatabase.GetAssetPath(normal);
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;

        ti.textureType = TextureImporterType.NormalMap;

        // Flip Green Channel: convierte DirectX normal map (Y hacia abajo)
        // a OpenGL normal map (Y hacia arriba) que es lo que espera Unity.
        TextureImporterSettings settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.flipGreenChannel = true;
        ti.SetTextureSettings(settings);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }
}
#endif
