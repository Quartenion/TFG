using UnityEngine;

/// <summary>
/// Bola de fuego teledirigida del jefe (Fase 2, Ataque B). Persigue al jugador y, cuando está a 1 beat
/// de DISTANCIA de él (lo que recorre en 'straightBeats' beats), DEJA DE PERSEGUIR y va recta (congela
/// la dirección hacia donde estaba el jugador) — esa es la ventana para esquivar apartándote. Letal al
/// tocar (game over).
///
/// El disparador de "recta" es por DISTANCIA real al jugador (no por tiempo desde el spawn), así que
/// siempre se congela justo a 1 beat, vaya la bola a la velocidad que vaya.
///
/// La detección es por SEGMENTO BARRIDO (posición anterior → actual contra el jugador), así que por
/// muy rápida que vaya es imposible que se cuele.
///
/// La instancia BossController y llama a Init().
/// </summary>
[AddComponentMenu("Boss/Boss Homing Fireball")]
public class BossHomingFireball : MonoBehaviour
{
    [Header("Movimiento")]
    [Tooltip("Velocidad (m/s).")]
    [SerializeField] private float _speed = 16f;
    [Tooltip("Homing SUAVE: grados/segundo que puede corregir mientras persigue. Sale ya apuntando al punto\n" +
             "de intercepción, así que con poco giro sigue llegando, pero se puede esquivar con más margen.")]
    [SerializeField] private float _homingTurnDeg = 45f;

    [Header("Letal")]
    [SerializeField] private float _killRadius = 1.1f;

    [Header("Objetivo")]
    [Tooltip("Offset sobre el jugador al que apunta (pecho).")]
    [SerializeField] private Vector3 _aimOffset = new Vector3(0f, 1f, 0f);

    private BossController _boss;
    private Transform     _player;
    private double        _homingEndDSP, _deathDSP;
    private float         _straightDist;
    private Vector3       _dir, _prevPos, _prevAim;
    private Vector3       _playerVel, _lastPlayerPos;   // velocidad medida del jugador (para interceptarlo)
    private bool          _straight, _init, _dead;

    /// <summary>Configura la bola: persigue como mucho homingBeats y va recta al estar a straightBeats (≈1) de DISTANCIA.</summary>
    public void Init(BossController boss, float homingBeats, float straightBeats)
    {
        _boss   = boss;
        _player = boss.PlayerT;
        double beatDur = boss.BeatDuration;
        double now = AudioSettings.dspTime;

        // Distancia que la bola recorre en 'straightBeats' beats = "1 beat de distancia" del jugador.
        _straightDist = _speed * Mathf.Max(0.01f, straightBeats) * (float)beatDur;
        // Tope de seguridad por si nunca se acerca (no puede perseguir eternamente) + margen de despawn.
        _homingEndDSP = now + homingBeats * beatDur;
        _deathDSP     = now + (homingBeats + straightBeats + 4f) * beatDur;

        // Velocidad inicial estimada: la de avance del auto-runner (luego se mide frame a frame).
        var pm = _player != null ? _player.GetComponent<PlayerMovement>() : null;
        _playerVel     = pm != null ? pm.RunForward * pm.walkSpeed : Vector3.zero;
        _lastPlayerPos = _player != null ? _player.position : Vector3.zero;

        _dir     = _player != null ? (Intercept() - transform.position).normalized : transform.forward;
        _prevPos = transform.position;
        _prevAim = AimPoint();
        _init    = true;
    }

    private Vector3 AimPoint() => _player != null ? _player.position + _aimOffset : transform.position;

    /// <summary>
    /// Punto de INTERCEPCIÓN: dónde estará el pecho del jugador cuando llegue la bola. El jugador corre hacia
    /// el jefe, así que apuntar a donde está AHORA hacía que la bola (que baja desde la boca) se cruzase con él
    /// antes de haber bajado → le pasaba por encima. Solo se usa la velocidad horizontal (los saltos no cuentan).
    /// </summary>
    private Vector3 Intercept()
    {
        Vector3 v = _playerVel; v.y = 0f;
        Vector3 aim = AimPoint();
        for (int i = 0; i < 4; i++)
        {
            float t = Vector3.Distance(transform.position, aim) / Mathf.Max(0.1f, _speed);
            aim = AimPoint() + v * t;
        }
        return aim;
    }

    private void Update()
    {
        if (!_init || _dead) return;
        double now = AudioSettings.dspTime;
        float  dt  = Time.deltaTime;

        // Medir la velocidad real del jugador (suavizada) para la intercepción.
        if (_player != null && dt > 0f)
        {
            Vector3 measured = (_player.position - _lastPlayerPos) / dt;
            _playerVel = Vector3.Lerp(_playerVel, measured, 0.2f);
            _lastPlayerPos = _player.position;
        }

        // A 1 beat del IMPACTO (o si se agota el tope de persecución): congelar la dirección hacia el punto
        // de intercepción y dejar de perseguir → va recta. Si el jugador cambia de carril o salta, la esquiva.
        if (!_straight && _player != null)
        {
            float distToImpact = Vector3.Distance(transform.position, Intercept());
            if (distToImpact <= _straightDist || now >= _homingEndDSP)
                _straight = true;   // deja de corregir: sigue recta en la dirección que llevaba
        }

        // Homing SUAVE hacia el punto de intercepción (giro limitado) mientras no vaya recta.
        if (!_straight && _player != null)
        {
            Vector3 want = (Intercept() - transform.position).normalized;
            _dir = Vector3.RotateTowards(_dir, want, _homingTurnDeg * Mathf.Deg2Rad * dt, 0f).normalized;
        }

        transform.position += _dir * (_speed * dt);
        if (_dir.sqrMagnitude > 1e-6f) transform.forward = _dir;

        // Letal por segmento barrido en MOVIMIENTO RELATIVO (bola y jugador se mueven a la vez, a ~28 m/s
        // de acercamiento): así no se cuela entre dos frames.
        Vector3 aimNow = AimPoint();
        if (!_dead && _player != null &&
            DistSegPoint(_prevPos - _prevAim, transform.position - aimNow, Vector3.zero) <= _killRadius)
        {
            _dead = true;
            _boss.KillPlayer();
            Destroy(gameObject);
            return;
        }
        _prevPos = transform.position;
        _prevAim = aimNow;

        // Despawn de seguridad tras pasar la ventana de impacto.
        if (now >= _deathDSP) Destroy(gameObject);
    }

    /// <summary>Distancia mínima del segmento a→b al punto p (para no tunelear).</summary>
    private static float DistSegPoint(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }
}
