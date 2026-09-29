using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Ataque de TRIDENTE del jefe (Ataque B), con SISTEMA DE DAÑO PROPIO (sin física ni triggers).
///
/// Ciclo:
///   • REPOSO: el tridente FLOTA vertical al lado del jefe, con bob (sube/baja) y giro lento.
///   • COGER (wind-up): el jefe lo agarra — la mano se desliza al mango — y lo lleva hacia delante (-Z)
///     girándolo de vertical a HORIZONTAL (puño por delante, listo para barrer).
///   • SWING: barre lateralmente (arco) justo por donde está el jugador, a la altura elegida.
///   • VOLVER: regresa a flotar vertical al lado del jefe y la mano vuelve a su sitio.
///   • LANZAMIENTO (Fase 3, Throw): mismo wind-up, arco y velocidad, pero lo SUELTA: vuela solo por el arco
///     girando sobre sí mismo y la OTRA mano lo coge al final y lo deja flotando en su sitio.
///
/// DAÑO: por VOLUMEN BARRIDO (caja de las puntas del frame anterior + la actual) contra el jugador,
/// solo durante el swing, así no hay tunneling. Al tocar → game over.
///
/// La esquiva es geométrica (no depende de timings):
///   • Swing BAJO  → pasa a ras de suelo: SALTAS por encima.
///   • Swing ALTO  → pasa a la altura de la cabeza: te AGACHAS/SLIDE por debajo.
///
/// NOTA de ejes: delante = -Z. Ajusta Attack Rotation Euler para dejar el tridente horizontal con las
/// puntas hacia -Z, y Attack Offset para centrar el swing justo donde pasa el jugador.
///
/// SETUP:
///   1. Componente en el TRIDENTE (o Tail Pivot = su transform). En reposo, colócalo vertical al lado del jefe.
///   2. Hit Volume = Renderer de las puntas.
///   3. Grip Hand = puño derecho. Grip Local On Object = punto del asta donde agarra la mano.
///   4. Asígnalo en BossController (campo del barrido).
/// </summary>
[AddComponentMenu("Boss/Boss Sweep Attack")]
public class BossTailSweep : MonoBehaviour
{
    [Header("Visual")]
    [Tooltip("Transform del tridente que se mueve. Vacío = este objeto.")]
    [SerializeField] private Transform _tailPivot;

    [Header("Flotación en reposo")]
    [Tooltip("Amplitud del bob (metros que sube/baja al flotar).")]
    [SerializeField] private float _floatBobAmplitude = 0.5f;
    [Tooltip("Beats que tarda un ciclo completo de bob.")]
    [SerializeField] private float _floatBobBeats = 2f;
    [Tooltip("Giro CONTINUO en reposo alrededor del eje vertical Y (grados/segundo). 0 = quieto.")]
    [SerializeField] private float _floatSpinDegPerSec = 30f;
    [Tooltip("Bamboleo suave (grados) en los ejes X y Z al flotar. Muy poco, para que no parezca rígido.")]
    [SerializeField] private float _idleTiltAmplitude = 4f;
    [Tooltip("Beats por ciclo del bamboleo X/Z.")]
    [SerializeField] private float _idleTiltBeats = 3f;

    [Header("Ataque (hachazo pivotando)")]
    [Tooltip("Punto de pivote del swing en espacio LOCAL del padre (el 'hombro'). El tridente gira\n" +
             "alrededor de este punto como un hachazo lateral. (0,0,0) = origen del jefe.")]
    [SerializeField] private Vector3 _shoulderLocalPos = Vector3.zero;
    [Tooltip("Cuánto SALE el tridente/puño hacia delante (-Z) desde el hombro. Súbelo para sacar más el puño.")]
    [SerializeField] private float _reachForward = 15f;
    [Tooltip("Rotación LOCAL ABSOLUTA del tridente durante el ataque (horizontal, puntas hacia delante -Z).\n" +
             "Con este modelo (puntas = +Z local), (0,180,0) las deja apuntando a -Z. Ajusta si hiciera falta.")]
    [SerializeField] private Vector3 _attackEuler = new Vector3(0f, 180f, 0f);
    [Tooltip("Medio arco del hachazo (grados a cada lado del hombro). El swing va de +este a −este.")]
    [SerializeField] private float _swingHalfArcDeg = 70f;
    [Tooltip("Giro extra en el eje Z (global) del tridente cuando el swing es ALTO.")]
    [SerializeField] private float _highRollDeg = 90f;
    [Tooltip("Duración del swing en beats (centrado en el contacto). Súbelo para que vaya más lento.")]
    [SerializeField] private float _sweepBeats = 1f;
    [Tooltip("OFFSET de altura (sobre el hombro) en swing ALTO (pasa alto → agacharse). Positivo = sube.")]
    [SerializeField] private float _highLocalY = 2.2f;
    [Tooltip("OFFSET de altura (sobre el hombro) en swing BAJO (pasa bajo → salto).")]
    [SerializeField] private float _lowLocalY = 0.4f;
    [Tooltip("Beats que tarda en volver a flotar tras el swing.")]
    [SerializeField] private float _recoverBeats = 1f;

