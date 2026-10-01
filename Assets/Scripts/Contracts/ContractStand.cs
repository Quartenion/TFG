using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PUESTO DE CONTRATOS físico del área de freeroam (como la tienda): el jugador lo MIRA y pulsa E → se abre la
/// pantalla de contratos (ContractUI). Mientras está abierta el jugador se queda quieto.
///
/// SETUP:
///   1. Pon este componente en el objeto del puesto (tablón, mesa, NPC…).
///   2. Necesita Collider(s) para detectar la mirada: los de este objeto o sus hijos (o asígnalos a mano).
///      Si el objeto está VACÍO (sin mallas ni colliders), se genera un BLOQUE ROJO por código. Para usar un modelo,
///      mete tu prefab como hijo (o pon el componente en el prefab): entonces no se genera nada.
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
    [SerializeField] private string _promptTextEn = "[ E ]  Contracts";
    [Tooltip("Solo se puede usar en freeroam (entre niveles, antes de abrir la puerta).")]
    [SerializeField] private bool _onlyInFreeroam = true;

    [Header("Bloque por defecto (solo si el objeto está vacío, sin prefab)")]
    [SerializeField] private Color   _blockColor = new Color(0.75f, 0.15f, 0.15f);   // rojo
    [SerializeField] private Vector3 _blockSize  = new Vector3(2f, 2f, 2f);

    private PlayerMovement _pm;
    private bool _inRange, _pmWasEnabled;

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
        _inRange = false;
        if (ContractUI.IsOpen || ContractUI.ClosedRecently || ShopUI.IsOpen || ShopUI.ClosedRecently) return;
        if (StudySession.Active && !StudySession.ShopAllowed) return;   // (modo prueba: contratos solo en las partes 4-5)
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

        int active = ContractCatalog.ActiveHere().Count;   // (firmados de este nivel)

        const float pw = 460f, ph = 48f;
        float px = (Screen.width - pw) * 0.5f, py = Screen.height * 0.72f;
        GUI.DrawTexture(new Rect(px, py, pw, ph), _bgTex);
        float a = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f));
        var st = new GUIStyle(_promptStyle) { normal = { textColor = new Color(0.93f, 0.86f, 0.7f, a) } };
        int toClaim = 0;
        foreach (ContractDef c in ContractCatalog.All) if (PlayerProgress.IsContractClaimable(c.id)) toClaim++;
        string extra = toClaim > 0 ? Loc.T($"   ·   ¡{toClaim} por cobrar!", $"   ·   {toClaim} to claim!") : "";
        GUI.Label(new Rect(px, py, pw, ph), Loc.T($"{_promptText}   ·   firmados {active}/{ContractCatalog.MaxActive}{extra}", $"{_promptTextEn}   ·   signed {active}/{ContractCatalog.MaxActive}{extra}"), st);
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
