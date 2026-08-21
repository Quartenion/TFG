using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shockwave que aparece al acertar un beat.
/// Se EXPANDE desde el HitZone hacia afuera (onda real de impacto),
/// desvaneciéndose mientras crece.
///
/// Solo aparece en Perfect o Good (controlado desde RhythmHUDController).
/// </summary>
[RequireComponent(typeof(Image))]
[RequireComponent(typeof(RectTransform))]
public class RhythmShockwave : MonoBehaviour
{
    [Header("Expansión")]
    [Tooltip("Cuántas veces su tamaño inicial crece la onda (2 = dobla, 3 = triplica).")]
    public float expandMultiplier = 2.5f;

    [Tooltip("Duración total de la onda en segundos.")]
    public float duration = 0.30f;

    [Header("Color")]
    public Color colorPerfect = new Color(1.0f, 0.85f, 0.1f, 1f);  // dorado para Perfect
    public Color colorGood    = new Color(0.85f, 0.08f, 0.08f, 1f); // rojo para Good

    // ── Privados ───────────────────────────────────────────────────────────────

    private Image         _image;
    private RectTransform _rect;
    private float         _startDiameter;
    private float         _endDiameter;
    private float         _elapsed;
    private bool          _initialized;

    // =========================================================================
    // INICIALIZACIÓN
    // =========================================================================

    public void Init(float targetDiameter, TimingResult timing)
    {
        _image         = GetComponent<Image>();
        _rect          = GetComponent<RectTransform>();
        _startDiameter = targetDiameter;
        _endDiameter   = targetDiameter * expandMultiplier;
        _elapsed       = 0f;
        _initialized   = true;

        // Anchors al centro
        _rect.anchorMin        = new Vector2(0.5f, 0.5f);
        _rect.anchorMax        = new Vector2(0.5f, 0.5f);
        _rect.pivot            = new Vector2(0.5f, 0.5f);
        _rect.anchoredPosition = Vector2.zero;
        _rect.sizeDelta        = new Vector2(_startDiameter, _startDiameter);

        // Color según precisión
        _image.color         = timing == TimingResult.Perfect ? colorPerfect : colorGood;
        _image.raycastTarget = false;
    }

    // =========================================================================
    // UPDATE — expansión
    // =========================================================================

    private void Update()
    {
        if (!_initialized) return;

        _elapsed += Time.deltaTime;
        float t = _elapsed / duration;

        if (t >= 1f) { Destroy(gameObject); return; }

        // Expansión con ease-out (arranque rápido, se frena al final)
        float eased    = 1f - Mathf.Pow(1f - t, 3f);
        float diameter = Mathf.Lerp(_startDiameter, _endDiameter, eased);
        _rect.sizeDelta = new Vector2(diameter, diameter);

        // Fade out: desaparece rápido al principio, lento al final
        Color c = _image.color;
        c.a          = Mathf.Pow(1f - t, 1.5f);
        _image.color = c;
    }
}
