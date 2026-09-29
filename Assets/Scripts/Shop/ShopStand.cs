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
///      Si el objeto está VACÍO (sin mallas ni colliders), se genera un puesto provisional por código.
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
    [Tooltip("Solo se puede usar en freeroam (entre niveles, antes de abrir la puerta).")]
    [SerializeField] private bool _onlyInFreeroam = true;

    [Header("Puesto provisional (si el objeto está vacío; su frente mira a +Z local)")]
    [SerializeField] private Color _standColor = new Color(0.45f, 0.08f, 0.12f);
    [SerializeField] private Color _signColor  = new Color(1f, 0.78f, 0.15f);

    private PlayerMovement _pm;
    private bool _inRange;
    private bool _pmWasEnabled;
    private Transform _floatingCoin;

    private GUIStyle  _promptStyle;
    private Texture2D _bgTex;

    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();
        if (GetComponentInChildren<Renderer>() == null && GetComponentInChildren<Collider>() == null) BuildPlaceholder();
        if (_colliders == null || _colliders.Length == 0) _colliders = GetComponentsInChildren<Collider>();
    }

    private void Update()
    {
        if (_floatingCoin != null)
        {
            _floatingCoin.Rotate(Vector3.up, 120f * Time.deltaTime, Space.World);
            Vector3 p = _floatingCoin.localPosition;
            p.y = 3.3f + 0.15f * Mathf.Sin(Time.time * 2f);
            _floatingCoin.localPosition = p;
        }

        _inRange = false;
        if (ShopUI.IsOpen || ShopUI.ClosedRecently || ContractUI.IsOpen || ContractUI.ClosedRecently) return;
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

    // ── Puesto provisional ────────────────────────────────────────────────────

    private void BuildPlaceholder()
    {
        Material Mat(GameObject g, Color c)
        {
            var r = g.GetComponent<Renderer>();
            var m = new Material(r.sharedMaterial) { color = c };
            r.sharedMaterial = m;
            return m;
        }

        // Mostrador
        var counter = GameObject.CreatePrimitive(PrimitiveType.Cube);
        counter.name = "Counter";
        counter.transform.SetParent(transform, false);
        counter.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        counter.transform.localScale    = new Vector3(3f, 1.1f, 1.2f);
        Mat(counter, _standColor);

        // Postes y cartel
        foreach (float x in new[] { -1.4f, 1.4f })
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            post.transform.SetParent(transform, false);
            post.transform.localPosition = new Vector3(x, 1.8f, -0.45f);
            post.transform.localScale    = new Vector3(0.15f, 2.5f, 0.15f);
            Mat(post, _standColor * 0.7f);
        }
        var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sign.name = "Sign";
        sign.transform.SetParent(transform, false);
        sign.transform.localPosition = new Vector3(0f, 2.9f, -0.45f);
        sign.transform.localScale    = new Vector3(3.1f, 0.6f, 0.12f);
        Mat(sign, _signColor);

        var label = new GameObject("SignText");
        label.transform.SetParent(sign.transform, false);
        label.transform.localPosition = new Vector3(0f, 0f, 0.6f);          // cara delantera (+Z)
        label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);     // legible desde delante
        label.transform.localScale    = new Vector3(1f / 3.1f, 1f / 0.6f, 1f) * 0.12f;
        var tm = label.AddComponent<TextMesh>();
        tm.text = "TIENDA"; tm.fontSize = 60; tm.characterSize = 0.5f; tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center; tm.color = new Color(0.25f, 0.05f, 0.05f); tm.fontStyle = FontStyle.Bold;
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f != null)
        {
            tm.font = f;
            var mr = label.GetComponent<MeshRenderer>();
            if (mr == null) mr = label.AddComponent<MeshRenderer>();
            mr.sharedMaterial = f.material;
        }

        // Moneda flotando encima
        var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        coin.name = "FloatingCoin";
        Destroy(coin.GetComponent<Collider>());
        coin.transform.SetParent(transform, false);
        coin.transform.localPosition = new Vector3(0f, 3.3f, 0f);
        coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        coin.transform.localScale    = new Vector3(0.7f, 0.05f, 0.7f);
        var cm = Mat(coin, _signColor);
        cm.EnableKeyword("_EMISSION");
        cm.SetColor("_EmissionColor", _signColor * 0.6f);
        var pivot = new GameObject("CoinPivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = new Vector3(0f, 3.3f, 0f);
        coin.transform.SetParent(pivot, true);
        _floatingCoin = pivot;
    }

    // ── Aviso en pantalla ─────────────────────────────────────────────────────

    private void OnGUI()
    {
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
        GUI.Label(new Rect(px, py, pw, ph), $"{_promptText}   ·   {PlayerProgress.Coins} monedas", st);
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
