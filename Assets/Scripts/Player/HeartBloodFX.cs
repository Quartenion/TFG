using UnityEngine;

/// <summary>
/// SALPICADURA DE SANGRE al apretar el corazón: cada pulsación (clic del jugador o Auto-latido) suelta un chorrito de
/// gotas rojas que salen del corazón, caen con gravedad y se encogen hasta desaparecer.
///
/// Se crea solo por código (no hay que montar nada): HandAnimationController llama a <see cref="Burst"/>.
/// Las gotas son pequeñas esferas con material URP/Lit (de Resources, así no salen rosas en el juego exportado),
/// en la misma capa que el corazón para que las pinte la cámara de la mano. Viajan con la mano (espacio local),
/// así no se quedan atrás cuando el jugador corre.
/// </summary>
public class HeartBloodFX : MonoBehaviour
{
    public const string ObjectName = "HeartBloodFX";

    [Tooltip("Gotas por pulsación (mín., máx.).")]
    public Vector2Int dropsPerPress = new Vector2Int(9, 14);
    [Tooltip("Tamaño de las gotas respecto al corazón.")]
    public float dropSize = 0.07f;
    [Tooltip("Velocidad de salida respecto al tamaño del corazón (corazones por segundo).")]
    public float speed = 2.6f;
    [Tooltip("Gravedad de las gotas (tamaños de corazón por segundo²).")]
    public float gravity = 9f;
    [Tooltip("Color de la sangre.")]
    public Color bloodColor = new Color(0.42f, 0.0f, 0.02f, 1f);

    private ParticleSystem _ps;
    private Transform _heart;
    private Renderer[] _heartRenderers;
    private float _heartSize = 0.1f;

    /// <summary>Busca (o crea) el efecto del corazón y suelta un chorrito.</summary>
    public static void Burst(Animator heartAnimator)
    {
        if (heartAnimator == null || !heartAnimator.isActiveAndEnabled) return;
        if (Time.timeScale <= 0f || HudHider.IsHidden) return;   // (en pausa / menús no)
        Transform heart = heartAnimator.transform;
        Transform parent = heart.parent != null ? heart.parent : heart;
        Transform t = parent.Find(ObjectName);
        HeartBloodFX fx = t != null ? t.GetComponent<HeartBloodFX>() : null;
        if (fx == null) fx = Create(heart, parent);
        fx.Emit();
    }

    /// <summary>Crea ya el efecto y suelta una gota diminuta (casi invisible) para que se prepare su shader.</summary>
    public static void Prewarm(Animator heartAnimator)
    {
        if (heartAnimator == null) return;
        Transform heart = heartAnimator.transform;
        Transform parent = heart.parent != null ? heart.parent : heart;
        Transform t = parent.Find(ObjectName);
        HeartBloodFX fx = t != null ? t.GetComponent<HeartBloodFX>() : null;
        if (fx == null) fx = Create(heart, parent);
        if (fx._ps == null) return;
        var ep = new ParticleSystem.EmitParams { position = Vector3.zero, startSize = 0.0001f, startLifetime = 0.05f,
                                                 velocity = Vector3.zero, applyShapeToPosition = false };
        fx._ps.Emit(ep, 1);
    }

    private static HeartBloodFX Create(Transform heart, Transform parent)
    {
        var go = new GameObject(ObjectName);
        go.layer = heart.gameObject.layer;
        go.transform.SetParent(parent, false);
        var fx = go.AddComponent<HeartBloodFX>();
        fx._heart = heart;
        fx._heartRenderers = heart.GetComponentsInChildren<Renderer>(true);
        fx.Build();
        return fx;
    }

    private void Build()
    {
        _ps = gameObject.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startColor = bloodColor;
        main.maxParticles = 200;

        var emission = _ps.emission;
        emission.enabled = false;   // solo ráfagas con Emit()

        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;

        // Se encogen hasta desaparecer (el material es opaco: así «se van» sin transparencias).
        var sol = _ps.sizeOverLifetime;
        sol.enabled = true;
        var curve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var r = GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = SphereMesh();
        Material m = RuntimeMaterials.Colored(bloodColor, 0.35f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);   // brillo de líquido
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private static Mesh _sphere;
    private static Mesh SphereMesh()
    {
        if (_sphere != null) return _sphere;
        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        return _sphere;
    }

    private void Emit()
    {
        if (_ps == null) return;

        // Centro y tamaño del corazón en este instante (en el espacio del efecto).
        Vector3 centerW = _heart != null ? _heart.position : transform.position;
        float sizeW = _heartSize;
        bool any = false; Bounds b = default;
        if (_heartRenderers != null)
            foreach (Renderer hr in _heartRenderers)
            {
                if (hr == null || !hr.enabled || !hr.gameObject.activeInHierarchy) continue;
                if (!any) { b = hr.bounds; any = true; } else b.Encapsulate(hr.bounds);
            }
        if (any) { centerW = b.center; sizeW = b.size.magnitude * 0.5f; _heartSize = sizeW; }

        transform.position = centerW;
        transform.rotation = Quaternion.identity;
        // Escala del efecto = 1 en el mundo (aunque la mano esté escalada): así todo va en metros.
        Vector3 ps = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(1f / Mathf.Max(Mathf.Abs(ps.x), 1e-4f),
                                           1f / Mathf.Max(Mathf.Abs(ps.y), 1e-4f),
                                           1f / Mathf.Max(Mathf.Abs(ps.z), 1e-4f));

        var main = _ps.main;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeW * dropSize * 0.6f, sizeW * dropSize * 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(sizeW * speed * 0.5f, sizeW * speed);
        main.gravityModifier = gravity * sizeW / 9.81f;
        var shape = _ps.shape;
        shape.radius = sizeW * 0.3f;

        _ps.Emit(Random.Range(dropsPerPress.x, dropsPerPress.y + 1));
    }
}
