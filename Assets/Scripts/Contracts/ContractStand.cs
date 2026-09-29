using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PUESTO DE CONTRATOS físico del área de freeroam (como la tienda): el jugador lo MIRA y pulsa E → se abre la
/// pantalla de contratos (ContractUI). Mientras está abierta el jugador se queda quieto.
///
/// SETUP:
///   1. Pon este componente en el objeto del puesto (tablón, mesa, NPC…).
///   2. Necesita Collider(s) para detectar la mirada: los de este objeto o sus hijos (o asígnalos a mano).
///      Si el objeto está VACÍO (sin mallas ni colliders), se genera un puesto provisional por código
///      (un tablón de anuncios con papeles clavados). Su frente mira a +Z local.
///   3. Por defecto solo funciona en FREEROAM.
/// </summary>
[AddComponentMenu("Level/Contract Stand")]
public class ContractStand : MonoBehaviour
{
    [Header("Interacción")]
    [Tooltip("Colliders que cuentan como 'el puesto' al mirarlo. Vacío = todos los de este objeto y sus hijos.")]
    [SerializeField] private Collider[] _colliders;
    [Tooltip("Distancia máxima (m) al mirarlo para que aparezca el aviso.")]
    [SerializeField] private float _maxDistance = 4f;
    [Tooltip("Texto del aviso.")]
    [SerializeField] private string _promptText = "[ E ]  Contratos";
    [Tooltip("Solo se puede usar en freeroam (entre niveles, antes de abrir la puerta).")]
    [SerializeField] private bool _onlyInFreeroam = true;

    [Header("Puesto provisional (si el objeto está vacío)")]
    [SerializeField] private Color _boardColor = new Color(0.36f, 0.24f, 0.14f);
    [SerializeField] private Color _paperColor = new Color(0.93f, 0.86f, 0.7f);
    [SerializeField] private Color _signColor  = new Color(0.2f, 0.55f, 0.3f);

    private PlayerMovement _pm;
    private bool _inRange, _pmWasEnabled;
    private Transform _quill;

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
        if (_quill != null)
        {
            Vector3 p = _quill.localPosition;
            p.y = 3.35f + 0.12f * Mathf.Sin(Time.time * 2f);
            _quill.localPosition = p;
            _quill.localRotation = Quaternion.Euler(0f, Time.time * 60f, 35f);
        }

