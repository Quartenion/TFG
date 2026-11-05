using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Modo prueba (estudio con jugadores). Todo se juega en la escena "Tutorial" (ver CourseBuilder), a 100 BPM.
///   0. Tutorial, después del consentimiento, en el que cada obstáculo se repite 3 veces con explicaciones. No forma
///      parte del estudio, por lo que no se registran datos. Al terminarlo, el estudio empieza en la sala.
///   1. Recorrido del estudio con la configuración A o B.
///   2. Recorrido con la otra configuración.
///   El orden (AB o BA) se sortea para cada participante, y los dos recorridos son el mismo (fijo, de fácil a difícil).
/// Si el jugador choca o se queda sin energía, reaparece en el último punto de control (gratis), así que todos juegan
/// el recorrido completo. Después de cada parte, el jugador vuelve a la sala del tutorial, donde StudyHub muestra el
/// cuestionario corto y la siguiente parte, que empieza al cruzar la puerta grande. Las puertas pequeñas, la tienda y
/// los contratos están cerrados durante todo el estudio, y no hay mejoras, para que todos jueguen en las mismas condiciones.
/// </summary>
public static class StudySession
{
    public struct Part
    {
        public string scene, titleEs, titleEn, descEs, descEn;
        public string title       => Loc.T(titleEs, titleEn);
        public string description => Keybinds.Fix(Loc.T(descEs, descEn));   // teclas de Ajustes > Controles
        public StudyConfig.Profile config;
        public bool tutorial, questionnaire, preference, shop;   // tutorial: carteles de consejos (práctica y mecánicas nuevas de los niveles 2 y 3)
        public CourseMode course;   // qué se construye en la escena "Tutorial" (ver CourseBuilder)
        public int round;           // número de recorrido, que cambia el orden de los obstáculos
    }

    public const string MenuScene = "MainMenu";
    public const string CourseScene = "Tutorial";   // escena del nivel tutorial y del estudio (CourseBuilder)

    /// <summary>Indica si ya se ha dado el presupuesto de la tienda, en las partes que la usan.</summary>
    public static bool ShopBudgetGiven { get; private set; }

    /// <summary>Da una cantidad fija de monedas, además de las ganadas, para que todos puedan comprar algo en las partes con tienda.</summary>
    public static void GiveShopBudget()
    {
        if (ShopBudgetGiven || !Active) return;
        ShopBudgetGiven = true;
        PlayerProgress.AddCoins(StudySettings.ShopBudget);
    }
    public const int MainParts = 2;   // 2 recorridos (A y B), los dos obligatorios. El tutorial va antes y no cuenta.

    public static bool   Active { get; private set; }
    /// <summary>
    /// Indica si ya ha empezado el estudio, es decir, si se ha cruzado la puerta grande para el primer recorrido. Antes,
    /// en la sala del tutorial, se pueden practicar los obstáculos por las puertas pequeñas.
    /// </summary>
    public static bool   RunsStarted { get; private set; }
    /// <summary>Indica si ya se ha visto en la sala la pantalla de la primera parte, que no se repite al volver de practicar.</summary>
    public static bool   RoomIntroSeen;

    /// <summary>Indica si ya se ha completado la prueba entera en este equipo. Hasta entonces, el botón JUGAR del menú está en gris.</summary>
    public static bool StudyCompleted => PlayerPrefs.GetInt("Study.Completed", 0) == 1;
    public static void MarkStudyCompleted()
    {
        PlayerPrefs.SetInt("Study.Completed", 1);
        // El código se guarda, de forma que lo que se juegue después en el modo normal se registra con el mismo código, como parte "libre".
        PlayerPrefs.SetString("Study.Participant", Participant);
        PlayerPrefs.SetInt("Study.Simulated", Simulated ? 1 : 0);
        PlayerPrefs.SetString("Study.Order", OrderAB ? "AB" : "BA");
        PlayerPrefs.Save();
    }

    /// <summary>Código del participante que completó la prueba en este equipo (vacío si no la ha completado).</summary>
    public static string SavedParticipant => PlayerPrefs.GetString("Study.Participant", "");
    public static bool   SavedSimulated   => PlayerPrefs.GetInt("Study.Simulated", 0) == 1;
    public static string SavedOrder       => PlayerPrefs.GetString("Study.Order", "");

