using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rellena el sprite de sangre de una jeringuilla del HUD según el EnergyManager.
///   • Red  → nivel de sangre roja (SyringePercent en modo rojo).
///   • Blue → nivel de sangre azul (SyringePercent en modo azul).
/// El juego usa una única jeringuilla que cambia de color según el modo, así que cada
/// jeringuilla del HUD se llena sólo cuando su modo está activo (la otra se vacía).
///
/// SETUP:
///   1. El sprite de sangre (syringe_blood) va en una Image con:
///        Image Type = Filled, Fill Method = Vertical, Fill Origin = Bottom.
///   2. Añade este componente a esa Image. Asigna Blood Type (Red / Blue).
///   3. Tíñela con el color de la Image (rojo para la izquierda, azul para la derecha).
/// </summary>
[AddComponentMenu("HUD/Syringe Fill UI")]
[RequireComponent(typeof(Image))]
public class SyringeFillUI : MonoBehaviour
{
    public enum BloodType { Red, Blue }

    [Tooltip("Qué sangre representa esta jeringuilla.")]
    public BloodType bloodType = BloodType.Red;

    [Tooltip("Image de sangre (Filled). Si se deja vacío se usa la de este objeto.")]
    public Image fillImage;

    [Tooltip("Suavizado del relleno. Alto = responde al instante; bajo = sube/baja despacio.")]
    [Range(1f, 25f)] public float fillLerpSpeed = 8f;

    [Tooltip("Vaciar esta jeringuilla cuando su modo NO está activo (recomendado).")]
    public bool emptyWhenInactive = true;

    private void Reset()  => fillImage = GetComponent<Image>();
    private void Awake()  { if (fillImage == null) fillImage = GetComponent<Image>(); }

    private void Update()
    {
        var em = EnergyManager.Instance;
        if (em == null || fillImage == null) return;

        bool  modeActive = (bloodType == BloodType.Blue) == em.IsBlueMode;
        float target     = modeActive ? em.SyringePercent
                                      : (emptyWhenInactive ? 0f : fillImage.fillAmount);

        float k = 1f - Mathf.Exp(-fillLerpSpeed * Time.deltaTime);
        fillImage.fillAmount = Mathf.Lerp(fillImage.fillAmount, Mathf.Clamp01(target), k);
    }
}
