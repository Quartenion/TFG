using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Aplica los cosméticos puestos a la escena (se crea solo en cada escena con jugador).</summary>
public class CosmeticApplier : MonoBehaviour
{
    private static CosmeticApplier _instance;

    private HeartBeat _heart;
    private Color _heartOriginal;
    private bool _heartSaved;
    private readonly List<Renderer> _hand = new List<Renderer>();
    private readonly List<Renderer> _kayak = new List<Renderer>();
    private MaterialPropertyBlock _mpb;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID     = Shader.PropertyToID("_Color");
    private static readonly int EmissionID  = Shader.PropertyToID("_EmissionColor");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();

    private static void Ensure()
    {
        if (_instance != null || FindAnyObjectByType<PlayerMovement>() == null) return;
        new GameObject("CosmeticApplier").AddComponent<CosmeticApplier>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        _mpb = new MaterialPropertyBlock();
        PlayerProgress.OnChanged += ApplyAll;
    }

    private void OnDestroy()
    {
        PlayerProgress.OnChanged -= ApplyAll;
        if (_instance == this) _instance = null;
    }

    private IEnumerator Start()
    {
        yield return null;   // (que las monedas y la mano se hayan construido)
        // Renderers de la mano/brazo y de los kayaks, guardados AHORA (antes de que el jugador se siente en un kayak).
        foreach (var h in FindObjectsByType<HandAnimationController>()) _hand.AddRange(h.GetComponentsInChildren<Renderer>(true));
        foreach (var a in FindObjectsByType<ArmPlaceholder>())         _hand.AddRange(a.GetComponentsInChildren<Renderer>(true));
        foreach (var k in FindObjectsByType<KayakController>())
            foreach (Renderer r in k.GetComponentsInChildren<Renderer>(true))
                if (r.GetComponentInParent<PlayerMovement>() == null) _kayak.Add(r);
        ApplyAll();
    }

    private void ApplyAll()
    {
        // Corazón del punto de mira
        if (_heart == null) _heart = FindAnyObjectByType<HeartBeat>();
        if (_heart != null)
        {
            if (!_heartSaved) { _heartOriginal = _heart.normalColor; _heartSaved = true; }
            CosmeticDef h = Cosmetics.Equipped(CosmeticSlot.Heart);
            _heart.normalColor = h != null ? h.color : _heartOriginal;
        }

        Tint(_hand, Cosmetics.Equipped(CosmeticSlot.Hand), false);
        Tint(_kayak, Cosmetics.Equipped(CosmeticSlot.Kayak), false);

        var coins = new List<Renderer>();
        foreach (Coin c in FindObjectsByType<Coin>()) coins.AddRange(c.GetComponentsInChildren<Renderer>(true));
        Tint(coins, Cosmetics.Equipped(CosmeticSlot.Coins), true);
    }

    /// <summary>Tiñe con un MaterialPropertyBlock (sin tocar los materiales). null = quitar el tinte.</summary>
    private void Tint(List<Renderer> rends, CosmeticDef c, bool emissive)
    {
        foreach (Renderer r in rends)
        {
            if (r == null) continue;
            if (c == null) { r.SetPropertyBlock(null); continue; }
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorID, c.color);
            _mpb.SetColor(ColorID, c.color);
            if (emissive) _mpb.SetColor(EmissionID, c.color * 0.6f);
            r.SetPropertyBlock(_mpb);
        }
    }
}
