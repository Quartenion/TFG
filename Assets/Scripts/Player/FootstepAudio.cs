using UnityEngine;

/// <summary>
/// Reproduce pasos aleatorios cada half-beat, pero solo si el jugador esta en el suelo.
///
/// SETUP en Unity:
///   1. Añade este componente al GameObject del jugador (donde esta CharacterController).
///   2. Arrastra 3 AudioClips de pasos a _footstepClips en el Inspector.
///   3. Asigna un AudioSource al campo _audioSource (puede ser uno dedicado).
///   4. En BeatManager -> OnHalfBeat, conecta FootstepAudio.Step().
///      Al igual que TimingFeedbackUI, se conecta via Inspector y queda desacoplado.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FootstepAudio : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("Los tres clips de pasos. Se elegira uno aleatoriamente en cada half-beat.")]
    [SerializeField] private AudioClip[] _footstepClips = new AudioClip[3];

    [SerializeField] private AudioSource _audioSource;

    [Header("Volumen y pitch")]
    [Range(0f, 1f)]
    [SerializeField] private float _volume = 0.6f;

    [Tooltip("Variacion aleatoria de pitch para que los pasos no suenen roboticos.")]
    [SerializeField] private float _pitchVariance = 0.08f;

    [Header("Sincronizacion")]
    [Tooltip("Retraso (+) o adelanto (-) del sonido respecto al half-beat en segundos.\n" +
             "Negativo: salta el silencio inicial del clip (util si el audio tarda en arrancar).\n" +
             "Positivo: retrasa la reproduccion. Prueba valores entre -0.15 y 0.15.")]
    [Range(-0.3f, 0.3f)]
    [SerializeField] private float _stepTimeOffset = 0f;

    [Header("Condiciones")]
    [Tooltip("Solo suena cuando el personaje esta en el suelo.")]
    [SerializeField] private bool _requireGrounded = true;

    [Tooltip("Solo suena cuando el jugador se esta moviendo (input WASD).")]
    [SerializeField] private bool _requireMoving = true;

    [Tooltip("Velocidad minima (unidades/s) para considerar que el jugador se esta moviendo.")]
    [SerializeField] private float _minSpeedThreshold = 0.5f;

    /// <summary>Clips de pasos asignados (los reutiliza LobbyMusic en el freeroam).</summary>
    public AudioClip[] Clips => _footstepClips;

    private CharacterController _cc;
    private int _lastClipIndex = -1;
    private Coroutine _pendingStep;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();

        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 2D para sonido de UI/camara, cambia a 1f si es 3D world
        }
    }

    /// <summary>
    /// Llama a este metodo desde BeatManager.OnHalfBeat en el Inspector.
    /// Reproduce un paso aleatorio si las condiciones se cumplen.
    /// </summary>
    public void Step()
    {
        if (_footstepClips == null || _footstepClips.Length == 0) return;
        if (_audioSource == null) return;

        // Comprobar condiciones antes de gastar recursos en la coroutine
        if (_requireGrounded && !_cc.isGrounded) return;
        if (_requireMoving)
        {
            Vector3 horizontal = _cc.velocity;
            horizontal.y = 0f;
            if (horizontal.magnitude < _minSpeedThreshold) return;
        }

        AudioClip clip = PickRandomClip();
        if (clip == null) return;

        // Cancelar paso anterior pendiente si aun no sono (por si el BPM es muy rapido)
        if (_pendingStep != null) StopCoroutine(_pendingStep);
        _pendingStep = StartCoroutine(ScheduledStep(clip));
    }

    private System.Collections.IEnumerator ScheduledStep(AudioClip clip)
    {
        if (_stepTimeOffset > 0f)
        {
            // Retraso: esperar antes de reproducir
            yield return new WaitForSeconds(_stepTimeOffset);
        }

        float pitch = 1f + Random.Range(-_pitchVariance, _pitchVariance);
        _audioSource.pitch = pitch;

        if (_stepTimeOffset < 0f)
        {
            // Adelanto: saltar el silencio inicial del clip
            // (equivale a empezar la reproduccion |offset| segundos dentro del clip)
            float skipTime = Mathf.Clamp(-_stepTimeOffset, 0f, clip.length - 0.01f);
            _audioSource.clip   = clip;
            _audioSource.volume = _volume;
            _audioSource.time   = skipTime;
            _audioSource.Play();
        }
        else
        {
            _audioSource.PlayOneShot(clip, _volume);
        }

        _pendingStep = null;
    }

    private AudioClip PickRandomClip()
    {
        // Filtrar nulls
        int validCount = 0;
        for (int i = 0; i < _footstepClips.Length; i++)
            if (_footstepClips[i] != null) validCount++;

        if (validCount == 0) return null;
        if (validCount == 1)
        {
            for (int i = 0; i < _footstepClips.Length; i++)
                if (_footstepClips[i] != null) return _footstepClips[i];
        }

        // Con varios clips: elegir aleatoriamente sin repetir el ultimo
        int attempts = 0;
        int idx;
        do
        {
            idx = Random.Range(0, _footstepClips.Length);
            attempts++;
        }
        while ((_footstepClips[idx] == null || idx == _lastClipIndex) && attempts < 20);

        _lastClipIndex = idx;
        return _footstepClips[idx];
    }
}
