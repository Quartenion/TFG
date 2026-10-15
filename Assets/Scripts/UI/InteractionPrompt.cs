using UnityEngine;

/// <summary>
/// Aviso de interacción en pantalla («[ E ]  …»): recuadro semitransparente centrado abajo con el texto parpadeando.
/// Lo comparten la puerta de inicio, el muro de inicio y los puestos de tienda y contratos.
/// Se sigue llamando desde OnGUI (cada frame que el aviso debe verse), pero ahora se dibuja con UI normal
/// (TextMeshPro, fuente del juego) en el componente <see cref="InteractionPromptUI"/>, que puede estar HORNEADO
/// dentro del Canvas de la escena para editarlo en el editor.
/// </summary>
public static class InteractionPrompt
{
    /// <summary>Muestra el aviso este frame. 'width' = ancho mínimo del recuadro; 'color' = color del texto.</summary>
    public static void Draw(string text, float width, Color color) => InteractionPromptUI.Show(text, width, color);
}
