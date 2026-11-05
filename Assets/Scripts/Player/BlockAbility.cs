using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Mecánica para romper bloques (tecla Q).
///
/// Al pulsar Q (una vez, sin mantenerla) se abre una ventana de rotura durante breakWindowDuration segundos. En cada
/// fotograma se comprueba si hay BreakableBlocks a menos de CC.radius + breakProximityMargin del jugador, y se rompen
/// automáticamente. Así, los bloques se rompen justo antes de que el jugador los toque, sin necesidad de una
/// colisión física.
///
/// Configuración.
///   1. Se añade este componente al mismo GameObject que PlayerMovement.
///   2. Se asignan las referencias en el Inspector.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class BlockAbility : MonoBehaviour
{
    [Header("Referencias")]
    public BeatManager beatManager;

    [Header("Ventana de rotura")]
    [Tooltip("Duracion en segundos de la ventana activa tras pulsar Q.")]
    public float breakWindowDuration = 1f;
    [Tooltip("Margen extra sobre el radio del CharacterController para detectar bloques proximos." +
             " 0.3 = rompe bloques a 30 cm antes de tocarlos fisicamente.")]
    public float breakProximityMargin = 0.3f;

    [Header("Energia")]
    [Tooltip("Coste puntual de energia al activar el bloqueo.")]
    public float blockStartCost = 10f;
    [Tooltip("Coste de energia por segundo mientras la ventana esta activa. 0 = sin drain.")]
    public float blockDrainPerSecond = 5f;
    [Tooltip("El bloqueo requiere estar al ritmo para activarse.")]
    public bool blockRequiresBeat = false;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip blockStartSound;
    public AudioClip blockEndSound;
    public AudioClip blockHitSound;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Eventos")]
    public UnityEvent OnBlockStarted;
    public UnityEvent OnBlockEnded;
    public UnityEvent OnBreakableDestroyed;

    /// <summary>Es true mientras la ventana de rotura está activa.</summary>
    public bool IsBlocking { get; private set; }

    private InputAction _blockAction;
    private CharacterController _cc;
    private float _breakWindowTimer;
    private CameraDashEffect _camFx;

    // -=+=-

    void Start()
    {
        _cc = GetComponent<CharacterController>();
    }

    void OnEnable()
    {
        _blockAction = new InputAction("Block", binding: "<Keyboard>/q");
        Keybinds.Apply(_blockAction, Keybinds.Bind.Block);   // Ajustes > Controles
        Keybinds.Changed += ApplyKey;
        _blockAction.Enable();
    }

    private void ApplyKey() => Keybinds.Apply(_blockAction, Keybinds.Bind.Block);

    void OnDisable()
    {
        Keybinds.Changed -= ApplyKey;
        if (_blockAction != null)
        {
            _blockAction.Disable();
            _blockAction = null;
        }
    }

    void Update()
    {
        // La ventana se activa con una pulsación, sin mantener la tecla. No hay tiempo de espera, así que se puede volver
        // a pulsar aunque la ventana siga abierta, y en ese caso se reinicia.
        if (_blockAction != null && _blockAction.WasPressedThisFrame() && TutorialGate.Allows(TutorialKey.Block))
            TryStartBreakWindow();

        if (!IsBlocking) return;

        // Cuenta atrás de la ventana
        _breakWindowTimer -= Time.deltaTime;

        // Se gasta energía durante la ventana, si está configurado.
        if (blockDrainPerSecond > 0f && EnergyManager.Instance != null)
        {
            if (!EnergyManager.Instance.TrySpend(blockDrainPerSecond * Time.deltaTime))
            {
                EndBreakWindow();
                return;
            }
        }

        // Se comprueban los bloques cercanos y se rompen.
        CheckNearbyBreakables();

        // Fin de la ventana
        if (_breakWindowTimer <= 0f)
            EndBreakWindow();
    }

    // -=+=-

    private void TryStartBreakWindow()
    {
        // Solo con el nivel en marcha, no en la zona de inicio ni durante la cuenta atrás.
        BeatManager bmNow = beatManager != null ? beatManager : BeatManager.Instance;
        if (bmNow == null || !bmNow.IsPlaying || (EnergyManager.Instance != null && !EnergyManager.Instance.isActiveAndEnabled))
            return;

        if (blockRequiresBeat && beatManager != null && !beatManager.IsOnBeat())
            return;

        if (blockStartCost > 0f && EnergyManager.Instance != null
            && !EnergyManager.Instance.TrySpend(blockStartCost))
            return;

        IsBlocking        = true;
        _breakWindowTimer = breakWindowDuration;

        PlaySfx(blockStartSound);

        // Los brazos se cruzan en X, preparándose para el impacto, mientras dura la ventana de rotura.
        ViewmodelPhysics.Instance?.Brace(breakWindowDuration);

        // Efecto de cámara, con un cambio rápido del campo de visión, como en un dash hacia delante.
        if (_camFx == null) _camFx = FindAnyObjectByType<CameraDashEffect>();
        if (_camFx != null) _camFx.OnBlockPunch();
        OnBlockStarted?.Invoke();
    }

    private void CheckNearbyBreakables()
    {
        if (_cc == null) return;

        // Radio de detección, que es la cápsula del jugador más el margen configurado más lo que va a avanzar en este
        // fotograma, ya que a mucha velocidad (en un dash) o con pocos FPS el jugador puede recorrer más que el margen en un
        // solo fotograma y chocar antes de que el bloque se rompa.
        float range  = _cc.radius + breakProximityMargin + _cc.velocity.magnitude * Time.deltaTime;
        Vector3 center = transform.position + _cc.center;

        Collider[] hits = Physics.OverlapSphere(center, range);
        foreach (Collider col in hits)
        {
            BreakableBlock block = col.GetComponentInParent<BreakableBlock>();   // el collider puede estar en un hijo
            if (block == null) continue;

            block.Break();
            PlaySfx(blockHitSound);
            OnBreakableDestroyed?.Invoke();
        }
    }

    private void EndBreakWindow()
    {
        if (!IsBlocking) return;
        IsBlocking = false;

        PlaySfx(blockEndSound);
        OnBlockEnded?.Invoke();
    }

    private void PlaySfx(AudioClip clip)
    {
        if (audioSource == null || clip == null) return;
        audioSource.PlayOneShot(clip, sfxVolume);
    }
}
