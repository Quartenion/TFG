using System;
using UnityEngine;

/// <summary>
/// Base común de los PUESTOS del área de freeroam (tienda y contratos): el jugador lo MIRA (raycast desde la cámara)
/// y pulsa E → se abre su panel. Mientras está abierto el jugador se queda quieto; al cerrarlo sigue explorando.
/// Antes ShopStand y ContractStand tenían una copia cada uno de toda esta lógica; ahora solo definen qué panel abren,
/// el texto del aviso y sus colores.
///
/// Los campos serializados conservan los nombres de antes, así que las escenas y prefabs no pierden sus valores.
/// </summary>
public abstract class FreeroamStand : MonoBehaviour
{
    [Header("Interacción")]
    [Tooltip("Colliders que cuentan como 'el puesto' al mirarlo. Vacío = todos los de este objeto y sus hijos.")]
    [SerializeField] protected Collider[] _colliders;
    [Tooltip("Distancia máxima (m) al mirarlo para que aparezca el aviso.")]
    [SerializeField] protected float _maxDistance = 4f;
    [Tooltip("Texto del aviso.")]
    [SerializeField] protected string _promptText = "[ E ]";
    [SerializeField] protected string _promptTextEn = "[ E ]";
    [Tooltip("Solo se puede usar en freeroam (entre niveles, antes de abrir la puerta).")]
    [SerializeField] protected bool _onlyInFreeroam = true;
    [Tooltip("Rótulo flotante encima del puesto («… · mira y pulsa E»).")]
    [SerializeField] protected bool _showLabel = true;

    [Header("Bloque por defecto (solo si el objeto está vacío, sin prefab)")]
    [SerializeField] protected Color   _blockColor = Color.gray;
    [SerializeField] protected Vector3 _blockSize  = new Vector3(2f, 2f, 2f);

    private PlayerMovement _pm;
    private bool _inRange, _pmWasEnabled;

    // ── Lo que cambia en cada puesto ──────────────────────────────────────────

    /// <summary>Rótulo flotante (ES, EN) y su color.</summary>
    protected abstract string LabelEs { get; }
    protected abstract string LabelEn { get; }
    protected abstract Color  LabelColor { get; }
    /// <summary>Texto del aviso en pantalla, su ancho y su color.</summary>
    protected abstract string PromptLine();
    protected abstract float  PromptWidth { get; }
    protected abstract Color  PromptColor { get; }
    /// <summary>Abre el panel; 'onClose' se debe llamar al cerrarlo (devuelve el control al jugador).</summary>
    protected abstract void OpenPanel(Action onClose);
    /// <summary>Gizmo del editor.</summary>
    protected abstract Color  GizmoColor { get; }
    protected abstract string GizmoLabel { get; }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();
        if (GetComponentInChildren<Renderer>() == null && GetComponentInChildren<Collider>() == null) BuildPlaceholder();
        if (_colliders == null || _colliders.Length == 0) _colliders = GetComponentsInChildren<Collider>();
        if (_showLabel) StandLabel.Create(transform, LabelEs, LabelEn, LabelColor, _onlyInFreeroam);
    }

    private void Update()
    {
        _inRange = false;
        if (ShopUI.IsOpen || ShopUI.ClosedRecently || ContractUI.IsOpen || ContractUI.ClosedRecently) return;
        if (StudySession.Active && !StudySession.ShopAllowed) return;   // (modo prueba: tienda y contratos solo en las partes 4-5)
        if (_onlyInFreeroam && (_pm == null || !_pm.IsFreeroam)) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, _maxDistance, ~0, QueryTriggerInteraction.Collide))
            foreach (Collider c in _colliders)
                if (c != null && hit.collider == c) { _inRange = true; break; }

        if (_inRange && Keybinds.Pressed(Keybinds.Bind.Interact)) Open();
    }

    private void Open()
    {
        // El jugador se queda quieto (ni se mueve ni gira la cámara) mientras el panel está abierto.
        if (_pm != null) { _pmWasEnabled = _pm.enabled; _pm.enabled = false; }
        OpenPanel(() => { if (_pm != null) _pm.enabled = _pmWasEnabled; });
    }

    // ── Puesto provisional: un bloque de color ────────────────────────────────

    /// <summary>Si el objeto está vacío (sin prefab/modelo), se genera un bloque de color con collider.</summary>
    private void BuildPlaceholder()
    {
        var block = RuntimeMaterials.Primitive(PrimitiveType.Cube);
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
        if (!_inRange || ShopUI.IsOpen || ContractUI.IsOpen) return;
        InteractionPrompt.Draw(Keybinds.Fix(PromptLine()), PromptWidth, PromptColor);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = GizmoColor;
        Gizmos.DrawWireSphere(transform.position, _maxDistance);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3.8f, GizmoLabel);
    }
#endif
}
