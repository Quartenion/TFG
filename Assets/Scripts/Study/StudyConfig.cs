/// <summary>
/// CONFIGURACIONES que se comparan en el estudio. Cambian dos cosas de la barra de energía:
///   • ActionCostMult  → cuánto gastan las acciones (saltar, deslizarse, dash…).
///   • MissPenaltyMult → cuánta energía quita cada fallo al hacer clic.
/// Fuera del modo prueba siempre es «Normal» (×1). Ajusta los valores aquí.
/// </summary>
public static class StudyConfig
{
    public enum Profile { Normal, Permisiva, Exigente }

    public static Profile Current = Profile.Normal;

    public static float ActionCostMult  => Current == Profile.Permisiva ? 0.6f : Current == Profile.Exigente ? 1.4f : 1f;
    public static float MissPenaltyMult => Current == Profile.Permisiva ? 0.5f : Current == Profile.Exigente ? 1.6f : 1f;

    /// <summary>Letra neutra que se enseña al jugador (no sabe cuál es cuál).</summary>
    public static string Label(Profile p) => p == Profile.Permisiva ? "A" : p == Profile.Exigente ? "B" : "N";
}
