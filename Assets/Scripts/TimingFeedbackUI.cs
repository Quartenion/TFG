using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Muestra el resultado de timing del ultimo click del jugador (PERFECT, GOOD, MISS)
/// con una animacion de punch-scale y fade-out.
///
/// SETUP en Unity:
///   1. Crea un TextMeshPro - Text (UI) en el Canvas, encima del crosshair.
///   2. Arrastra ese TextMeshPro al campo _label de este componente.
///   3. En RhythmInput -> OnTimingResult, conecta TimingFeedbackUI.Show(TimingResult).
///   4. Ajusta colores, duracion y escala desde el Inspector si quieres.
///
/// Este script NO necesita referencia a RhythmInput. Se conecta unicamente via
/// el UnityEvent OnTimingResult, manteniendolo completamente desacoplado.
/// </summary>
public class TimingFeedbackUI : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("El TextMeshPro que mostrara PERFECT, GOOD o MISS.")]
    [SerializeField] private TextMeshProUGUI _label;

    [Header("Colores por resultado")]
    [SerializeField] private Color _perfectColor = new Color(1f, 0.85f, 0.1f);
    [SerializeField] private Color _goodColor    = Color.white;
    [SerializeField] private Color _missColor    = new Color(1f, 0.2f, 0.2f);

    [Header("Textos por resultado")]
    [SerializeField] private string _perfectText = "PERFECT";
    [SerializeField] private string _goodText    = "GOOD";
    [SerializeField] private string _missText    = "MISS";

    [Header("Animacion")]
    [Tooltip("Tiempo total que el texto permanece visible.")]
    [SerializeField] private float _displayDuration = 0.55f;

    [Tooltip("Tiempo del fade-out al final.")]
    [SerializeField] private float _fadeOutDuration = 0.25f;

    [Tooltip("Escala maxima al aparecer (punch scale).")]
    [SerializeField] private float _punchScale = 1.4f;

    [Tooltip("Velocidad con la que la escala vuelve a 1.")]
    [SerializeField] private float _scaleReturnSpeed = 12f;

    [Tooltip("Cuantos pixels sube el texto durante la animacion.")]
    [SerializeField] private float _riseAmount = 18f;

    [Header("Offset de posicion")]
    [Tooltip("Posicion base del texto relativa a su anchoredPosition original.")]
    [SerializeField] private Vector2 _baseOffset = new Vector2(0f, 40f);

    private Coroutine _currentAnimation;
    private Vector2   _labelBaseAnchoredPos;
    private bool      _initialized;

    private void Awake()
    {
        if (_label == null)
        {
            Debug.LogWarning("[TimingFeedbackUI] No hay TextMeshProUGUI asignado en el Inspector.");
            return;
        }

        _labelBaseAnchoredPos = _label.rectTransform.anchoredPosition + _baseOffset;
        _label.text  = "";
        _label.alpha = 0f;
        _label.transform.localScale = Vector3.one;
        _initialized = true;
    }

    /// <summary>
    /// Muestra el feedback de timing. Conecta al UnityEvent OnTimingResult de RhythmInput.
    /// </summary>
    public void Show(TimingResult result)
    {
        if (!_initialized || _label == null) return;
        if (_currentAnimation != null) StopCoroutine(_currentAnimation);
        _currentAnimation = StartCoroutine(AnimateLabel(result));
    }

    private IEnumerator AnimateLabel(TimingResult result)
    {
        _label.text  = GetText(result);
        _label.color = GetColor(result);
        _label.alpha = 1f;
        _label.rectTransform.localScale      = Vector3.one * _punchScale;
        _label.rectTransform.anchoredPosition = _labelBaseAnchoredPos;

        float elapsed  = 0f;
        float holdTime = _displayDuration - _fadeOutDuration;

        // Fase 1: mantener visible, escala vuelve a 1 y texto sube suavemente
        while (elapsed < holdTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / holdTime;

            float s = Mathf.Lerp(_punchScale, 1f, elapsed * _scaleReturnSpeed);
            _label.rectTransform.localScale = Vector3.one * Mathf.Max(1f, s);

            float rise = Mathf.Lerp(0f, _riseAmount, t);
            _label.rectTransform.anchoredPosition = _labelBaseAnchoredPos + Vector2.up * rise;

            yield return null;
        }

        // Fase 2: fade-out
        float fadeElapsed = 0f;
        while (fadeElapsed < _fadeOutDuration)
        {
            fadeElapsed += Time.deltaTime;
            float t = fadeElapsed / _fadeOutDuration;
            _label.alpha = Mathf.Lerp(1f, 0f, t);

            float rise = Mathf.Lerp(_riseAmount, _riseAmount * 1.4f, t);
            _label.rectTransform.anchoredPosition = _labelBaseAnchoredPos + Vector2.up * rise;

            yield return null;
        }

        _label.text  = "";
        _label.alpha = 0f;
        _label.rectTransform.localScale      = Vector3.one;
        _label.rectTransform.anchoredPosition = _labelBaseAnchoredPos;
        _currentAnimation = null;
    }

    private string GetText(TimingResult result)
    {
        if (result == TimingResult.Perfect) return _perfectText;
        if (result == TimingResult.Good)    return _goodText;
        return _missText;
    }

    private Color GetColor(TimingResult result)
    {
        if (result == TimingResult.Perfect) return _perfectColor;
        if (result == TimingResult.Good)    return _goodColor;
        return _missColor;
    }
}
