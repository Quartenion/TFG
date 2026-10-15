using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Feedback de precisión del corazón del crosshair. En cada clic, el corazón
/// destella con un color según el timing y late (en aciertos):
///   • Perfect → destello VERDE + latido.
///   • Good    → destello BLANCO + latido.
///   • Miss    → destello ROJO (sin latido) — antes el fallo no daba feedback.
/// El color vuelve suavemente al normal. Es el indicador de acierto del propio
/// crosshair, junto con el anillo entrante del RhythmHUDController.
///
/// SETUP:
///   1. Añade este script al GameObject "Heart" del crosshair (tiene una Image).
///   2. Asigna RhythmInput en el Inspector.
/// </summary>
public class HeartBeat : MonoBehaviour
{
    [Header("Referencias")]
    public RhythmInput rhythmInput;

    [Header("Latido (Good / Perfect)")]
    [Tooltip("Escala máxima durante el latido (1.35 = crece un 35%).")]
    public float beatScale = 1.35f;

    [Tooltip("Duración total del latido en segundos.")]
    public float beatDuration = 0.18f;

    [Header("Colores por timing")]
    [Tooltip("Color en reposo del corazón.")]
    public Color normalColor  = Color.white;

    [Tooltip("Destello al acertar Perfect.")]
    public Color perfectColor = new Color(0.25f, 1f, 0.35f, 1f);    // verde

    [Tooltip("Destello al acertar Good.")]
    public Color goodColor    = Color.white;                         // blanco

    [Tooltip("Destello al fallar (Miss).")]
    public Color missColor    = new Color(0.92f, 0.12f, 0.12f, 1f);  // rojo

    [Tooltip("Velocidad a la que el corazón vuelve al color normal (lerp/s).")]
    public float colorReturnSpeed = 2.5f;

    [Header("Corazón firme (mantener clic, mejora de la tienda)")]
    [Tooltip("Color del corazón mientras lo mantienes apretado.")]
    public Color holdColor = new Color(1f, 0.3f, 0.35f, 1f);
    [Tooltip("Escala del corazón mientras lo mantienes apretado (< 1 = se encoge, apretado).")]
    public float holdScale = 0.85f;
    private float _hold01;

    // ── Privados ───────────────────────────────────────────────────────────────

    private Image     _heartImage;
    private Vector3   _baseScale;
    private Color     _currentColor;
    private Coroutine _beatCoroutine;

    // =========================================================================
    // UNITY
    // =========================================================================

    private void Awake()
    {
        _heartImage   = GetComponent<Image>();
        _baseScale    = transform.localScale;
        _currentColor = normalColor;

        if (_heartImage != null)
            _heartImage.color = normalColor;
    }

    private void Start()
    {
        if (rhythmInput != null)
            rhythmInput.OnTimingResult.AddListener(HandleTimingResult);
        else
            Debug.LogWarning("[HeartBeat] RhythmInput no asignado.", this);
    }

    private void OnDestroy()
    {
        if (rhythmInput != null)
            rhythmInput.OnTimingResult.RemoveListener(HandleTimingResult);
    }

    private void Update()
    {
        if (_heartImage == null) return;

        // Corazón firme: mientras se mantiene el clic, el corazón se queda apretado y más rojo.
        bool holding = EnergyManager.Instance != null && EnergyManager.Instance.IsHoldingHeart;
        _hold01 = Mathf.MoveTowards(_hold01, holding ? 1f : 0f, Time.deltaTime * 8f);
        Color rest = Color.Lerp(normalColor, holdColor, _hold01);

        _currentColor     = Color.Lerp(_currentColor, rest, Time.deltaTime * colorReturnSpeed);
        _heartImage.color = _currentColor;
        if (_beatCoroutine == null) transform.localScale = _baseScale * Mathf.Lerp(1f, holdScale, _hold01);
    }

    // =========================================================================
    // EVENTOS
    // =========================================================================

    private void HandleTimingResult(TimingResult result)
    {
        // Destello del color correspondiente al timing (incluye Miss en rojo).
        _currentColor = ColorFor(result);

        // El corazón sólo late en acierto; el fallo sólo destella.
        if (result != TimingResult.Miss)
        {
            if (_beatCoroutine != null) StopCoroutine(_beatCoroutine);
            _beatCoroutine = StartCoroutine(DoBeat());
        }
    }

    /// <summary>Destello + latido con un color concreto (p. ej. el Auto-latido de la tienda).</summary>
    public void Flash(Color color)
    {
        _currentColor = color;
        if (!isActiveAndEnabled) return;   // (oculto, p. ej. en el kayak: no se pueden lanzar corrutinas)
        if (_beatCoroutine != null) StopCoroutine(_beatCoroutine);
        _beatCoroutine = StartCoroutine(DoBeat());
    }

    private Color ColorFor(TimingResult r)
        => r == TimingResult.Perfect ? perfectColor
         : r == TimingResult.Good    ? goodColor
                                     : missColor;

    // =========================================================================
    // ANIMACIÓN DEL LATIDO
    // =========================================================================

    /// <summary>Latido: expande rápido y vuelve suave.</summary>
    private IEnumerator DoBeat()
    {
        // Fase 1: expansión rápida (25% del tiempo)
        float expandTime = Mathf.Max(0.01f, beatDuration * 0.25f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / expandTime;
            transform.localScale = _baseScale * Mathf.Lerp(1f, beatScale, Mathf.Clamp01(t));
            yield return null;
        }

        // Fase 2: retorno suave (75% del tiempo)
        float returnTime = Mathf.Max(0.01f, beatDuration * 0.75f);
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / returnTime;
            transform.localScale = _baseScale * Mathf.Lerp(beatScale, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }

        transform.localScale = _baseScale;
        _beatCoroutine = null;
    }
}
