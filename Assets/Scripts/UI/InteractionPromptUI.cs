using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Recuadro del aviso de interacción (ver <see cref="InteractionPrompt"/>).</summary>
[AddComponentMenu("HUD/Interaction Prompt UI")]
public class InteractionPromptUI : MonoBehaviour
{
    private static InteractionPromptUI _instance;

    [SerializeField, HideInInspector] private bool _baked;
    private RectTransform _box;
    private TextMeshProUGUI _label;
    private Canvas _canvas;

    private int   _shownFrame = -10;
    private Color _color = Color.white;
    private float _minWidth;

    public static void Show(string text, float width, Color color)
    {
        if (_instance == null) Ensure();
        if (_instance == null) return;
        _instance.Set(text, width, color);
    }

    private static void Ensure()
    {
        foreach (InteractionPromptUI p in FindObjectsByType<InteractionPromptUI>(FindObjectsInactive.Include))
            if (p._baked) { p.gameObject.SetActive(true); _instance = p; return; }
        new GameObject("InteractionPrompt").AddComponent<InteractionPromptUI>();
    }

    /// <summary>Herramienta de horneado (editor): construye la interfaz dentro de este objeto, una sola vez.</summary>
    public void BakeBuild() { _baked = true; Build(); }

    private void Awake()
    {
        _instance = this;
        if (_baked) { using (UIBind.Scope()) Build(); }
        else Build();
        _canvas.enabled = false;
    }

    private void OnDestroy() { if (_instance == this) _instance = null; }

    private void Build()
    {
        _canvas = UIKit.CreateCanvas("PromptCanvas", 80, transform);
        _box = SimpleUI.Box("Prompt", _canvas.transform, new Color(0f, 0f, 0f, 0.55f)).rectTransform;
        // Centrado, a un 28 % de la altura desde abajo (donde estaba el aviso antiguo).
        UIKit.Place(_box, new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 56f));
        _box.GetComponent<Image>().raycastTarget = false;
        _label = SimpleUI.Lbl("Text", _box, "", 26f, TextAlignmentOptions.Center);
        SimpleUI.Stretch(_label.rectTransform, 16f, 16f, 4f, 4f);
    }

    private void Set(string text, float width, Color color)
    {
        _shownFrame = Time.frameCount;
        if (_label.text != text) _label.text = text;
        _color = color;
        _minWidth = width;
    }

    private void LateUpdate()
    {
        bool on = Time.frameCount - _shownFrame <= 1 && !HudHider.IsHidden;
        if (_canvas.enabled != on) _canvas.enabled = on;
        if (!on) return;

        // El recuadro se ajusta al texto (como mínimo el ancho que pide quien lo muestra).
        float w = Mathf.Max(_minWidth, _label.preferredWidth + 40f);
        if (!Mathf.Approximately(_box.sizeDelta.x, w)) _box.sizeDelta = new Vector2(w, _box.sizeDelta.y);

        float a = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f));
        _label.color = new Color(_color.r, _color.g, _color.b, a);
    }
}
