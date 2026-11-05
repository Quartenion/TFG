using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Datos guardados de la partida, en JSON, en Application.persistentDataPath/save.json.</summary>
[Serializable]
public class SaveData
{
    public int coins;                                        // monedas en el monedero
    public int totalCoinsEarned;                             // estadística
    public List<SaveEntry> upgrades    = new List<SaveEntry>();   // id y nivel comprado
    public List<SaveEntry> consumables = new List<SaveEntry>();   // id y cantidad
    public List<string>    activeContracts = new List<string>();  // contratos firmados y sin cumplir
    public List<string>    doneContracts   = new List<string>();  // contratos ya cumplidos
    public List<string>    claimedContracts = new List<string>(); // cumplidos y ya cobrados en el puesto
    public List<string>    equippedUpgrades = new List<string>(); // muescas: mejoras compradas que el jugador lleva equipadas
    public int             notchesBought;                         // muescas extra compradas, además de las 3 iniciales
    public int             levelReached;                               // nivel más alto al que ha llegado el jugador, para los desbloqueos de la tienda

    [Serializable]
    public class SaveEntry
    {
        public string id;
        public int    value;
        public SaveEntry(string id, int value) { this.id = id; this.value = value; }
    }
}

/// <summary>
/// Progreso del jugador, estático y accesible desde cualquier escena.
///   - Monedas del monedero (AddCoins y TrySpendCoins).
///   - Mejoras permanentes por niveles (GetLevel y SetLevel).
///   - Consumibles con cantidad (GetCount, AddCount y TryUseConsumable).
/// Se guarda en disco automáticamente con cada cambio. Los sistemas del juego lo consultan al arrancar, por ejemplo
/// para saber qué nivel tiene el Auto-latido o si quedan vidas extra.
/// </summary>
public static class PlayerProgress
{
    public static event Action<int> OnCoinsChanged;   // nuevo total de monedas
    public static event Action      OnChanged;        // cualquier cambio (compras, consumibles...)

    private static SaveData _data;
    private static string   SavePath => Path.Combine(Application.persistentDataPath, "save.json");

    private static SaveData Data
    {
        get
        {
            if (_data == null) Load();
            return _data;
        }
    }

    // -=+=- Monedas -=+=-

    public static int Coins => Data.coins;

    public static void AddCoins(int amount)
    {
        if (amount <= 0) return;
        Data.coins += amount;
        Data.totalCoinsEarned += amount;
        Save();
        OnCoinsChanged?.Invoke(Data.coins);
        OnChanged?.Invoke();
    }

    public static bool TrySpendCoins(int amount)
    {
        if (amount < 0 || Data.coins < amount) return false;
        Data.coins -= amount;
        Save();
        OnCoinsChanged?.Invoke(Data.coins);
        OnChanged?.Invoke();
        return true;
    }

    // -=+=- Mejoras permanentes, por niveles -=+=-

