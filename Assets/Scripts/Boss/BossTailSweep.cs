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

    [Header("Eventos (animación / SFX)")]
    public BoolEvent OnWindUp;   // bool = high
    public BoolEvent OnContact;  // bool = high

    // ── Estado ────────────────────────────────────────────────────────────────
    private enum Phase { Idle, WindUp, Sweeping, Recover }
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
        _phase   = Phase.WindUp;

        SetTells(high, !high);
        OnWindUp?.Invoke(high);
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
                _tailPivot.localPosition = sp;
                _tailPivot.localRotation = sr;
                PlaceHand(1f);
                CheckHit();

                if (u >= 1f)
                {
                    _phase        = Phase.Recover;
                    _recoverStart = now;
                    _recFromPos   = sp;
                    _recFromRot   = sr;
                }
                break;
            }

            case Phase.Recover:
            {
                // Pequeña pausa agarrándolo en la pose final antes de soltarlo: si llega otro barrido
                // encadenado (aunque sea un frame tarde) arranca desde aquí sin haber empezado a soltarlo.
                double holdEnd = _recoverStart + RecoverHoldBeats * _beatDur;
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
                PlaceHand(1f - e);

                if (g >= 1f)
                {
                    _phase = Phase.Idle;
                    if (_gripHand != null) _gripHand.localPosition = _gripHandRestLocal;
                    SetTells(false, false);
                }
                break;
            }
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

        pos = _floatLocalPos + Vector3.up * bob;
        // Pre-multiplicar → el giro se aplica en los ejes GLOBALES (spin sobre la vertical, no tumbado).
        rot = Quaternion.Euler(tiltX, _spinAngle, tiltZ) * _floatLocalRot;
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
