using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Lo que un contrato OBLIGA a hacer mientras está firmado (además de su objetivo).</summary>
[Flags]
public enum ContractRule
{
    None       = 0,
    MovesOnBeat = 1 << 0,   // salto, dash, slide y remo solo funcionan en el beat
    NoShop      = 1 << 1,   // sin mejoras ni consumibles de la tienda durante el nivel
    PressLimit  = 1 << 2,   // «Contrarreloj»: pasarte de ContractCatalog.PressLimit pulsaciones = GAME OVER
}

/// <summary>Datos del final del nivel con los que se comprueban los contratos.</summary>
public struct LevelResult
{
    public float accuracy;       // % de beats sincronizados (LevelEndManager)
    public float energyPercent;  // 0..1 de energía al llegar a la meta
    public int   runCoins;       // monedas recogidas en la partida
    public float songBeats;      // beats de la canción que han pasado hasta llegar a la meta
}

/// <summary>Un contrato: objetivo que el jugador firma en el puesto y que cobra al completar un nivel cumpliéndolo.</summary>
public class ContractDef
{
    public string id, title, description;
    public int    tier;          // 1 = encargo, 2 = contrato, 3 = gran contrato
    public int    reward;        // monedas
    public ContractRule rules;
    /// <summary>Se puede cumplir A MITAD de nivel (objetivos de "consigue N…"): se cobra en cuanto se cumple.</summary>
    public bool midLevel;

    public Func<LevelResult, bool> check;    // ¿cumplido al terminar el nivel?
    public Func<string>            progress; // texto en directo para el HUD (null = sin progreso visible)
    public Func<bool>              broken;   // ¿ya es imposible en esta partida? (null = nunca se rompe antes del final)

    public static string TierName(int tier) =>
        tier >= 3 ? "Gran contrato" : tier == 2 ? "Contrato" : "Encargo";

    public static Color TierColor(int tier) =>
        tier >= 3 ? new Color(0.85f, 0.2f, 0.25f) : tier == 2 ? new Color(0.95f, 0.65f, 0.15f) : new Color(0.3f, 0.75f, 0.4f);
}

/// <summary>
/// LISTA DE CONTRATOS. Se cumplen al terminar un nivel (cualquiera menos la pelea del jefe) cumpliendo el objetivo;
/// cada uno se cobra UNA vez. Los números (objetivos y pagos) se ajustan aquí.
/// </summary>
public static class ContractCatalog
{
    /// <summary>Cuántos contratos puede tener firmados a la vez el jugador.</summary>
    public const int MaxActive = 3;

    /// <summary>Pulsaciones máximas del contrato «Contrarreloj» (la siguiente es game over).</summary>
    public const int PressLimit = 100;

    private static List<ContractDef> _all;

    public static IReadOnlyList<ContractDef> All => _all ??= Build();

    public static ContractDef Get(string id)
    {
        foreach (ContractDef c in All) if (c.id == id) return c;
        return null;
    }

    /// <summary>Beats de la canción que llevan sonando ahora mismo.</summary>
    public static float SongBeatsNow()
    {
        BeatManager bm = BeatManager.Instance;
        return bm != null && bm.BeatDuration > 0f ? bm.SongTime / bm.BeatDuration : 0f;
    }

