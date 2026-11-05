using System.Collections.Generic;

/// <summary>
/// Muescas, como los amuletos de Hollow Knight. Las mejoras permanentes compradas en la tienda solo funcionan si están
/// equipadas, y cada una ocupa muescas. El jugador empieza con 3 muescas y puede comprar hasta 5 más (8 como máximo),
/// por lo que nunca puede llevar todas las mejoras a la vez y tiene que elegir. Las mejoras más potentes ocupan más
/// muescas, y los consumibles no ocupan ninguna. Aquí se ajusta lo que ocupa cada mejora y el precio de las muescas.
/// </summary>
public static class Notches
{
    public const int BaseNotches = 3;
    public const int MaxNotches  = 8;   // máximo total (3 iniciales y 5 compradas)

    /// <summary>Muescas que ocupa cada mejora. Las que no aparecen ocupan 1.</summary>
    private static readonly Dictionary<string, int> Cost = new Dictionary<string, int>
    {
        { ShopIds.AutoBeater,    3 },
        { ShopIds.HalfBeat,      3 },
        { ShopIds.LastStand,     2 },
        { ShopIds.SteadyHeart,   2 },
        { ShopIds.Interest,      2 },
        { ShopIds.Perfectionist, 2 },
        { ShopIds.CoinBonus,     2 },
    };

    /// <summary>Precio de cada muesca extra (la primera, la segunda...). Hay tantos precios como muescas se pueden comprar.</summary>
    private static readonly int[] Prices = { 250, 450, 750, 1200, 1800 };

    public static int CostOf(string id) => Cost.TryGetValue(id, out int c) ? c : 1;

    public static int Total => UnityEngine.Mathf.Min(MaxNotches, BaseNotches + PlayerProgress.NotchesBought);

    public static bool AtMax => Total >= MaxNotches;

    public static int Used
    {
        get
        {
            int u = 0;
            foreach (string id in PlayerProgress.EquippedUpgrades) u += CostOf(id);
            return u;
        }
    }

    public static int Free => Total - Used;

    public static bool CanEquip(string id) => PlayerProgress.IsUpgradeEnabled(id) || CostOf(id) <= Free;

    /// <summary>Precio de la siguiente muesca (-1 si el jugador ya tiene el máximo).</summary>
    public static int NextNotchPrice
    {
        get
        {
            if (AtMax) return -1;
            int k = PlayerProgress.NotchesBought;
            return Prices[UnityEngine.Mathf.Clamp(k, 0, Prices.Length - 1)];
        }
    }

    public static bool TryBuyNotch()
    {
        if (AtMax || !PlayerProgress.TrySpendCoins(NextNotchPrice)) return false;
        PlayerProgress.AddNotch();
        return true;
    }
}
