using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MODO PRUEBA (estudio con jugadores). Guía al participante por 5 partes:
///   1. Nivel 1 de práctica con tutorial (configuración normal; no se compara).
///   2. Nivel 1 con la configuración A o B.          ┐ el orden (AB / BA) se sortea por participante
///   3. Nivel 1 con la otra configuración.           ┘ para que aprender el nivel no favorezca siempre a la misma
///   4. Nivel 2 (configuración normal).               ┐ opcionales: comparan niveles con mecánicas distintas
///   5. Nivel 3 (configuración normal).               ┘ (kayak; mirada + jefe) y evalúan la TIENDA
/// Entre partes se vuelve al menú principal, donde StudyHub enseña el cuestionario corto y la siguiente parte.
/// En las partes 1-3 la tienda y las mejoras no cuentan (todos juegan igual para comparar las configuraciones).
/// En las partes 4-5 se abre la tienda: el jugador gasta las monedas ganadas y se registra qué compra y equipa.
/// En las partes 4-5 también se pueden firmar CONTRATOS (bloque rojo): se registran firmas, cumplidos y cobros.
/// </summary>
public static class StudySession
{
    public struct Part
    {
        public string scene, titleEs, titleEn, descEs, descEn;
        public string title       => Loc.T(titleEs, titleEn);
        public string description => Loc.T(descEs, descEn);
        public StudyConfig.Profile config;
        public bool tutorial, questionnaire, preference, shop;   // tutorial = carteles de consejos (práctica y mecánicas nuevas de los niveles 2 y 3)
    }

    public const string MenuScene = "MainMenu";
    public const int MainParts = 3;   // las 3 primeras son obligatorias

    public static bool   Active { get; private set; }
    public static string Participant { get; private set; } = "";
    public static bool   OrderAB { get; private set; }
    public static int    Step { get; private set; } = -1;      // parte actual (0..Plan.Length-1)
    public static int    Attempts { get; private set; }        // intentos en la parte actual
    public static bool   PartCompleted { get; private set; }   // ¿llegó a la meta en esta parte?
    public static bool   AwaitingReturn { get; private set; }  // jugando una parte; al volver al menú, cuestionario

    public static Part[] Plan { get; private set; }

    public static bool InLevel => Active && AwaitingReturn && SceneManager.GetActiveScene().name != MenuScene;
    public static bool TutorialStep => InLevel && Step >= 0 && Plan[Step].tutorial;
    public static Part Current => Plan[Mathf.Clamp(Step, 0, Plan.Length - 1)];

    /// <summary>¿Se puede usar la tienda (y cuentan las mejoras) en esta parte?</summary>
    public static bool ShopAllowed => InLevel && Current.shop;

    // ── Inicio / fin ──────────────────────────────────────────────────────────

    /// <summary>Empieza una sesión nueva: código anónimo, orden sorteado y plan de partes.</summary>
    public static void Begin()
    {
        const string abc = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // sin 0/O ni 1/I
        var rng = new System.Random();
        char[] code = new char[5];
        for (int i = 0; i < code.Length; i++) code[i] = abc[rng.Next(abc.Length)];
        Participant = "P-" + new string(code);
        OrderAB = rng.Next(2) == 0;

        StudyConfig.Profile first  = OrderAB ? StudyConfig.Profile.Permisiva : StudyConfig.Profile.Exigente;
        StudyConfig.Profile second = OrderAB ? StudyConfig.Profile.Exigente  : StudyConfig.Profile.Permisiva;
        Plan = new[]
        {
            new Part { scene = "Level1", config = StudyConfig.Profile.Normal, tutorial = true, titleEs = "Práctica: Nivel 1 con tutorial", titleEn = "Practice: Level 1 with tutorial",
                       descEs = "Juega el Nivel 1 con consejos en pantalla para aprender los controles. Esta partida es solo para practicar.",
                       descEn = "Play Level 1 with on-screen hints to learn the controls. This run is just for practice." },
            new Part { scene = "Level1", config = first, questionnaire = true, titleEs = "Nivel 1 · partida 1", titleEn = "Level 1 · run 1",
                       descEs = "Vuelve a jugar el Nivel 1. Esta vez sin consejos. Al terminar responderás unas preguntas cortas.",
                       descEn = "Play Level 1 again. This time without hints. When you finish you will answer a few short questions." },
            new Part { scene = "Level1", config = second, questionnaire = true, preference = true, titleEs = "Nivel 1 · partida 2", titleEn = "Level 1 · run 2",
                       descEs = "Una última vez el Nivel 1. Puede que notes que la energía funciona algo distinto: juega con normalidad.",
                       descEn = "Level 1 one last time. You may notice that energy works a bit differently: just play normally." },
            new Part { scene = "Level2", config = StudyConfig.Profile.Normal, tutorial = true, questionnaire = true, shop = true, titleEs = "Nivel 2", titleEn = "Level 2",
                       descEn = "A new level with kayak sections: left or right click on the beat to paddle to each side.\n\n" +
                                "NEW: in the start area there is a SHOP (green block; look at it and press E). Spend the coins you " +
                                "have earned on upgrades and equip them (each upgrade uses «notches»). Buy whatever you want, or nothing.\n" +
                                "There is also a CONTRACT stand (red block): optional challenges that give coins if you complete them " +
                                "(you claim them later at the stand).",
                       descEs = "Un nivel nuevo con secciones en kayak: clic izquierdo o derecho al ritmo para remar hacia cada lado.\n\n" +
                                     "NOVEDAD: en la zona de inicio hay una TIENDA (bloque verde; míralo y pulsa E). Gasta las monedas que has " +
                                     "ganado en mejoras y equípalas (cada mejora ocupa «muescas»). Compra lo que quieras, o nada.\n" +
                                     "También hay un puesto de CONTRATOS (bloque rojo): retos opcionales que dan monedas si los cumples " +
                                     "(se cobran luego en el puesto)." },
            new Part { scene = "Level3", config = StudyConfig.Profile.Normal, tutorial = true, questionnaire = true, shop = true, titleEs = "Nivel 3", titleEn = "Level 3",
                       descEn = "The last level: there are enemies that you disable by LOOKING AT THEM for a moment, and a final boss.\n\n" +
                                "The shop and contracts are back in the start area, with new upgrades and challenges. " +
                                "If you completed any contract in Level 2, claim it here.",
                       descEs = "El último nivel: hay enemigos que se desactivan MIRÁNDOLOS un momento, y un jefe final.\n\n" +
                                     "La tienda y los contratos vuelven a estar en la zona de inicio, con mejoras y retos nuevos. " +
                                     "Si cumpliste algún contrato en el Nivel 2, cóbralo aquí." },
        };
        Step = 0;
        Attempts = 0;
        Active = true;
        PlayerProgress.Suspended = true;   // tienda, mejoras y consumibles fuera: todos juegan igual
        Debug.Log($"[Estudio] Sesión {Participant}, orden {(OrderAB ? "AB" : "BA")}.");
    }

