using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Objeto(s) que FLOTAN con una animación suave: bob vertical + ligero balanceo.
/// Opcionalmente reacciona cuando el jugador SALTA cerca (pequeño hundimiento con muelle).
///
/// COMPATIBLE CON BeatOscillator (u otros movers) EN EL MISMO OBJETO:
///   FloatingObject SUMA su bob POR ENCIMA de lo que otro script haya puesto ese frame, sin
///   pelearse. Lo hace en LateUpdate y de forma robusta: cada frame recupera la "base" (la
///   posición sin nuestro propio bob) y vuelve a sumar el bob nuevo. Así:
///     • Si el oscilador está MOVIÉNDOSE, flotamos encima de su movimiento.
///     • Si el oscilador está QUIETO (p.ej. en la cuenta atrás 3-2-1, antes de la música),
///       NO se acumula el bob (antes crecía sin control → "saltos" enormes).
///     • Si no hay ningún otro mover, flotamos alrededor de la posición de reposo.
///
/// DOS FORMAS DE USARLO:
///   • Uno por objeto.
///   • Un solo componente en el PADRE con "Affect Children": anima cada hijo por separado.
///     IMPORTANTE: si los bloques a animar son los HIJOS, usa Affect Children. Si no, el padre
///     se anima a sí mismo y su balanceo hace que los hijos lejanos (lejos del pivote) se
///     muevan con muchísima amplitud (efecto palanca).
///
/// Se mueve por transform (no necesita Rigidbody).
/// </summary>
[AddComponentMenu("Level/Floating Object")]
public class FloatingObject : MonoBehaviour
{
    [Header("Objetivos")]
    [Tooltip("Si está activo, anima TODOS los hijos directos (cada uno con su fase, cada uno\n" +
             "alrededor de SU propio pivote). Actívalo si el componente está en el PADRE que\n" +
             "agrupa los bloques que quieres que floten.")]
    [SerializeField] private bool _affectChildren = false;

    [Header("Animación de flote (idle)")]
    [Tooltip("Amplitud del bob vertical en metros.")]
    [SerializeField] private float _bobAmplitude = 0.15f;

    [Tooltip("Frecuencia del bob vertical en Hz.")]
    [SerializeField] private float _bobFrequency = 0.8f;

    [Tooltip("Amplitud del balanceo (cabeceo/alabeo) en grados. 0 = sin balanceo.")]
    [SerializeField] private float _swayAmplitude = 4f;

    [Tooltip("Frecuencia del balanceo en Hz.")]
    [SerializeField] private float _swayFrequency = 0.6f;

    [Tooltip("Desfasa la animación de forma aleatoria en cada objeto para que no floten sincronizados.")]
    [SerializeField] private bool _randomizePhase = true;

    [Header("Reacción al salto del jugador (opcional)")]
    [Tooltip("Si está activo, el flotador se hunde un poco cuando el jugador salta cerca.")]
    [SerializeField] private bool _reactToPlayerJump = true;

    [Tooltip("Radio (m) dentro del cual el salto del jugador afecta al flotador.")]
    [SerializeField] private float _reactRadius = 6f;

    [Tooltip("Impulso hacia abajo (m/s) de la reacción.")]
    [SerializeField] private float _reactImpulse = 2f;

    [Tooltip("La reacción es más fuerte cuanto más cerca está el jugador.")]
    [SerializeField] private bool _falloffWithDistance = true;

    [Tooltip("Rigidez del muelle que devuelve el flotador a su sitio.")]
    [SerializeField] private float _springStiffness = 40f;

    [Tooltip("Amortiguación del muelle.")]
    [SerializeField] private float _springDamping = 6f;

    [Header("Referencias")]
    [Tooltip("PlayerMovement de la escena. Se busca automáticamente si se deja vacío.")]
    [SerializeField] private PlayerMovement _player;

    // ── Estado interno ────────────────────────────────────────────────────────

    private class Floater
    {
        public Transform  t;
        public Quaternion baseRot;       // rotación base sobre la que se aplica el balanceo
        public float      phase;
        public float      springOffset;  // hundimiento actual de la reacción
        public float      springVel;

        // Composición: para recuperar la base sin acumular nuestro propio bob.
        public Vector3 lastOffset;       // offset (bob + reacción) que aplicamos el frame anterior
        public Vector3 lastFinal;        // posición final que escribimos el frame anterior
        public bool    initialized;
    }