    [Header("Anticipación (aviso justo antes de barrer)")]
    [Tooltip("Grados que el tridente se ECHA ATRÁS en su arco (cogiendo impulso) antes de soltar el barrido.")]
    [SerializeField] private float _anticipationDeg = 25f;
    [Tooltip("Beats que dura la anticipación (se recorta si no hay tiempo, máx. 60 % del aviso).")]
    [SerializeField] private float _anticipationBeats = 0.4f;
    [Tooltip("Temblor (grados) durante la anticipación: se nota que va a salir disparado.")]
    [SerializeField] private float _anticipationShakeDeg = 5f;
    [Tooltip("Velocidad del temblor (oscilaciones por segundo).")]
    [SerializeField] private float _anticipationShakeHz = 14f;

    [Header("Mano que lo agarra (opcional)")]
    [Tooltip("Puño/mano que coge y lleva el tridente (por convención, el puño derecho). Vacío = no mueve mano.")]
    [SerializeField] private Transform _gripHand;
    [Tooltip("Punto de agarre EN EL TRIDENTE (espacio local del tridente) donde se coloca la mano.")]
    [SerializeField] private Vector3 _gripLocalOnObject = Vector3.zero;

    [Header("Daño (sistema propio)")]
    [Tooltip("Renderer de las puntas cuya caja define el golpe. Vacío = el primero que encuentre en hijos.")]
    [SerializeField] private Renderer _hitVolume;
    [Tooltip("Margen (m) que se añade a la caja del golpe en cada eje.")]
    [SerializeField] private float _hitPadding = 0f;
    [Tooltip("Loguea cuando el tridente golpea al jugador.")]
    [SerializeField] private bool _debug = false;

    [Header("Indicadores (opcional)")]
    [SerializeField] private GameObject _highTell;
    [SerializeField] private GameObject _lowTell;

    [System.Serializable] public class BoolEvent : UnityEvent<bool> { }

    [Header("Lanzamiento (Fase 3): MISMO arco y velocidad que el barrido, pero SOLTADO")]
    [Tooltip("Vueltas completas sobre sí mismo desde que lo suelta hasta que pasa por el jugador. El giro es\n" +
             "PROPORCIONAL al arco recorrido, así que al pasar por el jugador apunta igual que en el barrido.")]
    [Min(0)][SerializeField] private int _throwSpinTurns = 1;
    [Tooltip("Calcular solo el centro de giro (centro de la malla del tridente).")]
    [SerializeField] private bool _autoSpinCenter = true;
    [Tooltip("Centro de giro en espacio LOCAL del tridente (si 'Auto Spin Center' está desactivado).")]
    [SerializeField] private Vector3 _throwSpinCenterLocal = Vector3.zero;
    [Tooltip("En qué punto del vuelo (0-1) la OTRA mano empieza a ir a por él para cogerlo al final del arco.")]
    [Range(0f, 1f)][SerializeField] private float _catchReachStart = 0.5f;
    [Tooltip("Beats que tarda la otra mano en soltarlo y volver a su sitio tras dejarlo flotando.")]
    [SerializeField] private float _catchReleaseBeats = 0.5f;

    [Header("Eventos (animación / SFX)")]
    public BoolEvent OnWindUp;   // bool = high
    public BoolEvent OnContact;  // bool = high
    public UnityEvent OnThrowCharge;    // empieza el wind-up del lanzamiento
    public UnityEvent OnThrowRelease;   // lo suelta (empieza a volar por el arco)
    public UnityEvent OnThrowCaught;    // la otra mano lo coge al final del arco

    /// <summary>¿Está ocupado (barriendo, lanzado o volviendo)? Si no, está flotando en reposo.</summary>
    public bool IsBusy => _phase != Phase.Idle;

    /// <summary>¿Hay un lanzamiento/recuperación EN CURSO? (Sujetándolo a la espera del siguiente lanzamiento = no.)</summary>
    public bool IsThrowInProgress => _phase == Phase.WindUp || _phase == Phase.Sweeping || _phase == Phase.Recover;

    // ── Estado ────────────────────────────────────────────────────────────────
    private enum Phase { Idle, WindUp, Sweeping, Recover, Held }
    private bool   _holdForNext;      // tras cogerlo, NO lo deja en su sitio: lo sujeta para el siguiente lanzamiento
    private double _heldStart;
    private const float HeldTimeoutBeats = 4f;   // si no llega otro lanzamiento, lo deja en su sitio igualmente

