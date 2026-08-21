using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Graba el recorrido del jugador y sus acciones en un RecordedPath asset.
/// La grabación empieza automáticamente al entrar en Play mode y se guarda al salir.
///
/// SETUP:
///   1. Assets → Create → TFG → Recorded Path
///   2. Añade este componente al Player
///   3. Asigna Recorded Path, Beat Manager y Player Movement
///   4. Entra en Play mode — graba automáticamente
///   5. Sal del Play mode — los datos se guardan a disco
///   6. Los gizmos aparecen en el Scene view
/// </summary>
public class PlayerPathRecorder : MonoBehaviour
{
    [Header("Referencias")]
    public RecordedPath  recordedPath;
    public BeatManager   beatManager;
    public PlayerMovement playerMovement;

    [Header("Configuración")]
    [Tooltip("Muestras de posición por segundo. 20 es suficiente para level design.")]
    [Range(5, 60)]
    public float sampleRate = 20f;

    [Header("Estado (solo lectura)")]
    [SerializeField] private bool _isRecording;
    public bool IsRecording => _isRecording;

    // ── Privado ───────────────────────────────────────────────────────────────

    private CharacterController _cc;
    private float               _sampleTimer;
    private bool                _wasGrounded;

    // =========================================================================
    // Unity lifecycle
    // =========================================================================

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();
    }

    private void Start()
    {
        SubscribeEvents();
        StartRecording();
    }

    private void OnDestroy()  => UnsubscribeEvents();
    private void OnDisable()  { if (_isRecording) StopRecording(); }

    // =========================================================================
    // Suscripción a eventos de PlayerMovement
    // =========================================================================

    private void SubscribeEvents()
    {
        if (playerMovement == null) return;
        playerMovement.OnJumpExecuted.AddListener(OnJump);
        playerMovement.OnDashStarted .AddListener(OnDash);
        playerMovement.OnSlideStarted.AddListener(OnSlide);
        playerMovement.OnSlideEnded  .AddListener(OnSlideEnd);
        playerMovement.OnWallrunStarted.AddListener(OnWallrun);
        playerMovement.OnWalljumped  .AddListener(OnWalljump);
    }

    private void UnsubscribeEvents()
    {
        if (playerMovement == null) return;
        playerMovement.OnJumpExecuted  .RemoveListener(OnJump);
        playerMovement.OnDashStarted   .RemoveListener(OnDash);
        playerMovement.OnSlideStarted  .RemoveListener(OnSlide);
        playerMovement.OnSlideEnded    .RemoveListener(OnSlideEnd);
        playerMovement.OnWallrunStarted.RemoveListener(OnWallrun);
        playerMovement.OnWalljumped    .RemoveListener(OnWalljump);
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private void OnJump()
    {
        if (!_isRecording) return;
        bool isAirJump = _cc != null && !_cc.isGrounded;
        RecordAction(isAirJump ? RecordedPath.ActionType.AirJump
                               : RecordedPath.ActionType.Jump,
                     Vector3.up);
    }

    private void OnDash(Vector3 dir)
    {
        if (!_isRecording) return;
        RecordAction(RecordedPath.ActionType.Dash, dir);
    }

    private void OnSlide()
    {
        if (!_isRecording) return;
        RecordAction(RecordedPath.ActionType.Slide, transform.forward);
    }

    private void OnSlideEnd()
    {
        if (!_isRecording) return;
        RecordAction(RecordedPath.ActionType.SlideEnd, Vector3.zero);
    }

    private void OnWallrun()
    {
        if (!_isRecording) return;
        RecordAction(RecordedPath.ActionType.Wallrun, transform.right);
    }

    private void OnWalljump()
    {
        if (!_isRecording) return;
        RecordAction(RecordedPath.ActionType.Walljump, transform.forward + Vector3.up);
    }

    private void RecordAction(RecordedPath.ActionType type, Vector3 direction)
    {
        if (recordedPath == null) return;
        recordedPath.actions.Add(new RecordedPath.ActionEvent
        {
            type      = type,
            position  = transform.position,
            direction = direction.normalized,
            songTime  = beatManager != null ? beatManager.SongTime         : Time.time,
            beatIndex = beatManager != null ? beatManager.CurrentBeatIndex  : -1,
        });
    }

    // =========================================================================
    // Update — muestreo de posición + detección de aterrizaje
    // =========================================================================

    private void Update()
    {
        if (!_isRecording || recordedPath == null) return;

        // Detección de aterrizaje (transición aire → suelo)
        bool grounded = _cc != null && _cc.isGrounded;
        if (grounded && !_wasGrounded)
            RecordAction(RecordedPath.ActionType.Land, Vector3.down);
        _wasGrounded = grounded;

        // Muestra de posición
        _sampleTimer += Time.deltaTime;
        if (_sampleTimer < 1f / sampleRate) return;
        _sampleTimer = 0f;

        recordedPath.frames.Add(new RecordedPath.Frame
        {
            position   = transform.position,
            songTime   = beatManager != null ? beatManager.SongTime         : Time.time,
            beatIndex  = beatManager != null ? beatManager.CurrentBeatIndex  : -1,
            isGrounded = grounded,
        });
    }

    // =========================================================================
    // API pública / ContextMenu
    // =========================================================================

    [ContextMenu("● Iniciar grabación")]
    public void StartRecording()
    {
        if (recordedPath == null)
        {
            Debug.LogError("[PathRecorder] Asigna un RecordedPath asset en el Inspector.");
            return;
        }

        // Sobreescribir la grabación anterior
        recordedPath.Clear();
        recordedPath.bpm       = beatManager != null ? beatManager.Bpm : 0f;
        recordedPath.walkSpeed = playerMovement != null ? playerMovement.walkSpeed : 0f;

        _isRecording = true;
        _sampleTimer = 0f;
        _wasGrounded = _cc != null && _cc.isGrounded;

        Debug.Log("[PathRecorder] ● Grabando automáticamente…");
    }

    [ContextMenu("■ Detener grabación")]
    public void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;

        int fc = recordedPath != null ? recordedPath.frames.Count  : 0;
        int ac = recordedPath != null ? recordedPath.actions.Count : 0;
        Debug.Log($"[PathRecorder] ■ Grabación finalizada · {fc} frames · {ac} acciones");

#if UNITY_EDITOR
        if (recordedPath != null)
        {
            EditorUtility.SetDirty(recordedPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[PathRecorder] Asset guardado en disco ✓");
        }
#endif
    }
}
