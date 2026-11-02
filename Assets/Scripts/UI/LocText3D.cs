using TMPro;
using UnityEngine;

/// <summary>Texto 3D (TextMeshPro) en el idioma del juego: pone 'es' o 'en' al empezar (p. ej. los carteles de las puertas).</summary>
[RequireComponent(typeof(TextMeshPro))]
public class LocText3D : MonoBehaviour
{
    [TextArea] public string es;
    [TextArea] public string en;

    private void Start()
    {
        var t = GetComponent<TextMeshPro>();
        if (t != null) t.text = Loc.T(es, string.IsNullOrEmpty(en) ? es : en);
    }
}
