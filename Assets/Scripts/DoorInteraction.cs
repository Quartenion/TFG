using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Puerta de inicio de nivel. El jugador mira la puerta (raycast) a menos de
/// <see cref="_maxDistance"/> metros y pulsa E para abrirla y comenzar el nivel.
///
/// SETUP:
///   1. Crea un GO vacío en la escena y añade este script.
///   2. En el Inspector arrastra:
///      · Start Sequence   → StartSequenceManager
///      · Player Movement  → PlayerMovement (para teletransporte y fin de freeroam)
///      · Starting Point   → Transform donde aparecerá el jugador durante la intro
///      · Heart Prop       → viewmodel del brazo/corazón (se oculta en freeroam)
///      · Door Colliders   → los Colliders de las puertas (uno o más)
///      · Door Pivots      → los PivotOscillator de las puertas (uno o más)
///   3. En cada PivotOscillator:
///      · Rotation Axis   = Y (para giro horizontal, como una puerta normal)
///      · Oscillation Angle = 90 (o el ángulo que quieras)
///      · Easing          = EaseOut
///      · Oscillate       = false (modo one-shot)
///   4. En StartSequenceManager activa "Wait For Interaction".
///
/// FLUJO:
///   · Freeroam: corazón oculto, barra de energía oculta.
///   · El jugador mira una puerta a ≤ _maxDistance m y pulsa E.
///   · Las puertas se abren (PivotOscillator.TriggerNow).
///   · El jugador se teletransporta al Starting Point.
///   · Aparecen el corazón y la barra de energía.
///   · Empieza la introducción (círculos de ritmo).
/// </summary>
[AddComponentMenu("Level/Door Interaction")]
public class DoorInteraction : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Referencias")]
    [Tooltip("StartSequenceManager de la escena.")]
    [SerializeField] private StartSequenceManager _startSequence;

    [Tooltip("PlayerMovement del jugador.")]
    [SerializeField] private PlayerMovement _playerMovement;

    [Tooltip("Transform donde el jugador aparece durante la introducción.")]
    [SerializeField] private Transform _startingPoint;

    [Tooltip("Viewmodel del corazón/mano. Se oculta en freeroam.")]
    [SerializeField] private GameObject _heartProp;

    [Header("Puertas")]
    [Tooltip("Colliders de las puertas detectables por raycast.\n" +
             "Puede ser uno o varios (ambas hojas de una puerta doble).")]
    [SerializeField] private Collider[] _doorColliders;

    [Tooltip("PivotOscillators que se activan al interactuar (las puertas que se abren).")]
    [SerializeField] private PivotOscillator[] _doorPivots;

    [Tooltip("Duración de la apertura de las puertas en segundos.")]
    [SerializeField] private float _openDuration = 1.2f;

    [Tooltip("Duración del desplazamiento suave del jugador hasta el Starting Point (segundos).\n" +
             "Debe ser menor que la duración de la introducción (círculos de ritmo).")]
    [SerializeField] private float _travelDuration = 2.5f;

    [Header("Interacción")]
    [Tooltip("Distancia máxima (m) al mirar la puerta para que aparezca el prompt.")]
    [SerializeField] private float _maxDistance = 2f;

    [Tooltip("Texto del prompt.")]
    [SerializeField] private string _promptText = "[ E ]  Abrir";

    // ── Estado ────────────────────────────────────────────────────────────────

    private bool _triggered;
    private bool _inRange;

    // ── UI ────────────────────────────────────────────────────────────────────

    private GUIStyle  _promptStyle;
    private Texture2D _bgTex;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        if (_heartProp != null)
            _heartProp.SetActive(false);

        if (_playerMovement == null)
            _playerMovement = FindAnyObjectByType<PlayerMovement>();

        if (_startSequence == null)
            _startSequence = FindAnyObjectByType<StartSequenceManager>();

        if (_startSequence == null)
            Debug.LogError("[DoorInteraction] No se encontró StartSequenceManager.", this);

        if (_doorColliders == null || _doorColliders.Length == 0)
            Debug.LogWarning("[DoorInteraction] No hay Door Colliders asignados — el raycast no detectará nada.", this);
    }

    private void Update()
    {
        if (_triggered) return;

        _inRange = false;

        Camera cam = Camera.main;
        if (cam == null) return;

        // Raycast desde la cámara: ¿está mirando alguna de las puertas?
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _maxDistance))
        {
            foreach (var col in _doorColliders)
            {
                if (col != null && hit.collider == col)
                {
                    _inRange = true;
                    break;
                }
            }
        }

        if (_inRange && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            Trigger();
    }

    private void Trigger()
    {
        _triggered = true;

        // 1. Abrir las puertas
        foreach (var pivot in _doorPivots)
            if (pivot != null) pivot.TriggerNow(_openDuration);

        // 2. Mover suavemente al starting point (desactiva freeroam automáticamente)
        if (_playerMovement != null)
        {
            if (_startingPoint != null)
                _playerMovement.StartSmoothMoveToStart(_startingPoint.position, _travelDuration);
            else
            {
                Debug.LogWarning("[DoorInteraction] Starting Point no asignado — solo se desactiva el freeroam.", this);
                _playerMovement.DisableFreeroam();
            }
        }

        // 3. Mostrar el brazo con el corazón
        if (_heartProp != null)
            _heartProp.SetActive(true);

        // 4. Lanzar la secuencia de inicio
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
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.15f);
        Gizmos.DrawSphere(transform.position, _maxDistance);
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, _maxDistance);

        if (_startingPoint != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
            Gizmos.DrawSphere(_startingPoint.position, 0.25f);
            Gizmos.DrawLine(transform.position, _startingPoint.position);
        }

        var style = new GUIStyle
        {
            normal    = { textColor = new Color(0.3f, 0.7f, 1f) },
            fontStyle = UnityEngine.FontStyle.Bold,
            fontSize  = 11
        };
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * (_maxDistance + 0.3f),
            "DOOR INTERACTION  (E)", style);
    }
#endif
}