    // Lanzamiento: usa las fases del barrido (WindUp → Sweeping → Recover) con '_throwing' activo.
    private bool      _throwing;                    // el barrido actual es un LANZAMIENTO (va suelto)
    private float     _thrPassY;                    // altura (mundo) a la que vuela
    private Vector3   _spinCenterLocal;             // centro de giro (espacio local del tridente)
    private Vector3   _handReleaseLocal;
    private Transform _catchHand, _catchHandParent; // la OTRA mano, que lo coge al final del arco
    private Vector3   _catchHandRestLocal;
    private bool      _recoverCatch;                // recuperación llevándolo con la otra mano
    private bool    _recoverHand = true;           // al recuperar, ¿la mano suelta el tridente? (tras lanzarlo ya está suelta)
    private const float RecoverHoldBeats = 0.25f;   // pausa agarrándolo antes de soltarlo (margen para encadenar)
    private Phase _phase = Phase.Idle;

    private BossController      _boss;
    private CharacterController _playerCC;
    private bool    _high, _fromRight, _contacted;
    private double  _windUpStart, _sweepStart, _sweepEnd, _recoverStart;
    private double  _beatDur = 0.857142857;

    private Vector3    _floatLocalPos;
    private Quaternion _floatLocalRot;
    private float      _spinAngle;

    private Vector3    _fromPos, _recFromPos;      // pose de partida del wind-up / recover
    private Quaternion _fromRot, _recFromRot;

    // Mano que agarra
    private Transform  _gripHandParent;
    private Vector3    _gripHandRestLocal;
    private float      _handGrip01;        // cuánto está agarrando la mano AHORA (0 = en reposo, 1 = en el mango)
    private float      _handGripFrom01;    // agarre al empezar la preparación (si viene de otro barrido, ya es 1)

    // Detección por volumen barrido
    private bool   _hit;
    private Bounds _prevBounds;
    private bool   _hasPrevBounds;

