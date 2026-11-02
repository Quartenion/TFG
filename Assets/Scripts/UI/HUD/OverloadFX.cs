using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// EFECTO EN PANTALLA de la «Sobrecarga» y del «Respiro» (ver EnergyManager). Lo añade EnergyManager solo.
///   • Sobrecarga (jeringuilla azul llena): SIN efecto en pantalla (era demasiado estridente); solo queda el latido
///     suave de la cámara del modo azul (LowHealthEffects).
///   • Respiro (tras perder la sangre azul): mecánica OCULTA (showGrace = false). Si se activa: bordes blancos
///     suaves + «RESPIRO», más intensos cuanta más sangre azul se perdió.
/// </summary>
public class OverloadFX : MonoBehaviour
{
    private static readonly Color GraceCol    = new Color(1f, 1f, 1f);

    private RawImage _edges;
    private Texture2D _tex;
    private TextMeshProUGUI _label;
    private EnergyManager _em;

    /// <summary>Mostrar el aviso «RESPIRO» (apagado: el Respiro es una mecánica oculta).</summary>
    public static bool showGrace = false;

    private float _graceFrac;
    private double _graceEnd = double.NegativeInfinity, _graceStart;

    private void Awake()
    {
        _em = GetComponent<EnergyManager>();
        Canvas c = UIKit.CreateCanvas("SobrecargaCanvas", 70, transform);

        // Textura de «bordes»: transparente en el centro y opaca hacia los lados.
        const int N = 64;
        _tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
            float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, d));
            px[y * N + x] = new Color32(255, 255, 255, (byte)(255 * a));
        }
        _tex.SetPixels32(px); _tex.Apply(false);

        var go = new GameObject("Bordes", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(c.transform, false);
        _edges = go.GetComponent<RawImage>();
        _edges.texture = _tex;
        _edges.raycastTarget = false;
        Stretch(_edges.rectTransform, 0f, 0f, 0f, 0f);

        _label = Lbl("Aviso", c.transform, "", FontBig + 6f, TextAlignmentOptions.Center);
        Place(_label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(900f, 70f), new Vector2(0.5f, 1f));

        _edges.enabled = false; _label.text = "";

        EnergyManager.GraceStarted += OnGrace;
    }

    private void OnDestroy()
    {
        EnergyManager.GraceStarted -= OnGrace;
        if (_tex != null) Destroy(_tex);
    }

    private void OnGrace(float frac, float beats)
    {
        BeatManager bm = BeatManager.Instance;
        if (bm == null) return;
        _graceFrac = frac;
        _graceStart = bm.ClockBeats;
        _graceEnd = _graceStart + beats;
    }

    private void Update()
    {
        BeatManager bm = BeatManager.Instance;
        bool hidden = HudHider.IsHidden || bm == null || !bm.HasClock || _em == null;
        double beat = hidden ? 0 : bm.ClockBeats;

        // El «Respiro» es una mecánica OCULTA: las ventanas se amplían igual, pero sin aviso ni bordes en pantalla.
        bool grace = showGrace && !hidden && beat < _graceEnd;

        if (grace)
        {
            float k = 1f - (float)((beat - _graceStart) / System.Math.Max(0.01, _graceEnd - _graceStart));   // se apaga
            Show(GraceCol, (0.15f + 0.35f * _graceFrac) * k, Loc.T("RESPIRO", "BREATHER"), 1f, k);
        }
        else if (_edges.enabled)
        {
            _edges.enabled = false; _label.text = "";
        }
    }

    private void Show(Color col, float alpha, string text, float scale, float textAlpha = 1f)
    {
        _edges.enabled = true;
        _edges.color = new Color(col.r, col.g, col.b, Mathf.Clamp01(alpha));
        _label.text = text;
        _label.color = new Color(col.r, col.g, col.b, Mathf.Clamp01(textAlpha));
        _label.rectTransform.localScale = Vector3.one * scale;
    }
}
