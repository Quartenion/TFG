using UnityEngine;

/// <summary>
/// Muerte por AGUA. Va en el jugador. Cuando el jugador cae al agua (layer <see cref="waterLayerName"/>,
/// el mismo agua sólida del kayak), MUERE al instante (game over). No hay buceo ni oxígeno.
///
/// Detecta el agua con un raycast HACIA ABAJO (solo contra el layer del agua, así que atraviesa el suelo):
/// busca la superficie y mata en cuanto los PIES del jugador llegan a esa altura. El agua debe tener un
/// collider SÓLIDO (no trigger) en ese layer.
/// En kayak / tirolina no aplica (esos gestionan el agua aparte).
///
/// SETUP:
///   1. Este componente va en el JUGADOR (el que tiene el CharacterController y PlayerMovement).
///   2. El agua tiene que estar en el layer "Water" (el mismo agua del kayak).
///   3. Asigna el GameOverManager (o se busca solo).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class WaterDiveController : MonoBehaviour
{
    [Tooltip("Layer del agua (el mismo agua sólida del kayak).")]
    public string waterLayerName = "Water";
    [Tooltip("Distancia máxima a la que se busca la superficie del agua (hacia arriba y hacia abajo).")]
    public float waterCheckDistance = 200f;
    [Tooltip("Margen sobre la superficie: muere cuando los pies quedan a esta distancia (o menos) del agua.")]
    public float surfaceThreshold = 0.2f;
    [Tooltip("Referencia al GameOverManager (se busca solo si está vacío).")]
    public GameOverManager gameOverManager;

    [Header("Splash al caer (opcional)")]
    [Tooltip("AudioSource para el sonido del splash.")]
    public AudioSource diveAudio;
    [Tooltip("Sonido de splash al caer al agua.")]
    public AudioClip splashSound;
    [Tooltip("(Opcional) Prefab de partículas de splash en la superficie al caer.")]
    public GameObject splashPrefab;
    [Tooltip("Segundos tras los que se destruye el splash instanciado.")]
    public float splashLifetime = 3f;

    private CharacterController _cc;
    private PlayerMovement      _pm;
    private int  _waterMask = 0;
    private bool _dead;

    private void Start()
    {
        _cc = GetComponent<CharacterController>();
        _pm = GetComponent<PlayerMovement>();
        if (gameOverManager == null) gameOverManager = FindAnyObjectByType<GameOverManager>();

        int w = LayerMask.NameToLayer(waterLayerName);
        _waterMask = w == -1 ? 0 : (1 << w);
        if (w == -1)
            Debug.LogError($"[WaterDiveController] El layer \"{waterLayerName}\" no existe. Créalo en Project Settings > Tags and Layers.", this);
    }

    private void Update()
    {
        if (!Active()) return;

        // Buscar la superficie del agua HACIA ABAJO desde bien por encima del jugador.
        // El mask solo incluye el agua, así que atraviesa el suelo/obstáculos y encuentra el agua.
        Vector3 origin = transform.position + Vector3.up * waterCheckDistance;
        float   maxDist = waterCheckDistance * 2f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                            maxDist, _waterMask, QueryTriggerInteraction.Ignore))
        {
            float waterY = hit.point.y;
            float feetY  = _cc != null ? _cc.bounds.min.y : transform.position.y;

            if (feetY <= waterY + surfaceThreshold)
                Die(new Vector3(transform.position.x, waterY, transform.position.z));
        }
    }

    /// <summary>¿Debe comprobar el agua ahora? (no muerto, mask válida, no en kayak/tirolina).</summary>
    private bool Active()
    {
        if (_dead || _waterMask == 0) return false;
        if (_pm != null && (_pm.IsOnKayak || _pm.IsOnZipline)) return false;
        return true;
    }

    private void Die(Vector3 splashPos)
    {
        if (_dead) return;
        _dead = true;

        if (diveAudio != null && splashSound != null) diveAudio.PlayOneShot(splashSound);
        if (splashPrefab != null)
        {
            GameObject fx = Instantiate(splashPrefab, splashPos, Quaternion.identity);
            if (splashLifetime > 0f) Destroy(fx, splashLifetime);
        }

        if (gameOverManager != null) gameOverManager.TriggerGameOver(Loc.T("Has caído al agua", "You fell into the water"));
        else Debug.LogError("[WaterDiveController] GameOverManager no está asignado.", this);
    }
}
