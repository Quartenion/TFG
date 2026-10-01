using UnityEngine;

/// <summary>
/// Pilar infernal del jefe (Fase 2, Ataque A). Emerge del suelo en un beat hasta su posición final y
/// se queda un rato antes de desaparecer. Es LETAL: si el CharacterController del jugador toca su caja
/// → game over. La esquiva sale por geometría según la altura del pilar (que fija BossController):
///   • Pilar CORTO → saltas y tus pies quedan por encima → no toca.
///   • Pilar ALTO  → no se puede saltar: te MUEVES al carril libre.
///
/// La detección es un overlap por frame (el pilar es estático una vez arriba, no hay tunneling).
/// La instancia BossController y llama a Init().
///
/// NOTA: haz el prefab con el PIVOTE EN LA BASE (para que al escalar Y crezca hacia arriba y la base
/// quede a ras de suelo).
/// </summary>
[AddComponentMenu("Boss/Boss Pillar")]
public class BossPillar : MonoBehaviour
{
    [Header("Subida")]
    [Tooltip("Beats que tarda en emerger del todo.")]
    [SerializeField] private float _riseBeats = 1f;
    [Tooltip("Metros por debajo desde los que emerge (empieza escondido).")]
    [SerializeField] private float _riseFrom = 25f;
    [Tooltip("Beats que se queda arriba antes de desaparecer.")]
    [SerializeField] private float _lifeBeats = 3f;

    [Header("Letal")]
    [Tooltip("Renderer cuya caja define el pilar. Vacío = el primero que encuentre en hijos.")]
    [SerializeField] private Renderer _hitVolume;

    private BossController      _boss;
    private CharacterController _playerCC;
    private PlayerMovement      _pm;
    private Vector3 _topPos;
    private double  _riseStartDSP, _riseEndDSP, _deathDSP;
    private bool    _init, _dead, _hitDone;

    private const float DespawnBehind = 30f;   // se destruye al quedar estos metros por detrás del jugador

    public void Init(BossController boss, double riseStartDSP)
    {
        _boss = boss;
        if (boss != null && boss.PlayerT != null)
        {
            _playerCC = boss.PlayerT.GetComponent<CharacterController>();
            _pm       = boss.PlayerT.GetComponent<PlayerMovement>();
        }
        if (_hitVolume == null) _hitVolume = GetComponentInChildren<Renderer>();

        _topPos = transform.position;   // colocado por BossController en su posición final (arriba)
        double beatDur = boss != null ? boss.BeatDuration : 0.5;
        _riseStartDSP = riseStartDSP;
        _riseEndDSP   = riseStartDSP + _riseBeats * beatDur;
        // Fallback GENEROSO: nunca debe morir antes de que el jugador llegue (el despawn real es al pasarlo).
        _deathDSP     = _riseEndDSP + (_lifeBeats + 12f) * beatDur;

        transform.position = _topPos - Vector3.up * _riseFrom;   // empieza escondido bajo el suelo

        // Los pinchos del jefe NO son sólidos: el jugador (y el kayak) los ATRAVIESA; al tocarlos solo recibe el golpe
        // de sangre del jefe (un cuarto de la sangre total, una vez por pincho). Sin colliders tampoco los detecta
        // ObstacleCollision ni el choque del kayak, que antes daban game over directo.
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        _init = true;
    }

    private void Update()
    {
        if (!_init || _dead) return;
        double now = AudioSettings.dspTime;

        // Subida (ease-out).
        float u = _riseEndDSP > _riseStartDSP
            ? Mathf.Clamp01((float)((now - _riseStartDSP) / (_riseEndDSP - _riseStartDSP)))
            : 1f;
        float eased = 1f - (1f - u) * (1f - u);
        transform.position = _topPos - Vector3.up * (_riseFrom * (1f - eased));

        // Si el jugador lo atraviesa (le toca en cualquier punto), recibe el golpe del jefe — una sola vez por pincho.
        if (!_hitDone && TouchesPlayer())
        {
            _hitDone = true;
            if (_boss != null) _boss.KillPlayer();   // (ahora es un golpe de sangre, no game over)
        }

        // Despawn cuando el jugador ya lo ha PASADO (así no desaparece antes de llegar a él).
        if (_pm != null && Vector3.Dot(_pm.transform.position - transform.position, _pm.RunForward) > DespawnBehind)
        {
            Destroy(gameObject);
            return;
        }

        if (now >= _deathDSP) Destroy(gameObject);   // fallback de seguridad
    }

    private const float TopGrace         = 0.3f;   // con los pies por encima de (techo − esto) estás ENCIMA: no mata

    /// <summary>¿La caja del pincho toca al jugador? (sin colliders: se atraviesa). Saltar por encima lo esquiva.</summary>
    private bool TouchesPlayer()
    {
        if (_hitVolume == null) return false;
        Bounds pb = _hitVolume.bounds;
        Bounds cb;
        if (_playerCC != null && _playerCC.enabled) cb = _playerCC.bounds;
        else if (_pm != null) cb = new Bounds(_pm.transform.position + Vector3.up * 0.9f, new Vector3(0.8f, 1.8f, 0.8f));   // (en el kayak)
        else return false;
        // Un pelín de margen para que rozar el borde o pasar justo por encima no cuente.
        pb.Expand(new Vector3(-0.2f, -2f * TopGrace, -0.2f));
        return pb.Intersects(cb);
    }

}
