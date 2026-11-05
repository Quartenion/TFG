using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pared de inicio. El jugador interactúa con ella mirándola directamente (raycast) a menos de
/// <see cref="_maxDistance"/> metros y pulsando E.
///
/// Configuración.
///   1. Se añade este script al GameObject de la pared (o a un GameObject vacío).
///   2. En el Inspector se asignan Start Sequence (StartSequenceManager), Heart Prop (el modelo del brazo con el
///      corazón), Starting Point (el Transform donde aparece el jugador durante la intro) y Wall Collider (el
///      collider de la pared, que se asigna solo si el script está en la pared).
///   3. En StartSequenceManager se activa "Wait For Interaction".
///
/// Flujo.
///   - En la zona de inicio, el corazón y la barra de energía están ocultos.
///   - El jugador mira la pared a _maxDistance metros o menos y pulsa E.
///   - La pared se desmorona en bloques (CrumbleFX), como las plataformas del jefe.
///   - El jugador camina (o se teletransporta) hasta el Starting Point.
///   - Aparecen el corazón y la barra de energía.
///   - Empieza la introducción, con los círculos de ritmo.
/// </summary>
[AddComponentMenu("Level/Start Wall")]
public class StartWall : MonoBehaviour
{
    // -=+=- Inspector -=+=-

    [Header("Referencias")]
    [Tooltip("GameObject que contiene StartSequenceManager.")]
    [SerializeField] private StartSequenceManager _startSequence;

    [Tooltip("Viewmodel del corazón/mano. Se oculta en freeroam.")]
    [SerializeField] private GameObject _heartProp;

    [Tooltip("Transform donde el jugador se posiciona al empezar la introducción.")]
    [SerializeField] private Transform _startingPoint;

    [Header("La Pared")]
    [Tooltip("Collider de la pared para el raycast.\n" +
             "Si está vacío se usa el Collider de este mismo GameObject.\n" +
             "Al interactuar, el GameObject de este Collider se desactiva.")]
    [SerializeField] private Collider _wallCollider;

    [Tooltip("Si está activo, la pared desaparece al interactuar.\n" +
             "Desactívalo si la pared es solo el trigger pero no debe ocultarse\n" +
             "(p. ej. cuando la interacción son unas puertas).")]
    [SerializeField] private bool _hideWallOnTrigger = true;

    [Tooltip("La pared se DESMORONA (tiembla, se parte en bloques que caen y se desintegran) en vez de desaparecer\n" +
             "de golpe, como las plataformas del jefe.")]
    [SerializeField] private bool _crumble = true;
    [SerializeField] private CrumbleFX.Settings _crumbleSettings = new CrumbleFX.Settings();

    [Tooltip("En vez de teletransportarse, el jugador CAMINA hasta el Starting Point (así ve cómo se viene abajo la\n" +
             "pared y pasa por el hueco).")]
    [SerializeField] private bool _walkToStart = true;
    [Tooltip("Segundos que tarda en llegar caminando al Starting Point.")]
    [SerializeField] private float _travelDuration = 2.5f;

    [Header("Interacción")]
    [Tooltip("Distancia máxima (m) al mirar la pared para que aparezca el prompt.")]
    [SerializeField] private float _maxDistance = 2f;

    [Tooltip("Texto del prompt que aparece al apuntar a la pared.")]
    [SerializeField] private string _promptText = "[ E ]  Empezar";
    [SerializeField] private string _promptTextEn = "[ E ]  Start";

    // -=+=- Estado -=+=-

    private PlayerMovement _playerMovement;
    private bool           _triggered;
    private bool           _inRange;

    // -=+=- Ciclo de vida -=+=-

    private void Start()
    {
        // En la zona de inicio se oculta el brazo con el corazón.
        if (_heartProp != null)
            _heartProp.SetActive(false);

        _playerMovement = FindAnyObjectByType<PlayerMovement>();

        if (_wallCollider == null)
            _wallCollider = GetComponent<Collider>();

        if (_startSequence == null)
            _startSequence = FindAnyObjectByType<StartSequenceManager>();

        if (_startSequence == null)
            Debug.LogError("[StartWall] No se encontró StartSequenceManager en la escena.", this);
    }

    private void Update()
    {
        if (_triggered) return;

        _inRange = false;

        Camera cam = Camera.main;
        if (cam == null || _wallCollider == null) return;

        // Raycast desde la cámara, para saber si el jugador está mirando la pared a _maxDistance metros o menos.
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _maxDistance)
            && hit.collider == _wallCollider)
        {
            _inRange = true;
        }

        if (_inRange && Keybinds.Pressed(Keybinds.Bind.Interact))
            Trigger();
    }

    /// <summary>Reaparición en un punto de control. La pared ya cuenta como usada (sin teletransporte ni secuencia) y se ve el corazón.</summary>
    public void SkipForCheckpoint()
    {
        _triggered = true;
        _inRange   = false;
        if (_hideWallOnTrigger && _wallCollider != null) _wallCollider.gameObject.SetActive(false);
        if (_heartProp != null) _heartProp.SetActive(true);
    }

    private void Trigger()
    {
        _triggered = true;

        // 1. La pared se desmorona, o desaparece de golpe.
        if (_hideWallOnTrigger && _wallCollider != null)
        {
            if (_crumble) CrumbleFX.Crumble(_wallCollider.gameObject, _crumbleSettings);
            else          _wallCollider.gameObject.SetActive(false);
        }

        // 2. Se sale siempre de la zona de inicio, haya Starting Point o no.
        if (_playerMovement != null)
        {
            if (_startingPoint != null)
            {
                if (_walkToStart)
                    _playerMovement.StartSmoothMoveToStart(_startingPoint.position, _travelDuration);   // sale de la zona de inicio
                else
                    _playerMovement.TeleportToStartPoint(_startingPoint.position, _startingPoint.rotation);
            }
            else
            {
                // Sin Starting Point, solo se sale de la zona de inicio.
                Debug.LogWarning("[StartWall] Starting Point no asignado — el jugador no se mueve.", this);
                _playerMovement.DisableFreeroam();
            }
        }

        // 3. Se muestra el brazo con el corazón.
        if (_heartProp != null)
            _heartProp.SetActive(true);

        // 4. Se lanza la secuencia, que muestra la barra de energía y los círculos de ritmo.
        if (_startSequence != null)
            _startSequence.TriggerSequence();
    }

    // -=+=- OnGUI -=+=-

    private void OnGUI()
    {
        if (HudHider.IsHidden) return;   // con la pausa o el game over no se muestra ningún aviso
        if (_triggered || !_inRange) return;
        InteractionPrompt.Draw(Keybinds.Fix(Loc.T(_promptText, _promptTextEn)), 260f, Color.white);
    }

    // -=+=- Gizmos -=+=-

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Rango de detección
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.15f);
        Gizmos.DrawSphere(transform.position, _maxDistance);
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, _maxDistance);

        // Starting Point
        if (_startingPoint != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
            Gizmos.DrawSphere(_startingPoint.position, 0.25f);
            Gizmos.DrawLine(transform.position, _startingPoint.position);
        }

        var style = new GUIStyle
        {
            normal    = { textColor = new Color(0.2f, 0.9f, 0.4f) },
            fontStyle = UnityEngine.FontStyle.Bold,
            fontSize  = 11
        };
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * (_maxDistance + 0.3f),
            "START WALL  (E)", style);
    }
#endif
}
