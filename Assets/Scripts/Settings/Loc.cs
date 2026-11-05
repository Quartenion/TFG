using System;
using UnityEngine;

/// <summary>
/// Idioma del juego, español (por defecto) o inglés. Se elige en los ajustes (menú principal y pausa) y se guarda.
///   - Los textos escritos en el código usan Loc.T("Saltar", "Jump"), que devuelve el del idioma actual.
///   - Los textos de las escenas y los prefabs usan el componente LocalizedText (campos Español y English).
///   - Al cambiar de idioma se lanza Loc.OnChanged, y los LocalizedText y las pantallas abiertas se actualizan.
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

    /// <summary>Pasa al siguiente idioma (botón de los ajustes).</summary>
    public static void Cycle() => Current = English ? Language.Es : Language.En;

    /// <summary>Texto del botón de idioma en los ajustes.</summary>
    public static string ButtonLabel => English ? "LANGUAGE: ENGLISH" : "IDIOMA: ESPAÑOL";
}
