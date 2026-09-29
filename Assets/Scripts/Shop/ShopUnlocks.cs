using System.Collections.Generic;

/// <summary>
/// ÁRBOL DE DESBLOQUEOS de la tienda: las mejoras más potentes («OP») están BLOQUEADAS hasta que compras otras.
/// Cada requisito es (id de la mejora, nivel mínimo COMPRADO). Cuenta lo comprado aunque esté apagado.
/// Lo que no aparece aquí está disponible desde el principio. Ajusta la tabla a tu gusto.
/// </summary>
public static class ShopUnlocks
{
    public struct Req
    {
        public string id; public int level;
        public Req(string id, int level = 1) { this.id = id; this.level = level; }
    }

    private static readonly Dictionary<string, Req[]> Table = new Dictionary<string, Req[]>
    {
        // ── Corazón ── (libres: Pulso fuerte, Jeringuilla grande, Metabolismo eficiente, Sangre azul)
        { ShopIds.SecondWind,  new[] { new Req(ShopIds.StrongPulse) } },
        { ShopIds.StartBoost,  new[] { new Req(ShopIds.BlueBoost) } },
        { ShopIds.SteadyHeart, new[] { new Req(ShopIds.BigSyringe), new Req(ShopIds.Efficient) } },
        { ShopIds.AutoBeater,  new[] { new Req(ShopIds.SteadyHeart), new Req(ShopIds.StrongPulse, 2) } },   // OP

        // ── Supervivencia ── (libres: Vida extra, Checkpoint, Coágulo, Piel gruesa, Coagulante)
        { ShopIds.Parachute,   new[] { new Req(ShopIds.SlowBleed) } },
        { ShopIds.Shield,      new[] { new Req(ShopIds.DrainGuard) } },                                      // OP (Rebobinado)
        { ShopIds.LastStand,   new[] { new Req(ShopIds.Parachute), new Req(ShopIds.MissGuard) } },          // OP

        // ── Ritmo ── (libres: Metrónomo, Marca de compás, Crosshair rítmico)
        { ShopIds.WideWindow,  new[] { new Req(ShopIds.Metronome) } },
        { ShopIds.CleanPulse,  new[] { new Req(ShopIds.BarFlash) } },
        { ShopIds.Streak,      new[] { new Req(ShopIds.WideWindow) } },
        { ShopIds.Perfectionist, new[] { new Req(ShopIds.Streak) } },
        { ShopIds.HalfBeat,    new[] { new Req(ShopIds.Perfectionist), new Req(ShopIds.CleanPulse) } },     // OP

        // ── Dinero ── (libres: Imán de monedas, Moneda de la suerte, Sangre de oro)
        { ShopIds.AccuracyPay, new[] { new Req(ShopIds.BloodGold) } },
        { ShopIds.ComboCoins,  new[] { new Req(ShopIds.LuckyCoin) } },
        { ShopIds.PerfectCoin, new[] { new Req(ShopIds.ComboCoins) } },
        { ShopIds.CoinBonus,   new[] { new Req(ShopIds.AccuracyPay) } },
        { ShopIds.Interest,    new[] { new Req(ShopIds.CoinBonus), new Req(ShopIds.PerfectCoin) } },        // OP
    };

    public static Req[] RequirementsOf(string id) => Table.TryGetValue(id, out var r) ? r : System.Array.Empty<Req>();

    /// <summary>¿Se puede comprar ya? (todos los requisitos comprados al nivel pedido)</summary>
    public static bool IsUnlocked(string id)
    {
        foreach (Req r in RequirementsOf(id))
            if (PlayerProgress.GetOwnedLevel(r.id) < r.level) return false;
        return true;
    }

    /// <summary>Texto con lo que falta, p. ej. «Requiere: Pulso fuerte (nv. 2), Corazón firme».</summary>
    public static string MissingText(string id)
    {
        var parts = new List<string>();
        ShopCatalog cat = ShopCatalog.Load();
        foreach (Req r in RequirementsOf(id))
        {
            if (PlayerProgress.GetOwnedLevel(r.id) >= r.level) continue;
            ShopItem it = cat.Find(r.id);
            string name = it != null ? it.displayName : r.id;
            parts.Add(r.level > 1 ? $"{name} (nv. {r.level})" : name);
        }
        return parts.Count == 0 ? "" : "Requiere: " + string.Join(", ", parts);
    }
}
