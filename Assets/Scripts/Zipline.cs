using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tirolina entre dos puntos. La cuerda se dibuja con TubeRenderer (tubo 3D).
///
/// SETUP:
///   1. Crea un GameObject vacio y añade este componente.
///   2. Crea dos GameObjects hijo (p.ej. "PointA" y "PointB"),
///      posicionalos donde quieras los extremos y asignalos en el Inspector.
///   3. Asigna un AudioSource en el mismo GameObject.
///   4. Crea un GameObject hijo "RopeMesh", añadele TubeRenderer,
///      asigna este Zipline en el campo _zipline del TubeRenderer
///      y asigna el material de la cuerda al MeshRenderer.
/// </summary>
public class Zipline : MonoBehaviour
{
    [Header("Puntos extremos")]
    [Tooltip("Extremo A de la cuerda.")]
    public Transform startPoint;
    [Tooltip("Extremo B de la cuerda.")]
    public Transform endPoint;

    [Header("Interaccion")]
    [Tooltip("Distancia maxima desde la cuerda para que el jugador pueda engancharse.")]
    public float interactRadius = 4f;

    [Header("Audio")]
    public AudioSource audioSource;
    [Tooltip("Sonido al engancharse a la tirolina.")]
    public AudioClip   attachSound;

    // ─────────────────────────────────────────────────────────────────────────
    // Registro estatico — evita FindObjectsOfType en tiempo de ejecucion
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly List<Zipline> _all = new List<Zipline>();
    public  static IReadOnlyList<Zipline>  All  => _all;

    // =========================================================================
    // UNITY
    // =========================================================================

    private void OnEnable()  => _all.Add(this);
    private void OnDisable() => _all.Remove(this);

    // =========================================================================
    // API PUBLICA
    // =========================================================================

    public Vector3 StartPos => startPoint != null ? startPoint.position : transform.position;
    public Vector3 EndPos   => endPoint   != null ? endPoint.position   : transform.position;

    /// <summary>
    /// Parametro t (0=startPoint, 1=endPoint) del punto de la cuerda mas
    /// cercano a <paramref name="worldPos"/>.
    /// </summary>
    public float GetT(Vector3 worldPos)
    {
        Vector3 ab   = EndPos - StartPos;
        float   len2 = ab.sqrMagnitude;
        if (len2 < 0.0001f) return 0f;
        return Mathf.Clamp01(Vector3.Dot(worldPos - StartPos, ab) / len2);
    }

    /// <summary>Posicion mundial en el parametro t de la cuerda.</summary>
    public Vector3 GetPosition(float t) => Vector3.Lerp(StartPos, EndPos, t);

    /// <summary>Distancia del punto mas cercano de la cuerda a worldPos.</summary>
    public float DistanceTo(Vector3 worldPos) => Vector3.Distance(worldPos, GetPosition(GetT(worldPos)));

    public bool IsInRange(Vector3 worldPos) => DistanceTo(worldPos) <= interactRadius;

    /// <summary>Reproduce el sonido de enganche (si esta configurado).</summary>
    public void PlayAttachSound()
    {
        if (audioSource != null && attachSound != null)
            audioSource.PlayOneShot(attachSound);
    }

    // =========================================================================
    // BUSQUEDA ESTATICA
    // =========================================================================

    /// <summary>
    /// Devuelve la tirolina mas cercana al jugador dentro de su propio
    /// interactRadius, o null si no hay ninguna en rango.
    /// </summary>
    public static Zipline FindNearest(Vector3 worldPos)
    {
        Zipline nearest = null;
        float   best    = float.MaxValue;

        foreach (Zipline z in _all)
        {
            if (!z.IsInRange(worldPos)) continue;
            float d = z.DistanceTo(worldPos);
            if (d < best) { best = d; nearest = z; }
        }
        return nearest;
    }

    // =========================================================================
    // GIZMOS (editor)
    // =========================================================================

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (startPoint == null || endPoint == null) return;

        // Cuerda (preview en editor)
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(startPoint.position, endPoint.position);
        Gizmos.DrawWireSphere(startPoint.position, 0.25f);
        Gizmos.DrawWireSphere(endPoint.position,   0.25f);

        // Radio de interaccion (en el centro de la cuerda)
        Gizmos.color = new Color(1f, 1f, 0f, 0.12f);
        Vector3 mid = (startPoint.position + endPoint.position) * 0.5f;
        Gizmos.DrawWireSphere(mid, interactRadius);
    }
#endif
}
