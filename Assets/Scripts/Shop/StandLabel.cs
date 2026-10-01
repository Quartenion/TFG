using TMPro;
using UnityEngine;

/// <summary>
/// RÓTULO FLOTANTE encima de un puesto (tienda / contratos) para que se vea que es interactuable:
///   «TIENDA» + «mira y pulsa E». Mira siempre a la cámara y late suavemente.
/// Solo se ve cuando el puesto se puede usar: fuera del modo prueba, o en sus partes con tienda (4 y 5); en freeroam;
/// y nunca con la pausa / game over ni con la tienda o los contratos abiertos.
/// Lo crean ShopStand y ContractStand solos (no hace falta añadirlo a mano).
/// </summary>
public class StandLabel : MonoBehaviour
{
    private string _es, _en;
    private bool _onlyFreeroam;
    private TextMeshPro _text;
    private Transform _billboard;
    private PlayerMovement _pm;
    private Camera _cam;

    public static StandLabel Create(Transform stand, string es, string en, Color color, bool onlyFreeroam, float extraHeight = 0.6f)
    {
        var host = new GameObject("Rotulo");
        host.transform.SetParent(stand, false);
        var label = host.AddComponent<StandLabel>();
        label._es = es; label._en = en; label._onlyFreeroam = onlyFreeroam;

        // Encima del puesto: por encima de lo más alto de sus mallas.
        float top = stand.position.y + 2f;
        foreach (Renderer r in stand.GetComponentsInChildren<Renderer>())
            if (r.GetComponent<TMP_Text>() == null) top = Mathf.Max(top, r.bounds.max.y);
        host.transform.position = new Vector3(stand.position.x, top + extraHeight, stand.position.z);

        // (Con RectTransform desde el principio: si TextMeshPro lo cambia luego se pierde la posición.)
        var go = new GameObject("Texto", typeof(RectTransform));
        label._text = go.AddComponent<TextMeshPro>();
        go.transform.SetParent(host.transform, false);
        label._billboard = go.transform;
        label._text.fontSize = 8f;
        label._text.alignment = TextAlignmentOptions.Bottom;
        label._text.textWrappingMode = TextWrappingModes.NoWrap;
        label._text.color = color;
        label._text.outlineWidth = 0.25f;
        label._text.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        label._text.rectTransform.sizeDelta = new Vector2(12f, 3f);
        label._text.rectTransform.pivot = new Vector2(0.5f, 0f);
        label.ApplyText();
        Loc.OnChanged += label.ApplyText;
        return label;
    }

    private void OnDestroy() => Loc.OnChanged -= ApplyText;

    private void ApplyText()
    {
        if (_text == null) return;
        _text.text = $"{Loc.T(_es, _en)}\n<size=45%>{Loc.T("mira y pulsa  E", "look and press  E")}</size>";
    }

    private void LateUpdate()
    {
        if (_billboard == null) return;
        bool show = ShouldShow();
        if (_billboard.gameObject.activeSelf != show) _billboard.gameObject.SetActive(show);
        if (!show) return;

        if (_cam == null) _cam = Camera.main;
        if (_cam != null)
        {
            Vector3 d = _billboard.position - _cam.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) _billboard.rotation = Quaternion.LookRotation(d);
        }
        // Latido suave (una vez por segundo).
        float phase = Time.unscaledTime % 1f;
        _billboard.localScale = Vector3.one * (1f + 0.08f * Mathf.Exp(-phase * 6f));
    }

    private bool ShouldShow()
    {
        if (HudHider.IsHidden || ShopUI.IsOpen || ContractUI.IsOpen) return false;
        if (StudySession.Active && !StudySession.ShopAllowed) return false;   // (modo prueba: solo partes 4 y 5)
        if (_onlyFreeroam)
        {
            if (_pm == null) _pm = FindAnyObjectByType<PlayerMovement>();
            if (_pm == null || !_pm.IsFreeroam) return false;
        }
        return true;
    }
}
