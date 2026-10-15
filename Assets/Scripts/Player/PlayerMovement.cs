using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public Camera playerCamera;
    public float walkSpeed = 6f;
    [Tooltip("Altura máxima del salto en metros. La gravedad y el impulso se calculan automáticamente\n" +
             "para que el jugador llegue exactamente a esta altura en exactamente 1 beat.")]
    public float jumpHeight = 3f;
    [Tooltip("Duración TOTAL del salto en beats (despegue + aterrizaje). 2 = por defecto (1 subiendo, 1 " +
             "bajando). Ponlo a 1 en el Player de ESTE nivel para saltos de un beat exacto. Es por-instancia, " +
             "así que los otros niveles no se ven afectados (siguen en 2).")]
    public float jumpAirTimeBeats = 2f;
    public float lookSpeed = 2f;
    // ── Física del salto derivada de jumpHeight + BPM ─────────────────────
    // Duración total del salto = 2 beats.
    // halfT = beatDuration  (tiempo hasta el pico = 1 beat).
    // Cinemática: h = ½ g t²  →  g = 2h / halfT²  →  v0 = √(2gh)

    private float BeatDuration => BeatManager.BeatDurationOf(beatManager);

    /// <summary>Gravedad calculada para que el salto dure exactamente jumpAirTimeBeats beats.</summary>
    private float Gravity
    {
        get
        {
            // tiempo hasta el pico = mitad del vuelo total. 2 beats -> halfT = 1 beat (por defecto).
            if (_gravityOverride > 0f) return _gravityOverride;   // p. ej. cayendo con el suelo en la Fase 3
            float halfT = BeatDuration * Mathf.Max(0.05f, jumpAirTimeBeats * 0.5f);
            return 2f * jumpHeight / (halfT * halfT);
        }
    }

    private float _gravityOverride;

    /// <summary>Fuerza una gravedad concreta (m/s²) hasta que se quite con 0. La usa el jefe para que el jugador caiga
    /// a la MISMA velocidad que los bloques del suelo al derrumbarse.</summary>
    public void SetGravityOverride(float gravity) => _gravityOverride = Mathf.Max(0f, gravity);

    /// <summary>Gravedad actual del jugador (m/s²). La usa el jefe para predecir dónde cae (Fase 3).</summary>
    public float CurrentGravity => Gravity;

    /// <summary>Impulso vertical inicial: jugador alcanza jumpHeight en 1 beat (subida).</summary>
    private float JumpPower => Mathf.Sqrt(2f * Gravity * jumpHeight);

    /// <summary>Impulso del doble salto usando la misma gravedad, pero con airJumpHeight.</summary>
    private float AirJumpPower => Mathf.Sqrt(2f * Gravity * airJumpHeight);

    /// <summary>Impulso vertical del walljump. Misma gravedad, altura configurable.</summary>
    private float WalljumpPower => Mathf.Sqrt(2f * Gravity * walljumpHeight);

    public float lookXLimit = 45f;
    public float defaultHeight = 2f;

    [Header("Costes de energia")]
    [Tooltip("Energia que cuesta saltar.")]
    public float jumpEnergyCost = 15f;

    [Header("Doble salto")]
    public int maxJumps = 2;
    [Tooltip("Altura máxima del doble salto en metros (mismo cálculo que jumpHeight).")]
    public float airJumpHeight = 2f;
    public float airJumpEnergyCost = 15f;

    [Header("Dash (E + A/D)")]
    public float dashSpeed = 28f;
    public float dashDuration = 0.18f;
    public float dashEnergyCost = 25f;

    [Header("Slide (Shift)")]
    [Tooltip("Duracion del slide en segundos")]
    public float slideDuration = 0.55f;
    [Tooltip("Energia que cuesta el slide")]
    public float slideEnergyCost = 12f;
    [Tooltip("El slide requiere estar al ritmo")]
    public bool slideRequiresBeat = false;
    [Tooltip("Angulo de tilt de la camara durante el slide (grados, eje pitch)")]
    public float slideCameraTilt = 12f;
    [Tooltip("Velocidad de lerp del tilt de camara")]
    public float slideCameraTiltSpeed = 10f;
    [Tooltip("Velocidad de lerp del cambio de altura (slide / uncrouch)")]
    public float heightLerpSpeed = 14f;

    [Header("Slam (S en el aire)")]
    [Tooltip("Velocidad vertical negativa aplicada al hacer slam. Mayor = más rápido al suelo.")]
    public float slamForce = 35f;
    public AudioClip slamSound;
    public AudioClip slamLandSound;

    [Header("Wallrun / Walljump")]
    [Tooltip("Distancia del raycast lateral para detectar paredes")]
    public float wallDetectDistance = 0.7f;
    [Tooltip("Duracion maxima del wallrun en segundos")]
    public float wallrunDuration = 1.5f;
    [Tooltip("Aceleración gravitatoria aplicada durante el wallrun (m/s²).\n" +
             "El jugador empieza recto y va ganando velocidad hacia abajo progresivamente.")]
    public float wallrunGravity = 6f;
    [Tooltip("Velocidad máxima de caída durante el wallrun (m/s).\n" +
             "Limita lo rápido que puede bajar para que no sea demasiado brusco.")]
    public float wallrunMaxFallSpeed = 5f;
    [Tooltip("Altura máxima del walljump en metros (mismo cálculo que jumpHeight).\n" +
             "La gravedad y el impulso se derivan automáticamente del BPM.")]
    public float walljumpHeight = 2.5f;
    [Tooltip("Fuerza horizontal (alejandose de la pared) del walljump")]
    public float walljumpAwayForce = 6f;
    [Tooltip("Duracion del impulso horizontal del walljump antes de que el control normal lo absorba")]
    public float walljumpBoostDuration = 0.25f;
    [Tooltip("Angulo de roll de la camara durante el wallrun (grados)")]
    public float wallCameraRoll = 15f;
    [Tooltip("Velocidad de lerp del roll de camara durante el wallrun")]
    public float wallCameraRollSpeed = 8f;
    [Tooltip("Layer mask de superficies validas para wallrun/walljump.\n" +
             "Se inicializa al layer 'Jumpable' en Start. Cambia aqui para incluir capas adicionales.")]
    public LayerMask wallLayerMask;

    [Header("Escaleras")]
    [Tooltip("Layer de las escaleras. Asigna el layer 'Stairs' aquí.")]
    public LayerMask stairsLayerMask;
    [Tooltip("Altura máxima de escalón que el jugador sube automáticamente (m).")]
    [Range(0.05f, 1f)]
    public float maxStepHeight = 0.8f;
    [Tooltip("Distancia hacia delante del raycast para detectar el siguiente escalón.")]
    [Range(0.1f, 0.8f)]
    public float stepCheckDistance = 0.4f;

    [Header("Corrección de posición")]
    [Tooltip("Fuerza con la que se ajusta la velocidad cuando el jugador lleva desfase " +
             "respecto al ritmo esperado (walkSpeed × tiempo).\n" +
             "0 = sin corrección. 1.5 = suave. 5 = agresiva.")]
    [Range(0f, 5f)]
    public float positionCorrectionStrength = 1.5f;

    [Tooltip("Corrección máxima como fracción de walkSpeed.\n" +
             "Ej.: 0.15 → velocidad puede variar ±15 % respecto a walkSpeed.")]
    [Range(0f, 0.4f)]
    public float maxPositionCorrection = 0.15f;

    [Header("Tirolina")]
    [Tooltip("Gravedad del péndulo de la tirolina (m/s²). Independiente de la gravedad de salto.\n" +
             "La gravedad de salto es muy alta (≈54 m/s² a 180BPM) y aplasta el balanceo lateral.\n" +
             "Usar aquí un valor físico estándar (9.81) deja que ziplineGravityMult lo ajuste.")]
    public float ziplinePendulumGravity = 9.81f;
    [Tooltip("Longitud de la cuerda entre el punto de enganche y el jugador (metros).")]
    public float ziplineArmLength      = 2f;
    [Tooltip("Angulo maximo de balanceo (grados). El jugador nunca supera este limite.")]
    public float ziplineMaxAngle       = 55f;
    [Tooltip("Control directo A/D: grados por segundo que se suman directamente al angulo. " +
             "Mas alto = esquivar obstaculos mas rapido y con mas precision.")]
    public float ziplineDirectControl  = 80f;
    [Tooltip("Aceleracion fisica del balanceo al usar A/D (grados/s^2). Da inercia al movimiento.")]
    public float ziplineSwingAccel     = 60f;
    [Tooltip("Multiplicador de gravedad sobre el pendulo. Mas alto = vuelve al centro mas rapido.")]
    public float ziplineGravityMult    = 3f;
    [Tooltip("Amortiguacion del pendulo (grados/s que se pierden por segundo). 0 = sin friccion.")]
    public float ziplineDamping        = 2.5f;
    [Tooltip("Duracion de la transicion suave al engancharse (segundos). 0 = instantaneo.")]
    public float ziplineAttachTime = 0.35f;
    [Tooltip("Sonido en bucle mientras el jugador se desliza por la tirolina.")]
    public AudioClip   ziplineRideSound;
    [Tooltip("AudioSource dedicado para el sonido de deslizamiento (activa Loop en el componente).")]
    public AudioSource ziplineRideSource;

    [Header("Beat gating")]
    public BeatManager beatManager;
    public bool dashRequiresBeat = false;
    public bool jumpRequiresBeat = false;


    [Header("Head Bob (camara al ritmo de la musica)")]
    [Tooltip("Escala todos los efectos del bob proporcionalmente. 0 = sin bob, 1 = base.")]
    [Range(0f, 3f)]
    public float headBobIntensity  = 1f;
    [Tooltip("Frecuencia natural del muelle de rebote (Hz). Cuanto mayor, mas rapido vuelve al centro. Prueba 4-8.")]
    public float headBobSpringHz   = 5f;
    [Tooltip("Amplitud de pitch (cabeceo arriba/abajo) en GRADOS por beat. 2-3 sutil, 5-8 muy visible.")]
    public float headBobAmplitudeY = 3f;
    [Tooltip("Amplitud de roll (balanceo lateral) en GRADOS por beat. Suele ser la mitad del pitch.")]
    public float headBobAmplitudeX = 1.5f;
    [Tooltip("Amortiguacion del muelle (0 = rebota para siempre, 1 = sin rebote). Prueba 0.6-0.8.")]
    [Range(0f, 1f)]
    public float headBobDamping    = 0.65f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip dashSound;
    public AudioClip dashBlockedSound;
    public AudioClip jumpSound;
    public AudioClip jumpBlockedSound;
    public AudioClip slideSound;
    public AudioClip slideBlockedSound;
    public AudioClip wallrunSound;
    public AudioClip walljumpSound;
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [Tooltip("Retraso en segundos aplicado a todos los SFX. " +
             "Súbelo si el sonido suena ANTES de la acción visual. " +
             "Si suena DESPUÉS, el problema es la latencia del driver de audio " +
             "(reduce el DSP Buffer en Project Settings → Audio).")]
    [Range(0f, 0.25f)] public float sfxOffset = 0f;

    [Header("Eventos")]
    public UnityEvent<Vector3> OnDashStarted;
    public UnityEvent OnDashBlocked;
    public UnityEvent OnJumpExecuted;
    public UnityEvent OnJumpBlocked;
    public UnityEvent OnSlideStarted;
    public UnityEvent OnSlideEnded;
    public UnityEvent OnSlideBlocked;
    public UnityEvent OnWallrunStarted;
    public UnityEvent OnWallrunEnded;
    public UnityEvent OnWalljumped;
    public UnityEvent OnSlamLand;
    public UnityEvent OnSlamStarted;
    public UnityEvent OnZiplineAttached;

    // --- Estado privado ---
    private Vector3 _moveDirection    = Vector3.zero;
    private bool    _isOnStairs       = false;   // true mientras sube/baja escaleras (para el viewmodel)
    private float   _rotationX        = 0f;
    private float   _rotationY        = 0f;
    private float   _initialYRotation = 0f;
    private float   _slidePitchOffset = 0f;
    private float   _wallRollOffset   = 0f;
    private CharacterController _cc;
    private bool _canMove      = false;  // permanece false hasta que StartMusic() lo active
    private bool _freeroamMode = false;  // movimiento libre (sin auto-forward ni beat-gating)
    private bool _isOnKayak   = false;  // jugador montado en el ataúd/kayak
    // Impulso de salida del kayak que se aplica justo antes del _cc.Move del frame siguiente,
    // DESPUÉS de que el cálculo de controlVel sobreescribiría _moveDirection.x/z.
    // Se consume en un solo frame (one-shot).
    private Vector3 _pendingKayakImpulse    = Vector3.zero;
    private bool    _hasPendingKayakImpulse = false;
    private bool    _isKayakPlayerJump      = false;   // true = salto voluntario (vs. eyección forzada del endpoint)

    // Colliders del kayak a ignorar. Unity RESETEA Physics.IgnoreCollision al
    // desactivar/reactivar un collider (el CC se apaga al montar y se reenciende al
    // saltar), así que hay que re-aplicar el ignore cada vez que el CC se reactiva.
    // Sin esto, el CC reactivado colisiona con el ataúd, lo hunde y el muelle de
    // flotación lo dispara al cielo.
    private Collider[] _kayakIgnoreColliders;
    // Corrección de sincronía con la música
    private BeatGridVisualizer _beatGrid;  // lee Origin, Forward y walkSpeed de nivel
    /// <summary>
    /// Corrección de velocidad calculada cada frame (error de posición respecto a la rejilla de beats).
    /// Positivo = jugador atrasado (acelerar). Negativo = adelantado (frenar). Cero = en sincronía.
    /// Expuesto para que KayakController y la tirolina también lo apliquen.
    /// </summary>
    public float SyncSpeedCorrection { get; private set; }

    /// <summary>
    /// Desfase de posición respecto a donde debería estar el jugador según la música, en METROS
    /// (>0 = adelantado, &lt;0 = atrasado). SIN capar. El kayak lo usa para su propia corrección
    /// (más fuerte que la de a pie).
    /// </summary>
    public float SyncDesfase { get; private set; }

    /// <summary>True mientras el jugador está montado en el KayakController.</summary>
    public bool IsOnKayak => _isOnKayak;

    /// <summary>¿Está en movimiento libre (área de antes de empezar el nivel)?</summary>
    public bool IsFreeroam => _freeroamMode;

    /// <summary>
    /// Dirección horizontal de AVANCE del auto-runner, INDEPENDIENTE de hacia dónde mire el
    /// jugador (el cuerpo gira al mirar, pero el avance sigue siempre el eje de la pista).
    /// La usa ObstacleCollision para clasificar la cara golpeada respecto al movimiento real,
    /// no respecto a la mirada.
    /// </summary>
    public Vector3 RunForward => Quaternion.Euler(0f, _initialYRotation, 0f) * Vector3.forward;

    /// <summary>True mientras el jugador sube/baja escaleras. Lo usa el viewmodel para no rebotar la mano.</summary>
    public bool IsOnStairs => _isOnStairs;

    // Altura suave
    private float _targetHeight;
    private float _defaultCameraLocalY;
    private float _defaultSlopeLimit;

    // Dash
    private bool    _isDashing;
    private float   _dashTimeRemaining;
    private Vector3 _dashDirection;

    // Slide
    private bool    _isSliding;
    private float   _slideTimeRemaining;
    private float   _slideLateralInput;   // input lateral (A/D) en el momento de iniciar el slide

    // Wallrun / Walljump
    private bool    _isWallrunning;
    private float   _wallrunTimeRemaining;
    private Vector3 _wallNormal;       // normal de la pared activa (persiste tras EndWallrun para coyote)
    private bool    _onLeftWall;
    private bool    _walljumpBoostActive;
    private float   _walljumpBoostRemaining;
    private Vector3 _walljumpBoostVelocity;

    // Salto
    private int _jumpsUsed;

    // -------------------------------------------------------------------
    // FIX 1 — Buffer de input de salto
    //   WasPressedThisFrame() solo es true UN frame. Si ese frame cae en una
    //   rama que no tiene walljump (p.ej. wallStillThere acaba de ser false),
    //   el input se pierde. El buffer lo conserva 0.12 s para que la siguiente
    //   rama que lo necesite pueda consumirlo.
    // FIX 2 — Deteccion de pared con _wallNormal en lugar de transform.right
    //   transform.right cambia al mover la camara horizontalmente, haciendo que
    //   el raycast deje de apuntar a la pared. Usando -_wallNormal la deteccion
    //   es independiente de la orientacion de la camara.
    // FIX 3 — Coyote time para walljump
    //   Permite hacer walljump hasta 0.12 s despues de desprenderse de la pared.
    // -------------------------------------------------------------------
    private float _jumpBufferTimer      = 0f;
    private float _walljumpCoyoteTimer  = 0f;
    private float _stairCoyoteTimer     = 0f;
    // Sin «jump buffering»: la pulsación solo vale en el frame en que se hace (el valor mínimo es para que la rama
    // que la usa la vea ese mismo frame). Sin «coyote time» en el salto normal (rompía el aterrizaje en el beat),
    // pero SÍ en el salto de pared: 0,12 s tras soltarse de la pared para encadenar el movimiento.
    private const float JumpBufferDuration      = 0.0001f;
    private const float WalljumpCoyoteDuration  = 0.12f;
    private const float StairCoyoteDuration     = 0.25f;   // mantiene "en escaleras" entre escalones

    // Slam
    private bool _isSlamming;

    // Zipline
    private bool    _isOnZipline;
    private Zipline _activeZipline;
    private float   _ziplineT;
    private float   _ziplineAttachProgress; // 0 = inicio transicion, 1 = posicion final
    private Vector3 _ziplineAttachStartPos; // posicion del jugador al engancharse
    // Head bob (spring sincronizado al beat)
    private float   _bobPitch;     // pitch offset actual (grados)
    private float   _bobPitchVel;  // velocidad del pitch spring (grados/s)
    private float   _bobRoll;      // roll offset actual (grados)
    private float   _bobRollVel;   // velocidad del roll spring (grados/s)
    private int     _bobBeatCount; // alterna el roll izq/der en cada beat
    private Vector3 _bobOffset;    // .y = pitch, .x = roll (grados) → HandleLook

    private float   _pendulumAngle;         // grados; 0 = cuelga recto abajo, + = hacia la derecha de la cuerda
    private float   _pendulumVelDeg;        // velocidad angular del pendulo (grados/s)
    private bool    _pendulumInputWasHeld;  // true si el frame anterior habia input A/D

    // Input actions
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _slideAction;
    private InputAction _dashAction;
    private InputAction _ziplineAction;
    private InputAction _slamAction;

    // =========================================================================
    // INICIALIZACION
    // =========================================================================

    void Start()
    {
        _cc                = GetComponent<CharacterController>();
        if (GetComponent<ObstacleUnstuck>() == null) gameObject.AddComponent<ObstacleUnstuck>();   // (anti-atasco en techos bajos)
        _targetHeight      = defaultHeight;
        _defaultSlopeLimit = _cc.slopeLimit;
        _beatGrid          = FindAnyObjectByType<BeatGridVisualizer>();
        _initialYRotation  = transform.eulerAngles.y;
        _rotationY         = 0f;
        if (playerCamera != null)
            _defaultCameraLocalY = playerCamera.transform.localPosition.y;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // Wallrun/walljump solo en paredes con el layer "Jumpable"
        wallLayerMask = LayerMask.GetMask("Jumpable");

        _moveAction = new InputAction("Move");
        _moveAction.AddCompositeBinding("Dpad")
            .With("Up",    "<Keyboard>/w")
            .With("Down",  "<Keyboard>/s")
            .With("Left",  "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        _lookAction  = new InputAction("Look",  binding: "<Mouse>/delta");
        _jumpAction  = new InputAction("Jump",  binding: "<Keyboard>/space");
        _slideAction   = new InputAction("Slide",   binding: "<Keyboard>/leftShift");
        _dashAction    = new InputAction("Dash",    binding: "<Keyboard>/e");
        _ziplineAction = new InputAction("Zipline", binding: "<Keyboard>/f");
        _slamAction    = new InputAction("Slam",    binding: "<Keyboard>/s");

        ApplyKeybinds();
        Keybinds.Changed += ApplyKeybinds;   // (Ajustes → Controles)

        _moveAction.Enable();
        _lookAction.Enable();
        _jumpAction.Enable();
        _slideAction.Enable();
        _dashAction.Enable();
        _ziplineAction.Enable();
        _slamAction.Enable();

        // Suscribir el head bob al half-beat (misma frecuencia que los footsteps)
        if (beatManager != null)
            beatManager.OnHalfBeat.AddListener(OnBobBeat);

    }


    /// <summary>Teclas elegidas en Ajustes → Controles.</summary>
    private void ApplyKeybinds()
    {
        Keybinds.Apply(_moveAction, Keybinds.Bind.Forward, 1);
        Keybinds.Apply(_moveAction, Keybinds.Bind.Back,    2);
        Keybinds.Apply(_moveAction, Keybinds.Bind.Left,    3);
        Keybinds.Apply(_moveAction, Keybinds.Bind.Right,   4);
        Keybinds.Apply(_jumpAction,    Keybinds.Bind.Jump);
        Keybinds.Apply(_slideAction,   Keybinds.Bind.Slide);
        Keybinds.Apply(_dashAction,    Keybinds.Bind.Dash);
        Keybinds.Apply(_ziplineAction, Keybinds.Bind.Zipline);
        Keybinds.Apply(_slamAction,    Keybinds.Bind.Slam);
    }

    void OnDestroy()
    {
        Keybinds.Changed -= ApplyKeybinds;
        // (Pueden ser null si el objeto se destruye antes de inicializarse, p. ej. al cambiar de escena.)
        _moveAction?.Disable();
        _lookAction?.Disable();
        _jumpAction?.Disable();
        _slideAction?.Disable();
        _dashAction?.Disable();
        _ziplineAction?.Disable();
        _slamAction?.Disable();

        if (beatManager != null)
            beatManager.OnHalfBeat.RemoveListener(OnBobBeat);
    }

    /// <summary>True mientras el jugador esta montado en una tirolina.</summary>
    public bool IsOnZipline => _isOnZipline;

    /// <summary>True mientras el slide está activo (invulnerable a obstáculos).</summary>
    public bool IsSliding => _isSliding;

    // ── Rebobinado (tienda) ────────────────────────────────────────────────────
    public bool  IsDashing     => _isDashing;
    public bool  IsWallrunning => _isWallrunning;
    public bool  IsGroundedNow => _cc != null && _cc.enabled && _cc.isGrounded;
    public float LookYaw       => _rotationY;
    public float LookPitch     => _rotationX;

    /// <summary>Coloca al jugador y su mirada (lo usa el Rebobinado mientras vuelve atrás en el tiempo).</summary>
    public void ApplyRewindPose(Vector3 pos, float yaw, float pitch)
    {
        bool ccOn = _cc != null && _cc.enabled;
        if (ccOn) _cc.enabled = false;
        transform.position = pos;
        if (ccOn) _cc.enabled = true;
        _rotationY = yaw;
        _rotationX = pitch;
        transform.rotation = Quaternion.Euler(0f, _initialYRotation + _rotationY, 0f);
        if (playerCamera != null) playerCamera.transform.localRotation = Quaternion.Euler(_rotationX, 0f, 0f);
    }

    /// <summary>Suelta la tirolina al instante (lo usa el Rebobinado al volver a un momento en que ibas a pie).</summary>
    public void ForceExitZipline()
    {
        if (_isOnZipline) DetachFromZipline();
    }

    /// <summary>Tras rebobinar: sin ninguna acción en curso (ni salto, ni dash, ni slide, ni wallrun, ni slam).</summary>
    public void ResetMotionAfterRewind()
    {
        if (_isOnZipline) DetachFromZipline();
        if (_isWallrunning) EndWallrun();
        if (_isSliding)     EndSlide();
        _isDashing        = false;
        _isSlamming       = false;
        _moveDirection    = Vector3.zero;
        _jumpsUsed        = 0;
        _jumpBufferTimer  = 0f;
        _slidePitchOffset = 0f;
        _wallRollOffset   = 0f;
    }

    /// <summary>True mientras el jugador está haciendo Slam (caída con S). Lo usa la cabeza del jefe (Fase 2).</summary>
    public bool IsSlamming => _isSlamming;

    /// <summary>Lanza al jugador hacia arriba (rebote). Lo usa la cabeza del jefe cuando la pisas con el Slam.</summary>
    public void Bounce(float upSpeed)
    {
        _isSlamming      = false;
        _moveDirection.y = upSpeed;
        _jumpsUsed       = Mathf.Min(_jumpsUsed, 1);   // deja un salto aéreo tras el rebote
    }

    /// <summary>
    /// Teletransporta al jugador a una posición y rotación, actualizando el estado
    /// interno de orientación. Úsalo antes de que empiece la secuencia de inicio.
    /// </summary>
    public void TeleportToStartPoint(Vector3 position, Quaternion rotation)
    {
        // Salir del freeroam: el jugador queda inmóvil hasta que arranque el beat
        _freeroamMode = false;
        _canMove      = false;

        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = position;
        transform.rotation = rotation;

        // Sincronizar estado interno con la nueva orientación
        _initialYRotation = rotation.eulerAngles.y;
        _rotationY        = 0f;
        _rotationX        = 0f;
        if (playerCamera != null)
            playerCamera.transform.localRotation = Quaternion.identity;

        if (cc != null) cc.enabled = true;
    }

    /// <summary>
    /// Mueve y rota al jugador suavemente desde su posición actual hasta
    /// <paramref name="targetPos"/>, mirando al eje Z+, durante <paramref name="duration"/> segundos.
    /// Úsalo en la introducción del nivel (el jugador camina cinématicamente al punto de inicio).
    /// </summary>
    public void StartSmoothMoveToStart(Vector3 targetPos, float duration)
    {
        _freeroamMode     = false;
        _canMove          = false;
        _initialYRotation = 0f;   // destino siempre mira al eje Z+
        _rotationY        = 0f;
        _rotationX        = 0f;
        _moveDirection    = Vector3.zero;
        IsMovingToStart   = true;
        StartCoroutine(SmoothMoveCoroutine(targetPos, duration));
    }

    /// <summary>True mientras el jugador camina solo hasta el Starting Point (tras abrir la puerta / la pared).</summary>
    public bool IsMovingToStart { get; private set; }

    private System.Collections.IEnumerator SmoothMoveCoroutine(Vector3 targetPos, float duration)
    {
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Vector3    fromPos    = transform.position;
        Quaternion fromBodyRot = transform.rotation;
        Quaternion toBodyRot   = Quaternion.LookRotation(Vector3.forward, Vector3.up);

        Quaternion fromCamRot = playerCamera != null
            ? playerCamera.transform.localRotation
            : Quaternion.identity;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            transform.position = Vector3.Lerp(fromPos, targetPos, t);
            transform.rotation = Quaternion.Slerp(fromBodyRot, toBodyRot, t);
            if (playerCamera != null)
                playerCamera.transform.localRotation =
                    Quaternion.Slerp(fromCamRot, Quaternion.identity, t);

            yield return null;
        }

        // Asegurar posición y rotación exactas al final
        transform.position = targetPos;
        transform.rotation = toBodyRot;
        if (playerCamera != null)
            playerCamera.transform.localRotation = Quaternion.identity;

        if (cc != null) cc.enabled = true;
        IsMovingToStart = false;
    }

    /// <summary>
    /// Sale del freeroam y bloquea el movimiento hasta que arranque el beat.
    /// </summary>
    public void DisableFreeroam()
    {
        _freeroamMode = false;
        _canMove      = false;
    }

    /// <summary>
    /// Activa el movimiento libre (freeroam) sin esperar al beat ni al AudioDSP.
    /// El jugador puede moverse y mirar en todas las direcciones.
    /// </summary>
    public void EnableFreeroam()
    {
        _freeroamMode = true;
        _canMove      = true;
    }

    // ── Kayak ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Llama esto desde KayakController cuando el jugador salta al ataúd.
    /// Desactiva todo el movimiento propio del jugador; el KayakController
    /// se encarga de posicionarlo cada frame.
    /// </summary>
    public void EnterKayak()
    {
        if (_isOnKayak) return;
        _isOnKayak = true;

        // Cancelar estados activos antes de ceder el control
        if (_isWallrunning) EndWallrun();
        if (_isSliding)     EndSlide();
        _isDashing     = false;
        // IMPORTANTE: resetear el slam. Sólo se limpia al tocar suelo (isGrounded), pero
        // al reembarcar desactivamos el CC antes de eso; si quedara activo, al saltar de
        // nuevo no se aplicaría gravedad (se suprime durante el slam) → subida infinita.
        _isSlamming    = false;
        _moveDirection = Vector3.zero;
        _cc.enabled    = false;

        // Limpiar el muelle del head-bob al montar. Así no queda energía residual que, tras
        // el trayecto (donde el bob no se integra), se suelte de golpe al saltar y sacuda la cámara.
        _bobPitch = _bobRoll = _bobPitchVel = _bobRollVel = 0f;
        _bobOffset = Vector3.zero;
    }

    /// <summary>Restaura el movimiento normal al salir del kayak.</summary>
    public void ExitKayak()
    {
        if (!_isOnKayak) return;
        _isOnKayak  = false;
        _cc.enabled = true;
        ApplyKayakIgnores();
    }

    /// <summary>
    /// El KayakController registra aquí sus colliders para que el CC del jugador los
    /// ignore de forma persistente (re-aplicándolos cada vez que el CC se reactiva).
    /// </summary>
    public void RegisterKayakColliders(Collider[] cols)
    {
        _kayakIgnoreColliders = cols;
        ApplyKayakIgnores();
    }

    /// <summary>
    /// Re-aplica Physics.IgnoreCollision entre el CC y los colliders del kayak.
    /// Debe llamarse tras CADA reactivación del CC (Unity resetea el ignore al
    /// desactivar/reactivar el collider).
    /// </summary>
    private void ApplyKayakIgnores()
    {
        if (_kayakIgnoreColliders == null || _cc == null || !_cc.enabled) return;
        foreach (Collider c in _kayakIgnoreColliders)
            if (c != null) Physics.IgnoreCollision(_cc, c, true);
    }

    /// <summary>
    /// Sale del kayak con un salto natural.
    /// <paramref name="jumpVelocity"/>: impulso vertical (m/s).
    /// <paramref name="horizontalImpulse"/>: velocidad horizontal inicial (misma dirección que el kayak)
    /// para que el jugador salga hacia adelante y no caiga de vuelta encima del ataúd.
    /// </summary>
    public void ExitKayakWithJump(float jumpVelocity, Vector3 horizontalImpulse)
    {
        if (!_isOnKayak) return;
        _isOnKayak = false;
        // CC se re-habilita DENTRO del bloque de impulso pendiente (en el bloque normal de
        // movimiento), NO aquí. Si lo habilitamos aquí con PM corriendo antes que KC en el
        // mismo frame, el CC se activa pero ya pasó el _cc.Move de ese frame → el impulso
        // dispara con CC activo pero el primer frame lo absorbe la física del CC.

        // El cuerpo lo orienta HandleLook durante todo el trayecto (KayakController solo fija la
        // POSICIÓN del asiento), así que _rotationY ya representa la vista actual del jugador:
        // no hay que resincronizar nada y la cámara conserva su orientación al salir → sin snap.

        _pendingKayakImpulse    = new Vector3(horizontalImpulse.x, jumpVelocity, horizontalImpulse.z);
        _hasPendingKayakImpulse = true;
    }

    /// <summary>
    /// El jugador salta voluntariamente del kayak pulsando Espacio.
    /// Usa el mismo JumpPower que el salto normal del jugador (calculado con BPM).
    /// El CC se re-habilita dentro del bloque de impulso pendiente — misma
    /// garantía atómica que ExitKayakWithJump.
    /// </summary>
    public void ExitKayakWithPlayerJump(Vector3 horizontalImpulse)
    {
        if (!_isOnKayak) return;
        _isOnKayak = false;

        // El cuerpo lo orienta HandleLook durante todo el trayecto (KayakController solo fija la
        // POSICIÓN del asiento), así que _rotationY ya representa la vista actual del jugador:
        // no hay que resincronizar nada y la cámara conserva su orientación al salir → sin snap.

        _pendingKayakImpulse    = new Vector3(horizontalImpulse.x, JumpPower, horizontalImpulse.z);
        _hasPendingKayakImpulse = true;
        _isKayakPlayerJump      = true;
    }

    public void ScheduleStart(double dspTime)
    {
        // Salir del freeroam: resetear orientación y esperar al beat
        _freeroamMode = false;
        _canMove      = false;
        _rotationY    = 0f;
        _rotationX    = 0f;
        transform.rotation = Quaternion.Euler(0f, _initialYRotation, 0f);
        if (playerCamera != null) playerCamera.transform.localRotation = Quaternion.identity;
        StartCoroutine(WaitForDSPStart(dspTime));
    }

    private System.Collections.IEnumerator WaitForDSPStart(double dspTime)
    {
        // Guard: si dspTime es inválido (p.ej. BeatManager.StartMusic() falló y
        // ScheduledStartDSP quedó en -1), AudioSettings.dspTime < -1 es siempre
        // falso → _canMove se activaría en el primer frame sin esperar.
        if (dspTime <= 0)
        {
            Debug.LogError("[PlayerMovement] WaitForDSPStart: dspTime inválido (" + dspTime +
                           "). ¿Está asignado el AudioSource en BeatManager? El jugador no arrancará.");
            yield break;
        }

        // Espera hasta que el reloj de audio llegue al instante programado
        while (AudioSettings.dspTime < dspTime)
            yield return null;

        // Seguridad: asegurar que el CharacterController está activo al arrancar
        var ccCheck = GetComponent<CharacterController>();
        if (ccCheck != null && !ccCheck.enabled) ccCheck.enabled = true;
        ApplyKayakIgnores();

        _canMove = true;
    }

    // =========================================================================
    // UPDATE PRINCIPAL
    // =========================================================================

    void Update()
    {
        UpdateSyncCorrection(); // siempre primero: actualiza SyncSpeedCorrection para todos los sistemas
        if (_isOnKayak)
        {
            HandleLook();   // el jugador puede seguir mirando libremente mientras va en el kayak
            return;
        }
        // En freeroam: forward sigue la dirección actual del jugador.
        // En juego: forward es siempre la dirección inicial (auto-runner).
        float lookYaw = _freeroamMode ? _initialYRotation + _rotationY : _initialYRotation;
        Quaternion runRot  = Quaternion.Euler(0f, lookYaw, 0f);
        Vector3 forward    = runRot * Vector3.forward;
        Vector3 right      = runRot * Vector3.right;

        Vector2 moveInput  = _moveAction.ReadValue<Vector2>();
        if (!_freeroamMode) moveInput.y = 1f;

        // =================================================================
        // TIROLINA — enganche / desenganche con F
        // =================================================================
        if (!_freeroamMode && _ziplineAction.WasPressedThisFrame() && TutorialGate.Allows(TutorialKey.Zipline))
        {
            if (_isOnZipline) DetachFromZipline();
            else TryAttachToZipline();
        }

        if (_isOnZipline)
        {
            // Actualizar el buffer de salto para poder saltar y soltarse
            if (_jumpAction.WasPressedThisFrame() && TutorialGate.Allows(TutorialKey.Jump))
                _jumpBufferTimer = JumpBufferDuration;
            else if (_jumpBufferTimer > 0f)
                _jumpBufferTimer -= Time.deltaTime;

            HandleZiplineMovement(moveInput);
            HandleLook();
            return;
        }

        // =================================================================
        // BUFFER DE SALTO (FIX 1)
        // Registra la pulsacion y la mantiene viva JumpBufferDuration segundos.
        // Solo se activa si el juego ya ha comenzado (_canMove) para evitar
        // que la pulsacion de inicio también ejecute un salto.
        // =================================================================
        // (También en freeroam/lobby: ahí se puede saltar libremente, sin coste ni ritmo.)
        if (_canMove && _jumpAction.WasPressedThisFrame() && TutorialGate.Allows(TutorialKey.Jump))
            _jumpBufferTimer = JumpBufferDuration;
        else if (_jumpBufferTimer > 0f)
            _jumpBufferTimer -= Time.deltaTime;

        // Coyote time de walljump (FIX 3)
        if (_walljumpCoyoteTimer > 0f)
            _walljumpCoyoteTimer -= Time.deltaTime;

        // =================================================================
        // DASH
        // =================================================================
        if (_dashAction.WasPressedThisFrame() && _canMove && !_freeroamMode && !_isDashing && TutorialGate.Allows(TutorialKey.Dash))
        {
            if (dashRequiresBeat && !IsOnBeat())
            {
                PlaySfx(dashBlockedSound);
                OnDashBlocked?.Invoke();
            }
            else
            {
                float lateralInput = moveInput.x;
                bool hasLateral = Mathf.Abs(lateralInput) > 0.1f;

                if (!hasLateral)
                {
                    PlaySfx(dashBlockedSound);
                    OnDashBlocked?.Invoke();
                }
                else
                {
                    Vector3 dir = right * Mathf.Sign(lateralInput);

                    bool canDash = EnergyManager.Instance == null
                        || EnergyManager.Instance.TrySpend(dashEnergyCost);

                    if (canDash)
                    {
                        _isDashing             = true;
                        _dashTimeRemaining     = dashDuration;
                        _dashDirection         = dir;
                        if (_isWallrunning) EndWallrun();
                        PlaySfx(dashSound);
                        OnDashStarted?.Invoke(dir);
                    }
                    else
                    {
                        PlaySfx(dashBlockedSound);
                        OnDashBlocked?.Invoke();
                    }
                }
            }
        }

        // =================================================================
        // SLIDE
        // =================================================================
        if (_slideAction.WasPressedThisFrame() && TutorialGate.Allows(TutorialKey.Slide)
            && _canMove && !_freeroamMode && !_isDashing && !_isSliding
            && _cc.isGrounded
            && moveInput.sqrMagnitude > 0.01f)
        {
            if (slideRequiresBeat && !IsOnBeat())
            {
                PlaySfx(slideBlockedSound);
                OnSlideBlocked?.Invoke();
            }
            else
            {
                bool canSlide = EnergyManager.Instance == null
                    || EnergyManager.Instance.TrySpend(slideEnergyCost);

                if (canSlide)
                {
                    _isSliding              = true;
                    _slideTimeRemaining     = slideDuration;
                    _slideLateralInput = moveInput.x;  // guardar lateral sin normalizar
                    SetControllerHeight(defaultHeight * 0.5f);
                    PlaySfx(slideSound);
                    OnSlideStarted?.Invoke();
                }
                else
                {
                    PlaySfx(slideBlockedSound);
                    OnSlideBlocked?.Invoke();
                }
            }
        }

        // =================================================================
        // DETECCION DE PAREDES (para inicio de wallrun)
        // Sigue usando transform.right aqui porque solo se usa para EMPEZAR
        // el wallrun — una vez activo, la deteccion usa _wallNormal (FIX 2).
        // =================================================================
        bool wallLeft  = false;
        bool wallRight = false;
        RaycastHit wallHitLeft  = default;
        RaycastHit wallHitRight = default;

        if (!_cc.isGrounded && !_isSliding && !_isDashing)
        {
            Vector3 origin = transform.position + Vector3.up * (_cc.height * 0.5f);

            wallLeft  = Physics.Raycast(origin, -transform.right, out wallHitLeft,
                            wallDetectDistance, wallLayerMask)
                        && Mathf.Abs(wallHitLeft.normal.y)  < 0.3f;

            wallRight = Physics.Raycast(origin,  transform.right, out wallHitRight,
                            wallDetectDistance, wallLayerMask)
                        && Mathf.Abs(wallHitRight.normal.y) < 0.3f;
        }

        // =================================================================
        // WALLRUN — inicio
        // =================================================================
        if (!_isWallrunning && !_cc.isGrounded && !_isDashing && !_isSliding
            && !_walljumpBoostActive && !_freeroamMode
            && moveInput.y > 0.1f && (wallLeft || wallRight))
        {
            RaycastHit hit = wallLeft ? wallHitLeft : wallHitRight;
            _wallNormal           = hit.normal;
            _onLeftWall           = wallLeft;
            _isWallrunning        = true;
            _wallrunTimeRemaining = wallrunDuration;
            _moveDirection.y      = 0f;
            if (_jumpsUsed < maxJumps) _jumpsUsed = Mathf.Max(_jumpsUsed, 1);
            PlaySfx(wallrunSound);
            OnWallrunStarted?.Invoke();
        }

        // Velocidad de avance efectiva = walkSpeed ± corrección de sincronía (calculada en UpdateSyncCorrection)
        float effectiveForwardSpeed = walkSpeed + SyncSpeedCorrection;

        // Se re-evalúa cada frame en la rama de movimiento normal; false por defecto en dash/slide/wallrun.
        _isOnStairs = false;

        // =================================================================
        // MOVIMIENTO SEGUN ESTADO
        // =================================================================

        if (_isDashing)
        {
            // --- DASH ---
            // El jugador mantiene su velocidad de avance + impulso lateral del dash.
            // Sin esto, el forward queda a 0 durante los 0.18s del dash.
            float dashY = _moveDirection.y != 0f ? _moveDirection.y : 0f;
            CcMove((_dashDirection * dashSpeed + forward * walkSpeed) * Time.deltaTime
                     + Vector3.up * dashY * Time.deltaTime);
            _dashTimeRemaining -= Time.deltaTime;
            if (_dashTimeRemaining <= 0f)
            {
                _isDashing       = false;
                _moveDirection.y = 0f;
            }
        }
        else if (_isSliding)
        {
            // --- SLIDE ---
            _slideTimeRemaining -= Time.deltaTime;

            // Velocidad hacia delante siempre a walkSpeed completo.
            // El lateral usa el input sin normalizar para no restar velocidad frontal.
            Vector3 slideVel = forward * effectiveForwardSpeed + right * (_slideLateralInput * walkSpeed);
            _moveDirection.x = slideVel.x;
            _moveDirection.z = slideVel.z;

            // Salto cancela el slide — usa el buffer (FIX 1)
            if (_jumpBufferTimer > 0f && _canMove)
            {
                _jumpBufferTimer = 0f;
                EndSlide();
                bool canJump = EnergyManager.Instance == null
                    || EnergyManager.Instance.TrySpend(jumpEnergyCost);
                if (canJump)
                {
                    _moveDirection.y = JumpPower;
                    _jumpsUsed++;
                    PlaySfx(jumpSound);
                    OnJumpExecuted?.Invoke();
                }
            }
            else if (!_cc.isGrounded)
                _moveDirection.y -= Gravity * Time.deltaTime;
            else
                _moveDirection.y = -0.5f;

            if (_slideTimeRemaining <= 0f) EndSlide();

            CcMove(_moveDirection * Time.deltaTime);
        }
        else if (_isWallrunning)
        {
            // --- WALLRUN ---

            // FIX 2: detectar la pared con -_wallNormal en lugar de transform.right.
            // Esto funciona aunque el jugador mire a los lados durante el wallrun.
            Vector3 wallCheckOrigin = transform.position + Vector3.up * (_cc.height * 0.5f);
            RaycastHit wallCheckHit;
            bool wallStillThere = Physics.Raycast(
                                      wallCheckOrigin, -_wallNormal,
                                      out wallCheckHit, wallDetectDistance * 1.2f, wallLayerMask)
                                  && Mathf.Abs(wallCheckHit.normal.y) < 0.3f;

            _wallrunTimeRemaining -= Time.deltaTime;

            if (!wallStillThere || _wallrunTimeRemaining <= 0f || _cc.isGrounded)
            {
                // Perdemos la pared. Si Espacio estaba en el buffer Y no estamos
                // en el suelo, ejecutar walljump antes de salir (FIX 1 + FIX 3).
                if (_jumpBufferTimer > 0f && _canMove && !_cc.isGrounded)
                {
                    _jumpBufferTimer = 0f;
                    PerformWalljump();
                }
                else
                {
                    EndWallrun();
                    _moveDirection.y -= Gravity * Time.deltaTime;
                }
                CcMove(_moveDirection * Time.deltaTime);
            }
            else
            {
                // Wallrun activo — movimiento sobre la superficie
                Vector3 wallForward = Vector3.Cross(_wallNormal, Vector3.up);
                if (Vector3.Dot(wallForward, transform.forward) < 0f)
                    wallForward = -wallForward;

                _moveDirection.x = wallForward.x * walkSpeed;
                _moveDirection.z = wallForward.z * walkSpeed;

                // Gravedad acumulativa: el jugador va bajando cada vez más rápido,
                // como si se deslizara por la pared. Empieza recto y cae progresivamente.
                _moveDirection.y -= wallrunGravity * Time.deltaTime;
                _moveDirection.y  = Mathf.Max(_moveDirection.y, -wallrunMaxFallSpeed);

                // WALLJUMP — usa el buffer en lugar de WasPressedThisFrame (FIX 1)
                if (_jumpBufferTimer > 0f && _canMove)
                {
                    _jumpBufferTimer = 0f;
                    PerformWalljump();
                }

                CcMove(_moveDirection * Time.deltaTime);
            }
        }
        else
        {
            // --- MOVIMIENTO NORMAL (suelo / aire) ---
            float prevY = _moveDirection.y;

            if (_cc.isGrounded)
            {
                _jumpsUsed           = 0;
                _walljumpBoostActive = false;
            }

            // !_hasPendingKayakImpulse: el salto de salida del kayak lo gestiona SOLO el
            // bloque del impulso pendiente. Si no, la pulsación se filtra aquí y, con el CC
            // desactivado (isGrounded=false), se procesaría como salto AÉREO (doble salto).
            bool tryJump   = _jumpBufferTimer > 0f && _canMove && !_hasPendingKayakImpulse;
            bool jumpsLeft = _jumpsUsed < maxJumps
                         || (EnergyManager.Instance != null && EnergyManager.Instance.godMode);
            // Tutorial: el doble salto (saltar en el aire) no se puede usar hasta que se enseña.
            if (!_cc.isGrounded && TutorialGate.IsLocked(TutorialKey.DoubleJump) && _walljumpCoyoteTimer <= 0f) jumpsLeft = false;

            // FIX 3 — Walljump coyote: el jugador acaba de salirse de la pared
            // y todavia puede hacer walljump en la ventana de tiempo.
            if (tryJump && _walljumpCoyoteTimer > 0f && !_cc.isGrounded)
            {
                _jumpBufferTimer     = 0f;
                _walljumpCoyoteTimer = 0f;
                PerformWalljump();
                prevY = _moveDirection.y; // actualizar para que gravity no lo sobreescriba abajo
            }
            else if (tryJump && jumpsLeft)
            {
                _jumpBufferTimer = 0f;

                if (jumpRequiresBeat && !_freeroamMode && !IsOnBeat())
                {
                    PlaySfx(jumpBlockedSound);
                    OnJumpBlocked?.Invoke();
                    _moveDirection.y = prevY;
                }
                else
                {
                    bool isAirJump = !_cc.isGrounded;
                    float cost     = isAirJump ? airJumpEnergyCost : jumpEnergyCost;
                    float power    = isAirJump ? AirJumpPower      : JumpPower;

                    // En freeroam (lobby, entre niveles) saltar es gratis: no hay barra en juego.
                    bool canJump = _freeroamMode || EnergyManager.Instance == null
                        || EnergyManager.Instance.TrySpend(cost);

                    if (canJump)
                    {
                        // Saltar CANCELA el slam: mientras _isSlamming no se aplica gravedad, así que si se
                        // quedaba activo, el salto subía a velocidad constante sin freno (salía disparado).
                        _isSlamming      = false;
                        _moveDirection.y = power;
                        _jumpsUsed++;
                        PlaySfx(jumpSound);
                        OnJumpExecuted?.Invoke();
                    }
                    else
                    {
                        _moveDirection.y = prevY;
                        PlaySfx(jumpBlockedSound);
                        OnJumpBlocked?.Invoke();
                    }
                }
            }
            else
            {
                _moveDirection.y = prevY;
            }

            if (!_cc.isGrounded)
            {
                // Slam: S pulsado por primera vez en el aire
                if (_canMove && !_freeroamMode && _slamAction.WasPressedThisFrame() && !_isSlamming && TutorialGate.Allows(TutorialKey.Slam))
                {
                    _isSlamming      = true;
                    _moveDirection.y = -slamForce;
                    PlaySfx(slamSound);
                    OnSlamStarted?.Invoke();
                }

                if (!_isSlamming)
                {
                    _moveDirection.y -= Gravity * Time.deltaTime;
                }
                // mientras hace slam no se aplica gravedad extra: ya va a -slamForce
            }
            else
            {
                if (_isSlamming)
                {
                    _isSlamming = false;
                    PlaySfx(slamLandSound);
                    OnSlamLand?.Invoke();
                }
            }

            float curSpeedX = _canMove ? effectiveForwardSpeed * moveInput.y : 0f;
            float curSpeedY = _canMove ? walkSpeed              * moveInput.x : 0f;
            Vector3 controlVel = (forward * curSpeedX) + (right * curSpeedY);

            if (_walljumpBoostActive)
            {
                _walljumpBoostRemaining -= Time.deltaTime;
                if (_walljumpBoostRemaining <= 0f)
                {
                    _walljumpBoostActive   = false;
                    _walljumpBoostVelocity = Vector3.zero;
                }
                float   t  = Mathf.Clamp01(_walljumpBoostRemaining / walljumpBoostDuration);
                Vector3 hv = controlVel + _walljumpBoostVelocity * t;

                // Garantizar velocidad minima de alejamiento de la pared.
                // Aunque el jugador empuje hacia la pared, la componente en la
                // direccion de la normal nunca cae por debajo de walljumpAwayForce*t.
                Vector3 awayDir = new Vector3(_wallNormal.x, 0f, _wallNormal.z).normalized;
                float   awayDot = Vector3.Dot(new Vector3(hv.x, 0f, hv.z), awayDir);
                float   minAway = walljumpAwayForce * t;
                if (awayDot < minAway)
                    hv += awayDir * (minAway - awayDot);

                _moveDirection.x = hv.x;
                _moveDirection.z = hv.z;
            }
            else
            {
                _moveDirection.x = controlVel.x;
                _moveDirection.z = controlVel.z;
            }

            // Escaleras: subir escalones + evitar atasco en saltos laterales
            Vector3 hStairDir = new Vector3(_moveDirection.x, 0f, _moveDirection.z);
            // Ray un poco más largo para detectar también los escalones al BAJAR, cuando el jugador
            // va cayendo de un escalón al siguiente y no está tocando el tread.
            bool stairHit = stairsLayerMask != 0 &&
                            Physics.Raycast(transform.position, Vector3.down,
                                            _cc.height * 0.6f + 0.6f, stairsLayerMask);
            // Coyote: mantener "en escaleras" un instante tras el último impacto, para que los huecos
            // entre escalón y escalón (sobre todo bajando) no desactiven el flag y la mano no bote.
            if (stairHit) _stairCoyoteTimer = StairCoyoteDuration;
            else if (_stairCoyoteTimer > 0f) _stairCoyoteTimer -= Time.deltaTime;
            bool onStairs = _stairCoyoteTimer > 0f;
            // slopeLimit alto: evita que el CC quede bloqueado por las caras diagonales
            // de ProBuilder al aterrizar de lado o con A/D presionado
            _cc.slopeLimit = onStairs ? 89f : _defaultSlopeLimit;
            _isOnStairs = onStairs;
            // Pasamos 'forward' (dirección de AVANCE, fija en el auto-runner) para que el
            // raycast de escalones apunte al frente aunque el jugador mire de lado.
            HandleStairClimb(hStairDir, forward);

            // Impulso one-shot de salida del kayak.
            // Se aplica AQUÍ — después de que controlVel sobreescribió x/z — para que
            // forward momentum y salto no queden pisados.
            // El CC se re-habilita EN ESTE MOMENTO (no antes) para que el Move ocurra
            // en el mismo frame que el re-enable → sin frame de limbo donde el CC está
            // activo pero el impulso todavía no se ha aplicado.
            if (_hasPendingKayakImpulse)
            {
                if (!_cc.enabled) { _cc.enabled = true; ApplyKayakIgnores(); }  // re-enable + re-ignorar kayak
                _moveDirection          = _pendingKayakImpulse;
                _hasPendingKayakImpulse = false;
                _pendingKayakImpulse    = Vector3.zero;
                if (_isKayakPlayerJump)
                {
                    // Salto voluntario = salto NORMAL (jump #1). Cuenta 1 salto usado, así
                    // queda disponible el doble salto (maxJumps−1 saltos aéreos). Suena como
                    // salto normal y se limpia el buffer para no encadenar otro salto.
                    // NO se invoca OnJumpExecuted: dispara el efecto de cámara del salto a pie
                    // (dip/FOV) que da un movimiento raro al salir del kayak.
                    _isKayakPlayerJump = false;
                    _jumpsUsed         = 1;
                    _jumpBufferTimer   = 0f;
                    PlaySfx(jumpSound);
                }
            }

            // Solo mover si el CC está activo. En frames de transición (entrar/salir del kayak o
            // la tirolina) puede estar desactivado un instante: entonces la posición la lleva ese
            // sistema y llamar a Move daría "Move called on inactive controller".
            if (_cc.enabled)
                CcMove(_moveDirection * Time.deltaTime);
        }

        // =================================================================
        // ALTURA SUAVE (CharacterController + camara)
        // =================================================================
        _cc.height = Mathf.Lerp(_cc.height, _targetHeight, Time.deltaTime * heightLerpSpeed);
        if (playerCamera != null && defaultHeight > 0f)
        {
            UpdateHeadBob();
            Vector3 camPos = playerCamera.transform.localPosition;
            camPos.y = _defaultCameraLocalY * (_cc.height / defaultHeight);
            playerCamera.transform.localPosition = camPos;
        }

        // =================================================================
        // CAMARA — pitch (slide) y roll (wallrun)
        // =================================================================
        float pitchTarget = _isSliding ? -slideCameraTilt : 0f;
        _slidePitchOffset = Mathf.Lerp(_slidePitchOffset, pitchTarget, Time.deltaTime * slideCameraTiltSpeed);

        float rollTarget = _isWallrunning
            ? (_onLeftWall ? -wallCameraRoll : wallCameraRoll)
            : 0f;
        _wallRollOffset = Mathf.Lerp(_wallRollOffset, rollTarget, Time.deltaTime * wallCameraRollSpeed);

        HandleLook();
    }

    // =========================================================================
    // WALLJUMP — extraido como metodo para reutilizarlo desde varias ramas
    // =========================================================================

    private void PerformWalljump()
    {
        // Usamos _wallNormal que persiste despues de EndWallrun (no se borra)
        Vector3 awayDir        = _wallNormal;
        awayDir.y              = 0f;
        _walljumpBoostVelocity  = awayDir.normalized * walljumpAwayForce;
        _walljumpBoostActive    = true;
        _walljumpBoostRemaining = walljumpBoostDuration;

        // Solo sobreescribir X (impulso lateral alejándose de la pared).
        // La Z (velocidad hacia adelante = walkSpeed) se preserva del wallrun
        // para no perder un frame de avance.
        _moveDirection.x = _walljumpBoostVelocity.x;
        _moveDirection.y = WalljumpPower;
        _isSlamming      = false; // el walljump también cancela el slam (si no, subiría sin gravedad)
        _jumpsUsed       = 1; // queda un salto en el aire tras el walljump

        EndWallrun();              // pone _walljumpCoyoteTimer = WalljumpCoyoteDuration
        _walljumpCoyoteTimer = 0f; // pero lo cancelamos: ya hicimos el walljump

        PlaySfx(walljumpSound != null ? walljumpSound : jumpSound);
        OnWalljumped?.Invoke();
    }

    // =========================================================================
    // LOOK
    // =========================================================================

    // =========================================================================
    // SINCRONÍA CON LA MÚSICA (corrección de posición)
    // =========================================================================

    /// <summary>
    /// Calcula cuántos m/s hay que añadir (o restar) a walkSpeed para que el jugador
    /// vuelva a la posición que le corresponde según el tiempo de la música.
    /// Usa BeatGridVisualizer como fuente
    /// de verdad (Origin, Forward y walkSpeed del nivel).
    ///
    /// Resultado guardado en <see cref="SyncSpeedCorrection"/>; accesible desde
    /// KayakController, HandleZiplineMovement, etc.
    ///
    /// Si no hay BeatGridVisualizer en escena o la música no está sonando, deja la
    /// corrección a 0 y el jugador viaja a walkSpeed exacto.
    /// </summary>
    private void UpdateSyncCorrection()
    {
        SyncSpeedCorrection = 0f;
        SyncDesfase         = 0f;

        if (_beatGrid == null)
        {
            // Intentar encontrarlo si aún no está (puede haberse creado tarde)
            _beatGrid = FindAnyObjectByType<BeatGridVisualizer>();
            if (_beatGrid == null) return;
        }

        if (BeatManager.Instance == null)                        return;
        if (!BeatManager.Instance.HasClock)      return;
        if (!BeatManager.Instance.IsPlaying)                     return;
        if (_freeroamMode)                                        return;
        if (positionCorrectionStrength <= 0f)                    return;

        // Error respecto a la rejilla ─────────────────────────────────
        double now         = AudioSettings.dspTime;
        float  expected    = (float)(BeatManager.Instance.ClockSeconds * _beatGrid.walkSpeed);
        float  actual      = _beatGrid.DistanceAlong(transform.position);
        float  deltaDist   = actual - expected; // > 0 = adelantado, < 0 = atrasado
        SyncDesfase        = deltaDist;

        // Corrección: si atrasado (deltaDist < 0) → añadir velocidad (resultado > 0)
        SyncSpeedCorrection = Mathf.Clamp(
            -deltaDist * positionCorrectionStrength,
            -maxPositionCorrection * walkSpeed,
             maxPositionCorrection * walkSpeed);
    }

    // =========================================================================
    // ESCALERAS
    // =========================================================================

    /// <summary>
    /// Mueve el CharacterController, salvo con el tiempo PARADO (Time.deltaTime = 0, p. ej. el tutorial): un Move de
    /// longitud cero pone isGrounded a false, y entonces no se podía deslizar ni hacer un salto normal en la pausa.
    /// </summary>
    private void CcMove(Vector3 delta)
    {
        if (Time.deltaTime <= 0f) return;
        _cc.Move(delta);
    }

    /// <summary>
    /// Sube escalones automáticamente, compatible con mallas ProBuilder.
    ///
    /// Estrategia: lanza un raycast desde ARRIBA hacia ABAJO ligeramente por delante
    /// del jugador. Así siempre golpea el "tread" (superficie plana) del escalón y
    /// nunca el "riser" (cara vertical), que es lo que atascaba al CC con MeshColliders
    /// de ProBuilder. Si la superficie detectada está más alta que los pies del jugador
    /// (y dentro de maxStepHeight), lo sube gradualmente con otro CC.Move para no
    /// luchar contra la física interna del CharacterController.
    /// </summary>
    private void HandleStairClimb(Vector3 hDir, Vector3 travelForward)
    {
        if (stairsLayerMask == 0)       return;
        if (!_cc.isGrounded)            return;
        if (hDir.sqrMagnitude < 0.001f) return;

        // El centro del CC está a height/2 sobre el suelo
        float feetY = transform.position.y - _cc.height * 0.5f;

        // Dirección de AVANCE (horizontal), independiente de A/D y de la MIRADA.
        // Antes se usaba transform.forward, pero el cuerpo gira con la mirada (HandleLook),
        // así que al mirar de lado el ray apuntaba de lado, no detectaba el riser y el
        // jugador se atascaba. travelForward es la dirección del auto-runner (fija).
        Vector3 facing = Vector3.ProjectOnPlane(travelForward, Vector3.up).normalized;
        if (facing.sqrMagnitude < 0.001f) return;

        // ── RAY 1: horizontal a nivel de pies, en dirección FRONTAL ──────────
        // Detecta si hay un riser justo delante. Sin este filtro el jugador
        // subiría continuamente mientras camina por el tread plano.
        Vector3 footOrigin = new Vector3(transform.position.x, feetY + 0.1f, transform.position.z);
        if (!Physics.Raycast(footOrigin, facing, _cc.radius + 0.2f, stairsLayerMask))
            return;

        // ── RAY 2: vertical (desde arriba hacia abajo), también frontal ───────
        // Encuentra la superficie plana (tread) del escalón que tiene delante.
        float   ahead  = _cc.radius + stepCheckDistance;
        Vector3 origin = new Vector3(transform.position.x,
                                     feetY + maxStepHeight + 0.1f,
                                     transform.position.z)
                       + facing * ahead;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                             maxStepHeight + 0.15f, stairsLayerMask))
            return;

        float diff = hit.point.y - feetY;
        if (diff < 0.02f || diff > maxStepHeight) return;

        // Snap instantáneo al nivel del escalón (como hace el stepOffset nativo).
        // Subir gradualmente deja al jugador a mitad del riser → pierde velocidad.
        _cc.Move(Vector3.up * diff);
    }

    private void HandleLook()
    {
        if (!_canMove) return;

        Vector2 look = _lookAction.ReadValue<Vector2>();
        if (TutorialGate.Busy) look *= Time.timeScale;   // (tutorial a cámara lenta: la cámara también va lenta; parado = quieta)
        float   sens = 0.1f * GameSettings.Sensitivity;   // (ajuste del jugador: menú / pausa)

        if (!_freeroamMode && !GameSettings.CameraLookActive)
        {
            // Ajustes → «Cámara con ratón: NO»: durante el nivel la vista mira siempre al frente (vuelve suave).
            // (En la zona de inicio sí se mueve: hay que mirar la tienda, los contratos y la pared para empezar.)
            float back = 240f * Time.deltaTime;
            _rotationX = Mathf.MoveTowards(_rotationX, 0f, back);
            _rotationY = Mathf.MoveTowards(_rotationY, 0f, back);
            look = Vector2.zero;
        }

        _rotationX += -look.y * lookSpeed * sens;
        _rotationX  = Mathf.Clamp(_rotationX, -lookXLimit, lookXLimit);

        _rotationY += look.x * lookSpeed * sens;
        // En juego el giro lateral está limitado a ±90°; en freeroam rotación libre.
        if (!_freeroamMode) _rotationY = Mathf.Clamp(_rotationY, -90f, 90f);
        transform.rotation = Quaternion.Euler(0f, _initialYRotation + _rotationY, 0f);

        playerCamera.transform.localRotation =
            Quaternion.Euler(_rotationX + _slidePitchOffset + _bobOffset.y, 0f, _wallRollOffset + _bobOffset.x);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void SetControllerHeight(float height)
    {
        _targetHeight = height;
    }

    private void EndSlide()
    {
        if (!_isSliding) return;
        _isSliding = false;
        SetControllerHeight(defaultHeight);
        OnSlideEnded?.Invoke();
    }

    private void EndWallrun()
    {
        if (!_isWallrunning) return;
        _isWallrunning = false;
        // Abre la ventana de coyote — el jugador puede hacer walljump
        // hasta WalljumpCoyoteDuration segundos despues de soltar la pared.
        _walljumpCoyoteTimer = WalljumpCoyoteDuration;
        OnWallrunEnded?.Invoke();
    }

    private bool IsOnBeat()
    {
        if (beatManager == null) return true;
        return beatManager.IsOnBeat();
    }

    private void PlaySfx(AudioClip clip)
    {
        if (audioSource == null || clip == null) return;
        if (sfxOffset > 0.001f)
            StartCoroutine(PlaySfxDelayed(clip));
        else
            audioSource.PlayOneShot(clip, sfxVolume);
    }

    private System.Collections.IEnumerator PlaySfxDelayed(AudioClip clip)
    {
        yield return new WaitForSeconds(sfxOffset);
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip, sfxVolume);
    }

    // =========================================================================
    // HEAD BOB
    // =========================================================================

    /// <summary>
    /// Llamado automaticamente por BeatManager.OnBeat.
    /// Da un impulso (kick) al spring de la camara al ritmo de la musica.
    /// </summary>
    private void OnBobBeat()
    {
        if (headBobIntensity < 0.01f) return;

        // Solo ejecutar si el jugador esta corriendo en el suelo.
        // !_isOnKayak es CLAVE: con el CC desactivado en el kayak, isGrounded conserva su
        // último valor (true) y UpdateHeadBob no corre, así que los kicks se ACUMULARÍAN sin
        // integrarse y al saltar del kayak se soltarían de golpe → cámara pegando un salto.
        bool active = _cc.isGrounded && _canMove
                   && !_isSliding && !_isDashing
                   && !_isWallrunning && !_isOnZipline
                   && !_isOnKayak;
        if (!active) return;

        // Velocidad de kick calculada para alcanzar ~headBobAmplitudeY grados de pico.
        // v0 ≈ amplitude * 2 * omega (aproximacion para muelle subamortiguado tipico)
        float omega    = headBobSpringHz * Mathf.PI * 2f;
        float kickPitch = headBobAmplitudeY * headBobIntensity * omega * 2f;
        float kickRoll  = headBobAmplitudeX * headBobIntensity * omega * 2f;

        _bobPitchVel -= kickPitch;                                          // cabeceo hacia abajo
        _bobRollVel  += kickRoll * (_bobBeatCount % 2 == 0 ? 1f : -1f);   // alterna izq/der
        _bobBeatCount++;
    }

    /// <summary>
    /// Integra el muelle amortiguado cada frame.
    /// El spring decae solo hasta que el siguiente beat lo vuelve a activar.
    /// </summary>
    private void UpdateHeadBob()
    {
        float dt    = Time.deltaTime;
        float omega = headBobSpringHz * Mathf.PI * 2f;
        float damp  = headBobDamping * 2f * omega;    // coeficiente de amortiguacion critica

        // F = -k*x - c*v  (muelle amortiguado)
        _bobPitchVel += (-omega * omega * _bobPitch - damp * _bobPitchVel) * dt;
        _bobPitch    += _bobPitchVel * dt;

        _bobRollVel  += (-omega * omega * _bobRoll - damp * _bobRollVel) * dt;
        _bobRoll     += _bobRollVel * dt;

        // _bobOffset.y = pitch (grados), _bobOffset.x = roll (grados) → HandleLook
        _bobOffset = new Vector3(_bobRoll, _bobPitch, 0f);
    }

    // =========================================================================
    // TIROLINA — pendulo sobre un punto de enganche que avanza por la cuerda
    // =========================================================================

    private void TryAttachToZipline()
    {
        Zipline z = Zipline.FindNearest(transform.position);
        if (z == null) return;

        // Cancelar otros estados activos
        if (_isWallrunning) EndWallrun();
        if (_isSliding)     EndSlide();
        _isDashing = false;

        _activeZipline         = z;
        _ziplineT              = z.GetT(transform.position);
        _pendulumAngle         = 0f;   // empieza colgando recto hacia abajo
        _pendulumVelDeg        = 0f;
        _isOnZipline           = true;
        _moveDirection         = Vector3.zero;
        _ziplineAttachStartPos = transform.position;  // posicion de partida para la transicion
        _ziplineAttachProgress = 0f;

        // Desactivar CC para posicionar el transform libremente
        _cc.enabled = false;

        // Sonido de enganche (en la tirolina)
        z.PlayAttachSound();
        OnZiplineAttached?.Invoke();

        // Sonido de deslizamiento en bucle (en el jugador)
        if (ziplineRideSource != null && ziplineRideSound != null)
        {
            ziplineRideSource.clip = ziplineRideSound;
            ziplineRideSource.loop = true;
            ziplineRideSource.Play();
        }
    }

    private void DetachFromZipline()
    {
        if (ziplineRideSource != null)
            ziplineRideSource.Stop();

        _isOnZipline   = false;
        _activeZipline = null;
        _moveDirection.y = 0f;

        // Reactivar CC
        _cc.enabled = true;
    }

    private void HandleZiplineMovement(Vector2 moveInput)
    {
        if (_activeZipline == null) { DetachFromZipline(); return; }

        // Espacio: soltar con impulso vertical
        if (_jumpBufferTimer > 0f && _canMove)
        {
            _jumpBufferTimer = 0f;
            _moveDirection.y = JumpPower;
            _jumpsUsed = 1;
            DetachFromZipline();
            PlaySfx(jumpSound);
            OnJumpExecuted?.Invoke();
            return;
        }

        float dt = Time.deltaTime;

        // ── 1. Avance automatico del punto de enganche por la cuerda ─────────
        Vector3 zVec = _activeZipline.EndPos - _activeZipline.StartPos;
        float   zLen = zVec.magnitude;
        if (zLen < 0.01f) { DetachFromZipline(); return; }
        Vector3 zDir = zVec / zLen;

        _ziplineT = Mathf.Clamp01(_ziplineT + ((walkSpeed + SyncSpeedCorrection) / zLen) * dt);

        // Pivot: punto de la cuerda al que el jugador esta enganchado
        Vector3 hookPos = Vector3.Lerp(_activeZipline.StartPos, _activeZipline.EndPos, _ziplineT);

        // ── 2. Pendulo — fisica simplificada ─────────────────────────────────
        // El pendulo oscila en el plano perpendicular a la direccion de la cuerda.
        // swingRight = eje de balanceo positivo (hacia la "derecha" de la cuerda en el mundo).
        Vector3 swingRight = Vector3.Cross(zDir, Vector3.up).normalized;
        if (swingRight.sqrMagnitude < 0.01f) swingRight = Vector3.right;

        // Input A/D segun como ve el jugador la cuerda desde la camara
        float camDot    = Vector3.Dot(transform.right, swingRight);
        float inputSign = Mathf.Sign(camDot != 0f ? camDot : 1f);
        float inputVal  = moveInput.x * inputSign;

        // Control directo: respuesta inmediata para esquivar obstaculos
        _pendulumAngle  += inputVal * ziplineDirectControl * dt;

        // Control fisico: acumula inercia mientras hay input.
        // Al SOLTAR A/D (solo en el frame de la transicion), cancela la velocidad
        // acumulada que iba empujando hacia el limite. Despues, el pendulo oscila
        // libremente sin interferencia: no se para en el centro.
        bool hasInput = Mathf.Abs(inputVal) > 0.01f;
        if (hasInput)
        {
            _pendulumVelDeg += inputVal * ziplineSwingAccel * dt;
        }
        else if (_pendulumInputWasHeld)
        {
            // Primer frame sin input: cancelar solo la velocidad que iba al limite
            if (Mathf.Sign(_pendulumVelDeg) == Mathf.Sign(_pendulumAngle))
                _pendulumVelDeg = 0f;
        }
        _pendulumInputWasHeld = hasInput;

        // Gravedad amplificada: fuerza restauradora mas fuerte para bajar rapido.
        // Usa ziplinePendulumGravity (≈9.81) en lugar de Gravity (≈54 a 180BPM):
        // la gravedad de salto es enormemente alta y aplastaba el balanceo lateral.
        float angleRad  = _pendulumAngle * Mathf.Deg2Rad;
        _pendulumVelDeg += -(ziplinePendulumGravity * ziplineGravityMult / ziplineArmLength)
                           * Mathf.Sin(angleRad) * Mathf.Rad2Deg * dt;

        // Amortiguacion
        _pendulumVelDeg -= _pendulumVelDeg * Mathf.Clamp01(ziplineDamping * dt);

        // Integrar y limitar al angulo maximo configurable
        _pendulumAngle  = Mathf.Clamp(_pendulumAngle + _pendulumVelDeg * dt,
                                       -ziplineMaxAngle, ziplineMaxAngle);

        // ── 3. Posicion final del jugador ─────────────────────────────────────
        // Cuelga a ziplineArmLength del pivot en la direccion del pendulo.
        float   aRad      = _pendulumAngle * Mathf.Deg2Rad;
        Vector3 targetPos = hookPos
                          + swingRight   * (Mathf.Sin(aRad) * ziplineArmLength)
                          + Vector3.down * (Mathf.Cos(aRad) * ziplineArmLength);

        // Transicion suave desde la posicion de enganche hasta la de cuelgue.
        // SmoothStep da una curva de ease-in/ease-out natural.
        if (_ziplineAttachProgress < 1f && ziplineAttachTime > 0f)
        {
            _ziplineAttachProgress = Mathf.Clamp01(_ziplineAttachProgress + dt / ziplineAttachTime);
            float blend = Mathf.SmoothStep(0f, 1f, _ziplineAttachProgress);
            transform.position = Vector3.Lerp(_ziplineAttachStartPos, targetPos, blend);
        }
        else
        {
            transform.position = targetPos;
        }

        // ── 4. Llegar al extremo -> desenganchar ──────────────────────────────
        if (_ziplineT >= 1f)
            DetachFromZipline();
    }
}
