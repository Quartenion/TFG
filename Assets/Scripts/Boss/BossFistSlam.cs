using UnityEngine;

/// <summary>
/// Puño del jefe (Fase 1, Ataque C). Se LEVANTA sobre el jugador (telegraph, deslizándose un poco hacia
/// atrás en Z para "coger impulso") y se ESTAMPA contra el suelo en el carril donde estaba el jugador al
/// iniciarse el aviso. Es LETAL: si te pilla la caja durante el estampido → game over. Se esquiva
/// apartándote de carril tras ver el puño levantado (whack-a-mole).
///
/// El carril (X) se BLOQUEA al iniciar el aviso — así el jugador tiene ~1 beat para moverse — mientras
/// que la Z sigue al jugador (para que caiga siempre a su lado, no detrás) y la Y va al suelo.
///
/// Los dos puños (izq/der) alternan cada beat: BossController llama a Slam() en cada uno a su tiempo,
/// pasando el DSP exacto del contacto (el aviso ocupa el beat anterior).
///
/// La detección letal es por VOLUMEN BARRIDO (caja anterior→actual del puño), solo durante el estampido,
/// así que ni el aviso mata ni el golpe rápido se cuela.
/// </summary>
[AddComponentMenu("Boss/Boss Fist Slam")]
public class BossFistSlam : MonoBehaviour
{
    [Header("Movimiento")]
    [Tooltip("Altura a la que se levanta sobre el punto de impacto durante el aviso.")]
    [SerializeField] private float _raiseHeight = 14f;
    [Tooltip("Deslizamiento en Z (hacia atrás) al levantarse, para el gesto de coger impulso.")]
    [SerializeField] private float _windupZ = 6f;
    [Tooltip("Fracción del beat que dura el ESTAMPIDO (el resto del beat es el aviso de subida).")]
    [SerializeField, Range(0.05f, 0.6f)] private float _slamPortion = 0.28f;
    [Tooltip("(Fase 1) Beats ANTES del impacto en los que el puño se queda FIJO encima del punto de caída\n" +
             "(ya no te sigue). Es tu ventana para esquivar con dash o cambio de carril. Se recorta sola si el\n" +
             "golpe es muy rápido (máx. 80 % del aviso).")]
    [SerializeField, Range(0.1f, 1f)] private float _lockBeforeImpactBeats = 0.6f;
    [Tooltip("Beats que el puño se queda CLAVADO en el suelo antes de retraerse.")]
    [SerializeField] private float _holdBeats = 1f;
    [Tooltip("Beats que tarda en recuperar (volver a reposo) tras la espera en el suelo.")]
    [SerializeField] private float _recoverBeats = 0.5f;
    [Tooltip("Altura de la base del puño respecto al suelo al estampar (0 = a ras).")]
    [SerializeField] private float _groundOffset = 0f;
    [Tooltip("Desfase en Z respecto al jugador donde cae el puño (0 = justo a su lado).")]
    [SerializeField] private float _zOffset = 0f;

    [Header("Letal")]
    [Tooltip("Renderer cuya caja hace daño. Vacío = el primero que encuentre en hijos.")]
    [SerializeField] private Renderer _hitVolume;
    [Tooltip("Margen extra sobre la caja para la detección.")]
    [SerializeField] private float _hitPadding = 0.2f;

    private BossController      _boss;
    private CharacterController _playerCC;
    private Transform _parent;
    private Vector3   _restLocalPos;
    private bool      _captured;

