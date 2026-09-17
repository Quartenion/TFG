using UnityEngine;

/// <summary>
/// Muerte por AGUA. Va en el jugador. Cuando el jugador cae al agua (layer <see cref="waterLayerName"/>,
/// el mismo agua sólida del kayak), MUERE al instante (game over). No hay buceo ni oxígeno.
///
/// Detecta el agua con un raycast HACIA ARRIBA desde la cabeza: si hay agua por encima, el jugador está
/// sumergido -> muerte. En kayak / tirolina no aplica (esos gestionan el agua aparte).
///
/// SETUP:
///   1. El agua es la del kayak, en el layer "Water".
///   2. Para que el jugador CAIGA al agua (y no se quede encima), Player x Water debe estar DESMARCADO
///      en la Layer Collision Matrix (Project Settings > Physics).
///   3. Asigna el GameOverManager (o se busca solo).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class WaterDiveController : MonoBehaviour
{
    [Tooltip("Layer del agua (el mismo agua sólida del kayak).")]
    public string waterLayerName = "Water";
    [Tooltip("Distancia del raycast hacia arriba para detectar agua sobre la cabeza.")]
    public float waterCheckUpDistance = 50f;
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
            Debug.LogError($"[WaterDiveController] El layer \"{waterLayerName}\" no existe. Créalo en Project Settings > Tags and Layers.");
    }

    private void Update()
    {
        if (_dead || _waterMask == 0) return;
        // En kayak / tirolina no aplica (el jugador va sobre el agua a propósito).
        if (_pm != null && (_pm.IsOnKayak || _pm.IsOnZipline)) return;

        float   headY = _cc != null ? _cc.height * 0.5f : 1f;
        Vector3 head  = transform.position + Vector3.up * headY;
        // Si hay agua sólida por ENCIMA de la cabeza, el jugador se ha caído al agua -> muerte.
        if (Physics.Raycast(head, Vector3.up, out RaycastHit hit,
                            waterCheckUpDistance, _waterMask, QueryTriggerInteraction.Ignore))
        {
            Die(new Vector3(transform.position.x, hit.point.y, transform.position.z));
        }
    }

    private void Die(Vector3 splashPos)
    {
        _dead = true;

        if (diveAudio != null && splashSound != null) diveAudio.PlayOneShot(splashSound);
        if (splashPrefab != null)
        {
            GameObject fx = Instantiate(splashPrefab, splashPos, Quaternion.identity);
            if (splashLifetime > 0f) Destroy(fx, splashLifetime);
        }

        if (gameOverManager != null) gameOverManager.TriggerGameOver();
        else Debug.LogError("[WaterDiveController] GameOverManager no está asignado.");
    }
}
