using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// TIENDAS EXCAVADAS EN LA PARED (sala final del nivel tutorial). Construye por código, con primitivas, un local de
/// estilo siglo XIX metido dentro del muro: fachada de piedra con el hueco en arco, marco de madera vieja, cartel
/// colgado, nicho con suelo de tablones, vigas y farol encendido, y el mobiliario de cada uno:
///   • TIENDA (botica / ultramarinos): mostrador con balanza, caja registradora y tarros; estanterías con frascos y
///     botellas de colores; barriles, cajas y sacos.
///   • CONTRATOS (notaría / oficina de recompensas): escritorio con libro de cuentas, tintero, pluma y vela; tablón
///     de anuncios con carteles clavados («SE BUSCA»); casillero de pergaminos; arcón con herrajes.
/// La tienda entera es lo interactuable (ver CourseBuilder: se le pone un ShopStand / ContractStand).
///
/// Sistema local: el origen está en el SUELO, en la cara de la pared; +Z entra en el muro, X va a lo largo de él.
/// </summary>
public static class CarvedShop
{
    // ── Paleta ────────────────────────────────────────────────────────────────
    private static readonly Color Stone     = new Color(0.30f, 0.27f, 0.25f);
    private static readonly Color StoneDark = new Color(0.20f, 0.18f, 0.17f);
    private static readonly Color WoodDark  = new Color(0.22f, 0.13f, 0.07f);
    private static readonly Color Wood      = new Color(0.36f, 0.22f, 0.12f);
    private static readonly Color WoodLight = new Color(0.52f, 0.36f, 0.20f);
    private static readonly Color Brass     = new Color(0.72f, 0.55f, 0.22f);
    private static readonly Color Iron      = new Color(0.12f, 0.12f, 0.13f);
    private static readonly Color Paper     = new Color(0.86f, 0.80f, 0.64f);
    private static readonly Color Parchment = new Color(0.78f, 0.68f, 0.48f);
    private static readonly Color Burlap    = new Color(0.62f, 0.52f, 0.36f);
    private static readonly Color Flame     = new Color(1f, 0.62f, 0.25f);

    private static readonly Dictionary<Color, Material> Mats = new Dictionary<Color, Material>();
    private static readonly Dictionary<Color, Material> Glow = new Dictionary<Color, Material>();

    private static Material M(Color c)
    {
        if (!Mats.TryGetValue(c, out Material m) || m == null) Mats[c] = m = RuntimeMaterials.Colored(c);
        return m;
    }

    private static Material E(Color c, float e)
    {
        if (!Glow.TryGetValue(c, out Material m) || m == null) Glow[c] = m = RuntimeMaterials.Colored(c, e);
        return m;
    }

    /// <summary>
    /// Modelos del «Fantasy Props MegaKit» (Assets/3D/Fantasy Props MegaKit[Standard]/Exports/FBX), por nombre de
    /// archivo. Los rellena CourseBuilder (campo Props Kit). Sin ellos se usan las piezas hechas con primitivas.
    /// </summary>
    public static Dictionary<string, GameObject> Kit;

    /// <summary>Escala de los modelos del kit (vienen a tamaño real; el jugador y el local son algo más grandes).</summary>
    private const float S = 1.35f;

    /// <summary>Construye el local. 'contracts' = oficina de contratos (si no, la tienda).</summary>
    public static GameObject Build(Transform parent, Vector3 worldPos, Quaternion rot, bool contracts)
    {
        var root = new GameObject(contracts ? "Contratos (excavado)" : "Tienda (excavada)").transform;
        root.SetParent(parent, true);
        root.SetPositionAndRotation(worldPos, rot);

        Shell(root);
        bool kit = Kit != null && Kit.Count > 0;
        if (contracts) { if (kit) ContractsPropsKit(root); else ContractsProps(root); }
        else           { if (kit) ShopPropsKit(root);      else ShopProps(root); }
        Sign(root, contracts ? "CONTRATOS" : "TIENDA", contracts ? "Notaría · Recompensas" : "Ultramarinos · Botica");
        return root.gameObject;
    }

    // =========================================================================
    // FACHADA Y NICHO
    // =========================================================================

    private const float W = 20f, H = 15f;        // hueco de la pared que se rellena
    private const float OW = 11f, OH = 8f;       // boca del local
    private const float D = 7f;                  // fondo del nicho

