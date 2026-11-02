using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// BALANZA DEL CORAZÓN (sala final del nivel tutorial), sobre el pedestal del centro: una pluma en el platillo derecho.
/// Al llegar a la meta (<see cref="Weigh"/>) el jugador deja su corazón en el platillo izquierdo y la balanza se
/// inclina según lo bien que ha jugado: si ha ido bien, el corazón pesa MENOS que la pluma y sube; si no, baja.
/// Construida por código con primitivas (como CarvedShop).
/// </summary>
public class HeartScale : MonoBehaviour
{
    private static readonly Color Gold     = new Color(0.78f, 0.6f, 0.25f);
    private static readonly Color GoldDark = new Color(0.45f, 0.33f, 0.12f);
    private static readonly Color Feather  = new Color(0.95f, 0.93f, 0.88f);
    private static readonly Color HeartCol = new Color(0.55f, 0.04f, 0.06f);

    private const float ArmHalf = 1.5f;     // medio brazo de la balanza
    private const float Hang = 1.15f;       // longitud de las cadenas de los platillos
    private const float MaxTilt = 18f;      // grados

    [SerializeField] private Transform _beam, _panL, _panR, _heart;
    [SerializeField] private TextMeshPro _verdict;

    /// <summary>
    /// (Editor) Si se asigna, da los materiales (como assets) al construir la balanza para guardarla en un prefab;
    /// si no, se crean materiales en memoria (al jugar).
    /// </summary>
    public static System.Func<Color, float, Material> MaterialProvider;
    private float _angle;
    private readonly List<Renderer> _hiddenArm = new List<Renderer>();

    /// <summary>Construye la balanza sobre el pedestal «Cylinder» de la sala (o en su centro si no lo hay).</summary>
    public static HeartScale Build(Transform room)
    {
        Vector3 top = room.position;
        Renderer floor = room.GetComponent<Renderer>();   // (sin pedestal: sobre el suelo de la sala)
        if (floor != null) top.y = floor.bounds.max.y;
        Transform ped = room.Find("Cylinder");
        if (ped != null && ped.GetComponent<Renderer>() != null)
        {
            Bounds b = ped.GetComponent<Renderer>().bounds;
            top = new Vector3(b.center.x, b.max.y, b.center.z);
        }
        var root = new GameObject("Balanza del corazón").transform;
        root.SetParent(room, false);
        root.position = top;
        root.rotation = room.rotation;
        var s = root.gameObject.AddComponent<HeartScale>();
        s.Construct();
        return s;
    }

    private void Construct()
    {
        Transform r = transform;
        // Pie y columna.
        P(PrimitiveType.Cylinder, r, "Pie", new Vector3(0f, 0.12f, 0f), new Vector3(1.3f, 0.12f, 1.3f), GoldDark);
        P(PrimitiveType.Cylinder, r, "Pie 2", new Vector3(0f, 0.3f, 0f), new Vector3(0.8f, 0.08f, 0.8f), Gold);
        P(PrimitiveType.Cylinder, r, "Columna", new Vector3(0f, 1.6f, 0f), new Vector3(0.2f, 1.25f, 0.2f), Gold);
        P(PrimitiveType.Sphere, r, "Remate", new Vector3(0f, 3.05f, 0f), new Vector3(0.32f, 0.32f, 0.32f), Gold, 0.4f);

        // Brazo (gira sobre el eje Z en lo alto de la columna).
        _beam = new GameObject("Brazo").transform;
        _beam.SetParent(r, false);
        _beam.localPosition = new Vector3(0f, 2.85f, 0f);
        P(PrimitiveType.Cube, _beam, "Barra", Vector3.zero, new Vector3(ArmHalf * 2f + 0.2f, 0.09f, 0.09f), Gold);
        for (int s = -1; s <= 1; s += 2)
            P(PrimitiveType.Sphere, _beam, "Extremo", new Vector3(s * ArmHalf, 0f, 0f), new Vector3(0.14f, 0.14f, 0.14f), Gold);

        _panL = Pan(r, "Platillo izquierdo");
        _panR = Pan(r, "Platillo derecho");

        // Pluma (platillo derecho): cuerpo blanco alargado y cañón.
        var feather = new GameObject("Pluma").transform;
        feather.SetParent(_panR, false);
        feather.localPosition = new Vector3(0f, 0.08f, 0f);
        feather.localRotation = Quaternion.Euler(0f, 25f, 8f);
        P(PrimitiveType.Sphere, feather, "Vexilo", new Vector3(0f, 0.03f, 0.05f), new Vector3(0.16f, 0.05f, 0.75f), Feather, 0.35f);
        P(PrimitiveType.Cube, feather, "Cañón", new Vector3(0f, 0.04f, -0.25f), new Vector3(0.02f, 0.02f, 0.65f), GoldDark);

        // Corazón (se coloca al pesar).
        _heart = new GameObject("Corazón").transform;
        _heart.SetParent(r, false);
        for (int s = -1; s <= 1; s += 2)
            P(PrimitiveType.Sphere, _heart, "Lóbulo", new Vector3(s * 0.09f, 0.06f, 0f), new Vector3(0.22f, 0.22f, 0.2f), HeartCol, 0.3f);
        P(PrimitiveType.Cube, _heart, "Punta", new Vector3(0f, -0.06f, 0f), new Vector3(0.19f, 0.19f, 0.18f), HeartCol, 0.3f, rotZ: 45f);
        _heart.gameObject.SetActive(false);

        // Veredicto (texto encima de la balanza).
        var tgo = new GameObject("Veredicto", typeof(RectTransform));
        tgo.transform.SetParent(r, false);
        tgo.transform.localPosition = new Vector3(0f, 3.7f, 0f);
        // (Sin girar: el texto se lee desde −Z, que es desde donde llega el jugador.)
        _verdict = tgo.AddComponent<TextMeshPro>();
        _verdict.alignment = TextAlignmentOptions.Center;
        _verdict.fontStyle = FontStyles.Bold;
        _verdict.enableAutoSizing = true; _verdict.fontSizeMin = 0.5f; _verdict.fontSizeMax = 3f;
        _verdict.rectTransform.sizeDelta = new Vector2(6f, 1f);
        _verdict.text = "";

        UpdatePans();
    }

