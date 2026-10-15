using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Proyectil del jefe (roca fundida / calavera). Sale del jefe, describe un arco con REBOTES y
/// aterriza en un CARRIL (X del mundo) en un beat objetivo. Como el jefe y el jugador avanzan a la
/// misma velocidad, el proyectil sigue la línea Z del jugador: el único esquive es LATERAL (cambiar
/// de carril).
///
/// Dos variantes (las decide BossController al lanzarlo):
///   • Normal: si el jugador está en el carril al aterrizar → pierde sangre. Esquiva moviéndote.
///   • DEFLECTABLE (color distinto): pulsa [Q] en el momento en que te llega para devolverlo al
///     jefe y hacerle 1 de daño. Es la ÚNICA forma de dañarlo en la Fase 1. Si no lo devuelves y
///     estás en el carril, te golpea; si te apartas, se pierde (sin daño a nadie).
///
/// No se coloca a mano: lo instancia BossController y llama a Init().
/// </summary>
[AddComponentMenu("Boss/Boss Projectile")]
public class BossProjectile : MonoBehaviour
{
    private enum State { Incoming, Returning, Done }

    [Header("Rebote (bota contra el suelo cada beat)")]
    [Tooltip("Altura del primer bote.")]
    [SerializeField] private float _hopHeight = 3f;
    [Tooltip("Cuánto decae la altura en cada bote (1 = todos iguales, 0.8 = cada uno al 80%).")]
    [Range(0f, 1f)]
    [SerializeField] private float _hopDecay = 0.85f;
    [Tooltip("Giro visual (grados/segundo). 0 = sin giro (si el modelo ya viene orientado).")]
    [SerializeField] private float _spinSpeed = 0f;
    [Tooltip("Radio del barril: bota con el CENTRO a esta altura sobre la plataforma (para que no se hunda).")]
    [SerializeField] private float _radius = 0.5f;

    [Header("Impacto")]
    [Tooltip("Radio lateral: si el jugador está a menos de esto del carril al aterrizar, le da.")]
    [SerializeField] private float _hitRadius = 2.2f;

    [Header("Deflect")]
    [Tooltip("Gracia (segundos) DESPUÉS de la llegada para pulsar [Q]. Antes de llegar hay 1 beat entero de ventana.")]
    [SerializeField] private float _deflectWindow = 0.18f;
    [Tooltip("Arco extra del proyectil al volver hacia el jefe.")]
    [SerializeField] private float _returnArc = 3f;

    [Header("Debug")]
    [Tooltip("Loguea las pulsaciones de [Q] y su desfase con la llegada.")]
    [SerializeField] private bool _debug = false;

    // ── Estado / config (via Init) ──────────────────────────────────────────
    private BossController _boss;
    private Transform      _player;
    private State          _state = State.Incoming;
    private bool           _init;

    private float   _laneX;
    private Vector3  _spawnPos;
    private double  _spawnDSP, _landDSP;
    private bool    _deflectable;
    private float   _lastGroundY;               // altura de la plataforma (pies del jugador en suelo)
    private CharacterController _playerCC;
    private float   _startZOffset;

    private bool    _life;                      // BARRIL DE VIDA: devuélvelo con [Q] → +1 vida extra (no hace daño)

    /// <summary>Lo convierte en el BARRIL DE VIDA (color de vida; [Q] a tiempo → vida extra; si no, se pierde).</summary>
    public void MakeLifeBarrel()
    {
        _life = true;
        _deflectable = true;
        var rend = GetComponentInChildren<Renderer>();
        if (rend != null && _boss != null)
        {
            rend.material.color = _boss.LifeColor;
            rend.material.EnableKeyword("_EMISSION");
            rend.material.SetColor("_EmissionColor", _boss.LifeColor * 0.8f);
        }
    }

    private Vector3 _returnStartPos;
    private double  _returnStartDSP, _returnEndDSP;

    /// <summary>Configura el proyectil recién instanciado.</summary>
    public void Init(BossController boss, float laneX, double landDSP, bool deflectable)
    {
        _boss        = boss;
        _player      = boss.PlayerT;
        _laneX       = laneX;
        _landDSP     = landDSP;
        _spawnDSP    = AudioSettings.dspTime;
        _spawnPos    = transform.position;
        _deflectable = deflectable;
        _playerCC    = _player != null ? _player.GetComponent<CharacterController>() : null;
        _lastGroundY = _playerCC != null ? _playerCC.bounds.min.y
                     : (_player != null ? _player.position.y : _spawnPos.y);
        _startZOffset = _player != null ? _spawnPos.z - _player.position.z : 0f;

        // Color según variante + RADIO automático: media altura real del modelo (para que no se hunda).
        var rend = GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            rend.material.color = deflectable ? boss.DeflectColor : boss.NormalColor;
            _radius = rend.bounds.extents.y;   // sobrescribe el valor del inspector con la altura real
        }

