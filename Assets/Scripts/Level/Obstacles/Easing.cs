/// <summary>Curvas de suavizado compartidas por los obstáculos que se mueven al ritmo (BeatOscillator, PivotOscillator).</summary>
public static class Easing
{
    /// <summary>Ease-out bounce estándar: llega al destino y rebota levemente.</summary>
    public static float BounceOut(float t)
    {
        const float n = 7.5625f, d = 2.75f;
        if (t < 1f / d)   { return n * t * t; }
        if (t < 2f / d)   { t -= 1.500f / d; return n * t * t + 0.750f;    }
        if (t < 2.5f / d) { t -= 2.250f / d; return n * t * t + 0.937500f; }
                            t -= 2.625f / d; return n * t * t + 0.984375f;
    }
}
