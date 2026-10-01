using System;
using UnityEngine;

/// <summary>
/// VIDA EXTRA de la Fase 3: ocupa el sitio de un BloodSucker (verde, latiendo). Si el jugador la MIRA (GazeSwitch),
/// se la lleva: +1 vida extra. La coloca BossController y la mueve con la misma función de posición que a los
/// BloodSuckers (va con el jugador).
/// </summary>
public class BossLifeOrb : MonoBehaviour
{
    private Func<Vector3> _position;
    private Action _onCollected;
    private GazeSwitch _gaze;
    private Transform _visual;
    private bool _taken;
    private float _takeT;
    private Camera _cam;

    public static BossLifeOrb Create(Func<Vector3> position, Color color, float holdSeconds, float maxDistance,
                                     Camera cam, Action onCollected, params Transform[] ignoredRoots)
    {
        var go = new GameObject("BossLifeOrb");
        go.layer = 2;   // Ignore Raycast: la mirada solo busca esta capa → nada la tapa
        go.transform.position = position();
        var orb = go.AddComponent<BossLifeOrb>();
        orb._position = position;
        orb._onCollected = onCollected;
        orb._cam = cam;

        // Corazón / orbe verde que brilla
        GameObject v = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(v.GetComponent<Collider>());
        v.transform.SetParent(go.transform, false);
        v.transform.localScale = Vector3.one * 1.3f;
        var mr = v.GetComponent<Renderer>();
        var m = new Material(mr.sharedMaterial) { color = color };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", color * 1.5f);
        mr.sharedMaterial = m;
        orb._visual = v.transform;

        var sc = go.AddComponent<SphereCollider>();
        sc.radius = 1.1f;

        orb._gaze = go.AddComponent<GazeSwitch>();
        orb._gaze.SetObstruction(1 << 2);
        orb._gaze.Configure(holdSeconds, maxDistance, mr);
        if (cam != null) orb._gaze.SetCamera(cam);
        orb._gaze.SetIgnoredRoots(ignoredRoots);
        orb._gaze.OnActivated.AddListener(orb.Collect);
        return orb;
    }

    private void Update()
    {
        if (_taken)
        {
            // Vuela hacia la cámara y desaparece.
            _takeT += Time.deltaTime / 0.4f;
            if (_cam != null) transform.position = Vector3.Lerp(transform.position, _cam.transform.position, _takeT);
            _visual.localScale = Vector3.one * 1.3f * Mathf.Max(0f, 1f - _takeT);
            if (_takeT >= 1f) Destroy(gameObject);
            return;
        }
        if (_position != null) transform.position = _position();
        float p = _gaze != null ? _gaze.Progress : 0f;
        _visual.localScale = Vector3.one * 1.3f * (1f + 0.12f * Mathf.Sin(Time.time * (6f + 18f * p)) + 0.3f * p);
    }

    private void Collect()
    {
        if (_taken) return;
        _taken = true;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        _onCollected?.Invoke();
    }
}
