using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mecánica del ataúd-kayak.
///
/// MODELO DE MOVIMIENTO:
///   Rigidbody no-kinematic con FreezeRotation. La rotación visual la manejamos nosotros.
///   El eje de avance se mantiene constante en FixedUpdate sobreescribiendo la componente Z
///   de la velocidad, de modo que colisiones frontales no alteran la velocidad de avance.
///   La flotación usa fuerzas de tipo muelle amortiguado aplicadas en FixedUpdate.
///   El movimiento lateral es puramente físico: se aplica como VelocityChange en cada
///   remada, y el arrastre del agua lo amortigua hasta cero con el tiempo.
///
/// COLISIONES:
///   Los colliders del kayak son físicos (no triggers) con material sin fricción.
///   OnCollisionStay agrega todos los contactos y distingue embestida de proa (letal)
///   de roce lateral (la pared sólida bloquea el avance lateral, sin game over).
///   Embestida frontal: game over si el jugador va dentro; rotura + sonido si ya saltó.
///   La capa de agua se ignora con Physics.IgnoreLayerCollision para que no frene el kayak.
///
/// DIRECCIÓN LATERAL:
///   Cada clic Good/Perfect fija la velocidad lateral a ±steerLateralSpeed con VelocityChange.
///   El arrastre del agua (waterDrag) la decae a 0 con el tiempo.
///
/// ENERGÍA:
///   Good/Perfect: EnergyManager.HitRed(timing)
///   Miss:         EnergyManager.MissRed()
///   RhythmInput ignora sus propios handlers cuando IsOnKayak = true.
///
/// ROTACIÓN VISUAL:
///   El kayak no rota por física. Bank (roll) proporcional a velocidad lateral,
///   yaw (giro de proa) proporcional a velocidad lateral, sway sinusoidal en flotación.
/// </summary>
[AddComponentMenu("Level/Kayak Controller")]
[RequireComponent(typeof(Rigidbody))]
public class KayakController : MonoBehaviour
{
    public enum KayakState { Vertical, Tilting, Falling, Floating, PlayerJumped }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Movimiento")]
    [Tooltip("Velocidad de avance constante (m/s). Se sobreescribe con PlayerMovement.walkSpeed al activar.")]
    public float forwardSpeed = 6f;

    [Header("Sincronía con la música")]
    [Tooltip("Fuerza con la que el kayak corrige su velocidad para volver a la posición que le\n" +
             "toca según la música. Más alto = recupera antes un desfase (al entrar, tras un choque…).")]
    [Min(0f)]
    public float kayakSyncStrength = 2f;

    [Tooltip("Corrección máxima de velocidad como fracción de forwardSpeed. 0.5 = puede ir hasta un\n" +
             "50% más rápido/lento para recuperar el desfase. Más alto que a pie para que el kayak\n" +
             "no se quede desincronizado.")]
    [Range(0f, 1.5f)]
    public float kayakMaxSyncCorrection = 0.5f;

    [Header("Dirección lateral (ritmo)")]
    [Tooltip("Velocidad lateral máxima objetivo al remar (m/s).")]
    public float steerLateralSpeed = 5f;

    [Tooltip("Aceleración lateral aplicada mientras dura la remada (m/s²).\n" +
             "La velocidad máxima real = steerAcceleration × paddleDuration.")]
    [Range(5f, 80f)]
    public float steerAcceleration = 35f;

    [Tooltip("Duración de la fuerza de cada remada (segundos).\n" +
             "Valores bajos = golpe rápido; altos = empuje prolongado.")]
    [Range(0.05f, 0.6f)]
    public float paddleDuration = 0.2f;

    [Header("Combo de remo (aceleración por repetición)")]
    [Tooltip("Nº de remadas consecutivas en la MISMA dirección para subir un escalón de velocidad.")]
    [Range(1, 6)]
    public int steerComboHitsPerStep = 2;

    [Tooltip("m/s que se añaden a la velocidad lateral máxima por cada escalón de combo.")]
    [Range(0f, 6f)]
    public float steerComboSpeedStep = 1.5f;

    [Tooltip("Velocidad lateral máxima ABSOLUTA alcanzable encadenando remadas (m/s).")]
    public float steerMaxLateralSpeed = 14f;

    [Tooltip("Segundos sin remar (o al cambiar de lado / fallar) tras los que el combo se reinicia.")]
    [Range(0.3f, 4f)]
    public float steerComboResetTime = 1.2f;

    [Tooltip("Si está activo, HACE FALTA acertar el beat para remar (como jumpRequiresBeat/dashRequiresBeat\n" +
             "a pie): un fallo NO mueve el kayak, solo penaliza energía. Si está desactivado (por defecto),\n" +
             "se rema aunque falles el beat y el fallo cuesta más energía (kayakMissPenaltyScale).")]
    public bool paddleRequiresBeat = false;

    [Tooltip("Al FALLAR el beat remando: cuánta MÁS energía quita respecto a un fallo a pie.\n" +
             "Multiplicador sobre la penalización normal. 1 = igual que a pie; 2 = el doble.")]
    [Min(1f)]
    public float kayakMissPenaltyScale = 2f;

    [Header("Bank visual (inclinación lateral)")]
    [Tooltip("Grados de inclinación por m/s de velocidad lateral.")]
    [Range(0f, 10f)]
    public float bankDegreesPerMPS = 3.5f;

    [Tooltip("Inclinación máxima en grados (clamp).")]
    [Range(0f, 45f)]
    public float maxBankDegrees = 22f;

    [Header("Giro de proa al remar (yaw)")]
    [Tooltip("Grados de giro de la proa (yaw) por m/s de velocidad lateral.")]
    [Range(0f, 25f)]
    public float yawDegreesPerMPS = 8f;

    [Tooltip("Ángulo máximo de giro de proa (grados).")]
    [Range(0f, 45f)]
    public float maxYawDegrees = 15f;

    [Tooltip("Suavizado de la inclinación visual (bank/yaw) ante cambios de velocidad lateral.\n" +
             "Alto = responde al instante; bajo = más suave. Evita el tirón de inclinación\n" +
             "cuando una pared corta de golpe la velocidad lateral al rozar un obstáculo.")]
    [Range(2f, 30f)]
    public float lateralVisualSmoothing = 12f;

    [Header("Sway / Bamboleo")]
    [Tooltip("Amplitud del roll sinusoidal en flotación (grados).")]
    public float swayAmplitude = 4f;

    [Tooltip("Frecuencia del roll (ciclos/segundo).")]
    [Range(0.1f, 3f)]
    public float swayFrequency = 0.7f;

    [Tooltip("Amplitud del pitch en flotación (grados).")]
    public float pitchAmplitude = 1.5f;

    [Header("Bamboleo de impacto con el agua")]
    [Tooltip("Amplitud del bamboleo (grados) al IMPACTAR con el agua. Decrece exponencialmente\n" +
             "hasta desaparecer en ~1.5 s. No se aplica durante la caída.")]
    public float fallWobbleAmplitude = 18f;

    [Tooltip("Frecuencia del bamboleo de impacto (Hz).")]
    [Range(0.5f, 6f)]
    public float fallWobbleFrequency = 3.5f;

    [Header("Agua y flotación")]
    [Tooltip("Layer de la superficie del agua.")]
    public LayerMask waterLayerMask;

    [Tooltip("Altura a la que flota el kayak sobre la superficie del agua (m).\n" +
             "Súbelo si el agua se mete dentro del ataúd al remar/inclinarse.\n" +
             "OJO: si ya tienes un valor puesto en el Inspector, cámbialo ahí (el valor\n" +
             "serializado de la escena no se actualiza solo al cambiar el default del script).")]
    public float floatHeight = 0.6f;

    [Tooltip("Rigidez del muelle de flotación (aceleración / metro de desviación).\n" +
             "Amortiguación crítica: buoyancyDamping ≈ 2 × √buoyancyForce.")]
    [Range(1f, 80f)]
    public float buoyancyForce = 25f;

    [Tooltip("Amortiguación de la flotación. Bajo = mucho rebote; alto = movimiento rígido.")]
    [Range(0.5f, 20f)]
    public float buoyancyDamping = 6f;

    [Tooltip("Límite de aceleración vertical del muelle de flotación (m/s²).\n" +
             "Blindaje: evita que, si el ataúd se hunde de golpe, el muelle sobre-corrija\n" +
             "y lo dispare al cielo. 40 deja margen de sobra para flotar con normalidad.")]
    [Range(15f, 100f)]
    public float maxBuoyancyAccel = 40f;

    [Tooltip("Bloquea la altura (eje Y) mientras el kayak flota: lo fija a la superficie del agua\n" +
             "y anula la velocidad vertical cada paso de física. Evita que un choque lateral fuerte\n" +
             "lo eleve o lo hunda de golpe. El balanceo visual (roll/pitch) no se ve afectado por ser\n" +
             "rotación y no posición.")]
    public bool lockYInWater = true;

