using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// NITIDEZ DE LA INTERFAZ («Tools/Menús/Mejorar nitidez de la interfaz»):
///   1. FUENTES: las dos fuentes de terror (Heartless y Aftermath) tenían un atlas SDF de 512×512 muestreado a ~75 pt.
///      Los títulos se dibujan a 200-280 pt, así que se veían blandos/borrosos. Se regeneran EN EL MISMO ASSET (no se
///      rompe ninguna referencia) con atlas 4096×4096, muestreo de 200 pt y solo los caracteres que usa el juego.
///   2. SPRITES de la interfaz (Assets/Sprites): sin compresión (la compresión DXT emborrona los bordes) y sin mipmaps.
/// Es seguro repetirlo. Tarda unos segundos.
/// </summary>
public static class UISharpnessTool
{
    private static readonly string[] Fonts =
    {
        "Assets/Fonts/Heartless SDF.asset",
        "Assets/Fonts/aftermath-brk-font/AftermathBrk-dMMR SDF.asset",
    };

    private const int SamplingSize = 200, Padding = 20, AtlasSize = 4096;

    /// <summary>Caracteres que necesita el juego (ASCII + español + símbolos usados en los textos).</summary>
    private const string Extra = "ÁÉÍÓÚÜÑáéíóúüñ¡¿«»·×→←◀▶•…–—";

    [MenuItem("Tools/Menús/Mejorar nitidez de la interfaz (fuentes y sprites)")]
    private static void Run()
    {
        var chars = new System.Text.StringBuilder();
        for (char c = ' '; c <= '~'; c++) chars.Append(c);
        chars.Append(Extra);

        int ok = 0;
        foreach (string path in Fonts) if (RebuildFont(path, chars.ToString())) ok++;
        int sprites = FixSprites("Assets/Sprites");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Nitidez] {ok}/{Fonts.Length} fuentes regeneradas a {AtlasSize}×{AtlasSize} ({SamplingSize} pt) y {sprites} sprites sin compresión.");
    }

    private static bool RebuildFont(string path, string chars)
    {
        var old = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (old == null) { Debug.LogWarning($"[Nitidez] No existe {path}"); return false; }

        Font src = old.sourceFontFile;
        if (src == null)
        {
            // Busca el .ttf/.otf junto al asset.
            foreach (string f in Directory.GetFiles(Path.GetDirectoryName(path)))
                if (f.EndsWith(".ttf") || f.EndsWith(".otf")) { src = AssetDatabase.LoadAssetAtPath<Font>(f.Replace('\\', '/')); if (src != null) break; }
        }
        if (src == null) { Debug.LogWarning($"[Nitidez] {path}: no se encontró la fuente original (.ttf)."); return false; }

        Texture2D oldTex = old.atlasTexture;
        Material oldMat = old.material;
        if (oldTex == null || oldMat == null) { Debug.LogWarning($"[Nitidez] {path}: le falta la textura o el material."); return false; }

        // Fuente nueva en memoria, de alta resolución.
        TMP_FontAsset fresh = TMP_FontAsset.CreateFontAsset(src, SamplingSize, Padding, GlyphRenderMode.SDFAA,
                                                            AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, false);
        if (fresh == null) { Debug.LogWarning($"[Nitidez] {path}: no se pudo crear la fuente."); return false; }
        fresh.TryAddCharacters(chars, out string missing);
        if (!string.IsNullOrEmpty(missing)) Debug.Log($"[Nitidez] {Path.GetFileName(path)}: la fuente no tiene «{missing}» (se usará la de reserva).");

        Texture2D newTex = fresh.atlasTexture;
        string name = old.name;

        // 1. Textura: la del atlas viejo (no legible desde scripts) se sustituye por la nueva dentro del mismo asset.
        string texName = oldTex.name;
        AssetDatabase.RemoveObjectFromAsset(oldTex);
        Object.DestroyImmediate(oldTex, true);
        newTex.name = texName;
        newTex.hideFlags = HideFlags.None;
        AssetDatabase.AddObjectToAsset(newTex, old);
        Texture2D atlas = newTex;

        // 2. Datos de la fuente (glifos, métricas…) copiados al asset de siempre.
        EditorUtility.CopySerialized(fresh, old);
        old.name = name;
        var so = new SerializedObject(old);
        var texArr = so.FindProperty("m_AtlasTextures");
        if (texArr != null) { texArr.arraySize = 1; texArr.GetArrayElementAtIndex(0).objectReferenceValue = atlas; }
        var matProp = so.FindProperty("m_Material") ?? so.FindProperty("material");
        if (matProp != null) matProp.objectReferenceValue = oldMat;
        so.ApplyModifiedPropertiesWithoutUndo();
        old.atlasPopulationMode = AtlasPopulationMode.Static;   // (en el juego no hace falta generar nada)

        // 3. Material: apunta al atlas nuevo con su tamaño y su margen SDF.
        oldMat.SetTexture(ShaderUtilities.ID_MainTex, atlas);
        oldMat.SetFloat(ShaderUtilities.ID_TextureWidth, atlas.width);
        oldMat.SetFloat(ShaderUtilities.ID_TextureHeight, atlas.height);
        oldMat.SetFloat(ShaderUtilities.ID_GradientScale, Padding + 1);
        ShaderUtilities.UpdateShaderRatios(oldMat);

        // Fuera lo temporal.
        if (fresh.material != null && fresh.material != oldMat) Object.DestroyImmediate(fresh.material);
        Object.DestroyImmediate(fresh);

        old.ReadFontAssetDefinition();
        EditorUtility.SetDirty(atlas);
        EditorUtility.SetDirty(oldMat);
        EditorUtility.SetDirty(old);
        TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, old);
        return true;
    }

    private static int FixSprites(string folder)
    {
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(p) is TextureImporter ti) || ti.textureType != TextureImporterType.Sprite) continue;
            bool changed = false;
            if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
            if (ti.filterMode != FilterMode.Bilinear) { ti.filterMode = FilterMode.Bilinear; changed = true; }
            // Quita la compresión también en las sobrescrituras por plataforma (PC y Web).
            foreach (string platform in new[] { "Standalone", "WebGL" })
            {
                var s = ti.GetPlatformTextureSettings(platform);
                if (s.overridden && s.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    s.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.SetPlatformTextureSettings(s);
                    changed = true;
                }
            }
            if (changed) { ti.SaveAndReimport(); n++; }
        }
        return n;
    }
}
