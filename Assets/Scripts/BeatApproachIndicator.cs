using UnityEngine;

/// <summary>
/// Componente para los prefabs de obstáculos.
/// Pulsa visualmente al ritmo de la música conforme el jugador se aproxima,
/// indicando cuándo actuar. El ritmo del pulso se sincroniza con BeatManager.BeatProgress,
/// por lo que es exacto independientemente del BPM.
///
/// Colores:
///   > 2 beats lejos  → colorSafe    (blanco/neutro)
///   1-2 beats lejos  → colorWarning (amarillo)
///   < 1 beat lejos   → colorDanger  (naranja/rojo)
///
/// SETUP:
///   1. Añade este componente al prefab del obstáculo.
///   2. El BeatObstacleSpawner llama a Init() automáticamente al instanciar.
///   3. Ajusta los colores y la intensidad del pulso en el Inspector del prefab.
///   4. Para que la emisión funcione, el material del obstáculo debe tener
///      la keyword EMISSION activada (Global Illumination → Realtime o None).
/// </summary>
public class BeatApproachIndicator : MonoBehaviour
{
    [Header("Colores de proximidad")]
    public Color colorSafe    = Color.white;
    public Color colorWarning = new Color(1f, 0.85f, 0f);   // amarillo
    public Color colorDanger  = new Color(1f, 0.25f, 0f);   // naranja

    [Header("Pulso")]
    [Tooltip("A partir de cuántos beats de distancia empieza el efecto de pulso.")]
    public float pulseStartBeats = 3f;

    [Tooltip("Intensidad máxima del brillo de emisión en el pulso (0 = sin brillo).")]
    [Range(0f, 3f)]
    public float pulseEmission = 1f;

    [Tooltip("Multiplicador de escala en el pico del pulso (0 = sin escala, 0.15 = +15%).")]
    [Range(0f, 0.5f)]
    public float pulseScale = 0.1f;

    // ── Internos ──────────────────────────────────────────────────────────────

    private BeatManager    _beatManager;
    private PlayerMovement _playerMovement;
    private Renderer[]     _renderers;
    private Vector3        _baseScale;
    private MaterialPropertyBlock _mpb;

    private static readonly int ColorProp    = Shader.PropertyToID("_Color");
    private static readonly int EmissionProp = Shader.PropertyToID("_EmissionColor");

    // =========================================================================
    // INICIALIZACIÓN (llamada por BeatObstacleSpawner)
    // =========================================================================

    /// <summary>
    /// Inicializa el indicador. Llamado automáticamente por BeatObstacleSpawner
    /// al instanciar el obstáculo.
    /// </summary>
    public void Init(BeatManager bm, PlayerMovement pm)
    {
        _beatManager    = bm;
        _playerMovement = pm;
        _renderers      = GetComponentsInChildren<Renderer>(includeInactive: false);
        _baseScale      = transform.localScale;
        _mpb            = new MaterialPropertyBlock();
    }

    // =========================================================================
    // UPDATE
    // =========================================================================

    private void Update()
    {
        if (_beatManager == null || _playerMovement == null) return;

        // CellSize recalculado cada frame → se adapta a cambios de BPM en tiempo real
        float cellSize = _playerMovement.walkSpeed * _beatManager.BeatDuration;
        if (cellSize < 0.01f) return;

        // Distancia horizontal al jugador
        Vector3 myXZ     = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 playerXZ = new Vector3(_playerMovement.transform.position.x,
                                       0f,
                                       _playerMovement.transform.position.z);
        float distance  = Vector3.Distance(myXZ, playerXZ);
        float beatsAway = distance / cellSize;

        // ── Color de proximidad ───────────────────────────────────────────────
        Color baseColor;
        if (beatsAway <= 1f)
            baseColor = Color.Lerp(colorDanger, colorWarning, beatsAway);
        else if (beatsAway <= 2f)
            baseColor = Color.Lerp(colorWarning, colorSafe, beatsAway - 1f);
        else
            baseColor = colorSafe;

        // ── Pulso alineado con el beat ────────────────────────────────────────
        // BeatProgress va de 0 (inicio de beat) a 1 (final de beat).
        // Creamos un pico agudo al inicio de cada beat (cuando BeatProgress ≈ 0).
        float pulse = 0f;
        if (beatsAway < pulseStartBeats)
        {
            float beatPhase  = 1f - _beatManager.BeatProgress; // 1 al inicio, 0 al final
            float rawPulse   = Mathf.Pow(beatPhase, 8f);       // curva muy pronunciada
            float proximity  = 1f - Mathf.Clamp01(beatsAway / pulseStartBeats);
            pulse = rawPulse * (0.3f + proximity * 0.7f);      // más intenso cuanto más cerca
        }

        // ── Aplicar a renderers vía MaterialPropertyBlock (sin modificar el material) ──
        Color finalBase     = baseColor;
        Color finalEmission = baseColor * (pulse * pulseEmission);

        foreach (var r in _renderers)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorProp,    finalBase);
            _mpb.SetColor(EmissionProp, finalEmission);
            r.SetPropertyBlock(_mpb);
        }

        // ── Escala pulsante ────────────────────────────────────────────────────
        transform.localScale = _baseScale * (1f + pulse * pulseScale);
    }

    private void OnDisable()
    {
        // Restaurar escala y material al desactivar
        if (_baseScale != Vector3.zero)
            transform.localScale = _baseScale;

        if (_renderers == null || _mpb == null) return;
        foreach (var r in _renderers)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorProp,    Color.white);
            _mpb.SetColor(EmissionProp, Color.black);
            r.SetPropertyBlock(_mpb);
        }
    }
}