    /// <summary>
    /// Con Suspended = true, todas las mejoras y consumibles cuentan como 0, ya que la pelea del jefe se juega sin
    /// modificadores. Lo guardado no se toca, así que al terminar la pelea todo vuelve a estar como antes.
    /// </summary>
    public static bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            OnChanged?.Invoke();   // para que los sistemas se actualicen (jeringuilla, HUD...)
        }
    }
    private static bool _suspended;

    /// <summary>
    /// Nivel efectivo de una mejora, que es el que usan los sistemas del juego. Vale 0 si está suspendida (jefe o
    /// contrato "A pelo") o si el jugador la ha apagado en la tienda.
    /// </summary>
    public static int GetLevel(string id)
    {
        // En el modo prueba y el nivel tutorial, el "Crosshair rítmico" va siempre equipado, y el resto de mejoras funcionan como siempre.
        if (id == ShopIds.RhythmSight && (StudySession.Active || (CourseBuilder.Instance != null && !LevelEndManager.Finished))) return Mathf.Max(1, Get(Data.upgrades, id));
        return _suspended || !Data.equippedUpgrades.Contains(id) ? 0 : Get(Data.upgrades, id);
    }


    /// <summary>Nivel comprado de una mejora, aunque esté apagada o suspendida. Se usa en la tienda y para los desbloqueos.</summary>
    public static int GetOwnedLevel(string id) => Get(Data.upgrades, id);

    // -=+=- Muescas (mejoras equipadas) -=+=-

    /// <summary>Indica si la mejora está equipada en una muesca, ya que solo funcionan las equipadas.</summary>
    public static bool IsUpgradeEnabled(string id) => Data.equippedUpgrades.Contains(id);
    public static IReadOnlyList<string> EquippedUpgrades => Data.equippedUpgrades;

    /// <summary>Equipa o quita una mejora. Equipar falla si el jugador no la tiene o si no hay muescas libres (ver Notches).</summary>
    public static bool SetUpgradeEnabled(string id, bool enabled)
    {
        if (enabled == IsUpgradeEnabled(id)) return true;
        if (enabled)
        {
            if (GetOwnedLevel(id) <= 0 || !Notches.CanEquip(id)) return false;
            Data.equippedUpgrades.Add(id);
        }
        else Data.equippedUpgrades.Remove(id);
        Save();
        OnChanged?.Invoke();
        return true;
    }

    public static int NotchesBought => Data.notchesBought;

    /// <summary>Nivel más alto al que ha llegado el jugador (1, 2, 3...), que desbloquea objetos de la tienda (ShopUnlocks).</summary>
    public static int LevelReached => Data.levelReached;

    public static void ReachLevel(int level)
    {
        if (level <= Data.levelReached) return;
        Data.levelReached = level;
        Save();
        OnChanged?.Invoke();
    }

    public static void AddNotch()
    {
        Data.notchesBought++;
        Save();
        OnChanged?.Invoke();
    }

    public static void SetLevel(string id, int level)
    {
        Set(Data.upgrades, id, Mathf.Max(0, level));
        Save();
        OnChanged?.Invoke();
    }

    // -=+=- Consumibles, que se gastan -=+=-

    public static int GetCount(string id)
    {
        return _suspended ? 0 : Get(Data.consumables, id);
    }

    public static void AddCount(string id, int amount)
    {
        Set(Data.consumables, id, Mathf.Max(0, Get(Data.consumables, id) + amount));
        Save();
        OnChanged?.Invoke();
    }

    /// <summary>Gasta una unidad del consumible, si hay. Devuelve si se ha podido.</summary>
    public static bool TryUseConsumable(string id)
    {
        int n = GetCount(id);
        if (n <= 0) return false;
        AddCount(id, -1);
        return true;
    }

    // -=+=- Contratos -=+=-

    public static IReadOnlyList<string> ActiveContracts => Data.activeContracts;
    public static bool IsContractActive(string id) => Data.activeContracts.Contains(id);
    public static bool IsContractDone(string id)   => Data.doneContracts.Contains(id);

    public static void SetContractActive(string id, bool active)
    {
        if (active == IsContractActive(id)) return;
        if (active) Data.activeContracts.Add(id); else Data.activeContracts.Remove(id);
        Save();
        StudyTelemetry.ContractEvent(active ? "firmar" : "rescindir", id);   // modo prueba
        OnChanged?.Invoke();
    }

    /// <summary>Indica si el contrato está cumplido, pero todavía sin cobrar, por lo que hay que ir al puesto de contratos a por el dinero.</summary>
    public static bool IsContractClaimable(string id) => Data.doneContracts.Contains(id) && !Data.claimedContracts.Contains(id);

    /// <summary>Cobra un contrato cumplido, suma 'reward' monedas y lo marca como cobrado. Devuelve si se ha podido.</summary>
    public static bool ClaimContract(string id, int reward)
    {
        if (!IsContractClaimable(id)) return false;
        Data.claimedContracts.Add(id);
        Data.coins += Mathf.Max(0, reward);
        Data.totalCoinsEarned += Mathf.Max(0, reward);
        Save();
        StudyTelemetry.ContractEvent("cobrar", id);   // modo prueba
        OnCoinsChanged?.Invoke(Data.coins);
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>Marca el contrato como cumplido, por lo que deja de estar activo. No paga, ya que se cobra en el puesto (ClaimContract).</summary>
    public static void CompleteContract(string id)
    {
        Data.activeContracts.Remove(id);
        if (!Data.doneContracts.Contains(id)) Data.doneContracts.Add(id);
        Save();
        StudyTelemetry.ContractEvent("cumplido", id);   // modo prueba
        OnChanged?.Invoke();
    }

    /// <summary>Depuración. Olvida los contratos cumplidos, que se podrán volver a firmar.</summary>
    public static void ClearDoneContracts()
    {
        Data.doneContracts.Clear();
        Data.claimedContracts.Clear();
        Save();
        OnChanged?.Invoke();
    }

    // -=+=- Guardado -=+=-

    public static void Load()
    {
        try
        {
            _data = File.Exists(SavePath) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)) : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Progreso] No se pudo leer la partida guardada ({e.Message}). Se empieza de cero.");
            _data = null;
        }
        if (_data == null) _data = new SaveData();
        if (_data.upgrades    == null) _data.upgrades    = new List<SaveData.SaveEntry>();
        if (_data.consumables == null) _data.consumables = new List<SaveData.SaveEntry>();
        if (_data.activeContracts == null) _data.activeContracts = new List<string>();
        if (_data.doneContracts   == null) _data.doneContracts   = new List<string>();
        if (_data.equippedUpgrades == null) _data.equippedUpgrades = new List<string>();
        if (_data.claimedContracts == null) _data.claimedContracts = new List<string>();
    }

    public static void Save()
    {
        try { File.WriteAllText(SavePath, JsonUtility.ToJson(Data, true)); }
        catch (Exception e) { Debug.LogWarning($"[Progreso] No se pudo guardar la partida: {e.Message}"); }
    }

    /// <summary>Copia de toda la partida guardada. Se usa en la práctica libre del modo prueba, para dejarlo todo como estaba después.</summary>
    public static string Snapshot() => JsonUtility.ToJson(Data);

    /// <summary>Vuelve a una copia hecha con <see cref="Snapshot"/>.</summary>
    public static void Restore(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try { _data = JsonUtility.FromJson<SaveData>(json); } catch { return; }
        try { File.WriteAllText(SavePath, json); } catch (Exception e) { Debug.LogWarning($"[Progreso] No se pudo guardar: {e.Message}"); }
        Load();   // rellena las listas vacías
        OnCoinsChanged?.Invoke(Data.coins);
        OnChanged?.Invoke();
    }

    /// <summary>Depuración. Borra toda la partida guardada.</summary>
    public static void ResetAll()
    {
        _data = new SaveData();
        Save();
        OnCoinsChanged?.Invoke(0);
        OnChanged?.Invoke();
    }

    // -=+=- Funciones auxiliares -=+=-

    private static int Get(List<SaveData.SaveEntry> list, string id)
    {
        foreach (var e in list) if (e.id == id) return e.value;
        return 0;
    }

    private static void Set(List<SaveData.SaveEntry> list, string id, int value)
    {
        foreach (var e in list) if (e.id == id) { e.value = value; return; }
        list.Add(new SaveData.SaveEntry(id, value));
    }
}