    [Tooltip("Coeficiente de arrastre lateral del agua.\n" +
             "Controla con qué rapidez la velocidad lateral decae a 0 al dejar de remar.\n" +
             "Velocidad decae como v(t) = v0·e^(-waterDrag·t). " +
             "waterDrag ≈ 3 → decay de 5→0.1 m/s en ~1.4 s.")]
    [Range(0.5f, 15f)]
    public float waterDrag = 3f;

    [Header("Animación de inclinación (patada)")]
    [Tooltip("Duración total del tilt en segundos.")]
    public float tiltDuration = 0.45f;

    [Tooltip("Eje LOCAL alrededor del que gira el ataúd al caer.")]
    public Vector3 tiltAxis = Vector3.right;

    [Tooltip("Ángulo total en grados. 90 = queda perfectamente horizontal.")]
    public float tiltAngle = 90f;

    [Tooltip("Si el kayak ya está colocado TUMBADO (horizontal) en la escena, actívalo para\n" +
             "saltarte la animación de caída 90°→0° y el chapoteo de impacto. Al activarse\n" +
             "(el jugador salta/entra encima) empieza a flotar y a moverse directamente con la\n" +
             "orientación en la que lo colocaste.\n" +
             "Colócalo tumbado con su eje de avance (Z azul) apuntando hacia donde debe avanzar.")]
    public bool startHorizontal = false;

    [Header("Endpoint")]
    [Tooltip("Transform que marca el final del tramo de kayak.")]
    public Transform endPoint;

    [Tooltip("Velocidad vertical del salto automático al llegar al endpoint (m/s).")]
    [Range(3f, 25f)]
    public float exitJumpVelocity = 10f;

    [Header("Salto voluntario del jugador")]
    [Tooltip("Velocidad angular con la que el kayak recupera su orientación frontal mientras el jugador está en el aire.")]
    [Range(1f, 15f)]
    public float jumpAlignSpeed = 5f;

    [Header("Obstáculos — Game Over")]
    [Tooltip("GameOverManager de la escena.")]
    public GameOverManager gameOverManager;

    [Tooltip("Nombre del layer de los objetos letales.")]
    public string obstacleLayerName = "Obstacle";

    [Tooltip("Fracción de la eslora (0-1) que cuenta como 'proa'. Un choque sólo es\n" +
             "frontal si el contacto cae en este tramo delantero del ataúd Y la superficie\n" +
             "lo encara de frente. Evita que un roce lateral se interprete como embestida.")]
    [Range(0.1f, 0.6f)]
    public float noseZoneFraction = 0.35f;

    [Tooltip("DEBUG: dibuja cada contacto (rojo = embestida de proa, verde = roce) y\n" +
             "loguea la decisión de cada colisión en consola. Activa Gizmos en Game/Scene\n" +
             "para ver las flechas. Desactívalo al terminar de depurar.")]
    public bool debugCollisions = false;

    [Header("Audio")]
    [Tooltip("AudioSource del ataúd.")]
    public AudioSource audioSource;

    [Tooltip("Sonido de madera al subir al ataúd.")]
    public AudioClip woodSound;

    [Tooltip("Sonido de splash al caer al agua.")]
    public AudioClip splashSound;

    [Tooltip("Efectos de sonido de remo (2-6 variaciones).")]
    public AudioClip[] paddleSounds;

    [Tooltip("Sonido del ataúd rompiéndose al llegar al endpoint.")]
    public AudioClip breakSound;

    [Header("Referencias")]
    [Tooltip("Altura del jugador sobre el centro del ataúd cuando está en el kayak (m).")]
    public float seatHeightOffset = 0.5f;

    [Tooltip("Tiempo (s) que tarda el jugador en asentarse suavemente en el asiento al\n" +
             "reembarcar tras un salto. 0 = teletransporte instantáneo.")]
    [Range(0f, 0.5f)]
    public float reboardSmoothTime = 0.18f;

    [Tooltip("Tamaño de la caja de activación en espacio LOCAL del ataúd (m).\n" +
             "X = ancho lateral, Y = alto, Z = profundidad (eje de avance).\n" +
             "Visible como wireframe amarillo en la vista de escena.")]
    public Vector3 activationBoxSize = new Vector3(3f, 3f, 5f);

    // ── Estado interno ────────────────────────────────────────────────────────

    private KayakState     _state = KayakState.Vertical;
    private PlayerMovement _player;
    private PlayerMovement _cachedPlayer;
    private Rigidbody      _rb;

    // Ejes fijos (inicializados en Activate, no cambian nunca)
    private Vector3 _forwardDir;
    private Vector3 _rightDir;

    // Velocidad lateral actual (leída del Rigidbody, usada para bank/yaw visual)
    private float _lateralVelocity = 0f;

    // Remo suave: fuerza lateral aplicada durante paddleDuration segundos
    private float _paddleDir   = 0f;
    private float _paddleTimer = 0f;

    // Combo de remo: remadas consecutivas en la misma dirección suben el tope de velocidad
    private float _comboDir         = 0f;   // dirección del combo actual (+1/-1/0)
    private int   _comboCount       = 0;    // remadas consecutivas en esa dirección
    private float _steerCap;                // velocidad lateral máxima efectiva actual
    private float _lastComboHitTime = -999f;

    // Rotación
    private float _initialYRot;
    private float _tiltProgress;
    // Orientación tumbada colocada por diseño (solo en modo startHorizontal).
    private Quaternion _baseFlatRotation = Quaternion.identity;

    // Caída (solo para la animación de bamboleo; el Rigidbody gestiona la física)
    private float _fallTimer;

    // Agua
    private float _waterSurfaceY;
    private bool  _waterFound;

    // Sway
    private float _swayTimer;

    // Anti-spam beat
    private int _lastHitBeatIdx = -1;

    // Anti-repetición sonidos de remo
    private int _lastPaddleSoundIdx = -1;

    // Endpoint
    private bool  _exhausted = false;
    private float _prevZ     = float.MinValue;

    // Corriente de agua
    private WaterCurrentZone _activeCurrentZone = null;

    // Salto voluntario
    private float _designedYRot;
    private float _playerJumpedTimer;
    private const float ReboardCooldown = 0.4f;

    // Reembarque suave: desplazamiento inicial jugador→asiento que decae a 0
    private float   _reboardBlendTimer = 0f;
    private Vector3 _reboardOffset     = Vector3.zero;

    // Plataforma sólida: durante PlayerJumped, cuando el jugador se eleva por encima
    // del ataúd, activamos la colisión física CC↔kayak para que aterrice sobre él
    // como en una plataforma (en vez de atravesarlo y hundirse).
    private bool               _platformSolid = false;
    private CharacterController _playerCC      = null;
    private float              _prevPlayerY   = 0f;   // para saber si el jugador desciende

    // Obstáculos
    private int        _obstacleLayer = -1;
    private bool       _crashed       = false;   // true tras un choque letal / rotura
    private Collider[] _kayakColliders;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        _designedYRot = transform.eulerAngles.y;
        _startPos = transform.position;   // (pose de diseño: el Rebobinado puede devolver el ataúd aquí)
        _startRot = transform.rotation;

        // Rigidbody: kinematic hasta que el jugador active el kayak.
        // En Vertical el ataúd es un prop estático; solo activamos la física al Tilt.
        _rb                  = GetComponent<Rigidbody>();
        _rb.isKinematic    = true;
        _rb.freezeRotation = true;
        _rb.useGravity     = true;
        _rb.linearDamping  = 0f;
        _rb.angularDamping = 0f;
        // Interpolación: suaviza la posición visual del ataúd entre pasos de física.
        // Sin esto, cuando el jugador salta (cámara moviéndose en Update a los FPS de
        // pantalla) el ataúd se mueve en FixedUpdate (50 Hz) y se ve a tirones (jitter).
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Separación suave al solaparse con una pared: sin esto, si el ataúd penetra
        // un poco en un obstáculo, el motor lo expulsa a gran velocidad (un "pop"
        // brusco). Limitarla hace que se despegue con suavidad.
        _rb.maxDepenetrationVelocity = 3f;

        // Colliders FÍSICOS (no triggers): OnCollisionStay detecta game over y rebote.
        // Los MeshCollider deben ser convex porque el Rigidbody es dinámico.
        _kayakColliders = GetComponentsInChildren<Collider>();
        foreach (var col in _kayakColliders)
        {
            col.isTrigger = false;
            if (col is MeshCollider mc)
                mc.convex = true;
        }

        // Material sin fricción: evita que la superficie del agua o las barreras
        // del nivel frenen al kayak lateralmente al colisionar físicamente con ellos.
        // frictionCombine = Minimum → aunque el otro objeto tenga fricción alta,
        // el resultado es siempre 0 (el mínimo de los dos).
        var noFriction = new PhysicsMaterial("Kayak_NoFriction")
        {
            dynamicFriction = 0f,
            staticFriction  = 0f,
            bounciness      = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine   = PhysicsMaterialCombine.Minimum
        };
        foreach (var col in _kayakColliders)
            col.sharedMaterial = noFriction;