    private static List<ContractDef> Build()
    {
        var l = new List<ContractDef>();

        // ── ENCARGOS (fáciles) ────────────────────────────────────────────────
        l.Add(new ContractDef { id = "first", tier = 1, reward = 30, title = "Primer encargo",
            description = "Completa un nivel. Sin más.",
            check = r => true });
        l.Add(new ContractDef { id = "coins25", midLevel = true, tier = 1, reward = 50, title = "Buscador de oro",
            description = "Recoge al menos 25 monedas en un mismo nivel.",
            check = r => r.runCoins >= 25,
            progress = () => $"{LevelCoins.RunCoins}/25 monedas" });
        l.Add(new ContractDef { id = "perfect20", midLevel = true, tier = 1, reward = 50, title = "Buen oído",
            description = "Consigue 20 latidos PERFECT en un nivel.",
            check = r => RunStats.Perfects >= 20,
            progress = () => $"{RunStats.Perfects}/20 Perfect" });
        l.Add(new ContractDef { id = "streak16", midLevel = true, tier = 1, reward = 60, title = "En racha",
            description = "Encadena 16 latidos acertados seguidos.",
            check = r => RunStats.BestStreak >= 16,
            progress = () => $"racha {RunStats.Streak} (mejor {RunStats.BestStreak}/16)" });
        l.Add(new ContractDef { id = "jumps25", midLevel = true, tier = 1, reward = 40, title = "Saltimbanqui",
            description = "Salta 25 veces en un nivel.",
            check = r => RunStats.Jumps >= 25,
            progress = () => $"{RunStats.Jumps}/25 saltos" });
        l.Add(new ContractDef { id = "dash8", midLevel = true, tier = 1, reward = 50, title = "Relámpago",
            description = "Haz 8 dashes en un nivel.",
            check = r => RunStats.Dashes >= 8,
            progress = () => $"{RunStats.Dashes}/8 dashes" });
        l.Add(new ContractDef { id = "wall4", midLevel = true, tier = 1, reward = 60, title = "Por las paredes",
            description = "Corre por las paredes 4 veces en un nivel.",
            check = r => RunStats.Wallruns >= 4,
            progress = () => $"{RunStats.Wallruns}/4 wallruns" });

        // ── CONTRATOS (medios) ────────────────────────────────────────────────
        l.Add(new ContractDef { id = "clean", tier = 2, reward = 120, title = "Sin rasguños",
            description = "Completa un nivel sin usar vidas extra, Adrenalina, Paracaídas, checkpoints ni rebobinados.",
            check = r => RunStats.HelpsUsed == 0,
            progress = () => RunStats.HelpsUsed == 0 ? "sin ayudas" : "ayuda usada",
            broken = () => RunStats.HelpsUsed > 0 });
        l.Add(new ContractDef { id = "acc85", tier = 2, reward = 120, title = "Afinado",
            description = "Termina un nivel con al menos un 85 % de precisión de beats.",
            check = r => r.accuracy >= 85f });
        l.Add(new ContractDef { id = "blue3", midLevel = true, tier = 2, reward = 100, title = "Sangre azul",
            description = "Entra 3 veces en modo azul en un mismo nivel.",
            check = r => RunStats.BlueEntries >= 3,
            progress = () => $"{RunStats.BlueEntries}/3 modo azul" });
        l.Add(new ContractDef { id = "fewmiss", tier = 2, reward = 150, title = "Pulso seguro",
            description = "Completa un nivel fallando menos de 5 latidos.",
            check = r => RunStats.Misses < 5,
            progress = () => $"{RunStats.Misses}/4 fallos",
            broken = () => RunStats.Misses >= 5 });
        l.Add(new ContractDef { id = "energy60", tier = 2, reward = 100, title = "Con reservas",
            description = "Llega a la meta con al menos un 60 % de energía.",
            check = r => r.energyPercent >= 0.6f });
        l.Add(new ContractDef { id = "streak40", midLevel = true, tier = 2, reward = 150, title = "Imparable",
            description = "Encadena 40 latidos acertados seguidos.",
            check = r => RunStats.BestStreak >= 40,
            progress = () => $"racha {RunStats.Streak} (mejor {RunStats.BestStreak}/40)" });

        // ── GRANDES CONTRATOS (difíciles) ─────────────────────────────────────
        l.Add(new ContractDef { id = "presses100", tier = 3, reward = 300, title = "Contrarreloj",
            description = $"Pásate un nivel con solo {PressLimit} pulsaciones. Si pulsas una vez más: GAME OVER " +
                          "(sin vidas extra ni checkpoints). El Auto-latido no funciona durante este reto.",
            rules = ContractRule.PressLimit,
            check = r => RunStats.HeartPresses <= PressLimit,
            progress = () => $"quedan {Mathf.Max(0, PressLimit - RunStats.HeartPresses)} pulsaciones" });
        l.Add(new ContractDef { id = "onbeat", tier = 3, reward = 300, title = "Al compás",
            description = "Pásate un nivel moviéndote al ritmo: el salto, el dash y el slide SOLO funcionan en el beat o el medio beat (y el remo, en el beat).",
            rules = ContractRule.MovesOnBeat,
            check = r => true,
            progress = () => "movimientos solo en el beat" });
        l.Add(new ContractDef { id = "acc95", tier = 3, reward = 300, title = "Metrónomo humano",
            description = "Termina un nivel con al menos un 95 % de precisión de beats.",
            check = r => r.accuracy >= 95f });
        l.Add(new ContractDef { id = "nomiss", tier = 3, reward = 350, title = "Latido perfecto",
            description = "Completa un nivel sin fallar ni un solo latido.",
            check = r => RunStats.Misses == 0,
            progress = () => RunStats.Misses == 0 ? "0 fallos" : $"{RunStats.Misses} fallos",
            broken = () => RunStats.Misses > 0 });
        l.Add(new ContractDef { id = "perfect60", tier = 3, reward = 250, title = "Perfeccionista",
            description = "Al menos el 60 % de tus latidos del nivel tienen que ser PERFECT (mínimo 40 latidos).",
            check = r => RunStats.HeartPresses >= 40 && RunStats.Perfects >= 0.6f * RunStats.HeartPresses,
            progress = () => RunStats.HeartPresses > 0
                ? $"{Mathf.RoundToInt(100f * RunStats.Perfects / RunStats.HeartPresses)} % Perfect ({RunStats.HeartPresses} latidos)"
                : "0 % Perfect" });
        l.Add(new ContractDef { id = "noshop", tier = 3, reward = 400, title = "A pelo",
            description = "Pásate un nivel sin NINGUNA mejora ni consumible de la tienda (se desactivan durante el nivel).",
            rules = ContractRule.NoShop,
            check = r => true,
            progress = () => "sin mejoras de la tienda" });

        return l;
    }
}