    public static void Finish()
    {
        Active = false;
        AwaitingReturn = false;
        StudyConfig.Current = StudyConfig.Profile.Normal;
        PlayerProgress.Suspended = false;
    }

    /// <summary>
    /// El participante sale al menú desde la pausa a mitad de la prueba: queda DESCALIFICADO. Se registra el abandono,
    /// se cierra la sesión y se borra el progreso de la partida, así la prueba vuelve a empezar desde cero (código nuevo).
    /// </summary>
    public static void Abort()
    {
        if (!Active) return;
        StudyTelemetry.EndAttempt("abandonado");
        StudyTelemetry.Abandon();
        Debug.Log($"[Estudio] Sesión {Participant} abandonada desde la pausa: descalificada.");
        Finish();
        Step = -1;
        Attempts = 0;
        PartCompleted = false;
        Participant = "";
        CheckpointSystem.Clear();
        PlayerProgress.ResetAll();   // (solo se puede llegar aquí en la versión de prueba)
    }

    // ── Partes ────────────────────────────────────────────────────────────────

    /// <summary>Carga el nivel de la parte actual con su configuración.</summary>
    public static void StartPart()
    {
        Attempts = 0;
        PartCompleted = false;
        AwaitingReturn = true;
        StudyConfig.Current = Current.config;
        PlayerProgress.Suspended = !Current.shop;   // mejoras solo en las partes con tienda
        CheckpointSystem.Clear();
        SceneManager.LoadScene(Current.scene);
    }

    /// <summary>Pasa a la siguiente parte (o termina). Devuelve false si ya no quedan.</summary>
    public static bool NextPart()
    {
        Step++;
        return Step < Plan.Length;
    }

    public static void NotifyAttemptStarted() => Attempts++;
    public static void NotifyAttemptEnded(bool completed) { if (completed) PartCompleted = true; }

    /// <summary>
    /// Lo llama GameOverManager al pulsar «Reintentar». Si ya se han gastado los intentos de esta parte, se vuelve
    /// al menú (siguiente paso de la prueba) en vez de recargar el nivel. Devuelve true si se ha encargado él.
    /// </summary>
    public static bool OnRestartRequested()
    {
        if (!InLevel || Attempts < StudySettings.MaxAttemptsPerPart) return false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(MenuScene);
        return true;
    }

    /// <summary>De vuelta en el menú tras una parte: ya no se está jugando.</summary>
    public static void ReturnedToMenu() => AwaitingReturn = false;

    // ── Al cargar un nivel ────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (!Active || s.name == MenuScene) return;
        // Al terminar el nivel, se vuelve al menú (cuestionario + siguiente parte) en vez de ir al siguiente nivel.
        foreach (var end in Object.FindObjectsByType<LevelEndManager>()) end.SetNextScene(MenuScene);
    }
}