        // Ignorar colisiones físicas entre el kayak y la capa de agua.
        // Sin esto el kayak "descansa" sobre la malla del agua, la fricción impide
        // el movimiento lateral y la transición Falling→Floating puede bloquearse
        // si el collider del agua empuja el centro del kayak por encima del umbral
        // waterSurfaceY + floatHeight. La flotación se gestiona con raycasts propios.
        for (int i = 0; i < 32; i++)
            if (((int)waterLayerMask & (1 << i)) != 0)
                Physics.IgnoreLayerCollision(gameObject.layer, i, true);

        // Ignorar colisiones entre el CC del jugador y los colliders del kayak.
        // Se REGISTRAN en PlayerMovement para que el ignore se re-aplique cada vez
        // que el CC se reactiva (al saltar del kayak). Sin esto, el CC reactivado
        // choca con el ataúd, lo hunde y el muelle de flotación lo lanza al cielo.
        _cachedPlayer = FindAnyObjectByType<PlayerMovement>();
        if (_cachedPlayer != null)
        {
            _playerCC = _cachedPlayer.GetComponent<CharacterController>();
            IgnorePlayerCollisions(true);
            _cachedPlayer.RegisterKayakColliders(_kayakColliders);
        }

        _obstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
        if (_obstacleLayer == -1)
            Debug.LogWarning($"[KayakController] Layer \"{obstacleLayerName}\" no encontrado.", this);

