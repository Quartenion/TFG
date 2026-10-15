using UnityEngine;

/// <summary>
/// LUZ DEL CORAZÓN: una luz roja pequeña delante de la cámara (donde el jugador sujeta el corazón) que LATE AL BEAT
/// y da un golpe más fuerte en cada latido acertado. En niveles oscuros (la cueva) ilumina lo que tienes justo
/// delante y une la estética con la mecánica rítmica. Va en el jugador (lo añade Tools/Estética/…).
/// </summary>
[AddComponentMenu("Player/Heart Light")]
public class HeartLight : MonoBehaviour
{
    [Tooltip("Color de la luz.")]
    public Color color = new Color(1f, 0.12f, 0.08f);
    [Tooltip("Posición respecto a la cámara (donde está el corazón en la mano).")]
    public Vector3 localOffset = new Vector3(0.25f, -0.35f, 0.6f);
    [Tooltip("Intensidad en reposo (entre latidos).")]
    public float baseIntensity = 0.6f;
    [Tooltip("Intensidad extra en cada beat de la música.")]
    public float beatPulse = 1.2f;
    [Tooltip("Intensidad extra al ACERTAR un latido (clic al ritmo).")]
    public float hitPulse = 2.5f;
    [Tooltip("Rapidez con la que se apaga cada pulso (más = pulso más corto).")]
    public float decay = 7f;
    [Tooltip("Alcance de la luz (m).")]
    public float range = 7f;

    private Light       _light;
    private RhythmInput _rhythm;
    private float       _hit;

    private void Start()
    {
        var pm = GetComponent<PlayerMovement>();
        Transform cam = pm != null && pm.playerCamera != null ? pm.playerCamera.transform
                      : Camera.main != null ? Camera.main.transform : transform;

        var go = new GameObject("Heart Light");
        go.transform.SetParent(cam, false);
        go.transform.localPosition = localOffset;
        _light = go.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = color;
        _light.range = range;
        _light.shadows = LightShadows.None;
        _light.intensity = baseIntensity;

        _rhythm = FindAnyObjectByType<RhythmInput>();
        if (_rhythm != null) _rhythm.OnBeatHit.AddListener(OnHit);
    }

    // Desactivar el componente (casilla del Inspector o DarkFantasyAtmosphere) apaga también su luz.
    private void OnEnable()  { if (_light != null) _light.gameObject.SetActive(true); }
    private void OnDisable() { if (_light != null) _light.gameObject.SetActive(false); }

    private void OnDestroy()
    {
        if (_rhythm != null) _rhythm.OnBeatHit.RemoveListener(OnHit);
    }

    private void OnHit() => _hit = 1f;

    private void Update()
    {
        if (_light == null) return;
        _light.color = color;
        _light.range = range;

        // Pulso al beat: máximo justo en el beat y se apaga hasta el siguiente.
        float beat = 0f;
        BeatManager bm = BeatManager.Instance;
        if (bm != null && bm.IsPlaying && bm.HasClock)
        {
            double b = bm.ClockBeats;
            if (b >= 0.0) beat = Mathf.Exp(-(float)(b - System.Math.Floor(b)) * decay);
        }
        _hit = Mathf.MoveTowards(_hit, 0f, Time.deltaTime * decay * 0.5f);

        _light.intensity = baseIntensity + beatPulse * beat + hitPulse * _hit * _hit;
    }
}
