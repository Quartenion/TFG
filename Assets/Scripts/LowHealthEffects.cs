using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Efectos de pantalla en dos casos opuestos:
///  - LOW HEALTH (rojo): cuando la energia baja por debajo de _redActivationThreshold.
///    Vignette rojo, musica ahogada, latido, pulso de camara.
///  - HIGH BONUS (azul): cuando estas en modo azul y la sangre azul supera
///    _blueActivationThreshold. Vignette azul + pulso de camara (sin ahogar musica).
/// </summary>
public class LowHealthEffects : MonoBehaviour
{
    [Header("Threshold rojo (low health)")]
    [Tooltip("Por debajo de este porcentaje de energia, los efectos rojos empiezan")]
    [Range(0f, 1f)]
    [SerializeField] private float _redActivationThreshold = 0.4f;

    [Header("Threshold azul (high bonus)")]
    [Tooltip("Por encima de este porcentaje de sangre azul (en bonus), los efectos azules empiezan")]
    [Range(0f, 1f)]
    [SerializeField] private float _blueActivationThreshold = 0.6f;

    [Header("Vignette (URP Volume)")]
    [SerializeField] private Volume _volume;
    [SerializeField] private Color _redColor = new Color(0.65f, 0.0f, 0.0f, 1f);
    [SerializeField] private Color _blueColor = new Color(0.0f, 0.55f, 1.0f, 1f);
    [Range(0f, 1f)]
    [SerializeField] private float _maxVignetteIntensity = 0.55f;
    [Range(0f, 1f)]
    [SerializeField] private float _vignetteSmoothness = 0.4f;

    [Header("Filtro low-pass de la musica (solo en rojo)")]
    [SerializeField] private AudioLowPassFilter _musicLowPass;
    [SerializeField] private float _normalCutoff = 22000f;
    [SerializeField] private float _lowHealthCutoff = 900f;
    [Range(0f, 1f)]
    [SerializeField] private float _musicDuckAmount = 0.4f;
    [SerializeField] private AudioSource _musicSource;

    [Header("Pulso de camara (latido)")]
    [SerializeField] private Camera _camera;
    [SerializeField] private float _maxPulseFOV = 5f;
    [SerializeField] private float _pulseInTime = 0.06f;
    [SerializeField] private float _pulseOutTime = 0.22f;

    [Header("Audio del corazon")]
    [SerializeField] private AudioSource _heartbeatSource;
    [SerializeField] private AudioClip _heartbeatClip;
    [Tooltip("Clip distinto para los pulsos en bonus azul (opcional). Si null, usa el de rojo.")]
    [SerializeField] private AudioClip _bonusPulseClip;
    [Tooltip("Volumen maximo del latido. Por encima de 1 puede clipear, ajusta segun el clip.")]
    [Range(0f, 5f)]
    [SerializeField] private float _heartbeatMaxVolume = 2.5f;

    [Header("Suavizado")]
    [SerializeField] private float _lerpSpeed = 4f;

    private Vignette _vignette;
    private float _baseFOV;
    private float _baseMusicVolume;
    private float _currentStrength;   // 0..1, intensidad del efecto activo
    private bool  _currentIsBlue;     // que color esta activo
    private Color _currentColor;
    private Coroutine _pulseRoutine;

    private void Start()
    {
        if (_camera != null) _baseFOV = _camera.fieldOfView;
        if (_musicSource != null) _baseMusicVolume = _musicSource.volume;

        if (_volume != null && _volume.profile != null)
        {
            _volume.profile.TryGet(out _vignette);
            if (_vignette != null)
            {
                _vignette.intensity.overrideState = true;
                _vignette.color.overrideState = true;
                _vignette.smoothness.overrideState = true;
                _vignette.smoothness.value = _vignetteSmoothness;
            }
        }

        _currentColor = _redColor;
    }