        if (waterLayerMask == 0)
            Debug.LogWarning("[KayakController] waterLayerMask no asignado.", this);
    }

    private void OnDestroy()
    {
        // Restaurar la matriz de colisiones de layers al destruir el objeto,
        // para no afectar a otros scripts que puedan necesitar la colisión agua-Default.
        for (int i = 0; i < 32; i++)
            if (((int)waterLayerMask & (1 << i)) != 0)
                Physics.IgnoreLayerCollision(gameObject.layer, i, false);
    }

    // ── Física (FixedUpdate) ──────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (_rewindFrozen) return;   // (rebobinando: lo coloca TimeRewinder)
        if (_crashed || _state == KayakState.Vertical || _forwardDir == Vector3.zero) return;

        switch (_state)
        {
            case KayakState.Tilting:
                // Gravedad libre (trayectoria parabólica).
                SetForwardSpeed(forwardSpeed);
                ZeroLateral();
                break;

            case KayakState.Falling:
                SetForwardSpeed(forwardSpeed);
                ZeroLateral();
                break;

            case KayakState.Floating:
                // Corrección de sincronía PROPIA del kayak (más fuerte que la de a pie, que está
                // capada al 15%): recupera de verdad un desfase de posición respecto a la música.
                float desfase  = _cachedPlayer != null ? _cachedPlayer.SyncDesfase : 0f;
                float capFwd   = kayakMaxSyncCorrection * forwardSpeed;
                float syncCorr = Mathf.Clamp(-desfase * kayakSyncStrength, -capFwd, capFwd);
                float targetFwd = forwardSpeed + syncCorr;
                SetForwardSpeed(targetFwd);
                ApplyPaddleForce();
                bool pinY = lockYInWater && _waterFound;
                if (!pinY) ApplyBuoyancy();
                ApplyWaterDrag();
                ApplyCurrent();
                ApplyWaterBounds();
                if (pinY) PinToWaterSurface();   // fija la Y a la superficie (anti-pop lateral)
                break;

            case KayakState.PlayerJumped:
                DriveKinematicPlatform();
                break;
        }
    }

    /// <summary>
    /// Conduce el ataúd vacío como plataforma cinemática en FixedUpdate: avance +
    /// seguimiento del agua (posición) y orientación de diseño (rotación), ambos con
    /// MovePosition/MoveRotation para que la interpolación los suavice. Kinematic no
    /// genera OnCollisionStay contra estáticos, así que la rotura contra obstáculos se
    /// detecta aquí con un sondeo hacia delante.
    /// </summary>
    private void DriveKinematicPlatform()
    {
        Vector3 pos = _rb.position + _forwardDir * (forwardSpeed * Time.fixedDeltaTime);
        if (_waterFound)
            pos.y = Mathf.Lerp(pos.y, _waterSurfaceY + floatHeight,
                               1f - Mathf.Exp(-8f * Time.fixedDeltaTime));
        _rb.MovePosition(pos);

        // Rotación objetivo (orientación de diseño, tumbado). Se alcanza con un Slerp
        // suave desde la orientación actual para no dar un "snap" al saltar (el ataúd
        // venía con sway/bank de Floating y no debe alinearse de golpe).
        Quaternion target;
        if (startHorizontal)
        {
            target = _baseFlatRotation;
        }
        else
        {
            Quaternion tiltQ = Quaternion.AngleAxis(tiltAngle, tiltAxis.normalized);
            target = Quaternion.Euler(0f, _initialYRot, 0f) * tiltQ;
        }
        float rotK = 1f - Mathf.Exp(-jumpAlignSpeed * Time.fixedDeltaTime);
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, target, rotK));

        // Rotura del ataúd vacío al embestir un obstáculo (sondeo desde el centro
        // hacia la proa, ya que kinematic no dispara OnCollisionStay contra estáticos).
        if (!_crashed && _obstacleLayer != -1)
        {
            Bounds b = KayakBounds();
            float halfFwd = Mathf.Abs(b.extents.x * _forwardDir.x) + Mathf.Abs(b.extents.z * _forwardDir.z);
            if (Physics.SphereCast(b.center, 0.35f, _forwardDir, out _, halfFwd + 0.3f,
                                   1 << _obstacleLayer, QueryTriggerInteraction.Ignore))
                CrashKayak();
        }
    }

    /// <summary>Sobreescribe la componente de avance de la velocidad del Rigidbody.</summary>
    private void SetForwardSpeed(float target)
    {
        float curr  = Vector3.Dot(_rb.linearVelocity, _forwardDir);
        _rb.AddForce(_forwardDir * (target - curr), ForceMode.VelocityChange);
    }

    /// <summary>Elimina toda velocidad lateral (durante Tilting y Falling).</summary>
    private void ZeroLateral()
    {
        float lat = Vector3.Dot(_rb.linearVelocity, _rightDir);
        if (Mathf.Abs(lat) > 0.001f)
            _rb.AddForce(-_rightDir * lat, ForceMode.VelocityChange);
    }

    /// <summary>
    /// Aplica un muelle amortiguado vertical que mantiene el kayak a
    /// <see cref="floatHeight"/> sobre la superficie del agua.
    /// F = +9.81 (contra-gravedad) − k·offset − c·vy
    /// → Equilibrio exacto en la altura objetivo; oscila con amortiguación natural.
    /// </summary>
    private void ApplyBuoyancy()
    {
        if (!_waterFound) return;
        float targetY  = _waterSurfaceY + floatHeight;
        float offset   = transform.position.y - targetY; // positivo = sobre objetivo
        float vy       = _rb.linearVelocity.y;
        float force    = 9.81f
                       - buoyancyForce  * offset
                       - buoyancyDamping * vy;
        // Blindaje: limitar la aceleración ascendente para que una inmersión brusca
        // no dispare el ataúd al cielo por sobre-corrección del muelle.
        force = Mathf.Min(force, maxBuoyancyAccel);
        _rb.AddForce(new Vector3(0f, force, 0f), ForceMode.Acceleration);
    }

    /// <summary>
    /// Fija la altura del kayak a la superficie del agua y anula la velocidad vertical.
    /// Sustituye a la boyancia por muelle cuando <see cref="lockYInWater"/> está activo:
    /// un choque lateral fuerte no puede elevar ni hundir el kayak en Y.
    /// </summary>
    private void PinToWaterSurface()
    {
        Vector3 p = _rb.position;
        p.y = _waterSurfaceY + floatHeight;
        _rb.position = p;

        Vector3 v = _rb.linearVelocity;
        v.y = 0f;
        _rb.linearVelocity = v;
    }

    /// <summary>Arrastre lateral del agua: decae la velocidad lateral exponencialmente.</summary>
    private void ApplyWaterDrag()
    {
        float latSpd = Vector3.Dot(_rb.linearVelocity, _rightDir);
        if (Mathf.Abs(latSpd) > 0.001f)
            _rb.AddForce(-_rightDir * latSpd * waterDrag, ForceMode.Acceleration);
    }

    /// <summary>Empuja el kayak con la corriente de agua activa.</summary>
    private void ApplyCurrent()
    {
        if (_activeCurrentZone != null)
            _rb.AddForce(_rightDir * _activeCurrentZone.SignedStrength, ForceMode.Acceleration);
    }

    /// <summary>
    /// Aplica la fuerza de remo durante <see cref="paddleDuration"/> segundos tras cada clic.
    /// Crea un efecto de aceleración progresiva en lugar de un salto brusco de velocidad.
    /// La velocidad lateral máxima se limita a <see cref="steerLateralSpeed"/> para que el
    /// remo continuo no acumule demasiada inercia.
    /// </summary>
    private void ApplyPaddleForce()
    {
        if (_paddleTimer <= 0f) return;
        _paddleTimer -= Time.fixedDeltaTime;

        float currLat = Vector3.Dot(_rb.linearVelocity, _rightDir);
        float target  = _paddleDir * _steerCap;   // tope actual (sube con el combo)

        // Conduce la velocidad lateral HACIA el tope de forma suave (no acelera "a ciegas"
        // contra el arrastre, que la frenaba mucho antes de llegar). steerAcceleration actúa
        // como rapidez de respuesta: con ~35 alcanza el tope dentro de la ventana de remada.
        // Solo empuja si vamos por debajo del tope en esa dirección (permite cambiar de lado).
        if (currLat * _paddleDir < _steerCap)
        {
            float k      = 1f - Mathf.Exp(-steerAcceleration * Time.fixedDeltaTime);
            float newLat = Mathf.Lerp(currLat, target, k);
            _rb.AddForce(_rightDir * (newLat - currLat), ForceMode.VelocityChange);
        }
    }

    /// <summary>Cancela el movimiento lateral si el siguiente paso saldría del río.</summary>
    private void ApplyWaterBounds()
    {
        if (waterLayerMask == 0) return;
        float latSpd = Vector3.Dot(_rb.linearVelocity, _rightDir);
        if (Mathf.Abs(latSpd) < 0.01f) return;
        Vector3 nextPos = transform.position + _rightDir * (latSpd * Time.fixedDeltaTime);
        if (!HasWaterBelow(nextPos))
            _rb.AddForce(-_rightDir * latSpd, ForceMode.VelocityChange);
    }

    // ── Lógica de frame (Update) ──────────────────────────────────────────────

    private void Update()
    {
        if (_rewindFrozen) return;   // (rebobinando: lo coloca TimeRewinder)
        if (_crashed) return;   // ataúd roto: no reacciona a nada más

        if (_state == KayakState.Vertical)
        {
            CheckProximityActivation();
            return;
        }

        switch (_state)
        {
            case KayakState.Tilting:      UpdateTilting();      break;
            case KayakState.Falling:      UpdateFalling();      break;
            case KayakState.Floating:     UpdateFloating();     break;
            case KayakState.PlayerJumped: UpdatePlayerJumped(); break;
        }

        // Velocidad lateral del Rigidbody, SUAVIZADA para los visuales (bank/yaw).
        // Al chocar con una pared la física corta la velocidad lateral de golpe; si
        // la inclinación siguiera ese valor crudo daría un tirón. El filtro exponencial
        // hace que la inclinación baje suave en lugar de saltar.
        float rawLateral = _rightDir.sqrMagnitude > 0f
            ? Vector3.Dot(_rb.linearVelocity, _rightDir)
            : 0f;
        float smoothK = 1f - Mathf.Exp(-lateralVisualSmoothing * Time.deltaTime);
        _lateralVelocity = Mathf.Lerp(_lateralVelocity, rawLateral, smoothK);

        // Durante PlayerJumped la rotación la fija MoveRotation en FixedUpdate: NO
        // escribimos transform.rotation desde Update para no romper la interpolación
        // (era la causa del jitter visto desde la cámara del jugador al saltar).
        if (_state != KayakState.PlayerJumped) ApplyRotation();
        if (_player != null) SyncPlayerToSeat();
    }

    // ── Activación ────────────────────────────────────────────────────────────

    private void CheckProximityActivation()
    {
        if (_exhausted || _cachedPlayer == null) return;
        // Comprobación en espacio local: la caja se alinea con la orientación del ataúd.
        Vector3 local = transform.InverseTransformPoint(_cachedPlayer.transform.position);
        Vector3 half  = activationBoxSize * 0.5f;
        if (Mathf.Abs(local.x) <= half.x &&
            Mathf.Abs(local.y) <= half.y &&
            Mathf.Abs(local.z) <= half.z)
        {
            Activate(_cachedPlayer);
            return;
        }

        // Ataúd TUMBADO desde el principio (startHorizontal): la caja de activación es local y, al estar girado 90°,
        // su eje Z queda en VERTICAL; con un Z pequeño (p. ej. 0.46) la caja es una lámina finísima que el jugador
        // atraviesa cayendo en un solo frame → no se montaba y moría en el agua. Respaldo robusto: si el jugador
        // está sobre la huella del ataúd (XZ) y sus pies han bajado hasta su tapa, se monta.
        if (startHorizontal && _playerCC != null && _playerCC.enabled)
        {
            Bounds kb = KayakBounds();
            if (kb.size.sqrMagnitude < 0.0001f) return;
            Vector3 pp = _cachedPlayer.transform.position;
            const float margin = 0.6f;
            bool overFootprint = Mathf.Abs(pp.x - kb.center.x) <= kb.extents.x + margin &&
                                 Mathf.Abs(pp.z - kb.center.z) <= kb.extents.z + margin;
            float feetY = _playerCC.bounds.min.y;
            bool atTop  = feetY <= KayakTopY() + 0.5f && feetY >= kb.min.y - 2f;
            if (overFootprint && atTop) Activate(_cachedPlayer);
        }
    }

    private void Activate(PlayerMovement player)
    {
        _player = player;
        _player.EnterKayak();
        BeatManager bmA = BeatManager.Instance;
        ActivatedSongTime = bmA != null && bmA.IsPlaying ? bmA.SongTime : 0f;

        forwardSpeed = player.walkSpeed;

        Vector3 coffinFwd = transform.forward;
        Vector3 flatFwd   = new Vector3(coffinFwd.x, 0f, coffinFwd.z);
        // Si el forward apunta casi vertical (típico de un modelo tumbado cuyo eje Z mira
        // arriba/abajo), usa el eje "up" del objeto proyectado como dirección de avance.
        if (flatFwd.sqrMagnitude < 0.001f)
        {
            Vector3 up = transform.up;
            flatFwd = new Vector3(up.x, 0f, up.z);
        }
        _forwardDir = flatFwd.sqrMagnitude > 0.001f ? flatFwd.normalized : Vector3.forward;
        _rightDir   = Vector3.Cross(Vector3.up, _forwardDir).normalized;

        _initialYRot = transform.eulerAngles.y;

        PlaySound(woodSound);

        _tiltProgress     = 0f;
        _fallTimer        = 0f;
        _swayTimer        = 0f;
        _lastHitBeatIdx     = -1;
        _lastPaddleSoundIdx = -1;
        _prevZ              = transform.position.z;
        _comboDir           = 0f;
        _comboCount         = 0;
        _steerCap           = steerLateralSpeed;

        // Activar física al entrar en juego
        _rb.isKinematic     = false;
        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        if (startHorizontal)
        {
            // Ya está tumbado por diseño: sin animación de caída ni bamboleo de impacto.
            // Conserva la orientación colocada y arranca flotando de inmediato.
            _baseFlatRotation = transform.rotation;
            _tiltProgress     = 1f;
            _swayTimer        = 10f;   // impactDecay ≈ 0 → sin chapoteo de aterrizaje

            if (FindWaterSurface(out float wy))
            {
                _waterSurfaceY = wy;
                _waterFound    = true;
                Vector3 p = _rb.position;
                p.y = wy + floatHeight;
                _rb.position       = p;
                transform.position = p;
            }

            _state = KayakState.Floating;
        }
        else
        {
            _state = KayakState.Tilting;
        }
    }

    // ── Estados ───────────────────────────────────────────────────────────────

    private void UpdateTilting()
    {
        _tiltProgress += Time.deltaTime / Mathf.Max(tiltDuration, 0.01f);
        _tiltProgress  = Mathf.Clamp01(_tiltProgress);

        if (_tiltProgress >= 1f)
        {
            _fallTimer = 0f;
            _state     = KayakState.Falling;
        }
    }

    private void UpdateFalling()
    {
        _fallTimer += Time.deltaTime;
        // La gravedad del Rigidbody gestiona la caída vertical

        if (FindWaterSurface(out float waterY))
        {
            _waterSurfaceY = waterY;
            _waterFound    = true;

            if (transform.position.y <= _waterSurfaceY + floatHeight)
            {
                // Limitar la velocidad de impacto vertical: el muelle de flotación
                // absorbe el resto progresivamente en lugar de recibir toda la energía
                // de golpe (evita el rebote explosivo con buoyancyForce alto).
                Vector3 vel = _rb.linearVelocity;
                vel.y = Mathf.Max(vel.y, -2f);
                _rb.linearVelocity  = vel;
                _rb.angularVelocity = Vector3.zero; // sin giro residual al entrar al agua

                _swayTimer = 0f;
                _state     = KayakState.Floating;
                PlaySound(splashSound);
            }
        }
    }

    private void UpdateFloating()
    {
        _swayTimer += Time.deltaTime;

        // El cuerpo del jugador sentado puede chocar con obstáculos elevados que el
        // ataúd esquiva por debajo → hay que saltar. Si no salta y su hitbox toca el
        // obstáculo, game over.
        if (CheckSeatedPlayerObstacle()) return;

        // Detectar superficie del agua para el sistema de flotación (FixedUpdate)
        if (FindWaterSurface(out float waterY))
        {
            _waterSurfaceY = waterY;
            _waterFound    = true;
        }

        // Corriente activa
        _activeCurrentZone = null;
        foreach (WaterCurrentZone zone in WaterCurrentZone.All)
        {
            if (zone.Contains(transform.position)) { _activeCurrentZone = zone; break; }
        }

        // Endpoint
        if (endPoint != null && !_exhausted)
        {
            float endZ = endPoint.position.z;
            if (_prevZ < endZ && transform.position.z >= endZ)
            {
                TriggerEndPoint();
                return;
            }
        }
        _prevZ = transform.position.z;

        // Salto voluntario
        if (_player != null && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            HandleJumpOff();
            return;
        }

        HandleKayakInput();
    }

    private void HandleKayakInput()
    {
        if (Mouse.current == null) return;
        if (BeatManager.Instance == null || !BeatManager.Instance.IsPlaying) return;

        bool leftClick  = Mouse.current.leftButton.wasPressedThisFrame;
        bool rightClick = Mouse.current.rightButton.wasPressedThisFrame;
        if (!leftClick && !rightClick) return;

        TimingResult timing = BeatManager.Instance.EvaluateTiming();

        if (timing != TimingResult.Miss)
        {
            int beatIdx = BeatManager.Instance.CurrentBeatIndex;
            if (beatIdx == _lastHitBeatIdx)
                timing = TimingResult.Miss;
            else
                _lastHitBeatIdx = beatIdx;
        }

        // El lado hacia el que rema lo decide el BOTÓN. Por defecto se rema aunque se falle el beat
        // (dirigir libre); si paddleRequiresBeat está activo, SOLO se rema al acertar (como las
        // acciones a pie con requiresBeat).
        float newDir   = leftClick ? +1f : -1f;
        bool  isHit    = timing != TimingResult.Miss;
        bool  doPaddle = isHit || !paddleRequiresBeat;

        if (doPaddle)
        {
            _paddleDir   = newDir;
            _paddleTimer = paddleDuration;
            PlayPaddleSound();
        }

        RunStats.HeartPress(timing);   // (contratos: cada remada es un latido)
        StudyTelemetry.Click(BeatManager.Instance.ClickOffset, timing);   // (modo prueba)

        if (isHit)
        {
            // ACIERTO: combo (sube el tope de velocidad) + energía normal.
            // El combo se reinicia al cambiar de lado o si pasó demasiado tiempo desde la última.
            bool sameDir  = Mathf.Approximately(newDir, _comboDir);
            bool timedOut = Time.time - _lastComboHitTime > steerComboResetTime;
            if (!sameDir || timedOut) _comboCount = 0;

            _comboDir         = newDir;
            _comboCount++;
            _lastComboHitTime = Time.time;

            // Cada steerComboHitsPerStep remadas = +steerComboSpeedStep m/s al tope,
            // hasta steerMaxLateralSpeed.
            int steps = _comboCount / Mathf.Max(1, steerComboHitsPerStep);
            _steerCap = Mathf.Min(steerLateralSpeed + steps * steerComboSpeedStep,
                                  steerMaxLateralSpeed);

            EnergyManager.Instance?.HitRed(timing);
        }
        else
        {
            // FALLO: rompe el combo (velocidad base, sin boost) y quita MÁS energía que un fallo
            // a pie (kayakMissPenaltyScale). El remo ya se ha aplicado arriba solo si procedía
            // (modo libre); en modo paddleRequiresBeat el kayak no se mueve con el fallo.
            _comboCount = 0;
            _steerCap   = steerLateralSpeed;
            EnergyManager.Instance?.MissRed(kayakMissPenaltyScale);
        }
    }

    // ── Salto voluntario desde el kayak ──────────────────────────────────────

    private void HandleJumpOff()
    {
        if (_player == null) return;
        _player.ExitKayakWithPlayerJump(_forwardDir * forwardSpeed);

        _prevPlayerY       = _player.transform.position.y;
        _player = null;

        // Ataúd vacío → plataforma CINEMÁTICA conducida en FixedUpdate con MovePosition/
        // MoveRotation. Con interpolación activa, se mueve perfectamente fluido respecto a
        // la cámara del jugador (que va por su cuenta al saltar). No tocamos el transform
        // en Update para no romper la interpolación.
        _rb.isKinematic    = true;

        _playerJumpedTimer = 0f;
        _state             = KayakState.PlayerJumped;
    }

    private void UpdatePlayerJumped()
    {
        _playerJumpedTimer += Time.deltaTime;
        _swayTimer         += Time.deltaTime;

        if (FindWaterSurface(out float waterY))
        {
            _waterSurfaceY = waterY;
            _waterFound    = true;
        }

        // Auto-alineación suave de la orientación Y hacia la dirección de diseño
        _initialYRot = Mathf.LerpAngle(_initialYRot, _designedYRot, jumpAlignSpeed * Time.deltaTime);

        // Plataforma sólida: en cuanto los pies del jugador superan el techo del ataúd,
        // activamos la colisión física para que al caer aterrice ENCIMA (como plataforma)
        // en vez de atravesarlo y hundirse. No se activa antes para evitar el "pop" de
        // despenetración mientras el jugador aún solapa el asiento al saltar.
        if (!_platformSolid && _playerCC != null && _playerCC.enabled)
        {
            float playerFeetY = _playerCC.bounds.min.y;
            if (playerFeetY > KayakTopY() + 0.02f)
                SetKayakSolidForPlayer(true);
        }

        // Endpoint
        if (endPoint != null && !_exhausted)
        {
            float endZ = endPoint.position.z;
            if (_prevZ < endZ && transform.position.z >= endZ)
            {
                _exhausted   = true;
                forwardSpeed = 0f;
                SetKayakSolidForPlayer(false);   // el jugador se va: restaurar ignore
            }
        }
        _prevZ = transform.position.z;

        // Reembarcarse
        float playerY   = _cachedPlayer != null ? _cachedPlayer.transform.position.y : 0f;
        bool  descending = playerY <= _prevPlayerY + 0.001f;   // no está subiendo en el salto
        _prevPlayerY = playerY;

        if (!_exhausted && _playerJumpedTimer >= ReboardCooldown && _cachedPlayer != null)
        {
            // Proximidad horizontal en ESPACIO MUNDO usando la huella (XZ) real de los
            // colliders del ataúd. No usamos InverseTransformPoint porque el ataúd está
            // tumbado 90° y su pivote está descentrado respecto al cuerpo, así que la
            // caja local no cubría donde el jugador aterriza de verdad.
            Bounds kb = KayakBounds();
            float horizMargin = 1.5f;
            bool inFootprint =
                Mathf.Abs(_cachedPlayer.transform.position.x - kb.center.x) <= kb.extents.x + horizMargin &&
                Mathf.Abs(_cachedPlayer.transform.position.z - kb.center.z) <= kb.extents.z + horizMargin;

            // Dos vías para detectar que el jugador ha vuelto al ataúd:
            //  (a) apoyado sobre la plataforma sólida (pies a la altura del techo).
            //  (b) fallback robusto: ha bajado cerca del asiento y NO está subiendo.
            bool onPlatform = _platformSolid && _playerCC != null && _playerCC.enabled
                              && _playerCC.bounds.min.y <= KayakTopY() + 0.3f;
            float seatY    = transform.position.y + seatHeightOffset;
            bool  nearSeat = descending && playerY <= seatY + 1.6f;

            if (debugCollisions)
                Debug.Log($"[Kayak reboard] inFootprint={inFootprint} onPlatform={onPlatform} nearSeat={nearSeat} " +
                          $"solid={_platformSolid} playerY={playerY:F2} feetY={(_playerCC != null ? _playerCC.bounds.min.y : 0f):F2} " +
                          $"kayakTop={KayakTopY():F2} seatY={seatY:F2}", this);

            if (inFootprint && (onPlatform || nearSeat))
                ReBoardPlayer();
        }
    }

    private void ReBoardPlayer()
    {
        // Restaurar la física dinámica (boyancia) para el viaje sentado.
        _rb.isKinematic = false;

        // El jugador vuelve al asiento: restaurar el ignore CC↔kayak (deja de ser
        // plataforma sólida) para que no colisione con el ataúd estando sentado.
        SetKayakSolidForPlayer(false);

        // Guardar el desfase actual jugador→asiento para cerrarlo suavemente
        // (SyncPlayerToSeat lo interpola a 0) en vez de teletransportar al jugador.
        Vector3 seatNow = transform.position + Vector3.up * seatHeightOffset;
        _reboardOffset     = _cachedPlayer.transform.position - seatNow;
        _reboardBlendTimer = reboardSmoothTime;

        _player = _cachedPlayer;
        _player.EnterKayak();
        PlaySound(woodSound);

        // Cancelar velocidad lateral al reembarcar
        float lat = Vector3.Dot(_rb.linearVelocity, _rightDir);
        if (Mathf.Abs(lat) > 0.001f)
            _rb.AddForce(-_rightDir * lat, ForceMode.VelocityChange);

        _lastHitBeatIdx     = -1;
        _lastPaddleSoundIdx = -1;
        _prevZ              = transform.position.z;
        _comboDir           = 0f;
        _comboCount         = 0;
        _steerCap           = steerLateralSpeed;
        _state              = KayakState.Floating;
    }

    // ── Rotación visual ───────────────────────────────────────────────────────

    private void ApplyRotation()
    {
        if (startHorizontal)
        {
            // Kayak tumbado por diseño: partimos de la orientación colocada y solo
            // añadimos el yaw de giro y el sway de flotación (sin el tilt de 90°).
            float yawH = (_state == KayakState.Floating || _state == KayakState.PlayerJumped)
                ? Mathf.Clamp(_lateralVelocity * yawDegreesPerMPS, -maxYawDegrees, maxYawDegrees)
                : 0f;

            float rollH  = Mathf.Sin(_swayTimer * swayFrequency * Mathf.PI * 2f) * swayAmplitude;
            float pitchH = Mathf.Sin(_swayTimer * swayFrequency * Mathf.PI * 2f * 0.6f + 1.1f) * pitchAmplitude;
            float bankH  = Mathf.Clamp(-_lateralVelocity * bankDegreesPerMPS, -maxBankDegrees, maxBankDegrees);

            Quaternion rollQh  = Quaternion.AngleAxis(rollH + bankH, _forwardDir);
            Quaternion pitchQh = Quaternion.AngleAxis(pitchH, _rightDir);
            transform.rotation = (rollQh * pitchQh)
                               * Quaternion.AngleAxis(yawH, Vector3.up)
                               * _baseFlatRotation;
            return;
        }

        float yawOffset = (_state == KayakState.Floating || _state == KayakState.PlayerJumped)
            ? Mathf.Clamp(_lateralVelocity * yawDegreesPerMPS, -maxYawDegrees, maxYawDegrees)
            : 0f;

        float easedTilt   = EaseOutExpo(_tiltProgress);
        Quaternion steerQ = Quaternion.Euler(0f, _initialYRot + yawOffset, 0f);
        Quaternion tiltQ  = Quaternion.AngleAxis(easedTilt * tiltAngle, tiltAxis.normalized);
        Quaternion baseQ  = steerQ * tiltQ;

        Quaternion overlayQ = Quaternion.identity;

        // Durante la caída NO hay bamboleo: el ataúd cae limpio en parábola.
        if (_state == KayakState.Floating || _state == KayakState.PlayerJumped)
        {
            float roll  = Mathf.Sin(_swayTimer * swayFrequency * Mathf.PI * 2f) * swayAmplitude;
            float pitch = Mathf.Sin(_swayTimer * swayFrequency * Mathf.PI * 2f * 0.6f + 1.1f)
                        * pitchAmplitude;
            float bank  = Mathf.Clamp(-_lateralVelocity * bankDegreesPerMPS,
                                      -maxBankDegrees, maxBankDegrees);

            // Bamboleo de IMPACTO: solo tras tocar el agua. _swayTimer se resetea a 0
            // al aterrizar, así que el término decae desde el máximo hasta ~0 en ~1.5 s
            // y no reaparece en el resto de la flotación.
            float impactDecay  = Mathf.Exp(-_swayTimer * 2.5f);
            float impactWobble = Mathf.Sin(_swayTimer * fallWobbleFrequency * Mathf.PI * 2f)
                               * fallWobbleAmplitude * impactDecay;

            Quaternion rollQ  = Quaternion.AngleAxis(roll + bank, _forwardDir);
            Quaternion pitchQ = Quaternion.AngleAxis(pitch + impactWobble, _rightDir);
            overlayQ = rollQ * pitchQ;
        }

        transform.rotation = overlayQ * baseQ;
    }

    // ── Colisión con obstáculos ───────────────────────────────────────────────

    /// <summary>
    /// Se llama cada FixedUpdate mientras el kayak toca un collider. Distingue entre:
    ///   • EMBESTIDA FRONTAL: la proa (tramo delantero) toca una superficie que la
    ///     encara de frente → choque letal (game over si el jugador va dentro,
    ///     rotura + sonido si ya saltó).
    ///   • ROCE LATERAL: cualquier otro contacto → no se hace nada; el muro sólido
    ///     (con material sin fricción) ya bloquea el avance lateral y deja resbalar
    ///     el kayak hacia delante.
    ///
    /// Se agregan TODOS los contactos porque un único contacto (collision.contacts[0])
    /// es ruidoso: durante un roce lateral su normal puede apuntar un frame hacia
    /// "Front" o "Bottom" y disparar un game over falso. Ese era el bug.
    /// </summary>
    private void OnCollisionStay(Collision collision)
    {
        if (_rewindFrozen || TimeRewinder.IsRewinding) return;
        if (_crashed) return;
        if (_state == KayakState.Vertical) return;          // aún es prop / ya roto
        if (_obstacleLayer == -1) return;
        if (collision.gameObject.layer != _obstacleLayer) return;

        int count = collision.contactCount;
        if (count == 0) return;

        // Caras letales del obstáculo (fuente única: ObstacleInfo).
        ObstacleInfo info  = collision.collider.GetComponentInParent<ObstacleInfo>();
        LethalFaces lethal = info != null ? info.lethalFaces : (LethalFaces.Front | LethalFaces.Bottom);

        // Localizar la proa: punto más adelantado del kayak sobre su eje de avance.
        float noseProj = KayakForwardSpan(out float lengthFwd);
        float noseZone = Mathf.Max(0.5f, lengthFwd * noseZoneFraction);

        // Agregar todos los contactos (robusto frente a normales ruidosas puntuales).
        Vector3 sumNormal = Vector3.zero;
        bool    noseRam   = false;

        for (int i = 0; i < count; i++)
        {
            ContactPoint cp = collision.GetContact(i);
            sumNormal += cp.normal;

            float pointProj = Vector3.Dot(cp.point, _forwardDir);   // ¿en la proa?
            float normalFwd = Vector3.Dot(cp.normal, _forwardDir);  // ¿encara de frente?

            // Contacto en el tramo de proa Y superficie que mira al kayak de frente
            // (normalFwd < -0.5 ≈ dentro de 60° de la oposición directa al avance).
            bool thisNose = pointProj > noseProj - noseZone && normalFwd < -0.5f;
            if (thisNose) noseRam = true;

            if (debugCollisions)
                Debug.DrawRay(cp.point, cp.normal * 1.5f, thisNose ? Color.red : Color.green, 1.5f);
        }

        LethalFaces face = ClassifyNormal((sumNormal / count).normalized);

        // Choque letal si:
        //   • Frontal: la proa embiste de frente Y el obstáculo marca Front como letal.
        //   • Otras caras (Bottom/Top/Back): por la normal media, ya estable al promediar.
        bool frontalLethal = noseRam && (lethal & LethalFaces.Front) != 0;
        bool otherLethal   = (lethal & face & (LethalFaces.Bottom | LethalFaces.Top | LethalFaces.Back)) != 0;

        if (debugCollisions)
        {
            string outcome = frontalLethal ? "FRONTAL LETAL"
                           : otherLethal   ? "OTRA LETAL"
                                           : "roce (ignora)";
            Debug.Log($"[Kayak] '{collision.gameObject.name}' contactos={count} caraMedia={face} " +
                      $"noseRam={noseRam} lethalCfg=({lethal}) -> {outcome}", this);
        }

        if (frontalLethal || otherLethal)
        {
            // REBOBINADO (tienda): con el jugador dentro, el tiempo (y el kayak) vuelve 4 beats atrás.
            if (_player != null && PlayerShield.Count > 0 && TimeRewinder.Instance != null &&
                TimeRewinder.Instance.TryRewind()) return;
            CrashKayak();
        }

        // Si no es letal (roce lateral / cara no letal): no hacemos nada.
        // La pared sólida bloquea el movimiento hacia ella y el kayak sigue de frente.
    }

    /// <summary>
    /// Rompe el kayak tras un choque letal. Si el jugador va dentro → Game Over.
    /// Si el jugador ya había saltado (kayak vacío) → sólo se rompe con sonido.
    /// </summary>
    private void CrashKayak()
    {
        _crashed = true;
        PlaySound(breakSound);

        // Solo se puede tocar la velocidad en cuerpos NO cinemáticos. Durante PlayerJumped el
        // ataúd va como plataforma kinematic (el jugador ya saltó, _player == null): ahí no hay
        // velocidad física que anular y hacerlo lanza "Setting velocity of a kinematic body...".
        if (!_rb.isKinematic)
        {
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        if (_player != null)
        {
            // Jugador dentro → muere.
            gameOverManager?.TriggerGameOver();
        }
        else
        {
            // Jugador fuera → el ataúd vacío sólo se rompe: se detiene y queda inerte.
            SetKayakSolidForPlayer(false);   // restaurar ignore (deja de ser plataforma)
            _exhausted      = true;
            forwardSpeed    = 0f;
            _rb.isKinematic = true;
            _state          = KayakState.Vertical;
        }
    }

    /// <summary>
    /// Comprueba si el CUERPO del jugador sentado toca un obstáculo. Su CharacterController
    /// está desactivado al ir sentado, así que reconstruimos su cápsula en el asiento y
    /// hacemos un overlap manual contra la capa de obstáculos. Sirve para obstáculos
    /// elevados que el ataúd esquiva por debajo pero que golpean al jugador si no salta.
    /// Devuelve true si se ha disparado el game over.
    /// </summary>
    private bool CheckSeatedPlayerObstacle()
    {
        if (_crashed || _player == null || _obstacleLayer == -1 || _playerCC == null)
            return false;

        // Cápsula del jugador reconstruida en el asiento (mismo tamaño que su CC).
        Vector3 seatPos = transform.position + Vector3.up * seatHeightOffset;
        Vector3 scale   = _cachedPlayer.transform.lossyScale;
        float   scaleY  = scale.y;
        float   scaleR  = Mathf.Max(scale.x, scale.z);
        float   h       = _playerCC.height * scaleY;
        float   r       = _playerCC.radius * scaleR;
        Vector3 center  = seatPos + Vector3.up * (_playerCC.center.y * scaleY);
        float   half    = Mathf.Max(0f, h * 0.5f - r);
        Vector3 p1      = center + Vector3.up * half;
        Vector3 p2      = center - Vector3.up * half;

        if (Physics.CheckCapsule(p1, p2, r, 1 << _obstacleLayer, QueryTriggerInteraction.Ignore))
        {
            CrashKayak();   // jugador dentro → game over
            return true;
        }
        return false;
    }

    /// <summary>
    /// Proyección (mundo) del punto más adelantado del kayak sobre su eje de avance
    /// <see cref="_forwardDir"/>, y su eslora total a lo largo de ese eje. Con esto
    /// sabemos si un contacto ocurre en la "proa" sin depender de dónde esté el pivote.
    /// </summary>
    private float KayakForwardSpan(out float lengthAlongForward)
    {
        float maxP = float.MinValue, minP = float.MaxValue;

        if (_kayakColliders != null)
        {
            foreach (Collider col in _kayakColliders)
            {
                if (col == null) continue;
                Bounds b = col.bounds;
                Vector3 c = b.center, e = b.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = c + new Vector3(e.x * sx, e.y * sy, e.z * sz);
                    float p = Vector3.Dot(corner, _forwardDir);
                    if (p > maxP) maxP = p;
                    if (p < minP) minP = p;
                }
            }
        }

        if (maxP < minP)   // sin colliders válidos: usar el pivote
        {
            lengthAlongForward = 0f;
            return Vector3.Dot(transform.position, _forwardDir);
        }

        lengthAlongForward = maxP - minP;
        return maxP;
    }

    private LethalFaces ClassifyNormal(Vector3 worldNormal)
    {
        float dotY   = worldNormal.y;
        float dotFwd = Vector3.Dot(worldNormal, _forwardDir);
        float dotRt  = Vector3.Dot(worldNormal, _rightDir);

        float absY   = Mathf.Abs(dotY);
        float absFwd = Mathf.Abs(dotFwd);
        float absRt  = Mathf.Abs(dotRt);

        if (absY >= absFwd && absY >= absRt)
            return dotY > 0f ? LethalFaces.Top : LethalFaces.Bottom;
        if (absFwd >= absY && absFwd >= absRt)
            return dotFwd < 0f ? LethalFaces.Front : LethalFaces.Back;
        return dotRt > 0f ? LethalFaces.Right : LethalFaces.Left;
    }

    // ── Agua ──────────────────────────────────────────────────────────────────

    private bool FindWaterSurface(out float surfaceY)
    {
        surfaceY = 0f;
        if (waterLayerMask == 0) return false;
        Vector3 origin = transform.position + Vector3.up * 3f;
        // IGNORAR TRIGGERS: el kayak flota sobre el agua SÓLIDA (WaterBlock); cualquier trigger que esté en el
        // layer Water no debe contar como superficie.
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 50f, waterLayerMask,
                            QueryTriggerInteraction.Ignore))
        {
            surfaceY = hit.point.y;
            return true;
        }
        return false;
    }

    private bool HasWaterBelow(Vector3 worldPos)
    {
        if (waterLayerMask == 0) return true;
        // Ignorar triggers (mismo motivo que FindWaterSurface): solo cuenta el agua sólida.
        return Physics.Raycast(worldPos + Vector3.up * 3f, Vector3.down, 50f, waterLayerMask,
                               QueryTriggerInteraction.Ignore);
    }

    // ── Sincronización jugador ────────────────────────────────────────────────

    private void SyncPlayerToSeat()
    {
        Vector3 seatPos = transform.position + Vector3.up * seatHeightOffset;

        // Reembarque suave: el desfase inicial (jugador→asiento) decae a 0 durante
        // reboardSmoothTime, así el jugador se asienta sin teletransportarse. El
        // desfase se añade sobre el asiento ACTUAL, de modo que acompaña al ataúd.
        if (_reboardBlendTimer > 0f)
        {
            _reboardBlendTimer -= Time.deltaTime;
            float frac = reboardSmoothTime > 0f
                ? Mathf.Clamp01(_reboardBlendTimer / reboardSmoothTime)
                : 0f;
            frac = frac * frac * (3f - 2f * frac);   // smoothstep
            seatPos += _reboardOffset * frac;
        }

        // Solo fijamos la POSICIÓN del asiento. La ROTACIÓN (hacia dónde mira el jugador) la
        // controla siempre PlayerMovement.HandleLook: así el jugador mira libremente mientras
        // va en el kayak y, al saltar, la cámara conserva su orientación sin pegar un giro brusco.
        _player.transform.position = seatPos;
    }

    // ── Rebobinado (tienda) ───────────────────────────────────────────────────

    /// <summary>¿Va el jugador sentado en este kayak, flotando (estado seguro para rebobinar)?</summary>
    public bool IsSeatedFloating => _player != null && _state == KayakState.Floating && !_crashed;

    private bool _rewindFrozen, _rbWasKinematic;

    /// <summary>Congela el kayak (sin física ni lógica) mientras TimeRewinder lo lleva hacia atrás.</summary>
    public void BeginRewind()
    {
        if (_rewindFrozen) return;
        _rewindFrozen   = true;
        _rbWasKinematic = _rb.isKinematic;
        if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
        _rb.isKinematic = true;
    }

    public void ApplyRewindPose(Vector3 pos, Quaternion rot)
    {
        _rb.position = pos;
        _rb.rotation = rot;
        transform.SetPositionAndRotation(pos, rot);
    }

    private Vector3    _startPos;
    private Quaternion _startRot;

    /// <summary>Segundo de la canción en que el jugador se montó por primera vez (-1 = aún no).</summary>
    public float ActivatedSongTime { get; private set; } = -1f;
    public bool  HasBeenActivated  => ActivatedSongTime >= 0f;
    public Vector3    StartPosition => _startPos;
    public Quaternion StartRotation => _startRot;

    /// <summary>
    /// REBOBINADO a un momento ANTERIOR a montarse: el ataúd vuelve a su sitio y estado de diseño (sin jugador, sin
    /// física), listo para volver a activarse cuando el jugador llegue. Si el jugador iba montado, se baja.
    /// </summary>
    public void ResetToStart()
    {
        if (_player != null) { _player.ExitKayak(); _player = null; }
        _rewindFrozen = false;
        _crashed      = false;
        _exhausted    = false;
        _state        = KayakState.Vertical;
        _tiltProgress = 0f;
        _fallTimer    = 0f;
        _swayTimer    = 0f;
        _playerJumpedTimer = 0f;
        _lateralVelocity   = 0f;
        _initialYRot  = _designedYRot;
        ActivatedSongTime = -1f;

        _rb.isKinematic = false;
        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
        _rb.position = _startPos;
        _rb.rotation = _startRot;
        transform.SetPositionAndRotation(_startPos, _startRot);
        _prevZ = transform.position.z;

        SetKayakSolidForPlayer(false);
        IgnorePlayerCollisions(true);
        foreach (Collider c in _kayakColliders) if (c != null) c.enabled = true;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = true;
    }

    /// <summary>Fin del rebobinado: vuelve la física, sin velocidad, y sigue remando desde ahí.</summary>
    public void EndRewind()
    {
        if (!_rewindFrozen) return;
        _rb.isKinematic = _rbWasKinematic;
        if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
        _lateralVelocity = 0f;
        _rewindFrozen    = false;
    }

    // ── Endpoint ──────────────────────────────────────────────────────────────

    private void TriggerEndPoint()
    {
        _exhausted = true;
        _prevZ     = float.MaxValue;

        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic     = true;   // el ataúd vacío queda como prop estático

        _state = KayakState.Vertical;

        PlaySound(breakSound);

        forwardSpeed = 0f;

        if (_player != null)
        {
            Vector3 horizontalImpulse = _forwardDir * forwardSpeed;
            _player.ExitKayakWithJump(exitJumpVelocity, horizontalImpulse);
            _player = null;
        }
    }

    /// <summary>
    /// El ataúd SE ROMPE (fin del jefe): lanza al jugador hacia arriba con 'jumpVelocity' (sale corriendo hacia
    /// delante a su velocidad normal), esconde el ataúd, suelta astillas que saltan y lo destruye al rato.
    /// </summary>
    public void BreakApart(float jumpVelocity, int shards = 10)
    {
        if (_exhausted) return;
        _exhausted = true;
        _prevZ     = float.MaxValue;

        if (_rb != null)
        {
            if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            _rb.isKinematic = true;
        }
        _state = KayakState.Vertical;
        PlaySound(breakSound);

        if (_player != null)
        {
            _player.ExitKayakWithJump(jumpVelocity, Vector3.zero);
            _player = null;
        }

        // Astillas: trozos del tamaño del ataúd que salen despedidos.
        Bounds b = new Bounds(transform.position, Vector3.one);
        bool any = false;
        Material mat = null;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            if (mat == null) mat = r.sharedMaterial;
            r.enabled = false;
        }
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        for (int i = 0; i < shards; i++)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.name = "CoffinShard";
            Destroy(s.GetComponent<Collider>());   // sin colisión: solo visual
            if (mat != null) s.GetComponent<Renderer>().sharedMaterial = mat;
            s.transform.position = b.center + Vector3.Scale(Random.insideUnitSphere, b.extents);
            s.transform.rotation = Random.rotation;
            s.transform.localScale = new Vector3(Random.Range(0.15f, 0.35f), Random.Range(0.1f, 0.25f),
                                                 Random.Range(0.6f, 1.4f)) * Mathf.Max(1f, b.size.magnitude * 0.25f);
            var rb = s.AddComponent<Rigidbody>();
            rb.linearVelocity  = (s.transform.position - b.center).normalized * Random.Range(4f, 9f)
                               + Vector3.up * Random.Range(6f, 12f);
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            Destroy(s, 3f);
        }
        Destroy(gameObject, 3.5f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void IgnorePlayerCollisions(bool ignore)
    {
        if (_cachedPlayer == null || _kayakColliders == null) return;
        CharacterController cc = _cachedPlayer.GetComponent<CharacterController>();
        if (cc == null) return;
        foreach (Collider col in _kayakColliders)
            Physics.IgnoreCollision(cc, col, ignore);
    }

    /// <summary>
    /// Activa/desactiva la colisión física entre el CC del jugador y el ataúd.
    /// solid=true  → colisionan (el ataúd es una plataforma sólida sobre la que caer).
    /// solid=false → se ignoran (el jugador va sentado y lo posiciona SyncPlayerToSeat).
    /// </summary>
    private void SetKayakSolidForPlayer(bool solid)
    {
        if (_platformSolid == solid) return;
        _platformSolid = solid;
        if (_playerCC == null || _kayakColliders == null) return;
        foreach (Collider col in _kayakColliders)
            if (col != null) Physics.IgnoreCollision(_playerCC, col, !solid);
    }

    /// <summary>Y (mundo) del punto más alto de los colliders del ataúd (su "techo").</summary>
    private float KayakTopY()
    {
        float maxY = float.MinValue;
        if (_kayakColliders != null)
            foreach (Collider col in _kayakColliders)
                if (col != null) maxY = Mathf.Max(maxY, col.bounds.max.y);
        return maxY > float.MinValue ? maxY : transform.position.y;
    }

    /// <summary>Bounds (mundo) combinados de todos los colliders del ataúd.</summary>
    // ── Protección contra la "muerte por agua" al caer encima del kayak ────────

    private static readonly System.Collections.Generic.List<KayakController> s_all =
        new System.Collections.Generic.List<KayakController>();
    private void OnEnable()  { if (!s_all.Contains(this)) s_all.Add(this); }
    private void OnDisable() { s_all.Remove(this); }

    /// <summary>
    /// ¿Hay un kayak (ya tumbado en el agua y sin agotar) justo debajo de 'pos' que pueda recoger al jugador?
    /// GameOverManager lo usa para NO matar por agua a quien está cayendo / aterrizando ENCIMA del kayak: como el
    /// kayak flota a ras del agua, los pies del jugador quedaban "a la altura del agua" y moría antes de que el
    /// kayak llegara a montarlo.
    /// </summary>
    public static bool IsKayakUnder(Vector3 pos, float margin = 0.8f)
    {
        foreach (KayakController k in s_all)
        {
            if (k == null || k._exhausted) continue;
            Bounds b = k.KayakBounds();
            if (b.size.sqrMagnitude < 0.0001f) continue;
            if (Mathf.Abs(pos.x - b.center.x) <= b.extents.x + margin &&
                Mathf.Abs(pos.z - b.center.z) <= b.extents.z + margin)
                return true;
        }
        return false;
    }

    private Bounds KayakBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool found = false;
        if (_kayakColliders != null)
            foreach (Collider col in _kayakColliders)
            {
                if (col == null) continue;
                if (!found) { b = col.bounds; found = true; }
                else          b.Encapsulate(col.bounds);
            }
        return b;
    }

    private static float EaseOutExpo(float t)
        => Mathf.Approximately(t, 1f) ? 1f : 1f - Mathf.Pow(2f, -10f * t);

    // ── Audio ─────────────────────────────────────────────────────────────────

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;
        if (audioSource != null)
            audioSource.PlayOneShot(clip);
        else
            AudioSource.PlayClipAtPoint(clip, transform.position);
    }

    private void PlayPaddleSound()
    {
        if (paddleSounds == null || paddleSounds.Length == 0) return;

        int idx;
        if (paddleSounds.Length == 1)
        {
            idx = 0;
        }
        else
        {
            do { idx = Random.Range(0, paddleSounds.Length); }
            while (idx == _lastPaddleSoundIdx);
        }

        _lastPaddleSoundIdx = idx;
        PlaySound(paddleSounds[idx]);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    public KayakState State      => _state;

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Caja de activación (espacio local del ataúd)
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color  = new Color(1f, 0.85f, 0.1f, 0.12f);
        Gizmos.DrawCube(Vector3.zero, activationBoxSize);
        Gizmos.color  = new Color(1f, 0.85f, 0.1f, 0.85f);
        Gizmos.DrawWireCube(Vector3.zero, activationBoxSize);
        Gizmos.matrix = Matrix4x4.identity;

        // Asiento
        Vector3 seatWorldPos = transform.position + Vector3.up * seatHeightOffset;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(seatWorldPos, 0.25f);
        Gizmos.DrawLine(transform.position, seatWorldPos);

        // Ejes (solo en play)
        if (Application.isPlaying && _state != KayakState.Vertical)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, _forwardDir * 3f);
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawRay(transform.position, _rightDir * 1.5f);
            Gizmos.DrawRay(transform.position, -_rightDir * 1.5f);
        }

        // Endpoint
        if (endPoint != null)
        {
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.9f);
            float half   = 5f;
            Vector3 ep   = endPoint.position;
            Gizmos.DrawLine(ep + Vector3.left * half, ep + Vector3.right * half);
            Gizmos.DrawLine(ep + Vector3.down * half, ep + Vector3.up    * half);
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.3f);
            Gizmos.DrawLine(transform.position, ep);
        }

        // Raycast al agua
        Gizmos.color = new Color(0f, 0.4f, 1f, 0.35f);
        Gizmos.DrawLine(transform.position + Vector3.up * 3f,
                        transform.position + Vector3.down * 15f);

#if UNITY_EDITOR
        var style = new GUIStyle
        {
            normal    = { textColor = new Color(0.2f, 0.9f, 1f) },
            fontStyle = UnityEngine.FontStyle.Bold,
            fontSize  = 11
        };
        string lateralInfo = Application.isPlaying && (_state == KayakState.Floating || _state == KayakState.PlayerJumped)
            ? $"  lateral {_lateralVelocity:F1} m/s"
            : "";
        string currentInfo = Application.isPlaying && _activeCurrentZone != null
            ? $"  corriente {_activeCurrentZone.direction} {_activeCurrentZone.strength:F1} m/s"
            : "";
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 1.8f,
            $"KAYAK [{_state}]  {forwardSpeed} m/s fwd{lateralInfo}{currentInfo}", style);
#endif
    }
#endif
}
