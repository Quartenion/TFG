using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pared de inicio. El jugador interactúa mirándola directamente (raycast) a menos de
/// <see cref="_maxDistance"/> metros y pulsando E.
///
/// SETUP:
///   1. Asigna este script al GameObject de la pared (o a un GO vacío).
///   2. En el Inspector arrastra:
///      · Start Sequence  → StartSequenceManager
///      · Heart Prop      → viewmodel del brazo/corazón
///      · Starting Point  → Transform donde aparece el jugador durante la intro
///      · Wall Collider   → Collider de la pared (auto si el script está en la pared)
///   3. En StartSequenceManager activa "Wait For Interaction".
///
/// FLUJO:
///   · Freeroam: corazón oculto, barra de energía oculta.
///   · El jugador mira la pared a ≤ _maxDistance m y pulsa E.
///   · La pared desaparece.
///   · El jugador se teletransporta a Starting Point.
///   · Aparecen el corazón y la barra de energía.
///   · Empieza la introducción (círculos de ritmo).
/// </summary>
[AddComponentMenu("Level/Start Wall")]
public class StartWall : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

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

    [Header("Interacción")]
    [Tooltip("Distancia máxima (m) al mirar la pared para que aparezca el prompt.")]
    [SerializeField] private float _maxDistance = 2f;

    [Tooltip("Texto del prompt que aparece al apuntar a la pared.")]
    [SerializeField] private string _promptText = "[ E ]  Empezar";

    // ── Estado ────────────────────────────────────────────────────────────────

    private PlayerMovement _playerMovement;
    private bool           _triggered;
    private bool           _inRange;

    // ── UI ────────────────────────────────────────────────────────────────────

    private GUIStyle  _promptStyle;
    private Texture2D _bgTex;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        // Ocultar brazo/corazón en freeroam
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

        // Raycast desde la cámara: ¿está mirando la pared a ≤ _maxDistance m?
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _maxDistance)
            && hit.collider == _wallCollider)
        {
            _inRange = true;
        }

        if (_inRange && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            Trigger();
    }

    private void Trigger()
    {
        _triggered = true;

        // 1. Ocultar la pared (si procede)
        if (_hideWallOnTrigger && _wallCollider != null)
            _wallCollider.gameObject.SetActive(false);

        // 2. Desactivar freeroam SIEMPRE, independientemente del starting point
        if (_playerMovement != null)
        {
            if (_startingPoint != null)
            {
                // Teletransportar + desactivar freeroam
                _playerMovement.TeleportToStartPoint(_startingPoint.position, _startingPoint.rotation);
            }
            else
            {
                // Sin starting point: solo desactivar freeroam
                Debug.LogWarning("[StartWall] Starting Point no asignado — el jugador no se mueve.", this);
                _playerMovement.DisableFreeroam();
            }
        }

        // 3. Mostrar el brazo con el corazón
        if (_heartProp != null)
            _heartProp.SetActive(true);

        // 4. Lanzar la secuencia (mostrará la barra de energía y los círculos de ritmo)
        if (_startSequence != null)
            _startSequence.TriggerSequence();
    }

    // ── OnGUI ─────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (_triggered || !_inRange) return;
        InitStyles();

        const float pw = 260f, ph = 48f;
        float px = (Screen.width  - pw) * 0.5f;
        float py =  Screen.height * 0.72f;

        if (_bgTex == null)
        {
            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
            _bgTex.Apply();
        }
        GUI.DrawTexture(new Rect(px, py, pw, ph), _bgTex);

        float alpha = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f));
        var style = new GUIStyle(_promptStyle)
        {
            normal = { textColor = new Color(1f, 1f, 1f, alpha) }
        };
        GUI.Label(new Rect(px, py, pw, ph), _promptText, style);
    }

    private void InitStyles()
    {
        if (_promptStyle != null) return;
        _promptStyle = new GUIStyle
        {
            fontSize  = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white }
        };
    }

    private void OnDestroy()
    {
        if (_bgTex != null) Destroy(_bgTex);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Rango de detección
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.15f);
        Gizmos.DrawSphere(transform.position, _maxDistance);
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, _maxDistance);

        // Starting point
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
