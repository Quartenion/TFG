using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Botón de IDIOMA de los Ajustes: cada clic cambia entre español e inglés (Loc) y su texto muestra el idioma actual.
/// Lo añaden «Tools/Idioma/…» en el menú principal y en el menú de pausa.
/// </summary>
[RequireComponent(typeof(Button))]
[AddComponentMenu("UI/Language Button")]
public class LanguageButton : MonoBehaviour
{
    private TMP_Text _label;

    private void Awake()
    {
        _label = GetComponentInChildren<TMP_Text>(true);
        GetComponent<Button>().onClick.AddListener(Loc.Cycle);
    }

    private void OnEnable()
    {
        Loc.OnChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => Loc.OnChanged -= Refresh;

    private void Refresh()
    {
        if (_label == null) _label = GetComponentInChildren<TMP_Text>(true);
        if (_label != null) _label.text = Loc.ButtonLabel;
    }
}
