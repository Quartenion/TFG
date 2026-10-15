using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ANTI-ATASCO EN OBSTÁCULOS (lo añade PlayerMovement solo, en el jugador).
/// Caso: el jugador va deslizándose bajo un techo bajo y salta (o se levanta) antes de salir de debajo. El
/// CharacterController recupera su altura DENTRO del obstáculo y se queda atascado.
/// Solución: si el jugador está METIDO en un obstáculo (layer «Obstacle») cuya cara INFERIOR no es letal, se ignora la
/// colisión con ese obstáculo hasta que el jugador sale de él: sigue avanzando como si no estuviera.
/// Los obstáculos con la cara inferior letal no se tocan (esos deben matar, no dejarse atravesar).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ObstacleUnstuck : MonoBehaviour
{
    private CharacterController _cc;
    private PlayerMovement _pm;
    private int _mask;
    private readonly Collider[] _hits = new Collider[16];
    private readonly HashSet<Collider> _ignored = new HashSet<Collider>();
    private readonly List<Collider> _toRestore = new List<Collider>();
    private readonly HashSet<Collider> _inside = new HashSet<Collider>();

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _pm = GetComponent<PlayerMovement>();
        int layer = LayerMask.NameToLayer("Obstacle");
        _mask = layer >= 0 ? 1 << layer : 0;
    }

    private void LateUpdate()
    {
        if (_mask == 0 || _cc == null || !_cc.enabled) return;
        if (_pm != null && (_pm.IsFreeroam || _pm.IsOnKayak || _pm.IsOnZipline)) return;

        // Cápsula del CharacterController un poco encogida: solo cuenta si está METIDO, no si lo roza.
        float r = Mathf.Max(0.05f, _cc.radius - 0.05f);
        float half = Mathf.Max(0f, _cc.height * 0.5f - _cc.radius);
        Vector3 c = transform.TransformPoint(_cc.center);
        Vector3 a = c + Vector3.up * half, b = c - Vector3.up * half;

        _inside.Clear();
        int n = Physics.OverlapCapsuleNonAlloc(a, b, r, _hits, _mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider col = _hits[i];
            if (col == null || col.isTrigger) continue;
            _inside.Add(col);
            if (_ignored.Contains(col) || BottomIsLethal(col)) continue;
            Physics.IgnoreCollision(_cc, col, true);
            _ignored.Add(col);
        }

        // Ya ha salido: el obstáculo vuelve a ser sólido.
        _toRestore.Clear();
        foreach (Collider col in _ignored)
            if (col == null || !_inside.Contains(col)) _toRestore.Add(col);
        foreach (Collider col in _toRestore)
        {
            if (col != null) Physics.IgnoreCollision(_cc, col, false);
            _ignored.Remove(col);
        }
    }

    private static bool BottomIsLethal(Collider col)
    {
        ObstacleInfo info = col.GetComponentInParent<ObstacleInfo>();
        // (Sin ObstacleInfo se usan las caras por defecto, que incluyen la inferior: no se atraviesa.)
        return info == null || (info.lethalFaces & LethalFaces.Bottom) != 0;
    }

    private void OnDisable()
    {
        foreach (Collider col in _ignored) if (col != null && _cc != null) Physics.IgnoreCollision(_cc, col, false);
        _ignored.Clear();
    }
}
