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
    public const string Version = "estudio-1";
}
