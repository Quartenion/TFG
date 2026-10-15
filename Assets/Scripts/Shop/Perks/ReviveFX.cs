using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Efecto en pantalla al gastar una VIDA EXTRA: destello rojo + «¡VIDA EXTRA!» y, en el beat en que vuelves,
/// un segundo latido (destello más fuerte y el texto que «late»). Se destruye solo.
/// </summary>
public class ReviveFX : MonoBehaviour
{
    private Image           _flash;
    private TextMeshProUGUI _title, _sub;
    private float _t, _beatT = -1f;
    private const float Life = 2.2f;

    public static ReviveFX Show(int livesLeft) =>
        ShowMessage(Loc.T("¡VIDA EXTRA!", "EXTRA LIFE!"), livesLeft > 0 ? Loc.T($"Te quedan {livesLeft}", $"{livesLeft} left") : Loc.T("Era la última", "That was the last one"));

    /// <summary>Mismo efecto con otro texto (Adrenalina, Paracaídas…).</summary>
    public static ReviveFX ShowMessage(string title, string sub, Color? flash = null)
    {
        var go = new GameObject("ReviveFX");
        var fx = go.AddComponent<ReviveFX>();
        if (flash.HasValue) fx._flashColor = flash.Value;
        // Solo letras y números (sin «◀◀», «¡!», «+»…) y con la fuente del juego.
        fx.Build(UIFonts.LettersOnly(title), UIFonts.LettersOnly(sub ?? ""));
        return fx;
    }

    private Color _flashColor = new Color(0.8f, 0.02f, 0.05f, 0.55f);

    private void Build(string titleText, string sub)
    {
        Canvas c = UIKit.CreateCanvas("ReviveCanvas", 800, transform);

        _flash = UIKit.Panel("Flash", c.transform, _flashColor, false);
        UIKit.Stretch(_flash.rectTransform);
        _flash.raycastTarget = false;

        _title = UIKit.Label("Title", c.transform, titleText, 96f, Color.white, TextAlignmentOptions.Center, true);
        UIKit.Place(_title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(1400f, 130f));
        UIFonts.Apply(_title, true);
        _title.outlineWidth = 0.25f; _title.outlineColor = new Color(0.3f, 0f, 0f, 1f);

        _sub = UIKit.Label("Sub", c.transform, sub, 40f, new Color(1f, 0.85f, 0.85f), TextAlignmentOptions.Center, true);
        UIKit.Place(_sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1000f, 60f));
        UIFonts.Apply(_sub, false);
    }

    /// <summary>El corazón vuelve a latir (en el beat).</summary>
    public void Beat() => _beatT = 0f;

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _t += dt;

        // Destello: se apaga poco a poco; en el latido vuelve a encenderse más fuerte.
        float a = 0.55f * Mathf.Exp(-_t * 2.5f);
        float s = 1f;
        if (_beatT >= 0f)
        {
            _beatT += dt;
            a = Mathf.Max(a, 0.7f * Mathf.Exp(-_beatT * 4f));
            s = 1f + 0.25f * Mathf.Exp(-_beatT * 6f);
        }
        _flash.color = new Color(_flashColor.r, _flashColor.g, _flashColor.b, a);
        _title.rectTransform.localScale = Vector3.one * s;

        float textA = Mathf.Clamp01((Life - _t) / 0.5f);
        _title.alpha = textA;
        _sub.alpha   = textA;

        if (_t >= Life && (_beatT < 0f || _beatT > 0.6f)) Destroy(gameObject);
    }
}
