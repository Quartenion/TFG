using UnityEngine;

/// <summary>
/// Hace MORTAL solo la CARA DE ABAJO de un bloque que sube y baja (aplastador), sin tocar sus colliders:
/// los lados y la parte de arriba siguen siendo sólidos y seguros.
///
/// Por qué hace falta: cuando un bloque (movido por transform, p. ej. BeatOscillator) baja sobre el jugador, el
/// CharacterController no puede "morir" solo: el bloque lo empuja y a veces lo atraviesa por debajo del suelo.
/// Este componente mira cada frame, DESPUÉS de que el bloque se haya movido, si la cabeza del jugador está pegada a
/// (o metida en) la cara inferior del bloque y, si es así, lo mata antes de que la física lo empuje.
///
/// SETUP: añádelo al bloque que se mueve (el que tiene el Collider). Nada más.
/// </summary>
[AddComponentMenu("Level/Crush Bottom (cara inferior mortal)")]
[DefaultExecutionOrder(1000)]   // (después de los osciladores que mueven el bloque)
public class CrushBottom : MonoBehaviour
{
    [Tooltip("Collider del bloque. Vacío = el de este objeto (o el primero de sus hijos).")]
    [SerializeField] private Collider _blockCollider;
    [Tooltip("Margen (m) por debajo de la cara inferior en el que ya se considera que te aplasta.")]
    [SerializeField] private float _killMargin = 0.15f;
    [Tooltip("Margen (m) hacia dentro en los bordes: rozar el canto del bloque NO mata (así los lados son seguros).")]
    [SerializeField] private float _edgeInset = 0.2f;
    [Tooltip("Solo mata mientras el bloque BAJA (si está quieto o subiendo, tocar su cara de abajo no mata).")]
    [SerializeField] private bool _onlyWhileMovingDown = false;

    private CharacterController _cc;
    private GameOverManager _gom;
    private float _lastBottomY;
    private bool _killed;

    private void Start()
    {
        if (_blockCollider == null) _blockCollider = GetComponent<Collider>();
        if (_blockCollider == null) _blockCollider = GetComponentInChildren<Collider>();
        var pm = FindAnyObjectByType<PlayerMovement>();
        if (pm != null) _cc = pm.GetComponent<CharacterController>();
        _gom = FindAnyObjectByType<GameOverManager>();
        if (_blockCollider != null) _lastBottomY = _blockCollider.bounds.min.y;
    }

    private void LateUpdate()
    {
        if (_blockCollider == null || _cc == null || !_cc.enabled) return;
        if (TimeRewinder.IsRewinding) { _killed = false; return; }

        Bounds b = _blockCollider.bounds;
        float bottom = b.min.y;
        bool movingDown = bottom < _lastBottomY - 0.0001f;
        _lastBottomY = bottom;
        if (_onlyWhileMovingDown && !movingDown) return;

        Bounds p = _cc.bounds;
        Vector3 pc = p.center;

        // ¿El jugador está DEBAJO del bloque (su centro dentro de la huella, sin contar los bordes)?
        bool underFootprint = pc.x > b.min.x + _edgeInset && pc.x < b.max.x - _edgeInset &&
                              pc.z > b.min.z + _edgeInset && pc.z < b.max.z - _edgeInset;
        // ¿Su cabeza toca (o ya está metida en) la cara de abajo, y sus pies siguen por debajo de ella?
        bool headAtBottom = p.max.y >= bottom - _killMargin && p.min.y < bottom;

        if (underFootprint && headAtBottom)
        {
            if (_killed) return;
            if (_gom != null) _killed = _gom.TryKillByObstacle(gameObject);   // (con Rebobinado, rebobina)
        }
        else _killed = false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Collider c = _blockCollider != null ? _blockCollider : GetComponent<Collider>();
        if (c == null) return;
        Bounds b = c.bounds;
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.5f);
        Gizmos.DrawCube(new Vector3(b.center.x, b.min.y - _killMargin * 0.5f, b.center.z),
                        new Vector3(Mathf.Max(0.01f, b.size.x - 2f * _edgeInset), _killMargin, Mathf.Max(0.01f, b.size.z - 2f * _edgeInset)));
    }
#endif
}
