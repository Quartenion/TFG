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

    [Header("Corazón firme (mantener clic, mejora de la tienda)")]
    [Tooltip("Punto de la animación (0-1) en el que se queda la mano mientras mantienes el corazón (0.3 = cerrada).")]
    [Range(0f, 1f)] public float closedNormalizedTime = 0.3f;
    [Tooltip("¿El corazón también se queda apretado en ese punto de su animación?")]
    public bool freezeHeartToo = true;

    private InputAction _clickAction;
    private bool _holding;

    void OnEnable()
    {
        // El latido es con clic IZQUIERDO o DERECHO (igual que en RhythmInput).
        _clickAction = new InputAction("HandClick", binding: "<Mouse>/leftButton");
        _clickAction.AddBinding("<Mouse>/rightButton");
        _clickAction.performed += OnClick;
        _clickAction.Enable();
    }

    void OnDisable()
    {
        if (handAnimator  != null) handAnimator.speed  = 1f;
        if (heartAnimator != null) heartAnimator.speed = 1f;
        _holding   = false;
        IsHandHeld = false;
        if (_clickAction != null)
        {
            _clickAction.performed -= OnClick;
            _clickAction.Disable();
            _clickAction = null;
        }
    }

    /// <summary>
    /// ¿La mano está CERRADA y congelada (clic mantenido con «Corazón firme»)? EnergyManager lo usa para dejar de
    /// perder sangre en cuanto la mano se cierra, sin esperar más.
    /// </summary>
    public static bool IsHandHeld { get; private set; }

    private void Update()
    {
        // Al hacer clic, la animación llega hasta el punto de mano cerrada; si EN ESE INSTANTE sigues pulsando, se
        // queda cerrada (se mantiene el corazón); si ya has soltado, sigue y termina como un latido normal.
        bool owned   = PlayerProgress.GetLevel(ShopIds.SteadyHeart) > 0;
        bool pressed = owned && Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed);

        if (pressed)
        {
            bool held = HoldAt(handAnimator, handStateName);
            if (freezeHeartToo) HoldAt(heartAnimator, heartStateName);
            IsHandHeld = held;
            _holding = true;
        }
        else if (_holding)
        {
            // Al soltar: la animación sigue desde donde se quedó (la mano se abre).
            if (handAnimator  != null) handAnimator.speed  = 1f;
            if (heartAnimator != null) heartAnimator.speed = 1f;
            _holding   = false;
            IsHandHeld = false;
        }
    }

    /// <summary>
    /// Mantiene la animación en el punto de "mano cerrada": si aún no ha llegado, deja que llegue; al llegar (o si
    /// ya lo había pasado), se coloca ahí y se congela.
    /// </summary>
    /// <returns>true si ya está cerrada y congelada.</returns>
    private bool HoldAt(Animator anim, string state)
    {
        if (anim == null || !anim.isActiveAndEnabled) return false;
        if (anim.speed == 0f) return true;                                                     // ya congelada
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        bool inState = info.IsName(state);
        if (inState && info.normalizedTime < closedNormalizedTime) return false;             // aún cerrándose
        anim.Play(state, 0, closedNormalizedTime);                                             // justo cerrada
        anim.speed = 0f;
        return true;
    }

    /// <summary>Reproduce la pulsación de la mano y el corazón (p. ej. el Auto-latido de la tienda).</summary>
    public void PlayPress() => DoPress();

    private void OnClick(InputAction.CallbackContext ctx)
    {
        // Clic REAL del jugador en un beat que ya da el Auto-latido: no se anima (lo anima el Auto-latido).
        if (AutoBeater.SuppressPlayerClickNow()) return;
        DoPress();
    }

    private void DoPress()
    {

        // Play(stateName, layer, normalizedTime=0) reinicia la animacion desde
        // el frame 0 aunque ya este reproduciendose, sin esperar a que termine.
        // Guard isActiveAndEnabled: si la mano/corazon estan ocultos (p. ej. en modo
        // kayak el GameObject se desactiva), Play() avisaria "animator is inactive".
        if (handAnimator != null && handAnimator.isActiveAndEnabled)
        {
            handAnimator.speed = 1f;
            handAnimator.Play(handStateName, 0, 0f);
        }

        if (heartAnimator != null && heartAnimator.isActiveAndEnabled)
        {
            heartAnimator.speed = 1f;
            heartAnimator.Play(heartStateName, 0, 0f);
        }
    }
}
