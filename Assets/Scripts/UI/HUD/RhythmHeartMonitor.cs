using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Línea de monitor de electrocardiograma dibujada por código dentro de un RectTransform (el recuadro negro del
/// monitor). La línea base es plana y se desplaza continuamente de derecha a izquierda, y cada clic (Pulse) añade un
/// latido que entra por la derecha y la recorre. El color de la línea indica el último resultado, verde para
/// Perfect, blanco para Good y rojo para Miss.
///
/// Configuración.
///   1. Va en un GameObject de interfaz, hijo del recuadro del monitor, sin Image. Es un Graphic, así que se dibuja
///      solo. Su RectTransform se ajusta al interior del recuadro.
///   2. Para recortar la línea dentro del recuadro, se añade un RectMask2D (o Mask) al recuadro padre.
///   3. Se conecta solo a RhythmInput.OnTimingResult, por lo que no hace falta conectarlo en el Inspector.
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

    // Instantes (Time.time) de cada latido que está en pantalla.
    private readonly List<float> _beats = new List<float>();

    private RhythmInput _input;
    private float _nextHook;

    protected override void OnEnable()
    {
        base.OnEnable();
        color = goodColor;   // al empezar, la línea es plana y del color en reposo (blanco)
        Hook();
    }

    protected override void OnDisable()
    {
        if (_input != null && _input.OnTimingResult != null) _input.OnTimingResult.RemoveListener(Pulse);
        _input = null;
        base.OnDisable();
    }

    /// <summary>Se suscribe al resultado de cada latido del jugador, una sola vez.</summary>
    private void Hook()
    {
        if (_input != null || !Application.isPlaying) return;
        _input = FindAnyObjectByType<RhythmInput>();
        if (_input == null) return;
        if (_input.OnTimingResult == null) _input.OnTimingResult = new UnityEngine.Events.UnityEvent<TimingResult>();
        _input.OnTimingResult.RemoveListener(Pulse);
        _input.OnTimingResult.AddListener(Pulse);
    }

    /// <summary>Se llama desde RhythmInput.OnTimingResult. Añade un latido y cambia el color de la línea.</summary>
    public void Pulse(TimingResult result)
    {
        _beats.Add(Time.time);
        color = ColorFor(result);   // el setter marca la malla para volver a dibujarla
    }

    private Color ColorFor(TimingResult r)
        => r == TimingResult.Perfect ? perfectColor
         : r == TimingResult.Good    ? goodColor
                                     : missColor;

    private void Update()
    {
        if (_input == null && Time.unscaledTime >= _nextHook) { _nextHook = Time.unscaledTime + 1f; Hook(); }   // por si RhythmInput aparece más tarde
        // Se descartan los latidos que ya han salido por la izquierda.
        float w       = rectTransform.rect.width;
        float maxAge  = w / Mathf.Max(1f, scrollSpeed) + beatDuration + 0.5f;
        float cutoff  = Time.time - maxAge;
        for (int i = _beats.Count - 1; i >= 0; i--)
            if (_beats[i] < cutoff) _beats.RemoveAt(i);

        SetVerticesDirty();   // se vuelve a dibujar en cada fotograma, ya que la línea se desplaza continuamente
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

            // Tiempo de juego que corresponde a esta X. El borde derecho (xMax) es el instante actual.
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

    /// <summary>Forma simplificada de un latido (complejo QRS). Para u entre 0 y 1, devuelve una altura entre -0,35 y 1.</summary>
    private static float Spike(float u)
    {
        if (u < 0.15f) return Mathf.Lerp( 0f,   -0.2f,  u / 0.15f);          // pequeña onda Q
        if (u < 0.35f) return Mathf.Lerp(-0.2f,  1f,   (u - 0.15f) / 0.2f);  // subida de la onda R
        if (u < 0.50f) return Mathf.Lerp( 1f,   -0.35f,(u - 0.35f) / 0.15f); // bajada de la onda S
        if (u < 0.70f) return Mathf.Lerp(-0.35f, 0f,   (u - 0.50f) / 0.2f);  // vuelta a la línea base
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
