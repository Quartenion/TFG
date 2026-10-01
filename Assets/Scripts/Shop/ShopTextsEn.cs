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
        { ShopIds.ExtraLife,  E("Extra life", "If you run out of blood, instead of dying you come back on the next beat.") },
        { ShopIds.Checkpoint, E("Checkpoint", "When you die, you go back to the last checkpoint you passed in the level (the song continues from there).") },
        { ShopIds.Shield,     E("Rewind", "If you crash into an obstacle, time goes back 4 beats (to a safe moment) instead of dying. Against boss attacks or in the kayak, it blocks the hit.") },

        { ShopIds.SteadyHeart, E("Steady heart", "While you hold the click, the heart does not lose blood passively.") },
        { ShopIds.AutoBeater,  E("Auto-beat", "An automatic successful beat every few beats.", "every 8 beats", "every 4 beats", "every 2 beats", "every beat") },
        { ShopIds.BigSyringe,  E("Big syringe", "More blood capacity before you run dry.", "+10 % capacity", "+20 % capacity", "+30 % capacity") },
        { ShopIds.StrongPulse, E("Strong pulse", "Each hit returns more blood to the heart.", "+15 % per hit", "+30 % per hit", "+50 % per hit") },
        { ShopIds.WideWindow,  E("Keen ear", "The window to hit the beat is a bit wider.", "+15 % margin", "+30 % margin") },
        { ShopIds.Efficient,   E("Efficient metabolism", "Actions (jump, dash, slide…) cost less energy.", "−5 % cost", "−10 % cost", "−15 % cost") },
        { ShopIds.SecondWind,  E("Second wind", "Below 20 % energy, each hit returns DOUBLE blood.") },
        { ShopIds.BlueBoost,   E("Blue blood", "Blue blood from bonus mode drains more slowly.", "−25 % blue drain", "−50 % blue drain") },
        { ShopIds.StartBoost,  E("Good breakfast", "You start each level already in blue mode, with blue blood in the syringe.", "start with 20 % blue blood", "with 40 %", "with 60 %") },
        { ShopIds.RhythmSight, E("Rhythm crosshair", "Notes (spikes) move into the crosshair to the beat of the music and show when to click.") },
        { ShopIds.HalfBeat,    E("Double tempo", "You can also hit on the HALF beats (twice the beats: more blood if you keep the rhythm).") },
        { ShopIds.Parachute,   E("Parachute", "Once per level, if you fall into water or lava, you bounce up instead of dying.") },
        { ShopIds.DrainGuard,  E("Thick skin", "BloodSuckers and hits take less blood.", "−25 % blood lost", "−50 % blood lost") },
        { ShopIds.LastStand,   E("Adrenaline", "Once per level, when you run out of blood you come back on the next beat with 30 % (free, before using extra lives).") },
        { ShopIds.SlowBleed,   E("Coagulant", "You lose blood passively more slowly.", "−15 % passive loss", "−30 % passive loss") },
        { ShopIds.Streak,      E("Streak", "From 8 hits in a row, each hit gives 25 % more blood until you miss.") },
        { ShopIds.Perfectionist, E("Perfectionist", "A Perfect returns ×2 blood (instead of ×1.5).") },
        { ShopIds.BarFlash,    E("Bar marker", "The crosshair heart flashes on the first beat of each bar.") },
        { ShopIds.PerfectCoin, E("Golden beat", "+1 coin for each PERFECT beat.") },
        { ShopIds.BloodGold,   E("Golden blood", "When you finish the level, +1 coin for each % of energy you have left.") },
        { ShopIds.LuckyCoin,   E("Lucky coin", "Each coin you pick up has a 10 % chance of being worth double.") },
        { ShopIds.Interest,    E("Interest", "When you finish each level, +5 % of what you have in your wallet (max. 100).") },
        { ShopIds.ComboCoins,  E("Golden streak", "+5 coins every 16 hits in a row.") },
        { ShopIds.AccuracyPay, E("Accuracy pay", "When you finish the level, +1 coin for each % of accuracy.") },
        { ShopIds.Metronome,   E("Metronome", "A «tick» on every beat (higher at the start of each bar) so you never lose the rhythm. [M] turns it on or off.") },
        { ShopIds.CoinMagnet,  E("Coin magnet", "Nearby coins fly towards you.", "5 m radius", "9 m radius") },
        { ShopIds.CoinBonus,   E("Bag of gold", "×1.5 coins if you finish the level with more than 80 % accuracy.") },
    };
}
