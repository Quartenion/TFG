using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Muestra un ARO DE CARGA en el HUD con el progreso del <see cref="GazeSwitch"/> que el jugador
/// está mirando. Un solo aro vale para TODOS los interruptores de mirada de la escena:
///   • muestra el que más cargado esté (el que estás mirando),
///   • se vacía si dejas de mirar (mientras el switch se descarga),
///   • se oculta al completarse (switch activado) o cuando no estás cargando ninguno.
///
/// SETUP:
///   1. En el Canvas del HUD crea una Image centrada (donde el crosshair): Image Type = Filled,
///      Fill Method = Radial 360, con un sprite de aro. Ponle Fill Amount = 0.
///   2. Añade este componente (al Canvas o a un objeto del HUD) y arrastra esa Image a "Ring".
///   3. Los GazeSwitch se localizan solos.
/// </summary>
[AddComponentMenu("HUD/Gaze Charge UI")]
public class GazeChargeUI : MonoBehaviour
{
    [Header("Aro")]
    [Tooltip("Image del HUD con Image Type = Filled, Fill Method = Radial 360.")]
    [SerializeField] private Image _ring;

    [Tooltip("Por debajo de este progreso el aro se oculta (para no ver el aro vacío).")]
    [SerializeField] private float _showThreshold = 0.01f;

    [Header("Color (opcional)")]
    [SerializeField] private bool  _colorLerp = true;
    [SerializeField] private Color _colorStart = new Color(0.2f, 0.9f, 1f, 1f);
    [SerializeField] private Color _colorFull  = new Color(0.3f, 1f, 0.45f, 1f);

    [Header("Suavizado (opcional)")]
    [Tooltip("Velocidad con la que el aro sigue al progreso real. 0 = instantáneo.")]
    [SerializeField] private float _followSpeed = 0f;

    private GazeSwitch[] _switches;
    private CanvasGroup  _group;
    private float        _shown;   // valor mostrado (para suavizado)

    private void Start()
    {
        _switches = FindObjectsByType<GazeSwitch>();

        if (_ring != null)
        {
            _group = _ring.GetComponent<CanvasGroup>();
            if (_group == null) _group = _ring.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _ring.fillAmount = 0f;
        }
    }

    private void LateUpdate()
    {
        if (_ring == null) return;

        // Progreso objetivo = el del switch más cargado que aún NO se ha activado.
        float target = 0f;
        if (_switches != null)
        {
            foreach (GazeSwitch s in _switches)
            {
                if (s == null || s.IsActivated) continue;
                if (s.Progress > target) target = s.Progress;
            }
        }

        // Seguir el objetivo (instantáneo o suavizado).
        if (_followSpeed > 0f)
            _shown = Mathf.MoveTowards(_shown, target, _followSpeed * Time.deltaTime);
        else
            _shown = target;

        _ring.fillAmount = _shown;
        if (_colorLerp) _ring.color = Color.Lerp(_colorStart, _colorFull, _shown);
        if (_group != null) _group.alpha = _shown > _showThreshold ? 1f : 0f;
    }

    /// <summary>Re-escanea los GazeSwitch (útil si instancias interruptores en runtime).</summary>
    public void RefreshSwitches()
    {
        _switches = FindObjectsByType<GazeSwitch>();
    }
}