    private void Update()
    {
        var em = EnergyManager.Instance;

        // Calculamos los dos targets posibles
        float redTarget = 0f;
        float blueTarget = 0f;

        if (em != null)
        {
            if (em.IsBlueMode)
            {
                // Solo el azul puede activarse en modo bonus
                float syringe = em.SyringePercent;
                if (syringe > _blueActivationThreshold && _blueActivationThreshold < 1f)
                {
                    blueTarget = (syringe - _blueActivationThreshold) / (1f - _blueActivationThreshold);
                }
            }
            else
            {
                // Solo el rojo puede activarse en modo normal
                float energy = em.EnergyPercent;
                if (energy < _redActivationThreshold && _redActivationThreshold > 0f)
                {
                    redTarget = 1f - (energy / _redActivationThreshold);
                }
            }
        }

        // Elegimos cual de los dos efectos (no se solapan, son exclusivos)
        bool useBlue = blueTarget > redTarget;
        float targetStrength = Mathf.Clamp01(useBlue ? blueTarget : redTarget);

        _currentIsBlue = useBlue;
        _currentStrength = Mathf.Lerp(_currentStrength, targetStrength,
                                       Time.deltaTime * _lerpSpeed);

        // Color objetivo (lerp tambien para que la transicion azul/rojo sea suave)
        Color targetColor = useBlue ? _blueColor : _redColor;
        _currentColor = Color.Lerp(_currentColor, targetColor, Time.deltaTime * _lerpSpeed);

        ApplyEffects();
    }

    private void ApplyEffects()
    {
        // Vignette
        if (_vignette != null)
        {
            _vignette.intensity.value = _currentStrength * _maxVignetteIntensity;
            _vignette.color.value = _currentColor;
            _vignette.smoothness.value = _vignetteSmoothness;
        }

        // Low pass y music ducking solo aplican en rojo (peligro real).
        // En azul la musica suena limpia.
        float audioStrength = _currentIsBlue ? 0f : _currentStrength;

        if (_musicLowPass != null)
        {
            _musicLowPass.cutoffFrequency = Mathf.Lerp(_normalCutoff, _lowHealthCutoff, audioStrength);
        }

        if (_musicSource != null && _musicDuckAmount > 0f)
        {
            float duck = 1f - (audioStrength * _musicDuckAmount);
            _musicSource.volume = _baseMusicVolume * duck;
        }
    }

    /// <summary>Llamar desde el BeatManager cuando hay beat. Pulsa la camara y suena latido.</summary>
    public void OnBeat()
    {
        if (_currentStrength < 0.05f) return;

        if (_pulseRoutine != null) StopCoroutine(_pulseRoutine);
        _pulseRoutine = StartCoroutine(PulseRoutine());

        if (_heartbeatSource != null)
        {
            AudioClip clipToPlay = (_currentIsBlue && _bonusPulseClip != null)
                ? _bonusPulseClip
                : _heartbeatClip;

            if (clipToPlay != null)
            {
                _heartbeatSource.PlayOneShot(clipToPlay, _currentStrength * _heartbeatMaxVolume);
            }
        }
    }

    private IEnumerator PulseRoutine()
    {
        if (_camera == null) yield break;

        float pulseDelta = _currentStrength * _maxPulseFOV;
        float startFOV = _camera.fieldOfView;
        float targetFOV = _baseFOV - pulseDelta;

        float t = 0f;
        while (t < _pulseInTime)
        {
            t += Time.deltaTime;
            _camera.fieldOfView = Mathf.Lerp(startFOV, targetFOV, t / _pulseInTime);
            yield return null;
        }

        t = 0f;
        while (t < _pulseOutTime)
        {
            t += Time.deltaTime;
            _camera.fieldOfView = Mathf.Lerp(targetFOV, _baseFOV, t / _pulseOutTime);
            yield return null;
        }

        _camera.fieldOfView = _baseFOV;
        _pulseRoutine = null;
    }
}