    /// <summary>
    /// Indica si se está jugando después de completar la prueba (modo normal desde JUGAR), fuera del menú. En ese caso
    /// también se registran datos, como parte "libre" y con el mismo código. Lo mismo ocurre en la práctica libre de la prueba.
    /// </summary>
    public static bool PostStudyPlay =>
        SceneManager.GetActiveScene().name != MenuScene &&
        (InFreePlay || (!Active && StudyCompleted && !string.IsNullOrEmpty(SavedParticipant)));
    /// <summary>En la sala, antes de empezar el estudio y con la pantalla de la primera parte ya vista.</summary>
    public static bool RoomRunsPending() => Active && !RunsStarted && RoomIntroSeen;
    public static string Participant { get; private set; } = "";
    public static bool   OrderAB { get; private set; }
    public static int    Step { get; private set; } = -1;      // parte actual (de 0 a Plan.Length - 1)
    public static int    Attempts { get; private set; }        // intentos en la parte actual
    public static bool   PartCompleted { get; private set; }   // si ha llegado a la meta en esta parte
    public static bool   AwaitingReturn { get; private set; }  // jugando una parte, y al volver al menú, cuestionario

    public static Part[] Plan { get; private set; }

    /// <summary>Indica si se está jugando una parte de la prueba (no la práctica libre).</summary>
    public static bool InLevel => Active && AwaitingReturn && !FreePlay && SceneManager.GetActiveScene().name != MenuScene;

    /// <summary>
    /// Indica si se está dentro de la prueba y fuera del menú, es decir, en el tutorial, la sala, las prácticas de las
    /// puertas o los recorridos. Salir de aquí al menú (desde la pausa o con el botón de salir del game over) cuenta como
    /// abandonar la prueba (ver AbandonDialog).
    /// </summary>
    public static bool InSession => Active && !FreePlay && SceneManager.GetActiveScene().name != MenuScene;

    /// <summary>
    /// Práctica libre, después de las partes principales. Se repite el último nivel con todo desbloqueado (la tienda con
    /// todas las mejoras, monedas de práctica y contratos), sin límite de intentos, sin cuestionario y sin registrar datos.
    /// Al volver al menú, la partida guardada queda exactamente como estaba, por lo que lo comprado en la práctica no cuenta.
    /// </summary>
    public static bool FreePlay { get; private set; }
    public static bool InFreePlay => Active && FreePlay && SceneManager.GetActiveScene().name != MenuScene;
    public const int FreePlayCoins = 5000;
    private static string _freePlaySave;

    /// <summary>Nivel que se acaba de jugar, que es el que se repite en la práctica libre.</summary>
    public static Part LastPlayed => Plan[Mathf.Clamp(Step - 1, 0, Plan.Length - 1)];

    public static void StartFreePlay()
    {
        if (!Active) return;
        _freePlaySave = PlayerProgress.Snapshot();
        FreePlay = true;
        AwaitingReturn = true;
        Attempts = 0;
        StudyConfig.Current = StudyConfig.Profile.Normal;
        PlayerProgress.Suspended = false;
        PlayerProgress.AddCoins(FreePlayCoins);
        CheckpointSystem.Clear();
        SceneManager.LoadScene(LastPlayed.scene);
    }

    /// <summary>De vuelta en el menú después de la práctica libre, todo vuelve a estar como antes.</summary>
    public static void EndFreePlay()
    {
        if (!FreePlay) return;
        FreePlay = false;
        AwaitingReturn = false;
        PlayerProgress.Restore(_freePlaySave);
        _freePlaySave = null;
        PlayerProgress.Suspended = true;
        CheckpointSystem.Clear();
    }
    public static bool TutorialStep => InLevel && Step >= 0 && Plan[Step].tutorial;
    public static Part Current => Plan[Mathf.Clamp(Step, 0, Plan.Length - 1)];

    /// <summary>Indica si en esta parte se puede usar la tienda y cuentan las mejoras.</summary>
    public static bool ShopAllowed => (InLevel && Current.shop) || InFreePlay;

    // -=+=- Inicio y fin -=+=-

    /// <summary>
    /// Sesión simulada, para probar en el editor desde la escena Tutorial. Todo funciona igual, pero el código empieza por
    /// "TEST-" y los datos no se envían al formulario, sino que solo se guardan en el CSV local.
    /// </summary>
    public static bool Simulated { get; private set; }

