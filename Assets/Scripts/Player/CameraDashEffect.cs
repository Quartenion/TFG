using System.Collections;
using UnityEngine;

/// <summary>
/// Efectos visuales de camara para el dash:
///  - FOV punch: zoom out brusco que vuelve suave (sensacion de aceleracion).
///  - Shake breve para impacto.
///  - Tilt direccional opcional (roll lateral).
///
/// Se aplica como OFFSET en LateUpdate, despues de que PlayerMovement haya escrito
/// la rotacion del look en Update, para que no se peleen y la mirada del jugador
/// se mantenga estable durante el dash.
/// Engancha el evento OnDashStarted -> CameraDashEffect.OnDash(Vector3) en inspector.
/// </summary>
public class CameraDashEffect : MonoBehaviour
{
    [Header("Camara objetivo")]
    [SerializeField] private Camera _camera;
    [SerializeField] private Transform _playerTransform;

    [Header("FOV punch")]
    [SerializeField] private float _baseFOV = 60f;
    [SerializeField] private float _punchFOV = 78f;
    [Tooltip("Tiempo del 'zoom out' brusco (s)")]
    [SerializeField] private float _punchInTime = 0.06f;
    [Tooltip("Tiempo de la vuelta suave a la base (s)")]
    [SerializeField] private float _punchOutTime = 0.35f;

    [Header("FOV punch al pulsar Q (romper bloques): la vista se ACERCA (zoom hacia delante)")]
    [Tooltip("FOV al pulsar Q. MENOR que el normal (60) = la cámara «se echa hacia delante» y los brazos en X llenan la " +
             "pantalla (con un FOV mayor se alejaban y se veía por dónde se cortan).")]
    [SerializeField] private float _blockPunchFOVIn = 50f;
    [Tooltip("Tiempo del 'zoom out' brusco al pulsar Q (s)")]
    [SerializeField] private float _blockPunchInTime = 0.05f;
    [Tooltip("Tiempo de la vuelta a la base (s)")]
    [SerializeField] private float _blockPunchOutTime = 0.3f;

    [Header("Shake")]
    [SerializeField] private float _shakeAmount = 0.04f;
    [SerializeField] private float _shakeDuration = 0.12f;

    [Header("Tilt direccional (roll)")]
    [SerializeField] private bool _enableTilt = true;
    [Tooltip("Grados maximos de roll lateral en la cara opuesta al dash")]
    [SerializeField] private float _tiltAmount = 6f;
    [SerializeField] private float _tiltInTime = 0.08f;
    [SerializeField] private float _tiltOutTime = 0.3f;

    // Posicion local base de la camara (para que el shake no se acumule)
    private Vector3 _baseLocalPos;

    // Offsets que aplicamos sobre lo que pone PlayerMovement
    private Vector3 _shakeOffset = Vector3.zero;
    private float   _tiltZ        = 0f;

    private Coroutine _fovRoutine;
    private float _fovOffset;   // golpe de FOV del dash / Q (grados sobre la base)

    /// <summary>
    /// FOV extra de OTROS efectos (p. ej. el latido de LowHealthEffects). Este componente es el ÚNICO que escribe el
    /// FOV de la cámara: base + golpe del dash/Q + este extra. Antes cada uno lo escribía por su cuenta y el latido
    /// (barra azul llena / poca sangre) pisaba el golpe de la Q.
    /// </summary>
    public static float ExternalFovOffset;
    public static bool Active { get; private set; }

    private void OnEnable()  => Active = true;
    private void OnDisable() { Active = false; ExternalFovOffset = 0f; }
    private Coroutine _shakeRoutine;
    private Coroutine _tiltRoutine;

    private void Awake()
    {
        if (_camera == null) _camera = GetComponent<Camera>();
        if (_camera != null) _baseFOV = _camera.fieldOfView;
        _baseLocalPos = transform.localPosition;
    }

    private void LateUpdate()
    {
        // PlayerMovement ya escribió la rotación del look en Update().
        // Este LateUpdate es el ÚNICO punto que escribe localPosition / localRotation
        // de la cámara, sumando todos los efectos activos de forma aditiva:
        //   · _shakeOffset → shake del dash (corrutina)
        //   · CameraShaker → shake de trauma (bloques, impactos, etc.)
        //   · _tiltZ       → roll del dash
        //   · RollOffset   → roll del trauma

        // ── posición ──────────────────────────────────────────────────────────
        Vector3 totalPosOffset = _shakeOffset;
        if (CameraShaker.Instance != null)
            totalPosOffset += CameraShaker.Instance.PositionOffset;

        transform.localPosition = _baseLocalPos + totalPosOffset;

        // ── FOV (aditivo) ──────────────────────────────────────────────────────
        if (_camera != null) _camera.fieldOfView = _baseFOV + _fovOffset + ExternalFovOffset;

        // ── rotación (roll aditivo sobre el look de PlayerMovement) ───────────
        float totalRoll = _tiltZ;
        if (CameraShaker.Instance != null)
            totalRoll += CameraShaker.Instance.RollOffset;

        // El roll se suma sobre la rotación que escribió PlayerMovement en su Update. Si este frame NADIE la ha
        // reescrito (pausa: PlayerMovement desactivado; tienda, rebobinado…), aún lleva el roll del frame anterior:
        // se quita antes de volver a aplicarlo. Si no, el roll se acumulaba frame a frame y la cámara giraba sin parar.
        Quaternion baseRot = transform.localRotation;
        if (_hasWritten && Quaternion.Angle(baseRot, _lastWrittenRot) < 0.01f)
            baseRot = _lastWrittenRot * Quaternion.Euler(0f, 0f, -_lastRoll);

        transform.localRotation = totalRoll != 0f ? baseRot * Quaternion.Euler(0f, 0f, totalRoll) : baseRot;
        _lastWrittenRot = transform.localRotation;
        _lastRoll       = totalRoll;
        _hasWritten     = true;
    }

