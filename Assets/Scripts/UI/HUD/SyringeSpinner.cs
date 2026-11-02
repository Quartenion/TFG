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

    private int _index = -1;   // 0 = la roja (izquierda), 1 = la azul (derecha): para SyringeHighlight

    private void Update()
    {
        if (_index < 0) _index = name.Contains("Blue") ? 1 : 0;
        float angle = _crosshair != null ? _crosshair.CurrentRotationAngle * _angleMultiplier : 0f;
        // (Tutorial: mientras se explica esta jeringuilla, además da vueltas sobre su eje.)
        angle += SyringeHighlight.ExtraAngle(_index);
        transform.localRotation = _baseRotation * Quaternion.AngleAxis(angle, _axis.normalized);
    }
}