    // Acción actual.
    private bool    _active;
    private double  _contactDSP, _beatDur;
    private float   _lockedX;
    private float   _lastGroundY;
    private Vector3 _prevHitCenter;
    private bool    _hasPrev, _lethalDone;
    private bool    _lethal = true;
    private bool    _emitShockwave;
    private bool    _shockwaveEmitted;
    private bool    _trackPlayer = true;   // true = sigue al jugador; false = delante del jefe (fijo)
    private float   _frontZ;               // (solo no-track) Z por delante del jefe
    private float   _centerPull;           // (solo no-track) 0 = su propia X, →1 = hacia la X del jefe
    private double  _callDSP;              // cuándo se llamó a Slam (el aviso puede ser más corto que 1 beat)
    private bool    _fallLocked;           // (Fase 1) ya está cayendo: su posición horizontal queda CONGELADA
    private float   _fallX, _fallZ;        // punto horizontal fijo de la caída
    private PlayerMovement _pm;
    private bool    _wave;                 // ataque de ondas: usa tiempos FIJOS para que ambos puños vayan iguales
    private float   _slamGroundY;          // superficie REAL de la plataforma bajo el golpe (raycast)
    private bool    _groundSampled;
    private float   _halfHeight = 2.5f;    // media altura del puño (prefab 5×5×5 → 2.5), para apoyar su BASE en el suelo

    // Tiempos FIJOS del ataque de ondas (idénticos en los dos puños → van sincronizados).
    private const float WaveSlamPortion  = 0.28f;
    private const float WaveHoldBeats    = 1f;
    private const float WaveRecoverBeats = 0.5f;
    private const float WaveRaiseHeight  = 12f;
    private const float WaveWindupZ      = 5f;

    private void Capture()
    {
        _parent       = transform.parent;
        _restLocalPos = transform.localPosition;
        if (_hitVolume == null) _hitVolume = GetComponentInChildren<Renderer>();
        // Media altura real del puño (pivote al centro): la base queda a ras de la plataforma.
        if (_hitVolume != null && _hitVolume.bounds.extents.y > 0.01f) _halfHeight = _hitVolume.bounds.extents.y;
        _captured = true;
    }