        // El barril NO es un obstáculo físico: su golpe lo decide ResolveArrival (golpe de sangre del jefe). Su
        // collider (el prefab está en la capa Obstacle) hacía que ObstacleCollision diera GAME OVER al tocarlo.
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

        _init = true;
    }

    private void Update()
    {
        if (!_init) return;
        double now = AudioSettings.dspTime;

        if (_spinSpeed != 0f) transform.Rotate(Vector3.one, _spinSpeed * Time.deltaTime, Space.Self);

        if (_state == State.Incoming)      UpdateIncoming(now);
        else if (_state == State.Returning) UpdateReturning(now);
    }

    // ── Vuelo hacia el jugador ────────────────────────────────────────────────

    private void UpdateIncoming(double now)
    {
        if (_player == null) { Destroy(gameObject); return; }

        double beatDur = _boss != null ? _boss.BeatDuration : 0.5;
        float u = _landDSP > _spawnDSP
            ? Mathf.Clamp01((float)((now - _spawnDSP) / (_landDSP - _spawnDSP)))
            : 1f;

        // X: del jefe al carril. Z: sigue la línea del jugador (avanza con él).
        float x = Mathf.Lerp(_spawnPos.x, _laneX, u);
        float z = _player.position.z + Mathf.Lerp(_startZOffset, 0f, u);

        // Suelo real: seguir los PIES del jugador cuando está en el suelo (sigue la plataforma que se
        // va generando e ignora los saltos; conserva el último suelo válido mientras está en el aire).
        if (_playerCC != null && _playerCC.isGrounded) _lastGroundY = _playerCC.bounds.min.y;
        float contactY = _lastGroundY + _radius;   // el CENTRO del barril bota a +radio (no se hunde)

        // Y: cae del jefe al suelo en el 1er beat y luego BOTA contra la plataforma en cada beat.
        double beatProgress = beatDur > 0 ? (now - _spawnDSP) / beatDur : 0.0;
        float y;
        if (beatProgress < 1.0)
        {
            y = Mathf.Lerp(_spawnPos.y, contactY, (float)beatProgress);   // caída inicial desde el jefe
        }
        else
        {
            float frac = (float)(beatProgress - System.Math.Floor(beatProgress));
            float hopH = _hopHeight * Mathf.Pow(Mathf.Clamp01(_hopDecay), (float)System.Math.Floor(beatProgress) - 1f);
            y = contactY + hopH * 4f * frac * (1f - frac);   // toca la plataforma en cada beat entero
        }
        transform.position = new Vector3(x, y, z);

        // Deflect: [Q] dentro de la ventana alrededor de la llegada.
        if (_deflectable && Keybinds.Pressed(Keybinds.Bind.Block))
        {
            // Ventana generosa: durante TODO el último beat de aproximación + un pelín tras la llegada.
            if (now >= _landDSP - beatDur && now <= _landDSP + _deflectWindow)
            {
                if (_debug) Debug.Log("[BossProjectile] ¡Deflect!", this);
                if (_life)
                {
                    _boss?.GiveExtraLife(Loc.T("¡Barril de vida!", "Life barrel!"));
                    Destroy(gameObject);
                    return;
                }
                StartReturn(now);
                return;
            }
            if (_debug) Debug.Log($"[BossProjectile] Q fuera de ventana (delta {(now - _landDSP):F2}s).", this);
        }

        if (u >= 1f) ResolveArrival();
    }

    private void ResolveArrival()
    {
        // No deflectado (o no era deflectable): si el jugador está en el carril, le golpea. (El de vida no hace daño.)
        if (!_life && _player != null && Mathf.Abs(_player.position.x - _laneX) <= _hitRadius)
            _boss?.HitPlayer();

        Destroy(gameObject);
    }

    // ── Vuelta al jefe (deflectado) ────────────────────────────────────────────

    private void StartReturn(double now)
    {
        _state          = State.Returning;
        _returnStartPos = transform.position;
        _returnStartDSP = now;
        _returnEndDSP   = now + _boss.DeflectReturnBeats * _boss.BeatDuration;
    }

    private void UpdateReturning(double now)
    {
        Vector3 target = _boss != null ? _boss.BossT.position : _returnStartPos;

        float u = _returnEndDSP > _returnStartDSP
            ? Mathf.Clamp01((float)((now - _returnStartDSP) / (_returnEndDSP - _returnStartDSP)))
            : 1f;

        float ease = u * u;   // acelera hacia el jefe
        Vector3 p = Vector3.Lerp(_returnStartPos, target, ease);
        p.y += _returnArc * Mathf.Sin(u * Mathf.PI);   // arco
        transform.position = p;

        if (u >= 1f)
        {
            _state = State.Done;
            _boss?.TakeDamage(1);
            Destroy(gameObject);
        }
    }
}
