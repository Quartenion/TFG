using UnityEngine;

/// <summary>
/// Genera dos brazos placeholder (capsulas simples) como hijos de la camara
/// y los anima entre posicion de reposo y posicion de bloqueo (X en pantalla).
///
/// SETUP:
///   1. Crea un GameObject vacio hijo de la camara del jugador (p.ej. "ArmRig").
///   2. Añade este componente a ese GameObject.
///   3. Asigna el ArmPlaceholder al campo armPlaceholder de BlockAbility.
///
/// SUSTITUCION POR MODELOS REALES:
///   Cuando tengas los modelos de brazos, desactiva este componente y conecta
///   tu propio animator/rig. Puedes usar los valores de posicion/rotacion de
///   este script como referencia para los keyframes de animacion.
///
/// NOTA SOBRE CLIPPING:
///   Los brazos estan colocados muy cerca de la camara. Si atraviesan paredes,
///   crea una layer "Viewmodel", asignala a los brazos, y añade una segunda
///   camara que solo renderice esa layer con Depth = 1 y Clear Flags = Depth Only.
/// </summary>
public class ArmPlaceholder : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Configuracion publica
    // -------------------------------------------------------------------------

    [Header("Animacion")]
    [Tooltip("Velocidad del lerp entre reposo y bloqueo")]
    public float animSpeed = 14f;

    [Header("Reposo — brazos colgando a los lados")]
    public Vector3 leftRestPos  = new Vector3(-0.30f, -0.38f, 0.60f);
    public Vector3 leftRestRot  = new Vector3( 25f,    0f,    25f);
    public Vector3 rightRestPos = new Vector3( 0.30f, -0.38f, 0.60f);
    public Vector3 rightRestRot = new Vector3( 25f,    0f,   -25f);

    [Header("Bloqueo — X cruzada frente a la camara")]
    public Vector3 leftBlockPos  = new Vector3( 0.13f, -0.04f, 0.48f);
    public Vector3 leftBlockRot  = new Vector3(  0f,    0f,   -52f);
    public Vector3 rightBlockPos = new Vector3(-0.13f, -0.04f, 0.48f);
    public Vector3 rightBlockRot = new Vector3(  0f,    0f,    52f);

    [Header("Apariencia del placeholder")]
    [Tooltip("Color de las capsulas (tono piel por defecto)")]
    public Color armColor = new Color(0.80f, 0.62f, 0.50f, 1f);

    // -------------------------------------------------------------------------
    // Estado interno
    // -------------------------------------------------------------------------

    private Transform _leftArm;
    private Transform _rightArm;
    private bool  _blocking;
    private float _blend;      // 0 = reposo, 1 = bloqueo

    // -------------------------------------------------------------------------
    // Ciclo de vida
    // -------------------------------------------------------------------------

    void Awake()
    {
        _leftArm  = BuildArm("LeftArm_Placeholder");
        _rightArm = BuildArm("RightArm_Placeholder");

        // Posicion inicial sin lerp
        _blend = 0f;
        ApplyPose(_leftArm,  leftRestPos,  leftRestRot);
        ApplyPose(_rightArm, rightRestPos, rightRestRot);
    }

    void LateUpdate()
    {
        // LateUpdate para que se aplique despues del movimiento de camara
        float target = _blocking ? 1f : 0f;
        _blend = Mathf.Lerp(_blend, target, Time.deltaTime * animSpeed);

        ApplyPose(_leftArm,
            Vector3.Lerp(leftRestPos,  leftBlockPos,  _blend),
            Vector3.Lerp(leftRestRot,  leftBlockRot,  _blend));

        ApplyPose(_rightArm,
            Vector3.Lerp(rightRestPos, rightBlockPos, _blend),
            Vector3.Lerp(rightRestRot, rightBlockRot, _blend));
    }

    // -------------------------------------------------------------------------
    // API publica
    // -------------------------------------------------------------------------

    /// <summary>Activa o desactiva la animacion de bloqueo.</summary>
    public void SetBlocking(bool blocking)
    {
        _blocking = blocking;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Transform BuildArm(string objName)
    {
        // Capsula elongada que simula el antebrazo
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = objName;
        go.transform.SetParent(transform, false);

        // Escala: delgado y corto en Y para parecer un antebrazo
        go.transform.localScale = new Vector3(0.07f, 0.20f, 0.07f);

        // Sin collider — es puramente visual
        Destroy(go.GetComponent<Collider>());

        // Material de placeholder con color de piel
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            // Intentar usar URP/Lit; si no existe, fallback a Standard
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = armColor;
                mr.material = mat;
            }
        }

        return go.transform;
    }

    private static void ApplyPose(Transform t, Vector3 localPos, Vector3 localEuler)
    {
        t.localPosition    = localPos;
        t.localEulerAngles = localEuler;
    }
}
