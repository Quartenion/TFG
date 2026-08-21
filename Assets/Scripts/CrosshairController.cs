using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem; // Nuevo Input System

public class CrosshairController : MonoBehaviour
{
    [Header("Referencias UI")]
    [Tooltip("RectTransform del corazon central")]
    [SerializeField] private RectTransform _heart;
    [Tooltip("Los 4 pinchos en orden libre (arriba, derecha, abajo, izquierda)")]
    [SerializeField] private RectTransform[] _spikes;

    [Header("Animacion al hacer clic")]
    [Tooltip("Cuanto crece el corazon al hacer clic (1 = sin cambio)")]
    [SerializeField] private float _heartExpandScale = 1.6f;
    [Tooltip("Cuantos pixeles se contraen los pinchos hacia el centro")]
    [SerializeField] private float _spikeContractDistance = 12f;
    [Tooltip("Velocidad con la que vuelven a su posicion / escala original")]
    [SerializeField] private float _returnSpeed = 12f;

    [Header("Rotacion de los pinchos")]
    [Tooltip("TEMPORAL: desactiva la rotacion de los pinchos del crosshair (se quedan en su base). " +
             "Util para comprobar el alineamiento con las notas. Reactivalo cuando cuadre.")]
    [SerializeField] private bool _enableSpikeRotation = false;

