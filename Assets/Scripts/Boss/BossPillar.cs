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
    private bool    _init, _dead;

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

        // Letal SOLO por la CARA FRONTAL (la que mira al jugador que viene corriendo). Rozar un lateral o
        // aterrizar encima NO mata (el collider del pilar ya te frena/sostiene).
        if (!_dead && HitsFrontFace())
        {
            _dead = true;
            if (_boss != null) _boss.KillPlayer();
            return;
        }

        // Despawn cuando el jugador ya lo ha PASADO (así no desaparece antes de llegar a él).
        if (_pm != null && Vector3.Dot(_pm.transform.position - transform.position, _pm.RunForward) > DespawnBehind)
        {
            Destroy(gameObject);
            return;
        }

        if (now >= _deathDSP) Destroy(gameObject);   // fallback de seguridad
    }

    private const float FrontContactDist = 0.3f;   // a esta distancia de la cara frontal ya cuenta como choque
    private const float SideGrace        = 0.2f;   // margen lateral: rozar la esquina/lateral no mata
    private const float TopGrace         = 0.3f;   // con los pies por encima de (techo − esto) estás ENCIMA: no mata

    /// <summary>
    /// ¿El jugador choca con la CARA FRONTAL? En el eje de avance: su frente ha llegado a la cara frontal del
    /// pilar y su espalda sigue por delante de ella. En lateral: solapa con el pilar (menos un margen, así rozar
    /// el lateral no cuenta). En vertical: sus pies están por debajo del techo del pilar (si está encima, no).
    /// </summary>
    private bool HitsFrontFace()
    {
        if (_hitVolume == null || _playerCC == null) return false;

        Vector3 f = _pm != null ? _pm.RunForward : Vector3.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
        f.Normalize();
        Vector3 r = Vector3.Cross(Vector3.up, f);   // eje lateral

        Bounds pb = _hitVolume.bounds;
        Bounds cb = _playerCC.bounds;

        // Proyecciones de las cajas (AABB) sobre un eje: centro ± semiancho proyectado.
        float Half(Bounds b, Vector3 axis) =>
            Mathf.Abs(axis.x) * b.extents.x + Mathf.Abs(axis.y) * b.extents.y + Mathf.Abs(axis.z) * b.extents.z;

        // Eje de avance: la cara frontal es la más cercana al jugador que llega.
        float pillarFront = Vector3.Dot(pb.center, f) - Half(pb, f);
        float playerLead  = Vector3.Dot(cb.center, f) + Half(cb, f);
        float playerTrail = Vector3.Dot(cb.center, f) - Half(cb, f);
        bool atFront = playerLead >= pillarFront - FrontContactDist && playerTrail < pillarFront;

        // Lateral: tiene que estar DELANTE de la cara, no al lado.
        float pc = Vector3.Dot(pb.center, r), ph = Half(pb, r) - SideGrace;
        float cc = Vector3.Dot(cb.center, r), chh = Half(cb, r);
        bool inLane = ph > 0f && Mathf.Abs(pc - cc) < ph + chh;

        // Vertical: por debajo del techo (si va por encima o aterriza en él, no es un choque frontal).
        bool belowTop = cb.min.y < pb.max.y - TopGrace;

        return atFront && inLane && belowTop;
    }
}