    private void Awake()
    {
        if (_tailPivot == null) _tailPivot = transform;
        if (_hitVolume == null) _hitVolume = GetComponentInChildren<Renderer>();
        _floatLocalPos = _tailPivot.localPosition;
        _floatLocalRot = _tailPivot.localRotation;
        if (_gripHand != null)
        {
            _gripHandParent    = _gripHand.parent;
            _gripHandRestLocal = _gripHand.localPosition;
        }
        _spinCenterLocal = _throwSpinCenterLocal;
        if (_autoSpinCenter)
        {
            // Centro de TODO el tridente (no solo las puntas) para que gire sobre sí mismo.
            bool any = false; Bounds b = default;
            foreach (Renderer r in _tailPivot.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            _spinCenterLocal = any && b.size.sqrMagnitude > 0.0001f ? _tailPivot.InverseTransformPoint(b.center) : Vector3.zero;
        }
        SetTells(false, false);
    }

    private void Start()
    {
        var pm = FindAnyObjectByType<PlayerMovement>();
        if (pm != null) _playerCC = pm.GetComponent<CharacterController>();
        if (BeatManager.Instance != null) _beatDur = BeatManager.Instance.BeatDuration;
    }

    /// <summary>
    /// Lanza un swing de tridente que pasa por el jugador en contactDSP. high = alto (agacharse) /
    /// false = bajo (salto). fromRight = empieza por la derecha (+) y barre a la izquierda (−); false al revés.
    /// </summary>
    /// <param name="sweepBeatsOverride">Duración del barrido en beats (si &gt; 0). Lo usa el jefe para acortarlo
    /// cuando los barridos van muy seguidos y así dejar siempre tiempo de aviso entre uno y otro.</param>
    public void Sweep(BossController boss, bool high, bool fromRight, double contactDSP, float sweepBeatsOverride = -1f)
    {
        UseGripHand(null);   // el barrido siempre con la mano asignada en el Inspector
        _throwing     = false;
        _recoverCatch = false;
        BeginSwing(boss, high, fromRight, contactDSP, sweepBeatsOverride);
        SetTells(high, !high);
        OnWindUp?.Invoke(high);
    }

    /// <summary>Prepara wind-up + barrido (común al barrido normal y al lanzamiento).</summary>
    private void BeginSwing(BossController boss, bool high, bool fromRight, double contactDSP, float sweepBeatsOverride)
    {
        _boss      = boss;
        _high      = high;
        _fromRight = fromRight;
        _beatDur   = boss.BeatDuration;

        float beats  = sweepBeatsOverride > 0f ? sweepBeatsOverride : _sweepBeats;
        float halfT  = beats * 0.5f * (float)_beatDur;
        _windUpStart = AudioSettings.dspTime;
        _sweepStart  = contactDSP - halfT;
        _sweepEnd    = contactDSP + halfT;

        _contacted     = false;
        _hit           = false;
        _hasPrevBounds = false;

        // Partimos de la pose actual (flotando) para que el 'coger' sea suave.
        _fromPos = _tailPivot.localPosition;
        _fromRot = _tailPivot.localRotation;
        // Si viene encadenado de otro barrido, la mano YA tiene el tridente: no lo suelta para volver a cogerlo.
        _handGripFrom01 = _handGrip01;
        _recoverHand    = true;
        _phase   = Phase.WindUp;
    }

    private void Update()
    {
        double now = AudioSettings.dspTime;
        _spinAngle += _floatSpinDegPerSec * Time.deltaTime;

        switch (_phase)
        {
            case Phase.Idle:
            {
                GetIdlePose(now, out Vector3 ip, out Quaternion ir);
                _tailPivot.localPosition = ip;
                _tailPivot.localRotation = ir;
                break;
            }

            case Phase.WindUp:
            {
                // El aviso tiene dos partes:
                //  1) COLOCARSE: de donde esté (flotando o del barrido anterior) a la pose de arranque.
                //  2) ANTICIPACIÓN: se echa atrás en el arco cogiendo impulso y TIEMBLA → va a salir disparado.
                double total       = _sweepStart - _windUpStart;
                double antic       = System.Math.Min(total * 0.6, _anticipationBeats * _beatDur);
                double approachEnd = _sweepStart - antic;
                float  dir         = _fromRight ? 1f : -1f;

                if (now < approachEnd)
                {
                    float g = approachEnd > _windUpStart
                        ? Mathf.Clamp01((float)((now - _windUpStart) / (approachEnd - _windUpStart)))
                        : 1f;
                    float e = 1f - (1f - g) * (1f - g);   // ease-out

                    GetSwingPoseAngle(dir * _swingHalfArcDeg, out Vector3 rp, out Quaternion rr);
                    _tailPivot.localPosition = Vector3.Lerp(_fromPos, rp, e);
                    _tailPivot.localRotation = Quaternion.Slerp(_fromRot, rr, e);
                    // Desde el agarre que ya tuviera (encadenado = 1 → la mano NO suelta el tridente).
                    PlaceHand(Mathf.Lerp(_handGripFrom01, 1f, e));
                }
                else
                {
                    float p = antic > 0.0001 ? Mathf.Clamp01((float)((now - approachEnd) / antic)) : 1f;
                    float pull = _anticipationDeg * (1f - (1f - p) * (1f - p));   // se echa atrás (ease-out)
                    GetSwingPoseAngle(dir * (_swingHalfArcDeg + pull), out Vector3 ap, out Quaternion ar);

                    // Temblor que crece y se apaga justo al soltar el golpe (sin(πp) = 0 al principio y al final).
                    float shake = _anticipationShakeDeg * Mathf.Sin(p * Mathf.PI)
                                * Mathf.Sin((float)now * _anticipationShakeHz * 2f * Mathf.PI);
                    ar = Quaternion.Euler(0f, shake, shake * 0.5f) * ar;

                    _tailPivot.localPosition = ap;
                    _tailPivot.localRotation = ar;
                    PlaceHand(1f);
                }

                if (now >= _sweepStart)
                {
                    _phase = Phase.Sweeping;
                    SetTells(false, false);
                    if (!_contacted) { _contacted = true; OnContact?.Invoke(_high); }
                    if (_throwing)
                    {
                        // LO SUELTA: a partir de aquí vuela solo por el mismo arco (y a la misma velocidad).
                        if (_gripHand != null) _handReleaseLocal = _gripHand.localPosition;
                        _handGrip01 = 0f;
                        OnThrowRelease?.Invoke();
                    }
                }
                break;
            }

            case Phase.Sweeping:
            {
                float u = _sweepEnd > _sweepStart
                    ? Mathf.Clamp01((float)((now - _sweepStart) / (_sweepEnd - _sweepStart)))
                    : 1f;

                // Arranca desde la pose echada atrás de la anticipación y barre hasta el otro lado.
                float dirS = _fromRight ? 1f : -1f;
                float ang  = Mathf.Lerp(dirS * (_swingHalfArcDeg + _anticipationDeg), -dirS * _swingHalfArcDeg, u);
                GetSwingPoseAngle(ang, out Vector3 sp, out Quaternion sr);
                if (_throwing) ApplyThrowSpin(ang, ref sp, ref sr);
                _tailPivot.localPosition = sp;
                _tailPivot.localRotation = sr;
                if (_throwing)
                {
                    UpdateThrowHands(now, u);
                    CheckThrowHit();
                }
                else
                {
                    PlaceHand(1f);
                    CheckHit();
                }

                if (u >= 1f)
                {
                    _phase        = Phase.Recover;
                    _recoverStart = now;
                    _recFromPos   = sp;
                    _recFromRot   = sr;
                    if (_throwing)
                    {
                        if (_gripHand != null) _gripHand.localPosition = _gripHandRestLocal;
                        _recoverHand  = false;              // la mano del lanzamiento ya lo soltó
                        _recoverCatch = CatchHandUsable;    // la OTRA mano lo coge…
                        if (_recoverCatch) OnThrowCaught?.Invoke();

                        // …y se queda flotando en el lado donde ACABÓ el arco (vaya de izq→der o de der→izq):
                        // el reposo original o su simétrico (X local → −X), el que quede más cerca.
                        Vector3 orig   = _floatLocalPos;
                        Vector3 mirror = new Vector3(-_floatLocalPos.x, _floatLocalPos.y, _floatLocalPos.z);
                        _floatMirrored = (sp - mirror).sqrMagnitude < (sp - orig).sqrMagnitude;

                        // Encadenado: la mano que lo coge lo SUJETA ahí y lo vuelve a lanzar (no lo deja flotando).
                        if (_recoverCatch && _holdForNext) { _phase = Phase.Held; _heldStart = now; }
                    }
                }
                break;
            }

            case Phase.Held:
            {
                // Sujetándolo con la mano que lo cogió, en la pose en la que lo cogió, a la espera del siguiente.
                _tailPivot.localPosition = _recFromPos;
                _tailPivot.localRotation = _recFromRot;
                PlaceCatchHand(1f);
                if (now > _heldStart + HeldTimeoutBeats * _beatDur)
                {
                    _phase = Phase.Recover;          // no llegó otro lanzamiento: lo deja en su sitio
                    _recoverStart = now;
                }
                break;
            }

            case Phase.Recover:
            {
                if (_recoverCatch) { UpdateCatchRecover(now); break; }

                // Pequeña pausa agarrándolo en la pose final antes de soltarlo: si llega otro barrido
                // encadenado (aunque sea un frame tarde) arranca desde aquí sin haber empezado a soltarlo.
                double holdEnd = _recoverStart + (_recoverHand ? RecoverHoldBeats : 0f) * _beatDur;
                if (now < holdEnd)
                {
                    PlaceHand(1f);
                    break;
                }

                double recEnd = holdEnd + _recoverBeats * _beatDur;
                float g = recEnd > holdEnd
                    ? Mathf.Clamp01((float)((now - holdEnd) / (recEnd - holdEnd)))
                    : 1f;
                float e = 1f - (1f - g) * (1f - g);

                GetIdlePose(now, out Vector3 ip, out Quaternion ir);
                _tailPivot.localPosition = Vector3.Lerp(_recFromPos, ip, e);
                _tailPivot.localRotation = Quaternion.Slerp(_recFromRot, ir, e);
                if (_recoverHand) PlaceHand(1f - e);   // tras un lanzamiento la mano ya está suelta

                if (g >= 1f)
                {
                    _phase    = Phase.Idle;
                    _throwing = false;
                    if (_gripHand != null) _gripHand.localPosition = _gripHandRestLocal;
                    SetTells(false, false);
                }
                break;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  LANZAMIENTO (Fase 3): el barrido, pero SOLTADO
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lanza el tridente siguiendo EXACTAMENTE el arco y la velocidad del barrido (mismo wind-up: lo coge,
    /// se coloca, se echa atrás y tiembla), pero al arrancar el barrido lo SUELTA: vuela solo por el arco,
    /// girando sobre sí mismo de forma proporcional al arco recorrido (al pasar por el jugador apunta igual
    /// que en el barrido). Pasa a la altura 'passY' (mundo) → hay que SALTAR. Al final del arco la OTRA mano
    /// ('catchHand') lo coge y lo deja flotando en su sitio. Si esa mano no existe/está destruida, vuelve solo.
    /// El lado de salida se elige para que acabe en el lado de la mano que lo coge.
    /// </summary>
    public void Throw(BossController boss, double contactDSP, float passY, Transform throwHand, Transform catchHand,
                      float sweepBeatsOverride = -1f, bool holdForNext = false)
    {
        // ¿Encadenado? La mano que lo sujeta (la que lo cogió) es la que lo lanza ahora, sin soltarlo.
        bool chained = _phase == Phase.Held && _catchHand != null && SameHand(throwHand, _catchHand);
        Transform heldBy = _catchHand, heldParent = _catchHandParent;
        Vector3   heldRest = _catchHandRestLocal;
        Transform prevGrip = _gripHand, prevGripParent = _gripHandParent;
        Vector3   prevGripRest = _gripHandRestLocal;

        if (_phase == Phase.Held && !chained && heldBy != null) heldBy.localPosition = heldRest;   // lo suelta
        UseGripHand(throwHand);
        if (chained)
        {
            _gripHand = heldBy; _gripHandParent = heldParent; _gripHandRestLocal = heldRest;
            _handGrip01 = 1f;
        }
        _boss     = boss;
        _throwing = true;
        _thrPassY = passY;
        _recoverCatch = false;
        _holdForNext  = holdForNext;

        _catchHand = catchHand != null && !SameHand(catchHand, _gripHand) ? catchHand : null;
        if (_catchHand != null)
        {
            _catchHandParent = _catchHand.parent;
            // Si la que va a cogerlo es la que lo lanzó antes, su reposo es el que ya teníamos (ahora puede estar
            // volviendo a su sitio).
            _catchHandRestLocal = chained && prevGrip != null && SameHand(_catchHand, prevGrip) && _catchHandParent == prevGripParent
                ? prevGripRest : _catchHand.localPosition;
        }

        _high = false;
        bool fromRight = ChooseThrowSide();
        BeginSwing(boss, false, fromRight, contactDSP, sweepBeatsOverride);

        SetTells(false, true);
        OnWindUp?.Invoke(false);
        OnThrowCharge?.Invoke();
    }

    /// <summary>¿Son la misma mano? (el mismo objeto o uno dentro del otro).</summary>
    public static bool SameHand(Transform a, Transform b) =>
        a != null && b != null && (a == b || a.IsChildOf(b) || b.IsChildOf(a));

    /// <summary>Mano asignada en el Inspector para el barrido (Grip Hand).</summary>
    public Transform GripHand => _defGripHand != null ? _defGripHand : _gripHand;

    private Transform _defGripHand, _defGripParent;
    private Vector3   _defGripRestLocal;
    private bool      _defGripSaved;

    /// <summary>Cambia la mano que agarra el tridente (null = la del Inspector).</summary>
    private void UseGripHand(Transform hand)
    {
        if (!_defGripSaved)
        {
            _defGripHand = _gripHand; _defGripParent = _gripHandParent; _defGripRestLocal = _gripHandRestLocal;
            _defGripSaved = true;
        }
        if (hand == null || hand == _defGripHand)
        {
            _gripHand = _defGripHand; _gripHandParent = _defGripParent; _gripHandRestLocal = _defGripRestLocal;
        }
        else
        {
            _gripHand = hand; _gripHandParent = hand.parent; _gripHandRestLocal = hand.localPosition;
            _handGrip01 = 0f;   // la otra mano no tenía el tridente
        }
    }

    private Transform  PivotParent => _tailPivot.parent;

    private bool CatchHandUsable => _catchHand != null && _catchHand.gameObject.activeInHierarchy;

    /// <summary>Empieza en el lado de la mano que lanza y acaba en el de la mano que lo coge.</summary>
    private bool ChooseThrowSide()
    {
        GetSwingPoseAngle( _swingHalfArcDeg, out Vector3 pPlus,  out _);
        GetSwingPoseAngle(-_swingHalfArcDeg, out Vector3 pMinus, out _);

        if (CatchHandUsable)
        {
            Vector3 rest = HandRestInPivotSpace(_catchHandParent, _catchHandRestLocal);
            bool endPlus = (pPlus - rest).sqrMagnitude < (pMinus - rest).sqrMagnitude;
            return !endPlus;   // fromRight = empieza en + y acaba en −
        }
        if (_gripHand != null && _gripHand.gameObject.activeInHierarchy)
        {
            Vector3 rest = HandRestInPivotSpace(_gripHandParent, _gripHandRestLocal);
            return (pPlus - rest).sqrMagnitude < (pMinus - rest).sqrMagnitude;
        }
        return Random.value < 0.5f;
    }

    private Vector3 HandRestInPivotSpace(Transform handParent, Vector3 restLocal)
    {
        Vector3 w = handParent != null ? handParent.TransformPoint(restLocal) : restLocal;
        return PivotParent != null ? PivotParent.InverseTransformPoint(w) : w;
    }

    /// <summary>Giro sobre sí mismo (eje vertical, por su centro) PROPORCIONAL al arco recorrido desde que
    /// se soltó: 0 al soltarlo y un número entero de vueltas al pasar por el jugador (ángulo 0).</summary>
    private void ApplyThrowSpin(float ang, ref Vector3 pos, ref Quaternion rot)
    {
        if (_throwSpinTurns <= 0) return;
        float dir      = _fromRight ? 1f : -1f;
        float angStart = dir * (_swingHalfArcDeg + _anticipationDeg);
        float travel   = Mathf.Abs(angStart - ang);
        float spin     = -dir * 360f * _throwSpinTurns * travel / Mathf.Max(1f, Mathf.Abs(angStart));

        Vector3 c      = Vector3.Scale(_tailPivot.localScale, _spinCenterLocal);
        Vector3 center = pos + rot * c;
        rot = Quaternion.Euler(0f, spin, 0f) * rot;
        pos = center - rot * c;
    }

    /// <summary>En vuelo: la mano que lo lanzó vuelve a su sitio y la otra va a por él para cogerlo al final.</summary>
    private void UpdateThrowHands(double now, float u)
    {
        if (_gripHand != null)
        {
            float h = Mathf.Clamp01((float)((now - _sweepStart) / (0.4 * _beatDur)));
            _gripHand.localPosition = Vector3.Lerp(_handReleaseLocal, _gripHandRestLocal, 1f - (1f - h) * (1f - h));
        }
        if (CatchHandUsable)
        {
            float c = Mathf.InverseLerp(_catchReachStart, 1f, u);
            PlaceCatchHand(c * c * (3f - 2f * c));
        }
    }

    /// <summary>La otra mano lo SUJETA donde lo cogió, lo lleva a su sitio de flotación y lo suelta allí.</summary>
    private void UpdateCatchRecover(double now)
    {
        double holdEnd  = _recoverStart + RecoverHoldBeats * _beatDur;
        double carryEnd = holdEnd + Mathf.Max(0.05f, _recoverBeats) * _beatDur;
        double relEnd   = carryEnd + Mathf.Max(0.05f, _catchReleaseBeats) * _beatDur;

        GetIdlePose(now, out Vector3 ip, out Quaternion ir);
        float g = now <= holdEnd ? 0f : Mathf.Clamp01((float)((now - holdEnd) / (carryEnd - holdEnd)));
        float e = 1f - (1f - g) * (1f - g);
        _tailPivot.localPosition = Vector3.Lerp(_recFromPos, ip, e);
        _tailPivot.localRotation = Quaternion.Slerp(_recFromRot, ir, e);

        if (now < carryEnd) PlaceCatchHand(1f);
        else
        {
            float r = Mathf.Clamp01((float)((now - carryEnd) / (relEnd - carryEnd)));
            PlaceCatchHand(1f - r * r * (3f - 2f * r));
        }

        if (now >= relEnd)
        {
            if (_catchHand != null) _catchHand.localPosition = _catchHandRestLocal;
            _recoverCatch = false;
            _throwing     = false;
            _phase        = Phase.Idle;
            SetTells(false, false);
        }
    }

    private void PlaceCatchHand(float t)
    {
        if (!CatchHandUsable) return;
        Vector3 gripWorld = _tailPivot.TransformPoint(_gripLocalOnObject);
        Vector3 restWorld = _catchHandParent != null
            ? _catchHandParent.TransformPoint(_catchHandRestLocal)
            : _catchHandRestLocal;
        _catchHand.position = Vector3.Lerp(restWorld, gripWorld, Mathf.Clamp01(t));
    }

    /// <summary>
    /// Golpe del tridente lanzado por VOLUMEN BARRIDO contra la caja del jugador. La caja se reconstruye con
    /// las medidas de su CharacterController y su posición real: así vale también SENTADO en el kayak
    /// (donde el CC está desactivado y sus bounds no sirven). Si salta, su caja sube y el tridente pasa por debajo.
    /// </summary>
    private void CheckThrowHit()
    {
        if (_hit || _hitVolume == null || _boss == null || _boss.PlayerT == null || _playerCC == null) return;

        Bounds cur = _hitVolume.bounds;
        if (_hitPadding != 0f) cur.Expand(_hitPadding);
        Bounds swept = cur;
        if (_hasPrevBounds) { swept.Encapsulate(_prevBounds.min); swept.Encapsulate(_prevBounds.max); }
        _prevBounds = cur;
        _hasPrevBounds = true;

        Transform pt = _boss.PlayerT;
        Vector3 sc = pt.lossyScale;
        float r = _playerCC.radius * Mathf.Max(sc.x, sc.z);
        float h = Mathf.Max(_playerCC.height * sc.y, 2f * r);
        Vector3 center = pt.position + Vector3.up * (_playerCC.center.y * sc.y);
        Bounds player = new Bounds(center, new Vector3(2f * r, h, 2f * r));

        if (swept.Intersects(player))
        {
            _hit = true;
            if (_debug) Debug.Log("[Tridente] ¡El tridente lanzado te ha dado! (había que saltar)", this);
            _boss.KillPlayer();
        }
    }

    /// <summary>Pose de flotación en reposo: vertical, con bob arriba/abajo, giro continuo en Y y un
    /// bamboleo muy leve en X/Z.</summary>
    private void GetIdlePose(double now, out Vector3 pos, out Quaternion rot)
    {
        double bobPeriod  = Mathf.Max(0.01f, _floatBobBeats)  * _beatDur;
        double tiltPeriod = Mathf.Max(0.01f, _idleTiltBeats)  * _beatDur;

        float bob   = _floatBobAmplitude * Mathf.Sin((float)(now / bobPeriod)  * 2f * Mathf.PI);
        float tiltX = _idleTiltAmplitude * Mathf.Sin((float)(now / tiltPeriod) * 2f * Mathf.PI);
        float tiltZ = _idleTiltAmplitude * Mathf.Cos((float)(now / tiltPeriod) * 2f * Mathf.PI * 0.8f);

        pos = FloatLocalPos + Vector3.up * bob;
        // Pre-multiplicar → el giro se aplica en los ejes GLOBALES (spin sobre la vertical, no tumbado).
        rot = Quaternion.Euler(tiltX, _spinAngle, tiltZ) * FloatLocalRot;
    }

    // Lado de reposo: el original o su SIMÉTRICO respecto al plano X = 0 del padre (tras cogerlo la otra mano).
    private bool _floatMirrored;

    private Vector3 FloatLocalPos =>
        _floatMirrored ? new Vector3(-_floatLocalPos.x, _floatLocalPos.y, _floatLocalPos.z) : _floatLocalPos;

    // Reflejo de una rotación respecto al plano YZ: (x, y, z, w) → (x, −y, −z, w).
    private Quaternion FloatLocalRot =>
        _floatMirrored ? new Quaternion(_floatLocalRot.x, -_floatLocalRot.y, -_floatLocalRot.z, _floatLocalRot.w)
                       : _floatLocalRot;

    /// <summary>Posición (mundo) donde flota ahora en reposo. El jefe lo lanza con la mano de ESE lado.</summary>
    public Vector3 IdleWorldPosition
    {
        get
        {
            Transform par = _tailPivot != null ? _tailPivot.parent : null;
            return par != null ? par.TransformPoint(FloatLocalPos) : FloatLocalPos;
        }
    }

    /// <summary>Pose del swing para un ÁNGULO del hachazo (grados alrededor del eje vertical del hombro):
    /// el tridente PIVOTA alrededor del hombro, a la altura elegida, con las puntas hacia delante.</summary>
    private void GetSwingPoseAngle(float ang, out Vector3 pos, out Quaternion rot)
    {
        float hoff = _high ? _highLocalY : _lowLocalY;

        // Pivote (hombro) en espacio local del padre.
        Vector3 shoulder = _shoulderLocalPos;
        Quaternion pivotRot = Quaternion.Euler(0f, ang, 0f);

        // El tridente cuelga del hombro: sale hacia delante (-Z) y a la altura elegida; gira con el pivote.
        Vector3 reach = new Vector3(0f, hoff, -_reachForward);
        pos = shoulder + pivotRot * reach;
        // Lanzamiento: el MISMO arco, pero a la altura pedida (mundo), p. ej. el torso del jugador en el kayak.
        if (_throwing)
        {
            Transform par = PivotParent;
            Vector3 w = par != null ? par.TransformPoint(pos) : pos;
            w.y = _thrPassY;
            pos = par != null ? par.InverseTransformPoint(w) : w;
        }
        // Swing ALTO: el tridente gira además _highRollDeg (90°) en el eje Z global.
        Quaternion highRot = _high ? Quaternion.Euler(0f, 0f, _highRollDeg) : Quaternion.identity;
        rot = pivotRot * highRot * Quaternion.Euler(_attackEuler);
    }

    /// <summary>Coloca la mano entre su reposo (t=0) y el punto de agarre del tridente (t=1).</summary>
    private void PlaceHand(float t)
    {
        _handGrip01 = Mathf.Clamp01(t);   // se recuerda para que un barrido encadenado no suelte el tridente
        if (_gripHand == null) return;
        Vector3 gripWorld = _tailPivot.TransformPoint(_gripLocalOnObject);
        Vector3 restWorld = _gripHandParent != null
            ? _gripHandParent.TransformPoint(_gripHandRestLocal)
            : _gripHandRestLocal;
        _gripHand.position = Vector3.Lerp(restWorld, gripWorld, t);
    }

    /// <summary>
    /// Golpe por VOLUMEN BARRIDO: caja de las puntas del frame anterior + la actual (imposible tunneling),
    /// contra la caja del jugador. Esquiva geométrica: saltar (bajo) o agacharse (alto).
    /// </summary>
    private void CheckHit()
    {
        if (_hit || _hitVolume == null || _playerCC == null || _boss == null) return;

        Bounds cur = _hitVolume.bounds;
        if (_hitPadding != 0f) cur.Expand(_hitPadding);

        Bounds swept = cur;
        if (_hasPrevBounds)
        {
            swept.Encapsulate(_prevBounds.min);
            swept.Encapsulate(_prevBounds.max);
        }
        _prevBounds    = cur;
        _hasPrevBounds = true;

        if (swept.Intersects(_playerCC.bounds))
        {
            _hit = true;
            if (_debug) Debug.Log($"[Tridente] swing {(_high ? "ALTO" : "BAJO")} — game over.", this);
            _boss.KillPlayer();
        }
    }

    private void SetTells(bool high, bool low)
    {
        if (_highTell != null) _highTell.SetActive(high);
        if (_lowTell  != null) _lowTell.SetActive(low);
    }
}
