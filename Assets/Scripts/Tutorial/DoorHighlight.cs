using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RESALTA UNA PUERTA de la sala final (la grande, al acabar el tutorial / cuando toca la siguiente parte del estudio):
/// sus hojas brillan en dorado latiendo y una luz cálida la ilumina desde dentro de la sala, para que se vea cuál es.
/// Se pone con <see cref="On"/> y se quita con <see cref="Off"/> (devuelve los materiales como estaban).
/// </summary>
public class DoorHighlight : MonoBehaviour
{
    private static readonly Color Gold = new Color(1f, 0.75f, 0.25f);
    private readonly List<(Renderer r, Material[] shared)> _orig = new List<(Renderer, Material[])>();
    private readonly List<Material> _mats = new List<Material>();
    private readonly List<Color> _baseEmission = new List<Color>();
    private Light _light;

    /// <summary>Resalta la puerta (sus hojas; si no tiene, todo lo que cuelga de ella).</summary>
    public static DoorHighlight On(CourseDoor door)
    {
        if (door == null) return null;
        var h = door.GetComponent<DoorHighlight>();
        if (h == null) h = door.gameObject.AddComponent<DoorHighlight>();
        h.Build(door);
        return h;
    }

    public static void Off(CourseDoor door)
    {
        if (door == null) return;
        var h = door.GetComponent<DoorHighlight>();
        if (h != null) Destroy(h);
    }

    private void Build(CourseDoor door)
    {
        if (_mats.Count > 0) return;
        var renderers = new List<Renderer>();
        foreach (Transform leaf in door.LeafTransforms)
            if (leaf != null) renderers.AddRange(leaf.GetComponentsInChildren<Renderer>());
        if (renderers.Count == 0) renderers.AddRange(door.GetComponentsInChildren<Renderer>());

        Bounds b = default; bool any = false;
        foreach (Renderer r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            _orig.Add((r, r.sharedMaterials));
            Material[] inst = r.materials;   // (copias: no se tocan los materiales del proyecto)
            foreach (Material m in inst)
            {
                m.EnableKeyword("_EMISSION");
                _mats.Add(m);
                _baseEmission.Add(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black);
            }
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }

        // Luz cálida delante de la puerta (hacia dentro de la sala).
        if (any)
        {
            var go = new GameObject("Luz (puerta resaltada)");
            go.transform.SetParent(transform, true);
            go.transform.position = b.center - door.Outward * 4f;
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = Gold;
            _light.range = Mathf.Max(12f, b.size.magnitude);
            _light.shadows = LightShadows.None;
        }
    }

    private void Update()
    {
        float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.5f);
        for (int i = 0; i < _mats.Count; i++)
            if (_mats[i] != null) _mats[i].SetColor("_EmissionColor", _baseEmission[i] + Gold * (0.6f + 1.6f * k));
        if (_light != null) _light.intensity = 2f + 4f * k;
    }

    private void OnDestroy()
    {
        foreach (var (r, shared) in _orig) if (r != null) r.sharedMaterials = shared;
        foreach (Material m in _mats) if (m != null) Destroy(m);
        if (_light != null) Destroy(_light.gameObject);
    }
}
