using TMPro;
using UnityEngine;

/// <summary>
/// Contador de monedas en pantalla (arriba a la derecha): las de esta partida, debajo las del monedero y, si tienes,
/// las VIDAS EXTRA que te quedan.
/// Se crea solo en cuanto hay monedas en el nivel (Coin llama a Ensure()); también puedes ponerlo a mano.
/// </summary>
[AddComponentMenu("HUD/Coin HUD")]
public class CoinHUD : MonoBehaviour
{
    public static CoinHUD Instance { get; private set; }

    private TextMeshProUGUI _run, _total, _lives;
    private RectTransform   _group;
    private float           _pop;

    public static void Ensure()
    {
        if (Instance != null) return;
        new GameObject("CoinHUD").AddComponent<CoinHUD>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    private void OnEnable()
    {
        LevelCoins.OnRunCoinsChanged  += OnRun;
        PlayerProgress.OnCoinsChanged += OnTotal;
        PlayerProgress.OnChanged      += OnProgress;
    }

    private void OnDisable()
    {
        LevelCoins.OnRunCoinsChanged  -= OnRun;
        PlayerProgress.OnCoinsChanged -= OnTotal;
        PlayerProgress.OnChanged      -= OnProgress;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Build()
    {
        Canvas c = UIKit.CreateCanvas("CoinHUDCanvas", 90, transform);
        _group = UIKit.Node("Coins", c.transform);
        UIKit.Place(_group, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(320f, 90f));

        var icon = UIKit.CoinIcon(_group, 46f);
        UIKit.Place(icon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(46f, 46f));

        _run = UIKit.Label("Run", _group, "0", 44f, Color.white, TextAlignmentOptions.Right, true);
        UIKit.Place(_run.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-58f, 4f), new Vector2(250f, 54f));
        _run.outlineWidth = 0.2f; _run.outlineColor = new Color(0f, 0f, 0f, 0.8f);

        _total = UIKit.Label("Total", _group, "", 22f, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Right);
        UIKit.Place(_total.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -52f), new Vector2(320f, 30f));

        // Vidas extra (solo si tienes alguna).
        _lives = UIKit.Label("Lives", _group, "", 26f, new Color(1f, 0.35f, 0.4f), TextAlignmentOptions.Right, true);
        UIKit.Place(_lives.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -84f), new Vector2(360f, 110f));
        _lives.alignment = TextAlignmentOptions.TopRight;
        _lives.outlineWidth = 0.2f; _lives.outlineColor = new Color(0f, 0f, 0f, 0.8f);

        OnRun(LevelCoins.RunCoins);
        OnTotal(PlayerProgress.Coins);
        OnProgress();
    }

    private void OnRun(int n)
    {
        if (_run == null) return;
        _run.text = n.ToString();
        if (n > 0) _pop = 1f;
    }

    private void OnTotal(int n)
    {
        if (_total != null) _total.text = $"monedero: {n}";
    }

    private int _lastLives = -1;

    private void OnProgress()
    {
        if (_lives == null) return;
        int lives   = PlayerProgress.GetCount(ShopIds.ExtraLife);
        int shields = PlayerProgress.GetCount(ShopIds.Shield);
        int cps     = PlayerProgress.GetCount(ShopIds.Checkpoint);
        var sb = new System.Text.StringBuilder();
        if (lives   > 0) sb.AppendLine($"VIDAS EXTRA ×{lives}");
        if (shields > 0) sb.AppendLine($"<color=#5AA0FF>REBOBINADOS ×{shields}</color>");
        if (cps     > 0) sb.AppendLine($"<color=#6CE07C>CHECKPOINTS ×{cps}</color>");
        _lives.text = sb.ToString();
        int n = lives + shields + cps;
        if (_lastLives >= 0 && n < _lastLives) _pop = 1f;   // se acaba de gastar algo
        _lastLives = n;
    }

    private void Update()
    {
        // Pequeño "pop" al recoger una moneda.
        if (_pop > 0f)
        {
            _pop = Mathf.MoveTowards(_pop, 0f, Time.unscaledDeltaTime * 5f);
            _group.localScale = Vector3.one * (1f + 0.18f * _pop * _pop);
        }
    }
}