    private static void Shell(Transform r)
    {
        // Fachada (de piedra, 1 m de grueso como el muro) con la boca en arco rebajado.
        float side = (W - OW) * 0.5f;
        Box(r, "Fachada izq.", new Vector3(-(OW + side) * 0.5f, H * 0.5f, 0.5f), new Vector3(side, H, 1f), Stone);
        Box(r, "Fachada der.", new Vector3((OW + side) * 0.5f, H * 0.5f, 0.5f), new Vector3(side, H, 1f), Stone);
        Box(r, "Dintel", new Vector3(0f, (OH + H) * 0.5f, 0.5f), new Vector3(OW, H - OH, 1f), Stone);
        for (int s = -1; s <= 1; s += 2)   // esquinas achaflanadas del arco
            Box(r, "Arco", new Vector3(s * OW * 0.5f, OH, 0.5f), new Vector3(2.6f, 2.6f, 1f), Stone, rotZ: 45f);
        // Sillares marcados en la fachada (juntas más oscuras).
        for (int row = 1; row < 7; row++)
            for (int s = -1; s <= 1; s += 2)
                Box(r, "Junta", new Vector3(s * (OW * 0.5f + side * 0.5f), row * 2.1f, -0.02f), new Vector3(side - 0.4f, 0.08f, 0.05f), StoneDark, col: false);

        // Marco de madera envejecida en la boca.
        for (int s = -1; s <= 1; s += 2)
            Box(r, "Jamba", new Vector3(s * (OW * 0.5f - 0.3f), (OH - 1.3f) * 0.5f, 0.15f), new Vector3(0.5f, OH - 1.3f, 0.5f), WoodDark);
        Box(r, "Travesaño", new Vector3(0f, OH - 1.3f, 0.15f), new Vector3(OW - 1.2f, 0.45f, 0.5f), WoodDark);
        Box(r, "Umbral", new Vector3(0f, 0.06f, 0.6f), new Vector3(OW - 0.4f, 0.12f, 1.2f), StoneDark);

        // Nicho excavado: suelo de tablones, paredes de roca, techo con vigas.
        Box(r, "Suelo", new Vector3(0f, -0.05f, 1f + D * 0.5f), new Vector3(OW, 0.1f, D), Wood);
        for (int i = 0; i < 9; i++)
            Box(r, "Tablón", new Vector3(-OW * 0.5f + 0.6f + i * 1.22f, 0.01f, 1f + D * 0.5f), new Vector3(0.04f, 0.02f, D), WoodDark, col: false);
        Box(r, "Fondo", new Vector3(0f, OH * 0.6f, 1f + D + 0.25f), new Vector3(OW + 1f, OH * 1.2f, 0.5f), StoneDark);
        for (int s = -1; s <= 1; s += 2)
            Box(r, "Pared nicho", new Vector3(s * (OW * 0.5f + 0.25f), OH * 0.6f, 1f + D * 0.5f), new Vector3(0.5f, OH * 1.2f, D), StoneDark);
        Box(r, "Techo", new Vector3(0f, OH + 0.25f, 1f + D * 0.5f), new Vector3(OW + 1f, 0.5f, D + 0.5f), StoneDark);
        for (int i = 0; i < 3; i++)
            Box(r, "Viga", new Vector3(0f, OH - 0.2f, 2.2f + i * 2.4f), new Vector3(OW, 0.4f, 0.45f), WoodDark, col: false);

        // Lámpara del techo (luz cálida) y dos apliques en la fachada: los del kit si están; si no, de primitivas.
        if (Prop(r, "Chandelier", new Vector3(0f, OH, 1f + D * 0.45f)) != null)
            AddLight(r, new Vector3(0f, OH - 1.6f, 1f + D * 0.45f), 14f, 2.4f);
        else
            Lantern(r, new Vector3(0f, OH - 2.6f, 1f + D * 0.45f), 1.6f, 14f, 2.2f);
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * (OW * 0.5f + 1.2f);
            if (Prop(r, "Lantern_Wall", new Vector3(x, 3.6f, 0f)) != null)
                AddLight(r, new Vector3(x, 5.0f, -1.2f), 7f, 1.2f);
            else
            {
                Box(r, "Soporte aplique", new Vector3(x, 5.2f, -0.3f), new Vector3(0.12f, 0.12f, 0.6f), Iron, col: false);
                Lantern(r, new Vector3(x, 4.8f, -0.6f), 0f, 7f, 1.2f);
            }
            // Estandartes colgando a los lados de la boca.
            Prop(r, s < 0 ? "Banner_1" : "Banner_2", new Vector3(s * (OW * 0.5f + 3.6f), 12.5f, 0f), scale: 1.6f);
        }
    }

    /// <summary>Cartel de madera colgado de dos cadenas sobre la boca, con el nombre en letras doradas.</summary>
    private static void Sign(Transform r, string title, string subtitle)
    {
        float y = OH + 2.2f;
        for (int s = -1; s <= 1; s += 2)
        {
            Box(r, "Ménsula", new Vector3(s * 3.2f, OH + 3.6f, -0.5f), new Vector3(0.15f, 0.15f, 1.0f), Iron, col: false);
            Box(r, "Cadena", new Vector3(s * 3.2f, OH + 3.0f, -0.95f), new Vector3(0.06f, 1.2f, 0.06f), Iron, col: false);
        }
        Box(r, "Cartel", new Vector3(0f, y, -0.95f), new Vector3(7.4f, 1.9f, 0.18f), WoodDark);
        Box(r, "Marco cartel", new Vector3(0f, y, -1.02f), new Vector3(7.0f, 1.55f, 0.04f), Wood, col: false);
        Text(r, title, new Vector3(0f, y + 0.18f, -1.06f), 7.0f, 1.1f, 9f, Brass);
        Text(r, subtitle, new Vector3(0f, y - 0.5f, -1.06f), 6.5f, 0.5f, 3f, Paper);
    }

    // =========================================================================
    // TIENDA
    // =========================================================================

    private static void ShopProps(Transform r)
    {
        var rng = new System.Random(1847);
        // Mostrador con paneles, encimera, balanza, caja registradora y tarros.
        float cz = 3.6f;
        Box(r, "Mostrador", new Vector3(0f, 0.55f, cz), new Vector3(7.5f, 1.1f, 1.2f), Wood);
        Box(r, "Encimera", new Vector3(0f, 1.16f, cz), new Vector3(7.9f, 0.12f, 1.5f), WoodLight);
        for (int i = -1; i <= 1; i++)
            Box(r, "Panel", new Vector3(i * 2.4f, 0.55f, cz - 0.61f), new Vector3(2.0f, 0.75f, 0.03f), WoodDark, col: false);
        // Balanza de platillos.
        Cyl(r, "Balanza base", new Vector3(-2.4f, 1.27f, cz), new Vector3(0.45f, 0.05f, 0.45f), Brass);
        Box(r, "Balanza fiel", new Vector3(-2.4f, 1.65f, cz), new Vector3(0.06f, 0.75f, 0.06f), Brass);
        Box(r, "Balanza brazo", new Vector3(-2.4f, 2.0f, cz), new Vector3(1.1f, 0.05f, 0.05f), Brass);
        for (int s = -1; s <= 1; s += 2)
        {
            Box(r, "Hilo", new Vector3(-2.4f + s * 0.52f, 1.75f, cz), new Vector3(0.02f, 0.5f, 0.02f), Brass);
            Cyl(r, "Platillo", new Vector3(-2.4f + s * 0.52f, 1.5f, cz), new Vector3(0.38f, 0.02f, 0.38f), Brass);
        }
        // Caja registradora.
        Box(r, "Caja", new Vector3(2.5f, 1.5f, cz + 0.1f), new Vector3(0.95f, 0.55f, 0.7f), Brass);
        Box(r, "Caja teclado", new Vector3(2.5f, 1.32f, cz - 0.38f), new Vector3(0.9f, 0.18f, 0.35f), Iron, rotX: -25f);
        Box(r, "Caja cartela", new Vector3(2.5f, 1.95f, cz + 0.15f), new Vector3(0.7f, 0.35f, 0.08f), Brass);
        // Tarros de caramelos en el mostrador.
        Color[] glass = { new Color(0.9f, 0.55f, 0.2f), new Color(0.35f, 0.7f, 0.4f), new Color(0.75f, 0.3f, 0.35f) };
        for (int i = 0; i < 3; i++)
        {
            Cyl(r, "Tarro", new Vector3(-0.8f + i * 0.8f, 1.47f, cz), new Vector3(0.36f, 0.25f, 0.36f), glass[i], glow: 0.25f);
            Cyl(r, "Tapa", new Vector3(-0.8f + i * 0.8f, 1.75f, cz), new Vector3(0.3f, 0.04f, 0.3f), Brass);
        }

        // Estanterías del fondo con frascos y botellas.
        float bz = 1f + D - 0.4f;
        for (int x = -1; x <= 1; x++)
            Box(r, "Montante", new Vector3(x * 4.6f, 3.6f, bz), new Vector3(0.18f, 7.2f, 0.7f), WoodDark);
        Color[] bottles = { new Color(0.55f, 0.32f, 0.12f), new Color(0.2f, 0.45f, 0.25f), new Color(0.18f, 0.3f, 0.55f),
                            new Color(0.6f, 0.15f, 0.15f), new Color(0.75f, 0.65f, 0.35f) };
        for (int shelf = 0; shelf < 4; shelf++)
        {
            float y = 1.3f + shelf * 1.45f;
            Box(r, "Balda", new Vector3(0f, y, bz), new Vector3(9.4f, 0.12f, 0.75f), Wood);
            for (float x = -4.3f; x < 4.3f; x += 0.42f + (float)rng.NextDouble() * 0.25f)
            {
                if (Mathf.Abs(x) < 0.25f || Mathf.Abs(Mathf.Abs(x) - 4.6f) < 0.25f) continue;
                Color c = bottles[rng.Next(bottles.Length)];
                if (rng.NextDouble() < 0.5)
                {   // botella: cuerpo + cuello
                    float h = 0.45f + (float)rng.NextDouble() * 0.25f;
                    Cyl(r, "Botella", new Vector3(x, y + 0.06f + h * 0.5f, bz), new Vector3(0.2f, h * 0.5f, 0.2f), c, glow: 0.15f);
                    Cyl(r, "Cuello", new Vector3(x, y + 0.06f + h + 0.1f, bz), new Vector3(0.08f, 0.1f, 0.08f), c, glow: 0.15f);
                }
                else
                {   // frasco ancho con etiqueta
                    Cyl(r, "Frasco", new Vector3(x, y + 0.26f, bz), new Vector3(0.32f, 0.2f, 0.32f), c, glow: 0.15f);
                    Box(r, "Etiqueta", new Vector3(x, y + 0.26f, bz - 0.165f), new Vector3(0.18f, 0.14f, 0.01f), Paper, col: false);
                }
            }
        }

        // Barriles, cajas y sacos.
        Barrel(r, new Vector3(-4.3f, 0f, 2.0f));
        Barrel(r, new Vector3(-4.4f, 0f, 3.6f));
        Box(r, "Caja madera", new Vector3(4.3f, 0.5f, 2.0f), new Vector3(1.0f, 1.0f, 1.0f), WoodLight);
        Box(r, "Caja madera", new Vector3(4.35f, 1.35f, 2.1f), new Vector3(0.75f, 0.7f, 0.75f), Wood, rotY: 18f);
        Box(r, "Caja madera", new Vector3(4.2f, 0.4f, 3.4f), new Vector3(0.9f, 0.8f, 0.9f), Wood, rotY: -10f);
        for (int i = 0; i < 3; i++)
            Sph(r, "Saco", new Vector3(-2.9f + i * 0.65f, 0.38f, 6.3f), new Vector3(0.75f, 0.75f, 0.6f), Burlap);
    }

    private static void Barrel(Transform r, Vector3 p)
    {
        Cyl(r, "Barril", p + new Vector3(0f, 0.7f, 0f), new Vector3(1.05f, 0.7f, 1.05f), Wood);
        foreach (float y in new[] { 0.25f, 0.7f, 1.15f })
            Cyl(r, "Aro", p + new Vector3(0f, y, 0f), new Vector3(1.1f, 0.04f, 1.1f), Iron, col: false);
        Cyl(r, "Tapa barril", p + new Vector3(0f, 1.41f, 0f), new Vector3(0.95f, 0.02f, 0.95f), WoodDark, col: false);
    }

    // =========================================================================
    // CONTRATOS
    // =========================================================================

    private static void ContractsProps(Transform r)
    {
        var rng = new System.Random(1862);
        // Escritorio del notario.
        float dz = 3.6f;
        Box(r, "Escritorio", new Vector3(0f, 0.5f, dz), new Vector3(6.2f, 1.0f, 1.4f), WoodDark);
        Box(r, "Tablero", new Vector3(0f, 1.06f, dz), new Vector3(6.6f, 0.1f, 1.7f), Wood);
        Box(r, "Tapete", new Vector3(0f, 1.115f, dz), new Vector3(3.2f, 0.01f, 1.1f), new Color(0.18f, 0.3f, 0.2f), col: false);
        // Libro de cuentas abierto.
        Box(r, "Libro izq.", new Vector3(-0.42f, 1.15f, dz), new Vector3(0.8f, 0.05f, 1.0f), Paper, rotZ: 4f, col: false);
        Box(r, "Libro der.", new Vector3(0.42f, 1.15f, dz), new Vector3(0.8f, 0.05f, 1.0f), Paper, rotZ: -4f, col: false);
        Box(r, "Tapas", new Vector3(0f, 1.12f, dz), new Vector3(1.75f, 0.03f, 1.08f), new Color(0.35f, 0.08f, 0.06f), col: false);
        // Tintero y pluma.
        Cyl(r, "Tintero", new Vector3(1.6f, 1.22f, dz - 0.2f), new Vector3(0.22f, 0.1f, 0.22f), Iron);
        Box(r, "Pluma", new Vector3(1.66f, 1.55f, dz - 0.15f), new Vector3(0.05f, 0.75f, 0.02f), Paper, rotZ: -25f, col: false);
        // Sello y lacre.
        Cyl(r, "Sello", new Vector3(-1.7f, 1.3f, dz - 0.2f), new Vector3(0.12f, 0.18f, 0.12f), Brass);
        Cyl(r, "Lacre", new Vector3(-1.4f, 1.13f, dz - 0.35f), new Vector3(0.14f, 0.02f, 0.14f), new Color(0.6f, 0.08f, 0.08f), col: false);
        // Vela encendida.
        Cyl(r, "Palmatoria", new Vector3(2.5f, 1.14f, dz + 0.3f), new Vector3(0.3f, 0.03f, 0.3f), Brass, col: false);
        Cyl(r, "Vela", new Vector3(2.5f, 1.4f, dz + 0.3f), new Vector3(0.1f, 0.25f, 0.1f), Paper, col: false);
        Sph(r, "Llama", new Vector3(2.5f, 1.72f, dz + 0.3f), new Vector3(0.07f, 0.13f, 0.07f), Flame, glow: 4f);
        AddLight(r, new Vector3(2.5f, 1.9f, dz + 0.3f), 6f, 1.1f);
        // Silla.
        Box(r, "Asiento", new Vector3(0f, 0.55f, dz + 1.3f), new Vector3(0.9f, 0.1f, 0.9f), Wood);
        Box(r, "Respaldo", new Vector3(0f, 1.15f, dz + 1.7f), new Vector3(0.9f, 1.1f, 0.1f), Wood);

        // Tablón de anuncios con carteles clavados.
        float bz = 1f + D - 0.15f;
        Box(r, "Tablón", new Vector3(0f, 4.2f, bz), new Vector3(8.4f, 4.2f, 0.15f), new Color(0.45f, 0.32f, 0.2f));
        Box(r, "Marco tablón", new Vector3(0f, 4.2f, bz + 0.02f), new Vector3(8.8f, 4.6f, 0.12f), WoodDark, col: false);
        for (int i = 0; i < 9; i++)
        {
            float x = -3.4f + (i % 5) * 1.7f + (float)(rng.NextDouble() - 0.5) * 0.3f;
            float y = 5.2f - (i / 5) * 2.0f + (float)(rng.NextDouble() - 0.5) * 0.3f;
            bool wanted = i == 2 || i == 6;
            Box(r, "Cartel", new Vector3(x, y, bz - 0.09f), new Vector3(wanted ? 1.3f : 1.0f, wanted ? 1.7f : 1.3f, 0.02f),
                wanted ? Parchment : Paper, rotZ: (float)(rng.NextDouble() - 0.5) * 10f, col: false);
            Sph(r, "Chincheta", new Vector3(x, y + (wanted ? 0.75f : 0.55f), bz - 0.11f), new Vector3(0.08f, 0.08f, 0.08f), new Color(0.7f, 0.1f, 0.1f), col: false);
            if (wanted)
            {
                Text(r, "SE BUSCA", new Vector3(x, y + 0.5f, bz - 0.105f), 1.2f, 0.3f, 2.2f, new Color(0.25f, 0.12f, 0.06f));
                Text(r, "RECOMPENSA", new Vector3(x, y - 0.55f, bz - 0.105f), 1.2f, 0.25f, 1.4f, new Color(0.35f, 0.15f, 0.08f));
                Box(r, "Retrato", new Vector3(x, y, bz - 0.1f), new Vector3(0.6f, 0.6f, 0.01f), new Color(0.35f, 0.28f, 0.2f), col: false);
            }
        }

        // Casillero de pergaminos (lateral derecho).
        float sx = OW * 0.5f - 0.6f;
        Box(r, "Casillero", new Vector3(sx, 2.0f, 5.5f), new Vector3(0.9f, 4.0f, 2.6f), WoodDark);
        for (int row = 0; row < 5; row++)
            for (int c = 0; c < 3; c++)
                Cyl(r, "Pergamino", new Vector3(sx - 0.35f, 0.45f + row * 0.78f, 4.6f + c * 0.85f),
                    new Vector3(0.22f, 0.3f, 0.22f), Parchment, rotZ: 90f, col: false);

        // Arcón con herrajes (lateral izquierdo) y archivador.
        float ax = -OW * 0.5f + 1.2f;
        Box(r, "Arcón", new Vector3(ax, 0.45f, 2.4f), new Vector3(1.5f, 0.9f, 1.0f), Wood);
        Box(r, "Tapa arcón", new Vector3(ax, 0.98f, 2.4f), new Vector3(1.55f, 0.18f, 1.05f), WoodDark);
        for (int s = -1; s <= 1; s += 2)
            Box(r, "Herraje", new Vector3(ax + s * 0.5f, 0.6f, 1.88f), new Vector3(0.1f, 0.9f, 0.04f), Brass, col: false);
        Box(r, "Cerradura", new Vector3(ax, 0.75f, 1.88f), new Vector3(0.18f, 0.22f, 0.04f), Brass, col: false);
        Box(r, "Archivador", new Vector3(ax, 1.3f, 5.6f), new Vector3(1.0f, 2.6f, 1.6f), Wood);
        for (int i = 0; i < 4; i++)
        {
            Box(r, "Cajón", new Vector3(ax + 0.51f, 0.45f + i * 0.6f, 5.6f), new Vector3(0.03f, 0.5f, 1.4f), WoodLight, col: false);
            Box(r, "Tirador", new Vector3(ax + 0.55f, 0.45f + i * 0.6f, 5.6f), new Vector3(0.05f, 0.06f, 0.3f), Brass, col: false);
        }
    }

    // =========================================================================
    // CON LOS MODELOS DEL KIT
    // =========================================================================

    /// <summary>Tienda con los modelos del kit: mostrador lleno, estanterías con pociones, barriles, cajas, armas…</summary>
    private static void ShopPropsKit(Transform r)
    {
        float cz = 3.4f, top = 0.81f * S;   // mostrador (Table_Large) y altura de su tablero
        Prop(r, "Table_Large", new Vector3(0f, 0f, cz), collider: true);
        Prop(r, "Coin_Pile", new Vector3(-1.3f, top, cz - 0.1f));
        Prop(r, "Coin_Pile_2", new Vector3(-0.9f, top, cz + 0.2f), 30f);
        Prop(r, "Potion_2", new Vector3(-0.4f, top, cz - 0.1f));
        Prop(r, "Potion_4", new Vector3(-0.1f, top, cz + 0.15f));
        Prop(r, "Potion_1", new Vector3(0.15f, top, cz - 0.2f));
        Prop(r, "Pouch_Large", new Vector3(0.6f, top, cz), -20f);
        Prop(r, "Key_Gold", new Vector3(0.95f, top, cz - 0.25f), 40f);
        Prop(r, "CandleStick_Triple", new Vector3(1.4f, top, cz + 0.25f));
        AddLight(r, new Vector3(1.4f, top + 0.8f, cz + 0.25f), 5f, 0.8f);
        Prop(r, "Book_7", new Vector3(-1.75f, top, cz + 0.25f), 15f);

        // Pared del fondo: estanterías con frascos, el arco con botellas y un mueble bajo.
        float bz = 1f + D;
        Prop(r, "Cabinet", new Vector3(0f, 0f, bz), collider: true);
        Prop(r, "Shelf_Arch", new Vector3(0f, 3.4f, bz));
        foreach (float y in new[] { 2.3f, 3.6f, 4.9f })
        {
            Prop(r, "Shelf_Small_Bottles", new Vector3(-3.2f, y, bz));
            Prop(r, "Shelf_Simple", new Vector3(3.2f, y, bz));
            float st = y + 0.15f;
            Prop(r, "Potion_1", new Vector3(2.6f, st, bz - 0.2f));
            Prop(r, "Bottle_1", new Vector3(3.0f, st, bz - 0.2f));
            Prop(r, "Potion_2", new Vector3(3.4f, st, bz - 0.2f));
            Prop(r, "SmallBottles_1", new Vector3(3.8f, st, bz - 0.2f));
        }

        // Laterales: barriles, cajas, sacos, un caldero y las armas a la venta.
        Prop(r, "Barrel", new Vector3(-4.5f, 0f, 2.0f), collider: true);
        Prop(r, "Barrel_Apples", new Vector3(-4.5f, 0f, 3.3f), collider: true);
        Prop(r, "Crate_Wooden", new Vector3(-4.4f, 0f, 5.2f), 10f, collider: true);
        Prop(r, "Crate_Wooden", new Vector3(-4.3f, 0.93f * S, 5.2f), -15f, 0.9f);
        Prop(r, "FarmCrate_Apple", new Vector3(-3.3f, 0f, 1.7f), 8f);
        Prop(r, "Bag", new Vector3(-3.4f, 0f, 6.6f), 30f);
        Prop(r, "WeaponStand", new Vector3(4.2f, 0f, 5.4f), -90f, collider: true);
        Prop(r, "Shield_Wooden", new Vector3(OW * 0.5f, 3.4f, 3.0f), -90f);
        Prop(r, "Sword_Bronze", new Vector3(OW * 0.5f - 0.1f, 2.0f, 4.0f), -90f);
        Prop(r, "Vase_2", new Vector3(3.6f, 0f, 1.6f), collider: true);
        Prop(r, "Cauldron", new Vector3(3.3f, 0f, 7.0f), collider: true);
        Prop(r, "Rope_2", new Vector3(-2.2f, 0f, 6.8f), 40f);
    }

    /// <summary>Oficina de contratos con los modelos del kit: escritorio, estanterías de libros, tablón, arcón…</summary>
    private static void ContractsPropsKit(Transform r)
    {
        var rng = new System.Random(1862);
        float dz = 3.4f, top = 0.81f * S;
        Prop(r, "Table_Large", new Vector3(0f, 0f, dz), collider: true);
        Prop(r, "Chair_1", new Vector3(0f, 0f, dz + 1.2f), 180f);
        Prop(r, "Book_Stack_1", new Vector3(-1.5f, top, dz + 0.2f), 10f);
        Prop(r, "Book_Stack_2", new Vector3(-1.1f, top, dz + 0.25f), -20f);
        Prop(r, "Book_5", new Vector3(-0.3f, top, dz - 0.1f), 5f);
        Prop(r, "Scroll_1", new Vector3(0.3f, top, dz - 0.15f), 30f);
        Prop(r, "Scroll_2", new Vector3(0.45f, top, dz + 0.2f), -15f);
        Prop(r, "Key_Metal", new Vector3(0.8f, top, dz - 0.25f), 60f);
        Prop(r, "Coin_Pile_2", new Vector3(1.2f, top, dz));
        Prop(r, "CandleStick", new Vector3(1.6f, top, dz + 0.25f));
        AddLight(r, new Vector3(1.6f, top + 0.6f, dz + 0.25f), 5f, 0.9f);

        // Fondo: dos estanterías de libros y el tablón de anuncios en medio.
        float bz = 1f + D;
        for (int s = -1; s <= 1; s += 2)
        {
            Prop(r, "Bookcase_2", new Vector3(s * 4.0f, 0f, bz), collider: true);
            for (int k = 0; k < 3; k++)
                Prop(r, "BookGroup_Medium_" + (k + 1), new Vector3(s * 4.0f, 0.55f + k * 0.8f, bz - 0.25f), 0f, 1.2f);
        }
        float nz = bz - 0.15f;
        Box(r, "Tablón", new Vector3(0f, 4.2f, nz), new Vector3(4.6f, 3.6f, 0.15f), new Color(0.45f, 0.32f, 0.2f));
        Box(r, "Marco tablón", new Vector3(0f, 4.2f, nz + 0.02f), new Vector3(5.0f, 4.0f, 0.12f), WoodDark, col: false);
        for (int i = 0; i < 6; i++)
        {
            float x = -1.5f + (i % 3) * 1.5f + (float)(rng.NextDouble() - 0.5) * 0.25f;
            float y = 5.0f - (i / 3) * 1.7f + (float)(rng.NextDouble() - 0.5) * 0.25f;
            bool wanted = i == 1 || i == 3;
            Box(r, "Cartel", new Vector3(x, y, nz - 0.09f), new Vector3(wanted ? 1.15f : 0.95f, wanted ? 1.45f : 1.2f, 0.02f),
                wanted ? Parchment : Paper, rotZ: (float)(rng.NextDouble() - 0.5) * 10f, col: false);
            Sph(r, "Chincheta", new Vector3(x, y + (wanted ? 0.65f : 0.52f), nz - 0.11f), new Vector3(0.08f, 0.08f, 0.08f), new Color(0.7f, 0.1f, 0.1f), col: false);
            if (wanted)
            {
                Text(r, "SE BUSCA", new Vector3(x, y + 0.42f, nz - 0.105f), 1.05f, 0.28f, 2f, new Color(0.25f, 0.12f, 0.06f));
                Text(r, "RECOMPENSA", new Vector3(x, y - 0.48f, nz - 0.105f), 1.05f, 0.22f, 1.3f, new Color(0.35f, 0.15f, 0.08f));
                Box(r, "Retrato", new Vector3(x, y, nz - 0.1f), new Vector3(0.55f, 0.55f, 0.01f), new Color(0.35f, 0.28f, 0.2f), col: false);
            }
        }

        // Laterales: atril, arcón, jaula, cadenas, taburete y candelabro de pie.
        Prop(r, "BookStand", new Vector3(-3.0f, 0f, 2.4f), 20f, collider: true);
        Prop(r, "Chest_Wood", new Vector3(-4.3f, 0f, 5.6f), 90f, collider: true);
        Prop(r, "CandleStick_Stand", new Vector3(-2.4f, 0f, 6.6f));
        AddLight(r, new Vector3(-2.4f, 1.9f, 6.6f), 6f, 1f);
        Prop(r, "Cage_Small", new Vector3(4.3f, 0f, 2.2f), -20f, collider: true);
        Prop(r, "Chain_Coil", new Vector3(3.2f, 0f, 2.9f));
        Prop(r, "Stool", new Vector3(2.6f, 0f, 5.2f));
        Prop(r, "Pouch_Large", new Vector3(2.6f, 0.59f * S, 5.2f), 25f);
    }

    /// <summary>
    /// Pone un modelo del kit (si está). Los modelos miran a +Z: se giran 180° para que den la cara al jugador, que
    /// mira desde −Z. 'rotY' gira además sobre sí mismo. collider = caja que lo envuelve (para no atravesarlo).
    /// </summary>
    private static GameObject Prop(Transform r, string name, Vector3 p, float rotY = 0f, float scale = 1f, bool collider = false)
    {
        if (Kit == null || !Kit.TryGetValue(name, out GameObject pf) || pf == null) return null;
        GameObject g = Object.Instantiate(pf, r);
        g.name = name;
        g.transform.localPosition = p;
        g.transform.localRotation = Quaternion.Euler(0f, 180f + rotY, 0f) * pf.transform.localRotation;
        g.transform.localScale = pf.transform.localScale * (S * scale);
        if (collider) AddBox(g);
        return g;
    }

    /// <summary>
    /// Pone un modelo del kit en el mundo (decorado de la sala final): 'facing' = hacia dónde mira su frente.
    /// Devuelve null si el modelo no está.
    /// </summary>
    public static GameObject PlaceProp(Transform parent, string name, Vector3 worldPos, Vector3 facing, float scale = 1f, bool collider = false)
    {
        if (Kit == null || !Kit.TryGetValue(name, out GameObject pf) || pf == null) return null;
        GameObject g = Object.Instantiate(pf, parent);
        g.name = name;
        g.transform.position = worldPos;
        g.transform.rotation = Quaternion.LookRotation(facing, Vector3.up) * pf.transform.localRotation;
        g.transform.localScale = pf.transform.localScale * (S * scale);
        if (collider) AddBox(g);
        return g;
    }

    /// <summary>Luz cálida puntual (velas, antorchas…).</summary>
    public static void WarmLight(Transform parent, Vector3 worldPos, float range, float intensity)
    {
        var go = new GameObject("Luz");
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.72f, 0.42f);
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
    }

    /// <summary>BoxCollider que envuelve las mallas del modelo (los del kit no traen colliders).</summary>
    private static void AddBox(GameObject g)
    {
        Renderer[] rs = g.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (Renderer x in rs) b.Encapsulate(x.bounds);
        var box = g.AddComponent<BoxCollider>();
        Vector3 c = g.transform.InverseTransformPoint(b.center);
        // (Medidas en espacio local; el modelo solo está girado en Y, así que basta con intercambiar X/Z si va de lado.)
        Vector3 size = g.transform.InverseTransformVector(b.size);
        box.center = c;
        box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
    }

    // =========================================================================
    // PIEZAS
    // =========================================================================

    private static void Lantern(Transform r, Vector3 p, float chain, float range, float intensity)
    {
        if (chain > 0f)
            Box(r, "Cadena farol", p + new Vector3(0f, 0.35f + chain * 0.5f, 0f), new Vector3(0.05f, chain, 0.05f), Iron, col: false);
        Box(r, "Farol techo", p + new Vector3(0f, 0.38f, 0f), new Vector3(0.5f, 0.08f, 0.5f), Iron, col: false);
        Box(r, "Farol cristal", p, new Vector3(0.38f, 0.6f, 0.38f), Flame, glow: 2.5f, col: false);
        Box(r, "Farol base", p - new Vector3(0f, 0.34f, 0f), new Vector3(0.45f, 0.08f, 0.45f), Iron, col: false);
        AddLight(r, p, range, intensity);
    }

    private static void AddLight(Transform r, Vector3 p, float range, float intensity)
    {
        var go = new GameObject("Luz");
        go.transform.SetParent(r, false);
        go.transform.localPosition = p;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.72f, 0.42f);
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
    }

    private static void Text(Transform r, string s, Vector3 p, float w, float h, float size, Color c)
    {
        var go = new GameObject("Texto " + s, typeof(RectTransform));
        go.transform.SetParent(r, false);
        go.transform.localPosition = p;
        var t = go.AddComponent<TextMeshPro>();
        t.text = s;
        t.fontSize = size;
        t.enableAutoSizing = true; t.fontSizeMin = 0.5f; t.fontSizeMax = size;
        t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold;
        t.color = c;
        t.rectTransform.sizeDelta = new Vector2(w, h);
    }

    private static GameObject Box(Transform r, string n, Vector3 p, Vector3 s, Color c,
                                  float rotX = 0f, float rotY = 0f, float rotZ = 0f, float glow = 0f, bool col = true)
        => Piece(PrimitiveType.Cube, r, n, p, s, c, Quaternion.Euler(rotX, rotY, rotZ), glow, col);

    private static GameObject Cyl(Transform r, string n, Vector3 p, Vector3 s, Color c,
                                  float rotZ = 0f, float glow = 0f, bool col = true)
        => Piece(PrimitiveType.Cylinder, r, n, p, s, c, Quaternion.Euler(0f, 0f, rotZ), glow, col);

    private static GameObject Sph(Transform r, string n, Vector3 p, Vector3 s, Color c, float glow = 0f, bool col = true)
        => Piece(PrimitiveType.Sphere, r, n, p, s, c, Quaternion.identity, glow, col);

    private static GameObject Piece(PrimitiveType type, Transform r, string n, Vector3 p, Vector3 s, Color c, Quaternion rot, float glow, bool col)
    {
        GameObject g = RuntimeMaterials.Primitive(type);
        g.name = n;
        g.transform.SetParent(r, false);
        g.transform.localPosition = p;
        g.transform.localRotation = rot;
        g.transform.localScale = s;
        g.GetComponent<Renderer>().sharedMaterial = glow > 0f ? E(c, glow) : M(c);
        if (!col) Object.Destroy(g.GetComponent<Collider>());
        return g;
    }
}