    private readonly List<Floater> _floaters = new List<Floater>();
    private bool  _subscribed;
    private const float TAU = Mathf.PI * 2f;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        foreach (Transform tr in CollectTargets())
        {
            // Un FloatingObject duplicado en el mismo objeto siempre sobra (dos bobs peleándose).
            foreach (FloatingObject fo in tr.GetComponents<FloatingObject>())
                if (fo != this && fo.enabled) fo.enabled = false;

            _floaters.Add(new Floater
            {
                t       = tr,
                baseRot = tr.rotation,
                phase   = _randomizePhase ? Random.Range(0f, 1000f) : 0f,
            });
        }
    }

    private void OnEnable()
    {
        ResolvePlayer();
        Subscribe();
    }

    private void Start()
    {
        ResolvePlayer();
        Subscribe();
    }

    private void OnDisable()
    {
        if (_subscribed && _player != null)
        {
            _player.OnJumpExecuted.RemoveListener(OnPlayerJump);
            _subscribed = false;
        }
    }

    // Se anima en LateUpdate para SUMAR el bob DESPUÉS de que otro mover (BeatOscillator) haya
    // colocado el objeto ese frame en Update.
    private void LateUpdate()
    {
        float t  = Time.time;
        float dt = Time.deltaTime;

        for (int i = 0; i < _floaters.Count; i++)
        {
            Floater f = _floaters[i];
            if (f.t == null) continue;

            // Muelle de la reacción al salto (decae a 0).
            if (f.springOffset != 0f || f.springVel != 0f)
            {
                f.springVel    += (-_springStiffness * f.springOffset - _springDamping * f.springVel) * dt;
                f.springOffset += f.springVel * dt;
                if (Mathf.Abs(f.springOffset) < 0.0005f && Mathf.Abs(f.springVel) < 0.0005f)
                {
                    f.springOffset = 0f;
                    f.springVel    = 0f;
                }
            }

            float bob = Mathf.Sin((t + f.phase) * _bobFrequency * TAU) * _bobAmplitude;
            Vector3 offset = Vector3.up * (bob + f.springOffset);

            // BASE = posición sin NUESTRO bob.
            //  · Si el transform sigue EXACTAMENTE donde lo dejamos → nadie más lo movió este
            //    frame → le quitamos nuestro offset anterior para recuperar la base (no acumula).
            //  · Si está en otro sitio → un mover (BeatOscillator) lo recolocó → esa es la base.
            Vector3 cur = f.t.position;
            Vector3 basePos = (f.initialized && (cur - f.lastFinal).sqrMagnitude < 1e-6f)
                ? cur - f.lastOffset
                : cur;

            Vector3 finalPos = basePos + offset;
            f.t.position   = finalPos;
            f.lastOffset   = offset;
            f.lastFinal    = finalPos;
            f.initialized  = true;

            // Balanceo (rotación). Los movers de este proyecto no rotan; lo aplicamos sobre la
            // rotación base capturada al inicio (absoluto, no acumula).
            if (_swayAmplitude > 0f)
            {
                float pitch = Mathf.Sin((t + f.phase) * _swayFrequency * TAU) * _swayAmplitude;
                float roll  = Mathf.Sin((t + f.phase) * _swayFrequency * TAU * 0.8f + 1.1f) * _swayAmplitude;
                f.t.rotation = f.baseRot * Quaternion.Euler(pitch, 0f, roll);
            }
        }
    }

    // ── Reacción al salto ───────────────────────────────────────────────────────

    private void OnPlayerJump()
    {
        if (!_reactToPlayerJump || _player == null) return;

        Vector3 pp = _player.transform.position;
        for (int i = 0; i < _floaters.Count; i++)
        {
            Floater f = _floaters[i];
            if (f.t == null) continue;

            float dist = Vector3.Distance(pp, f.t.position);
            if (dist > _reactRadius) continue;

            float strength = _falloffWithDistance
                ? Mathf.Clamp01(1f - dist / Mathf.Max(0.01f, _reactRadius))
                : 1f;
            f.springVel -= _reactImpulse * strength;
        }
    }

    /// <summary>Provoca una reacción de hundimiento en todos los flotadores (0..1).</summary>
    public void React(float strength = 1f)
    {
        float s = Mathf.Max(0f, strength);
        for (int i = 0; i < _floaters.Count; i++)
            _floaters[i].springVel -= _reactImpulse * s;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private List<Transform> CollectTargets()
    {
        var list = new List<Transform>();
        if (_affectChildren)
        {
            foreach (Transform c in transform) list.Add(c);
            if (list.Count == 0) list.Add(transform);
        }
        else
        {
            list.Add(transform);
        }
        return list;
    }

    private void ResolvePlayer()
    {
        if (_player == null) _player = FindAnyObjectByType<PlayerMovement>();
    }

    private void Subscribe()
    {
        if (_subscribed || _player == null || _player.OnJumpExecuted == null) return;
        _player.OnJumpExecuted.AddListener(OnPlayerJump);
        _subscribed = true;
    }

    // ── Gizmos ──────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!_reactToPlayerJump) return;

        List<Transform> targets = new List<Transform>();
        if (Application.isPlaying && _floaters.Count > 0)
        {
            foreach (Floater f in _floaters) if (f.t != null) targets.Add(f.t);
        }
        else if (_affectChildren)
        {
            foreach (Transform c in transform) targets.Add(c);
        }
        if (targets.Count == 0) targets.Add(transform);

        foreach (Transform tr in targets)
        {
            UnityEditor.Handles.color = new Color(0.3f, 0.7f, 1f, 0.18f);
            UnityEditor.Handles.DrawSolidDisc(tr.position, Vector3.up, _reactRadius);
            UnityEditor.Handles.color = new Color(0.3f, 0.7f, 1f, 0.6f);
            UnityEditor.Handles.DrawWireDisc(tr.position, Vector3.up, _reactRadius);
        }
    }
#endif
}
