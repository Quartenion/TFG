using UnityEngine;

/// <summary>Identificadores de los objetos de la tienda (los usan los sistemas del juego para consultar el progreso).</summary>
public static class ShopIds
{
    // Consumibles
    public const string ExtraLife   = "extra_life";     // al morir, respawn en el siguiente beat
    public const string Checkpoint  = "checkpoint";     // al morir, vuelves al último checkpoint del nivel
    public const string Shield      = "shield";         // «Rebobinado»: al chocar, vuelves 4 beats atrás

    // Mejoras
    public const string SteadyHeart = "steady_heart";   // mantener clic: no pierdes sangre pasivamente
    public const string AutoBeater  = "auto_beater";    // acierto automático cada 8/4/2/1 beats
    public const string BigSyringe  = "big_syringe";    // más capacidad de sangre
    public const string StrongPulse = "strong_pulse";   // los aciertos devuelven más sangre
    public const string WideWindow  = "wide_window";    // ventana de timing más amplia
    public const string CoinMagnet  = "coin_magnet";    // las monedas cercanas vienen a ti
    public const string CoinBonus   = "coin_bonus";     // ×1.5 monedas si acabas con buena precisión
    public const string Metronome   = "metronome";      // «tic» en cada beat (encender/apagar con M)
    public const string RhythmSight = "rhythm_sight";   // crosshair rítmico: se ven las notas (spikes) entrantes
    public const string HalfBeat    = "half_beat";      // se puede acertar también en los medios beats
    public const string PerfectCoin = "perfect_coin";   // +1 moneda por cada Perfect
    public const string BloodGold   = "blood_gold";     // +1 moneda por cada % de energía al acabar el nivel
    public const string Efficient   = "efficient";      // las acciones (saltar, dash…) gastan un 5 % menos por nivel
    // Corazón
    public const string SecondWind  = "second_wind";    // bajo el 20 % de energía, los aciertos devuelven el doble
    public const string BlueBoost   = "blue_boost";     // la sangre azul (bonus) se gasta más despacio
    public const string StartBoost  = "start_boost";    // empiezas cada nivel con más energía
    // Supervivencia
    public const string Parachute   = "parachute";      // una vez por nivel, caer al agua/lava te hace rebotar
    public const string DrainGuard  = "drain_guard";    // BloodSuckers y golpes quitan menos sangre
    public const string LastStand   = "last_stand";     // una vez por nivel, al quedarte seco vuelves con 30 %
    public const string SlowBleed   = "slow_bleed";     // pierdes sangre pasivamente más despacio
    // Ritmo
    public const string Streak      = "streak";         // desde 8 aciertos seguidos, +25 % de sangre por acierto
    public const string Perfectionist = "perfectionist";// un Perfect da ×2 de sangre (en vez de ×1.5)
    public const string BarFlash    = "bar_flash";      // el corazón del punto de mira marca el 1er beat de cada compás
    // Dinero
    public const string LuckyCoin   = "lucky_coin";     // 10 % de que una moneda valga doble
    public const string Interest    = "interest";       // al acabar el nivel, +5 % de tu monedero (máx. 100)
    public const string ComboCoins  = "combo_coins";    // +5 monedas cada 16 aciertos seguidos
    public const string AccuracyPay = "accuracy_pay";   // al acabar el nivel, +1 moneda por cada % de precisión
}

public enum ShopItemKind { Upgrade, Consumable }

/// <summary>
/// Un objeto de la tienda. Créalo con clic derecho → Create → Tienda → Objeto (o usa el catálogo por defecto:
/// Tools → Tienda → Crear catálogo por defecto).
///   • Upgrade    : tantos niveles como precios haya (Prices[0] = nivel 1, Prices[1] = nivel 2…). Permanente.
///   • Consumable : precio Prices[0] por unidad, hasta 'Max Stack' unidades. Se gasta al usarse.
/// </summary>
[CreateAssetMenu(fileName = "ShopItem", menuName = "Tienda/Objeto")]
public class ShopItem : ScriptableObject
{
    [Tooltip("Identificador único (ver ShopIds). Es lo que se guarda en la partida.")]
    public string id;
    public string displayName;
    [TextArea(2, 4)] public string description;
    [Tooltip("Descripción de cada nivel (opcional, mejoras). [0] = nivel 1…")]
    public string[] levelDescriptions;
    public Sprite icon;
    [Tooltip("Color de la tarjeta en la tienda.")]
    public Color color = new Color(0.8f, 0.15f, 0.2f);
    public ShopItemKind kind = ShopItemKind.Upgrade;
    [Tooltip("Mejora: precio de cada nivel. Consumible: [0] = precio por unidad.")]
    public int[] prices = { 100 };
    [Tooltip("Consumible: cuántas unidades se pueden tener como máximo.")]
    public int maxStack = 5;

