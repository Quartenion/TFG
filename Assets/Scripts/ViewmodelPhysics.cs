using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Fisicas procedurales del viewmodel (mano/arma) al estilo Valorant.
///
/// Efectos:
///   - Sway rotacional al mover el raton (la mano se inclina levemente)
///   - Lag posicional al moverse (la mano va por detras)
///   - Roll lateral al hacer strafe
///   - Spring de salto: la mano baja al saltar y rebota al aterrizar
///
/// SETUP:
///   1. Pon este componente en ArmRig (o el padre de la mano).
///   2. Asigna el CharacterController del jugador en el Inspector.
/// </summary>
[AddComponentMenu("Player/Viewmodel Physics")]
public class ViewmodelPhysics : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("CharacterController del jugador (en el raiz Player).")]
    [SerializeField] private CharacterController _cc;

    // ── Sway de raton ─────────────────────────────────────────────────────────

    [Header("Sway de camara (raton)")]
    [Tooltip("Sensibilidad del sway al mover el raton. Valorant: ~0.02-0.05.")]
    [SerializeField] private float _mouseSwayFactor   = 0.03f;
    [Tooltip("Angulo maximo de inclinacion por sway (grados).")]
    [SerializeField] private float _mouseSwayMaxDeg   = 4f;
    [Tooltip("Velocidad con la que el sway vuelve al reposo cuando el raton esta quieto.")]
    [SerializeField] private float _mouseSwayReturn   = 8f;

    // ── Lag de movimiento ─────────────────────────────────────────────────────

    [Header("Lag de movimiento")]
    [Tooltip("Cuanto se desplaza la mano respecto a la camara al moverse.")]
    [SerializeField] private float _moveLagAmount     = 0.025f;
    [Tooltip("Roll (inclinacion) al hacer strafe lateral (grados).")]
    [SerializeField] private float _strafeTiltDeg     = 2.5f;
    [Tooltip("Velocidad de suavizado del lag posicional.")]
    [SerializeField] private float _moveLagSmoothing  = 7f;

    // ── Spring de salto / aterrizaje ──────────────────────────────────────────

    [Header("Intensidad global")]
    [Tooltip("Escala todos los efectos proporcionalmente. 0 = sin movimiento, 1 = efecto base, 10 = maximo.")]
    [Range(0f, 10f)]
    [SerializeField] private float _intensity = 1f;

    [Header("Spring de salto")]
    [Tooltip("Desplazamiento vertical de la mano al saltar (negativo = baja).")]
    [SerializeField] private float _jumpKickY         = -0.04f;
    [Tooltip("Inclinacion hacia atras al saltar (grados).")]
    [SerializeField] private float _jumpKickPitch     =  5f;
    [Tooltip("Multiplicador de rebote al aterrizar segun velocidad de caida.")]
    [SerializeField] private float _landKickMultiplier = 0.4f;
    [Tooltip("Rigidez del muelle (cuanto mas alto, mas rapido vuelve al centro).")]
    [SerializeField] private float _springStiffness   = 120f;
    [Tooltip("Amortiguacion del muelle (evita oscilaciones infinitas).")]
    [SerializeField] private float _springDamping     = 14f;

    // ── Estado interno ────────────────────────────────────────────────────────

    private Vector3    _restPos;          // posicion local en reposo
    private Quaternion _restRot;          // rotacion local en reposo

    // Sway de raton
    private float _swayRoll;             // Z (roll): mouse X
    private float _swayPitch;            // X (pitch): mouse Y

    // Lag de movimiento
    private Vector3 _lagPos;             // offset de posicion
    private float   _lagRoll;            // roll por strafe

    // Spring de salto
    private float _springY;              // desplazamiento Y del spring
    private float _springVelY;           // velocidad del spring Y
    private float _springPitch;          // inclinacion del spring
    private float _springVelPitch;       // velocidad del spring pitch

    // Deteccion de salto/aterrizaje
    private bool  _wasGrounded;
    private float _prevVelY;

    // =========================================================================
    // UNITY
    // =========================================================================

    private void Start()
    {
        _restPos     = transform.localPosition;
        _restRot     = transform.localRotation;
        _wasGrounded = _cc != null && _cc.isGrounded;
    }

    private void Update()
    {
        if (Time.timeScale == 0f) return;   // no actualizar si el juego esta pausado

        UpdateMouseSway();
        UpdateMovementLag();
        UpdateJumpSpring();
        Compose();
    }

    // =========================================================================
    // MOUSE SWAY
    // =========================================================================

    private void UpdateMouseSway()
    {
        Vector2 delta = Mouse.current != null
            ? Mouse.current.delta.ReadValue()
            : Vector2.zero;

        // La mano se inclina en SENTIDO CONTRARIO al movimiento del raton
        //   Mouse derecha -> roll izquierda (como en Valorant)
        //   Mouse arriba  -> pitch hacia abajo
        _swayRoll  -= delta.x * _mouseSwayFactor;
        _swayPitch -= delta.y * _mouseSwayFactor;

        _swayRoll  = Mathf.Clamp(_swayRoll,  -_mouseSwayMaxDeg, _mouseSwayMaxDeg);
        _swayPitch = Mathf.Clamp(_swayPitch, -_mouseSwayMaxDeg, _mouseSwayMaxDeg);

        // Volver al centro progresivamente cuando el raton esta quieto
        float t = Time.deltaTime * _mouseSwayReturn;
        _swayRoll  = Mathf.Lerp(_swayRoll,  0f, t);
        _swayPitch = Mathf.Lerp(_swayPitch, 0f, t);
    }

    // =========================================================================
    // MOVEMENT LAG
    // =========================================================================

    private void UpdateMovementLag()
    {
        if (_cc == null) return;

        // Velocidad del jugador en el espacio local del padre (la camara)
        // Esto hace que el lag sea relativo a la vista, no al mundo
        Transform parentT = transform.parent != null ? transform.parent : transform;
        Vector3 localVel  = parentT.InverseTransformDirection(_cc.velocity);

        // Offset posicional: la mano va ligeramente opuesta a la velocidad
        Vector3 targetLag = new Vector3(
            -localVel.x * _moveLagAmount,
            0f,
            -localVel.z * _moveLagAmount * 0.25f   // menos lag en Z (adelante/atras)
        );
        targetLag = Vector3.ClampMagnitude(targetLag, _moveLagAmount * 2.5f);

        float t  = Time.deltaTime * _moveLagSmoothing;
        _lagPos  = Vector3.Lerp(_lagPos, targetLag, t);

        // Roll al strafe: la mano se inclina en la direccion del movimiento lateral
        float targetRoll = Mathf.Clamp(
            -localVel.x * _strafeTiltDeg * 0.12f,
            -_strafeTiltDeg, _strafeTiltDeg);
        _lagRoll = Mathf.Lerp(_lagRoll, targetRoll, t);
    }

    // =========================================================================
    // JUMP SPRING
    // =========================================================================

    private void UpdateJumpSpring()
    {
        if (_cc == null) return;

        bool  grounded = _cc.isGrounded;
        float velY     = _cc.velocity.y;

        // Detectar salto (despegar del suelo con velocidad ascendente)
        if (_wasGrounded && !grounded && velY > 0.5f)
        {
            // Impulso inicial: aproximar la velocidad necesaria para alcanzar _jumpKickY
            float omega = Mathf.Sqrt(_springStiffness);
            _springVelY     += _jumpKickY    * omega;
            _springVelPitch += _jumpKickPitch * omega * 0.4f;
        }

        // Detectar aterrizaje (estaba en aire, ahora en suelo)
        if (!_wasGrounded && grounded)
        {
            float impact    = Mathf.Abs(_prevVelY) * _landKickMultiplier;
            float omega     = Mathf.Sqrt(_springStiffness);
            _springVelY    += impact * omega * 0.03f;
            _springVelPitch -= impact * omega * 0.04f;
        }

        // Integrar el spring amortiguado: F = -k*x - c*v
        float dt = Time.deltaTime;
        _springVelY     += (-_springStiffness * _springY     - _springDamping * _springVelY)     * dt;
        _springVelPitch += (-_springStiffness * _springPitch - _springDamping * _springVelPitch) * dt;
        _springY        += _springVelY     * dt;
        _springPitch    += _springVelPitch * dt;

        _wasGrounded = grounded;
        _prevVelY    = velY;
    }

    // =========================================================================
    // COMPOSE — combinar todos los efectos
    // =========================================================================

    private void Compose()
    {
        float i = _intensity;

        // ── Posicion ─────────────────────────────────────────────────────────
        transform.localPosition = _restPos
            + _lagPos           * i              // lag de movimiento
            + Vector3.up * (_springY * i);       // spring de salto/aterrizaje

        // ── Rotacion ─────────────────────────────────────────────────────────
        Quaternion rMouse  = Quaternion.Euler(_swayPitch * i, 0f, _swayRoll * i);  // sway de raton
        Quaternion rStrafe = Quaternion.Euler(0f, 0f, _lagRoll * i);               // roll de strafe
        Quaternion rSpring = Quaternion.Euler(_springPitch * i, 0f, 0f);           // spring de salto

        transform.localRotation = _restRot * rMouse * rStrafe * rSpring;
    }

    // =========================================================================
    // GIZMOS (solo en editor)
    // =========================================================================

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.05f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, transform.forward * 0.15f);
    }
#endif
}
