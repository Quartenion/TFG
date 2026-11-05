using UnityEngine;

// El enum LethalFaces está en ObstacleInfo.cs, ya que forma parte de la configuración del obstáculo.

/// <summary>
/// Detector de obstáculos del jugador cuando va a pie o en la tirolina. Este componente solo va en el jugador. Cuando
/// el jugador choca con un objeto de la capa "Obstacle", lee del <see cref="ObstacleInfo"/> de ese obstáculo qué
/// caras son letales y, si la cara golpeada lo es, lanza el game over.
///
/// Los choques del kayak los gestiona <see cref="KayakController"/> por separado.
///
/// Configuración.
///   1. En Edit > Project Settings > Tags and Layers se crea la capa "Obstacle".
///   2. Se asigna esa capa a cada objeto peligroso y se le añade un <see cref="ObstacleInfo"/>.
///   3. Este componente se añade al jugador y se le asigna el GameOverManager de la escena.
///   4. defaultLethalFaces solo se usa si un obstáculo no tiene ObstacleInfo.
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

    // -=+=- Ciclo de vida -=+=-

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

    // -=+=- Detección con el CharacterController desactivado (tirolina y kayak) -=+=-

    private void Update()
    {
        if (_dead || _obstacleLayer == -1) return;
        if (playerMovement == null) return;

        // Solo la tirolina necesita detección manual, ya que el CharacterController está desactivado y el jugador no
        // tiene un Rigidbody que genere eventos de trigger. En el kayak, KayakController detecta sus propios obstáculos.
        if (!playerMovement.IsOnZipline) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, ziplineDetectRadius, 1 << _obstacleLayer);

        foreach (Collider hit in hits)
        {
            // Como no hay una normal de contacto exacta, se aproxima con la dirección desde el centro del obstáculo hasta el
            // jugador, que equivale a la normal de salida.
            Vector3 approxNormal = (transform.position - hit.bounds.center).normalized;
            LethalFaces face = ClassifyNormal(approxNormal);

            if ((FacesOf(hit.gameObject) & face) == 0) continue; // cara no letal, se ignora

            if (verbose)
                Debug.Log($"[ObstacleCollision] Obstáculo detectado (tirolina): \"{hit.name}\"  cara: {face}");
            Die(hit.gameObject);
            return;
        }
    }

    // -=+=- Detección con el CharacterController activo (movimiento normal) -=+=-

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (_dead || _obstacleLayer == -1) return;
        if (hit.gameObject.layer != _obstacleLayer) return;
        if (playerMovement != null && playerMovement.IsSliding) return;

        // hit.normal es la normal real de la superficie, por lo que la cara se clasifica con precisión.
        LethalFaces face = ClassifyNormal(hit.normal);
        if ((FacesOf(hit.gameObject) & face) == 0) return;

        if (verbose)
            Debug.Log($"[ObstacleCollision] Choque con \"{hit.gameObject.name}\"  cara: {face}. Game Over.");
        Die(hit.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_dead || _obstacleLayer == -1) return;
        if (other.gameObject.layer != _obstacleLayer) return;
        if (playerMovement != null && playerMovement.IsSliding) return;

        // Sin normal de contacto, se aproxima igual que con OverlapSphere.
        Vector3 approxNormal = (transform.position - other.bounds.center).normalized;
        LethalFaces face = ClassifyNormal(approxNormal);
        if ((FacesOf(other.gameObject) & face) == 0) return;

        if (verbose)
            Debug.Log($"[ObstacleCollision] Trigger con \"{other.gameObject.name}\"  cara: {face}. Game Over.");
        Die(other.gameObject);
    }

    // -=+=- Configuración del obstáculo -=+=-

    /// <summary>
    /// Devuelve las caras letales del obstáculo golpeado. Cada obstáculo define las suyas en su
    /// <see cref="ObstacleInfo"/> y, si no tiene, se usan las caras por defecto.
    /// </summary>
    private LethalFaces FacesOf(GameObject obstacle)
    {
        ObstacleInfo info = obstacle.GetComponentInParent<ObstacleInfo>();
        return info != null ? info.lethalFaces : defaultLethalFaces;
    }

    // -=+=- Game over -=+=-

    private BlockAbility _block;

    /// <summary>
    /// Bloques rompibles. Si el jugador choca con uno mientras tiene la Q activa, el bloque se rompe en lugar de matarle.
    /// Sirve por si el choque ocurre en el mismo fotograma, antes de que BlockAbility lo rompa. Si ya estaba roto (y sus
    /// colliders todavía no se han desactivado del todo), el choque se ignora.
    /// </summary>
    private bool SavedByBreakable(GameObject obstacle)
    {
        BreakableBlock bb = obstacle != null ? obstacle.GetComponentInParent<BreakableBlock>() : null;
        if (bb == null) return false;
        if (bb.IsBroken) return true;
        if (_block == null) _block = GetComponent<BlockAbility>();
        if (_block != null && _block.IsBlocking) { bb.Break(); return true; }
        return false;
    }

    private void Die(GameObject obstacle)
    {
        if (SavedByBreakable(obstacle)) return;

        if (gameOverManager == null)
        {
            _dead = true;
            Debug.LogError("[ObstacleCollision] GameOverManager NO está asignado en el Inspector.");
            return;
        }
        // Con el escudo (tienda), el obstáculo se rompe y el jugador sigue vivo, y se siguen detectando los siguientes obstáculos.
        _dead = gameOverManager.TryKillByObstacle(obstacle);
    }

    // -=+=- Clasificación de la cara -=+=-

    /// <summary>
    /// Determina qué cara del obstáculo se ha golpeado a partir de la normal de la superficie en coordenadas del mundo.
    ///
    /// Se busca el eje dominante de <paramref name="worldNormal"/>. Para el eje Y se usan direcciones absolutas (Top y
    /// Bottom), y para los ejes horizontales se usa la dirección en la que avanza el jugador, no la de la mirada, de forma
    /// que "Front" es siempre la cara que el jugador golpea de frente al correr, aunque esté mirando hacia un lado.
    /// </summary>
    private LethalFaces ClassifyNormal(Vector3 worldNormal)
    {
        // Dirección en la que avanza el jugador, que no depende de hacia dónde mire. Si se usara transform.forward, que gira
        // con la mirada, al mirar hacia un lado un choque frontal se clasificaría como lateral y no lanzaría el game over.
        Vector3 rawFwd = playerMovement != null
            ? playerMovement.RunForward
            : Vector3.forward;
        Vector3 fwd   = new Vector3(rawFwd.x, 0f, rawFwd.z);
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd = fwd.normalized;

        // Vector lateral derecho respecto al avance del jugador. Con Cross(up, fwd), si fwd = (0,0,1), right = (1,0,0).
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        // Proyecciones sobre los tres ejes
        float dotY   = worldNormal.y;
        float dotFwd = Vector3.Dot(worldNormal, fwd);   // si es menor que 0, la cara está de frente al jugador (Front)
        float dotRt  = Vector3.Dot(worldNormal, right); // si es mayor que 0, la cara mira hacia la derecha del jugador

        float absY   = Mathf.Abs(dotY);
        float absFwd = Mathf.Abs(dotFwd);
        float absRt  = Mathf.Abs(dotRt);

        // El eje dominante indica la cara.
        if (absY >= absFwd && absY >= absRt)
            return dotY > 0f ? LethalFaces.Top : LethalFaces.Bottom;

        if (absFwd >= absY && absFwd >= absRt)
            return dotFwd < 0f ? LethalFaces.Front : LethalFaces.Back;

        return dotRt > 0f ? LethalFaces.Right : LethalFaces.Left;
    }
}