    /// <summary>
    /// Altura de la SUPERFICIE de la plataforma en (x,z) por raycast. Ignora el propio puño, todo el jefe,
    /// el jugador y los pilares (si no, la onda salía encima del puño: el rayo chocaba con su cara de arriba).
    /// De los impactos válidos se queda con el más cercano a 'fallback' (el suelo donde pisa el jugador).
    /// </summary>
    private float GroundYAt(float x, float z, float fallback)
    {
        Vector3 from = new Vector3(x, fallback + 40f, z);
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
        Transform bossRoot = _boss != null ? _boss.transform : null;
        Transform playerT  = _boss != null ? _boss.PlayerT   : null;

        float best = fallback, bestDiff = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            Transform t = h.collider.transform;
            if (t == transform || t.IsChildOf(transform)) continue;          // el propio puño
            if (bossRoot != null && t.IsChildOf(bossRoot)) continue;         // cuerpo/cabeza/tridente del jefe
            if (playerT  != null && t.IsChildOf(playerT))  continue;         // jugador
            if (h.collider.GetComponentInParent<BossPillar>() != null) continue;  // pilares
            float diff = Mathf.Abs(h.point.y - fallback);
            if (diff < bestDiff) { bestDiff = diff; best = h.point.y; }
        }
        return best;
    }

    private Vector3 RestWorld => _parent != null ? _parent.TransformPoint(_restLocalPos) : _restLocalPos;

    /// <summary>Suelta la onda expansiva desde DEBAJO del puño, a ras de la plataforma (una vez por golpe).</summary>
    private void EmitShockwave()
    {
        _shockwaveEmitted = true;
        // Superficie de la plataforma justo bajo el puño (ignorando el propio puño, que interceptaba el rayo).
        float gy = GroundYAt(transform.position.x, transform.position.z, _slamGroundY);
        BossShockwave.Spawn(_boss, new Vector3(transform.position.x, gy, transform.position.z));
    }

    // Pose desde la que arranca el golpe (donde estaba el puño al llamarse Slam), guardada en el espacio del
    // padre para que siga al jefe. Así, si se re-lanza estando en el suelo o volviendo, NO salta al reposo.
    private Vector3 _fromLocal;
    private Vector3 FromWorld => _parent != null ? _parent.TransformPoint(_fromLocal) : _fromLocal;

    /// <summary>
    /// Inicia un estampido que cae en contactDSP.
    ///   trackPlayer = true  → bloquea el carril del jugador AHORA y cae a su lado (whack-a-mole, Fase 1).
    ///   trackPlayer = false → cae DELANTE DEL JEFE, fijo: X = jefe.x + offsetX, Z = jefe.z + frontZ (ataque de ondas).
    ///   lethal        → si el puño en sí mata (Fase 1). En el ataque de ondas es false (mata la ONDA).
    ///   emitShockwave → al tocar, suelta una onda expansiva (por código).
    /// </summary>
    public void Slam(BossController boss, double contactDSP,
                     bool emitShockwave = false, bool lethal = true, bool trackPlayer = true,
                     float offsetX = 0f, float frontZ = 0f, float centerPull = 0f)
    {
        // Si el golpe ANTERIOR ya ha llegado (o está a punto) al suelo pero su onda aún no ha salido —porque el
        // jefe relanza el puño en el mismo frame del contacto, antes de que este Update la soltara—, se suelta
        // AHORA. Antes se perdía y "a veces el puño no generaba onda".
        if (_active && _emitShockwave && !_shockwaveEmitted && _boss != null &&
            AudioSettings.dspTime >= _contactDSP - 0.05 * _beatDur)
            EmitShockwave();

        _centerPull = Mathf.Clamp(centerPull, 0f, 0.95f);
        _boss = boss;
        if (!_captured) Capture();
        if (_playerCC == null && boss.PlayerT != null) _playerCC = boss.PlayerT.GetComponent<CharacterController>();

        _beatDur     = boss.BeatDuration;
        _contactDSP  = contactDSP;
        _callDSP     = AudioSettings.dspTime;   // el aviso empieza como pronto AHORA (golpes a medio beat)
        _fallLocked  = false;
        if (_pm == null && boss.PlayerT != null) _pm = boss.PlayerT.GetComponent<PlayerMovement>();
        _trackPlayer = trackPlayer;
        _wave        = !trackPlayer;       // el ataque de ondas usa tiempos fijos (sincronía garantizada)
        _groundSampled = false;            // se vuelve a medir la plataforma para este golpe
        _frontZ      = frontZ;
        _lockedX     = trackPlayer ? (boss.PlayerT != null ? boss.PlayerT.position.x : 0f) : offsetX;
        if (_playerCC != null) _lastGroundY = _playerCC.bounds.min.y;

        // Arrancar desde donde esté AHORA el puño (reposo, en el suelo o a medio volver) → transición suave.
        _fromLocal = _parent != null ? _parent.InverseTransformPoint(transform.position) : transform.position;

        _lethal           = lethal;
        _emitShockwave    = emitShockwave;
        _shockwaveEmitted = false;
        _active           = true;
        _hasPrev          = false;
        _lethalDone       = false;
        _manualMode       = ManualMode.None;   // un golpe cancela el control manual
    }

    // ── Control MANUAL (animaciones de guion, p. ej. agarrarse al borde en la Fase 3) ───────────
    private enum ManualMode { None, Hold, Return }
    private ManualMode _manualMode = ManualMode.None;
    private Vector3    _manualTarget;
    private Vector3    _manualFromLocal;
    private double     _manualStart, _manualEnd;
    private float      _manualFollow = 10f;

    /// <summary>Lleva el puño (suave) hasta 'worldPos' y lo mantiene ahí. Llámalo cada frame para moverlo.</summary>
    public void ManualHold(Vector3 worldPos, float followSpeed = 10f)
    {
        if (!_captured) Capture();
        _active       = false;
        _manualMode   = ManualMode.Hold;
        _manualTarget = worldPos;
        _manualFollow = followSpeed;
    }

    /// <summary>Suelta el control manual: el puño vuelve a su reposo en 'beats' beats.</summary>
    public void ManualRelease(float beatDuration, float beats = 1f)
    {
        if (_manualMode == ManualMode.None) return;
        _manualMode      = ManualMode.Return;
        _manualFromLocal = _parent != null ? _parent.InverseTransformPoint(transform.position) : transform.position;
        _manualStart     = AudioSettings.dspTime;
        _manualEnd       = _manualStart + Mathf.Max(0.05f, beats) * beatDuration;
    }

    /// <summary>Posición de reposo del puño en el mundo (sigue al jefe).</summary>
    public Vector3 RestWorldPosition { get { if (!_captured) Capture(); return RestWorld; } }

    /// <summary>Media altura del puño (para apoyar su base en una superficie).</summary>
    public float HalfHeight => _captured ? _halfHeight : (_hitVolume != null ? _hitVolume.bounds.extents.y : 2.5f);

    private void Update()
    {
        if (_manualMode == ManualMode.Hold)
        {
            float k = 1f - Mathf.Exp(-_manualFollow * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, _manualTarget, k);
            return;
        }
        if (_manualMode == ManualMode.Return)
        {
            double t = AudioSettings.dspTime;
            float u = _manualEnd > _manualStart ? Mathf.Clamp01((float)((t - _manualStart) / (_manualEnd - _manualStart))) : 1f;
            Vector3 from = _parent != null ? _parent.TransformPoint(_manualFromLocal) : _manualFromLocal;
            transform.position = Vector3.Lerp(from, RestWorld, u * u * (3f - 2f * u));
            if (u >= 1f) { _manualMode = ManualMode.None; transform.localPosition = _restLocalPos; }
            return;
        }

        if (!_active || _boss == null) return;
        double now = AudioSettings.dspTime;

        // En el ataque de ondas usamos tiempos FIJOS (iguales en ambos puños) para que vayan a la vez.
        float slamPortion  = _wave ? WaveSlamPortion  : _slamPortion;
        float holdBeats    = _wave ? WaveHoldBeats    : _holdBeats;
        float recoverBeats = _wave ? WaveRecoverBeats : _recoverBeats;
        float raiseHeight  = _wave ? WaveRaiseHeight  : _raiseHeight;
        float windupZ      = _wave ? WaveWindupZ      : _windupZ;

        // Aviso: 1 beat antes del contacto, o desde que se llamó si el golpe es más rápido (medio beat).
        double windupStart = System.Math.Max(_contactDSP - _beatDur, _callDSP);
        double windupLen   = _contactDSP - windupStart;
        double slamStart   = _contactDSP - System.Math.Min(slamPortion * _beatDur, windupLen * 0.5);   // último tramo: estampa
        double holdEnd     = _contactDSP + holdBeats * _beatDur;       // se queda clavado en el suelo
        double recoverEnd  = holdEnd + recoverBeats * _beatDur;        // y luego se retrae

        // Muestrear el suelo mientras el jugador lo pisa (para que el puño caiga a ras y no siga al salto).
        if (_playerCC != null && _playerCC.isGrounded) _lastGroundY = _playerCC.bounds.min.y;

        // Punto de impacto: sigue al jugador (Fase 1) o cae fijo DELANTE DEL JEFE (ataque de ondas).
        float cx, cz;
        if (_trackPlayer)
        {
            if (_fallLocked)
            {
                // CAYENDO: no se mueve en horizontal (ni sigue al jugador). Un dash a tiempo lo esquiva.
                cx = _fallX;
                cz = _fallZ;
            }
            else
            {
                // Aviso: se coloca encima de DONDE ESTARÁ el jugador al impactar si sigue corriendo normal
                // (X del carril fijado al llamarse; Z = la actual + lo que avanza hasta el contacto). Así, al
                // congelarse para caer no pega ningún salto y no cae siempre por detrás del auto-runner.
                float remaining = Mathf.Max(0f, (float)(_contactDSP - now));
                float lead = _pm != null ? _pm.walkSpeed * remaining : 0f;
                Vector3 fwd = _pm != null ? _pm.RunForward : Vector3.zero;
                cx = _lockedX + fwd.x * lead;
                cz = (_boss.PlayerT != null ? _boss.PlayerT.position.z : transform.position.z) + _zOffset + fwd.z * lead;

                // Se FIJA encima del punto de caída '_lockBeforeImpactBeats' antes del impacto (no justo al caer):
                // así ves dónde va a golpear y te da tiempo a salir de debajo.
                double lockStart = _contactDSP - System.Math.Min(_lockBeforeImpactBeats * _beatDur, windupLen * 0.8);
                if (now >= System.Math.Min(lockStart, slamStart))
                {
                    // El punto horizontal se CONGELA (y así sigue mientras cae). Cambiar de carril o un dash le saca.
                    _fallX = cx;
                    _fallZ = cz;
                    _fallLocked = true;
                }
            }
        }
        else
        {
            // Delante del jefe HACIA el jugador (robusto al signo del eje) para que el jugador los vea.
            // La X NO se toca: cada puño estampa recto en su propia X de reposo (no se cruzan ni se mueven en X).
            Vector3 bossP = _boss.BossT != null ? _boss.BossT.position : _boss.transform.position;
            float dirZ = _boss.PlayerT != null ? Mathf.Sign(_boss.PlayerT.position.z - bossP.z) : 1f;
            // Se acerca al centro (X del jefe) según _centerPull, sin llegar a cruzarse con el otro puño.
            cx = Mathf.Lerp(RestWorld.x, bossP.x, _centerPull);
            cz = bossP.z + dirZ * _frontZ;
        }

        // Superficie REAL de la plataforma bajo el golpe (una vez por golpe) y el puño APOYADO encima:
        // su centro queda a media altura (2.5 en el prefab 5×5×5) para que la BASE toque el suelo.
        if (!_groundSampled)
        {
            _slamGroundY   = GroundYAt(cx, cz, _lastGroundY);
            _groundSampled = true;
        }
        Vector3 contact = new Vector3(cx, _slamGroundY + _halfHeight + _groundOffset, cz);
        Vector3 raised = contact + Vector3.up * raiseHeight - Vector3.forward * windupZ;

        Vector3 pos;
        if (now < windupStart)
        {
            pos = FromWorld;                                          // aún no toca: quieto donde estaba
        }
        else if (now < slamStart)
        {
            float u = Mathf.InverseLerp((float)windupStart, (float)slamStart, (float)now);
            float e = u * u * (3f - 2f * u);                         // smoothstep: sin tirones al arrancar/llegar
            pos = Vector3.Lerp(FromWorld, raised, e);                // desde donde estaba → pose de aviso
        }
        else if (now < _contactDSP)
        {
            float u = Mathf.InverseLerp((float)slamStart, (float)_contactDSP, (float)now);
            pos = Vector3.Lerp(raised, contact, u * u);              // ease-in: cae rápido
        }
        else if (now < holdEnd)
        {
            pos = contact;                                          // CLAVADO en el suelo (Z sigue al jugador)
        }
        else if (now < recoverEnd)
        {
            float u = Mathf.InverseLerp((float)holdEnd, (float)recoverEnd, (float)now);
            float e = u * u * (3f - 2f * u);                        // smoothstep
            pos = Vector3.Lerp(contact, RestWorld, e);              // vuelve a reposo
        }
        else
        {
            _active = false;
            transform.localPosition = _restLocalPos;                // asentar exacto en reposo
            return;
        }

        transform.position = pos;

        // Al TOCAR el suelo: soltar la onda expansiva por código, desde DEBAJO del puño (una sola vez).
        // La Y se saca por RAYCAST al suelo bajo el puño (no del jugador, que está saltando y da un valor viejo).
        if (_emitShockwave && !_shockwaveEmitted && now >= _contactDSP) EmitShockwave();

        // Letal SOLO durante el estampido (aviso→contacto + pequeña gracia), por volumen barrido.
        bool lethalWindow = _lethal && now >= slamStart && now <= _contactDSP + 0.05;
        if (!_lethalDone && lethalWindow && _hitVolume != null && _playerCC != null)
        {
            Bounds hb     = _hitVolume.bounds;
            Vector3 center = hb.center;
            Vector3 half   = hb.extents + Vector3.one * _hitPadding;

            Bounds swept = new Bounds(center, half * 2f);
            if (_hasPrev)
            {
                swept.Encapsulate(_prevHitCenter - half);
                swept.Encapsulate(_prevHitCenter + half);
            }

            if (swept.Intersects(_playerCC.bounds))
            {
                _lethalDone = true;
                _boss.KillPlayer();
                return;
            }
            _prevHitCenter = center;
            _hasPrev = true;
        }
    }
}
