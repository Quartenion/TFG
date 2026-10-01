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
            "Si te quedas sin sangre, en vez de morir vuelves a la carga en el siguiente beat.",
            ShopItemKind.Consumable, new[] { 150 }, red, 5));
        c.items.Add(ShopItem.Create(ShopIds.Checkpoint, "Checkpoint",
            "Al morir, vuelves al último checkpoint que hayas pasado en el nivel (la canción sigue desde ahí).",
            ShopItemKind.Consumable, new[] { 100 }, green, 5));
        c.items.Add(ShopItem.Create(ShopIds.Shield, "Rebobinado",
            "Si chocas contra un obstáculo, el tiempo vuelve 4 beats atrás (a un momento seguro) en vez de morir. " +
            "Contra ataques del jefe o en el kayak, para el golpe.",
            ShopItemKind.Consumable, new[] { 120 }, blue, 3));

        c.items.Add(ShopItem.Create(ShopIds.SteadyHeart, "Corazón firme",
            "Mientras mantienes el clic, el corazón no pierde sangre pasivamente.",
            ShopItemKind.Upgrade, new[] { 400 }, red));
        c.items.Add(ShopItem.Create(ShopIds.AutoBeater, "Auto-latido",
            "Un latido automático acertado cada cierto número de beats.",
            ShopItemKind.Upgrade, new[] { 250, 500, 900, 1500 }, purple, 5,
            new[] { "cada 8 beats", "cada 4 beats", "cada 2 beats", "cada beat" }));
        c.items.Add(ShopItem.Create(ShopIds.BigSyringe, "Jeringuilla grande",
            "Más capacidad de sangre antes de quedarte seco.",
            ShopItemKind.Upgrade, new[] { 200, 400, 700 }, red, 5,
            new[] { "+10 % de capacidad", "+20 % de capacidad", "+30 % de capacidad" }));
        c.items.Add(ShopItem.Create(ShopIds.StrongPulse, "Pulso fuerte",
            "Cada acierto devuelve más sangre al corazón.",
            ShopItemKind.Upgrade, new[] { 200, 450, 800 }, red, 5,
            new[] { "+15 % por acierto", "+30 % por acierto", "+50 % por acierto" }));
        c.items.Add(ShopItem.Create(ShopIds.WideWindow, "Oído fino",
            "La ventana para acertar el beat es un poco más amplia.",
            ShopItemKind.Upgrade, new[] { 300, 700 }, blue, 5,
            new[] { "+15 % de margen", "+30 % de margen" }));
        // ── Corazón ──
        c.items.Add(ShopItem.Create(ShopIds.Efficient, "Metabolismo eficiente",
            "Las acciones (saltar, dash, deslizarse…) gastan menos energía.",
            ShopItemKind.Upgrade, new[] { 250, 500, 850 }, red, 5,
            new[] { "−5 % de gasto", "−10 % de gasto", "−15 % de gasto" }));
        c.items.Add(ShopItem.Create(ShopIds.SecondWind, "Segundo aliento",
            "Con menos del 20 % de energía, cada acierto devuelve el DOBLE de sangre.",
            ShopItemKind.Upgrade, new[] { 450 }, red));
        c.items.Add(ShopItem.Create(ShopIds.BlueBoost, "Sangre azul",
            "La sangre azul del modo bonus se gasta más despacio.",
            ShopItemKind.Upgrade, new[] { 300, 650 }, red, 5,
            new[] { "−25 % de gasto azul", "−50 % de gasto azul" }));
        c.items.Add(ShopItem.Create(ShopIds.StartBoost, "Buen desayuno",
            "Empiezas cada nivel ya en modo azul, con sangre azul en la jeringuilla.",
            ShopItemKind.Upgrade, new[] { 200, 400, 700 }, red, 5,
            new[] { "empiezas con un 20 % de sangre azul", "con un 40 %", "con un 60 %" }));
        // ── Ritmo ──
        c.items.Add(ShopItem.Create(ShopIds.RhythmSight, "Crosshair rítmico",
            "Las notas (spikes) entran hacia el punto de mira al ritmo de la música y marcan cuándo pulsar.",
            ShopItemKind.Upgrade, new[] { 150 }, blue));
        c.items.Add(ShopItem.Create(ShopIds.HalfBeat, "Doble tempo",
            "También puedes acertar en los MEDIOS beats (el doble de latidos: más sangre si llevas el ritmo).",
            ShopItemKind.Upgrade, new[] { 900 }, blue));
        // ── Supervivencia ──
        c.items.Add(ShopItem.Create(ShopIds.Parachute, "Paracaídas",
            "Una vez por nivel, si caes al agua o a la lava, rebotas hacia arriba en vez de morir.",
            ShopItemKind.Upgrade, new[] { 600 }, green));
        c.items.Add(ShopItem.Create(ShopIds.DrainGuard, "Piel gruesa",
            "Los BloodSuckers y los golpes te quitan menos sangre.",
            ShopItemKind.Upgrade, new[] { 350, 750 }, green, 5,
            new[] { "−25 % de sangre perdida", "−50 % de sangre perdida" }));
        c.items.Add(ShopItem.Create(ShopIds.LastStand, "Adrenalina",
            "Una vez por nivel, al quedarte sin sangre vuelves en el siguiente beat con un 30 % (gratis, antes de gastar vidas extra).",
            ShopItemKind.Upgrade, new[] { 1000 }, green));
        c.items.Add(ShopItem.Create(ShopIds.SlowBleed, "Coagulante",
            "Pierdes sangre pasivamente más despacio.",
            ShopItemKind.Upgrade, new[] { 300, 700 }, green, 5,
            new[] { "−15 % de pérdida pasiva", "−30 % de pérdida pasiva" }));
        c.items.Add(ShopItem.Create(ShopIds.Streak, "Racha",
            "A partir de 8 aciertos seguidos, cada acierto da un 25 % más de sangre hasta que falles.",
            ShopItemKind.Upgrade, new[] { 450 }, blue));
        c.items.Add(ShopItem.Create(ShopIds.Perfectionist, "Perfeccionista",
            "Un Perfect devuelve ×2 de sangre (en vez de ×1.5).",
            ShopItemKind.Upgrade, new[] { 550 }, blue));
        c.items.Add(ShopItem.Create(ShopIds.BarFlash, "Marca de compás",
            "El corazón del punto de mira destella en el primer beat de cada compás.",
            ShopItemKind.Upgrade, new[] { 120 }, blue));
        // ── Dinero ──
        c.items.Add(ShopItem.Create(ShopIds.PerfectCoin, "Latido dorado",
            "+1 moneda por cada latido PERFECTO.",
            ShopItemKind.Upgrade, new[] { 700 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.BloodGold, "Sangre de oro",
            "Al terminar el nivel, +1 moneda por cada % de energía que te quede.",
            ShopItemKind.Upgrade, new[] { 500 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.LuckyCoin, "Moneda de la suerte",
            "Cada moneda que recoges tiene un 10 % de probabilidad de valer el doble.",
            ShopItemKind.Upgrade, new[] { 400 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.Interest, "Interés",
            "Al terminar cada nivel, +5 % de lo que tengas en el monedero (máx. 100).",
            ShopItemKind.Upgrade, new[] { 800 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.ComboCoins, "Racha dorada",
            "+5 monedas cada 16 aciertos seguidos.",
            ShopItemKind.Upgrade, new[] { 600 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.AccuracyPay, "Pago por precisión",
            "Al terminar el nivel, +1 moneda por cada % de precisión.",
            ShopItemKind.Upgrade, new[] { 500 }, gold));
        c.items.Add(ShopItem.Create(ShopIds.Metronome, "Metrónomo",
            "Un «tic» en cada beat (más agudo al empezar cada compás) para no perder el ritmo. [M] lo enciende o apaga.",
            ShopItemKind.Upgrade, new[] { 250 }, blue));
        c.items.Add(ShopItem.Create(ShopIds.CoinMagnet, "Imán de monedas",
            "Las monedas cercanas vuelan hacia ti.",
            ShopItemKind.Upgrade, new[] { 150, 350 }, gold, 5,
            new[] { "radio 5 m", "radio 9 m" }));
        c.items.Add(ShopItem.Create(ShopIds.CoinBonus, "Bolsa de oro",
            "×1.5 monedas si terminas el nivel con más del 80 % de precisión.",
            ShopItemKind.Upgrade, new[] { 600 }, gold));
        return c;
    }
}
