using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Pantalla de calibracion de latencia de audio.
///
/// COMO FUNCIONA:
///   1. Reproduce un metrónomo visual (flash en pantalla) y un click.wav al ritmo.
///   2. El jugador hace clic al ritmo durante N repeticiones.
///   3. Calculamos la media del error sistematico y lo aplicamos a BeatManager.
///   4. El offset se guarda en PlayerPrefs para futuras sesiones.
///
/// SETUP en Unity:
///   1. Crea una escena o un panel de UI para la calibracion.
///   2. Arrastra un AudioSource con tu click.wav al campo _metronomeAudio.
///   3. Arrastra el BeatManager al campo _beatManager.
///   4. Arrastra los elementos de UI opcionales (label, flash image).
///   5. Conecta StartCalibration() a un boton de la UI.
///   6. Conecta OnCalibrationComplete a lo que quieras hacer despues (ej: cargar escena).
///
/// NOTA ACADEMICA (TFG):
///   La latencia tipica en Windows con WASAPI es 20-80ms.
///   En Mac con CoreAudio es 10-30ms.
///   Esta calibracion permite compensarla de forma personalizada por dispositivo.
/// </summary>
public class LatencyCalibrator : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Inspector
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Referencias")]
    [SerializeField] private BeatManager _beatManager;
    [SerializeField] private AudioSource _metronomeAudio;
    [SerializeField] private AudioClip   _clickSound;

    [Header("Configuracion")]
    [Tooltip("BPM del metrónomo de calibracion. 80 es comodo para la mayoria de usuarios.")]
    [SerializeField] private float _calibrationBpm = 80f;

    [Tooltip("Numero de clicks que se recogen para calcular la media. Minimo 4, recomendado 8.")]
    [SerializeField] private int _sampleCount = 8;

    [Tooltip("Descartar el X% de muestras mas extremas (outliers). 0 = sin descarte.")]
    [Range(0f, 0.4f)]
    [SerializeField] private float _trimPercent = 0.2f;

    [Header("UI — opcional")]
    [Tooltip("Imagen o panel que hace flash al ritmo del metrónomo. Puede ser null.")]
    [SerializeField] private UnityEngine.UI.Image _flashImage;

    [Tooltip("Color del flash al ritmo.")]
    [SerializeField] private Color _flashColor = new Color(1f, 1f, 1f, 0.8f);

    [Tooltip("Duracion del flash visual en segundos.")]
    [SerializeField] private float _flashDuration = 0.08f;

    [Tooltip("Etiqueta de instrucciones / resultado. Puede ser null.")]
    [SerializeField] private TextMeshProUGUI _instructionLabel;

    [Tooltip("Etiqueta que muestra el offset calculado en tiempo real.")]
    [SerializeField] private TextMeshProUGUI _offsetLabel;

    [Header("Eventos")]
    [Tooltip("Se dispara cuando la calibracion termina. Pasa el offset calculado en segundos.")]
    public UnityEvent<float> OnCalibrationComplete;

    // ──────────────────────────────────────────────────────────────────────────
    // Estado interno
    // ──────────────────────────────────────────────────────────────────────────

    private bool _isCalibrating;
    private float _beatDuration;
    private float _songTime;
    private List<float> _rawOffsets = new List<float>();

    private InputAction _clickAction;
    private Coroutine   _metronomeRoutine;
    private Coroutine   _flashRoutine;

    // ──────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _clickAction = new InputAction("CalibrationClick", binding: "<Mouse>/leftButton");
        _clickAction.performed += OnClick;
        _clickAction.Enable();
    }

    private void OnDisable()
    {
        if (_clickAction != null)
        {
            _clickAction.performed -= OnClick;
            _clickAction.Disable();
        }
        StopAllCoroutines();
        _isCalibrating = false;
    }

    private void Update()
    {
        if (!_isCalibrating) return;
        _songTime += Time.deltaTime;

        // Actualizar label con el offset actual si tiene suficientes muestras.
        if (_offsetLabel != null && _rawOffsets.Count >= 2)
        {
            float current = ComputeOffset(_rawOffsets);
            _offsetLabel.text = $"Offset actual: {current * 1000f:+0.0;-0.0} ms ({_rawOffsets.Count}/{_sampleCount} muestras)";
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // API publica
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inicia la calibracion. Conecta a un boton de UI.
    /// </summary>
    public void StartCalibration()
    {
        if (_isCalibrating) return;
        if (_beatManager == null)
        {
            Debug.LogError("[LatencyCalibrator] BeatManager no asignado.");
            return;
        }

        _isCalibrating   = true;
        _rawOffsets.Clear();
        _songTime        = 0f;
        _beatDuration    = 60f / _calibrationBpm;

        if (_instructionLabel != null)
            _instructionLabel.text = $"Haz clic al ritmo del metrónomo. Se necesitan {_sampleCount} clicks.";

        if (_offsetLabel != null)
            _offsetLabel.text = "";

        _metronomeRoutine = StartCoroutine(MetronomeRoutine());
    }

    /// <summary>
    /// Cancela la calibracion en curso sin aplicar cambios.
    /// </summary>
    public void CancelCalibration()
    {
        _isCalibrating = false;
        StopAllCoroutines();
        if (_flashImage != null) _flashImage.color = Color.clear;
        if (_instructionLabel != null) _instructionLabel.text = "Calibracion cancelada.";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Input
    // ──────────────────────────────────────────────────────────────────────────

    private void OnClick(InputAction.CallbackContext ctx)
    {
        if (!_isCalibrating) return;

        // Calculamos el error SIGNADO respecto al beat mas cercano.
        // Positivo = el click llego tarde respecto al beat que escucho Unity.
        // Si es positivo, el jugador escucha el beat despues de que Unity lo "play",
        // por lo que necesitamos restar ese valor del tiempo percibido.
        float mod      = _songTime % _beatDuration;
        float distFwd  = mod;                         // tiempo desde el ultimo beat
        float distBack = _beatDuration - mod;         // tiempo hasta el siguiente beat

        float signedOffset;
        if (distFwd < distBack)
            signedOffset = distFwd;       // el click llego justo despues del beat -> tarde
        else
            signedOffset = -distBack;     // el click llego antes del siguiente beat -> pronto

        _rawOffsets.Add(signedOffset);

        if (_instructionLabel != null)
            _instructionLabel.text = $"Click registrado ({_rawOffsets.Count}/{_sampleCount})...";

        // Cuando tenemos suficientes muestras, aplicamos y terminamos.
        if (_rawOffsets.Count >= _sampleCount)
            FinishCalibration();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Metrónomo visual + auditivo
    // ──────────────────────────────────────────────────────────────────────────

    private IEnumerator MetronomeRoutine()
    {
        // Esperar medio beat antes de empezar para dar tiempo al jugador a prepararse.
        yield return new WaitForSeconds(_beatDuration * 0.5f);

        while (_isCalibrating)
        {
            // Sonido
            if (_metronomeAudio != null && _clickSound != null)
                _metronomeAudio.PlayOneShot(_clickSound);

            // Flash visual
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            if (_flashImage != null)
                _flashRoutine = StartCoroutine(FlashRoutine());

            yield return new WaitForSeconds(_beatDuration);
        }
    }

    private IEnumerator FlashRoutine()
    {
        if (_flashImage == null) yield break;

        _flashImage.color = _flashColor;
        float elapsed = 0f;
        while (elapsed < _flashDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / _flashDuration;
            Color c = _flashColor;
            c.a = Mathf.Lerp(_flashColor.a, 0f, t);
            _flashImage.color = c;
            yield return null;
        }
        _flashImage.color = Color.clear;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Calculos
    // ──────────────────────────────────────────────────────────────────────────

    private void FinishCalibration()
    {
        _isCalibrating = false;
        StopAllCoroutines();
        if (_flashImage != null) _flashImage.color = Color.clear;

        float offset = ComputeOffset(_rawOffsets);

        // Aplicar al BeatManager y guardar en PlayerPrefs.
        if (_beatManager != null)
            _beatManager.SetLatencyOffset(offset);

        string sign = offset >= 0f ? "+" : "";
        if (_instructionLabel != null)
            _instructionLabel.text = $"Calibracion completada.\nOffset: {sign}{offset * 1000f:0.0} ms\n(Guardado automaticamente)";

        if (_offsetLabel != null)
            _offsetLabel.text = $"Offset aplicado: {sign}{offset * 1000f:0.0} ms";

        Debug.Log($"[LatencyCalibrator] Offset calculado: {offset * 1000f:0.0} ms y aplicado al BeatManager.");

        OnCalibrationComplete?.Invoke(offset);
    }

    /// <summary>
    /// Calcula la media recortada (trimmed mean) de los offsets para eliminar outliers.
    /// </summary>
    private float ComputeOffset(List<float> samples)
    {
        if (samples.Count == 0) return 0f;

        var sorted = new List<float>(samples);
        sorted.Sort();

        int trimCount = Mathf.FloorToInt(sorted.Count * _trimPercent);
        int start     = trimCount;
        int end       = sorted.Count - trimCount;

        if (start >= end) return sorted[sorted.Count / 2]; // fallback: mediana

        float sum = 0f;
        for (int i = start; i < end; i++)
            sum += sorted[i];

        return sum / (end - start);
    }
}
