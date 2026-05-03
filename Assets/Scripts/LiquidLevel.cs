using UnityEngine;

/// <summary>
/// Conecta EnergyManager con el shader de liquido:
///  - Mueve la _FillAmount entre _bottomMarker.y y _topMarker.y segun SyringePercent.
///  - Cambia el color principal y el color del fondo (top color) segun el modo (rojo/azul).
///  - Suaviza tanto el nivel como los colores con lerps.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class LiquidLevel : MonoBehaviour
{
    [Header("Marcadores del nivel (en world space)")]
    [Tooltip("Transform que marca el Y del fondo de la jeringuilla (liquido = 0%)")]
    [SerializeField] private Transform _bottomMarker;
    [Tooltip("Transform que marca el Y del tope de la jeringuilla (liquido = 100%)")]
    [SerializeField] private Transform _topMarker;

    [Header("Embolo (syringe handle) — opcional")]
    [Tooltip("Transform del embolo que se mueve fisicamente con el liquido. Si null, no se anima.")]
    [SerializeField] private Transform _plunger;
    [Tooltip("Marker para la posicion del embolo cuando el liquido esta a 0% (totalmente abajo)")]
    [SerializeField] private Transform _plungerMarkerAt0;
    [Tooltip("Marker para la posicion del embolo cuando el liquido esta a 100% (totalmente arriba)")]
    [SerializeField] private Transform _plungerMarkerAt100;

    [Header("Colores modo rojo")]
    [SerializeField] private Color _redColor = new Color(0.78f, 0.05f, 0.10f, 1f);
    [SerializeField] private Color _redTopColor = new Color(0.50f, 0.02f, 0.05f, 1f);
    [SerializeField] private Color _redSurfaceColor = new Color(1.00f, 0.55f, 0.55f, 1f);

    [Header("Colores modo azul (bonus)")]
    [SerializeField] private Color _blueColor = new Color(0.15f, 0.55f, 1.00f, 1f);
    [SerializeField] private Color _blueTopColor = new Color(0.05f, 0.30f, 0.70f, 1f);
    [SerializeField] private Color _blueSurfaceColor = new Color(0.55f, 0.85f, 1.00f, 1f);

    [Header("Suavizado")]
    [Tooltip("Suavizado del nivel: mas alto = el liquido sigue mas rapido al cambio")]
    [SerializeField] private float _fillLerpSpeed = 8f;
    [Tooltip("Suavizado del cambio de color rojo<->azul")]
    [SerializeField] private float _colorLerpSpeed = 5f;

    private Renderer _renderer;
    private Material _materialInstance;

    private static readonly int FillAmountID    = Shader.PropertyToID("_FillAmount");
    private static readonly int ColorID         = Shader.PropertyToID("_Color");
    private static readonly int TopColorID      = Shader.PropertyToID("_TopColor");
    private static readonly int SurfaceColorID  = Shader.PropertyToID("_SurfaceColor");

    private float _displayedPercent;
    private Color _currentColor;
    private Color _currentTopColor;
    private Color _currentSurfaceColor;

    private void Start()
    {
        _renderer = GetComponent<Renderer>();
        _materialInstance = _renderer.material;
        _currentColor = _redColor;
        _currentTopColor = _redTopColor;
        _currentSurfaceColor = _redSurfaceColor;

        ApplyMaterial();
    }

    private void Update()
    {
        if (_materialInstance == null || _bottomMarker == null || _topMarker == null) return;

        // Nivel
        float target = EnergyManager.Instance != null
            ? EnergyManager.Instance.SyringePercent
            : 0.5f;

        _displayedPercent = Mathf.Lerp(
            _displayedPercent, target, Time.deltaTime * _fillLerpSpeed);

        // Modo (rojo / azul)
        bool blueMode = EnergyManager.Instance != null && EnergyManager.Instance.IsBlueMode;
        Color targetColor        = blueMode ? _blueColor        : _redColor;
        Color targetTopColor     = blueMode ? _blueTopColor     : _redTopColor;
        Color targetSurfaceColor = blueMode ? _blueSurfaceColor : _redSurfaceColor;

        _currentColor        = Color.Lerp(_currentColor,        targetColor,        Time.deltaTime * _colorLerpSpeed);
        _currentTopColor     = Color.Lerp(_currentTopColor,     targetTopColor,     Time.deltaTime * _colorLerpSpeed);
        _currentSurfaceColor = Color.Lerp(_currentSurfaceColor, targetSurfaceColor, Time.deltaTime * _colorLerpSpeed);

        ApplyMaterial();
    }

    private void ApplyMaterial()
    {
        // World Y donde se traza la linea de llenado
        float fillY = Mathf.Lerp(_bottomMarker.position.y, _topMarker.position.y, _displayedPercent);
        _materialInstance.SetFloat(FillAmountID, fillY);
        _materialInstance.SetColor(ColorID, _currentColor);
        _materialInstance.SetColor(TopColorID, _currentTopColor);
        _materialInstance.SetColor(SurfaceColorID, _currentSurfaceColor);

        // Embolo: sube/baja proporcional al nivel del liquido (lerp entre los dos markers)
        if (_plunger != null && _plungerMarkerAt0 != null && _plungerMarkerAt100 != null)
        {
            _plunger.position = Vector3.Lerp(
                _plungerMarkerAt0.position,
                _plungerMarkerAt100.position,
                _displayedPercent);
        }
    }
}
