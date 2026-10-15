using TMPro;
using UnityEngine;

/// <summary>
/// Texto de una escena o prefab traducido: pon aquí el texto en español y en inglés y se mostrará el del idioma elegido
/// en Ajustes (y cambia al momento si se cambia el idioma).
/// </summary>
[RequireComponent(typeof(TMP_Text))]
[AddComponentMenu("UI/Localized Text")]
public class LocalizedText : MonoBehaviour
{
    [TextArea] public string spanish;
    [TextArea] public string english;

    private TMP_Text _text;

    private void OnEnable()
    {
        Loc.OnChanged += Apply;
        Apply();
    }

    private void OnDisable() => Loc.OnChanged -= Apply;

    public void Apply()
    {
        if (_text == null) _text = GetComponent<TMP_Text>();
        if (_text != null) _text.text = Loc.T(spanish, english);
    }
}