    private Quaternion _lastWrittenRot = Quaternion.identity;
    private float      _lastRoll;
    private bool       _hasWritten;

    /// <summary>
    /// Llamado desde el evento OnDashStarted (recibe la direccion del dash en world space).
    /// Engancha en inspector: PlayerMovement -> OnDashStarted -> CameraDashEffect.OnDash
    /// </summary>
    public void OnDash(Vector3 dashDirection)
    {
        TriggerFOVPunch();
        TriggerShake();
        if (_enableTilt && _playerTransform != null)
        {
            // side: -1 si dash es a la izquierda, +1 a la derecha (respecto al forward del jugador)
            Vector3 right = _playerTransform.right;
            float side = Vector3.Dot(dashDirection.normalized, right);
            TriggerTilt(-side * _tiltAmount); // contraria al lado del dash
        }
    }

    /// <summary>
    /// Q (romper bloques): golpe de FOV rápido, como si saliera disparado hacia delante, + un pequeño shake.
    /// Lo llama BlockAbility al activar la ventana de rotura.
    /// </summary>
    public void OnBlockPunch()
    {
        if (_camera == null) return;
        if (_fovRoutine != null) StopCoroutine(_fovRoutine);
        _fovRoutine = StartCoroutine(FOVPunchRoutine(_blockPunchFOVIn, _blockPunchInTime, _blockPunchOutTime));
        TriggerShake();
    }

    // === FOV ===
    private void TriggerFOVPunch()
    {
        if (_camera == null) return;
        if (_fovRoutine != null) StopCoroutine(_fovRoutine);
        _fovRoutine = StartCoroutine(FOVPunchRoutine(_punchFOV, _punchInTime, _punchOutTime));
    }

    private IEnumerator FOVPunchRoutine(float punchFOV, float inTime, float outTime)
    {
        float t = 0f;
        float start = _fovOffset, peak = punchFOV - _baseFOV;
        while (t < inTime)
        {
            t += Time.deltaTime;
            _fovOffset = Mathf.Lerp(start, peak, t / inTime);
            yield return null;
        }

        t = 0f;
        while (t < outTime)
        {
            t += Time.deltaTime;
            // (ease-out: vuelve rápido al principio y se asienta suave)
            float u = Mathf.Clamp01(t / outTime);
            _fovOffset = Mathf.Lerp(peak, 0f, 1f - (1f - u) * (1f - u));
            yield return null;
        }
        _fovOffset = 0f;
        _fovRoutine = null;
    }

    // === Shake ===
    private void TriggerShake()
    {
        if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
        _shakeRoutine = StartCoroutine(ShakeRoutine());
    }

    private IEnumerator ShakeRoutine()
    {
        float t = 0f;
        while (t < _shakeDuration)
        {
            t += Time.deltaTime;
            float damping = 1f - (t / _shakeDuration);
            _shakeOffset = Random.insideUnitSphere * _shakeAmount * damping;
            yield return null;
        }
        _shakeOffset = Vector3.zero;
        _shakeRoutine = null;
    }

    // === Tilt (roll en Z local) ===
    private void TriggerTilt(float zDegrees)
    {
        if (_tiltRoutine != null) StopCoroutine(_tiltRoutine);
        _tiltRoutine = StartCoroutine(TiltRoutine(zDegrees));
    }

    private IEnumerator TiltRoutine(float zDegrees)
    {
        float t = 0f;
        while (t < _tiltInTime)
        {
            t += Time.deltaTime;
            _tiltZ = Mathf.Lerp(0f, zDegrees, t / _tiltInTime);
            yield return null;
        }

        t = 0f;
        while (t < _tiltOutTime)
        {
            t += Time.deltaTime;
            _tiltZ = Mathf.Lerp(zDegrees, 0f, t / _tiltOutTime);
            yield return null;
        }

        _tiltZ = 0f;
        _tiltRoutine = null;
    }
}
