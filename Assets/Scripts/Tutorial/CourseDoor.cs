using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// PUERTA de la sala final del nivel tutorial: el jugador la MIRA y pulsa E → empieza lo que indica su rótulo
/// (recorrido de estudio, tutorial completo o la práctica de un obstáculo). Usa la misma mirada + E + rótulo que la
/// tienda (FreeroamStand); solo funciona en freeroam.
/// Sus datos se guardan en la escena / el prefab (la sala End_Tutorial lleva las puertas ya puestas); también se puede
/// crear por código con <see cref="Create"/>.
/// </summary>
public class CourseDoor : FreeroamStand
{
    [Header("Puerta del curso")]
    [SerializeField] private string _es = "", _en = "";
    [SerializeField] private Color _col = Color.white;
    [Tooltip("Qué empieza al cruzarla.")]
    [SerializeField] private CourseMode _mode = CourseMode.Study;
    [Tooltip("Práctica: índice del obstáculo en la lista de CourseBuilder.")]
    [SerializeField] private int _obstacleIndex;
    [Tooltip("Práctica: nombre del prefab del obstáculo (manda sobre el índice: así no se lía si cambia el orden de la lista).")]
    [SerializeField] private string _obstacleKey = "";
    [Tooltip("El recorrido sale por la pared de esta puerta (ángulo de la pared, en grados, desde el frente de la sala).")]
    [SerializeField] private bool _exitThroughWall = true;
    [SerializeField] private float _wallYaw;
    [Tooltip("Hojas que se ABREN al cruzarla (giran hacia fuera de la sala sobre su borde). Vacío = sus hijos «Hoja…».")]
    [SerializeField] private Transform[] _leaves = new Transform[0];

    public CourseMode Mode => _mode;
    public int ObstacleIndex => _obstacleIndex;
    public string ObstacleKey => _obstacleKey;
    /// <summary>(Editor) Nombre del prefab del obstáculo que practica.</summary>
    public void SetObstacleKey(string key) => _obstacleKey = key;
    public bool ExitThroughWall => _exitThroughWall;
    public float WallYaw => _wallYaw;
    public bool IsOpen { get; private set; }

    /// <summary>(Editor) Hojas que se abren.</summary>
    public void SetLeaves(params Transform[] leaves) => _leaves = leaves;

    private Action _action;   // (alternativa por código)

    /// <summary>
    /// Crea la puerta. Sin 'colliders' se genera un bloque de 'size' (la puerta); con ellos (p. ej. la puerta de la
    /// sala) se usan esos y no se genera nada.
    /// </summary>
    public static CourseDoor Create(Transform parent, Vector3 worldPos, Quaternion rot, string es, string en, Color col,
                                    Action action, Collider[] colliders = null, Vector3? size = null)
    {
        var d = Make(parent, worldPos, rot, es, en, col, colliders, size);
        d._action = action;
        return d;
    }

    /// <summary>Igual, pero con los datos guardados (modo, obstáculo y pared): vale para dejarla en un prefab.</summary>
    public static CourseDoor CreateSerialized(Transform parent, Vector3 worldPos, Quaternion rot, string es, string en, Color col,
                                              CourseMode mode, int obstacleIndex, bool exitThroughWall, float wallYaw,
                                              Collider[] colliders = null, Vector3? size = null, bool floatingLabel = true)
    {
        var d = Make(parent, worldPos, rot, es, en, col, colliders, size);
        d._showLabel = floatingLabel;   // (las puertas con cartel no llevan el rótulo flotante)
        d._mode = mode; d._obstacleIndex = obstacleIndex; d._exitThroughWall = exitThroughWall; d._wallYaw = wallYaw;
        return d;
    }

    private static CourseDoor Make(Transform parent, Vector3 worldPos, Quaternion rot, string es, string en, Color col,
                                   Collider[] colliders, Vector3? size)
    {
        var go = new GameObject("Puerta · " + es);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(worldPos, rot);
        var d = go.AddComponent<CourseDoor>();   // (su Start aún no se ha ejecutado: se configura ya)
        d._es = es; d._en = en; d._col = col;
        d._blockColor = col * 0.55f + Color.black * 0.45f;
        d._blockSize = colliders != null ? Vector3.zero : size ?? new Vector3(3.5f, 5f, 0.6f);
        d._maxDistance = 7f;
        d._onlyInFreeroam = true;
        d._promptText = "[ E ]  " + es;
        d._promptTextEn = "[ E ]  " + en;
        if (colliders != null) d._colliders = colliders;
        return d;
    }

