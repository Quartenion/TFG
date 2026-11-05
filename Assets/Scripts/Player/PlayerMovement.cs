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
    // -=+=- Física del salto, calculada a partir de jumpHeight y del BPM -=+=-
    // El salto dura 2 beats en total, por lo que halfT = beatDuration (el tiempo hasta el punto más alto es 1 beat).
    // Con h = ½ g t², se obtiene g = 2h / halfT² y v0 = √(2gh).

    private float BeatDuration => BeatManager.BeatDurationOf(beatManager);

    /// <summary>Gravedad calculada para que el salto dure exactamente jumpAirTimeBeats beats.</summary>
    private float Gravity
    {
        get
        {
            // El tiempo hasta el punto más alto es la mitad del vuelo total. Con 2 beats, halfT es 1 beat (por defecto).
            if (_gravityOverride > 0f) return _gravityOverride;   // por ejemplo, al caer con el suelo en la fase 3
            float halfT = BeatDuration * Mathf.Max(0.05f, jumpAirTimeBeats * 0.5f);
            return 2f * jumpHeight / (halfT * halfT);
        }
    }

    private float _gravityOverride;

    /// <summary>
    /// Fuerza una gravedad concreta (en m/s²) hasta que se quita con 0. La usa el jefe para que el jugador caiga a la
    /// misma velocidad que los bloques del suelo al derrumbarse.
    /// </summary>
    public void SetGravityOverride(float gravity) => _gravityOverride = Mathf.Max(0f, gravity);

    /// <summary>Gravedad actual del jugador (en m/s²). La usa el jefe para predecir dónde cae en la fase 3.</summary>
    public float CurrentGravity => Gravity;

    /// <summary>Impulso vertical inicial, con el que el jugador alcanza jumpHeight en 1 beat (la subida).</summary>
    private float JumpPower => Mathf.Sqrt(2f * Gravity * jumpHeight);

    /// <summary>Impulso del doble salto, con la misma gravedad, pero con airJumpHeight.</summary>
    private float AirJumpPower => Mathf.Sqrt(2f * Gravity * airJumpHeight);

    /// <summary>Impulso vertical del salto desde la pared, con la misma gravedad y una altura configurable.</summary>
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
    /// <summary>
    /// Salto desde el kayak. No lanza OnJumpExecuted, para no reproducir el efecto de cámara del salto a pie. Lo usa el
    /// tutorial para dar por hecho el salto.
    /// </summary>
    public UnityEvent OnKayakJump;
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

    // -=+=- Estado privado -=+=-
    private Vector3 _moveDirection    = Vector3.zero;
    private bool    _isOnStairs       = false;   // true mientras sube o baja escaleras (para el modelo del brazo)
    private float   _rotationX        = 0f;
    private float   _rotationY        = 0f;
    private float   _initialYRotation = 0f;
    private float   _slidePitchOffset = 0f;
    private float   _wallRollOffset   = 0f;
    private CharacterController _cc;
    private bool _canMove      = false;  // sigue en false hasta que StartMusic() lo active
    private bool _freeroamMode = false;  // movimiento libre (sin avance automático ni acciones limitadas al beat)
    private bool _isOnKayak   = false;  // jugador subido al ataúd (kayak)
    // Impulso de salida del kayak, que se aplica justo antes del _cc.Move del fotograma siguiente, después de que el
    // cálculo de controlVel sobrescriba _moveDirection.x y z. Se usa en un solo fotograma.
    private Vector3 _pendingKayakImpulse    = Vector3.zero;
    private bool    _hasPendingKayakImpulse = false;
    private bool    _isKayakPlayerJump      = false;   // true si es un salto voluntario, y false si es una expulsión forzada al final del recorrido

    // Colliders del kayak que se ignoran. Unity reinicia Physics.IgnoreCollision al desactivar y reactivar un collider
    // (el CharacterController se apaga al subir y se vuelve a encender al saltar), así que hay que volver a aplicarlo
    // cada vez que se reactiva. Si no, el CharacterController reactivado choca con el ataúd, lo hunde y el muelle de
    // flotación lo lanza hacia arriba.
    private Collider[] _kayakIgnoreColliders;
    // Corrección de la sincronía con la música
    private BeatGridVisualizer _beatGrid;  // lee Origin, Forward y walkSpeed del nivel
    /// <summary>
    /// Corrección de velocidad que se calcula en cada fotograma a partir del error de posición respecto a la rejilla de
    /// beats. Es positiva si el jugador va atrasado (acelerar), negativa si va adelantado (frenar) y 0 si va sincronizado.
    /// Es pública para que KayakController y la tirolina también la apliquen.
    /// </summary>
    public float SyncSpeedCorrection { get; private set; }

    /// <summary>
    /// Desfase de posición respecto a donde debería estar el jugador según la música, en metros (mayor que 0 si va
    /// adelantado y &lt;0 si va atrasado), sin limitar. El kayak lo usa para su propia corrección, que es más fuerte
    /// que la de a pie.
    /// </summary>
    public float SyncDesfase { get; private set; }

    /// <summary>Es true mientras el jugador va subido en el KayakController.</summary>
    public bool IsOnKayak => _isOnKayak;

    /// <summary>Indica si está en movimiento libre, es decir, en la zona de antes de empezar el nivel.</summary>
    public bool IsFreeroam => _freeroamMode;

    /// <summary>
    /// Dirección horizontal en la que avanza el jugador, independiente de hacia dónde mire (el cuerpo gira al mirar, pero
    /// el avance sigue siempre el eje de la pista). La usa ObstacleCollision para clasificar la cara golpeada según el
    /// movimiento real y no según la mirada.
    /// </summary>
    public Vector3 RunForward => Quaternion.Euler(0f, _initialYRotation, 0f) * Vector3.forward;

    /// <summary>Es true mientras el jugador sube o baja escaleras. Lo usa el modelo del brazo para que la mano no rebote.</summary>
    public bool IsOnStairs => _isOnStairs;

    // Altura suave
    private float _targetHeight;
    private float _defaultCameraLocalY;
    private float _defaultSlopeLimit;

    // Dash
    private bool    _isDashing;
    private float   _dashTimeRemaining;
    private Vector3 _dashDirection;

    // Deslizamiento
    private bool    _isSliding;
    private float   _slideTimeRemaining;
    private float   _slideLateralInput;   // movimiento lateral (A/D) en el momento de empezar el deslizamiento

    // Carrera por la pared y salto desde la pared
    private bool    _isWallrunning;
    private float   _wallrunTimeRemaining;
    private Vector3 _wallNormal;       // normal de la pared activa, que se mantiene después de EndWallrun para el margen de tiempo
    private bool    _onLeftWall;
    private bool    _walljumpBoostActive;
    private float   _walljumpBoostRemaining;
    private Vector3 _walljumpBoostVelocity;

    // Salto
    private int _jumpsUsed;

    // Detección de la pared con _wallNormal en lugar de transform.right. transform.right cambia al mover la cámara
    // horizontalmente, y el raycast dejaría de apuntar a la pared, mientras que con -_wallNormal la detección no depende
    // de la orientación de la cámara.
    private float _jumpBufferTimer      = 0f;
    private float _walljumpCoyoteTimer  = 0f;
    private float _stairCoyoteTimer     = 0f;
    // No hay margen para pulsar el salto antes de tiempo, por lo que la pulsación solo vale en el fotograma en el que se
    // hace (el valor mínimo es para que la rama que la usa la vea en ese mismo fotograma). Tampoco hay margen después
    // de dejar el suelo en el salto normal, ya que estropeaba el aterrizaje en el beat, pero sí en el salto desde la
    // pared, de 0,12 s después de soltarse, para poder encadenar el movimiento.
    private const float JumpBufferDuration      = 0.0001f;
    private const float WalljumpCoyoteDuration  = 0.12f;
    private const float StairCoyoteDuration     = 0.25f;   // mantiene el estado "en escaleras" entre un escalón y otro

    // Slam
    private bool _isSlamming;

    // Tirolina
    private bool    _isOnZipline;
    private Zipline _activeZipline;
    private float   _ziplineT;
    private float   _ziplineAttachProgress; // 0 es el inicio de la transición y 1, la posición final
    private Vector3 _ziplineAttachStartPos; // posición del jugador al engancharse
    // Balanceo de la cabeza (muelle sincronizado con el beat)
    private float   _bobPitch;     // desplazamiento actual del cabeceo (grados)
    private float   _bobPitchVel;  // velocidad del muelle del cabeceo (grados/s)
    private float   _bobRoll;      // desplazamiento actual de la inclinación lateral (grados)
    private float   _bobRollVel;   // velocidad del muelle de la inclinación lateral (grados/s)
    private int     _bobBeatCount; // alterna la inclinación a izquierda y derecha en cada beat
    private Vector3 _bobOffset;    // .y es el cabeceo y .x, la inclinación lateral (grados), que se usan en HandleLook

    private float   _pendulumAngle;         // grados, donde 0 es colgando recto hacia abajo y un valor positivo es hacia la derecha de la cuerda
    private float   _pendulumVelDeg;        // velocidad angular del péndulo (grados/s)
    private bool    _pendulumInputWasHeld;  // true si en el fotograma anterior había movimiento lateral (A/D)

    // Acciones de entrada
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _slideAction;
    private InputAction _dashAction;
    private InputAction _ziplineAction;
    private InputAction _slamAction;

    // -=+=- Inicialización -=+=-

    void Start()
    {
        _cc                = GetComponent<CharacterController>();
        if (GetComponent<ObstacleUnstuck>() == null) gameObject.AddComponent<ObstacleUnstuck>();   // para no quedarse atascado en techos bajos
        _targetHeight      = defaultHeight;
        _defaultSlopeLimit = _cc.slopeLimit;
        _beatGrid          = FindAnyObjectByType<BeatGridVisualizer>();
        _initialYRotation  = transform.eulerAngles.y;
        _rotationY         = 0f;
        if (playerCamera != null)
            _defaultCameraLocalY = playerCamera.transform.localPosition.y;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // La carrera y el salto desde la pared solo funcionan en paredes de la capa "Jumpable".
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
        Keybinds.Changed += ApplyKeybinds;   // Ajustes > Controles

        _moveAction.Enable();
        _lookAction.Enable();
        _jumpAction.Enable();
        _slideAction.Enable();
        _dashAction.Enable();
        _ziplineAction.Enable();
        _slamAction.Enable();

        // El balanceo de la cabeza se suscribe al medio beat, con la misma frecuencia que los pasos.
        if (beatManager != null)
            beatManager.OnHalfBeat.AddListener(OnBobBeat);

    }


    /// <summary>Teclas elegidas en Ajustes > Controles.</summary>
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
        // Pueden ser null si el objeto se destruye antes de inicializarse, por ejemplo al cambiar de escena.
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

    /// <summary>Es true mientras el jugador está enganchado a una tirolina.</summary>
    public bool IsOnZipline => _isOnZipline;

    /// <summary>Es true mientras el deslizamiento está activo (el jugador es invulnerable a los obstáculos).</summary>
    public bool IsSliding => _isSliding;

    // -=+=- Rebobinado (tienda) -=+=-
    public bool  IsDashing     => _isDashing;
    public bool  IsWallrunning => _isWallrunning;
    public bool  IsGroundedNow => _cc != null && _cc.enabled && _cc.isGrounded;
    public float LookYaw       => _rotationY;
    public float LookPitch     => _rotationX;

    /// <summary>Coloca al jugador y su mirada. Lo usa el Rebobinado mientras vuelve atrás en el tiempo.</summary>
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

    /// <summary>Suelta la tirolina al instante. Lo usa el Rebobinado al volver a un momento en el que el jugador iba a pie.</summary>
    public void ForceExitZipline()
    {
        if (_isOnZipline) DetachFromZipline();
    }

    /// <summary>Después de rebobinar, no queda ninguna acción en curso (salto, dash, deslizamiento, carrera por la pared ni slam).</summary>
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

    /// <summary>Es true mientras el jugador hace el slam (caída con S). Lo usa la cabeza del jefe en la fase 2.</summary>
    public bool IsSlamming => _isSlamming;

    /// <summary>Lanza al jugador hacia arriba, como un rebote. Lo usa la cabeza del jefe cuando el jugador cae sobre ella con el slam.</summary>
    public void Bounce(float upSpeed)
    {
        _isSlamming      = false;
        _moveDirection.y = upSpeed;
        _jumpsUsed       = Mathf.Min(_jumpsUsed, 1);   // después del rebote queda un salto en el aire
    }

    /// <summary>
    /// Teletransporta al jugador a una posición y rotación, actualizando el estado interno de la orientación. Se usa antes
    /// de que empiece la secuencia de inicio.
    /// </summary>
    public void TeleportToStartPoint(Vector3 position, Quaternion rotation)
    {
        // Se sale de la zona de inicio, y el jugador queda quieto hasta que empiece el beat.
        _freeroamMode = false;
        _canMove      = false;

        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = position;
        transform.rotation = rotation;

        // Se sincroniza el estado interno con la nueva orientación.
        _initialYRotation = rotation.eulerAngles.y;
        _rotationY        = 0f;
        _rotationX        = 0f;
        if (playerCamera != null)
            playerCamera.transform.localRotation = Quaternion.identity;

        if (cc != null) cc.enabled = true;
    }

    /// <summary>
    /// Mueve y gira al jugador suavemente desde su posición actual hasta <paramref name="targetPos"/>, mirando hacia Z+,
    /// durante <paramref name="duration"/> segundos. Se usa en la introducción del nivel, en la que el jugador camina
    /// solo hasta el punto de inicio.
    /// </summary>
    public void StartSmoothMoveToStart(Vector3 targetPos, float duration)
    {
        _freeroamMode     = false;
        _canMove          = false;
        _initialYRotation = 0f;   // en el destino siempre mira hacia Z+
        _rotationY        = 0f;
        _rotationX        = 0f;
        _moveDirection    = Vector3.zero;
        IsMovingToStart   = true;
        StartCoroutine(SmoothMoveCoroutine(targetPos, duration));
    }

    /// <summary>Es true mientras el jugador camina solo hasta el Starting Point, después de abrir la puerta o la pared.</summary>
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

        // Al final se fijan la posición y la rotación exactas.
        transform.position = targetPos;
        transform.rotation = toBodyRot;
        if (playerCamera != null)
            playerCamera.transform.localRotation = Quaternion.identity;

        if (cc != null) cc.enabled = true;
        IsMovingToStart = false;
    }

    /// <summary>Sale de la zona de inicio y bloquea el movimiento hasta que empiece el beat.</summary>
    public void DisableFreeroam()
    {
        _freeroamMode = false;
        _canMove      = false;
    }

    /// <summary>
    /// Activa el movimiento libre sin esperar al beat ni al reloj de audio. El jugador puede moverse y mirar en todas las
    /// direcciones.
    /// </summary>
    public void EnableFreeroam()
    {
        _freeroamMode = true;
        _canMove      = true;
    }

    // -=+=- Kayak -=+=-

    /// <summary>
    /// Lo llama KayakController cuando el jugador salta al ataúd. Desactiva todo el movimiento propio del jugador, ya que
    /// el KayakController se encarga de colocarlo en cada fotograma.
    /// </summary>
    public void EnterKayak()
    {
        if (_isOnKayak) return;
        _isOnKayak = true;

        // Se cancelan los estados activos antes de ceder el control.
        if (_isWallrunning) EndWallrun();
        if (_isSliding)     EndSlide();
        _isDashing     = false;
        // Es importante reiniciar el slam, ya que solo se limpia al tocar el suelo (isGrounded), pero al subir al kayak el
        // CharacterController se desactiva antes. Si quedara activo, al volver a saltar no se aplicaría la gravedad (que se
        // quita durante el slam) y el jugador subiría sin parar.
        _isSlamming    = false;
        _moveDirection = Vector3.zero;
        _cc.enabled    = false;

        // Se reinicia el muelle del balanceo de la cabeza al subir, para que no quede energía acumulada que, después del
        // trayecto (en el que el balanceo no se calcula), se suelte de golpe al saltar y sacuda la cámara.
        _bobPitch = _bobRoll = _bobPitchVel = _bobRollVel = 0f;
        _bobOffset = Vector3.zero;
    }

    /// <summary>Devuelve el movimiento normal al salir del kayak.</summary>
    public void ExitKayak()
    {
        if (!_isOnKayak) return;
        _isOnKayak  = false;
        _cc.enabled = true;
        ApplyKayakIgnores();
    }

    /// <summary>
    /// El KayakController registra aquí sus colliders para que el CharacterController del jugador los ignore siempre,
    /// volviendo a aplicarlo cada vez que se reactiva.
    /// </summary>
    public void RegisterKayakColliders(Collider[] cols)
    {
        // Los colliders se acumulan (quitando los ya destruidos), ya que con dos kayaks a la vez (en el nivel tutorial) el
        // segundo borraría la lista del primero, y el CharacterController del jugador chocaría con ese ataúd y lo hundiría.
        var list = new System.Collections.Generic.List<Collider>();
        if (_kayakIgnoreColliders != null)
            foreach (Collider c in _kayakIgnoreColliders) if (c != null && !list.Contains(c)) list.Add(c);
        if (cols != null)
            foreach (Collider c in cols) if (c != null && !list.Contains(c)) list.Add(c);
        _kayakIgnoreColliders = list.ToArray();
        ApplyKayakIgnores();
    }

    /// <summary>
    /// Vuelve a aplicar Physics.IgnoreCollision entre el CharacterController y los colliders del kayak. Se tiene que llamar
    /// después de cada reactivación del CharacterController, ya que Unity reinicia el ignore al desactivar y reactivar el collider.
    /// </summary>
    private void ApplyKayakIgnores()
    {
        if (_kayakIgnoreColliders == null || _cc == null || !_cc.enabled) return;
        foreach (Collider c in _kayakIgnoreColliders)
            if (c != null) Physics.IgnoreCollision(_cc, c, true);
    }

    /// <summary>
    /// Sale del kayak con un salto natural. <paramref name="jumpVelocity"/> es el impulso vertical (m/s), y
    /// <paramref name="horizontalImpulse"/>, la velocidad horizontal inicial (en la misma dirección que el kayak), para
    /// que el jugador salga hacia delante y no caiga otra vez encima del ataúd.
    /// </summary>
    public void ExitKayakWithJump(float jumpVelocity, Vector3 horizontalImpulse)
    {
        if (!_isOnKayak) return;
        _isOnKayak = false;
        // El CharacterController se vuelve a activar dentro del bloque del impulso pendiente (en el bloque normal de
        // movimiento), y no aquí. Si se activara aquí, como PlayerMovement se ejecuta antes que KayakController en el mismo
        // fotograma, el _cc.Move de ese fotograma ya habría pasado y la física del CharacterController absorbería el impulso.

        // HandleLook orienta el cuerpo durante todo el trayecto (KayakController solo fija la posición del asiento), así que
        // _rotationY ya es la vista actual del jugador. No hay que sincronizar nada y la cámara mantiene su orientación al
        // salir, sin saltos.

        _pendingKayakImpulse    = new Vector3(horizontalImpulse.x, jumpVelocity, horizontalImpulse.z);
        _hasPendingKayakImpulse = true;
    }

    /// <summary>Tutorial. Pide un salto como si se acabara de pulsar la tecla, que se hace en cuanto se pueda.</summary>
    public void BufferJump() => _jumpBufferTimer = Mathf.Max(_jumpBufferTimer, JumpBufferDuration);

    /// <summary>
    /// El jugador salta del kayak por su cuenta pulsando Espacio. Usa el mismo JumpPower que el salto normal (calculado
    /// con el BPM), y el CharacterController se vuelve a activar dentro del bloque del impulso pendiente, igual que en
    /// ExitKayakWithJump.
    /// </summary>
    public void ExitKayakWithPlayerJump(Vector3 horizontalImpulse)
    {
        if (!_isOnKayak) return;
        _isOnKayak = false;

        // HandleLook orienta el cuerpo durante todo el trayecto (KayakController solo fija la posición del asiento), así que
        // _rotationY ya es la vista actual del jugador. No hay que sincronizar nada y la cámara mantiene su orientación al
        // salir, sin saltos.

        _pendingKayakImpulse    = new Vector3(horizontalImpulse.x, JumpPower, horizontalImpulse.z);
        _hasPendingKayakImpulse = true;
        _isKayakPlayerJump      = true;
        OnKayakJump?.Invoke();
    }

    public void ScheduleStart(double dspTime)
    {
        // Se sale de la zona de inicio, se reinicia la orientación y se espera al beat.
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
        // Si dspTime no es válido (por ejemplo, si BeatManager.StartMusic() falló y ScheduledStartDSP se quedó en -1),
        // la condición AudioSettings.dspTime < -1 siempre sería falsa y _canMove se activaría en el primer fotograma sin esperar.
        if (dspTime <= 0)
        {
            Debug.LogError("[PlayerMovement] WaitForDSPStart: dspTime inválido (" + dspTime +
                           "). ¿Está asignado el AudioSource en BeatManager? El jugador no arrancará.");
            yield break;
        }

        // Se espera hasta que el reloj de audio llegue al instante programado.
        while (AudioSettings.dspTime < dspTime)
            yield return null;

        // Por seguridad, se comprueba que el CharacterController esté activo al empezar.
        var ccCheck = GetComponent<CharacterController>();
        if (ccCheck != null && !ccCheck.enabled) ccCheck.enabled = true;
        ApplyKayakIgnores();

        _canMove = true;
    }

    // -=+=- Update principal -=+=-

    void Update()
    {
        UpdateSyncCorrection(); // siempre primero, ya que actualiza SyncSpeedCorrection para todos los sistemas
        if (_isOnKayak)
        {
            HandleLook();   // el jugador puede seguir mirando libremente mientras va en el kayak
            return;
        }
        // En la zona de inicio, el avance sigue la dirección actual del jugador, y durante la partida es siempre la
        // dirección inicial, ya que el jugador avanza solo.
        float lookYaw = _freeroamMode ? _initialYRotation + _rotationY : _initialYRotation;
        Quaternion runRot  = Quaternion.Euler(0f, lookYaw, 0f);
        Vector3 forward    = runRot * Vector3.forward;
        Vector3 right      = runRot * Vector3.right;

        Vector2 moveInput  = _moveAction.ReadValue<Vector2>();
        if (!_freeroamMode) moveInput.y = 1f;

        // -=+=- Tirolina: enganche y desenganche con F -=+=-
        if (!_freeroamMode && _ziplineAction.WasPressedThisFrame() && TutorialGate.Allows(TutorialKey.Zipline))
        {
            if (_isOnZipline) DetachFromZipline();
            else TryAttachToZipline();
        }

        if (_isOnZipline)
        {
            // Se actualiza el registro del salto para poder saltar y soltarse.
            if (_jumpAction.WasPressedThisFrame() && TutorialGate.Allows(TutorialKey.Jump))
                _jumpBufferTimer = JumpBufferDuration;
            else if (_jumpBufferTimer > 0f)
                _jumpBufferTimer -= Time.deltaTime;

            HandleZiplineMovement(moveInput);
            HandleLook();
            return;
        }

        // -=+=- Registro de la pulsación del salto -=+=-
        // Guarda la pulsación durante JumpBufferDuration segundos. También funciona en la zona de inicio, donde se puede
        // saltar libremente, sin coste ni ritmo.
        if (_canMove && _jumpAction.WasPressedThisFrame() && TutorialGate.Allows(TutorialKey.Jump))
            _jumpBufferTimer = JumpBufferDuration;
        else if (_jumpBufferTimer > 0f)
            _jumpBufferTimer -= Time.deltaTime;

        // Margen de tiempo para el salto desde la pared
        if (_walljumpCoyoteTimer > 0f)
            _walljumpCoyoteTimer -= Time.deltaTime;

        // -=+=- Dash -=+=-
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

        // -=+=- Deslizamiento -=+=-
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
                    _slideLateralInput = moveInput.x;  // se guarda el movimiento lateral sin normalizar
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

        // -=+=- Detección de paredes para empezar la carrera por la pared -=+=-
        // Aquí se sigue usando transform.right, ya que solo sirve para empezar la carrera. Una vez activa, la detección usa _wallNormal.
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

        // -=+=- Inicio de la carrera por la pared -=+=-
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

        // Velocidad de avance efectiva, que es walkSpeed más o menos la corrección de sincronía (calculada en UpdateSyncCorrection).
        float effectiveForwardSpeed = walkSpeed + SyncSpeedCorrection;

        // Se vuelve a evaluar en cada fotograma en la rama del movimiento normal, y es false por defecto en el dash, el deslizamiento y la carrera por la pared.
        _isOnStairs = false;

        // -=+=- Movimiento según el estado -=+=-

        if (_isDashing)
        {
            // Dash. El jugador mantiene su velocidad de avance y se le suma el impulso lateral del dash, ya que, si no, el
            // avance quedaría a 0 durante los 0,18 s del dash.
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
            // Deslizamiento
            _slideTimeRemaining -= Time.deltaTime;

            // La velocidad hacia delante es siempre walkSpeed completa, y el movimiento lateral usa la entrada sin normalizar
            // para no restar velocidad hacia delante.
            Vector3 slideVel = forward * effectiveForwardSpeed + right * (_slideLateralInput * walkSpeed);
            _moveDirection.x = slideVel.x;
            _moveDirection.z = slideVel.z;

            // El salto cancela el deslizamiento, usando la pulsación registrada.
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
            // Carrera por la pared

            // La pared se detecta con -_wallNormal en lugar de transform.right, por lo que funciona aunque el jugador mire a los lados.
            Vector3 wallCheckOrigin = transform.position + Vector3.up * (_cc.height * 0.5f);
            RaycastHit wallCheckHit;
            bool wallStillThere = Physics.Raycast(
                                      wallCheckOrigin, -_wallNormal,
                                      out wallCheckHit, wallDetectDistance * 1.2f, wallLayerMask)
                                  && Mathf.Abs(wallCheckHit.normal.y) < 0.3f;

            _wallrunTimeRemaining -= Time.deltaTime;

            if (!wallStillThere || _wallrunTimeRemaining <= 0f || _cc.isGrounded)
            {
                // Se pierde la pared. Si el salto está registrado y el jugador no está en el suelo, se hace el salto desde la
                // pared antes de salir.
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
                // Carrera por la pared activa, con movimiento sobre la superficie
                Vector3 wallForward = Vector3.Cross(_wallNormal, Vector3.up);
                if (Vector3.Dot(wallForward, transform.forward) < 0f)
                    wallForward = -wallForward;

                _moveDirection.x = wallForward.x * walkSpeed;
                _moveDirection.z = wallForward.z * walkSpeed;

                // La gravedad se acumula, por lo que el jugador baja cada vez más rápido, como si se deslizara por la pared. Empieza
                // recto y va cayendo poco a poco.
                _moveDirection.y -= wallrunGravity * Time.deltaTime;
                _moveDirection.y  = Mathf.Max(_moveDirection.y, -wallrunMaxFallSpeed);

                // Salto desde la pared, usando la pulsación registrada en lugar de WasPressedThisFrame
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
            // Movimiento normal (suelo y aire)
            float prevY = _moveDirection.y;

            if (_cc.isGrounded)
            {
                _jumpsUsed           = 0;
                _walljumpBoostActive = false;
            }

            // Con !_hasPendingKayakImpulse, el salto de salida del kayak lo gestiona solo el bloque del impulso pendiente. Si
            // no, la pulsación llegaría aquí y, con el CharacterController desactivado (isGrounded = false), se trataría como un
            // salto en el aire (doble salto).
            bool tryJump   = _jumpBufferTimer > 0f && _canMove && !_hasPendingKayakImpulse;
            bool jumpsLeft = _jumpsUsed < maxJumps
                         || (EnergyManager.Instance != null && EnergyManager.Instance.godMode);
            // En el tutorial, el doble salto (saltar en el aire) no se puede usar hasta que se enseña.
            if (!_cc.isGrounded && TutorialGate.IsLocked(TutorialKey.DoubleJump) && _walljumpCoyoteTimer <= 0f) jumpsLeft = false;

            // Margen del salto desde la pared. El jugador acaba de soltarse de la pared y todavía puede saltar desde ella.
            if (tryJump && _walljumpCoyoteTimer > 0f && !_cc.isGrounded)
            {
                _jumpBufferTimer     = 0f;
                _walljumpCoyoteTimer = 0f;
                PerformWalljump();
                prevY = _moveDirection.y; // se actualiza para que la gravedad no lo sobrescriba más abajo
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

                    // En la zona de inicio (entre niveles), saltar no cuesta energía, ya que no hay barra.
                    bool canJump = _freeroamMode || EnergyManager.Instance == null
                        || EnergyManager.Instance.TrySpend(cost);

                    if (canJump)
                    {
                        // Saltar cancela el slam, ya que mientras _isSlamming está activo no se aplica gravedad y, si se quedara activo,
                        // el salto subiría a velocidad constante sin frenar.
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
                // Slam, al pulsar S por primera vez en el aire
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
                // mientras se hace el slam no se aplica gravedad extra, ya que el jugador ya cae a -slamForce
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

                // Se garantiza una velocidad mínima para alejarse de la pared, por lo que, aunque el jugador empuje hacia ella, la
                // componente en la dirección de la normal nunca baja de walljumpAwayForce * t.
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

            // Escaleras. Se suben los escalones y se evita quedarse atascado en los saltos laterales.
            Vector3 hStairDir = new Vector3(_moveDirection.x, 0f, _moveDirection.z);
            // El rayo es un poco más largo para detectar también los escalones al bajar, cuando el jugador va cayendo de un
            // escalón al siguiente y no toca la superficie del escalón.
            bool stairHit = stairsLayerMask != 0 &&
                            Physics.Raycast(transform.position, Vector3.down,
                                            _cc.height * 0.6f + 0.6f, stairsLayerMask);
            // Se mantiene el estado "en escaleras" un instante después del último impacto, para que los huecos entre
            // escalones (sobre todo al bajar) no lo desactiven y la mano no rebote.
            if (stairHit) _stairCoyoteTimer = StairCoyoteDuration;
            else if (_stairCoyoteTimer > 0f) _stairCoyoteTimer -= Time.deltaTime;
            bool onStairs = _stairCoyoteTimer > 0f;
            // Con un slopeLimit alto, el CharacterController no queda bloqueado por las caras diagonales de ProBuilder al
            // aterrizar de lado o con A o D pulsadas.
            _cc.slopeLimit = onStairs ? 89f : _defaultSlopeLimit;
            _isOnStairs = onStairs;
            // Se pasa 'forward' (la dirección de avance, que es fija) para que el raycast de los escalones apunte hacia delante
            // aunque el jugador mire hacia un lado.
            HandleStairClimb(hStairDir, forward);

            // Impulso de salida del kayak, que se aplica una sola vez. Se aplica aquí, después de que controlVel haya sobrescrito
            // x y z, para que no se pierdan el avance ni el salto. El CharacterController se vuelve a activar en este momento
            // para que el Move ocurra en el mismo fotograma, sin un fotograma en el que esté activo y el impulso todavía no se
            // haya aplicado.
            if (_hasPendingKayakImpulse)
            {
                if (!_cc.enabled) { _cc.enabled = true; ApplyKayakIgnores(); }  // se reactiva y se vuelve a ignorar el kayak
                _moveDirection          = _pendingKayakImpulse;
                _hasPendingKayakImpulse = false;
                _pendingKayakImpulse    = Vector3.zero;
                if (_isKayakPlayerJump)
                {
                    // El salto voluntario es un salto normal (el primero). Cuenta como un salto usado, por lo que queda disponible el
                    // doble salto (maxJumps - 1 saltos en el aire). Suena como un salto normal y se borra la pulsación registrada para
                    // no encadenar otro salto. No se lanza OnJumpExecuted, ya que reproduce el efecto de cámara del salto a pie, que
                    // queda raro al salir del kayak.
                    _isKayakPlayerJump = false;
                    _jumpsUsed         = 1;
                    _jumpBufferTimer   = 0f;
                    PlaySfx(jumpSound);
                }
            }

            // Solo se mueve si el CharacterController está activo. En los fotogramas de transición (al entrar o salir del kayak
            // o de la tirolina) puede estar desactivado un instante, y entonces la posición la controla ese sistema, por lo que
            // llamar a Move daría el error "Move called on inactive controller".
            if (_cc.enabled)
                CcMove(_moveDirection * Time.deltaTime);
        }

        // -=+=- Altura suave (CharacterController y cámara) -=+=-
        _cc.height = Mathf.Lerp(_cc.height, _targetHeight, Time.deltaTime * heightLerpSpeed);
        if (playerCamera != null && defaultHeight > 0f)
        {
            UpdateHeadBob();
            Vector3 camPos = playerCamera.transform.localPosition;
            camPos.y = _defaultCameraLocalY * (_cc.height / defaultHeight);
            playerCamera.transform.localPosition = camPos;
        }

        // -=+=- Cámara: cabeceo (deslizamiento) e inclinación (carrera por la pared) -=+=-
        float pitchTarget = _isSliding ? -slideCameraTilt : 0f;
        _slidePitchOffset = Mathf.Lerp(_slidePitchOffset, pitchTarget, Time.deltaTime * slideCameraTiltSpeed);

        float rollTarget = _isWallrunning
            ? (_onLeftWall ? -wallCameraRoll : wallCameraRoll)
            : 0f;
        _wallRollOffset = Mathf.Lerp(_wallRollOffset, rollTarget, Time.deltaTime * wallCameraRollSpeed);

        HandleLook();
    }

    // -=+=- Salto desde la pared, en un método aparte para usarlo desde varias ramas -=+=-

    private void PerformWalljump()
    {
        // Se usa _wallNormal, que se mantiene después de EndWallrun.
        Vector3 awayDir        = _wallNormal;
        awayDir.y              = 0f;
        _walljumpBoostVelocity  = awayDir.normalized * walljumpAwayForce;
        _walljumpBoostActive    = true;
        _walljumpBoostRemaining = walljumpBoostDuration;

        // Solo se sobrescribe la X (el impulso lateral para alejarse de la pared). La Z (la velocidad hacia delante,
        // walkSpeed) se mantiene de la carrera por la pared para no perder un fotograma de avance.
        _moveDirection.x = _walljumpBoostVelocity.x;
        _moveDirection.y = WalljumpPower;
        _isSlamming      = false; // el salto desde la pared también cancela el slam, ya que si no, el jugador subiría sin gravedad
        _jumpsUsed       = 1; // después del salto desde la pared queda un salto en el aire

        EndWallrun();              // pone _walljumpCoyoteTimer = WalljumpCoyoteDuration
        _walljumpCoyoteTimer = 0f; // pero se cancela, ya que el salto desde la pared ya se ha hecho

        PlaySfx(walljumpSound != null ? walljumpSound : jumpSound);
        OnWalljumped?.Invoke();
    }

    // -=+=- Mirada -=+=-

    // -=+=- Sincronía con la música (corrección de la posición) -=+=-

    /// <summary>
    /// Calcula cuántos m/s hay que sumar (o restar) a walkSpeed para que el jugador vuelva a la posición que le
    /// corresponde según el tiempo de la música. Toma como referencia el BeatGridVisualizer (Origin, Forward y walkSpeed
    /// del nivel). El resultado se guarda en <see cref="SyncSpeedCorrection"/>, al que acceden KayakController,
    /// HandleZiplineMovement, etc. Si no hay BeatGridVisualizer en la escena o la música no está sonando, la corrección
    /// queda en 0 y el jugador avanza exactamente a walkSpeed.
    /// </summary>
    private void UpdateSyncCorrection()
    {
        SyncSpeedCorrection = 0f;
        SyncDesfase         = 0f;

        if (_beatGrid == null)
        {
            // Se intenta encontrar si todavía no está, ya que puede haberse creado más tarde.
            _beatGrid = FindAnyObjectByType<BeatGridVisualizer>();
            if (_beatGrid == null) return;
        }

        if (BeatManager.Instance == null)                        return;
        if (!BeatManager.Instance.HasClock)      return;
        if (!BeatManager.Instance.IsPlaying)                     return;
        if (_freeroamMode)                                        return;
        if (positionCorrectionStrength <= 0f)                    return;

        // Error respecto a la rejilla
        double now         = AudioSettings.dspTime;
        float  expected    = (float)(BeatManager.Instance.ClockSeconds * _beatGrid.walkSpeed);
        float  actual      = _beatGrid.DistanceAlong(transform.position);
        float  deltaDist   = actual - expected; // mayor que 0 si va adelantado y menor que 0 si va atrasado
        SyncDesfase        = deltaDist;

        // Corrección. Si va atrasado (deltaDist < 0), se añade velocidad (resultado > 0).
        SyncSpeedCorrection = Mathf.Clamp(
            -deltaDist * positionCorrectionStrength,
            -maxPositionCorrection * walkSpeed,
             maxPositionCorrection * walkSpeed);
    }

    // -=+=- Escaleras -=+=-

    /// <summary>
    /// Mueve el CharacterController, salvo con el tiempo parado (Time.deltaTime = 0, por ejemplo en el tutorial), ya que
    /// un Move de longitud cero pone isGrounded a false, y entonces no se podría deslizar ni hacer un salto normal en la parada.
    /// </summary>
    private void CcMove(Vector3 delta)
    {
        if (Time.deltaTime <= 0f) return;
        _cc.Move(delta);
    }

    /// <summary>
    /// Sube escalones automáticamente, y es compatible con las mallas de ProBuilder. Lanza un raycast de arriba abajo un
    /// poco por delante del jugador, de forma que siempre golpea la superficie plana del escalón y nunca la cara
    /// vertical, que es la que atascaba al CharacterController con los MeshColliders de ProBuilder. Si la superficie
    /// detectada está más alta que los pies del jugador (y dentro de maxStepHeight), lo sube con otro CC.Move, para no
    /// interferir con la física interna del CharacterController.
    /// </summary>
    private void HandleStairClimb(Vector3 hDir, Vector3 travelForward)
    {
        if (stairsLayerMask == 0)       return;
        if (!_cc.isGrounded)            return;
        if (hDir.sqrMagnitude < 0.001f) return;

        // El centro del CharacterController está a height/2 sobre el suelo.
        float feetY = transform.position.y - _cc.height * 0.5f;

        // Dirección de avance (horizontal), que no depende de A y D ni de la mirada. El cuerpo gira con la mirada
        // (HandleLook), por lo que con transform.forward el rayo apuntaría hacia un lado al mirar de lado, no detectaría el
        // escalón y el jugador se atascaría. travelForward es la dirección fija en la que avanza el jugador.
        Vector3 facing = Vector3.ProjectOnPlane(travelForward, Vector3.up).normalized;
        if (facing.sqrMagnitude < 0.001f) return;

        // -=+=- Rayo 1: horizontal, a la altura de los pies y hacia delante -=+=-
        // Detecta si hay una cara vertical de escalón justo delante. Sin este filtro, el jugador subiría continuamente
        // mientras camina por la superficie plana.
        Vector3 footOrigin = new Vector3(transform.position.x, feetY + 0.1f, transform.position.z);
        if (!Physics.Raycast(footOrigin, facing, _cc.radius + 0.2f, stairsLayerMask))
            return;

        // -=+=- Rayo 2: vertical (de arriba abajo), también hacia delante -=+=-
        // Encuentra la superficie plana del escalón que tiene delante.
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

        // El jugador se coloca al instante a la altura del escalón, como hace el stepOffset de Unity, ya que subir poco a
        // poco lo dejaría a mitad de la cara vertical y perdería velocidad.
        _cc.Move(Vector3.up * diff);
    }

    // -=+=- Mirada guiada (tutorial): con el juego parado, la vista gira sola y suavemente hacia algo -=+=-
    private bool _guideOn, _guideReturn;
    private Vector3 _guidePoint;
    private float _guideFromX, _guideFromY, _guideVelX, _guideVelY;
    private const float GuideSmooth = 0.35f;   // segundos (en tiempo real) que tarda más o menos en girar

    /// <summary>
    /// Tutorial. Gira la vista suavemente hacia 'point', también con el juego parado. Con null se suelta y, si
    /// 'returnAfter' es true, vuelve suavemente a donde miraba antes, hasta que el jugador mueva el ratón.
    /// </summary>
    public void SetGuideLook(Vector3? point, bool returnAfter = false)
    {
        if (point.HasValue)
        {
            if (!_guideOn) { _guideFromX = _rotationX; _guideFromY = _rotationY; }
            _guideOn = true; _guideReturn = false;
            _guidePoint = point.Value;
            return;
        }
        if (!_guideOn) return;
        _guideOn = false;
        _guideReturn = returnAfter;
        _guideVelX = _guideVelY = 0f;
    }

    /// <summary>Aplica la mirada guiada. Devuelve true si controla la mirada, en cuyo caso no se lee el ratón.</summary>
    private bool GuideLook(Vector2 look)
    {
        float dt = Time.unscaledDeltaTime;
        if (_guideOn && playerCamera != null)
        {
            Vector3 d = _guidePoint - playerCamera.transform.position;
            float yaw = Mathf.DeltaAngle(_initialYRotation, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            if (!_freeroamMode) yaw = Mathf.Clamp(yaw, -90f, 90f);
            float pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -lookXLimit, lookXLimit);
            _rotationY = Mathf.SmoothDampAngle(_rotationY, yaw, ref _guideVelY, GuideSmooth, Mathf.Infinity, dt);
            _rotationX = Mathf.SmoothDampAngle(_rotationX, pitch, ref _guideVelX, GuideSmooth, Mathf.Infinity, dt);
            return true;
        }
        if (_guideReturn)
        {
            if (look.sqrMagnitude > 0.01f) { _guideReturn = false; return false; }   // el jugador ya mueve la vista
            _rotationY = Mathf.SmoothDampAngle(_rotationY, _guideFromY, ref _guideVelY, GuideSmooth, Mathf.Infinity, dt);
            _rotationX = Mathf.SmoothDampAngle(_rotationX, _guideFromX, ref _guideVelX, GuideSmooth, Mathf.Infinity, dt);
            if (Mathf.Abs(Mathf.DeltaAngle(_rotationY, _guideFromY)) < 0.3f && Mathf.Abs(_rotationX - _guideFromX) < 0.3f) _guideReturn = false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Mueve solo la cámara con el ratón, aunque el componente esté desactivado, por ejemplo mientras el final del nivel
    /// lleva al jugador andando hasta la sala después de practicar una parte del tutorial.
    /// </summary>
    public void LookOnly()
    {
        bool could = _canMove;
        _canMove = true;
        HandleLook();
        _canMove = could;
    }

    // -=+=- Salto de la mirada al capturar el ratón -=+=-
    // Al recuperar el foco, al cambiar el bloqueo del cursor o con el primer clic después (en el editor, el cursor no
    // se bloquea hasta que se hace clic en la vista de juego), el ratón se coloca en el centro de la ventana y en ese
    // fotograma llega un desplazamiento muy grande, que haría saltar la cámara. Por eso, la mirada se ignora durante
    // esos pocos fotogramas.
    private int  _lookSkipFrames = 3;
    private bool _lookPrimed;
    private CursorLockMode _lastLock;

    private void OnEnable()
    {
        // Al volver a activarse (después de una pantalla con el cursor libre), se quitan los restos del muelle de la cámara
        // y se evita el salto de la mirada al colocarse el ratón en el centro.
        _bobPitch = _bobRoll = _bobPitchVel = _bobRollVel = 0f;
        _bobOffset = Vector3.zero;
        _lookSkipFrames = 5;
    }

    private void OnApplicationFocus(bool focus)
    {
        _lookSkipFrames = 3;
        if (!focus) _lookPrimed = false;
    }

    /// <summary>Fotograma en el que se ignoró la mirada por el salto del ratón. También lo usa el balanceo de la mano.</summary>
    public static int LookSpikeFrame = -1;

    private bool LookSpike()
    {
        if (Cursor.lockState != _lastLock) { _lastLock = Cursor.lockState; _lookSkipFrames = 5; _lookPrimed = false; }
        if (Cursor.lockState != CursorLockMode.Locked) { LookSpikeFrame = Time.frameCount; return true; }   // con el cursor libre no se mira
        Mouse m = Mouse.current;
        if (!_lookPrimed && m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame))
        {
            _lookPrimed = true;
            _lookSkipFrames = Mathf.Max(_lookSkipFrames, 3);
        }
        if (_lookSkipFrames > 0) { _lookSkipFrames--; LookSpikeFrame = Time.frameCount; return true; }
        return false;
    }

    private void HandleLook()
    {
        if (!_canMove) return;

        Vector2 look = _lookAction.ReadValue<Vector2>();
        if (LookSpike()) look = Vector2.zero;
        if (TutorialGate.Busy) look *= Time.timeScale;   // en el tutorial a cámara lenta, la cámara también va lenta, y con el juego parado se queda quieta
        float   sens = 0.1f * GameSettings.Sensitivity;   // ajuste del jugador, desde el menú o la pausa

        if (GuideLook(look)) look = Vector2.zero;
        else if (!_freeroamMode && !GameSettings.CameraLookActive)
        {
            // Con Ajustes > "Cámara con ratón: NO", durante el nivel la vista mira siempre al frente (y vuelve suavemente). En
            // la zona de inicio sí se mueve, ya que hay que mirar la tienda, los contratos y la pared para empezar.
            float back = 240f * Time.deltaTime;
            _rotationX = Mathf.MoveTowards(_rotationX, 0f, back);
            _rotationY = Mathf.MoveTowards(_rotationY, 0f, back);
            look = Vector2.zero;
        }

        _rotationX += -look.y * lookSpeed * sens;
        _rotationX  = Mathf.Clamp(_rotationX, -lookXLimit, lookXLimit);

        _rotationY += look.x * lookSpeed * sens;
        // Durante la partida, el giro lateral está limitado a ±90°, y en la zona de inicio es libre.
        if (!_freeroamMode) _rotationY = Mathf.Clamp(_rotationY, -90f, 90f);
        transform.rotation = Quaternion.Euler(0f, _initialYRotation + _rotationY, 0f);

        playerCamera.transform.localRotation =
            Quaternion.Euler(_rotationX + _slidePitchOffset + _bobOffset.y, 0f, _wallRollOffset + _bobOffset.x);
    }

    // -=+=- Funciones auxiliares -=+=-

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
        // Abre el margen de tiempo, durante el que el jugador puede saltar desde la pared hasta WalljumpCoyoteDuration
        // segundos después de soltarse.
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

    // -=+=- Balanceo de la cabeza -=+=-

    /// <summary>
    /// Lo llama automáticamente BeatManager.OnBeat. Da un impulso al muelle de la cámara al ritmo de la música.
    /// </summary>
    private void OnBobBeat()
    {
        if (headBobIntensity < 0.01f) return;
        // Con el componente desactivado (por ejemplo, en la sala mientras se responde el cuestionario), Update no calcula
        // el muelle, por lo que los impulsos se acumularían y, al reactivarlo, se soltarían de golpe y la cámara saltaría.
        if (!isActiveAndEnabled) return;

        // Solo se aplica si el jugador está corriendo por el suelo. La condición !_isOnKayak es necesaria, ya que con el
        // CharacterController desactivado en el kayak, isGrounded mantiene su último valor (true) y UpdateHeadBob no se
        // ejecuta, así que los impulsos se acumularían sin calcularse y, al saltar del kayak, se soltarían de golpe.
        bool active = _cc.isGrounded && _canMove
                   && !_isSliding && !_isDashing
                   && !_isWallrunning && !_isOnZipline
                   && !_isOnKayak;
        if (!active) return;

        // Velocidad del impulso, calculada para llegar a un pico de unos headBobAmplitudeY grados. v0 ≈ amplitude * 2 *
        // omega, una aproximación para un muelle subamortiguado típico.
        float omega    = headBobSpringHz * Mathf.PI * 2f;
        float kickPitch = headBobAmplitudeY * headBobIntensity * omega * 2f;
        float kickRoll  = headBobAmplitudeX * headBobIntensity * omega * 2f;

        _bobPitchVel -= kickPitch;                                          // cabeceo hacia abajo
        _bobRollVel  += kickRoll * (_bobBeatCount % 2 == 0 ? 1f : -1f);   // alterna izquierda y derecha
        _bobBeatCount++;
    }

    /// <summary>
    /// Calcula el muelle amortiguado en cada fotograma. El muelle se va calmando solo hasta que el siguiente beat le vuelve
    /// a dar un impulso.
    /// </summary>
    private void UpdateHeadBob()
    {
        float omega = headBobSpringHz * Mathf.PI * 2f;
        float damp  = headBobDamping * 2f * omega;    // coeficiente de amortiguación crítica

        // F = -k*x - c*v (muelle amortiguado). Se calcula en subpasos de 1/240 s como máximo, ya que con fotogramas largos
        // (un portátil sin enchufar, tirones al cargar...) un solo paso grande haría el muelle inestable, oscilando cada
        // vez más hasta dar NaN.
        float remaining = Mathf.Min(Time.deltaTime, 0.25f);
        while (remaining > 0f)
        {
            float dt = Mathf.Min(remaining, 1f / 240f);
            remaining -= dt;
            _bobPitchVel += (-omega * omega * _bobPitch - damp * _bobPitchVel) * dt;
            _bobPitch    += _bobPitchVel * dt;
            _bobRollVel  += (-omega * omega * _bobRoll - damp * _bobRollVel) * dt;
            _bobRoll     += _bobRollVel * dt;
        }

        // Por seguridad, si algo llega a NaN o infinito, se reinicia el muelle para no romper la cámara.
        if (!float.IsFinite(_bobPitch) || !float.IsFinite(_bobRoll) ||
            !float.IsFinite(_bobPitchVel) || !float.IsFinite(_bobRollVel))
            _bobPitch = _bobRoll = _bobPitchVel = _bobRollVel = 0f;

        // _bobOffset.y es el cabeceo y _bobOffset.x, la inclinación lateral (grados), que se usan en HandleLook
        _bobOffset = new Vector3(_bobRoll, _bobPitch, 0f);
    }

    // -=+=- Tirolina: péndulo sobre un punto de enganche que avanza por la cuerda -=+=-

    private void TryAttachToZipline()
    {
        Zipline z = Zipline.FindNearest(transform.position);
        if (z == null) return;

        // Se cancelan los otros estados activos.
        if (_isWallrunning) EndWallrun();
        if (_isSliding)     EndSlide();
        _isDashing = false;

        _activeZipline         = z;
        _ziplineT              = z.GetT(transform.position);
        _pendulumAngle         = 0f;   // empieza colgando recto hacia abajo
        _pendulumVelDeg        = 0f;
        _isOnZipline           = true;
        _moveDirection         = Vector3.zero;
        _ziplineAttachStartPos = transform.position;  // posición de partida para la transición
        _ziplineAttachProgress = 0f;

        // Se desactiva el CharacterController para colocar el transform libremente.
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

        // Se vuelve a activar el CharacterController.
        _cc.enabled = true;
    }

    private void HandleZiplineMovement(Vector2 moveInput)
    {
        if (_activeZipline == null) { DetachFromZipline(); return; }

        // Con Espacio, el jugador se suelta con un impulso vertical.
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

        // -=+=- 1. Avance automático del punto de enganche por la cuerda -=+=-
        Vector3 zVec = _activeZipline.EndPos - _activeZipline.StartPos;
        float   zLen = zVec.magnitude;
        if (zLen < 0.01f) { DetachFromZipline(); return; }
        Vector3 zDir = zVec / zLen;

        _ziplineT = Mathf.Clamp01(_ziplineT + ((walkSpeed + SyncSpeedCorrection) / zLen) * dt);

        // Punto de la cuerda al que está enganchado el jugador
        Vector3 hookPos = Vector3.Lerp(_activeZipline.StartPos, _activeZipline.EndPos, _ziplineT);

        // -=+=- 2. Péndulo, con física simplificada -=+=-
        // El péndulo oscila en el plano perpendicular a la dirección de la cuerda. swingRight es el eje positivo del
        // balanceo, hacia la derecha de la cuerda en el mundo.
        Vector3 swingRight = Vector3.Cross(zDir, Vector3.up).normalized;
        if (swingRight.sqrMagnitude < 0.01f) swingRight = Vector3.right;

        // Movimiento lateral (A/D) según cómo ve el jugador la cuerda desde la cámara
        float camDot    = Vector3.Dot(transform.right, swingRight);
        float inputSign = Mathf.Sign(camDot != 0f ? camDot : 1f);
        float inputVal  = moveInput.x * inputSign;

        // Control directo, con respuesta inmediata para esquivar obstáculos
        _pendulumAngle  += inputVal * ziplineDirectControl * dt;

        // Control físico, que acumula inercia mientras se pulsa A o D. Al soltar la tecla (solo en el fotograma de la
        // transición), se cancela la velocidad acumulada que empujaba hacia el límite. Después, el péndulo oscila libremente,
        // sin pararse en el centro.
        bool hasInput = Mathf.Abs(inputVal) > 0.01f;
        if (hasInput)
        {
            _pendulumVelDeg += inputVal * ziplineSwingAccel * dt;
        }
        else if (_pendulumInputWasHeld)
        {
            // En el primer fotograma sin entrada, solo se cancela la velocidad que iba hacia el límite.
            if (Mathf.Sign(_pendulumVelDeg) == Mathf.Sign(_pendulumAngle))
                _pendulumVelDeg = 0f;
        }
        _pendulumInputWasHeld = hasInput;

        // Gravedad amplificada, con una fuerza que devuelve el péndulo al centro más rápido. Se usa ziplinePendulumGravity
        // (unos 9,81) en lugar de Gravity (unos 54 a 180 BPM), ya que la gravedad del salto es mucho más alta y anularía el
        // balanceo lateral.
        float angleRad  = _pendulumAngle * Mathf.Deg2Rad;
        _pendulumVelDeg += -(ziplinePendulumGravity * ziplineGravityMult / ziplineArmLength)
                           * Mathf.Sin(angleRad) * Mathf.Rad2Deg * dt;

        // Amortiguación
        _pendulumVelDeg -= _pendulumVelDeg * Mathf.Clamp01(ziplineDamping * dt);

        // Se calcula y se limita al ángulo máximo configurado.
        _pendulumAngle  = Mathf.Clamp(_pendulumAngle + _pendulumVelDeg * dt,
                                       -ziplineMaxAngle, ziplineMaxAngle);

        // -=+=- 3. Posición final del jugador -=+=-
        // Cuelga a ziplineArmLength del punto de enganche, en la dirección del péndulo.
        float   aRad      = _pendulumAngle * Mathf.Deg2Rad;
        Vector3 targetPos = hookPos
                          + swingRight   * (Mathf.Sin(aRad) * ziplineArmLength)
                          + Vector3.down * (Mathf.Cos(aRad) * ziplineArmLength);

        // Transición suave desde la posición de enganche hasta la de colgar. SmoothStep da una curva que acelera al
        // principio y frena al final.
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

        // -=+=- 4. Al llegar al extremo, se desengancha -=+=-
        if (_ziplineT >= 1f)
            DetachFromZipline();
    }
}