    [Tooltip("Velocidad base de rotacion en grados/segundo. Negativo = sentido contrario")]
    [SerializeField] private float _baseSpikeRotationSpeed = 45f;
    [Tooltip("Si esta activado, el pincho se gira sobre si mismo ademas de orbitar el centro")]
    [SerializeField] private bool _rotateSpikeItself = true;
    [Tooltip("Curva: en X energia (0..1), en Y multiplicador de velocidad. Sube los keys finales para acelerar mas en altos %.")]
    [SerializeField] private AnimationCurve _energyToRotationCurve =
        new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 1.6f), new Keyframe(1f, 4f));
    [Tooltip("Multiplicador extra cuando el modo bonus esta activo")]
    [SerializeField] private float _bonusRotationMultiplier = 1.75f;

    [Header("Inercia (fidget spinner)")]
    [Tooltip("Como de rapido la velocidad real se ajusta a la velocidad objetivo. Mas bajo = mas inercia.")]
    [SerializeField] private float _inertiaResponse = 1.5f;
    [Tooltip("Cuantos grados/segundo extra se anaden al hacer clic, como un 'flick' de fidget spinner")]
    [SerializeField] private float _clickRotationImpulse = 200f;
    [Tooltip("Friccion del impulso del clic: cuanto mas alto, antes se diluye el efecto del flick")]
    [SerializeField] private float _impulseFriction = 1.2f;

    [Header("Referencias opcionales")]
    [Tooltip("Para leer el estado de combo/bonus. Si es null, no se aplica el boost de bonus.")]
    [SerializeField] private RhythmInput _rhythmInput;

    [Header("Aspecto")]
    [Tooltip("Color de los pinchos del crosshair, para que combinen con el aro (tenue). Se aplica al iniciar. " +
             "La nota que converge aporta el blanco al ponerse hittable.")]
    [SerializeField] private Color _spikeColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("Input")]
    [Tooltip("Si esta activado, el script escucha el clic directamente. Desactivalo si usas RhythmInput.")]
    [SerializeField] private bool _listenForMouseClick = false;

    private Vector3 _heartBaseScale;
    private Vector2[] _spikeBasePositions;
    private Vector2[] _spikeInwardDirections;
    private Quaternion[] _spikeBaseRotations;

    // Angulo acumulado de rotacion (grados)
    private float _currentRotationAngle;
    // Contraccion actual (0 = posicion base, 1 = totalmente contraido)
    private float _currentContraction;
    // Velocidad angular actual (grados/segundo) — con inercia
    private float _currentAngularVelocity;
    // Impulso adicional del clic, decae solo
    private float _clickImpulse;

    /// <summary>Angulo acumulado de rotacion de los pinchos (grados). Para sincronizar otros objetos.</summary>
    public float CurrentRotationAngle => _currentRotationAngle;
    /// <summary>Velocidad angular actual (grados/segundo).</summary>
    public float CurrentAngularVelocity => _currentAngularVelocity + _clickImpulse;

    // Action del nuevo Input System para el clic izquierdo del raton
    private InputAction _clickAction;

    private void Start()
    {
        if (_heart != null)
            _heartBaseScale = _heart.localScale;

        if (_spikes != null && _spikes.Length > 0)
        {
            _spikeBasePositions = new Vector2[_spikes.Length];
            _spikeInwardDirections = new Vector2[_spikes.Length];
            _spikeBaseRotations = new Quaternion[_spikes.Length];

            for (int i = 0; i < _spikes.Length; i++)
            {
                _spikeBasePositions[i] = _spikes[i].anchoredPosition;
                _spikeBaseRotations[i] = _spikes[i].localRotation;

                Vector2 toCenter = -_spikeBasePositions[i];
                _spikeInwardDirections[i] = toCenter.sqrMagnitude > 0.0001f
                    ? toCenter.normalized
                    : Vector2.zero;

                // Tinte tenue para que los pinchos combinen con el aro (no blancos).
                Image img = _spikes[i].GetComponent<Image>();
                if (img != null) img.color = _spikeColor;
            }
        }

        if (_listenForMouseClick)
        {
            _clickAction = new InputAction("Click", binding: "<Mouse>/leftButton");
            _clickAction.Enable();
        }
    }

    private void OnDestroy()
    {
        if (_clickAction != null)
            _clickAction.Disable();
    }

    private void Update()
    {
        if (_listenForMouseClick && _clickAction != null && _clickAction.WasPressedThisFrame())
        {
            TriggerClick();
        }

        // Velocidad objetivo basada en energia (EnergyPercent ya es consciente del modo)
        float targetSpeed = _baseSpikeRotationSpeed;
        bool bonusActive = _rhythmInput != null && _rhythmInput.IsBonusActive;
        if (EnergyManager.Instance != null)
        {
            float pct = EnergyManager.Instance.EnergyPercent;
            targetSpeed *= _energyToRotationCurve.Evaluate(pct);
        }
        if (bonusActive) targetSpeed *= _bonusRotationMultiplier;

        // Inercia: la velocidad real se acerca a la objetivo de forma suave
        _currentAngularVelocity = Mathf.Lerp(
            _currentAngularVelocity,
            targetSpeed,
            Time.deltaTime * _inertiaResponse);

        // El impulso del clic decae con el tiempo (friccion)
        _clickImpulse = Mathf.Lerp(_clickImpulse, 0f, Time.deltaTime * _impulseFriction);

        // La velocidad final = velocidad inercial + impulso del clic
        float finalSpeed = _currentAngularVelocity + _clickImpulse;

        if (_enableSpikeRotation)
        {
            _currentRotationAngle += finalSpeed * Time.deltaTime;
            if (_currentRotationAngle > 360f) _currentRotationAngle -= 360f;
            else if (_currentRotationAngle < -360f) _currentRotationAngle += 360f;
        }
        else
        {
            _currentRotationAngle = 0f;   // congelado en la base
        }

        if (_heart != null)
        {
            _heart.localScale = Vector3.Lerp(
                _heart.localScale,
                _heartBaseScale,
                Time.deltaTime * _returnSpeed);
        }

        _currentContraction = Mathf.Lerp(_currentContraction, 0f, Time.deltaTime * _returnSpeed);

        if (_spikes != null && _spikeBasePositions != null)
        {
            float rad = _currentRotationAngle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            for (int i = 0; i < _spikes.Length; i++)
            {
                Vector2 logicalPos = _spikeBasePositions[i]
                    + _spikeInwardDirections[i] * (_currentContraction * _spikeContractDistance);

                Vector2 rotated = new Vector2(
                    logicalPos.x * cos - logicalPos.y * sin,
                    logicalPos.x * sin + logicalPos.y * cos
                );
                _spikes[i].anchoredPosition = rotated;

                if (_rotateSpikeItself)
                {
                    _spikes[i].localRotation = _spikeBaseRotations[i]
                        * Quaternion.Euler(0f, 0f, _currentRotationAngle);
                }
            }
        }
    }

    /// <summary>
    /// Dispara la animacion del crosshair: corazon se expande, pinchos se contraen,
    /// y se anade un impulso de rotacion como un flick de fidget spinner.
    /// </summary>
    public void TriggerClick()
    {
        if (_heart != null)
            _heart.localScale = _heartBaseScale * _heartExpandScale;

        _currentContraction = 1f;

        // El impulso suma sobre la velocidad actual: flick que decae solo
        _clickImpulse += _clickRotationImpulse;
    }
}
