using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Catálogo de la tienda. Si existe un asset 'ShopCatalog' en una carpeta Resources se usa ese (editable desde el
/// Inspector); si no, se usa el catálogo por defecto definido aquí.
/// </summary>
[CreateAssetMenu(fileName = "ShopCatalog", menuName = "Tienda/Catálogo")]
public class ShopCatalog : ScriptableObject
{
    public List<ShopItem> items = new List<ShopItem>();

    private static ShopCatalog _cached;

    public static ShopCatalog Load()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<ShopCatalog>("ShopCatalog");
        if (_cached == null || _cached.items == null || _cached.items.Count == 0) _cached = BuildDefault();
        return _cached;
    }

    public ShopItem Find(string id)
    {
        foreach (var it in items) if (it != null && it.id == id) return it;
        return null;
    }

    /// <summary>Catálogo por defecto (precios orientativos; ajústalos en el asset).</summary>
    public static ShopCatalog BuildDefault()
    {
        var c = CreateInstance<ShopCatalog>();
        c.name = "ShopCatalog (por defecto)";
        Color red = new Color(0.78f, 0.1f, 0.16f), blue = new Color(0.2f, 0.5f, 0.9f),
              gold = new Color(0.95f, 0.72f, 0.15f), green = new Color(0.25f, 0.7f, 0.35f),
              purple = new Color(0.55f, 0.3f, 0.85f);

        c.items.Add(ShopItem.Create(ShopIds.ExtraLife, "Vida extra",
            "Al quedarte sin sangre, revives en vez de morir.",
            ShopItemKind.Consumable, new[] { 150 }, red, 5));
        c.items.Add(ShopItem.Create(ShopIds.Checkpoint, "Checkpoint",
            "Al morir, vuelves al último checkpoint.",
            ShopItemKind.Consumable, new[] { 100 }, green, 5));
        c.items.Add(ShopItem.Create(ShopIds.Shield, "Rebobinado",
            "Al chocar, el tiempo retrocede 4 beats en vez de morir.",
            ShopItemKind.Consumable, new[] { 120 }, blue, 3));

        c.items.Add(ShopItem.Create(ShopIds.SteadyHeart, "Corazón firme",
            "Mantén el clic para no perder sangre.",
            ShopItemKind.Upgrade, new[] { 400 }, red));
        c.items.Add(ShopItem.Create(ShopIds.AutoBeater, "Auto-latido",
            "El corazón late solo cada cierto número de beats.",
            ShopItemKind.Upgrade, new[] { 250, 500, 900, 1500 }, purple, 5,
            new[] { "cada 8 beats", "cada 4 beats", "cada 2 beats", "cada beat" }));
        c.items.Add(ShopItem.Create(ShopIds.BigSyringe, "Jeringuilla grande",
            "Más capacidad de sangre.",
            ShopItemKind.Upgrade, new[] { 200, 400, 700 }, red, 5,
            new[] { "+10 % de capacidad", "+20 % de capacidad", "+30 % de capacidad" }));
        c.items.Add(ShopItem.Create(ShopIds.StrongPulse, "Pulso fuerte",
            "Cada acierto recupera más sangre.",
            ShopItemKind.Upgrade, new[] { 200, 450, 800 }, red, 5,
            new[] { "+15 % por acierto", "+30 % por acierto", "+50 % por acierto" }));
        c.items.Add(ShopItem.Create(ShopIds.WideWindow, "Oído fino",
            "Más margen para acertar el beat.",
            ShopItemKind.Upgrade, new[] { 300, 700 }, blue, 5,
            new[] { "+15 % de margen", "+30 % de margen" }));
        // ── Corazón ──
        c.items.Add(ShopItem.Create(ShopIds.Efficient, "Metabolismo eficiente",
            "Las acciones gastan menos energía.",
            ShopItemKind.Upgrade, new[] { 250, 500, 850 }, red, 5,
            new[] { "−5 % de gasto", "−10 % de gasto", "−15 % de gasto" }));
        c.items.Add(ShopItem.Create(ShopIds.SecondWind, "Segundo aliento",
            "Por debajo del 20 % de energía, los aciertos recuperan el doble.",
            ShopItemKind.Upgrade, new[] { 450 }, red));
        c.items.Add(ShopItem.Create(ShopIds.BlueBoost, "Sangre azul",
            "La sangre azul se gasta más despacio.",
            ShopItemKind.Upgrade, new[] { 300, 650 }, red, 5,
            new[] { "−25 % de gasto azul", "−50 % de gasto azul" }));
        c.items.Add(ShopItem.Create(ShopIds.StartBoost, "Buen desayuno",
            "Empiezas el nivel con sangre azul.",
            ShopItemKind.Upgrade, new[] { 200, 400, 700 }, red, 5,
            new[] { "empiezas con un 20 % de sangre azul", "con un 40 %", "con un 60 %" }));
        // ── Ritmo ──
        c.items.Add(ShopItem.Create(ShopIds.RhythmSight, "Crosshair rítmico",
            "El punto de mira marca cuándo pulsar.",
            ShopItemKind.Upgrade, new[] { 150 }, blue));
        c.items.Add(ShopItem.Create(ShopIds.HalfBeat, "Doble tempo",
            "También puedes acertar en los medios beats.",
            ShopItemKind.Upgrade, new[] { 900 }, blue));
        // ── Supervivencia ──
        c.items.Add(ShopItem.Create(ShopIds.Parachute, "Paracaídas",
            "Una vez por nivel, rebotas en vez de morir al caer.",
            ShopItemKind.Upgrade, new[] { 600 }, green));
        c.items.Add(ShopItem.Create(ShopIds.DrainGuard, "Piel gruesa",
            "Pierdes menos sangre por enemigos y golpes.",
            ShopItemKind.Upgrade, new[] { 350, 750 }, green, 5,
            new[] { "−25 % de sangre perdida", "−50 % de sangre perdida" }));
        c.items.Add(ShopItem.Create(ShopIds.LastStand, "Adrenalina",
            "Una vez por nivel, revives con un 30 % de sangre.",
            ShopItemKind.Upgrade, new[] { 1000 }, green));
        c.items.Add(ShopItem.Create(ShopIds.SlowBleed, "Coagulante",
            "Pierdes sangre más despacio.",
            ShopItemKind.Upgrade, new[] { 300, 700 }, green, 5,
            new[] { "−15 % de pérdida pasiva", "−30 % de pérdida pasiva" }));
        c.items.Add(ShopItem.Create(ShopIds.Streak, "Racha",
            "Con 8 aciertos seguidos, +25 % de sangre por acierto.",
            ShopItemKind.Upgrade, new[] { 450 }, blue));
        c.items.Add(ShopItem.Create(ShopIds.Perfectionist, "Perfeccionista",
            "Los Perfect recuperan ×2 de sangre.",
            ShopItemKind.Upgrade, new[] { 550 }, blue));
        c.items.Add(ShopItem.Create(ShopIds.BarFlash, "Marca de compás",
            "El punto de mira destella al empezar cada compás.",
            ShopItemKind.Upgrade, new[] { 120 }, blue));
        // ── Dinero ──
        c.items.Add(ShopItem.Create(ShopIds.PerfectCoin, "Latido dorado",
            "+1 moneda por cada Perfect.",
            ShopItemKind.Upgrade, new[] { 700 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.BloodGold, "Sangre de oro",
            "Al acabar el nivel, +1 moneda por cada % de energía restante.",
            ShopItemKind.Upgrade, new[] { 500 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.LuckyCoin, "Moneda de la suerte",
            "Cada moneda tiene un 10 % de probabilidad de valer el doble.",
            ShopItemKind.Upgrade, new[] { 400 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.Interest, "Interés",
            "Al acabar el nivel, +5 % de tu monedero (máx. 100).",
            ShopItemKind.Upgrade, new[] { 800 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.ComboCoins, "Racha dorada",
            "+5 monedas cada 16 aciertos seguidos.",
            ShopItemKind.Upgrade, new[] { 600 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.AccuracyPay, "Pago por precisión",
            "Al acabar el nivel, +1 moneda por cada % de precisión.",
            ShopItemKind.Upgrade, new[] { 500 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.Metronome, "Metrónomo",
            "Suena un «tic» en cada beat. [M] lo activa o desactiva.",
            ShopItemKind.Upgrade, new[] { 250 }, blue));
        c.items.Add(ShopItem.Create(ShopIds.CoinMagnet, "Imán de monedas",
            "Atrae las monedas cercanas.",
            ShopItemKind.Upgrade, new[] { 150, 350 }, gold, 5,
            new[] { "radio 5 m", "radio 9 m" }));
        c.items.Add(ShopItem.Create(ShopIds.CoinBonus, "Bolsa de oro",
            "×1.5 monedas si acabas con más del 80 % de precisión.",
            ShopItemKind.Upgrade, new[] { 600 }, gold));
        return c;
    }
}
