using System;
using UnityEngine;

/// <summary>
/// IDIOMA del juego: español (por defecto) o inglés. Se elige en Ajustes (menú principal y pausa) y se guarda.
///   • Textos escritos en código:  Loc.T("Saltar", "Jump")  → devuelve el del idioma actual.
///   • Textos de escenas/prefabs:  componente LocalizedText (campos Español / English).
///   • Al cambiar de idioma se lanza Loc.OnChanged (los LocalizedText y las pantallas abiertas se refrescan).
/// </summary>
public static class Loc
{
    public enum Language { Es, En }

    private const string Key = "Language";
    private static bool _loaded;
    private static Language _lang;

    public static event Action OnChanged;

    public static Language Current
    {
        get
        {
            if (!_loaded) { _lang = PlayerPrefs.GetInt(Key, (int)Language.Es) == (int)Language.En ? Language.En : Language.Es; _loaded = true; }
            return _lang;
        }
        set
        {
            if (Current == value) return;
            _lang = value;
            PlayerPrefs.SetInt(Key, (int)value);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }

    public static bool English => Current == Language.En;

    /// <summary>Devuelve el texto en el idioma actual.</summary>
    public static string T(string es, string en) => English && !string.IsNullOrEmpty(en) ? en : es;

    /// <summary>Pasa al siguiente idioma (botón de Ajustes).</summary>
    public static void Cycle() => Current = English ? Language.Es : Language.En;

    /// <summary>Texto del botón de idioma en Ajustes.</summary>
    public static string ButtonLabel => English ? "LANGUAGE: ENGLISH" : "IDIOMA: CASTELLANO";
}
