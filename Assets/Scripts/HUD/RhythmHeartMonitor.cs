using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Línea de heart-rate monitor dibujada por código dentro de un RectTransform (el recuadro
/// negro del monitor). La línea base es plana y se desplaza continuamente de derecha a
/// izquierda; cada clic (Pulse) inyecta un latido que entra por la derecha y viaja.
/// El color de la línea refleja el último timing: verde=Perfect, blanco=Good, rojo=Miss.
///
/// SETUP:
///   1. Va en un GameObject de UI (hijo del recuadro del monitor), SIN Image.
///      Es un Graphic: se dibuja solo. Ajusta su RectTransform al interior del recuadro.
///   2. Para recortarla dentro del recuadro: añade un RectMask2D (o Mask) al recuadro padre.
///   3. Conecta RhythmInput.OnTimingResult (en el Inspector) → este objeto → Pulse.
///      (Aparece bajo "Dynamic TimingResult".)
/// </summary>
[AddComponentMenu("HUD/Rhythm Heart Monitor")]
public class RhythmHeartMonitor : MaskableGraphic
{
    [Header("Trazo")]
    [Tooltip("Velocidad de desplazamiento de la línea (px/seg).")]
    public float scrollSpeed = 120f;

    [Tooltip("Grosor de la línea (px).")]
    [Range(1f, 12f)] public float lineThickness = 3f;

    [Tooltip("Separación entre muestras (px). Menor = más suave y más caro.")]
    [Range(2f, 12f)] public float sampleSpacing = 4f;

    [Tooltip("Altura del latido como fracción de la mitad del alto del recuadro.")]
    [Range(0.1f, 1f)] public float amplitude = 0.75f;

    [Tooltip("Duración del latido (segundos) desde que entra hasta que vuelve a plano.")]
    [Range(0.05f, 0.5f)] public float beatDuration = 0.18f;

    [Header("Colores por timing")]
    public Color perfectColor = new Color(0.25f, 1f, 0.35f, 1f);   // verde
    public Color goodColor    = Color.white;                        // blanco
    public Color missColor     = new Color(1f, 0.25f, 0.2f, 1f);    // rojo

    // Instantes (Time.time) de cada latido activo en pantalla.
    private readonly List<float> _beats = new List<float>();

    protected override void OnEnable()
    {
        base.OnEnable();
        color = goodColor;   // línea plana en color "idle" (blanco) al empezar
    }

    /// <summary>Llamar desde RhythmInput.OnTimingResult: inyecta un latido y tiñe la línea.</summary>
    public void Pulse(TimingResult result)
    {
        _beats.Add(Time.time);
        color = ColorFor(result);   // el setter marca el mesh como sucio
    }

    private Color ColorFor(TimingResult r)
        => r == TimingResult.Perfect ? perfectColor
         : r == TimingResult.Good    ? goodColor
                                     : missColor;

    private void Update()
    {
        // Descartar latidos que ya salieron por la izquierda.
        float w       = rectTransform.rect.width;
        float maxAge  = w / Mathf.Max(1f, scrollSpeed) + beatDuration + 0.5f;
        float cutoff  = Time.time - maxAge;
        for (int i = _beats.Count - 1; i >= 0; i--)
            if (_beats[i] < cutoff) _beats.RemoveAt(i);

        SetVerticesDirty();   // redibujar cada frame (scroll continuo)
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect  r    = rectTransform.rect;
        float half = r.height * 0.5f * amplitude;
        int   n    = Mathf.Max(2, Mathf.CeilToInt(r.width / Mathf.Max(1f, sampleSpacing)));

        Vector2 prev = Vector2.zero;
        for (int i = 0; i <= n; i++)
        {
            float x = r.xMin + r.width * i / n;

            // Tiempo (de juego) representado en esta X: la derecha (xMax) es "ahora".
            float t   = Time.time - (r.xMax - x) / Mathf.Max(1f, scrollSpeed);
            float off = 0f;
            for (int b = 0; b < _beats.Count; b++)
            {
                float tau = t - _beats[b];
                if (tau >= 0f && tau <= beatDuration)
                    off += Spike(tau / beatDuration);
            }

            float y = r.center.y + Mathf.Clamp(off, -1f, 1.2f) * half;
            Vector2 p = new Vector2(x, y);

            if (i > 0) AddSegment(vh, prev, p);
            prev = p;
        }
    }

    /// <summary>Forma estilizada de un latido (QRS). u en 0..1 → altura en -0.35..1.</summary>
    private static float Spike(float u)
    {
        if (u < 0.15f) return Mathf.Lerp( 0f,   -0.2f,  u / 0.15f);          // pequeña Q
        if (u < 0.35f) return Mathf.Lerp(-0.2f,  1f,   (u - 0.15f) / 0.2f);  // subida R
        if (u < 0.50f) return Mathf.Lerp( 1f,   -0.35f,(u - 0.35f) / 0.15f); // bajada S
        if (u < 0.70f) return Mathf.Lerp(-0.35f, 0f,   (u - 0.50f) / 0.2f);  // vuelta
        return 0f;
    }

    private void AddSegment(VertexHelper vh, Vector2 a, Vector2 b)
    {
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 1e-4f) return;
        dir /= len;

        Vector2 nrm = new Vector2(-dir.y, dir.x) * (lineThickness * 0.5f);
        Color32 c = color;
        int idx = vh.currentVertCount;

        vh.AddVert(a - nrm, c, Vector2.zero);
        vh.AddVert(a + nrm, c, Vector2.zero);
        vh.AddVert(b + nrm, c, Vector2.zero);
        vh.AddVert(b - nrm, c, Vector2.zero);
        vh.AddTriangle(idx,     idx + 1, idx + 2);
        vh.AddTriangle(idx,     idx + 2, idx + 3);
    }
}
