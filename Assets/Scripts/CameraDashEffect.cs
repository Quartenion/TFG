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

    [Header("FOV punch al pulsar Q (romper bloques): como un dash hacia delante")]
    [SerializeField] private float _blockPunchFOV = 82f;
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

        // ── rotación (roll aditivo sobre el look de PlayerMovement) ───────────
        float totalRoll = _tiltZ;
        if (CameraShaker.Instance != null)
            totalRoll += CameraShaker.Instance.RollOffset;

        if (totalRoll != 0f)
            transform.localRotation = transform.localRotation * Quaternion.Euler(0f, 0f, totalRoll);
    }

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
        _fovRoutine = StartCoroutine(FOVPunchRoutine(_blockPunchFOV, _blockPunchInTime, _blockPunchOutTime));
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
        float startFOV = _camera.fieldOfView;
        while (t < inTime)
        {
            t += Time.deltaTime;
            _camera.fieldOfView = Mathf.Lerp(startFOV, punchFOV, t / inTime);
            yield return null;
        }

        t = 0f;
        while (t < outTime)
        {
            t += Time.deltaTime;
            // (ease-out: vuelve rápido al principio y se asienta suave)
            float u = Mathf.Clamp01(t / outTime);
            _camera.fieldOfView = Mathf.Lerp(punchFOV, _baseFOV, 1f - (1f - u) * (1f - u));
            yield return null;
        }
        _camera.fieldOfView = _baseFOV;
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
