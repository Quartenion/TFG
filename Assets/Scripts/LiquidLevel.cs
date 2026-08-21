using UnityEngine;

/// <summary>
/// Versi�n simplificada del controlador del l�quido de la jeringuilla:
///  - Escala el mesh del l�quido en Y entre 0 y _maxYScale seg�n SyringePercent.
///  - Mueve el �mbolo entre dos markers en world space.
///  - Cambia entre dos materials (rojo / azul) seg�n IsBlueMode.
///
/// Importante: el pivote del mesh del l�quido tiene que estar en su BASE para que
/// crezca hacia arriba al rellenarse. Si tu mesh tiene el pivote en el centro
/// (caso normal de los primitivos de Unity), envu�lvelo en un Empty con el
/// transform en la base y mete el mesh dentro como hijo.
/// </summary>
public class LiquidLevel : MonoBehaviour
{
    public enum BloodMode
    {
        Auto,      // una jeringuilla que cambia de color según el modo (comportamiento original)
        RedOnly,   // jeringuilla FIJA roja: muestra el nivel sólo en modo rojo (vacía en azul)
        BlueOnly   // jeringuilla FIJA azul: muestra el nivel sólo en modo azul (vacía en rojo)
    }

    [Header("Tipo de sangre de ESTA jeringuilla")]
    [Tooltip("Auto = cambia de color con el modo. RedOnly/BlueOnly = fija; sólo se llena en su modo.\n" +
             "Para el HUD de dos jeringuillas: la izquierda RedOnly, la derecha BlueOnly.")]
    [SerializeField] private BloodMode _bloodMode = BloodMode.Auto;

    [Header("Mesh del l�quido")]
    [Tooltip("Transform del mesh que representa el l�quido. Su pivote debe estar en la BASE.")]
    [SerializeField] private Transform _liquidMesh;
    [Tooltip("Escala Y cuando el l�quido est� al 100% (normalmente 1)")]
    [SerializeField] private float _maxYScale = 1f;

    [Header("Materials por modo (red / blue)")]
    [Tooltip("Renderer del l�quido al que se le cambia el material seg�n el modo")]
    [SerializeField] private Renderer _liquidRenderer;
    [SerializeField] private Material _redMaterial;
    [SerializeField] private Material _blueMaterial;

    [Header("�mbolo (syringe handle)")]
    [Tooltip("Transform del �mbolo que se mueve f�sicamente con el l�quido. Si es null, no se anima.")]
    [SerializeField] private Transform _plunger;
    [Tooltip("Marker para la posici�n del �mbolo cuando el l�quido est� a 0%")]
    [SerializeField] private Transform _plungerMarkerAt0;
    [Tooltip("Marker para la posici�n del �mbolo cuando el l�quido est� a 100%")]
    [SerializeField] private Transform _plungerMarkerAt100;

    [Header("Suavizado")]
    [Tooltip("Velocidad de seguimiento del nivel: m�s alto = el l�quido sigue m�s r�pido")]
    [SerializeField] private float _fillLerpSpeed = 8f;

    private float _displayedPercent;
    private bool _isCurrentlyBlue;

    private void Start()
    {
        if (EnergyManager.Instance != null)
            _displayedPercent = EnergyManager.Instance.SyringePercent;

        // Aplica el material correcto al inicio segun el tipo de sangre.
        if (_liquidRenderer != null && _redMaterial != null)
        {
            bool startBlue = _bloodMode == BloodMode.BlueOnly
                          || (_bloodMode == BloodMode.Auto
                              && EnergyManager.Instance != null && EnergyManager.Instance.IsBlueMode);
            _liquidRenderer.material = startBlue && _blueMaterial != null
                ? _blueMaterial
                : _redMaterial;
            _isCurrentlyBlue = startBlue;
        }
    }

    private void Update()
    {
        if (EnergyManager.Instance == null) return;

        bool blueMode = EnergyManager.Instance.IsBlueMode;

        // Nivel objetivo según el tipo de sangre de esta jeringuilla:
        //  • Auto: siempre el nivel actual (cambia de color con el modo).
        //  • RedOnly: sólo se llena en modo rojo (vacía en azul).
        //  • BlueOnly: sólo se llena en modo azul (vacía en rojo).
        float target;
        switch (_bloodMode)
        {
            case BloodMode.RedOnly:  target = blueMode ? 0f : EnergyManager.Instance.SyringePercent; break;
            case BloodMode.BlueOnly: target = blueMode ? EnergyManager.Instance.SyringePercent : 0f; break;
            default:                 target = EnergyManager.Instance.SyringePercent;                  break;
        }
        _displayedPercent = Mathf.Lerp(_displayedPercent, target, Time.deltaTime * _fillLerpSpeed);

        // 1. Escala vertical del mesh del l�quido
        if (_liquidMesh != null)
        {
            Vector3 scale = _liquidMesh.localScale;
            scale.y = Mathf.Max(0f, _displayedPercent * _maxYScale);
            _liquidMesh.localScale = scale;
        }

        // 2. Posici�n del �mbolo entre los dos markers
        if (_plunger != null && _plungerMarkerAt0 != null && _plungerMarkerAt100 != null)
        {
            _plunger.position = Vector3.Lerp(
                _plungerMarkerAt0.position,
                _plungerMarkerAt100.position,
                _displayedPercent);
        }

        // 3. Material: fijo en RedOnly/BlueOnly; en Auto cambia con el modo.
        if (_liquidRenderer != null)
        {
            bool wantBlue = _bloodMode == BloodMode.BlueOnly
                         || (_bloodMode == BloodMode.Auto && blueMode);
            if (wantBlue != _isCurrentlyBlue)
            {
                _isCurrentlyBlue = wantBlue;
                Material targetMat = wantBlue ? _blueMaterial : _redMaterial;
                if (targetMat != null) _liquidRenderer.material = targetMat;
            }
        }
    }
}
