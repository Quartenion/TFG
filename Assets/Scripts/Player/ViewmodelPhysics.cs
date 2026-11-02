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
    [Tooltip("PlayerMovement del jugador: sirve para no rebotar la mano al subir escaleras. Auto si se deja vacío.")]
    [SerializeField] private PlayerMovement _playerMovement;

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
    [Tooltip("Lo máximo (m) que la mano se ACERCA a la cámara al correr hacia delante (0 = nada).")]
    [SerializeField] private float _maxBackLag = 0.03f;

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
    [Tooltip("Velocidad vertical mínima para contar como SALTO real (filtra micro-saltos de escalones/bordes).")]
    [SerializeField] private float _jumpDetectMinVelY = 2f;
    [Tooltip("Velocidad de caída mínima para contar como ATERRIZAJE (filtra rebotes de escalones/bordes).")]
    [SerializeField] private float _landDetectMinVelY = 2f;
    [Tooltip("Tope del desplazamiento del muelle (m). La mano nunca puede derivar más de esto del reposo.")]
    [SerializeField] private float _maxSpringOffset = 0.1f;

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

        // La mano se acerca a la cámara al correr (lag de movimiento): con el plano cercano a 0.3 m se cortaba.
        // Las cámaras de la mano (no la principal) usan un plano cercano mínimo.
        foreach (Camera cam in FindObjectsByType<Camera>())
            if (!cam.CompareTag("MainCamera") && (cam.cullingMask & (1 << gameObject.layer)) != 0)
                cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, 0.01f);
        _wasGrounded = _cc != null && _cc.isGrounded;

        if (_playerMovement == null)
            _playerMovement = _cc != null ? _cc.GetComponent<PlayerMovement>() : null;
        if (_playerMovement == null)
            _playerMovement = FindAnyObjectByType<PlayerMovement>();
    }

    private void Update()
    {
        if (Time.timeScale == 0f) return;   // no actualizar si el juego esta pausado

        UpdateMouseSway();
        UpdateMovementLag();
        UpdateJumpSpring();
        UpdateBrace();
        Compose();
    }

    // =========================================================================
    // POSE DE BLOQUEO (Q): brazos en X, «preparándose para el impacto»
    // =========================================================================

    [Header("Pose de bloqueo (Q): brazos en X")]
    [Tooltip("Dónde queda el CENTRO del brazo en la pantalla al bloquear (coordenadas de pantalla: 0,0 = centro; " +
             "X −1…1 de izquierda a derecha, Y −1…1 de abajo arriba). X = 0: se cruzan por la muñeca; cuanto más negativa, " +
             "más se separan y el cruce baja hacia el ANTEBRAZO (el izquierdo es su espejo).")]
    [SerializeField] private Vector2 _braceScreenCenter = new Vector2(-0.06f, -0.08f);
    [Tooltip("Desplaza los DOS brazos a la vez en horizontal (pantalla, −1…1) para centrar el vértice de la X con el " +
             "crosshair. Positivo = a la derecha.")]
    [SerializeField] private float _braceNudgeX = 0.016f;
    [Tooltip("Giro del brazo SOLO en el eje Z de la pantalla, alrededor de SU CENTRO (grados). Positivo = antihorario.")]
    [SerializeField] private float _braceRoll = 52f;
    [Tooltip("Inclinación extra hacia arriba (grados). Déjalo en 0: si no, el dorso de la mano deja de mirar a la cámara.")]
    [SerializeField] private float _bracePitch = 0f;
    [Tooltip("Segundos en subir los brazos.")]
    [SerializeField] private float _braceUpTime = 0.04f;
    [Tooltip("Segundos que se quedan arriba (como máximo; nunca más que la ventana de la Q).")]
    [SerializeField] private float _braceHoldTime = 0.18f;
    [Tooltip("Segundos en bajarlos.")]
    [SerializeField] private float _braceDownTime = 0.12f;
    [Tooltip("Distancia de los brazos a la cámara respecto a la normal (1 = igual; menos = más cerca, más grandes).")]
    [Range(0.4f, 1.2f)]
    [SerializeField] private float _braceDistance = 0.7f;
    [Tooltip("Mostrar el brazo izquierdo (copia en espejo, sin corazón) para formar la X.")]
    [SerializeField] private bool _braceLeftArm = true;
    [Tooltip("Cuánto más ATRÁS (lejos de la cámara) va el brazo izquierdo: así el derecho queda por delante en la X.")]
    [SerializeField] private float _braceLeftBehind = 0.3f;
    [Tooltip("Desde cuánto más ABAJO sube el brazo izquierdo (entra desde fuera de la pantalla, no aparece de golpe).")]
    [SerializeField] private float _braceLeftFromBelow = 3f;
    [Tooltip("Los puños se cierran durante el bloqueo (como al mantener el corazón con «Corazón firme»).")]
    [SerializeField] private bool _braceCloseHands = true;
    [Tooltip("Rebote al subir los brazos: cuánto se pasan de la pose antes de asentarse (0 = sin rebote, 0.25 = un 25 %).")]
    [Range(0f, 0.6f)]
    [SerializeField] private float _braceBounce = 0.25f;
    [Tooltip("Segundos que dura el rebote (subida + oscilación hasta quedarse quietos).")]
    [SerializeField] private float _braceBounceTime = 0.3f;

    // ── Valores por defecto de la pose «empujados» a las escenas ─────────────────
    // Unity guarda en cada escena/prefab los valores del Inspector; cambiar el valor por defecto en el código no los
    // actualiza. Al subir BraceDefaultsVersion, OnValidate copia los valores de abajo al componente (una sola vez).
    private const int BraceDefaultsVersion = 8;
    [SerializeField, HideInInspector] private int _braceDefaultsVersion;

    private void ApplyBraceDefaults()
    {
        _braceScreenCenter  = new Vector2(-0.06f, -0.08f);
        _braceNudgeX        = 0.016f;
        _braceRoll          = 52f;
        _bracePitch         = 0f;
        _braceUpTime        = 0.04f;
        _braceHoldTime      = 0.18f;
        _braceDownTime      = 0.12f;
        _braceDistance      = 0.7f;
        _braceLeftArm       = true;
        _braceLeftBehind    = 0.3f;
        _braceLeftFromBelow = 3f;
        _braceCloseHands    = true;
    }

    private void OnValidate()
    {
        if (_braceDefaultsVersion >= BraceDefaultsVersion) return;
        ApplyBraceDefaults();
        _braceDefaultsVersion = BraceDefaultsVersion;
    }

    /// <summary>El brazo del jugador (para que BlockAbility lo encuentre).</summary>
    public static ViewmodelPhysics Instance { get; private set; }

    private float _braceUntil = -1f, _braceW, _braceStart = -1f;
    private Transform _leftArm;
    private Animator _leftCloseAnim;

    private void OnEnable()  { Instance = this; }
    private void OnDisable() { if (Instance == this) Instance = null; if (_leftArm != null) _leftArm.gameObject.SetActive(false); }

    /// <summary>Pose de bloqueo durante 'seconds' (Q).</summary>
    public void Brace(float seconds)
    {
        if (Time.time >= _braceUntil) _braceStart = Time.time;   // (empieza el rebote; si ya estaba arriba, sigue)
        float hold = Mathf.Max(Mathf.Min(_braceHoldTime, Mathf.Max(0.05f, seconds)), _braceBounceTime - _braceUpTime);
        _braceUntil = Time.time + _braceUpTime + hold;   // (arriba al menos lo que dura el rebote)
        if (_braceLeftArm && _leftArm == null) BuildLeftArm();
    }

    private float _braceNudgeNow;   // desplazamiento común aplicado ahora (para que el izquierdo no lo refleje)
    private bool _hasCenter;
    private Vector3 _centerInArm;   // centro de las mallas del brazo, en coordenadas del propio brazo (sin escala)

    /// <summary>Centro visual del brazo (sus mallas), en el espacio del brazo. Se mide una vez.</summary>
    private Vector3 BraceCenterInArm()
    {
        if (_hasCenter) return _centerInArm;
        bool any = false; Bounds b = default;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            // Solo las mallas del brazo/mano/corazón: las partículas (la sangre del corazón) cambian de tamaño
            // y de sitio y movían el centro → los brazos de la Q salían más abajo.
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        _centerInArm = any ? transform.InverseTransformPoint(b.center) : Vector3.zero;
        _hasCenter = any;
        return _centerInArm;
    }

    private Camera _armCam;

    /// <summary>Punto de pantalla (−1…1) a la profundidad 'z' de la cámara padre, en su espacio local.</summary>
    private Vector3 ScreenToCameraLocal(Vector2 screen, float z)
    {
        if (_armCam == null)
            foreach (Camera cam in FindObjectsByType<Camera>())
                if (!cam.CompareTag("MainCamera") && (cam.cullingMask & (1 << gameObject.layer)) != 0) { _armCam = cam; break; }
        if (_armCam == null && transform.parent != null) _armCam = transform.parent.GetComponent<Camera>();
        float fov    = _armCam != null ? _armCam.fieldOfView : 60f;
        float aspect = _armCam != null ? _armCam.aspect : 16f / 9f;
        float halfH  = Mathf.Abs(z) * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        return new Vector3(screen.x * halfH * aspect, screen.y * halfH, z);
    }

    /// <summary>Curva suave (smoothstep).</summary>
    private static float BraceEase(float x) => x * x * (3f - 2f * x);

    /// <summary>
    /// Peso de la pose de la Q. Al SUBIR: rebote (muelle amortiguado: sube de golpe, se pasa un poco, vuelve y se
    /// asienta). Al BAJAR: suave. Puede pasar de 1 (el rebote) → se usa con Lerp/Slerp sin límite.
    /// </summary>
    private float BraceWeight()
    {
        bool on = Time.time < _braceUntil;
        if (!on || _braceBounce <= 0f || _braceStart < 0f) return BraceEase(_braceW);
        float u = (Time.time - _braceStart) / Mathf.Max(0.05f, _braceBounceTime);
        if (u >= 1f) return 1f;
        // 1 − e^(−d·u)·cos(3πu): primer pico en u = 1/3 con un exceso igual a _braceBounce.
        float d = -3f * Mathf.Log(Mathf.Clamp(_braceBounce, 0.01f, 0.99f));
        return 1f - Mathf.Exp(-d * u) * Mathf.Cos(3f * Mathf.PI * u);
    }

    private HandAnimationController _hac;
    private bool _handsClosed;

    private void UpdateBrace()
    {
        bool on = Time.time < _braceUntil;
        float rate = on ? 1f / Mathf.Max(0.01f, _braceUpTime) : 1f / Mathf.Max(0.01f, _braceDownTime);
        _braceW = Mathf.MoveTowards(_braceW, on ? 1f : 0f, Time.deltaTime * rate);

        // Puños cerrados mientras dura el bloqueo (mismo punto de la animación que «Corazón firme»).
        if (!_braceCloseHands) return;
        if (_hac == null) _hac = FindAnyObjectByType<HandAnimationController>();
        if (_hac == null) return;
        if (on && _handsClosed && _hac.handAnimator != null && _hac.handAnimator.speed != 0f)
            _handsClosed = false;   // (un clic de latido la ha abierto: se vuelve a cerrar)
        if (on && !_handsClosed)
        {
            Freeze(_hac.handAnimator, _hac.handStateName, _hac.closedNormalizedTime);
            Freeze(_hac.heartAnimator, _hac.heartStateName, _hac.closedNormalizedTime);
            _handsClosed = true;
        }
        else if (!on && _handsClosed && !HandAnimationController.IsHandHeld)
        {
            if (_hac.handAnimator  != null) _hac.handAnimator.speed  = 1f;   // (se abren siguiendo la animación)
            if (_hac.heartAnimator != null) _hac.heartAnimator.speed = 1f;
            _handsClosed = false;
        }
    }

    private static void Freeze(Animator anim, string state, float normalizedTime)
    {
        if (anim == null || !anim.isActiveAndEnabled) return;
        anim.Play(state, 0, normalizedTime);
        anim.speed = 0f;
    }

    /// <summary>Brazo izquierdo: copia en espejo de este brazo, sin el corazón ni los scripts.</summary>
    private void BuildLeftArm()
    {
        // Rama hija que lleva el corazón (para quitarla de la copia).
        int heartChild = -1;
        HandAnimationController hac = FindAnyObjectByType<HandAnimationController>();
        if (hac != null && hac.heartAnimator != null)
        {
            Transform t = hac.heartAnimator.transform;
            while (t != null && t.parent != transform) t = t.parent;
            if (t != null) heartChild = t.GetSiblingIndex();
        }

        GameObject copy = Instantiate(gameObject, transform.parent);
        copy.name = name + " (izquierda)";
        DestroyImmediate(copy.GetComponent<ViewmodelPhysics>());
        if (heartChild >= 0 && heartChild < copy.transform.childCount)
            DestroyImmediate(copy.transform.GetChild(heartChild).gameObject);
        foreach (HeartBloodFX fx in copy.GetComponentsInChildren<HeartBloodFX>(true))
            DestroyImmediate(fx.gameObject);   // (la sangre del corazón no va en la mano izquierda)
        // Mano izquierda: quieta y CERRADA en el puño (mismo punto que la derecha).
        if (_hac == null) _hac = FindAnyObjectByType<HandAnimationController>();
        foreach (Animator a in copy.GetComponentsInChildren<Animator>(true))
        {
            a.speed = 0f;
            if (_braceCloseHands && _hac != null) _leftCloseAnim = a;
        }
        _leftArm = copy.transform;
        _leftArm.gameObject.SetActive(false);
        Instance = this;   // (la copia pasó un instante por OnEnable/OnDisable y lo había cambiado)
    }

    // =========================================================================
    // MOUSE SWAY
    // =========================================================================

    private void UpdateMouseSway()
    {
        Vector2 delta = Mouse.current != null
            ? Mouse.current.delta.ReadValue()
            : Vector2.zero;
        if (PlayerMovement.LookSpikeFrame >= Time.frameCount - 1) delta = Vector2.zero;   // (recolocación del ratón al capturarlo)

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
        // Hacia la cámara (Z negativa) casi nada: al correr hacia delante la mano se metía en la cámara.
        targetLag.z = Mathf.Max(targetLag.z, -_maxBackLag);

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

        // En escaleras NO reaccionamos: al subir escalones isGrounded parpadea y el step-up
        // vertical da picos de velocidad que, sin este filtro, harían botar la mano en cada escalón.
        bool onStairs = _playerMovement != null && _playerMovement.IsOnStairs;

        // Detectar salto REAL (despegar del suelo con velocidad ascendente por encima del umbral)
        if (!onStairs && _wasGrounded && !grounded && velY > _jumpDetectMinVelY)
        {
            // Impulso inicial: aproximar la velocidad necesaria para alcanzar _jumpKickY
            float omega = Mathf.Sqrt(_springStiffness);
            _springVelY     += _jumpKickY    * omega;
            _springVelPitch += _jumpKickPitch * omega * 0.4f;
        }

        // Detectar aterrizaje (estaba en aire, ahora en suelo) con impacto significativo
        if (!onStairs && !_wasGrounded && grounded && Mathf.Abs(_prevVelY) > _landDetectMinVelY)
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

        // Seguridad anti-deriva: acotar el muelle para que la mano nunca se aleje del reposo.
        if (_springY > _maxSpringOffset)  { _springY =  _maxSpringOffset; if (_springVelY > 0f) _springVelY = 0f; }
        if (_springY < -_maxSpringOffset) { _springY = -_maxSpringOffset; if (_springVelY < 0f) _springVelY = 0f; }
        _springPitch = Mathf.Clamp(_springPitch, -12f, 12f);

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

        // ── Pose de bloqueo (Q) ──────────────────────────────────────────────
        if (_braceW > 0f)
        {
            float w = BraceWeight();   // rebote al subir, suave al bajar
            // Pose en ejes de PANTALLA (los de la cámara padre, no los del propio brazo) y girando alrededor del
            // CENTRO del brazo (no de su pivote, que está lejos de la malla).
            Vector3    pos0  = transform.localPosition;
            Quaternion rot0  = transform.localRotation;
            Vector3    cIn   = Vector3.Scale(BraceCenterInArm(), transform.localScale);
            Vector3    cRest = _restPos + _restRot * cIn;                                         // centro en reposo
            Vector3    cT    = ScreenToCameraLocal(_braceScreenCenter, 1f).normalized * (cRest.magnitude * _braceDistance);   // centro destino (misma distancia al ojo)
            // Giro SOLO en Z de pantalla (alrededor de la línea de visión que pasa por el centro del brazo).
            // Al llevar el brazo de la derecha al centro se corrige también la perspectiva (como si se moviera
            // alrededor del ojo), para que no parezca girado en otros ejes: se ve el mismo brazo, solo inclinado.
            Quaternion persp = Quaternion.FromToRotation(cRest.normalized, cT.normalized);
            Quaternion rotB  = Quaternion.AngleAxis(_braceRoll, cT.normalized) * persp
                             * Quaternion.Euler(_bracePitch, 0f, 0f) * _restRot;
            Vector3    posB  = cT - rotB * cIn;
            float      nudge = ScreenToCameraLocal(new Vector2(_braceNudgeX, 0f), cT.z).x;   // los dos brazos a la vez
            posB.x += nudge;
            _braceNudgeNow = nudge * w;
            transform.localPosition = Vector3.LerpUnclamped(pos0, posB, w);
            transform.localRotation = Quaternion.SlerpUnclamped(rot0, rotB, w);
        }

        // Brazo izquierdo = espejo exacto del derecho (forma la X).
        if (_leftArm != null)
        {
            bool show = _braceW > 0.01f;
            if (_leftArm.gameObject.activeSelf != show)
            {
                _leftArm.gameObject.SetActive(show);
                // (al activarse, el Animator vuelve a su estado inicial: se cierra el puño otra vez)
                if (show && _leftCloseAnim != null && _hac != null) Freeze(_leftCloseAnim, _hac.handStateName, _hac.closedNormalizedTime);
            }
            if (show && _leftCloseAnim != null && _hac != null && _braceCloseHands)
            {
                // Puño izquierdo cerrado (se comprueba cada frame: al activarse, el Animator puede volver a su estado inicial).
                var info = _leftCloseAnim.GetCurrentAnimatorStateInfo(0);
                if (_leftCloseAnim.speed != 0f || !info.IsName(_hac.handStateName) ||
                    Mathf.Abs(info.normalizedTime - _hac.closedNormalizedTime) > 0.01f)
                    Freeze(_leftCloseAnim, _hac.handStateName, _hac.closedNormalizedTime);
            }
            if (show)
            {
                Vector3 p = transform.localPosition;
                Quaternion q = transform.localRotation;
                Vector3 s = transform.localScale;
                // (Sin pasarse de 1: el rebote ya le llega al copiar la pose del derecho. Si no, la subida «desde
                // abajo» —3 unidades— también rebotaba y el izquierdo saltaba muchísimo más que el derecho.)
                float wl = Mathf.Clamp01(BraceWeight());
                // Espejo del derecho, un poco más atrás (el derecho queda por DELANTE) y subiendo desde abajo.
                // Corrección de perspectiva: el izquierdo va más atrás y, sin esto, se vería desplazado hacia el centro
                // (la V no quedaría alineada con el crosshair). Se separa lo justo para que su centro se vea simétrico.
                Vector3 cR = p + q * Vector3.Scale(BraceCenterInArm(), s);
                float persp = Mathf.Abs(cR.z) > 0.01f ? (cR.z + _braceLeftBehind) / cR.z : 1f;
                float dx = -cR.x * (persp - 1f) * wl;
                _leftArm.localPosition = new Vector3(-p.x + dx + 2f * _braceNudgeNow, p.y - _braceLeftFromBelow * (1f - wl), p.z + _braceLeftBehind);
                _leftArm.localRotation = new Quaternion(q.x, -q.y, -q.z, q.w);   // reflejo sobre el plano YZ
                _leftArm.localScale    = new Vector3(-s.x, s.y, s.z);
            }
        }
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
