using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Marca un objeto como rompible por el escudo del jugador (BlockAbility).
///
/// SETUP:
///   1. Añade este componente a cualquier GameObject con Collider.
///   2. El jugador lo rompe al colisionar con el mientras bloquea (Q).
///   3. Configura destroyOnBreak, efectos y sonido en el Inspector.
/// </summary>
public class BreakableBlock : MonoBehaviour
{
    [Header("Comportamiento")]
    [Tooltip("Si true, el objeto se destruye al romperse. Si false, se desactiva.")]
    public bool destroyOnBreak = true;
    [Tooltip("Segundos de espera antes de destruir/desactivar (para reproducir efectos primero).")]
    public float breakDelay = 0.1f;

    [Header("Efectos visuales")]
    [Tooltip("Prefab de particulas que se instancia en la posicion del bloque al romperse.")]
    public GameObject breakParticlesPrefab;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip breakSound;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Eventos")]
    public UnityEvent OnBroken;

    private bool _broken = false;

    // -------------------------------------------------------------------------

    /// <summary>
    /// Rompe el bloque. Seguro llamarlo varias veces: solo actua la primera vez.
    /// </summary>
    public void Break()
    {
        if (_broken) return;
        _broken = true;

        OnBroken?.Invoke();

        // Efectos
        if (breakParticlesPrefab != null)
            Instantiate(breakParticlesPrefab, transform.position, Quaternion.identity);

        if (breakSound != null && audioSource != null)
            audioSource.PlayOneShot(breakSound, sfxVolume);

        // Desactivar el collider de inmediato para que el jugador pueda pasar
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (breakDelay > 0f)
            Invoke(nameof(DoBreak), breakDelay);
        else
            DoBreak();
    }

    private void DoBreak()
    {
        if (destroyOnBreak)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }
}
