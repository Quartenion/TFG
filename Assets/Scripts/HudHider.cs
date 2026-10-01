using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Esconde TODO el HUD del jugador mientras se muestra un menú a pantalla completa (pausa, game over):
///   • los hermanos del panel dentro de su Canvas (mirilla, barra de energía, 3-2-1…), salvo los fundidos a negro;
///   • el resto de Canvas raíz (monedas, «F1 · Controles», contratos…);
///   • los avisos OnGUI y los carteles del tutorial (miran HudHider.IsHidden).
/// Restore() lo deja todo como estaba. Al cargar otra escena se reinicia solo.
/// </summary>
public static class HudHider
{
    /// <summary>Hermanos que NO se esconden (fundidos y oscurecido de pantalla que usa el game over).</summary>
    private static readonly HashSet<string> Keep = new HashSet<string> { "BlackOverlay", "Vision Dim", "BackgroundDim" };

    private static readonly List<GameObject> _objects = new List<GameObject>();
    private static readonly List<Canvas> _canvases = new List<Canvas>();

    public static bool IsHidden { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Clear();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) Clear(); }

    private static void Clear()
    {
        _objects.Clear();
        _canvases.Clear();
        IsHidden = false;
    }

    /// <summary>Esconde todo menos 'keep' (el panel del menú) y lo que haya dentro de él.</summary>
    public static void Hide(GameObject keep)
    {
        Restore();
        IsHidden = true;
        if (keep == null) return;

        Transform panel = keep.transform;
        Canvas own = keep.GetComponentInParent<Canvas>(true);
        Canvas ownRoot = own != null ? own.rootCanvas : null;

        for (Transform t = panel; t != null && t.parent != null; t = t.parent)
        {
            foreach (Transform sibling in t.parent)
                if (sibling != t && sibling.gameObject.activeSelf && !Keep.Contains(sibling.name))
                {
                    sibling.gameObject.SetActive(false);
                    _objects.Add(sibling.gameObject);
                }
            if (ownRoot != null && t.parent == ownRoot.transform) break;
        }

        foreach (Canvas c in Object.FindObjectsByType<Canvas>())
            if (c.isRootCanvas && c != ownRoot && c.enabled)
            {
                c.enabled = false;
                _canvases.Add(c);
            }
    }

    /// <summary>Vuelve a mostrar lo que se escondió.</summary>
    public static void Restore()
    {
        foreach (var go in _objects) if (go != null) go.SetActive(true);
        foreach (var c in _canvases) if (c != null) c.enabled = true;
        Clear();
    }
}