    private Transform Pan(Transform r, string name)
    {
        var pan = new GameObject(name).transform;
        pan.SetParent(r, false);
        P(PrimitiveType.Cylinder, pan, "Plato", Vector3.zero, new Vector3(0.95f, 0.03f, 0.95f), Gold);
        // Tres cadenas hasta el punto de cuelgue.
        for (int i = 0; i < 3; i++)
        {
            float a = i * 120f * Mathf.Deg2Rad;
            Vector3 rim = new Vector3(Mathf.Cos(a) * 0.42f, 0f, Mathf.Sin(a) * 0.42f);
            Vector3 mid = (rim + new Vector3(0f, Hang, 0f)) * 0.5f;
            var c = P(PrimitiveType.Cube, pan, "Cadena", mid, new Vector3(0.015f, Vector3.Distance(rim, new Vector3(0f, Hang, 0f)), 0.015f), GoldDark);
            c.transform.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(0f, Hang, 0f) - rim);
        }
        return pan;
    }

    private void LateUpdate() => UpdatePans();

    /// <summary>Los platillos cuelgan siempre derechos de los extremos del brazo.</summary>
    private void UpdatePans()
    {
        if (_beam == null) return;
        _beam.localRotation = Quaternion.Euler(0f, 0f, _angle);
        _panL.position = _beam.TransformPoint(new Vector3(-ArmHalf, 0f, 0f)) - transform.up * Hang;
        _panR.position = _beam.TransformPoint(new Vector3(ArmHalf, 0f, 0f)) - transform.up * Hang;
        _panL.rotation = _panR.rotation = transform.rotation;
    }

    /// <summary>
    /// El jugador deja su corazón en el platillo izquierdo y la balanza se inclina. score01: 1 = nivel perfecto
    /// (el corazón sube), 0 = muy mal (el corazón baja).
    /// </summary>
    public IEnumerator Weigh(Transform player, Camera cam, float score01)
    {
        // 1. El corazón que el jugador lleva en la mano sale de ella y vuela en arco hasta el platillo izquierdo.
        //    (El de la mano se ve con la cámara del brazo; se crea una copia en el mundo justo donde se veía y del mismo
        //     tamaño aparente, y el de la mano se esconde.)
        Transform flying = TakeHeldHeart(player, cam);
        if (flying == null)
        {
            flying = _heart;   // (sin corazón en la mano: el de la balanza)
            _heart.gameObject.SetActive(true);
            _heart.SetParent(transform, true);
            if (cam != null) _heart.position = cam.transform.position + cam.transform.forward * 0.7f + cam.transform.right * 0.28f - cam.transform.up * 0.35f;
        }
        Vector3 from = flying.position;
        Quaternion r0 = flying.rotation;
        Vector3 s0 = flying.localScale, s1 = TargetScale(flying, 0.42f);
        const float fly = 1.5f;
        for (float t = 0f; t < fly; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / fly);
            Vector3 to = _panL.position + transform.up * 0.2f;
            flying.position = Vector3.Lerp(from, to, k) + transform.up * Mathf.Sin(k * Mathf.PI) * 0.8f;
            flying.rotation = Quaternion.Slerp(r0, transform.rotation, k);
            flying.localScale = Vector3.Lerp(s0, s1, k);
            yield return null;
        }
        flying.SetParent(_panL, true);
        flying.position = _panL.position + transform.up * 0.2f;
        flying.rotation = transform.rotation;
        yield return new WaitForSeconds(0.5f);

        // 2. La balanza se inclina (con un pequeño vaivén amortiguado): ángulo + = baja el izquierdo (el corazón).
        float target = Mathf.Lerp(MaxTilt, -MaxTilt, Mathf.Clamp01(score01));
        const float settle = 2.6f;
        for (float t = 0f; t < settle; t += Time.deltaTime)
        {
            _angle = target * (1f - Mathf.Exp(-2.6f * t) * Mathf.Cos(6f * t));
            yield return null;
        }
        _angle = target;

        yield return new WaitForSeconds(0.8f);   // (sin texto de veredicto: la balanza ya lo dice)
    }

    /// <summary>
    /// Copia en el mundo del corazón de la mano («HeartBeat», que se ve con la ViewmodelCamera), en el punto de la pantalla
    /// donde se veía y con su tamaño aparente. El de la mano se esconde (vuelve con HideArm(player, false)).
    /// </summary>
    private Transform TakeHeldHeart(Transform player, Camera cam)
    {
        if (player == null || cam == null) return null;
        Transform held = FindDeep(player, "HeartBeat");
        Transform vmT = FindDeep(player, "ViewmodelCamera");
        Camera vm = vmT != null ? vmT.GetComponent<Camera>() : null;
        if (held == null || vm == null) return null;

        GameObject copy = Instantiate(held.gameObject);
        copy.name = "Corazón del jugador";
        foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);   // (solo el modelo y su Animator)
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        Vector3 sp = vm.WorldToScreenPoint(held.position);
        const float dist = 1.1f;
        copy.transform.position = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, dist));
        copy.transform.rotation = cam.transform.rotation * (Quaternion.Inverse(vm.transform.rotation) * held.rotation);
        float k = dist / Mathf.Max(0.01f, sp.z) *
                  (Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(vm.fieldOfView * 0.5f * Mathf.Deg2Rad));
        copy.transform.localScale = held.lossyScale * k;
        copy.transform.SetParent(transform, true);

        _hiddenArm.Clear();
        foreach (Renderer r in held.GetComponentsInChildren<Renderer>())
            if (r.enabled) { r.enabled = false; _hiddenArm.Add(r); }
        return copy.transform;
    }

    /// <summary>Escala con la que el objeto mide 'size' metros (su lado más largo).</summary>
    private static Vector3 TargetScale(Transform t, float size)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return t.localScale;
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        return m > 0.0001f ? t.localScale * (size / m) : t.localScale;
    }

    /// <summary>Oculta (o vuelve a mostrar) el brazo con el corazón de la vista del jugador.</summary>
    public void HideArm(Transform player, bool hide)
    {
        if (hide)
        {
            _hiddenArm.Clear();
            Transform arm = player != null ? FindDeep(player, "ArmRig") : null;
            if (arm == null) return;
            foreach (Renderer r in arm.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; _hiddenArm.Add(r); }
        }
        else
        {
            foreach (Renderer r in _hiddenArm) if (r != null) r.enabled = true;
            _hiddenArm.Clear();
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    private static readonly Dictionary<(Color, float), Material> Mats = new Dictionary<(Color, float), Material>();

    private static GameObject P(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Color c,
                                float glow = 0f, float rotZ = 0f)
    {
        GameObject g = RuntimeMaterials.Primitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        g.transform.localScale = scale;
        Material m;
        if (MaterialProvider != null) m = MaterialProvider(c, glow);
        else if (!Mats.TryGetValue((c, glow), out m) || m == null) Mats[(c, glow)] = m = RuntimeMaterials.Colored(c, glow);
        g.GetComponent<Renderer>().sharedMaterial = m;
        if (Application.isPlaying) Object.Destroy(g.GetComponent<Collider>());
        else Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }
}
