/// <summary>
/// AJUSTES DEL MODO PRUEBA (estudio con jugadores). Pega aquí los enlaces de tus dos Google Forms
/// (las instrucciones están en ESTUDIO_FORMULARIOS.md, en la raíz del proyecto).
/// </summary>
public static class StudySettings
{
    // ── Formulario OCULTO de datos (una sola pregunta de «Párrafo») ────────────
    // URL de envío: la del formulario cambiando «/viewform» por «/formResponse».
    // Ej.: "https://docs.google.com/forms/d/e/1FAIpQLSe.../formResponse"
    public const string DataFormUrl = "https://docs.google.com/forms/d/e/1FAIpQLSdeWotI2YMfe_0pIMb4N9b8jIyhycNUqBeMXbTcWrCu63XKKg/formResponse";
    // Id de la pregunta (sale en el «enlace rellenado previamente»), p. ej. "entry.123456789".
    public const string DataEntryId = "entry.1425205905";

    // ── CUESTIONARIO FINAL (el que rellena el jugador al terminar) ────────────
    // «Enlace rellenado previamente» con la pregunta del código de participante rellenada con la palabra CODIGO.
    // Ej.: "https://docs.google.com/forms/d/e/1FAIpQLSf.../viewform?usp=pp_url&entry.987654321=CODIGO"
    public const string FinalFormUrl = "https://docs.google.com/forms/d/e/1FAIpQLSdyzA2I1MI7x2vVg_rltITNvi6o0Xmne6yJdiECfDm9RkTt-w/viewform?usp=pp_url&entry.448336735=CODIGO";

    // ── Seguridad de los datos ────────────────────────────────────────────────
    // Solo se envía a esta dirección exacta (y siempre por HTTPS).
    public const string AllowedHost = "docs.google.com";
    // Clave de la «verificación» que se añade a cada fila (HMAC). Va en Assets/Resources/StudyKey.txt, que está en
    // el .gitignore para que NO se suba a GitHub. (Dentro del juego exportado sí va: frena filas falsas hechas a
    // mano, no a quien descompile el juego.) Si falta el archivo, se avisa y se usa una clave por defecto.
    public static string RowKey
    {
        get
        {
            if (_rowKey == null)
            {
                var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("StudyKey");
                _rowKey = asset != null ? asset.text.Trim() : "";
                if (_rowKey.Length == 0)
                {
                    UnityEngine.Debug.LogWarning("[Estudio] Falta Assets/Resources/StudyKey.txt: se usa una clave por defecto.");
                    _rowKey = "clave-por-defecto-cambiala";
                }
            }
            return _rowKey;
        }
    }
    private static string _rowKey;
    public const int RequestTimeoutSeconds = 15;

    // ── Sesión ────────────────────────────────────────────────────────────────
    public const int MaxAttemptsPerPart = 3;   // muertes permitidas por parte; después se pasa a la siguiente
    public const string Version = "estudio-2";
}

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
