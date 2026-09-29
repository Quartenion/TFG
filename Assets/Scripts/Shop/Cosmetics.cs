using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Huecos de cosmético: en cada uno se lleva puesto uno (o el «Original»).</summary>
public enum CosmeticSlot { Heart, Hand, Coins, Kayak }

/// <summary>Un cosmético: solo cambia el aspecto. Se compra con IRIS (las monedas de mirada, no las normales).</summary>
public class CosmeticDef
{
    public string id, name, description;
    public CosmeticSlot slot;
    public int price;          // en iris
    public Color color;
}

/// <summary>
/// COSMÉTICOS: catálogo, comprar/poner, y aplicarlos en la escena (CosmeticApplier se crea solo).
///   • Corazón  → color del corazón del punto de mira.
///   • Mano     → color de la mano / brazo.
///   • Monedas  → color de las monedas del nivel.
///   • Kayak    → color del kayak.
/// Los precios están en IRIS. Ajusta la lista aquí.
/// </summary>
public static class Cosmetics
{
    public static readonly CosmeticSlot[] Slots = { CosmeticSlot.Heart, CosmeticSlot.Hand, CosmeticSlot.Coins, CosmeticSlot.Kayak };

    private static List<CosmeticDef> _all;
    public static IReadOnlyList<CosmeticDef> All => _all ??= Build();

    public static CosmeticDef Get(string id)
    {
        foreach (CosmeticDef c in All) if (c.id == id) return c;
        return null;
    }

    public static string SlotName(CosmeticSlot s)
    {
        switch (s)
        {
            case CosmeticSlot.Hand:  return "Mano";
            case CosmeticSlot.Coins: return "Monedas";
            case CosmeticSlot.Kayak: return "Kayak";
            default:                 return "Corazón del punto de mira";
        }
    }

    private static List<CosmeticDef> Build()
    {
        var l = new List<CosmeticDef>();
        void Add(string id, CosmeticSlot slot, string name, int price, Color col, string desc) =>
            l.Add(new CosmeticDef { id = id, slot = slot, name = name, price = price, color = col, description = desc });

        Add("heart_gold",   CosmeticSlot.Heart, "Corazón dorado", 2, new Color(1f, 0.8f, 0.2f),    "El corazón del punto de mira, de oro.");
        Add("heart_cyan",   CosmeticSlot.Heart, "Corazón helado", 2, new Color(0.35f, 0.9f, 1f),   "Frío como el hielo.");
        Add("heart_violet", CosmeticSlot.Heart, "Corazón violeta", 3, new Color(0.7f, 0.35f, 1f),  "Del color de los iris.");
        Add("heart_black",  CosmeticSlot.Heart, "Corazón negro", 5, new Color(0.12f, 0.12f, 0.14f), "Para los que ya no sienten nada.");

        Add("hand_pale",    CosmeticSlot.Hand, "Mano pálida", 2, new Color(0.95f, 0.9f, 0.85f),     "Como si no le diera el sol.");
        Add("hand_ghoul",   CosmeticSlot.Hand, "Mano de ghoul", 3, new Color(0.55f, 0.75f, 0.5f),   "Verdosa. Poco saludable.");
        Add("hand_ink",     CosmeticSlot.Hand, "Mano de tinta", 4, new Color(0.18f, 0.2f, 0.35f),   "Como si hubieras firmado demasiados contratos.");
        Add("hand_gold",    CosmeticSlot.Hand, "Mano de oro", 6, new Color(1f, 0.78f, 0.25f),       "Todo lo que toca…");

        Add("coins_silver", CosmeticSlot.Coins, "Monedas de plata", 2, new Color(0.8f, 0.85f, 0.9f), "Mismo valor, otro brillo.");
        Add("coins_ruby",   CosmeticSlot.Coins, "Monedas de rubí", 3, new Color(0.9f, 0.15f, 0.25f), "Rojas como la sangre.");
        Add("coins_jade",   CosmeticSlot.Coins, "Monedas de jade", 3, new Color(0.2f, 0.85f, 0.5f),  "Verdes y relucientes.");

        Add("kayak_red",    CosmeticSlot.Kayak, "Kayak rojo", 2, new Color(0.85f, 0.15f, 0.15f),   "Más rápido (no).");
        Add("kayak_night",  CosmeticSlot.Kayak, "Kayak nocturno", 3, new Color(0.15f, 0.15f, 0.25f), "Discreto.");
        Add("kayak_gold",   CosmeticSlot.Kayak, "Kayak de oro", 6, new Color(1f, 0.8f, 0.25f),     "No flota mejor, pero luce.");
        return l;
    }

    // ── Comprar / poner ───────────────────────────────────────────────────────

    public static bool Owns(CosmeticDef c) => c != null && PlayerProgress.OwnsCosmetic(c.id);

    public static bool TryBuy(CosmeticDef c)
    {
        if (c == null || Owns(c) || !PlayerProgress.TrySpendGazeCoins(c.price)) return false;
        PlayerProgress.AddCosmetic(c.id);
        Equip(c.slot, c.id);   // (se pone al comprarlo)
        return true;
    }

    /// <summary>Pone 'id' en su hueco (null / vacío = el Original).</summary>
    public static void Equip(CosmeticSlot slot, string id) =>
        PlayerProgress.EquipCosmetic(id, e => { CosmeticDef d = Get(e); return d == null || d.slot == slot; });

    /// <summary>Lo que se lleva puesto en ese hueco (null = Original).</summary>
    public static CosmeticDef Equipped(CosmeticSlot slot)
    {
        foreach (string id in PlayerProgress.EquippedCosmetics)
        {
            CosmeticDef d = Get(id);
            if (d != null && d.slot == slot && Owns(d)) return d;
        }
        return null;
    }
}