        _inRange = false;
        if (ContractUI.IsOpen || ContractUI.ClosedRecently || ShopUI.IsOpen || ShopUI.ClosedRecently) return;
        if (_onlyInFreeroam && (_pm == null || !_pm.IsFreeroam)) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _maxDistance, ~0, QueryTriggerInteraction.Collide))
            foreach (Collider c in _colliders)
                if (c != null && hit.collider == c) { _inRange = true; break; }

        if (_inRange && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) Open();
    }

    private void Open()
    {
        if (_pm != null) { _pmWasEnabled = _pm.enabled; _pm.enabled = false; }
        ContractUI.Open(() => { if (_pm != null) _pm.enabled = _pmWasEnabled; });
    }

    // ── Puesto provisional: tablón de anuncios ────────────────────────────────

    private void BuildPlaceholder()
    {
        GameObject Box(string n, Vector3 pos, Vector3 scale, Color c, Transform parent = null, bool collider = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = n;
            if (!collider) Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent != null ? parent : transform, false);
            g.transform.localPosition = pos;
            g.transform.localScale    = scale;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = new Material(r.sharedMaterial) { color = c };
            return g;
        }

        // Postes + tablón
        foreach (float x in new[] { -1.35f, 1.35f })
            Box("Post", new Vector3(x, 1.3f, 0f), new Vector3(0.18f, 2.6f, 0.18f), _boardColor * 0.75f);
        Box("Board", new Vector3(0f, 1.75f, 0f), new Vector3(2.6f, 1.5f, 0.12f), _boardColor);

        // Papeles clavados (los contratos), ligeramente torcidos
        var rng = new System.Random(7);
        for (int i = 0; i < 6; i++)
        {
            float x = -0.85f + (i % 3) * 0.85f + (float)(rng.NextDouble() - 0.5) * 0.12f;
            float y = 2.1f - (i / 3) * 0.7f + (float)(rng.NextDouble() - 0.5) * 0.08f;
            var paper = Box("Paper", new Vector3(x, y, 0.075f), new Vector3(0.55f, 0.55f, 0.01f), _paperColor, null, false);
            paper.transform.localRotation = Quaternion.Euler(0f, 0f, (float)(rng.NextDouble() - 0.5) * 16f);
            Box("Pin", new Vector3(x, y + 0.22f, 0.09f), new Vector3(0.05f, 0.05f, 0.03f), new Color(0.8f, 0.15f, 0.15f), null, false);
        }

        // Cartel
        var sign = Box("Sign", new Vector3(0f, 2.8f, 0f), new Vector3(2.9f, 0.5f, 0.14f), _signColor);
        var label = new GameObject("SignText");
        label.transform.SetParent(sign.transform, false);
        label.transform.localPosition = new Vector3(0f, 0f, 0.6f);          // cara delantera (+Z)
        label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);     // legible desde delante
        label.transform.localScale    = new Vector3(1f / 2.9f, 1f / 0.5f, 1f) * 0.11f;
        var tm = label.AddComponent<TextMesh>();
        tm.text = "CONTRATOS"; tm.fontSize = 60; tm.characterSize = 0.5f; tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center; tm.color = _paperColor; tm.fontStyle = FontStyle.Bold;
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f != null)
        {
            tm.font = f;
            var mr = label.GetComponent<MeshRenderer>();
            if (mr == null) mr = label.AddComponent<MeshRenderer>();
            mr.sharedMaterial = f.material;
        }

        // Pluma flotando encima (para firmar)
        var pivot = new GameObject("QuillPivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = new Vector3(0f, 3.35f, 0f);
        var feather = Box("Quill", Vector3.zero, new Vector3(0.08f, 0.7f, 0.02f), _paperColor, pivot, false);
        feather.GetComponent<Renderer>().sharedMaterial.EnableKeyword("_EMISSION");
        feather.GetComponent<Renderer>().sharedMaterial.SetColor("_EmissionColor", _paperColor * 0.4f);
        _quill = pivot;
    }

    // ── Aviso en pantalla ─────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!_inRange || ContractUI.IsOpen) return;
        if (_promptStyle == null)
            _promptStyle = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                                          normal = { textColor = Color.white } };
        if (_bgTex == null)
        {
            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
            _bgTex.Apply();
        }

        int active = 0;
        foreach (string id in PlayerProgress.ActiveContracts) if (ContractCatalog.Get(id) != null) active++;

        const float pw = 460f, ph = 48f;
        float px = (Screen.width - pw) * 0.5f, py = Screen.height * 0.72f;
        GUI.DrawTexture(new Rect(px, py, pw, ph), _bgTex);
        float a = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f));
        var st = new GUIStyle(_promptStyle) { normal = { textColor = new Color(0.93f, 0.86f, 0.7f, a) } };
        int toClaim = 0;
        foreach (ContractDef c in ContractCatalog.All) if (PlayerProgress.IsContractClaimable(c.id)) toClaim++;
        string extra = toClaim > 0 ? $"   ·   ¡{toClaim} por cobrar!" : "";
        GUI.Label(new Rect(px, py, pw, ph), $"{_promptText}   ·   firmados {active}/{ContractCatalog.MaxActive}{extra}", st);
    }

    private void OnDestroy()
    {
        if (_bgTex != null) Destroy(_bgTex);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 0.4f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, _maxDistance);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3.8f, "CONTRATOS  (E)");
    }
#endif
}
