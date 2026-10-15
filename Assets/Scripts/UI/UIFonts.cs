using TMPro;
using UnityEngine;

/// <summary>
/// FUENTES DEL JUEGO para los textos creados por código: Metal Mania (títulos) y Pirata One (resto).
/// Se buscan entre las fuentes ya cargadas (las usan el Canvas y los menús de cada escena, así que también van en
/// el juego exportado). Si no se encuentran, se deja la fuente por defecto de TextMesh Pro.
/// </summary>
public static class UIFonts
{
    private static TMP_FontAsset _title, _body;

    public static TMP_FontAsset Title => _title != null ? _title : (_title = Find("MetalMania") ?? Body);
    // (Pirata One es además la fuente POR DEFECTO de TextMesh Pro —TMP Settings—, así que siempre está en la build.)
    public static TMP_FontAsset Body  => _body  != null ? _body  : (_body  = Find("PirataOne") ?? TMP_Settings.defaultFontAsset);

    /// <summary>
    /// Las mismas fuentes para los textos dibujados con OnGUI (avisos «[ E ] …», resultados del nivel, toasts…).
    /// Sale del archivo de fuente original de cada TMP_FontAsset (los nuestros son dinámicos y lo conservan).
    /// </summary>
    public static Font LegacyTitle => Title != null && Title.sourceFontFile != null ? Title.sourceFontFile : LegacyBody;
    public static Font LegacyBody  => Body  != null ? Body.sourceFontFile : null;

    private static TMP_FontAsset Find(string prefix)
    {
        foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (f != null && f.name.StartsWith(prefix)) return f;
        return null;
    }

    /// <summary>Pone la fuente de título o la normal (si está disponible).</summary>
    public static void Apply(TMP_Text t, bool title)
    {
        if (t == null) return;
        TMP_FontAsset f = title ? Title : Body;
        if (f == null) return;
        t.font = f;
        t.fontSharedMaterial = f.material;
    }

    /// <summary>Deja solo letras (con tildes y Ñ), números y espacios: sin símbolos que la fuente no tenga (◀, ¡, «…).</summary>
    public static string LettersOnly(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s)
            if (char.IsLetterOrDigit(ch) || ch == ' ') sb.Append(ch);
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " {2,}", " ").Trim();
    }
}
