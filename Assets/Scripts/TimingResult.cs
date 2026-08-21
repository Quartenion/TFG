/// <summary>
/// Resultado de evaluar si un input del jugador cayo dentro de una ventana de ritmo.
/// </summary>
public enum TimingResult
{
    /// <summary>Dentro de la ventana Perfect (muy preciso). Da bonus de energia.</summary>
    Perfect,

    /// <summary>Dentro de la ventana Good (aceptable). Da energia normal.</summary>
    Good,

    /// <summary>Fuera de cualquier ventana. Penalizacion de energia.</summary>
    Miss
}
