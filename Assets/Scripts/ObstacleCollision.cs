using UnityEngine;

// El enum LethalFaces ahora vive en ObstacleInfo.cs (es la configuración del obstáculo).

/// <summary>
/// Detector de obstáculos DEL JUGADOR (a pie / tirolina). Va SÓLO en el jugador.
/// Cuando el jugador choca con un objeto del layer "Obstacle", lee qué caras son
/// letales del <see cref="ObstacleInfo"/> de ese obstáculo y, si la cara golpeada
/// es letal, dispara el Game Over.
///
/// La detección del kayak la gestiona <see cref="KayakController"/> aparte.
///
/// SETUP:
///   1. En Edit > Project Settings > Tags and Layers: crea el layer "Obstacle".
///   2. Asigna ese layer a cada objeto peligroso y ponle un <see cref="ObstacleInfo"/>.
///   3. Este componente va en el jugador. Asigna el GameOverManager de la escena.
///   4. defaultLethalFaces se usa sólo si un obstáculo no tiene ObstacleInfo.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ObstacleCollision : MonoBehaviour
{
    [Tooltip("Nombre exacto del layer que identifica los objetos letales.")]
    public string obstacleLayerName = "Obstacle";

    [Tooltip("Referencia al GameOverManager de la escena.")]
    public GameOverManager gameOverManager;

    [Tooltip("Referencia al PlayerMovement (se busca automáticamente si está vacío).")]
    public PlayerMovement playerMovement;

    [Header("Caras letales (fallback)")]
    [Tooltip("Caras letales por defecto, usadas SÓLO cuando el obstáculo golpeado\n" +
             "no tiene componente ObstacleInfo. Lo normal es que cada obstáculo defina\n" +
             "sus propias caras en su ObstacleInfo.")]
    public LethalFaces defaultLethalFaces = LethalFaces.Front | LethalFaces.Bottom;

    [Header("Detección con CC desactivado (tirolina)")]
    [Tooltip("Radio de detección de obstáculos mientras el jugador está en tirolina.\n" +
             "Para el kayak, la detección la gestiona KayakController aparte.")]
    public float ziplineDetectRadius = 0.6f;

    [Header("Debug — desactivar cuando funcione")]
    public bool verbose = true;

    private bool _dead;
    private int  _obstacleLayer = -1;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        _obstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
        if (_obstacleLayer == -1)
            Debug.LogError($"[ObstacleCollision] El layer \"{obstacleLayerName}\" no existe. Créalo en Project Settings > Tags and Layers.");
        else if (verbose)
            Debug.Log($"[ObstacleCollision] Layer \"{obstacleLayerName}\" encontrado en índice {_obstacleLayer}.");
    }

    // ── Detección (CC desactivado: tirolina y kayak) ──────────────────────────

    private void Update()
    {
        if (_dead || _obstacleLayer == -1) return;
        if (playerMovement == null) return;

        // Solo la tirolina necesita detección manual: el CC está desactivado y no hay
        // Rigidbody en el jugador que genere eventos de trigger.
        // En kayak, KayakController gestiona su propia detección de obstáculos.
        if (!playerMovement.IsOnZipline) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, ziplineDetectRadius, 1 << _obstacleLayer);

        foreach (Collider hit in hits)
        {
            // Sin normal de contacto exacta → aproximar: dirección desde el centro
            // del obstáculo hasta el jugador (equivale a la normal de salida).
            Vector3 approxNormal = (transform.position - hit.bounds.center).normalized;
            LethalFaces face = ClassifyNormal(approxNormal);

            if ((FacesOf(hit.gameObject) & face) == 0) continue; // cara no letal, ignorar

            if (verbose)
                Debug.Log($"[ObstacleCollision] Obstáculo detectado (tirolina): \"{hit.name}\"  cara: {face}");
            Die();
            return;
        }
    }

    // ── Detección (CC activo: movimiento normal) ──────────────────────────────

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (_dead || _obstacleLayer == -1) return;
        if (hit.gameObject.layer != _obstacleLayer) return;
        if (playerMovement != null && playerMovement.IsSliding) return;

        // hit.normal es la normal real de la superficie → clasificación precisa
        LethalFaces face = ClassifyNormal(hit.normal);
        if ((FacesOf(hit.gameObject) & face) == 0) return;

        if (verbose)
            Debug.Log($"[ObstacleCollision] Choque con \"{hit.gameObject.name}\"  cara: {face}. Game Over.");
        Die();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_dead || _obstacleLayer == -1) return;
        if (other.gameObject.layer != _obstacleLayer) return;
        if (playerMovement != null && playerMovement.IsSliding) return;

        // Sin normal de contacto → aproximar igual que en el caso de OverlapSphere
        Vector3 approxNormal = (transform.position - other.bounds.center).normalized;
        LethalFaces face = ClassifyNormal(approxNormal);
        if ((FacesOf(other.gameObject) & face) == 0) return;

        if (verbose)
            Debug.Log($"[ObstacleCollision] Trigger con \"{other.gameObject.name}\"  cara: {face}. Game Over.");
        Die();
    }

    // ── Configuración del obstáculo ───────────────────────────────────────────

    /// <summary>
    /// Devuelve las caras letales del obstáculo golpeado. Cada obstáculo define las
    /// suyas en su <see cref="ObstacleInfo"/>; si no tiene, se usa el fallback.
    /// </summary>
    private LethalFaces FacesOf(GameObject obstacle)
    {
        ObstacleInfo info = obstacle.GetComponentInParent<ObstacleInfo>();
        return info != null ? info.lethalFaces : defaultLethalFaces;
    }

    // ── Game Over ─────────────────────────────────────────────────────────────

    private void Die()
    {
        _dead = true;

        if (gameOverManager != null)
            gameOverManager.TriggerGameOver();
        else
            Debug.LogError("[ObstacleCollision] GameOverManager NO está asignado en el Inspector.");
    }

    // ── Clasificación de cara ────────────────────────────────────────────────

    /// <summary>
    /// Determina qué cara de un obstáculo fue golpeada a partir de la normal de
    /// la superficie en world space.
    ///
    /// Lógica: se encuentra el eje dominante de <paramref name="worldNormal"/>.
    /// Para el eje Y se usan direcciones absolutas (Top/Bottom).
    /// Para los ejes horizontales se usa el forward del jugador en ese instante,
    /// de forma que "Front" es siempre la cara que el jugador golpea de frente.
    /// </summary>
    private LethalFaces ClassifyNormal(Vector3 worldNormal)
    {
        // Dirección de avance del jugador (sin componente vertical)
        Vector3 rawFwd = playerMovement != null
            ? playerMovement.transform.forward
            : Vector3.forward;
        Vector3 fwd   = new Vector3(rawFwd.x, 0f, rawFwd.z);
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd = fwd.normalized;

        // Vector lateral derecho relativo al avance del jugador
        // Cross(up, fwd): si fwd=(0,0,1) → right=(1,0,0) ✓
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        // Proyecciones sobre los tres ejes
        float dotY   = worldNormal.y;
        float dotFwd = Vector3.Dot(worldNormal, fwd);   // <0 → cara enfrenta al jugador (Front)
        float dotRt  = Vector3.Dot(worldNormal, right); // >0 → cara hacia la derecha del jugador

        float absY   = Mathf.Abs(dotY);
        float absFwd = Mathf.Abs(dotFwd);
        float absRt  = Mathf.Abs(dotRt);

        // Eje dominante → cara correspondiente
        if (absY >= absFwd && absY >= absRt)
            return dotY > 0f ? LethalFaces.Top : LethalFaces.Bottom;

        if (absFwd >= absY && absFwd >= absRt)
            return dotFwd < 0f ? LethalFaces.Front : LethalFaces.Back;

        return dotRt > 0f ? LethalFaces.Right : LethalFaces.Left;
    }
}
