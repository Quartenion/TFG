/// <summary>
/// GUION que sigue <see cref="TutorialDirector"/>: qué acciones se piden, en qué beat, hasta dónde dura…
///   • <see cref="Level1"/>: el tutorial del nivel 1 (TutorialLevel1Steps).
///   • El nivel tutorial (escena «Tutorial», ver CourseBuilder) crea el suyo al cargar la escena y lo pone en
///     <see cref="Override"/>; al salir de la escena se quita.
/// </summary>
public class TutorialScript
{
    public string scene;                       // escena en la que se usa
    public TutorialLevel1Steps.Step[] steps;   // acciones en orden de beat (con texto = frena la primera vez)
    public float endBeat;                      // hasta este beat dura el guion
    public float energyBeat = -1f;             // beat de la explicación de la energía (−1 = no hay)
    public float lockSeconds;                  // al pararse: segundos SIN poder hacer la acción (para leer el aviso)
    public bool requireFromStart = true;       // solo se da por completado si se jugó desde el principio
    public bool alwaysRun;                     // se ejecuta siempre (no depende de Ajustes → TUTORIAL)
    public bool allowInStudy;                  // también en el modo prueba
    public bool livePrompts = true;            // aviso grande arriba en los pasos sin frenada (si no, solo la nota del monitor)
    public bool eventExplanations = true;      // explicaciones por evento (primer fallo, sangre azul…) que paran el juego
    public string pendingPref;                 // PlayerPrefs que se pone a 0 al completarlo (null = ninguna)
    public System.Action<int> onExplained;     // se ha parado y explicado el paso i
    public System.Action<int> onFollowShown;   // se ha enseñado la explicación de después del paso i (Step.follow)
    public System.Action onCompleted;          // guion terminado

    /// <summary>Guion del nivel tutorial (lo pone CourseBuilder). Null = el del nivel 1.</summary>
    public static TutorialScript Override;

    public static TutorialScript Current => Override ?? Level1;

    private static TutorialScript _level1;
    public static TutorialScript Level1 => _level1 ??= new TutorialScript
    {
        scene = "Level1",
        steps = TutorialLevel1Steps.Steps,
        endBeat = TutorialLevel1Steps.EndBeat,
        // (Sin la explicación de la energía ni las de los fallos / la sangre azul: ya se explican en la escena Tutorial.)
        energyBeat = -1f,
        eventExplanations = false,
        lockSeconds = 0f,
        requireFromStart = true,
        pendingPref = "Tutorial.L1",
    };
}
