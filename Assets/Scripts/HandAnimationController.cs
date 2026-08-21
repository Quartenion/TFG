using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reproduce las animaciones de la mano y el corazon al hacer clic izquierdo.
/// Si se hace clic mientras la animacion ya se esta reproduciendo, se reinicia
/// desde el principio en lugar de esperar a que termine.
///
/// SETUP:
///   1. Añade este componente al GameObject del jugador (el que tiene PlayerMovement).
///   2. Asigna el Animator de HandPress en handAnimator.
///   3. Asigna el Animator de HeartBeat en heartAnimator.
///   4. handStateName debe coincidir con el nombre del estado en HandController.
///   5. heartStateName debe coincidir con el nombre del estado en HeartController.
/// </summary>
public class HandAnimationController : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Animator del modelo de mano (HandPress)")]
    public Animator handAnimator;

    [Tooltip("Animator del modelo de corazon (HeartBeat)")]
    public Animator heartAnimator;

    [Header("Nombres de estado en el Animator Controller")]
    [Tooltip("Nombre exacto del estado de animacion de la mano (ej: Hand Press)")]
    public string handStateName = "Hand Press";

    [Tooltip("Nombre exacto del estado de animacion del corazon (ej: Heart Beat)")]
    public string heartStateName = "Heart Beat";

    private InputAction _clickAction;

    void OnEnable()
    {
        _clickAction = new InputAction("HandClick", binding: "<Mouse>/leftButton");
        _clickAction.performed += OnClick;
        _clickAction.Enable();
    }

    void OnDisable()
    {
        if (_clickAction != null)
        {
            _clickAction.performed -= OnClick;
            _clickAction.Disable();
            _clickAction = null;
        }
    }

    private void OnClick(InputAction.CallbackContext ctx)
    {
        // Play(stateName, layer, normalizedTime=0) reinicia la animacion desde
        // el frame 0 aunque ya este reproduciendose, sin esperar a que termine.
        // Guard isActiveAndEnabled: si la mano/corazon estan ocultos (p. ej. en modo
        // kayak el GameObject se desactiva), Play() avisaria "animator is inactive".
        if (handAnimator != null && handAnimator.isActiveAndEnabled)
            handAnimator.Play(handStateName, 0, 0f);

        if (heartAnimator != null && heartAnimator.isActiveAndEnabled)
            heartAnimator.Play(heartStateName, 0, 0f);
    }
}
