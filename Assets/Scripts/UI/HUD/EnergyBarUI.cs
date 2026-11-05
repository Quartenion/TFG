using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Muestra la sangre de la jeringuilla, no la energía del jugador, y cambia entre rojo (modo normal) y azul (modo bonus).
/// </summary>
public class EnergyBarUI : MonoBehaviour
{
    [Header("Referencias UI")]
    [Tooltip("Image con Image Type = Filled (la sangre)")]
    [SerializeField] private Image _fillImage;
    [Tooltip("Texto que muestra el % de sangre acumulada (TMP)")]
    [SerializeField] private TMP_Text _percentText;
    [Tooltip("Texto al lado para combo / bonus (TMP)")]
    [SerializeField] private TMP_Text _comboText;

    [Header("Referencias logicas")]
    [SerializeField] private RhythmInput _rhythmInput;

    [Header("Apariencia")]
    [SerializeField] private float _smoothing = 12f;
    [SerializeField] private Color _redColor = new Color(0.78f, 0.05f, 0.1f, 1f);
    [SerializeField] private Color _blueColor = new Color(0.15f, 0.55f, 1f, 1f);
    [SerializeField] private float _colorLerpSpeed = 6f;

    [Header("Parpadeo al drenar (BloodSucker)")]
    [Tooltip("Color de destello al que parpadea la barra mientras un BloodSucker chupa sangre.")]
    [SerializeField] private Color _drainFlashColor = new Color(1f, 0.85f, 0.25f, 1f);
    [Tooltip("Velocidad del parpadeo mientras te drenan.")]
    [SerializeField] private float _drainBlinkSpeed = 18f;

    [Header("Texto de combo")]
    [SerializeField] private string _bonusFormat = "BONUS x{0}";
    [SerializeField] private string _comboFormat = "Combo {0}";
    [SerializeField] private bool _pulseComboOnHit = true;
    [SerializeField] private float _comboPulseScale = 1.25f;

    private float _displayedPercent;
    private Color _currentColor;
    private Vector3 _comboBaseScale = Vector3.one;
    private int _lastCombo;

    private void Start()
    {
        if (EnergyManager.Instance != null)
            _displayedPercent = EnergyManager.Instance.SyringePercent;

        _currentColor = _redColor;
        if (_fillImage != null) _fillImage.color = _currentColor;
        if (_comboText != null) _comboBaseScale = _comboText.rectTransform.localScale;

        UpdateComboText();
    }

    private void Update()
    {
        if (EnergyManager.Instance != null && _fillImage != null)
        {
            // La barra muestra la sangre que hay en la jeringuilla, no la energía del jugador.
            float target = EnergyManager.Instance.SyringePercent;
            _displayedPercent = Mathf.Lerp(_displayedPercent, target, Time.deltaTime * _smoothing);
            _fillImage.fillAmount = _displayedPercent;
        }

        // Color según el modo (rojo o azul)
        bool blueMode = EnergyManager.Instance != null && EnergyManager.Instance.IsBlueMode;
        Color targetColor = blueMode ? _blueColor : _redColor;
        _currentColor = Color.Lerp(_currentColor, targetColor, Time.deltaTime * _colorLerpSpeed);

        // Parpadea mientras un BloodSucker drena la sangre del jugador.
        Color shown = _currentColor;
        if (EnergyManager.Instance != null && EnergyManager.Instance.IsBeingDrained)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * _drainBlinkSpeed);
            shown = Color.Lerp(_currentColor, _drainFlashColor, pulse);
        }
        if (_fillImage != null) _fillImage.color = shown;

        // Porcentaje de sangre en texto
        if (_percentText != null && EnergyManager.Instance != null)
        {
            int pct = Mathf.RoundToInt(EnergyManager.Instance.SyringePercent * 100f);
            _percentText.text = pct + "%";
        }

        UpdateComboText();
        UpdateComboPulse();
    }

    private void UpdateComboText()
    {
        if (_comboText == null) return;

        if (_rhythmInput == null)
        {
            _comboText.text = string.Empty;
            return;
        }

        int combo = _rhythmInput.CurrentCombo;
        bool bonus = _rhythmInput.IsBonusActive;

        if (bonus)
        {
            _comboText.text = string.Format(_bonusFormat, combo);
            _comboText.color = _blueColor;
        }
        else if (combo > 0)
        {
            _comboText.text = string.Format(_comboFormat, combo);
            _comboText.color = Color.white;
        }
        else
        {
            _comboText.text = string.Empty;
        }
    }

    private void UpdateComboPulse()
    {
        if (_comboText == null || _rhythmInput == null) return;

        int combo = _rhythmInput.CurrentCombo;
        if (_pulseComboOnHit && combo > _lastCombo)
        {
            _comboText.rectTransform.localScale = _comboBaseScale * _comboPulseScale;
        }
        _lastCombo = combo;

        _comboText.rectTransform.localScale = Vector3.Lerp(
            _comboText.rectTransform.localScale,
            _comboBaseScale,
            Time.deltaTime * 10f);
    }
}
