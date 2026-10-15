using System.Collections.Generic;

/// <summary>
/// Textos en INGLÉS de los objetos de la tienda (por id). Los textos en español están en los assets ShopItem
/// (Resources/ShopItems) o en el catálogo por defecto. ShopItem.Name / Description / NextDescription eligen según Loc.
/// </summary>
public static class ShopTextsEn
{
    public struct Entry { public string name, desc; public string[] levels; }

    private static Entry E(string name, string desc, params string[] levels) => new Entry { name = name, desc = desc, levels = levels };

    public static readonly Dictionary<string, Entry> Items = new Dictionary<string, Entry>
    {
        { ShopIds.ExtraLife,  E("Extra life", "When you run out of blood, you revive instead of dying.") },
        { ShopIds.Checkpoint, E("Checkpoint", "When you die, you return to the last checkpoint.") },
        { ShopIds.Shield,     E("Rewind", "When you crash, time rewinds 4 beats instead of dying.") },

        { ShopIds.SteadyHeart, E("Steady heart", "Hold the click to stop losing blood.") },
        { ShopIds.AutoBeater,  E("Auto-beat", "The heart beats by itself every few beats.", "every 8 beats", "every 4 beats", "every 2 beats", "every beat") },
        { ShopIds.BigSyringe,  E("Big syringe", "More blood capacity.", "+10 % capacity", "+20 % capacity", "+30 % capacity") },
        { ShopIds.StrongPulse, E("Strong pulse", "Each hit restores more blood.", "+15 % per hit", "+30 % per hit", "+50 % per hit") },
        { ShopIds.WideWindow,  E("Keen ear", "More margin to hit the beat.", "+15 % margin", "+30 % margin") },
        { ShopIds.Efficient,   E("Efficient metabolism", "Actions cost less energy.", "−5 % cost", "−10 % cost", "−15 % cost") },
        { ShopIds.SecondWind,  E("Second wind", "Below 20 % energy, hits restore double.") },
        { ShopIds.BlueBoost,   E("Blue blood", "Blue blood drains more slowly.", "−25 % blue drain", "−50 % blue drain") },
        { ShopIds.StartBoost,  E("Good breakfast", "You start the level with blue blood.", "start with 20 % blue blood", "with 40 %", "with 60 %") },
        { ShopIds.RhythmSight, E("Rhythm crosshair", "The crosshair shows when to click.") },
        { ShopIds.HalfBeat,    E("Double tempo", "You can also hit on half beats.") },
        { ShopIds.Parachute,   E("Parachute", "Once per level, you bounce instead of dying when you fall.") },
        { ShopIds.DrainGuard,  E("Thick skin", "You lose less blood from enemies and hits.", "−25 % blood lost", "−50 % blood lost") },
        { ShopIds.LastStand,   E("Adrenaline", "Once per level, you revive with 30 % blood.") },
        { ShopIds.SlowBleed,   E("Coagulant", "You lose blood more slowly.", "−15 % passive loss", "−30 % passive loss") },
        { ShopIds.Streak,      E("Streak", "After 8 hits in a row, +25 % blood per hit.") },
        { ShopIds.Perfectionist, E("Perfectionist", "Perfects restore ×2 blood.") },
        { ShopIds.BarFlash,    E("Bar marker", "The crosshair flashes at the start of each bar.") },
        { ShopIds.PerfectCoin, E("Golden beat", "+1 coin per Perfect.") },
        { ShopIds.BloodGold,   E("Golden blood", "At the end of the level, +1 coin per % of energy left.") },
        { ShopIds.LuckyCoin,   E("Lucky coin", "Each coin has a 10 % chance to be worth double.") },
        { ShopIds.Interest,    E("Interest", "At the end of the level, +5 % of your wallet (max. 100).") },
        { ShopIds.ComboCoins,  E("Golden streak", "+5 coins every 16 hits in a row.") },
        { ShopIds.AccuracyPay, E("Accuracy pay", "At the end of the level, +1 coin per % of accuracy.") },
        { ShopIds.Metronome,   E("Metronome", "A «tick» on every beat. [M] turns it on or off.") },
        { ShopIds.CoinMagnet,  E("Coin magnet", "Pulls in nearby coins.", "5 m radius", "9 m radius") },
        { ShopIds.CoinBonus,   E("Bag of gold", "×1.5 coins if you finish with more than 80 % accuracy.") },
    };
}
