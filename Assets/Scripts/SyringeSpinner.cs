using UnityEngine;

/// <summary>
/// Hace girar la jeringuilla 3D al mismo ritmo que los pinchos del crosshair.
/// Lee el angulo acumulado de CrosshairController y lo aplica como rotacion
/// sobre el eje configurado (por defecto Y, vertical), respetando la rotacion base.
/// </summary>
public class SyringeSpinner : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Crosshair del que copia el angulo de rotacion")]
    [SerializeField] private CrosshairController _crosshair;

    [Header("Eje y direccion")]
    [Tooltip("Eje sobre el que rota la jeringuilla (en local space)")]
    [SerializeField] private Vector3 _axis = Vector3.up;
    [Tooltip("Multiplicador del angulo. Negativo invierte el sentido. 1 = exactamente igual al crosshair.")]
    [SerializeField] private float _angleMultiplier = 1f;

    private Quaternion _baseRotation;

    private void Start()
    {
        _baseRotation = transform.localRotation;
    }

    private void Update()
    {
        if (_crosshair == null) return;

        float angle = _crosshair.CurrentRotationAngle * _angleMultiplier;
        transform.localRotation = _baseRotation * Quaternion.AngleAxis(angle, _axis.normalized);
    }
}
