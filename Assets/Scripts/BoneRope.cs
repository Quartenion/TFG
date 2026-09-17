using UnityEngine;

/// <summary>
/// Usa una cuerda de FÍSICA de huesos (Rigidbody + joints, tipo tutorial) como la cuerda del corazón.
/// NO fuerza la forma: solo ancla el primer hueso a la MANO (y opcionalmente el último al CORAZÓN) y
/// aplica GRAVEDAD INVERTIDA a los huesos de en medio, así la cuerda "cae hacia arriba" y cuelga con la
/// física del propio engine. Los huesos de en medio son libres (no se tocan cada frame).
///
/// Misma API que el TubeRenderer (SetEndpoints / Hide): el WaterDiveController la dibuja igual.
///
/// SETUP:
///   1. DEJA la física del tutorial en los huesos (Rigidbody + joints).
///   2. Añade este componente a la RAÍZ de la cuerda (el objeto con el mesh).
///   3. 'Bones' EN ORDEN: [0] = extremo MANO, último = extremo CORAZÓN (el último con Rigidbody).
///   4. En el WaterDiveController pon este componente en 'Bone Rope' (deja vacíos Rope y Rope Chain).
/// </summary>
public class BoneRope : MonoBehaviour
{
    [Tooltip("Huesos EN ORDEN: [0] = extremo MANO (ancla), último = extremo CORAZÓN.")]
    public Transform[] bones;

    [Header("Física (cae hacia arriba)")]
    [Tooltip("Gravedad INVERTIDA: la cuerda cae hacia ARRIBA con física propia. Desactívalo para gravedad normal.")]
    public bool fallUp = true;
    [Tooltip("Fuerza de la gravedad invertida (m/s²). ~9.8 = como la gravedad normal pero hacia arriba.")]
    public float fallUpGravity = 9.8f;
    [Tooltip("Cuánto IGUALA la cuerda tu velocidad horizontal (para que no se arrastre hacia atrás al " +
             "avanzar). 1 = pegada al jugador (sin arrastre); 0 = física pura (se arrastra). ~0.8-1 recomendado.")]
    [Range(0f, 1f)]
    public float followForwardStrength = 1f;
    [Tooltip("Fijar también el ÚLTIMO hueso al corazón. OFF (recomendado) = solo ancla la mano y el resto " +
             "cuelga/cae libre con física (lo más natural). ON = conecta el extremo al corazón (puede tirar/" +
             "estirar si el corazón está más lejos que el largo de la cuerda).")]
    public bool pinEnd = false;

    [SerializeField] private Renderer _renderer;

    private Rigidbody[] _rbs;
    private float[]     _restLengths;   // largos de reposo entre huesos (para colocarlos sin estirar)
    private Rigidbody   _startRb, _endRb;
    private Vector3     _startTarget, _endTarget;
    private Vector3     _prevStartTarget;   // para estimar la velocidad del ancla (jugador)
    private bool        _hasPrev;
    private bool        _active;

    private void Awake()
    {
        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        if (_renderer is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        if (bones == null || bones.Length < 2) return;

        int n = bones.Length;
        _rbs         = new Rigidbody[n];
        _restLengths = new float[n - 1];
        for (int i = 0; i < n; i++)
            _rbs[i] = bones[i] != null ? bones[i].GetComponent<Rigidbody>() : null;
        for (int i = 0; i < n - 1; i++)
            _restLengths[i] = (bones[i] != null && bones[i + 1] != null)
                ? Vector3.Distance(bones[i].position, bones[i + 1].position) : 0.5f;

        _startRb = _rbs[0];
        _endRb   = _rbs[n - 1];
        if (_startRb != null) _startRb.isKinematic = true;               // ancla MANO (la movemos nosotros)
        if (pinEnd && _endRb != null) _endRb.isKinematic = true;         // ancla CORAZÓN (opcional)
        // A los huesos DINÁMICOS les quitamos la gravedad normal: se la aplicamos invertida en FixedUpdate.
        if (fallUp)
            for (int i = 0; i < n; i++)
                if (_rbs[i] != null && !_rbs[i].isKinematic) _rbs[i].useGravity = false;
    }

    /// <summary>Ancla mano ('start') y, si pinEnd, corazón ('end'). 'sag' se ignora (la física ya cuelga).</summary>
    public void SetEndpoints(Vector3 start, Vector3 end, float sag)
    {
        if (bones == null || bones.Length < 2) return;
        if (!_active) PlaceAlong(start, end);   // colocar UNA vez al lanzar (no forzar cada frame)
        _startTarget = start;
        _endTarget   = end;
        _active      = true;
        if (_renderer != null && !_renderer.enabled) _renderer.enabled = true;
    }

    public void Hide()
    {
        _active = false;
        if (_renderer != null) _renderer.enabled = false;
    }

    private void OnDisable() => _active = false;

    private void FixedUpdate()
    {
        if (!_active || bones == null || bones.Length < 2) return;

        // Velocidad del ancla (≈ velocidad del jugador), estimada por el desplazamiento entre frames.
        float dt = Time.fixedDeltaTime;
        Vector3 anchorVel = (_hasPrev && dt > 0f) ? (_startTarget - _prevStartTarget) / dt : Vector3.zero;
        _prevStartTarget = _startTarget;
        _hasPrev = true;
        Vector3 anchorHoriz = new Vector3(anchorVel.x, 0f, anchorVel.z);

        // Anclar la MANO (y el CORAZÓN si pinEnd). El resto de huesos son libres (física).
        MovePinned(_startRb, bones[0], _startTarget);
        if (pinEnd) MovePinned(_endRb, bones[bones.Length - 1], _endTarget);

        if (_rbs == null) return;
        for (int i = 0; i < _rbs.Length; i++)
        {
            Rigidbody rb = _rbs[i];
            if (rb == null || rb.isKinematic) continue;

            // IGUALAR la velocidad HORIZONTAL a la del jugador: quita el arrastre por avanzar, así la
            // cuerda se mueve contigo y no se estira hacia atrás. La vertical se deja LIBRE (para que cuelgue).
            float   k        = Mathf.Clamp01(followForwardStrength);
            Vector3 v        = rb.linearVelocity;
            Vector3 curHoriz = new Vector3(v.x, 0f, v.z);
            Vector3 newHoriz = Vector3.Lerp(curHoriz, anchorHoriz, k);
            rb.linearVelocity = new Vector3(newHoriz.x, v.y, newHoriz.z);

            // Gravedad INVERTIDA: la cuerda cae hacia arriba con física propia.
            if (fallUp) rb.AddForce(Vector3.up * fallUpGravity, ForceMode.Acceleration);
        }
    }

    private static void MovePinned(Rigidbody rb, Transform bone, Vector3 target)
    {
        if (rb != null)        rb.MovePosition(target);
        else if (bone != null) bone.position = target;
    }

    /// <summary>Coloca los huesos UNA vez en fila (mano -> corazón) con sus largos de reposo, sin estirar.</summary>
    private void PlaceAlong(Vector3 start, Vector3 end)
    {
        _prevStartTarget = start;   // arrancar la estimación de velocidad sin saltos
        _hasPrev         = false;

        int n = bones.Length;
        Vector3 dir = end - start;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;

        Vector3 p = start;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) p += dir * _restLengths[i - 1];
            if (bones[i] == null) continue;
            bones[i].position = p;
            if (_rbs[i] != null && !_rbs[i].isKinematic)
            {
                _rbs[i].position        = p;
                _rbs[i].linearVelocity  = Vector3.zero;
                _rbs[i].angularVelocity = Vector3.zero;
            }
        }
    }
}
