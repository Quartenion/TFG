using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TIENDA FÍSICA del área de freeroam (antes de empezar cada nivel): el jugador se acerca, la MIRA (raycast desde
/// la cámara, como la puerta de inicio) y pulsa E → se abre la tienda. Mientras está abierta el jugador se queda
/// quieto; al cerrarla (Esc / Volver) sigue explorando.
///
/// SETUP:
///   1. Pon este componente en el objeto de la tienda (mostrador, puesto, NPC…).
///   2. Necesita Collider(s) para que la mirada lo detecte: los de este objeto o sus hijos (o asígnalos a mano).
///      Si el objeto está VACÍO (sin mallas ni colliders), se genera un BLOQUE VERDE por código. Para usar un modelo,
///      mete tu prefab como hijo (o pon el componente en el prefab): entonces no se genera nada.
///   3. Por defecto solo funciona en FREEROAM (desactívalo en "Only In Freeroam" para probar en otro momento).
/// </summary>
[AddComponentMenu("Level/Shop Stand")]
public class ShopStand : MonoBehaviour
{
    [Header("Interacción")]
    [Tooltip("Colliders que cuentan como 'la tienda' al mirarla. Vacío = todos los de este objeto y sus hijos.")]
    [SerializeField] private Collider[] _colliders;
    [Tooltip("Distancia máxima (m) al mirarla para que aparezca el aviso.")]
    [SerializeField] private float _maxDistance = 4f;
    [Tooltip("Texto del aviso.")]
    [SerializeField] private string _promptText = "[ E ]  Tienda";
    [SerializeField] private string _promptTextEn = "[ E ]  Shop";
    [Tooltip("Solo se puede usar en freeroam (entre niveles, antes de abrir la puerta).")]
    [SerializeField] private bool _onlyInFreeroam = true;
    [Tooltip("Rótulo flotante encima del puesto («TIENDA · mira y pulsa E»).")]
    [SerializeField] private bool _showLabel = true;

    [Header("Bloque por defecto (solo si el objeto está vacío, sin prefab)")]
    [SerializeField] private Color   _blockColor = new Color(0.2f, 0.7f, 0.25f);   // verde
    [SerializeField] private Vector3 _blockSize  = new Vector3(2f, 2f, 2f);

    private PlayerMovement _pm;
    private bool _inRange;
    private bool _pmWasEnabled;

    private GUIStyle  _promptStyle;
    private Texture2D _bgTex;

    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();
        if (GetComponentInChildren<Renderer>() == null && GetComponentInChildren<Collider>() == null) BuildPlaceholder();
        if (_colliders == null || _colliders.Length == 0) _colliders = GetComponentsInChildren<Collider>();
        if (_showLabel) StandLabel.Create(transform, "TIENDA", "SHOP", new Color(0.45f, 1f, 0.5f), _onlyInFreeroam);
    }

    private void Update()
    {
        _inRange = false;
        if (ShopUI.IsOpen || ShopUI.ClosedRecently || ContractUI.IsOpen || ContractUI.ClosedRecently) return;
        if (StudySession.Active && !StudySession.ShopAllowed) return;   // (modo prueba: tienda solo en las partes 4-5)
        if (_onlyInFreeroam && (_pm == null || !_pm.IsFreeroam)) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _maxDistance, ~0, QueryTriggerInteraction.Collide))
            foreach (Collider c in _colliders)
                if (c != null && hit.collider == c) { _inRange = true; break; }

        if (_inRange && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) OpenShop();
    }

    private void OpenShop()
    {
        // El jugador se queda quieto (ni se mueve ni gira la cámara) mientras compra.
        if (_pm != null) { _pmWasEnabled = _pm.enabled; _pm.enabled = false; }
        ShopUI.Open(() => { if (_pm != null) _pm.enabled = _pmWasEnabled; });
    }

    // ── Puesto provisional: un bloque de color ────────────────────────────────

    /// <summary>Si el objeto está vacío (sin prefab/modelo), se genera un bloque de color con collider.</summary>
    private void BuildPlaceholder()
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "Block";
        block.transform.SetParent(transform, false);
        block.transform.localPosition = new Vector3(0f, _blockSize.y * 0.5f, 0f);
        block.transform.localScale    = _blockSize;
        var r = block.GetComponent<Renderer>();
        r.sharedMaterial = new Material(r.sharedMaterial) { color = _blockColor };
    }

    // ── Aviso en pantalla ─────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (HudHider.IsHidden) return;   // (con la pausa o el game over no se ve ningún aviso)
        if (!_inRange || ShopUI.IsOpen) return;
        if (_promptStyle == null)
            _promptStyle = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                                          normal = { textColor = Color.white } };
        if (_bgTex == null)
        {
            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
            _bgTex.Apply();
        }

        const float pw = 300f, ph = 48f;
        float px = (Screen.width - pw) * 0.5f, py = Screen.height * 0.72f;
        GUI.DrawTexture(new Rect(px, py, pw, ph), _bgTex);
        float a = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f));
        var st = new GUIStyle(_promptStyle) { normal = { textColor = new Color(1f, 0.85f, 0.3f, a) } };
        GUI.Label(new Rect(px, py, pw, ph), Loc.T($"{_promptText}   ·   {PlayerProgress.Coins} monedas", $"{_promptTextEn}   ·   {PlayerProgress.Coins} coins"), st);
    }

    private void OnDestroy()
    {
        if (_bgTex != null) Destroy(_bgTex);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, _maxDistance);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3.8f, "TIENDA  (E)");
    }
#endif
}
