using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zona de corriente de agua. Añade este componente directamente al GameObject del
/// tramo del río (el que ya tiene MeshCollider o BoxCollider por ser geometría de nivel).
///
/// La detección es puramente horizontal (XZ): si el kayak está dentro de la huella
/// en planta del collider, la corriente se aplica — el eje Y se ignora por completo,
/// de modo que no importa que el mesh del río esté bajo el agua y el kayak flote en
/// la superficie.
///
/// NOTA: si el script anterior te añadió un BoxCollider automáticamente, puedes
/// eliminarlo — ya no se necesita.
/// </summary>
[AddComponentMenu("Level/Water Current Zone")]
public class WaterCurrentZone : MonoBehaviour
{
    public enum Direction { Left, Right }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Corriente")]
    [Tooltip("Dirección en que el agua empuja el kayak (relativa al avance del ataúd).")]
    public Direction direction = Direction.Right;

    [Tooltip("Velocidad de empuje en m/s.\n" +
             "Con valores menores que steerLateralSpeed el jugador puede compensarla;\n" +
             "con valores mayores la corriente es irresistible.")]
    [Range(0f, 12f)]
    public float strength = 2f;

    // ── Registro global ───────────────────────────────────────────────────────

    private static readonly List<WaterCurrentZone> _all = new List<WaterCurrentZone>();
    public static IReadOnlyList<WaterCurrentZone> All => _all;

    // ── Estado interno ────────────────────────────────────────────────────────

    private Collider _col;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Preferir MeshCollider (geometría de nivel típica de ProBuilder);
        // si no hay, usar cualquier otro collider del objeto.
        _col = GetComponent<MeshCollider>();
        if (_col == null) _col = GetComponent<Collider>();

        if (_col == null)
            Debug.LogWarning($"[WaterCurrentZone] '{name}' no tiene ningún Collider. " +
                             "Añade un MeshCollider o BoxCollider al tramo del río.", this);
    }

    private void OnEnable()  => _all.Add(this);
    private void OnDisable() => _all.Remove(this);

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// True si <paramref name="worldPoint"/> está dentro de la huella XZ del collider.
    /// El eje Y se ignora: el kayak flota en superficie, no dentro del mesh del río.
    /// </summary>
    public bool Contains(Vector3 worldPoint)
    {
        if (_col == null) _col = GetComponent<MeshCollider>() ?? GetComponent<Collider>();
        if (_col == null) return false;

        Bounds b = _col.bounds; // AABB en world space
        return worldPoint.x >= b.min.x && worldPoint.x <= b.max.x &&
               worldPoint.z >= b.min.z && worldPoint.z <= b.max.z;
    }

    /// <summary>Velocidad lateral con signo: positivo = derecha, negativo = izquierda.</summary>
    public float SignedStrength => strength * (direction == Direction.Right ? 1f : -1f);

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()         => DrawZoneGizmo(false);
    private void OnDrawGizmosSelected() => DrawZoneGizmo(true);

    private void DrawZoneGizmo(bool selected)
    {
        Collider col = GetComponent<MeshCollider>() ?? GetComponent<Collider>();
        if (col == null) return;

        Bounds b    = col.bounds;
        float  topY = b.max.y + 0.05f; // ligeramente por encima de la superficie

        Color wire = selected
            ? new Color(0.35f, 0.68f, 1f, 0.95f)
            : new Color(0.35f, 0.68f, 1f, 0.45f);
        Color fill = selected
            ? new Color(0.2f, 0.5f, 1f, 0.12f)
            : new Color(0.2f, 0.5f, 1f, 0.05f);

        // Huella XZ en la superficie (quad plano)
        Vector3 fl = new Vector3(b.min.x, topY, b.min.z);
        Vector3 fr = new Vector3(b.max.x, topY, b.min.z);
        Vector3 br = new Vector3(b.max.x, topY, b.max.z);
        Vector3 bl = new Vector3(b.min.x, topY, b.max.z);

        Gizmos.color = wire;
        Gizmos.DrawLine(fl, fr);
        Gizmos.DrawLine(fr, br);
        Gizmos.DrawLine(br, bl);
        Gizmos.DrawLine(bl, fl);

        // Relleno semitransparente (dos triángulos)
        Gizmos.color = fill;
        Gizmos.DrawLine(fl, br); // diagonal para dar sensación de relleno
        Gizmos.DrawLine(fr, bl);

        if (strength <= 0f) return;

        // Flecha de corriente centrada en la huella
        Vector3 center = new Vector3(b.center.x, topY, b.center.z);

        // Derivar eje lateral igual que KayakController: Cross(up, forward)
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized).normalized;

        float   sign     = direction == Direction.Right ? 1f : -1f;
        float   arrowLen = Mathf.Clamp(strength * 0.4f, 0.6f, 4f);
        Vector3 arrowVec = right * sign * arrowLen;
        Vector3 tip      = center + arrowVec;
        Vector3 perp     = Vector3.Cross(arrowVec.normalized, Vector3.up) * 0.22f;

        Gizmos.color = wire;
        Gizmos.DrawLine(center, tip);
        Gizmos.DrawLine(tip, tip - arrowVec.normalized * 0.4f + perp);
        Gizmos.DrawLine(tip, tip - arrowVec.normalized * 0.4f - perp);

        if (!selected) return;

        var style = new GUIStyle
        {
            normal    = { textColor = new Color(0.35f, 0.75f, 1f) },
            fontStyle = FontStyle.Bold,
            fontSize  = 11
        };
        string arrow = direction == Direction.Right ? "→" : "←";
        UnityEditor.Handles.Label(
            center + Vector3.up * 0.3f,
            $"CURRENT  {arrow}  {strength:F1} m/s", style);
    }
#endif
}
