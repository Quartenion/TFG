using UnityEngine;

/// <summary>
/// MATERIALES DE LOS OBJETOS CREADOS POR CÓDIGO (bloques de la tienda/contratos, arcos de checkpoint, monedas,
/// trozos que saltan al romperse algo, orbes del jefe…).
///
/// GameObject.CreatePrimitive pone el material por defecto de Unity (shader «Standard» del pipeline antiguo). En el
/// editor URP lo arregla, pero en el JUEGO EXPORTADO ese shader no existe → objetos ROSA FOSFORITO. Por eso los
/// primitivos se crean con <see cref="Primitive"/>, que les pone un material URP/Lit de Resources.
/// En Assets/Resources hay además una versión con emisión y una URP/Unlit transparente: así esos shaders y variantes
/// entran en la build aunque solo se usen desde código (Shader.Find, EnableKeyword("_EMISSION")…).
/// </summary>
public static class RuntimeMaterials
{
    private static Material _lit;

    /// <summary>Material URP/Lit blanco (Resources/RuntimeLit). Clónalo para cambiarle el color.</summary>
    public static Material Lit
    {
        get
        {
            if (_lit == null)
            {
                _lit = Resources.Load<Material>("RuntimeLit");
                if (_lit == null) Debug.LogWarning("[RuntimeMaterials] Falta Assets/Resources/RuntimeLit.mat: los objetos creados por código saldrán rosas en el juego exportado.");
            }
            return _lit;
        }
    }

    /// <summary>Como GameObject.CreatePrimitive, pero con un material URP válido también en el juego exportado.</summary>
    public static GameObject Primitive(PrimitiveType type)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        var r = g.GetComponent<Renderer>();
        if (r != null && Lit != null) r.sharedMaterial = Lit;
        return g;
    }

    /// <summary>Material URP/Lit de un color (opcionalmente con emisión).</summary>
    public static Material Colored(Color color, float emission = 0f)
    {
        var m = Lit != null ? new Material(Lit) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.color = color;
        if (emission > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * emission); }
        return m;
    }
}
