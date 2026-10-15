using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// DESBLOQUEOS POR NIVEL: cada objeto de la tienda se puede comprar a partir de un nivel (9 mejoras por nivel).
///   • Consumibles: siempre (nivel 1).
///   • Nivel 1: lo básico.
///   • Nivel 2: más corazón, supervivencia y ritmo.
///   • Nivel 3: lo avanzado (golpes del jefe, BloodSuckers, Auto-latido, Doble tempo…).
/// El nivel alcanzado se guarda (PlayerProgress.LevelReached) al cargar una escena «LevelN».
/// Los objetos que no aparecen en la tabla están disponibles desde el nivel 1. Cambia la tabla a tu gusto.
/// </summary>
public static class ShopUnlocks
{
    private static readonly Dictionary<string, int> UnlockLevel = new Dictionary<string, int>
    {
        // ── Consumibles: siempre desde el nivel 1 ──
        { ShopIds.ExtraLife,     1 },
        { ShopIds.Checkpoint,    1 },
        { ShopIds.Shield,        1 },   // Rebobinado

        // ── Mejoras · nivel 1 (básicas) ──
        { ShopIds.BigSyringe,    1 },
        { ShopIds.StrongPulse,   1 },
        { ShopIds.Efficient,     1 },
        { ShopIds.WideWindow,    1 },
        { ShopIds.Metronome,     1 },
        { ShopIds.RhythmSight,   1 },
        { ShopIds.BarFlash,      1 },
        { ShopIds.CoinMagnet,    1 },
        { ShopIds.LuckyCoin,     1 },

        // ── Mejoras · nivel 2 ──
        { ShopIds.BlueBoost,     2 },
        { ShopIds.StartBoost,    2 },
        { ShopIds.SlowBleed,     2 },
        { ShopIds.Parachute,     2 },   // agua (nivel 2) y lava
        { ShopIds.Streak,        2 },
        { ShopIds.Perfectionist, 2 },
        { ShopIds.ComboCoins,    2 },
        { ShopIds.AccuracyPay,   2 },
        { ShopIds.BloodGold,     2 },

        // ── Mejoras · nivel 3 (avanzadas) ──
        { ShopIds.DrainGuard,    3 },   // golpes del jefe y BloodSuckers
        { ShopIds.LastStand,     3 },
        { ShopIds.SecondWind,    3 },
        { ShopIds.SteadyHeart,   3 },
        { ShopIds.AutoBeater,    3 },
        { ShopIds.HalfBeat,      3 },
        { ShopIds.PerfectCoin,   3 },
        { ShopIds.Interest,      3 },
        { ShopIds.CoinBonus,     3 },
    };

    public static int LevelOf(string id) => UnlockLevel.TryGetValue(id, out int l) ? l : 1;

    public static bool IsUnlocked(string id) => StudySession.InFreePlay || LevelOf(id) <= Mathf.Max(1, PlayerProgress.LevelReached);   // (práctica libre: todo)

    public static string MissingText(string id) => Loc.T($"Se desbloquea en el nivel {LevelOf(id)}", $"Unlocks at level {LevelOf(id)}");

    // Al cargar «Level1», «Level2»… se guarda el nivel alcanzado.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        AutoSpawn.Register(OnSceneLoaded);
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name.StartsWith("Level") && int.TryParse(s.name.Substring(5), out int n)) PlayerProgress.ReachLevel(n);
    }
}