    /// <summary>Empieza una sesión nueva, con un código anónimo, el orden sorteado y el plan de partes.</summary>
    public static void Begin(bool simulated = false)
    {
        Simulated = simulated;
        const string abc = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // sin 0, O, 1 ni I
        var rng = new System.Random();
        char[] code = new char[5];
        for (int i = 0; i < code.Length; i++) code[i] = abc[rng.Next(abc.Length)];
        Participant = (simulated ? "TEST-" : "P-") + new string(code);
        OrderAB = rng.Next(2) == 0;

        StudyConfig.Profile first  = OrderAB ? StudyConfig.Profile.Permisiva : StudyConfig.Profile.Exigente;
        StudyConfig.Profile second = OrderAB ? StudyConfig.Profile.Exigente  : StudyConfig.Profile.Permisiva;
        Plan = new[]
        {
            new Part { scene = CourseScene, course = CourseMode.Study, round = 1, config = first, questionnaire = true,
                       titleEs = "Recorrido 1", titleEn = "Run 1",
                       descEs = "Un recorrido con los obstáculos que has aprendido, ordenados de los más fáciles a los más difíciles. Durante " +
                                "el recorrido sonará el metrónomo y el monitor del corazón te indicará qué acción toca en cada momento. " +
                                "Al terminar, responderás unas preguntas cortas.",
                       descEn = "A run with the obstacles you have learned, from the easiest to the hardest. The metronome will play and " +
                                "the heart monitor will show you which action comes next. When you finish you will answer a few short questions." },
            new Part { scene = CourseScene, course = CourseMode.Study, round = 2, config = second, questionnaire = true, preference = true,
                       titleEs = "Recorrido 2", titleEn = "Run 2",
                       descEs = "El mismo recorrido otra vez. Es posible que notes que la energía funciona de forma algo distinta, pero " +
                                "juega con normalidad.",
                       descEn = "The same run again. You may notice that energy works a bit differently: just play normally." },
        };
        Step = 0;
        Attempts = 0;
        RunsStarted = false;
        RoomIntroSeen = false;
        CourseBuilder.ResetTutorialRun();   // en una prueba nueva (por ejemplo, después de abandonar), el tutorial vuelve a explicarlo todo
        ShopBudgetGiven = false;
        Active = true;
        PlayerProgress.Suspended = true;   // sin tienda, mejoras ni consumibles, para que todos jueguen igual
        Debug.Log($"[Estudio] Sesión {Participant}, orden {(OrderAB ? "AB" : "BA")}.");
    }

    public static void Finish()
    {
        EndFreePlay();
        Active = false;
        AwaitingReturn = false;
        StudyConfig.Current = StudyConfig.Profile.Normal;
        PlayerProgress.Suspended = false;
    }

    /// <summary>
    /// El participante sale al menú desde la pausa a mitad de la prueba, por lo que queda descalificado. Se registra el
    /// abandono, se cierra la sesión y se borra el progreso de la partida, de forma que la prueba vuelve a empezar desde
    /// cero, con un código nuevo.
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
        PlayerProgress.ResetAll();   // solo se puede llegar aquí en la versión de prueba
    }

    // -=+=- Partes -=+=-

    /// <summary>Carga el nivel de la parte actual con su configuración.</summary>
    public static void StartPart()
    {
        PreparePart();
        SceneManager.LoadScene(Current.scene);
    }

    /// <summary>
    /// Prepara la parte actual (configuración, intentos...) sin cargar la escena, ya que en la sala del tutorial la carga
    /// la puerta grande al cruzarla. En el estudio no hay mejoras ni consumibles, para que todos jueguen igual.
    /// </summary>
    public static void PreparePart()
    {
        RunsStarted = true;
        Attempts = 0;
        PartCompleted = false;
        AwaitingReturn = true;
        StudyConfig.Current = Current.config;
        PlayerProgress.Suspended = true;
        CheckpointSystem.Clear();
    }

    /// <summary>Pasa a la siguiente parte o termina la prueba. Devuelve false si ya no quedan partes.</summary>
    public static bool NextPart()
    {
        Step++;
        return Step < Plan.Length;
    }

    public static void NotifyAttemptStarted() => Attempts++;
    public static void NotifyAttemptEnded(bool completed) { if (completed) PartCompleted = true; }

    /// <summary>
    /// Lo llama GameOverManager al pulsar "Reintentar". Si ya se han gastado los intentos de esta parte, se vuelve al
    /// menú (siguiente paso de la prueba) en lugar de recargar el nivel. Devuelve true si se ha encargado de ello.
    /// </summary>
    public static bool OnRestartRequested()
    {
        if (!InLevel || Attempts < StudySettings.MaxAttemptsPerPart) return false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(MenuScene);
        return true;
    }

    /// <summary>De vuelta en el menú después de una parte, por lo que ya no se está jugando.</summary>
    public static void ReturnedToMenu() => AwaitingReturn = false;

    // -=+=- Al cargar un nivel -=+=-

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (!Active || s.name == MenuScene) return;
        // Al terminar el nivel, se vuelve al menú (cuestionario y siguiente parte) en lugar de ir al siguiente nivel.
        foreach (var end in Object.FindObjectsByType<LevelEndManager>()) end.SetNextScene(MenuScene);
    }
}
