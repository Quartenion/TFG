using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Mecanica jeringuilla:
///  - Modo rojo (normal):
///      Hit  -> inyecta una cantidad fija (la barra baja un poco).
///              Si no hay sangre roja suficiente, el sobrante (overflow) se convierte
///              automaticamente en sangre azul y entramos en modo bonus.
///      Miss -> absorbe sangre extra (la barra sube; mala noticia).
///      Pasivo -> la barra sube sola lentamente.
///  - Modo azul (bonus):
///      Hit  -> ANADE sangre azul (barra sube). Riesgo: si la jeringuilla se llena, mueres.
///      Miss -> inyecta TODA la sangre azul de golpe -> barra a 0 + boost.
///      Si la sangre azul se agota (decay/acciones) -> sale del bonus sin boost
///      y la jeringuilla empieza a llenarse otra vez de rojo.
/// </summary>
public class RhythmInput : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private BeatManager _beatManager;
    [SerializeField] private CrosshairController _crosshair;

    [Header("Deteccion de hits")]
    [SerializeField] private float _hitWindow = 0.15f;

    [Header("Combo (solo para mostrar / metricas)")]
    [Tooltip("El bonus ya no depende del combo. Esto solo se usa para UI/logs.")]
    [SerializeField] private bool _trackCombo = true;

    [Header("Eventos")]
    public UnityEvent OnBeatHit;
    public UnityEvent OnBeatMiss;
    public UnityEvent<int> OnComboChanged;
    public UnityEvent OnBonusActivated;
    public UnityEvent OnBonusDeactivated;
    public UnityEvent<float> OnBlueInjection;  // boost dado al fallar en bonus

    public int CurrentCombo { get; private set; }
    public bool IsBonusActive { get; private set; }

    private InputAction _clickAction;

    private void OnEnable()
    {
        _clickAction = new InputAction("RhythmClick", binding: "<Mouse>/leftButton");
        _clickAction.performed += HandleClick;
        _clickAction.Enable();
    }

    private void OnDisable()
    {
        if (_clickAction != null)
        {
            _clickAction.performed -= HandleClick;
            _clickAction.Disable();
        }
    }

    private void Update()
    {
        // Sincronizamos con EnergyManager en ambos sentidos:
        //  - Si entro en azul (por overflow de un hit) y aun no lo sabemos -> activar bonus.
        //  - Si salio de azul (por decay/agotarse) y aun lo teniamos como activo -> desactivar.
        if (EnergyManager.Instance == null) return;

        bool managerBonus = EnergyManager.Instance.IsBlueMode;
        if (managerBonus && !IsBonusActive)
        {
            IsBonusActive = true;
            OnBonusActivated?.Invoke();
        }
        else if (!managerBonus && IsBonusActive)
        {
            IsBonusActive = false;
            OnBonusDeactivated?.Invoke();
        }
    }

    private void HandleClick(InputAction.CallbackContext ctx)
    {
        if (_crosshair != null) _crosshair.TriggerClick();

        bool hit = false;
        if (_beatManager != null)
        {
            float distance = _beatManager.GetSecondsToNearestBeat();
            hit = distance <= _hitWindow;
        }

        if (hit) RegisterHit();
        else RegisterMiss();
    }

    private void RegisterHit()
    {
        var em = EnergyManager.Instance;
        if (_trackCombo)
        {
            CurrentCombo++;
            OnComboChanged?.Invoke(CurrentCombo);
        }

        if (em != null && em.IsBlueMode)
        {
            // Ya en modo azul: cada acierto acumula sangre azul
            em.HitBlue();
        }
        else if (em != null)
        {
            // Modo rojo: inyecta sangre. Si la inyeccion se pasa de lo que hay,
            // EnergyManager activara solo el modo azul con el overflow.
            em.HitRed();
        }

        OnBeatHit?.Invoke();
    }

    private void RegisterMiss()
    {
        var em = EnergyManager.Instance;
        bool wasBonus = IsBonusActive;

        if (wasBonus)
        {
            // Fallo en azul: inyeccion masiva con boost
            float boost = em != null ? em.MissBlueAndInject() : 0f;
            if (boost > 0f) OnBlueInjection?.Invoke(boost);
            em?.ExitBlueMode();
            IsBonusActive = false;
            OnBonusDeactivated?.Invoke();
        }
        else
        {
            // Fallo en rojo: penalizacion (sube la barra)
            em?.MissRed();
        }

        if (_trackCombo)
        {
            CurrentCombo = 0;
            OnComboChanged?.Invoke(0);
        }
        OnBeatMiss?.Invoke();
    }
}
