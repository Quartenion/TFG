using System;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// «Aparición automática» compartida: varios sistemas (mejoras de la tienda, contratos, rebobinado…) no están
/// puestos en las escenas, sino que se crean solos cuando hacen falta. Antes cada uno repetía el mismo bloque
/// (suscribirse a SceneManager.sceneLoaded y, a veces, a PlayerProgress.OnChanged, y comprobar al arrancar).
///
/// Uso, desde un método [RuntimeInitializeOnLoadMethod(AfterSceneLoad)]:
///     AutoSpawn.Register(Ensure);                       // en cada escena cargada
///     AutoSpawn.Register(Ensure, onProgressChange: true); // …y también al comprar/cambiar algo en la tienda
/// 'ensure' debe ser idempotente: comprobar si ya existe y si la escena tiene lo necesario antes de crear nada.
/// </summary>
public static class AutoSpawn
{
    private static readonly List<Action> _onScene = new List<Action>();
    private static bool _hooked;

    public static void Register(Action ensure, bool onProgressChange = false)
    {
        if (ensure == null) return;
        if (!_hooked)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _hooked = true;
        }
        if (!_onScene.Contains(ensure)) _onScene.Add(ensure);
        if (onProgressChange)
        {
            PlayerProgress.OnChanged -= ensure;
            PlayerProgress.OnChanged += ensure;
        }
        ensure();
    }

    /// <summary>
    /// Variante para quien necesita saber QUÉ escena se ha cargado: se suscribe a SceneManager.sceneLoaded (sin
    /// duplicarse) y se llama ya con la escena activa.
    /// </summary>
    public static void Register(UnityAction<Scene, LoadSceneMode> onSceneLoaded)
    {
        if (onSceneLoaded == null) return;
        SceneManager.sceneLoaded -= onSceneLoaded;
        SceneManager.sceneLoaded += onSceneLoaded;
        onSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        foreach (Action a in _onScene.ToArray()) a();
    }
}