    public int  MaxLevel => kind == ShopItemKind.Upgrade ? (prices != null ? prices.Length : 0) : Mathf.Max(1, maxStack);
    public int  Owned    => kind == ShopItemKind.Upgrade ? PlayerProgress.GetOwnedLevel(id) : PlayerProgress.GetCount(id);
    public bool IsMaxed  => Owned >= MaxLevel;

    /// <summary>Precio de la siguiente compra (−1 si ya está al máximo).</summary>
    public int NextPrice
    {
        get
        {
            if (IsMaxed || prices == null || prices.Length == 0) return -1;
            return kind == ShopItemKind.Upgrade ? prices[Mathf.Clamp(Owned, 0, prices.Length - 1)] : prices[0];
        }
    }

    public bool CanAfford => NextPrice >= 0 && PlayerProgress.Coins >= NextPrice;

    /// <summary>¿Bloqueada? (aún no has llegado al nivel en el que se desbloquea: ver ShopUnlocks)</summary>
    public bool IsLocked => !ShopUnlocks.IsUnlocked(id);

    /// <summary>Compra (siguiente nivel / una unidad) si hay monedas. Devuelve si se ha comprado.</summary>
    public bool TryBuy()
    {
        if (IsLocked) return false;
        int price = NextPrice;
        if (price < 0 || !PlayerProgress.TrySpendCoins(price)) return false;
        if (kind == ShopItemKind.Upgrade)
        {
            bool first = Owned == 0;
            PlayerProgress.SetLevel(id, Owned + 1);
            // Al comprarla por primera vez se EQUIPA sola si caben sus muescas.
            if (first && Notches.CanEquip(id)) PlayerProgress.SetUpgradeEnabled(id, true);
        }
        else PlayerProgress.AddCount(id, 1);
        StudyTelemetry.ShopEvent("compra", id, price);   // (modo prueba)
        return true;
    }

    // ── Textos (según el idioma: español en el asset, inglés en ShopTextsEn) ──

    private bool En(out ShopTextsEn.Entry e)
    {
        e = default;
        return Loc.English && id != null && ShopTextsEn.Items.TryGetValue(id, out e);
    }

    /// <summary>Nombre en el idioma actual.</summary>
    public string Name => En(out var e) && !string.IsNullOrEmpty(e.name) ? e.name : displayName;

    /// <summary>Descripción general en el idioma actual.</summary>
    public string Description => En(out var e) && !string.IsNullOrEmpty(e.desc) ? e.desc : description;

    private string LevelDescription(int i)
    {
        if (En(out var e) && e.levels != null && i < e.levels.Length) return e.levels[i];
        return levelDescriptions != null && i < levelDescriptions.Length ? levelDescriptions[i] : null;
    }

    /// <summary>Texto del estado actual («Nivel 2/4», «Tienes 3»…).</summary>
    public string OwnedLabel => kind == ShopItemKind.Upgrade
        ? (Owned == 0 ? Loc.T("No comprado", "Not bought") : Loc.T($"Nivel {Owned}/{MaxLevel}", $"Level {Owned}/{MaxLevel}"))
        : Loc.T($"Tienes {Owned}/{MaxLevel}", $"You have {Owned}/{MaxLevel}");

    /// <summary>Descripción de lo que da el SIGUIENTE nivel (si la hay), o la general.</summary>
    public string NextDescription
    {
        get
        {
            if (kind == ShopItemKind.Upgrade)
            {
                string next = LevelDescription(Owned);
                if (!string.IsNullOrEmpty(next)) return Description + "\n<i>" + Loc.T("Siguiente: ", "Next: ") + next + "</i>";
            }
            return Description;
        }
    }

    public static ShopItem Create(string id, string name, string desc, ShopItemKind kind, int[] prices,
                                  Color color, int maxStack = 5, string[] levelDescs = null)
    {
        var it = CreateInstance<ShopItem>();
        it.name = id;
        it.id = id; it.displayName = name; it.description = desc; it.kind = kind;
        it.prices = prices; it.color = color; it.maxStack = maxStack; it.levelDescriptions = levelDescs;
        return it;
    }
}
