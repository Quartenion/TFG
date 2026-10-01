using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Lo que un contrato OBLIGA a hacer mientras está firmado (además de su objetivo).</summary>
[Flags]
public enum ContractRule
{
    None       = 0,
    MovesOnBeat = 1 << 0,   // salto, dash, slide y remo solo funcionan en el beat
    NoShop      = 1 << 1,   // sin mejoras ni consumibles de la tienda durante el nivel
    PressLimit  = 1 << 2,   // «Contrarreloj»: pasarte de ContractCatalog.PressLimitFor(nivel) pulsaciones = GAME OVER
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
    public string id, titleEs, titleEn;
    public Func<string> descFn;
    public string title       => Loc.T(titleEs, titleEn);
    public string description => descFn != null ? descFn() : "";
    public int    level;         // nivel del juego al que pertenece (1, 2, 3): solo cuenta jugando ese nivel
    public int    tier;          // 1 = encargo, 2 = contrato, 3 = gran contrato
    public int    reward;        // monedas
    public ContractRule rules;
    /// <summary>Se puede cumplir A MITAD de nivel (objetivos de "consigue N…"): se cobra en cuanto se cumple.</summary>
    public bool midLevel;

    public Func<LevelResult, bool> check;    // ¿cumplido al terminar el nivel?
    public Func<string>            progress; // texto en directo para el HUD (null = sin progreso visible)
    public Func<bool>              broken;   // ¿ya es imposible en esta partida? (null = nunca se rompe antes del final)

    public static string TierName(int tier) =>
        tier >= 3 ? Loc.T("Gran contrato", "Big contract") : tier == 2 ? Loc.T("Contrato", "Contract") : Loc.T("Encargo", "Job");

    public static Color TierColor(int tier) =>
        tier >= 3 ? new Color(0.85f, 0.2f, 0.25f) : tier == 2 ? new Color(0.95f, 0.65f, 0.15f) : new Color(0.3f, 0.75f, 0.4f);
}

/// <summary>
/// LISTA DE CONTRATOS. Cada contrato existe UNA VEZ POR NIVEL (Level1, Level2, Level3), con objetivo y pago cada vez
/// mayores: se firma en el puesto de ese nivel, solo cuenta jugando ese nivel y se cobra una vez.
/// Los números de cada nivel están en los arrays { nivel 1, nivel 2, nivel 3 } de abajo: ajústalos aquí.
/// </summary>
public static class ContractCatalog
{
    /// <summary>Cuántos contratos se pueden tener firmados a la vez EN CADA NIVEL.</summary>
    public const int MaxActive = 3;

    public const int Levels = 3;

    /// <summary>Pulsaciones máximas de «Contrarreloj» en cada nivel (la siguiente es game over).</summary>
    private static readonly int[] PressLimits = { 100, 90, 80 };
    public static int PressLimitFor(int level) => PressLimits[Mathf.Clamp(level, 1, Levels) - 1];

    private static List<ContractDef> _all;

    public static IReadOnlyList<ContractDef> All => _all ??= Build();

    public static ContractDef Get(string id)
    {
        foreach (ContractDef c in All) if (c.id == id) return c;
        return null;
    }

    /// <summary>Nivel actual: el de la escena «LevelN»; fuera de un nivel (menú), el último alcanzado.</summary>
    public static int CurrentLevel
    {
        get
        {
            string s = SceneManager.GetActiveScene().name;
            if (s.StartsWith("Level") && int.TryParse(s.Substring(5), out int n)) return Mathf.Clamp(n, 1, Levels);
            return Mathf.Clamp(PlayerProgress.LevelReached, 1, Levels);
        }
    }

    /// <summary>Contratos firmados del nivel actual (los de otros niveles siguen firmados, esperando a su nivel).</summary>
    public static List<ContractDef> ActiveHere()
    {
        var list = new List<ContractDef>();
        if (StudySession.Active && !StudySession.ShopAllowed) return list;   // (modo prueba: contratos solo en las partes 4-5)
        int lv = CurrentLevel;
        foreach (string id in PlayerProgress.ActiveContracts)
        {
            ContractDef c = Get(id);
            if (c != null && c.level == lv) list.Add(c);
        }
        return list;
    }

    /// <summary>Beats de la canción que llevan sonando ahora mismo.</summary>
    public static float SongBeatsNow()
    {
        BeatManager bm = BeatManager.Instance;
        return bm != null && bm.BeatDuration > 0f ? bm.SongTime / bm.BeatDuration : 0f;
    }

    // ── Definición ────────────────────────────────────────────────────────────

    private static List<ContractDef> _build;

    /// <summary>Crea las 3 versiones (una por nivel) de un contrato. 'v' = valor del objetivo en cada nivel.</summary>
    private static void Add(string id, int tier, (string es, string en) title, int[] reward, int[] v,
                            Func<int, string> desc, Func<int, Func<LevelResult, bool>> check,
                            Func<int, Func<string>> progress = null, Func<int, Func<bool>> broken = null,
                            bool midLevel = false, ContractRule rules = ContractRule.None)
    {
        for (int lv = 1; lv <= Levels; lv++)
        {
            int x = v[lv - 1];
            _build.Add(new ContractDef
            {
                id = $"{id}_{lv}", level = lv, tier = tier, titleEs = title.es, titleEn = title.en, reward = reward[lv - 1],
                descFn = () => desc(x), check = check(x), progress = progress?.Invoke(x), broken = broken?.Invoke(x),
                midLevel = midLevel, rules = rules,
            });
        }
    }

