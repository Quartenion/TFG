using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Marca un objeto como rompible por el escudo del jugador (BlockAbility).
///
/// SETUP:
///   1. Añade este componente a cualquier GameObject con Collider.
///   2. El jugador lo rompe al colisionar con el mientras bloquea (Q).
///   3. Configura efectos y sonido en el Inspector.
/// </summary>
public class BreakableBlock : MonoBehaviour, IRewindable
{
    [Header("Comportamiento")]
    [Tooltip("Segundos de espera antes de destruir/desactivar (para reproducir efectos primero).")]
    public float breakDelay = 0.1f;

    [Header("Desmoronarse")]
    [Tooltip("Al romperse se DESMORONA en bloques que salen despedidos hacia delante y caen (como la pared del inicio\n" +
             "y las plataformas del jefe). Desactívalo para el comportamiento antiguo (desaparecer).")]
    public bool crumble = true;
    public CrumbleFX.Settings crumbleSettings = new CrumbleFX.Settings
    {
        chunkSize = 0.35f, shakeTime = 0f, maxDelay = 0.12f, push = new Vector2(1f, 3f),
        life = new Vector2(0.8f, 1.6f), cameraShake = 0.15f,
    };
    [Tooltip("Velocidad (m/s) con la que los bloques salen despedidos en la dirección en que va el jugador.")]
    public float forwardFling = 5f;

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
        RecordBreakTime();

        OnBroken?.Invoke();

        // Efectos
        if (breakParticlesPrefab != null)
            Instantiate(breakParticlesPrefab, transform.position, Quaternion.identity);

        if (breakSound != null)
        {
            // (Si la fuente está en el propio bloque, se apagaría con él: entonces suena en el sitio.)
            if (audioSource != null && !audioSource.transform.IsChildOf(transform))
                audioSource.PlayOneShot(breakSound, sfxVolume);
            else
                AudioSource.PlayClipAtPoint(breakSound, transform.position, sfxVolume);
        }

        // Desactivar el collider de inmediato para que el jugador pueda pasar
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (crumble)
        {
            // Se desmorona (el efecto lo desactiva al romperse). Los bloques salen hacia donde va el jugador.
            Vector3 fling = Vector3.zero;
            var pm = FindAnyObjectByType<PlayerMovement>();
            if (pm != null)
            {
                Vector3 dir = transform.position - pm.transform.position; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = pm.transform.forward;
                fling = dir.normalized * forwardFling + Vector3.up * forwardFling * 0.3f;
            }
            _fx = CrumbleFX.Crumble(gameObject, crumbleSettings, fling);
            return;
        }

        if (breakDelay > 0f)
            Invoke(nameof(DoBreak), breakDelay);
        else
            DoBreak();
    }

    private void DoBreak()
    {
        // Se desactiva (no se destruye) para que el Rebobinado pueda recomponerlo.
        gameObject.SetActive(false);
    }

    private CrumbleFX _fx;
    private double _brokenSongTime = double.MaxValue;   // segundo de la canción en que se rompió

    private static double SongClock()
    {
        BeatManager bm = BeatManager.Instance;
        return bm != null && bm.ScheduledStartDSP > 0.0 ? AudioSettings.dspTime - bm.ScheduledStartDSP : double.MaxValue;
    }

    private void RecordBreakTime() => _brokenSongTime = SongClock();

    /// <summary>REBOBINADO: si el reloj vuelve a antes de romperse, el bloque reaparece entero.</summary>
    public void OnTimeRewind()
    {
        if (!_broken || SongClock() >= _brokenSongTime) return;
        CancelInvoke(nameof(DoBreak));
        if (_fx != null) { _fx.Cancel(); _fx = null; }   // (bloques del desmoronamiento: fuera)
        _broken = false;
        _brokenSongTime = double.MaxValue;
        gameObject.SetActive(true);
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
    }
}
