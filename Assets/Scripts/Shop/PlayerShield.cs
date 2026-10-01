using UnityEngine;

/// <summary>
/// ESCUDO (consumible de la tienda): aguanta UN golpe letal de obstáculo / ataque. El obstáculo que te habría matado
/// se ROMPE en pedazos y deja de tener colisión (así no te quedas atascado contra él). Tras gastarlo hay un margen de
/// 0.6 s en el que otro choque en el mismo sitio tampoco mata (y no gasta otro escudo) — solo para que un mismo
/// impacto no cuente dos veces. No protege de caer al agua / lava ni de quedarte sin sangre.
/// </summary>
public static class PlayerShield
{
    private static float _graceUntil = -1f;
    private const float GraceSeconds = 0.6f;

    public static int Count => PlayerProgress.GetCount(ShopIds.Shield);

    /// <summary>
    /// Intenta parar un golpe letal causado por 'obstacle' (puede ser null: p. ej. un ataque del jefe).
    /// Devuelve true si el escudo lo ha parado (no hay que morir).
    /// </summary>
    public static bool TryAbsorb(GameObject obstacle)
    {
        if (Time.time < _graceUntil)
        {
            Neutralize(obstacle);
            return true;
        }
        if (!PlayerProgress.TryUseConsumable(ShopIds.Shield)) return false;
        RunStats.HelpUsed();

        _graceUntil = Time.time + GraceSeconds;
        Neutralize(obstacle);
        int left = Count;
        ReviveFX.ShowMessage(Loc.T("¡ESCUDO!", "SHIELD!"), left > 0 ? Loc.T($"Te quedan {left}", $"{left} left") : Loc.T("Era el último", "That was the last one"), new Color(0.2f, 0.55f, 1f, 0.55f));
        Debug.Log($"[Escudo] Golpe parado{(obstacle != null ? $" ('{obstacle.name}')" : "")}. Quedan {left}.");
        return true;
    }

    /// <summary>El obstáculo se rompe en pedazos (solo visual) y deja de colisionar.</summary>
    private static void Neutralize(GameObject obstacle)
    {
        if (obstacle == null) return;
        ObstacleInfo info = obstacle.GetComponentInParent<ObstacleInfo>();
        GameObject root = info != null ? info.gameObject : obstacle;

        foreach (Collider c in root.GetComponentsInChildren<Collider>()) c.enabled = false;

        bool any = false; Bounds b = default; Material mat = null;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            if (mat == null) mat = r.sharedMaterial;
            r.enabled = false;
        }
        if (!any) return;

        int n = Mathf.Clamp(Mathf.RoundToInt(b.size.magnitude * 2f), 6, 24);
        for (int i = 0; i < n; i++)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.name = "ShieldShard";
            Object.Destroy(s.GetComponent<Collider>());
            var sr = s.GetComponent<Renderer>();
            if (mat != null) sr.sharedMaterial = mat;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.transform.position   = b.center + Vector3.Scale(Random.insideUnitSphere, b.extents);
            s.transform.rotation   = Random.rotation;
            s.transform.localScale = Vector3.one * Mathf.Clamp(b.size.magnitude * 0.12f, 0.2f, 1.5f) * Random.Range(0.6f, 1.2f);
            var rb = s.AddComponent<Rigidbody>();
            rb.linearVelocity  = (s.transform.position - b.center).normalized * Random.Range(4f, 10f) + Vector3.up * Random.Range(2f, 8f);
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            Object.Destroy(s, 2.5f);
        }
    }
}
