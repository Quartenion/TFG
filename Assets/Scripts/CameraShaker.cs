using UnityEngine;

/// <summary>
/// Screen shake basado en "trauma": añade trauma [0-1] y la cámara sacude
/// proporcionalmente a trauma², decayendo hasta 0.
///
/// IMPORTANTE — este script NO escribe el transform directamente.
/// Solo calcula y expone PositionOffset y RollOffset.
/// CameraDashEffect (u otro compositor) los consume en su LateUpdate y los
/// suma al resto de efectos, evitando que dos scripts se machaquen entre sí.
///
/// SETUP:
///   1. Añade este componente en cualquier GameObject de la escena (p.ej. la cámara).
///   2. Llama a CameraShaker.Instance.Shake(trauma) desde FallingObstacle u otros scripts.
///      trauma: 0.2 = suave  |  0.5 = medio  |  1.0 = máximo
/// </summary>
public class CameraShaker : MonoBehaviour
{
    public static CameraShaker Instance { get; private set; }

    [Header("Intensidad máxima")]
    [Tooltip("Desplazamiento máximo en X/Y cuando trauma = 1 (metros).")]
    public float maxOffset = 0.15f;

    [Tooltip("Rotación máxima en Z cuando trauma = 1 (grados).")]
    public float maxRoll = 1.5f;

    [Header("Decaimiento")]
    [Tooltip("Cuánto trauma se pierde por segundo. 2.5 → shake de trauma=1 dura ~0.7 s.")]
    public float traumaDecay = 2.5f;

    [Header("Frecuencia del ruido Perlin")]
    [Tooltip("Velocidad del ruido. Más alto = sacudida más nerviosa.")]
    public float shakeFrequency = 18f;

    // ─── offsets calculados (solo lectura para el compositor) ─────────────────

    /// <summary>Offset de posición (X/Y) que debe sumarse a la posición local base de la cámara.</summary>
    public Vector3 PositionOffset { get; private set; }

    /// <summary>Offset de roll (Z) en grados que debe sumarse al tilt local de la cámara.</summary>
    public float RollOffset { get; private set; }

    // ─── estado interno ───────────────────────────────────────────────────────

    private float _trauma;
    private float _seed;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _seed = Random.value * 100f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_trauma <= 0f)
        {
            PositionOffset = Vector3.zero;
            RollOffset     = 0f;
            return;
        }

        _trauma = Mathf.Max(0f, _trauma - traumaDecay * Time.deltaTime);

        float magnitude = _trauma * _trauma;   // trauma² → caída más natural
        float t         = Time.unscaledTime * shakeFrequency;

        float ox = (Mathf.PerlinNoise(_seed,       t) - 0.5f) * 2f * maxOffset * magnitude;
        float oy = (Mathf.PerlinNoise(_seed + 10f, t) - 0.5f) * 2f * maxOffset * magnitude;
        float oz = (Mathf.PerlinNoise(_seed + 20f, t) - 0.5f) * 2f * maxRoll   * magnitude;

        PositionOffset = new Vector3(ox, oy, 0f);
        RollOffset     = oz;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // API pública
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Añade trauma al shake actual. Los shakes se acumulan (clamp a 1).
    /// </summary>
    public void Shake(float trauma)
    {
        _trauma = Mathf.Clamp01(_trauma + trauma);
    }
}
