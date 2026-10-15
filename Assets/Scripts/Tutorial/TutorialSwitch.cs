using UnityEngine;

/// <summary>
/// INTERRUPTOR DEL TUTORIAL (ponlo en el objeto «Tutorial» del nivel 1).
///   • Tutorial Activo  : desmarcado = este nivel nunca hace el tutorial (ni las explicaciones de una vez).
///   • Forzar Siempre   : (para probar) hace el tutorial completo en cada partida, aunque ya se haya completado.
/// Sin este componente en la escena, el tutorial funciona normal: sale la primera vez o si se activa en Ajustes.
/// </summary>
[AddComponentMenu("Tutorial/Tutorial Switch")]
public class TutorialSwitch : MonoBehaviour
{
    [Tooltip("Desmarcado = el nivel no hace el tutorial.")]
    public bool tutorialActivo = true;

    [Tooltip("(Para probar) Hace el tutorial en cada partida, aunque ya se haya completado.")]
    public bool forzarSiempre = false;
}
