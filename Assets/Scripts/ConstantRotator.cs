using UnityEngine;

/// <summary>
/// Rota este GameObject constantemente alrededor de uno o más ejes.
/// </summary>
[AddComponentMenu("Level/Constant Rotator")]
public class ConstantRotator : MonoBehaviour
{
    [Tooltip("Grados por segundo en cada eje (espacio local).")]
    [SerializeField] private Vector3 _degreesPerSecond = new Vector3(0f, 90f, 0f);

    [Tooltip("Si está activo, usa espacio mundial en lugar de local.")]
    [SerializeField] private bool _worldSpace = false;

    private void Update()
    {
        transform.Rotate(
            _degreesPerSecond * Time.deltaTime,
            _worldSpace ? Space.World : Space.Self
        );
    }
}