    private static List<ContractDef> Build()
    {
        _build = new List<ContractDef>();
        int[] none = { 0, 0, 0 };

        // ── ENCARGOS (fáciles) ────────────────────────────────────────────────
        Add("first", 1, ("Primer encargo", "First job"), new[] { 30, 45, 60 }, none,
            x => Loc.T("Completa este nivel. Sin más.", "Complete this level. That's it."),
            x => r => true);
        Add("coins", 1, ("Buscador de oro", "Gold digger"), new[] { 50, 90, 140 }, new[] { 25, 50, 80 },
            x => Loc.T($"Recoge al menos {x} monedas en este nivel.", $"Collect at least {x} coins in this level."),
            x => r => r.runCoins >= x,
            x => () => Loc.T($"{LevelCoins.RunCoins}/{x} monedas", $"{LevelCoins.RunCoins}/{x} coins"), midLevel: true);
        Add("perfect", 1, ("Buen oído", "Good ear"), new[] { 50, 80, 120 }, new[] { 20, 35, 50 },
            x => Loc.T($"Consigue {x} latidos PERFECT en este nivel.", $"Get {x} PERFECT beats in this level."),
            x => r => RunStats.Perfects >= x,
            x => () => $"{RunStats.Perfects}/{x} Perfect", midLevel: true);
        Add("streak", 1, ("En racha", "On a roll"), new[] { 60, 90, 130 }, new[] { 16, 24, 32 },
            x => Loc.T($"Encadena {x} latidos acertados seguidos.", $"Chain {x} successful beats in a row."),
            x => r => RunStats.BestStreak >= x,
            x => () => Loc.T($"racha {RunStats.Streak} (mejor {RunStats.BestStreak}/{x})", $"streak {RunStats.Streak} (best {RunStats.BestStreak}/{x})"), midLevel: true);
        Add("jumps", 1, ("Saltimbanqui", "Acrobat"), new[] { 40, 60, 90 }, new[] { 25, 40, 60 },
            x => Loc.T($"Salta {x} veces en este nivel.", $"Jump {x} times in this level."),
            x => r => RunStats.Jumps >= x,
            x => () => Loc.T($"{RunStats.Jumps}/{x} saltos", $"{RunStats.Jumps}/{x} jumps"), midLevel: true);
        Add("dash", 1, ("Relámpago", "Lightning"), new[] { 50, 75, 110 }, new[] { 8, 12, 18 },
            x => Loc.T($"Haz {x} dashes en este nivel.", $"Do {x} dashes in this level."),
            x => r => RunStats.Dashes >= x,
            x => () => $"{RunStats.Dashes}/{x} dashes", midLevel: true);
        Add("wall", 1, ("Por las paredes", "Up the walls"), new[] { 60, 90, 130 }, new[] { 4, 8, 12 },
            x => Loc.T($"Corre por las paredes {x} veces en este nivel.", $"Wallrun {x} times in this level."),
            x => r => RunStats.Wallruns >= x,
            x => () => $"{RunStats.Wallruns}/{x} wallruns", midLevel: true);

        // ── CONTRATOS (medios) ────────────────────────────────────────────────
        Add("clean", 2, ("Sin rasguños", "Not a scratch"), new[] { 120, 180, 250 }, none,
            x => Loc.T("Completa este nivel sin usar vidas extra, Adrenalina, Paracaídas, checkpoints ni rebobinados.",
                        "Complete this level without using extra lives, Adrenaline, Parachute, checkpoints or rewinds."),
            x => r => RunStats.HelpsUsed == 0,
            x => () => RunStats.HelpsUsed == 0 ? Loc.T("sin ayudas", "no help") : Loc.T("ayuda usada", "help used"),
            x => () => RunStats.HelpsUsed > 0);
        Add("acc", 2, ("Afinado", "In tune"), new[] { 120, 170, 230 }, new[] { 80, 85, 90 },
            x => Loc.T($"Termina este nivel con al menos un {x} % de precisión de beats.", $"Finish this level with at least {x} % beat accuracy."),
            x => r => r.accuracy >= x);
        Add("blue", 2, ("Sangre azul", "Blue blood"), new[] { 100, 140, 190 }, new[] { 2, 3, 4 },
            x => Loc.T($"Entra {x} veces en modo azul en este nivel.", $"Enter blue mode {x} times in this level."),
            x => r => RunStats.BlueEntries >= x,
            x => () => Loc.T($"{RunStats.BlueEntries}/{x} modo azul", $"{RunStats.BlueEntries}/{x} blue mode"), midLevel: true);
        Add("fewmiss", 2, ("Pulso seguro", "Steady pulse"), new[] { 150, 210, 280 }, new[] { 8, 5, 3 },
            x => Loc.T($"Completa este nivel fallando menos de {x} latidos.", $"Complete this level missing fewer than {x} beats."),
            x => r => RunStats.Misses < x,
            x => () => Loc.T($"{RunStats.Misses}/{x - 1} fallos", $"{RunStats.Misses}/{x - 1} misses"),
            x => () => RunStats.Misses >= x);
        Add("energy", 2, ("Con reservas", "In reserve"), new[] { 100, 140, 190 }, new[] { 50, 60, 70 },
            x => Loc.T($"Llega a la meta con al menos un {x} % de energía.", $"Reach the goal with at least {x} % energy."),
            x => r => r.energyPercent * 100f >= x);
        Add("bigstreak", 2, ("Imparable", "Unstoppable"), new[] { 150, 200, 260 }, new[] { 30, 40, 50 },
            x => Loc.T($"Encadena {x} latidos acertados seguidos.", $"Chain {x} successful beats in a row."),
            x => r => RunStats.BestStreak >= x,
            x => () => Loc.T($"racha {RunStats.Streak} (mejor {RunStats.BestStreak}/{x})", $"streak {RunStats.Streak} (best {RunStats.BestStreak}/{x})"), midLevel: true);

        // ── GRANDES CONTRATOS (difíciles) ─────────────────────────────────────
        Add("presses", 3, ("Contrarreloj", "Against the clock"), new[] { 300, 400, 500 }, PressLimits,
            x => Loc.T($"Pásate este nivel con solo {x} pulsaciones. Si pulsas una vez más: GAME OVER " +
                       "(sin vidas extra ni checkpoints). El Auto-latido no funciona durante este reto.",
                       $"Beat this level with only {x} presses. One more press: GAME OVER " +
                       "(no extra lives or checkpoints). Auto-beat does not work during this challenge."),
            x => r => RunStats.HeartPresses <= x,
            x => () => Loc.T($"quedan {Mathf.Max(0, x - RunStats.HeartPresses)} pulsaciones", $"{Mathf.Max(0, x - RunStats.HeartPresses)} presses left"),
            rules: ContractRule.PressLimit);
        Add("onbeat", 3, ("Al compás", "On the beat"), new[] { 300, 400, 500 }, none,
            x => Loc.T("Pásate este nivel moviéndote al ritmo: el salto, el dash y el slide SOLO funcionan en el beat o el medio beat (y el remo, en el beat).",
                        "Beat this level moving to the rhythm: jump, dash and slide ONLY work on the beat or half beat (and paddling, on the beat)."),
            x => r => true,
            x => () => Loc.T("movimientos solo en el beat", "moves only on the beat"),
            rules: ContractRule.MovesOnBeat);
        Add("acc_top", 3, ("Metrónomo humano", "Human metronome"), new[] { 300, 400, 520 }, new[] { 92, 95, 97 },
            x => Loc.T($"Termina este nivel con al menos un {x} % de precisión de beats.", $"Finish this level with at least {x} % beat accuracy."),
            x => r => r.accuracy >= x);
        Add("nomiss", 3, ("Latido perfecto", "Perfect heartbeat"), new[] { 350, 450, 600 }, none,
            x => Loc.T("Completa este nivel sin fallar ni un solo latido.", "Complete this level without missing a single beat."),
            x => r => RunStats.Misses == 0,
            x => () => RunStats.Misses == 0 ? Loc.T("0 fallos", "0 misses") : Loc.T($"{RunStats.Misses} fallos", $"{RunStats.Misses} misses"),
            x => () => RunStats.Misses > 0);
        Add("perfect_pct", 3, ("Perfeccionista", "Perfectionist"), new[] { 250, 330, 420 }, new[] { 60, 65, 70 },
            x => Loc.T($"Al menos el {x} % de tus latidos del nivel tienen que ser PERFECT (mínimo 40 latidos).", $"At least {x} % of your beats in the level must be PERFECT (minimum 40 beats)."),
            x => r => RunStats.HeartPresses >= 40 && RunStats.Perfects >= x / 100f * RunStats.HeartPresses,
            x => () => RunStats.HeartPresses > 0
                ? Loc.T($"{Mathf.RoundToInt(100f * RunStats.Perfects / RunStats.HeartPresses)}/{x} % Perfect ({RunStats.HeartPresses} latidos)", $"{Mathf.RoundToInt(100f * RunStats.Perfects / RunStats.HeartPresses)}/{x} % Perfect ({RunStats.HeartPresses} beats)")
                : $"0/{x} % Perfect");
        Add("noshop", 3, ("A pelo", "Bare-handed"), new[] { 400, 500, 650 }, none,
            x => Loc.T("Pásate este nivel sin NINGUNA mejora ni consumible de la tienda (se desactivan durante el nivel).", "Beat this level without ANY shop upgrades or consumables (they are disabled during the level)."),
            x => r => true,
            x => () => Loc.T("sin mejoras de la tienda", "no shop upgrades"),
            rules: ContractRule.NoShop);

        List<ContractDef> result = _build;
        _build = null;
        return result;
    }
}