    /// <summary>
    /// Las puertas no son tiendas (no les afecta que estén cerradas). En el modo prueba solo se abre la GRANDE, y solo
    /// cuando toca la siguiente parte (CourseBuilder.StudyDoorOpen).
    /// </summary>
    protected override bool AllowedNow =>
        !(StudySession.Active && !StudySession.InFreePlay)
        // (Modo prueba, ANTES de empezar el estudio: se pueden practicar los obstáculos por las puertas pequeñas.)
        || (!StudySession.RunsStarted && _mode != CourseMode.Study)
        || (_mode == CourseMode.Study && CourseBuilder.StudyDoorOpen);

    protected override string LabelEs    => _es;
    protected override string LabelEn    => _en;
    protected override Color  LabelColor => _col;

    protected override string PromptLine()  => Loc.T(_promptText, _promptTextEn);
    protected override float  PromptWidth   => 520f;
    protected override Color  PromptColor   => new Color(1f, 0.85f, 0.3f);

    protected override void OpenPanel(Action onClose)
    {
        onClose?.Invoke();
        if (_action != null) { _action(); return; }
        // Se abre la puerta, el jugador camina hasta ella y el recorrido empieza DETRÁS (ver CourseBuilder).
        if (CourseBuilder.Instance != null) CourseBuilder.Instance.DoorChosen(this);
        else CourseBuilder.ChooseFromDoor(_mode, _obstacleIndex, _exitThroughWall ? _wallYaw : float.NaN);
    }

    // ── Apertura ─────────────────────────────────────────────────────────────

    /// <summary>Dirección horizontal hacia FUERA de la sala (por donde sale el recorrido).</summary>
    public Vector3 Outward
    {
        get
        {
            // La normal de su pared (no la dirección desde el centro: las puertas están desplazadas a lo largo de ella).
            Quaternion room = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            Vector3 d = room * Quaternion.Euler(0f, _wallYaw, 0f) * Vector3.forward; d.y = 0f;
            return d.normalized;
        }
    }

    /// <summary>Hojas de la puerta (para resaltarla, ver DoorHighlight).</summary>
    public Transform[] LeafTransforms => Leaves();

    private Transform[] Leaves()
    {
        if (_leaves != null && _leaves.Length > 0) return _leaves;
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform t in transform) if (t.name.StartsWith("Hoja")) list.Add(t);
        return list.ToArray();
    }

    /// <summary>Abre las hojas en 'seconds' (0 = de golpe). Cada hoja gira sobre su borde exterior hacia fuera.</summary>
    public IEnumerator Open(float seconds, float angle = 90f)
    {
        if (IsOpen) yield break;
        IsOpen = true;
        Transform[] leaves = Leaves();
        Vector3 outward = Outward, inward = -outward;
        Vector3 right = Vector3.Cross(Vector3.up, inward);   // (derecha vista desde dentro de la sala)
        var hinges = new Vector3[leaves.Length];
        var signs = new float[leaves.Length];
        for (int i = 0; i < leaves.Length; i++)
        {
            if (leaves[i] == null) continue;
            Bounds b = Bounds(leaves[i]);
            float side = Vector3.Dot(b.center - transform.position, right);
            bool rightLeaf = side > 0.5f;
            float ext = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z))));
            hinges[i] = b.center + right * (rightLeaf ? ext : -ext);
            signs[i] = rightLeaf ? -1f : 1f;
            foreach (Collider c in leaves[i].GetComponentsInChildren<Collider>()) c.enabled = false;   // (se puede pasar)
        }
        float done = 0f;
        float total = Mathf.Max(0.0001f, seconds);
        while (done < 1f)
        {
            float next = seconds <= 0f ? 1f : Mathf.Min(1f, done + Time.deltaTime / total);
            float step = (Mathf.SmoothStep(0f, 1f, next) - Mathf.SmoothStep(0f, 1f, done)) * angle;
            for (int i = 0; i < leaves.Length; i++)
                if (leaves[i] != null) leaves[i].RotateAround(hinges[i], Vector3.up, step * signs[i]);
            done = next;
            if (done < 1f) yield return null;
        }
    }

    /// <summary>Abierta del todo, sin animación (al aparecer la sala de salida detrás del jugador).</summary>
    public void OpenInstant() { IEnumerator e = Open(0f); while (e.MoveNext()) { } }

    private static Bounds Bounds(Transform t)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(t.position, Vector3.one);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    protected override Color  GizmoColor => _col;
    protected override string GizmoLabel => "PUERTA · " + _es;
}
