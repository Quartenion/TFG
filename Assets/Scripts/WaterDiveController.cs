using UnityEngine;

/// <summary>
/// Muerte por AGUA. Va en el jugador. Cuando el jugador cae al agua (layer <see cref="waterLayerName"/>,
/// el mismo agua sólida del kayak), MUERE al instante (game over). No hay buceo ni oxígeno.
///
/// Detecta el agua de TRES formas (con que salte una, muere), para que funcione con cualquier setup:
///   1. Raycast HACIA ABAJO: busca la superficie del agua (su cara superior, la misma que usa el kayak
///      para flotar) y mata en cuanto los PIES del jugador llegan a esa altura. Funciona tanto si el
///      jugador se queda encima del agua como si la atraviesa.
///   2. OnControllerColliderHit: si el CharacterController choca físicamente con el agua.
///   3. OnTriggerEnter: si el agua es un trigger y el jugador entra dentro.
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

    [Header("Debug")]
    [Tooltip("Escribe en la consola cuándo detecta el agua y por qué método.")]
    public bool debugLogs = false;

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
        else if (debugLogs)
            Debug.Log($"[WaterDiveController] Activo. Layer '{waterLayerName}' (mask {_waterMask}).", this);
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
            {
                if (debugLogs) Debug.Log($"[WaterDiveController] Muerte por raycast. feetY={feetY:F2} waterY={waterY:F2}", this);
                Die(new Vector3(transform.position.x, waterY, transform.position.z));
            }
        }
    }

    // Respaldo 1: el CharacterController choca con el agua sólida.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!Active() || hit.collider == null) return;
        if (((1 << hit.collider.gameObject.layer) & _waterMask) != 0)
        {
            if (debugLogs) Debug.Log("[WaterDiveController] Muerte por colisión del CharacterController.", this);
            Die(hit.point);
        }
    }

    // Respaldo 2: el agua es un trigger y el jugador entra.
    private void OnTriggerEnter(Collider other)
    {
        if (!Active() || other == null) return;
        if (((1 << other.gameObject.layer) & _waterMask) != 0)
        {
            if (debugLogs) Debug.Log("[WaterDiveController] Muerte por trigger de agua.", this);
            Die(transform.position);
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

        if (gameOverManager != null) gameOverManager.TriggerGameOver();
        else Debug.LogError("[WaterDiveController] GameOverManager no está asignado.", this);
    }
}
