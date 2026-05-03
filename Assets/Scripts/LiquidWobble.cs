using UnityEngine;

/// <summary>
/// Hace que el liquido del shader se "balancee" cuando el objeto se mueve o gira.
/// Basado en la tecnica clasica de MinionsArt: rastrea velocidad lineal/angular y
/// alimenta una sinusoide amortiguada que va a las propiedades _WobbleX / _WobbleZ.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class LiquidWobble : MonoBehaviour
{
    [Header("Intensidad")]
    [Tooltip("Wobble maximo que se anade por velocidad/rotacion")]
    [SerializeField] private float _maxWobble = 0.03f;
    [Tooltip("Velocidad de la sinusoide del wobble (ciclos por segundo aprox.)")]
    [SerializeField] private float _wobbleSpeed = 1f;
    [Tooltip("Cuanto de rapido vuelve a calmarse (1 = lento, 5 = rapido)")]
    [SerializeField] private float _recovery = 1f;

    [Header("Influencia")]
    [Tooltip("Cuanto influye la velocidad lineal del objeto")]
    [SerializeField] private float _linearInfluence = 1f;
    [Tooltip("Cuanto influye la velocidad angular del objeto")]
    [SerializeField] private float _angularInfluence = 0.2f;

    private Renderer _renderer;
    private Material _materialInstance;

    private static readonly int WobbleXID = Shader.PropertyToID("_WobbleX");
    private static readonly int WobbleZID = Shader.PropertyToID("_WobbleZ");

    private Vector3 _lastPos;
    private Quaternion _lastRot;

    private float _wobbleAmountX;
    private float _wobbleAmountZ;
    private float _wobbleAmountToAddX;
    private float _wobbleAmountToAddZ;
    private float _time;

    private void Start()
    {
        _renderer = GetComponent<Renderer>();
        // .material crea una instancia por GameObject (no comparte con otros)
        _materialInstance = _renderer.material;
        _lastPos = transform.position;
        _lastRot = transform.rotation;
    }

    private void Update()
    {
        _time += Time.deltaTime;

        // Recuperacion: el wobble que queda por aplicar tiende a 0
        _wobbleAmountToAddX = Mathf.Lerp(_wobbleAmountToAddX, 0f, Time.deltaTime * _recovery);
        _wobbleAmountToAddZ = Mathf.Lerp(_wobbleAmountToAddZ, 0f, Time.deltaTime * _recovery);

        // Sinusoide amortiguada para el efecto chapoteo
        float pulse = 2f * Mathf.PI * _wobbleSpeed;
        _wobbleAmountX = _wobbleAmountToAddX * Mathf.Sin(pulse * _time);
        _wobbleAmountZ = _wobbleAmountToAddZ * Mathf.Sin(pulse * _time);

        if (_materialInstance != null)
        {
            _materialInstance.SetFloat(WobbleXID, _wobbleAmountX);
            _materialInstance.SetFloat(WobbleZID, _wobbleAmountZ);
        }

        // Calculo de velocidad y rotacion respecto al frame anterior
        if (Time.deltaTime > 0f)
        {
            Vector3 velocity = (_lastPos - transform.position) / Time.deltaTime;
            Vector3 angularVelocity = (transform.rotation.eulerAngles - _lastRot.eulerAngles);

            _wobbleAmountToAddX += Mathf.Clamp(
                (velocity.x * _linearInfluence + angularVelocity.z * _angularInfluence) * _maxWobble,
                -_maxWobble, _maxWobble);

            _wobbleAmountToAddZ += Mathf.Clamp(
                (velocity.z * _linearInfluence + angularVelocity.x * _angularInfluence) * _maxWobble,
                -_maxWobble, _maxWobble);
        }

        _lastPos = transform.position;
        _lastRot = transform.rotation;
    }
}
