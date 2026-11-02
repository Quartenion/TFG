using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>Qué se juega en la escena del nivel tutorial.</summary>
public enum CourseMode { None, Menu, Tutorial, Practice, Study }

/// <summary>Una acción que pide un obstáculo: tecla, beat DENTRO de su sección (puede ser negativo) y lado (dash).</summary>
[Serializable]
public class CourseAction
{
    public TutorialKey key = TutorialKey.Jump;
    [Tooltip("Beat de la acción contado desde el PRINCIPIO de la sección (0 = al entrar; −1 = un beat antes).")]
    public float beat;
    [Tooltip("Solo dash: −1 = izquierda, +1 = derecha (en las secciones en espejo se invierte solo).")]
    public int side;
    [Tooltip("Explicación de la PRIMERA vez (frena el juego). Vacía = solo aviso, sin frenar.")]
    [TextArea(2, 3)] public string explainEs;
    [TextArea(2, 3)] public string explainEn;
}

/// <summary>Un tipo de obstáculo (una fase del tutorial).</summary>
[Serializable]
public class CourseObstacle
{
    public string titleEs = "", titleEn = "";
    [Tooltip("Prefab de la sección. Su ORIGEN es el CENTRO de la primera sección (14,4 m desde su principio).")]
    public GameObject prefab;
    [Tooltip("Secciones de 4 beats que ocupa (Tirolina / Paredes / Kayak: 4).")]
    public int segments = 1;
    [Tooltip("Veces que se repite en el tutorial.")]
    public int reps = 3;
    [Tooltip("Dash: las repeticiones alternan izquierda / derecha (la derecha es el prefab en espejo).")]
    public bool alternateSides;
    [Tooltip("¿Entra en el recorrido de estudio generado? (sin usar: el recorrido de estudio es fijo, ver StudySequence)")]
    public bool inStudy = true;
    [Tooltip("Tramo de suelo al terminar sus repeticiones (la tirolina, los kayaks, el dash y las paredes lo llevan siempre).")]
    public bool floorAfter;
    [Header("Kayak")]
    [Tooltip("Metros extra que flota el kayak sobre el agua en esta sección (0 = como en el prefab).")]
    public float kayakRaise = 0.5f;
    public CourseAction[] actions = new CourseAction[0];
    [Tooltip("Consejo que sale al reaparecer en el checkpoint si te has chocado con este obstáculo.")]
    [TextArea(2, 3)] public string tipEs;
    [TextArea(2, 3)] public string tipEn;

    public string Title => Loc.T(titleEs, titleEn);
    public string Tip => Loc.T(tipEs, tipEn);
}

/// <summary>
/// NIVEL TUTORIAL Y DE ESTUDIO (escena «Tutorial», 100 BPM, canción MC en bucle con la intro MC-INTRO).
///
/// El recorrido se GENERA AL JUGAR, sección a sección (1 sección = 4 beats; a 12 m/s y 100 BPM, 28,8 m), con los
/// prefabs de Assets/Prefabs/Estudio. Como el suelo del jefe, cada sección SUBE desde abajo cuando está a
/// <see cref="spawnAheadBeats"/> beats del jugador, y las que quedan atrás se borran.
///
///   • TUTORIAL (siempre al entrar en la escena): tramo inicial con los avisos del clic cada 4 beats → cada obstáculo 3 veces seguidas (los
///     largos —tirolina, paredes, kayak— 1 vez) → tramo de suelo con CHECKPOINT → siguiente obstáculo… → sala final.
///     La primera vez de cada acción frena, explica y deja <see cref="readSeconds"/> s para leer (TutorialDirector).
///   • PRÁCTICA: un solo obstáculo (menú al terminar el tutorial).
///   • ESTUDIO: un recorrido FIJO, de los obstáculos fáciles a los difíciles (<see cref="StudySequence"/>), colocado en
///     la escena (carril 1) e igual en las dos rondas y en cada reintento. Con metrónomo y con las pistas de las
///     acciones de cada sección (nota en el monitor del corazón), sin explicaciones ni frenadas.
/// Checkpoints INFINITOS: al chocar, caer o quedarse sin energía se reaparece en el último con un CONSEJO.
/// </summary>
[DefaultExecutionOrder(-50)]
public class CourseBuilder : MonoBehaviour
{
    [Header("Secciones")]
    public GameObject floorPrefab;
    [Tooltip("Sala final (Prefabs/End). Su hijo «EndPoint» es la meta. Al terminar se queda en ella y elige puerta.")]
    public GameObject endPrefab;
    [Tooltip("Altura de la sala final respecto al suelo (como en el nivel 3: un poco por debajo).")]
    public float endHeightOffset = -0.3f;
    public List<CourseObstacle> obstacles = new List<CourseObstacle>();

    [Header("Música")]
    [Tooltip("Canción del tutorial y de los recorridos (en bucle, sin intro). Vacía = la del BeatManager de la escena.")]
    public AudioClip tutorialMusic;
    [Tooltip("Música de la sala final, PORTAPAPELES-LOBBY (como la del lobby entre niveles, con los pasos al ritmo).")]
    public AudioClip endRoomMusic;
    [Tooltip("BPM de la música de la sala (los pasos van a este ritmo).")]
    public float endRoomMusicBpm = 133.333f;
    [Tooltip("Compás de la música de la sala (Papercut: 3/4) y beat del clip en el que cae el primer tiempo fuerte.")]
    public int endRoomBeatsPerBar = 3;
    public int endRoomDownbeatBeat = 2;

    [Header("Aparición (como el suelo del jefe)")]
    [Tooltip("Beats de antelación con los que aparece cada sección.")]
    public float spawnAheadBeats = 16f;
    [Tooltip("Beats que tarda en subir a su sitio.")]
    public float riseBeats = 4f;
    [Tooltip("Metros por debajo desde los que sube.")]
    public float riseDepth = 40f;
    [Tooltip("Se borran las secciones que quedan tantos beats por detrás.")]
    public float despawnBehindBeats = 12f;


    [Header("Monedas (en los tramos de suelo; solo en el recorrido de estudio)")]
    public GameObject coinPrefab;
    public bool coinsOnFloors = true;
    public float coinHeight = 1.2f;

    [Header("Recorrido de estudio")]
    public int studyObstacles = 15;
    [Tooltip("Checkpoint cada tantas secciones.")]
    public int studyCheckpointEvery = 8;

    [Header("Pruebas (solo en el editor)")]
    [Tooltip("Al darle a Play en esta escena, empieza una sesión de estudio SIMULADA (como si se hubiera aceptado el " +
             "consentimiento en el menú): tutorial → sala → cuestionarios y recorridos A/B. El código empieza por «TEST-» " +
             "y los datos NO se envían al formulario (solo al CSV local).")]
    public bool simularEstudio;
    [Tooltip("Al darle a Play en esta escena, se empieza DIRECTAMENTE en la sala final (sin hacer el tutorial), libre y con " +
             "las puertas. Con «Simular estudio» marcado, en la sala sale la pantalla de la primera parte.")]
    public bool empezarEnLaSala;

    [Header("Interruptores de mirada")]
    [Tooltip("Segundos mirando un interruptor para activarlo (tutorial y recorrido de estudio).")]
    public float gazeHoldSeconds = 0.5f;

    [Header("Sala final")]
    [Tooltip("Si el jugador cae por debajo de esta altura en la sala (moviéndose libre), vuelve a su centro.")]
    public float roomFallY = -20f;

    [Header("Tutorial")]
    [Tooltip("Segundos sin poder hacer la acción al pararse (para leer el aviso).")]
    public float readSeconds = 3f;
    [Tooltip("Por debajo de esta altura se considera que el jugador se ha caído.")]
    public float fallY = -8f;

    // ── Estado entre recargas de la escena ───────────────────────────────────
    private const string PrefDone = "Course.TutorialDone";
    public static bool TutorialDone => PlayerPrefs.GetInt(PrefDone, 0) == 1;

    /// <summary>
    /// Prueba nueva (StudySession.Begin): si el tutorial no se ha COMPLETADO nunca, se olvida lo ya explicado, así vuelve
    /// a frenar y explicar todo desde el principio (p. ej. al abandonar a medias y volver a empezar). Solo deja de
    /// frenar cuando el tutorial entero se ha terminado.
    /// </summary>
    public static void ResetTutorialRun()
    {
        if (!TutorialDone) Explained.Clear();
        s_deathsAt.Clear();
        s_shiftFinal = -1;
        CheckpointSystem.Clear();
    }

    /// <summary>Modo elegido en el menú (se conserva al reaparecer en un checkpoint).</summary>
    public static CourseMode Selected = CourseMode.None;
    public static int PracticeIndex;
    private static string _pendingTip;
    private static readonly HashSet<string> Explained = new HashSet<string>();   // acciones ya explicadas (frenada)
    private static string _lastScene;
    private static bool  s_fromRoom;     // se sale por una puerta de la sala final (la sala queda detrás del jugador)
    private static float s_roomYaw;      // ángulo (local de la sala) de la pared de esa puerta
    private static string s_doorName;    // nombre de esa puerta (para dejarla abierta en la sala de salida)
    private static bool  s_fadeIn;       // al cargar: aparecer desde negro (tras cruzar una puerta)
    private static bool  s_blinkIn;      // al cargar: abrir los ojos (parpadeo al cruzar una puerta)


    public static CourseBuilder Instance { get; private set; }
    public CourseMode Mode { get; private set; }

    /// <summary>Longitud de una sección (4 beats) en metros.</summary>
    public float SegmentLength => _segLen;

    // ── Plan del recorrido ───────────────────────────────────────────────────
    private enum Kind { Floor, Obstacle, End }
    private class Sec
    {
        public Kind kind;
        public int baseSeg, len = 1, obstacle = -1;
        public bool shift;   // va DESPUÉS de la fase de energía: se desplaza mientras dura (ver s_shift)
        public int seg => baseSeg + (shift ? s_shift : 0);
        public bool mirrored, checkpoint, end;
        public GameObject go;
        public GameObject placed;      // sección ya colocada en la escena (Tools/Curso: Colocar el tutorial)
        public CourseAction[] actions; // acciones de la sección colocada (null = las del obstáculo)
        public Vector3 target;         // dónde se queda al terminar de subir
        public double riseStart = -1;   // beat en que empezó a subir (−1 = aún no)
        public bool pendingRise, risen;
        public readonly List<int> steps = new List<int>();   // pasos del guion que apuntan a esta sección
        public readonly List<(Rigidbody rb, bool kin, RigidbodyInterpolation interp)> bodies = new List<(Rigidbody, bool, RigidbodyInterpolation)>();
        public readonly List<Renderer> hidden = new List<Renderer>();   // ocultos durante el primer fotograma
    }
    private readonly List<Sec> _plan = new List<Sec>();
    private float _segLen = 28.8f;
    private readonly List<Sec> _cps = new List<Sec>();   // tramos con checkpoint, en orden
    private int _cpReached = -1;
    private Sec _endSec;

    // ── Fase de energía (tutorial): los obstáculos no salen hasta que se ha explicado toda la barra ──
    private static int s_shift;                     // secciones que se retrasan los obstáculos
    private static int s_shiftFinal = -1;           // retraso definitivo (se conserva al reaparecer)
    private bool _energyPhase;
    private int _shiftFromSeg;                      // primera sección que se desplaza (sin contar el retraso)
    private float[] _stepBase;                      // beat de cada paso sin el retraso
    private float _endBeatBase;
    private TextMeshProUGUI _goal;                  // objetivo de la fase de energía
    private Transform _finish;
    private Transform _root;
    private TutorialScript _script;
    private readonly List<string> _stepIds = new List<string>();
    private readonly List<int> _stepSec = new List<int>();

    // ── Referencias ──────────────────────────────────────────────────────────
    private BeatManager _bm;
    private PlayerMovement _pm;
    private GameOverManager _gom;
    private StartSequenceManager _seq;
    private LevelEndManager _end;
    private bool _deathHandled, _ended, _started, _zipHint, _zipSideShown;
    private const string ZipSideId = "zip.lados";

    private static string ZipSideText() =>
        Loc.T($"Mientras vas colgado de la tirolina, puedes moverte a los lados con {Keybinds.Name(Keybinds.Bind.Left)} y " +
              $"{Keybinds.Name(Keybinds.Bind.Right)} para esquivar los obstáculos. Para soltarte, salta.",
              $"While hanging from the zipline you can move sideways with {Keybinds.Name(Keybinds.Bind.Left)} and " +
              $"{Keybinds.Name(Keybinds.Bind.Right)} to dodge. Jump to let go.");
    private HeartScale _scale;
    private bool _prevSuspended;
    private LobbyMusic _lobby;

    // ── Interfaz ─────────────────────────────────────────────────────────────
    private Transform _canvas;
    private DialogBox _dialog;
    private Image _fade;

    // =========================================================================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Selected = CourseMode.None; PracticeIndex = 0; _pendingTip = null; _lastScene = null;
        Explained.Clear();
        SceneManager.sceneLoaded -= Track;
        SceneManager.sceneLoaded += Track;
    }

    private static void Track(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) _lastScene = s.name; }

    private void Reset() => FillDefaults();

    private void Awake()
    {
        Instance = this;
#if UNITY_EDITOR
        MergeDefaults();   // (por si se han creado prefabs nuevos y aún no se ha guardado la escena)
#endif
        string scene = SceneManager.GetActiveScene().name;
        bool respawn = CheckpointSystem.PendingRespawn;
#if UNITY_EDITOR
        if (simularEstudio && !StudySession.Active) { StudySession.Begin(simulated: true); Debug.Log($"[Curso] Estudio SIMULADO: {StudySession.Participant} (no se envían datos)."); }
#endif
        // Llegando desde OTRA escena (no al reaparecer ni al recargar desde el menú del curso): se vuelve a decidir.
        if (_lastScene != scene && !respawn) { Selected = CourseMode.None; _pendingTip = null; s_fromRoom = false; s_doorName = null; }

        _bm = FindAnyObjectByType<BeatManager>();
        _pm = FindAnyObjectByType<PlayerMovement>();
        _gom = FindAnyObjectByType<GameOverManager>();
        _seq = FindAnyObjectByType<StartSequenceManager>();
        _end = FindAnyObjectByType<LevelEndManager>();

        // Canción del tutorial (PORTAPAPELES: 48 beats exactos a 100 BPM, en bucle) y SIN intro.
        if (_bm != null && _bm.CurrentSource != null)
        {
            if (tutorialMusic != null) _bm.CurrentSource.clip = tutorialMusic;
            _bm.CurrentSource.loop = true;
        }
        StartSequenceManager.ForceSkipIntro = true;

        // Cielo: la cámara del jugador viene con fondo de COLOR SÓLIDO (prefab Player); aquí se ve el skybox de la escena.
        var atmosphere = FindAnyObjectByType<DarkFantasyAtmosphere>();
        if (atmosphere != null) atmosphere.skyboxBackground = true;
        if (RenderSettings.skybox != null)
            foreach (Camera c in FindObjectsByType<Camera>())
                if (c.CompareTag("MainCamera") && c.targetTexture == null) c.clearFlags = CameraClearFlags.Skybox;
        float walk = _pm != null ? _pm.walkSpeed : 12f;
        _segLen = walk * BeatManager.BeatDurationOf(_bm) * 4f;

        Mode = DecideMode();
        StudyDoorOpen = false;
        // Los avisos se pasan con el clic izquierdo, pero ese clic es también el latido: solo vale con el juego parado
        // por el tutorial, moviéndose libre por la sala o en los resultados (no corriendo ni en el 3-2-1).
        DialogBox.ClickAllowed = () => TutorialGate.Frozen || LevelEndManager.Finished || (_pm != null && _pm.IsFreeroam);
        // En esta escena no se compran mejoras (ni en el tutorial ni en el estudio) y las que se tengan no cuentan.
        FreeroamStand.ShopsClosed = true;
        _prevSuspended = PlayerProgress.Suspended;
        PlayerProgress.Suspended = true;
        TutorialScript.Override = null;
        CheckpointSystem.EnableFreeRespawn();
        _root = new GameObject("Recorrido (generado)").transform;
        // Restos sueltos en la escena (rocas de pruebas, un tramo de suelo con sus paredes invisibles): fuera al jugar.
        // Estaban en el origen y se veían/chocaban en la sala cuando se empezaba directamente en ella.
        foreach (GameObject g in gameObject.scene.GetRootGameObjects())
            if (g.name == "Free Pack - Rocks Stylized" || g.name == "Tramo_Suelo") g.SetActive(false);

        // Fase de energía (solo el tutorial completo): al empezar de cero se vuelven a explicar la energía, el primer fallo,
        // la sangre azul y el fallo con sangre azul; hasta entonces solo sale suelo y los obstáculos se van retrasando.
        if (!respawn) s_deathsAt.Clear();   // (muertes por sección: desde cero al empezar de nuevo)
        GameOverManager.SkipAvailable = CanSkipSection;
        // Repitiendo el tutorial (o una parte) desde una puerta de la sala: la pausa permite volver a la sala.
        // (Y también en el tutorial normal si ya se completó alguna vez: así se lo puede saltar.)
        PauseMenuManager.BackToRoomAvailable = () => (Mode == CourseMode.Tutorial || Mode == CourseMode.Practice) && (s_fromRoom || TutorialDone) && !_ended;
        PauseMenuManager.BackToRoom = BackToRoomFromPause;
        GameOverManager.SkipSection = SkipToNextCheckpoint;
        if (Mode == CourseMode.Tutorial && !respawn)
        {
            s_shiftFinal = -1;
            for (int k = 0; k < EnergyTips.Length; k++) Explained.Remove("E." + k);   // (desde el principio: se vuelven a explicar)
        }
        _energyPhase = Mode == CourseMode.Tutorial && s_shiftFinal < 0;
        s_shift = Mode == CourseMode.Tutorial ? Mathf.Max(0, s_shiftFinal) : 0;


        // Lo colocado en la escena (carril 0 = tutorial, carril 1 = estudio) empieza escondido; cada modo usa el suyo.
        foreach (CourseSection cs in FindObjectsByType<CourseSection>()) cs.gameObject.SetActive(false);
        switch (Mode)
        {
            case CourseMode.Tutorial: if (!UsePlaced(0)) PlanTutorial(-1); MakeScript(false); break;
            case CourseMode.Practice: PlanTutorial(PracticeIndex); MakeScript(true); break;
            // Estudio: el recorrido FIJO (de fácil a difícil), con las pistas de las acciones y sin explicaciones.
            case CourseMode.Study:    if (!UsePlaced(StudyLane)) PlanStudy(); SetCourseOrder(); MakeScript(true, hintsOnly: true); break;
            default:                  break;   // (menú: la sala final, ver Start)
        }

        // Saliendo por una puerta de la sala final: la sala está detrás (sin el tramo de suelo de antes de empezar).
        if (s_fromRoom && Mode != CourseMode.Menu)
        {
            _plan.RemoveAll(x => x.kind == Kind.Floor && x.baseSeg == -1);
            SpawnStartRoom();
        }
        // Música de la sala final: la del lobby (suena en la sala, al moverse libre, y tras la balanza).
        _lobby = FindAnyObjectByType<LobbyMusic>();
        if (_lobby == null && endRoomMusic != null)
        {
            _lobby = gameObject.AddComponent<LobbyMusic>();
            _lobby.music = endRoomMusic;
            _lobby.bpm = endRoomMusicBpm;
            _lobby.stepsPerBeat = 1;                                  // (un paso por beat)
            _lobby.beatsPerBar = Mathf.Max(1, endRoomBeatsPerBar);    // (Papercut va en 3/4: FUERTE-débil-débil)
            _lobby.downbeatOffsetBeats = Mathf.Max(0, endRoomDownbeatBeat);
            _lobby.reverbPreset = AudioReverbPreset.Auditorium;
        }
        // (Solo en la sala final, cuando CourseBuilder lo pide: no en el momento de freeroam antes del 3-2-1.)
        if (_lobby != null) _lobby.onlyWhenForced = true;
        // En esta escena no hay monedas (ni en el tutorial ni en el estudio, ni las que haya puestas en la escena).
        foreach (Coin c in FindObjectsByType<Coin>(FindObjectsInactive.Include)) Destroy(c.gameObject);
        // Modelos del kit para la tienda, los contratos y la sala.
        CarvedShop.Kit = null;   // (sin modelos del pack medieval)
        // Metrónomo encendido en el tutorial, la práctica y el estudio (aunque no se haya comprado en la tienda).
        Metronome.SetForced(Mode == CourseMode.Tutorial || Mode == CourseMode.Practice || Mode == CourseMode.Study);


        // Lo que ya tiene que estar al empezar (desde el principio o desde el checkpoint): aparece sin subir.
        double startBeat = respawn && _bm != null ? CheckpointSystem.Active.songOffset / BeatManager.BeatDurationOf(_bm) : 0.0;
        int startSeg = Mathf.FloorToInt((float)(startBeat / 4.0));
        // Tramos de suelo de la fase de energía ya decidida (al reaparecer después de ella) y, si sigue, los que hagan falta.
        if (Mode == CourseMode.Tutorial)
            for (int k = 0; k < s_shift; k++) AddFiller(k);
        if (_energyPhase) UpdateEnergyShift(startBeat);
        foreach (Sec s in _plan)
            if (s.seg + s.len >= startSeg - 1 && s.seg * 4.0 <= startBeat + spawnAheadBeats) Spawn(s, instant: true);

        if (_end != null) _end.SetBlocked(true);   // (el final lo lanza este script)
        // (Se apunta aquí también, no solo al cargar la escena: al darle a Play en el editor, la carga de la primera
        //  escena no siempre avisa, y la primera puerta que se cruzaba «olvidaba» qué se había elegido.)
        _lastScene = scene;
    }

    private CourseMode DecideMode()
    {
        if (StudySession.Active && StudySession.InLevel) return StudySession.Current.course == CourseMode.None ? CourseMode.Study : StudySession.Current.course;
        if (Selected == CourseMode.Menu) return CourseMode.Menu;   // (de vuelta a la sala desde la pausa)
#if UNITY_EDITOR
        // (Pruebas: empezar en la sala sin hacer el tutorial.)
        if (empezarEnLaSala && Selected == CourseMode.None && !CheckpointSystem.PendingRespawn) return CourseMode.Menu;
#endif
        if (Selected != CourseMode.None) return Selected;
        return CourseMode.Tutorial;   // (al entrar en la escena, siempre desde el principio del tutorial)
    }

    private void Start()
    {
        BuildUI();
        if (s_fadeIn) { s_fadeIn = false; _fade.gameObject.SetActive(true); _fade.color = Color.black; StartCoroutine(Fade(1f, 0f, 0.35f)); }
        if (s_blinkIn)
        {
            // Se abren los ojos al otro lado de la puerta (la escena empieza con ellos cerrados).
            s_blinkIn = false;
            _lidTop.gameObject.SetActive(true); _lidBottom.gameObject.SetActive(true);
            SetLids(1f);
            StartCoroutine(OpenEyesSoon());
        }
        if (Mode == CourseMode.Menu)
        {
            // Tutorial ya hecho: se empieza DENTRO de la sala final, libre, y se elige puerta.
            StartCoroutine(StartInRoom());
            return;
        }

        if (CheckpointSystem.PendingRespawn)
        {
            // Reaparición cerca del principio (p. ej. al fallar el primer obstáculo): la sala de salida vuelve a estar
            // ahí con su puerta, que ya estaba abierta. Si no se abre, el jugador se queda encerrado en la sala.
            if (_startDoor != null && !s_blinkIn) StartCoroutine(_startDoor.Open(0f, 90f));
            if (!string.IsNullOrEmpty(_pendingTip)) ShowInfo(Loc.T("Consejo", "Tip"), _pendingTip);
            _pendingTip = null;
            return;   // (StartSequenceManager hace el 3-2-1 desde el checkpoint)
        }
        StartCoroutine(Launch());
    }

    private void OnDestroy()
    {
        if (TutorialScript.Override == _script) TutorialScript.Override = null;
        if (Instance == this)
        {
            Instance = null; Metronome.SetForced(false); StartSequenceManager.ForceSkipIntro = false;
            GameOverManager.SkipAvailable = null; GameOverManager.SkipSection = null;
            PauseMenuManager.BackToRoomAvailable = null; PauseMenuManager.BackToRoom = null;
            FreeroamStand.ShopsClosed = false;
            DialogBox.ClickAllowed = null;
            if (!StudySession.Active) PlayerProgress.Suspended = _prevSuspended;
            StudyTelemetry.CurrentObstacle = StudyTelemetry.CourseOrder = "";
        }
    }

    private bool Valid(int i) => i >= 0 && i < obstacles.Count && obstacles[i] != null && obstacles[i].prefab != null;

    /// <summary>Menú: la sala final alrededor del jugador, con sus puertas, y movimiento libre.</summary>
    private IEnumerator StartInRoom()
    {
        if (endPrefab == null) yield break;
        GameObject room = Instantiate(endPrefab, new Vector3(0f, endHeightOffset, 0f), Quaternion.identity, _root);
        room.name = "Sala final";
        _endRoom = room.transform;
        AddBigDoorSign(_endRoom);
        Transform ep = room.transform.Find("EndPoint");
        _finish = ep != null ? ep : room.transform;
        CloseEntrance(_endRoom);
        if (room.GetComponentInChildren<CourseDoor>(true) == null) BuildDoors(room.transform);
        yield return null;   // (después del Start de StartSequenceManager)
        Transform p = room.transform.Find("EndPoint");
        Vector3 at = p != null ? p.position : room.transform.position + Vector3.up;
        if (_pm != null)
        {
            _pm.TeleportToStartPoint(at, Quaternion.identity);
            var cc = _pm.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            _pm.EnableFreeroam();
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (_lobby != null) _lobby.forcePlay = true;
        SetHandLowered(1f);   // (al entrar en la sala desde el menú, la mano ya está abajo)
        HideHudForRoom();
        if (StudySession.Active && !StudySession.InFreePlay)
        {
            // Modo prueba (de vuelta de repetir una parte): la puerta grande lista; si aún no se vio la pantalla, se enseña.
            if (StudySession.RoomRunsPending()) { StudyDoorOpen = true; HighlightBigDoor(true); }
            else OpenStudyHub();
            yield break;
        }
        HighlightBigDoor(true);
        ShowInfo(Loc.T("Sala del tutorial", "Tutorial room"), DoorsHelp());
    }

    /// <summary>
    /// En la sala final (moviéndose libre): se esconden la mano con el corazón, la mirilla, las jeringuillas, la barra de
    /// energía y el monitor del corazón. (Al cruzar una puerta la escena se recarga y vuelve todo.)
    /// </summary>
    private void HideHudForRoom()
    {
        RoomCrosshair(true);                                         // (de la mirilla, solo el corazón del centro)
        if (_handLowered < 0.99f) StartCoroutine(MoveHand(_handLowered, 1f, 0.7f));   // (la mano baja suave)
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            if (t.GetComponentInParent<Canvas>() != null &&
                (t.name == "EnergyBar" || t.name == "Jeringuillas" || t.name == "HeartMonitorHUD" || t.name == "Timing Feedback"))
                t.gameObject.SetActive(false);
    }

    // ── Mano (brazo con el corazón): sube y baja moviendo la cámara del brazo ──
    private float _handLowered;
    private const float HandDrop = 2.2f;   // metros que sube la cámara del brazo (= baja la mano fuera de la vista)

    private void SetHandLowered(float k)
    {
        _handLowered = k;
        Transform vm = _pm != null ? FindDeep(_pm.transform, "ViewmodelCamera") : null;
        if (vm != null) vm.localPosition = Vector3.up * (HandDrop * Mathf.SmoothStep(0f, 1f, k));
    }

    private IEnumerator MoveHand(float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            SetHandLowered(Mathf.Lerp(from, to, t / seconds));
            yield return null;
        }
        SetHandLowered(to);
    }

    // ── Mirilla de la sala: solo el corazón, que crece al mirar algo con lo que se puede interactuar ──
    private bool _roomCrosshair;
    private Transform _xHeart;
    private Vector3 _xHeartScale = Vector3.one;
    private float _xGrow;

    private void RoomCrosshair(bool on)
    {
        _roomCrosshair = on;
        foreach (CrosshairController c in FindObjectsByType<CrosshairController>(FindObjectsInactive.Include))
        {
            c.gameObject.SetActive(true);
            c.enabled = !on;   // (su animación no toca el corazón mientras tanto)
            foreach (Transform child in c.transform)
            {
                bool heart = child.name == "Heart";
                child.gameObject.SetActive(!on || heart);
                if (heart && on) { _xHeart = child; _xHeartScale = child.localScale; }
            }
        }
    }

    // (En LateUpdate: el corazón de la mirilla también lo escala HeartBeat en su Update. Según en qué orden corrieran
    //  los dos —cambia al recargar la escena, p. ej. al volver a la sala desde la pausa— HeartBeat pisaba el tamaño y
    //  el corazón no crecía al mirar una puerta.)
    private void LateUpdate() => UpdateRoomCrosshair();

    private void UpdateRoomCrosshair()
    {
        if (!_roomCrosshair || _xHeart == null) return;
        // (Lo decide cada puesto/puerta con su propia mirada: así vale también la puerta grande, cuyas hojas no cuelgan de
        //  ella, y solo crece si de verdad se puede usar ahora.)
        bool over = _pm != null && _pm.IsFreeroam && FreeroamStand.AnyTargeted;
        _xGrow = Mathf.MoveTowards(_xGrow, over ? 1f : 0f, Time.deltaTime * 6f);
        _xHeart.localScale = _xHeartScale * (1f + 0.7f * Mathf.SmoothStep(0f, 1f, _xGrow));
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    private static string DoorsHelp() => Loc.T(
        "Mira una puerta y pulsa E para cruzarla. La puerta grande empieza el recorrido de estudio, mientras que las de las " +
        "paredes repiten el tutorial completo o una de sus partes.",
        "Look at a door and press E to go through it. The big door starts the study run, while the doors on the walls " +
        "replay the whole tutorial or one of its parts.");

    /// <summary>
    /// Sala de salida (al elegir una puerta): la sala girada para que esa pared quede mirando a +Z justo en el inicio
    /// del recorrido (z = 0), y sin esa pared, para que el recorrido salga por ella.
    /// </summary>
    private void SpawnStartRoom()
    {
        if (endPrefab == null) return;
        GameObject room = Instantiate(endPrefab, Vector3.zero, Quaternion.Euler(0f, -s_roomYaw, 0f), _root);
        room.name = "Sala de salida";
        _startRoom = room;
        // La puerta por la que se ha salido queda justo en el inicio del recorrido (0, 0, 0), mirando a +Z, y ABIERTA:
        // es la misma vista que tenía el jugador al cruzarla.
        CourseDoor door = null;
        foreach (CourseDoor d in room.GetComponentsInChildren<CourseDoor>(true))
            if (d.name == s_doorName) { door = d; break; }
        if (door != null)
        {
            // La puerta queda un poco por DELANTE del jugador (que empieza dentro de la sala, mirándola) y CERRADA:
            // se abre al abrir los ojos (OpenEyesSoon).
            room.transform.position = -Flat(door.transform.position) + Vector3.forward * StartDoorAhead + Vector3.up * endHeightOffset;
            _startDoor = door;
            return;
        }
        // (Sala sin puertas guardadas: se abre la pared que queda delante, como antes.)
        Transform anchor = null; float best = float.MaxValue;
        Transform walls = room.transform.Find("Walls");
        if (walls != null)
            foreach (Transform w in walls)
            {
                float a = Vector3.Angle(Flat(w.position - room.transform.position), Vector3.forward);
                if (a < best) { best = a; anchor = w; }
            }
        Vector3 shift = anchor != null ? -Flat(anchor.position) : Vector3.zero;
        room.transform.position = shift + Vector3.up * endHeightOffset;
        var opening = new List<Transform>(TutorialWalls(room.transform));
        foreach (Transform t in room.transform) if (t.name.StartsWith("LevelDoor")) opening.Add(t);
        foreach (Transform t in opening)
        {
            if (Vector3.Angle(Flat(t.position - room.transform.position), Vector3.forward) > 12f) continue;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (Collider c in t.GetComponentsInChildren<Collider>()) c.enabled = false;
        }
    }

    // =========================================================================
    // CRUZAR UNA PUERTA DE LA SALA: se abre, el jugador camina hasta ella y el recorrido sale por detrás
    // =========================================================================

    private bool _doorBusy;

    /// <summary>La llama CourseDoor al pulsar E.</summary>
    public void DoorChosen(CourseDoor door)
    {
        if (_doorBusy || door == null) return;
        StartCoroutine(DoorTransition(door));
    }

    private IEnumerator DoorTransition(CourseDoor door)
    {
        _doorBusy = true;
        HideInfo();
        if (_lobby != null) _lobby.forcePlay = false;
        Vector3 outward = door.Outward;

        // Lo que se va a jugar, y la escena se empieza a cargar YA, en segundo plano, mientras se abre la puerta.
        CourseMode mode = door.Mode;
        int index = ResolveObstacle(door);
        // (Modo prueba: solo la PUERTA GRANDE empieza la parte que toca; las pequeñas son práctica, sin registrar datos.)
        if (StudySession.Active && !StudySession.InFreePlay && mode == CourseMode.Study) { StudySession.PreparePart(); StudyDoorOpen = false; }
        if (mode == CourseMode.Practice && !Valid(index)) { Debug.LogWarning($"[Curso] La puerta «{door.name}» apunta a un obstáculo que no existe: tutorial completo."); mode = CourseMode.Tutorial; }
        PrepareChoice(mode, index, door.ExitThroughWall ? door.WallYaw : float.NaN);
        s_doorName = door.name;
        AsyncOperation load = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().name);
        if (load != null) load.allowSceneActivation = false;

        // El jugador CIERRA LOS OJOS nada más elegir la puerta; al abrirlos (en la escena ya cargada) está delante de
        // ella, dentro de la sala, y entonces la puerta se abre (ver SpawnStartRoom / OpenEyesSoon).
        if (_pm != null)
        {
            _pm.DisableFreeroam();
            var cc = _pm.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
        }
        yield return Blink(true, 0.25f);
        while (load != null && load.progress < 0.9f) yield return null;
        s_blinkIn = true;
        if (load != null) load.allowSceneActivation = true;
        else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Índice del obstáculo de la puerta: por el nombre de su prefab (si lo tiene) o por el índice guardado.</summary>
    private int ResolveObstacle(CourseDoor door)
    {
        if (!string.IsNullOrEmpty(door.ObstacleKey))
            for (int i = 0; i < obstacles.Count; i++)
                if (obstacles[i] != null && obstacles[i].prefab != null && obstacles[i].prefab.name == door.ObstacleKey) return i;
        return door.ObstacleIndex;
    }

    // ── Parpadeo: dos párpados negros que se cierran desde arriba y abajo ──
    private RectTransform _lidTop, _lidBottom;

    private IEnumerator Blink(bool close, float seconds)
    {
        if (_lidTop == null) yield break;
        _lidTop.gameObject.SetActive(true); _lidBottom.gameObject.SetActive(true);
        for (float t = 0f; t <= seconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
            SetLids(close ? k : 1f - k);
            yield return null;
        }
        SetLids(close ? 1f : 0f);
        if (!close) { _lidTop.gameObject.SetActive(false); _lidBottom.gameObject.SetActive(false); }
    }

    private CourseDoor _startDoor;
    private GameObject _startRoom;   // sala de la que se ha salido (se borra al alejarse: no se ve desde la sala final)
    private const float StartDoorAhead = 1.5f;   // metros entre el jugador y la puerta de salida al empezar

    private IEnumerator OpenEyesSoon()
    {
        yield return null; yield return null;   // (que el jugador ya esté colocado delante de la puerta)
        yield return Blink(false, 0.25f);
        // (La puerta se abre del todo al abrir los ojos: 90°, perpendicular a la pared. Más de 90° la hoja se vuelve hacia
        //  la pared y se mete en ella; menos, se queda a medio abrir.)
        if (_startDoor != null) StartCoroutine(_startDoor.Open(0.9f, 90f));
    }

    /// <summary>0 = ojos abiertos, 1 = cerrados (cada párpado cubre media pantalla, con el borde un poco curvado).</summary>
    private void SetLids(float closed)
    {
        float h = 0.5f * closed + 0.002f;
        _lidTop.anchorMin = new Vector2(-0.05f, 1f - h); _lidTop.anchorMax = new Vector2(1.05f, 1.02f);
        _lidBottom.anchorMin = new Vector2(-0.05f, -0.02f); _lidBottom.anchorMax = new Vector2(1.05f, h);
        _lidTop.offsetMin = _lidTop.offsetMax = _lidBottom.offsetMin = _lidBottom.offsetMax = Vector2.zero;
    }

    private IEnumerator Fade(float from, float to, float seconds)
    {
        if (_fade == null) yield break;
        _fade.gameObject.SetActive(true);
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / seconds));
            yield return null;
        }
        _fade.color = new Color(0f, 0f, 0f, to);
        if (to <= 0f) _fade.gameObject.SetActive(false);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>Paredes de la sala que llegan al suelo (las de las puertas del tutorial): ni la entrada, ni la de la
    /// puerta de los niveles, ni las de la tienda y los contratos (esas empiezan en alto).</summary>
    private static List<Transform> TutorialWalls(Transform room)
    {
        var list = new List<Transform>();
        Transform walls = room.Find("Walls");
        if (walls == null) return list;
        foreach (Transform w in walls)
        {
            Renderer r = w.GetComponent<Renderer>();
            if (r == null) continue;
            if (r.bounds.min.y - room.position.y < 1f) list.Add(w);
        }
        return list;
    }

    private IEnumerator PlaceAtStart()
    {
        yield return null;   // (después del Start de StartSequenceManager, que activa el freeroam)
        if (_pm != null) _pm.TeleportToStartPoint(new Vector3(0f, _pm.transform.position.y, 0f), Quaternion.identity);
    }

    /// <summary>Coloca al jugador al principio y lanza la intro + 3-2-1 (no hay pared de inicio en esta escena).</summary>
    private IEnumerator Launch()
    {
        SetHandLowered(1f);                       // (la mano empieza abajo…)
        yield return PlaceAtStart();
        StartCoroutine(MoveHand(1f, 0f, 0.8f));   // (…y sube suave, con el corazón, al empezar el nivel)
        // (Tutorial: en la intro no sale nada; la energía se explica al empezar a correr, como en el nivel 1.)
        if (Mode == CourseMode.Practice && Valid(PracticeIndex))
            ShowInfo(Loc.T("Práctica", "Practice"), obstacles[PracticeIndex].Title);
        if (_seq != null) _seq.TriggerSequence();
    }

    // =========================================================================
    // PLAN DEL RECORRIDO
    // =========================================================================

    private Sec AddFloor(int seg, bool checkpoint = false, bool end = false)
    {
        var s = new Sec { kind = Kind.Floor, baseSeg = seg, checkpoint = checkpoint, end = end, shift = _shifting };
        _plan.Add(s);
        if (checkpoint) _cps.Add(s);
        if (end) _endSec = s;
        return s;
    }

    private Sec AddObstacle(int seg, int index, bool mirrored, bool checkpoint = false)
    {
        var s = new Sec { kind = Kind.Obstacle, baseSeg = seg, obstacle = index, mirrored = mirrored, len = Mathf.Max(1, obstacles[index].segments),
                          shift = _shifting, checkpoint = checkpoint };
        _plan.Add(s);
        if (checkpoint) _cps.Add(s);   // (checkpoint al principio de la sección del obstáculo)
        return s;
    }

    /// <summary>¿Lleva un tramo de suelo detrás? La tirolina, los kayaks, el dash y las paredes (walljump).</summary>
    private bool NeedsFloorAfter(int index)
    {
        CourseObstacle ob = obstacles[index];
        if (ob.floorAfter) return true;
        string n = ob.prefab != null ? ob.prefab.name : "";
        return n.Contains("Tirolina") || n.Contains("Kayak") || n.Contains("Dash") || n.Contains("Paredes");
    }

    private void AddEnd(int seg)
    {
        if (endPrefab == null) { AddFloor(seg); AddFloor(seg + 1); return; }
        _plan.Add(new Sec { kind = Kind.End, baseSeg = seg, len = 3, shift = _shifting });
    }

    /// <summary>Carril del recorrido de estudio colocado en la escena, y a cuántos metros (X) está del tutorial.</summary>
    public const int StudyLane = 1;
    public const float LaneSpacing = 250f;

    /// <summary>
    /// Recorrido COLOCADO EN LA ESCENA (Tools/Curso: Colocar el tutorial / el recorrido de estudio): el plan sale de sus
    /// secciones (CourseSection del carril 'lane'), en el orden de su posición Z. Se esconden y van subiendo como las
    /// generadas; las del carril del estudio se juegan en X = 0. Devuelve false si no hay ninguna.
    /// </summary>
    private bool UsePlaced(int lane)
    {
        var list = new List<CourseSection>();
        foreach (CourseSection cs in FindObjectsByType<CourseSection>(FindObjectsInactive.Include))
            if (cs.lane == lane) list.Add(cs);
        if (list.Count == 0) return false;
        list.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));
        _shiftFromSeg = 1;
        foreach (CourseSection cs in list)
        {
            int seg = cs.kind == CourseSection.Kind.End ? cs.seg
                    : Mathf.RoundToInt((cs.transform.position.z - _segLen * 0.5f) / _segLen);
            var kind = (Kind)(int)cs.kind;
            int len = kind == Kind.Obstacle && Valid(cs.obstacle) ? Mathf.Max(1, obstacles[cs.obstacle].segments) : kind == Kind.End ? 3 : 1;
            var s = new Sec
            {
                kind = kind, baseSeg = seg, len = len, obstacle = kind == Kind.Obstacle ? cs.obstacle : -1,
                mirrored = cs.mirrored, checkpoint = cs.checkpoint, end = cs.end, placed = cs.gameObject,
                actions = cs.actions != null && cs.actions.Length > 0 ? cs.actions : null,
                shift = Mode == CourseMode.Tutorial && seg >= _shiftFromSeg,
            };
            if (kind == Kind.Obstacle && !Valid(cs.obstacle)) s.kind = Kind.Floor;   // (obstáculo sin datos: decorado)
            _plan.Add(s);
            if (s.checkpoint) _cps.Add(s);
            if (s.end) _endSec = s;
            cs.gameObject.SetActive(false);   // (aparece al acercarse)
        }
        return true;
    }

    /// <summary>Elemento del plan del tutorial (para la herramienta que lo coloca en la escena).</summary>
    public struct PlanItem { public int kind, seg, obstacle; public bool mirrored, checkpoint, end; }

    /// <summary>(Editor) Plan del tutorial completo, sin el retraso de la fase de energía.</summary>
    public List<PlanItem> EditorTutorialPlan(float segLen)
    {
        _plan.Clear(); _cps.Clear(); _endSec = null;
        _segLen = segLen; s_shift = 0;
        PlanTutorial(-1);
        var items = new List<PlanItem>();
        foreach (Sec x in _plan)
            items.Add(new PlanItem { kind = (int)x.kind, seg = x.baseSeg, obstacle = x.obstacle, mirrored = x.mirrored, checkpoint = x.checkpoint, end = x.end });
        _plan.Clear(); _cps.Clear(); _endSec = null;
        return items;
    }

    /// <summary>Tutorial completo (only = −1) o práctica de un solo obstáculo.</summary>
    private bool _shifting;

    private void PlanTutorial(int only)
    {
        AddFloor(-1);
        AddFloor(0, checkpoint: true);   // tramo inicial (intro MC-INTRO) y fase de energía
        // Todo lo que viene después se retrasa mientras dura la fase de energía (solo en el tutorial completo).
        _shifting = true;
        _shiftFromSeg = 1;
        AddFloor(1, checkpoint: true);   // checkpoint justo antes de los obstáculos
        int seg = 2;
        for (int i = 0; i < obstacles.Count; i++)
        {
            if (!Valid(i) || (only >= 0 && i != only)) continue;
            CourseObstacle ob = obstacles[i];
            // Los obstáculos van seguidos, sin tramo de suelo entre medias; el checkpoint de cada fase está al principio
            // de su primera sección. Solo después de la tirolina, los kayaks, el dash y las paredes hay un tramo de suelo.
            for (int r = 0; r < Mathf.Max(1, ob.reps); r++)
            {
                AddObstacle(seg, i, ob.alternateSides && r % 2 == 1, checkpoint: r == 0 && seg > 2);
                seg += Mathf.Max(1, ob.segments);
            }
            if (NeedsFloorAfter(i)) { AddFloor(seg); seg++; }   // (al acabar TODAS las repeticiones, p. ej. los 3 dashes)
        }
        AddFloor(seg, checkpoint: true, end: true);
        AddEnd(seg + 1);
        _shifting = false;
    }

    /// <summary>
    /// RECORRIDO DE ESTUDIO, siempre EL MISMO (en las dos rondas y en cada reintento): de los obstáculos fáciles a los
    /// difíciles. Cada elemento es el nombre del prefab sin «Obs_» («DashR» = dash a la derecha), «-» = tramo de suelo
    /// y «C» = tramo de suelo con checkpoint. Después del dash, la tirolina y las paredes se pone suelo solo.
    /// </summary>
    public static readonly string[] StudySequence =
    {
        // Fácil: una acción cada vez, con aire entre medias.
        "Salto", "-", "Deslizar", "-", "Salto", "Puerta", "-", "Deslizar", "Salto", "Puerta", "C",
        // Medio: dos acciones seguidas (doble salto, slam) y la mirada.
        "DobleSalto", "-", "Slam", "Salto", "Deslizar", "Mirada", "-", "DobleSalto", "Puerta", "Slam", "Mirada", "C",
        // Difícil: dash a los dos lados, combinaciones, tirolina y paredes.
        "Dash", "Slam", "DashR", "DobleSalto", "Deslizar", "Puerta", "C",
        "Tirolina", "Mirada", "DobleSalto", "Dash", "Salto", "Slam", "DashR", "C",
        "Paredes", "Deslizar", "DobleSalto", "Slam", "Puerta",
    };

    private int ObstacleByName(string key)
    {
        for (int i = 0; i < obstacles.Count; i++)
            if (Valid(i) && obstacles[i].prefab.name == "Obs_" + key) return i;
        return -1;
    }

    private void PlanStudy()
    {
        AddFloor(-1); AddFloor(0, checkpoint: true); AddFloor(1);
        int seg = 2;
        foreach (string item in StudySequence)
        {
            if (item == "-" || item == "C") { AddFloor(seg, checkpoint: item == "C"); seg++; continue; }
            bool right = item == "DashR";
            int ob = ObstacleByName(right ? "Dash" : item);
            if (ob < 0) { Debug.LogWarning($"[Curso] Recorrido de estudio: no hay obstáculo «Obs_{item}» en la lista."); continue; }
            AddObstacle(seg, ob, right);
            seg += Mathf.Max(1, obstacles[ob].segments);
            if (NeedsFloorAfter(ob)) { AddFloor(seg); seg++; }
        }
        AddFloor(seg, checkpoint: true, end: true);
        AddEnd(seg + 1);
    }

    /// <summary>(Editor) Plan del recorrido de estudio (para colocarlo en la escena).</summary>
    public List<PlanItem> EditorStudyPlan(float segLen)
    {
        _plan.Clear(); _cps.Clear(); _endSec = null;
        _segLen = segLen; s_shift = 0;
        PlanStudy();
        var items = new List<PlanItem>();
        foreach (Sec x in _plan)
            items.Add(new PlanItem { kind = (int)x.kind, seg = x.baseSeg, obstacle = x.obstacle, mirrored = x.mirrored, checkpoint = x.checkpoint, end = x.end });
        _plan.Clear(); _cps.Clear(); _endSec = null;
        return items;
    }

    /// <summary>Orden de los obstáculos del recorrido (columna «orden_obstaculos» de los datos).</summary>
    private void SetCourseOrder()
    {
        var names = new List<string>();
        foreach (Sec x in _plan)
            if (x.kind == Kind.Obstacle) names.Add(obstacles[x.obstacle].titleEs + (x.mirrored ? " (der.)" : ""));
        StudyTelemetry.CourseOrder = string.Join("|", names);
        Debug.Log($"[Curso] Recorrido de estudio: {names.Count} obstáculos.");
    }

    /// <summary>
    /// Guion para TutorialDirector: las acciones de cada obstáculo en su beat (las de la sección colocada, si las tiene).
    /// practice = la primera vez de cada obstáculo siempre guiada; hintsOnly = solo las pistas (nota en el monitor del
    /// corazón y aviso), sin frenar ni explicar (recorrido de estudio).
    /// </summary>
    /// <summary>
    /// Lado de una acción de una sección COLOCADA (recorrido de estudio): sus acciones se copiaron del obstáculo cuando aún
    /// no tenían lado (solo «ESPACIO», sin «+ A»). Se toma de la acción equivalente de la lista (misma tecla y beat; si no,
    /// la del mismo índice con la misma tecla).
    /// </summary>
    private static int SideFromList(CourseObstacle ob, CourseAction act, int index)
    {
        if (ob == null || ob.actions == null || act.key == TutorialKey.None) return 0;
        foreach (CourseAction o in ob.actions)
            if (o != null && o != act && o.key == act.key && Mathf.Abs(o.beat - act.beat) < 0.01f && o.side != 0) return o.side;
        if (index < ob.actions.Length && ob.actions[index] != null && ob.actions[index] != act && ob.actions[index].key == act.key)
            return ob.actions[index].side;
        return 0;
    }

    private void MakeScript(bool practice, bool hintsOnly = false)
    {
        var steps = new List<TutorialLevel1Steps.Step>();
        var ids = new List<string>();
        var secOf = new List<int>();
        var seen = new HashSet<int>();
        for (int si = 0; si < _plan.Count; si++)
        {
            Sec sec = _plan[si];
            if (sec.kind != Kind.Obstacle) continue;
            CourseObstacle ob = obstacles[sec.obstacle];
            // (Estudio: las acciones de la sección colocada; tutorial y práctica: las de la lista, siempre al día.)
            CourseAction[] acts = (hintsOnly ? sec.actions : null) ?? ob.actions;
            bool first = seen.Add(sec.obstacle) && !hintsOnly;
            for (int a = 0; a < acts.Length; a++)
            {
                CourseAction act = acts[a];
                string id = $"{sec.obstacle}.{a}";
                // (Práctica: la primera vez SIEMPRE guiada, con la frenada; las otras dos, normales.)
                bool explain = first && (practice || !Explained.Contains(id)) && !string.IsNullOrEmpty(act.explainEs);
                // Lado (dash y saltos hacia un lado): −1 izquierda, +1 derecha; en las secciones en espejo, al revés.
                int side = act.side != 0 ? act.side : SideFromList(ob, act, a);
                if (side == 0 && act.key == TutorialKey.Dash) side = -1;
                if (sec.mirrored) side = -side;
                steps.Add(new TutorialLevel1Steps.Step
                {
                    beat = sec.seg * 4f + act.beat,
                    key = act.key,
                    side = act.key == TutorialKey.Dash || act.key == TutorialKey.Jump ? side : 0,
                    text = explain ? act.explainEs : "",
                    en = explain ? (string.IsNullOrEmpty(act.explainEn) ? act.explainEs : act.explainEn) : "",
                    highlight = "",
                    // (Tirolina: al engancharse, sin reanudar, cómo moverse a los lados.)
                    follow = explain && act.key == TutorialKey.Zipline ? ZipSideText() : null,
                });
                ids.Add(id);
                secOf.Add(si);
            }
        }
        // Tutorial completo: los avisos del CLIC al principio, cada uno con su frenada (se para, se explica y sigue),
        // dejando 4 beats para probarlo antes del siguiente. No se desplazan con la fase de energía (secOf = −1).
        if (!practice && !hintsOnly)
            for (int k = 0; k < EnergyTips.Length; k++)
            {
                string id = "E." + k;
                if (Explained.Contains(id)) continue;   // (ya explicado: al reaparecer no se repite)
                steps.Add(new TutorialLevel1Steps.Step
                {
                    beat = EnergyBeatOf(k), key = TutorialKey.None,
                    text = EnergyTips[k].es, en = EnergyTips[k].en, highlight = "",
                    hud = EnergyHud(k),
                });
                ids.Add(id);
                secOf.Add(-1);
            }
        var order = new List<int>();
        for (int i = 0; i < steps.Count; i++) order.Add(i);
        // (Los avisos del clic, siempre primero: los obstáculos se van retrasando y no deben colarse entre ellos.)
        order.Sort((x, y) =>
        {
            int fx = secOf[x] < 0 ? 0 : 1, fy = secOf[y] < 0 ? 0 : 1;
            return fx != fy ? fx.CompareTo(fy) : steps[x].beat.CompareTo(steps[y].beat);
        });
        var sorted = new TutorialLevel1Steps.Step[steps.Count];
        _stepIds.Clear(); _stepSec.Clear();
        _stepFixed = new bool[steps.Count];
        for (int i = 0; i < order.Count; i++)
        {
            sorted[i] = steps[order[i]];
            _stepIds.Add(ids[order[i]]);
            _stepFixed[i] = secOf[order[i]] < 0;
            if (!_stepFixed[i]) _plan[secOf[order[i]]].steps.Add(i);
        }

        _stepBase = new float[sorted.Length];
        for (int i = 0; i < sorted.Length; i++) _stepBase[i] = sorted[i].beat - (_stepFixed[i] ? 0f : s_shift * 4f);
        _endBeatBase = _endSec != null ? _endSec.baseSeg * 4f : 0f;
        _script = new TutorialScript
        {
            scene = SceneManager.GetActiveScene().name,
            steps = sorted,
            endBeat = _endBeatBase + s_shift * 4f,
            energyBeat = -1f,                 // (la energía se explica con los avisos del clic, pasos «E.k» del guion)
            eventExplanations = false,        // (sin paradas por fallar o por la sangre azul)
            livePrompts = !hintsOnly,         // (estudio: solo las notas del monitor, sin el aviso grande de arriba)
            lockSeconds = readSeconds,
            requireFromStart = false,
            alwaysRun = true,
            allowInStudy = true,
            onExplained = i => { if (i >= 0 && i < _stepIds.Count) Explained.Add(_stepIds[i]); },
            onFollowShown = _ => { Explained.Add(ZipSideId); _zipSideShown = true; },
        };
        TutorialScript.Override = _script;
    }

    // =========================================================================
    // APARICIÓN DE LAS SECCIONES
    // =========================================================================

    private Vector3 FinalPos(Sec s) => new Vector3(0f, 0f, s.seg * _segLen + _segLen * 0.5f);

    private void Spawn(Sec s, bool instant)
    {
        if (s.go != null) return;
        if (s.placed != null)
        {
            // Sección colocada en la escena: se activa en su sitio (con el retraso de la fase de energía, si lo hay).
            s.go = s.placed;
            s.placed = null;
            Vector3 p = s.go.transform.position;
            if (s.kind != Kind.End) p.z = FinalPos(s).z;
            p.x = 0f;   // (el carril del estudio está a un lado en la escena: se juega en el centro)
            s.go.transform.position = p;
            s.go.SetActive(true);
        }
        else
        {
            GameObject prefab = s.kind == Kind.Floor ? floorPrefab : s.kind == Kind.End ? endPrefab : obstacles[s.obstacle].prefab;
            if (prefab == null) return;
            s.go = Instantiate(prefab, FinalPos(s), Quaternion.identity, _root);
            s.go.name = $"{s.seg:000} · {prefab.name}{(s.mirrored ? " (derecha)" : "")}";
            if (s.mirrored) { Vector3 sc = s.go.transform.localScale; sc.x = -sc.x; s.go.transform.localScale = sc; }
        }
        if (s.kind == Kind.End)
        {
            // La sala se adelanta medio largo (su suelo «End» mide unos ±30,9 m) para que la entrada coincida.
            float half = 30.9f;
            Renderer floor = s.go.GetComponent<Renderer>();   // (la raíz «End» es el suelo de la sala)
            if (floor != null && floor.bounds.extents.z > 1f) half = floor.bounds.extents.z;
            s.go.transform.position = new Vector3(0f, endHeightOffset, s.seg * _segLen + half);
            Transform p = s.go.transform.Find("EndPoint");
            _finish = p != null ? p : s.go.transform;
            // Sala ya montada (prefab End_Tutorial): puertas, tienda, contratos, balanza y decorado vienen puestos.
            bool prebuilt = s.go.GetComponentInChildren<CourseDoor>(true) != null;
            if (!prebuilt && (!StudySession.Active || StudySession.InFreePlay)) BuildDoors(s.go.transform);
            _endRoom = s.go.transform;
            AddBigDoorSign(_endRoom);
            _scale = s.go.GetComponentInChildren<HeartScale>(true);
            if (_scale == null) _scale = HeartScale.Build(s.go.transform);   // (la balanza del corazón, en el pedestal)
        }
        // (Sin monedas en esta escena: fuera también las que traiga el prefab de la sección.)
        foreach (Coin c in s.go.GetComponentsInChildren<Coin>(true)) Destroy(c.gameObject);

        // Paredes para correr (capa «Jumpable»): la cara que mira al jugador es LETAL (chocar de frente mata).
        if (s.kind == Kind.Obstacle) AddFrontKillFaces(s.go.transform);

        // Interruptores de mirada: se activan antes en esta escena.
        foreach (GazeSwitch g in s.go.GetComponentsInChildren<GazeSwitch>(true)) g.Configure(gazeHoldSeconds, 0f);

        // Kayak: un poco más alto sobre el agua.
        if (s.kind == Kind.Obstacle)
        {
            CourseObstacle ob = obstacles[s.obstacle];
            if (ob.kayakRaise != 0f)
                foreach (KayakController k in s.go.GetComponentsInChildren<KayakController>()) k.floatHeight += ob.kayakRaise;
        }

        // Objetivo de las explicaciones (el obstáculo, no el suelo).
        // (Interruptor de mirada y tirolina: al pararse, la vista gira sola hacia ellos; la cuerda se ilumina.)
        if (_script != null)
            foreach (int i in s.steps)
            {
                Transform t = HighlightOf(s.go.transform, _script.steps[i].key);
                _script.steps[i].target = t;
                _script.steps[i].look = _script.steps[i].key == TutorialKey.Zipline || (t != null && t.GetComponentInParent<GazeSwitch>() != null);
            }

        s.target = s.go.transform.position;
        if (instant) { s.risen = true; return; }
        // Se deja un fotograma en su sitio (para que los Start guarden su posición de diseño: kayak, monedas…) y
        // después baja, invisible, y sube a su sitio.
        // (Solo los que estaban ENCENDIDOS en el prefab: las paredes invisibles tienen el MeshRenderer apagado y así se quedan.)
        foreach (Renderer r in s.go.GetComponentsInChildren<Renderer>())
            if (r.enabled) { r.enabled = false; s.hidden.Add(r); }
        // Cuerpos físicos quietos DESDE YA: si no, en el fotograma de espera (antes del Start del kayak, que lo deja
        // quieto) la gravedad lo hacía caer y se quedaba por debajo del agua.
        foreach (Rigidbody rb in s.go.GetComponentsInChildren<Rigidbody>())
        {
            s.bodies.Add((rb, rb.isKinematic, rb.interpolation));
            rb.isKinematic = true;
        }
        s.pendingRise = true;
    }

    private void BeginRise(Sec s, double beatNow)
    {
        s.pendingRise = false;
        // Cuerpos físicos (el kayak): quietos y SIN interpolación mientras sube. Con interpolación, Unity coloca el objeto
        // donde dice la física y no donde lo mueve su padre: el ataúd se quedaba abajo.
        // (Los cuerpos ya están quietos desde Spawn; aquí solo se quita la interpolación mientras sube.)
        foreach (var (rb, _, _) in s.bodies) if (rb != null) rb.interpolation = RigidbodyInterpolation.None;
        s.go.transform.position = s.target - Vector3.up * riseDepth;
        foreach (var (rb, _, _) in s.bodies) if (rb != null) rb.position = rb.transform.position;
        foreach (Renderer r in s.hidden) if (r != null) r.enabled = true;
        s.hidden.Clear();
        s.riseStart = beatNow;
    }

    private void UpdateRise(Sec s, double beatNow)
    {
        float t = Mathf.Clamp01((float)((beatNow - s.riseStart) / Mathf.Max(0.1f, riseBeats)));
        float e = 1f - (1f - t) * (1f - t) * (1f - t);   // (sale rápido y frena al llegar)
        Vector3 p = s.go.transform.position;
        p.y = s.target.y - riseDepth * (1f - e);
        s.go.transform.position = p;
        foreach (var (rb, _, _) in s.bodies)
            if (rb != null) { rb.position = rb.transform.position; rb.rotation = rb.transform.rotation; }
        if (t >= 1f)
        {
            s.risen = true;
            foreach (var (rb, kin, interp) in s.bodies)
            {
                if (rb == null) continue;
                rb.position = rb.transform.position;
                // El kayak gestiona él solo su física (quieto hasta que el jugador se sube): se deja como lo deja su Start.
                if (rb.GetComponent<KayakController>() != null) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate; continue; }
                rb.isKinematic = kin; rb.interpolation = interp;
            }
            s.bodies.Clear();
        }
    }

    private void UpdateSections(double beatNow)
    {
        foreach (Sec s in _plan)
        {
            if (s.go == null)
            {
                bool due = s.seg * 4.0 <= beatNow + spawnAheadBeats && (s.seg + s.len) * 4.0 >= beatNow - despawnBehindBeats;
                if (due && s.riseStart < 0) Spawn(s, instant: false);
                continue;
            }
            if (s.pendingRise) { BeginRise(s, beatNow); continue; }
            if (!s.risen) UpdateRise(s, beatNow);
            // Muy atrás: se borra (la sala final se queda).
            if (s.kind != Kind.End && (s.seg + s.len) * 4.0 < beatNow - despawnBehindBeats) { Destroy(s.go); s.go = null; s.riseStart = 0; }
        }
    }

    /// <summary>Lo que se ilumina al explicar: el obstáculo de la sección (no el suelo ni las paredes del pasillo).</summary>
    private static Transform HighlightOf(Transform sec, TutorialKey key)
    {
        if (key == TutorialKey.None)
        {
            GazeSwitch g = sec.GetComponentInChildren<GazeSwitch>();
            if (g != null) return g.transform;
        }
        if (key == TutorialKey.Zipline)
        {
            Zipline z = sec.GetComponentInChildren<Zipline>();
            if (z != null) return z.transform;
        }
        int obstacle = LayerMask.NameToLayer("Obstacle");
        Transform best = null;
        foreach (Collider c in sec.GetComponentsInChildren<Collider>())
        {
            if (c.transform == sec) continue;   // (la raíz es el suelo)
            if (c.gameObject.layer == obstacle) return c.transform;
            if (best == null && c.gameObject.layer != 0) best = c.transform;   // (p. ej. paredes para correr)
        }
        return best != null ? best : sec;
    }

    // =========================================================================
    // BUCLE: secciones, checkpoints, caídas, consejo al morir, final
    // =========================================================================

    private void Update()
    {
        _dialog?.Tick();
        // La sala de la que se salió se borra al alejarse (si no, al llegar a la sala final se veía a lo lejos por el hueco).
        if (_startRoom != null && _pm != null && _pm.transform.position.z > _segLen * 3f) { Destroy(_startRoom); _startRoom = null; }
        // La entrada de la sala final se tapa en cuanto el jugador ya está dentro (no antes: entra andando por ella).
        if (_ended && _endRoom != null && _entranceCover == null && _pm != null &&
            _endRoom.InverseTransformPoint(_pm.transform.position).z > -24f)
            CloseEntrance(_endRoom);
        // Caída al vacío estando libre en la sala → de vuelta a su centro.
        if (_pm != null && _pm.IsFreeroam && _pm.transform.position.y < roomFallY && !_doorBusy) RespawnInRoom();
        if (Mode == CourseMode.Menu)
        {
            if (_infoHideAt > 0f && Time.unscaledTime > _infoHideAt) { _infoHideAt = -1f; HideInfo(); }
            return;
        }
        if (_pm == null) return;
        Vector3 pos = _pm.transform.position;
        // (Para los datos del estudio: en qué obstáculo está el jugador, por si muere.)
        int obNow = ObstacleIndexAt(Mathf.FloorToInt((pos.z + 2.5f) / _segLen));
        if (obNow < 0) obNow = ObstacleIndexAt(Mathf.FloorToInt(pos.z / _segLen));
        StudyTelemetry.CurrentObstacle = obNow >= 0 ? obstacles[obNow].titleEs : "";
        bool running = _bm != null && _bm.HasClock && _bm.ClockSeconds > 0.0;
        // El consejo / la explicación inicial se quitan en cuanto empieza a correr.
        if (running && !_started) { _started = true; HideInfo(); }
        if (running && _energyPhase) UpdateEnergyPhase(_bm.ClockBeats);
        if (running) UpdateSections(_bm.ClockBeats);
        if (_infoHideAt > 0f && Time.unscaledTime > _infoHideAt) { _infoHideAt = -1f; HideInfo(); }

        // Tirolina (tutorial y práctica): mientras vas colgado, recordatorio de que puedes moverte a los lados.
        // (La primera vez se PARA el juego para explicarlo, como las demás explicaciones.)
        bool zip = (Mode == CourseMode.Tutorial || Mode == CourseMode.Practice) && _pm.IsOnZipline;
        if (zip && !_zipHint)
        {
            _zipHint = true;
            // (Normalmente ya se ha explicado al engancharse, con el juego parado: ver Step.follow. Esto es por si no.)
            if (!_zipSideShown && (Mode == CourseMode.Practice || !Explained.Contains(ZipSideId)))
            {
                Explained.Add(ZipSideId);
                _zipSideShown = true;
                string text = ZipSideText();
                if (TutorialDirector.Instance != null) TutorialDirector.Instance.Explain(text);
                else ShowInfo(Loc.T("Tirolina", "Zipline"), text);
            }
        }

        // Checkpoints: se activa el último que se ha pasado (con la canción en su beat).
        if (running && (_gom == null || !_gom.IsDead))
            for (int i = _cpReached + 1; i < _cps.Count; i++)
            {
                float cz = _cps[i].seg * _segLen;
                // Checkpoint en un obstáculo: se reaparece UN BEAT ANTES de su sección, porque algunas acciones van
                // antes de entrar (doble salto, romper la puerta) y desde el borde mismo no daría tiempo.
                if (_cps[i].kind == Kind.Obstacle) cz -= _segLen / 4f;
                if (pos.z < cz - 0.5f) break;
                _cpReached = i;
                CheckpointSystem.Activate(new Vector3(0f, pos.y, cz));
            }

        // Caída al vacío.
        if (running && _gom != null && !_gom.IsDead && pos.y < fallY)
            _gom.TriggerGameOver(Loc.T("Te has caído", "You fell"));

        // Muerte: se prepara el consejo que saldrá al reaparecer.
        if (_gom != null && _gom.IsDead && !_deathHandled)
        {
            _deathHandled = true;
            _pendingTip = TipFor(pos.z, _gom.IsObstacleDeath);
            int cp = Mathf.Max(0, _cpReached);
            s_deathsAt[cp] = (s_deathsAt.TryGetValue(cp, out int n) ? n : 0) + 1;   // (para «Saltar sección»)
        }

        // Final del recorrido.
        float endZ = _endSec != null ? _endSec.seg * _segLen + _segLen * 0.5f : float.MaxValue;
        if (!_ended && running && !_energyPhase && pos.z >= endZ && (_gom == null || !_gom.IsDead))
        {
            _ended = true;
            OnCourseEnd();
        }
    }

    private string TipFor(float z, bool obstacle)
    {
        if (!obstacle)
            return Loc.T("Te has quedado sin energía. Haz clic AL RITMO para devolver sangre al corazón y, si no estás seguro, es mejor " +
                         "no hacer clic, ya que los fallos quitan más sangre que no hacer nada. Recuerda que cada acción también gasta energía.",
                         "You ran out of energy. Click ON THE BEAT to send blood back to your heart, and if you are not sure, " +
                         "better not click: misses take more blood than doing nothing. Remember that every action also costs energy.");
        // Obstáculo de la sección en la que estaba. Se mira un poco por delante (las puertas y muros están justo al
        // principio de su sección y el jugador choca antes de entrar en ella); si ahí no hay, en la que estaba.
        int a = ObstacleIndexAt(Mathf.FloorToInt((z + 2.5f) / _segLen));
        if (a < 0) a = ObstacleIndexAt(Mathf.FloorToInt(z / _segLen));
        if (a >= 0 && !string.IsNullOrEmpty(obstacles[a].tipEs)) return obstacles[a].Tip;
        return Loc.T("Fíjate en la nota que cae en el monitor del corazón, ya que indica qué tecla pulsar y en qué momento.",
                     "Watch the note falling on the heart monitor: it tells you which key to press and when.");
    }

    // =========================================================================
    // FASE DE ENERGÍA: suelo y más suelo hasta que se han explicado las 4 interacciones con la barra
    // =========================================================================

    /// <summary>
    /// Retrasa los obstáculos para que la primera sección de después de la fase de energía no aparezca todavía
    /// (siempre más allá de lo que se genera por delante) y rellena el hueco con tramos de suelo.
    /// </summary>
    private void UpdateEnergyShift(double beatNow)
    {
        int need = Mathf.CeilToInt((float)((beatNow + spawnAheadBeats + 4.0) / 4.0)) - _shiftFromSeg;
        if (need <= s_shift) return;
        for (int k = s_shift; k < need; k++) AddFiller(k);
        s_shift = need;
        ApplyShiftToScript();
    }

    /// <summary>Tramo de suelo de relleno nº k de la fase de energía (no se desplaza).</summary>
    private void AddFiller(int k)
    {
        bool before = _shifting; _shifting = false;
        AddFloor(_shiftFromSeg + k);
        _shifting = before;
    }

    private void ApplyShiftToScript()
    {
        if (_script == null || _stepBase == null) return;
        for (int i = 0; i < _script.steps.Length && i < _stepBase.Length; i++)
            _script.steps[i].beat = _stepBase[i] + (_stepFixed != null && i < _stepFixed.Length && _stepFixed[i] ? 0f : s_shift * 4f);
        _script.endBeat = _endBeatBase + s_shift * 4f;
    }

    // ── Avisos del clic (tutorial): pasos del guion con frenada, uno cada EnergyGap beats ──
    private const float EnergyFirstBeat = 4f;
    private const float EnergyGap = 5f;   // (se para en su beat; al seguir quedan 4 beats normales antes de frenar otra vez)
    private bool[] _stepFixed;            // pasos que no se desplazan con la fase de energía (los avisos del clic)

    private static float EnergyBeatOf(int k) => EnergyFirstBeat + k * EnergyGap;

    private static (string es, string en)[] EnergyTips => new[]
    {
        ("La jeringuilla izquierda extrae sangre del corazón poco a poco. Si se llena del todo, es GAME OVER. Se podría pensar como " +
         "una energía negativa: cuanto más llena está la jeringuilla, menos energía tienes.",
         "The left syringe slowly draws blood from your heart. If it fills up completely, it's GAME OVER. You can think of it as " +
         "negative energy: the fuller the syringe, the less energy you have."),
        ("Haz clic AL RITMO de la música para devolver sangre (energía) al corazón. El metrónomo marca cada beat, así que puedes probarlo ahora.",
         "Click ON THE BEAT of the music to send blood (energy) back to your heart. The metronome marks the beat: try it!"),
        ("Cada acción (saltar, deslizarte, hacer un dash…) también manda sangre a la jeringuilla roja. No hace falta hacer clic en todos los beats.",
         "Every action (jumping, sliding, dashing…) also sends blood to the red syringe. You don't need to click on every beat."),
        ("Un clic FUERA de ritmo te quita más sangre de golpe, de modo que, si no estás seguro, es mejor no hacer clic.",
         "An OFF-beat click takes extra blood at once. If you're not sure, better not click."),
        ("Cuando el corazón está lleno, los aciertos llenan la jeringuilla AZUL, que es energía extra. Si la llenas, entrarás en SOBRECARGA.",
         "With your heart full, hits fill the BLUE syringe: extra energy. Fill it up and you enter OVERLOAD."),
        ("En SOBRECARGA, las acciones no gastan energía durante 4 beats, pero si fallas un clic con sangre azul, la pierdes toda.",
         "In OVERLOAD, for 4 beats your actions cost no energy. But if you miss a click with blue blood, you lose all of it."),
    };

    /// <summary>
    /// Parte del HUD de la que habla cada aviso (se resalta: late, brilla y una flecha la señala; la caja de texto sube
    /// arriba). 0, 2, 3 → la jeringuilla roja; 1 (clic al ritmo, metrónomo) → el monitor del corazón; 4, 5 → la azul.
    /// </summary>
    private static SyringeHighlight.Side EnergyHud(int k) =>
        k == 0 || k == 2 || k == 3 ? SyringeHighlight.Side.Left : k == 1 ? SyringeHighlight.Side.Monitor : SyringeHighlight.Side.Right;

    private static bool EnergyTipsDone()
    {
        for (int k = 0; k < EnergyTips.Length; k++) if (!Explained.Contains("E." + k)) return false;
        return true;
    }

    /// <summary>
    /// Fase de energía del tutorial: solo suelo mientras salen los avisos del clic (pasos del guion: el juego frena,
    /// se para, se explica y sigue). Tras el último, y 4 beats para probarlo, empiezan los obstáculos.
    /// </summary>
    private void UpdateEnergyPhase(double beatNow)
    {
        UpdateEnergyShift(beatNow);
        float last = EnergyBeatOf(EnergyTips.Length - 1);
        if (!EnergyTipsDone() || TutorialGate.Busy || beatNow < last + 4f) return;
        // Todo explicado: los obstáculos se quedan donde están (a 16 beats o más) y empiezan a aparecer.
        _energyPhase = false;
        s_shiftFinal = s_shift;
        ShowInfo(Loc.T("¡Ya sabes cómo funciona la energía!", "Now you know how energy works!"),
                 Loc.T($"Ahora vienen los obstáculos. Muévete con {Keybinds.Name(Keybinds.Bind.Left)} y {Keybinds.Name(Keybinds.Bind.Right)}. " +
                       "Para ver todos los controles pulsa TAB.",
                       $"Now the obstacles are coming. Move with {Keybinds.Name(Keybinds.Bind.Left)} and {Keybinds.Name(Keybinds.Bind.Right)}. " +
                       "To see all the controls press TAB."));
        _infoHideAt = Time.unscaledTime + 7f;   // (más texto: más tiempo para leerlo)
    }

    private void SetGoal(string text)
    {
        if (_goal == null) return;
        bool show = !string.IsNullOrEmpty(text) && !HudHider.IsHidden;
        if (_goal.gameObject.activeSelf != show) _goal.gameObject.SetActive(show);
        if (show && _goal.text != text) _goal.text = text;
    }

    /// <summary>Índice del obstáculo que ocupa la sección 'seg' (−1 = suelo / nada).</summary>
    public int ObstacleIndexAt(int seg)
    {
        foreach (Sec s in _plan)
            if (s.kind == Kind.Obstacle && seg >= s.seg && seg < s.seg + s.len) return s.obstacle;
        return -1;
    }

    /// <summary>Primera sección del obstáculo que ocupa 'seg' (int.MinValue = ninguno).</summary>
    public int ObstacleStartAt(int seg)
    {
        foreach (Sec s in _plan)
            if (s.kind == Kind.Obstacle && seg >= s.seg && seg < s.seg + s.len) return s.seg;
        return int.MinValue;
    }

    private void OnCourseEnd()
    {
        if (Mode == CourseMode.Tutorial) { PlayerPrefs.SetInt(PrefDone, 1); PlayerPrefs.Save(); }
        Selected = CourseMode.None;
        if (_end != null)
        {
            // Tras la nota NO se cambia de escena: el jugador se queda en la sala final (también en el modo prueba: allí
            // responde el cuestionario y cruza la puerta grande para la siguiente parte).
            _end.SetNextScene("");
            _end.onResultsClosed = OpenEndRoom;
        }
        if (_end != null)
        {
            // El «mirar alrededor» empieza al entrar en la sala (no nada más pasar la meta) y, al llegar, la ceremonia:
            // las jeringuillas se vacían y el corazón se pesa en la balanza.
            foreach (Sec x in _plan) if (x.kind == Kind.End) _end.lookAroundFromZ = x.seg * _segLen;
            if (Mode == CourseMode.Practice)
            {
                // Práctica de un obstáculo: sin balanza ni resultados; entra en la sala y queda libre.
                _end.customArrival = PracticeArrival;
                _end.skipResults = true;
                _end.quickArrival = true;   // (sin mirar alrededor y más deprisa: es solo una práctica)
            }
            else _end.customArrival = Ceremony;
            if (Mode == CourseMode.Tutorial) { _end.resultsTitleEs = "TUTORIAL COMPLETADO"; _end.resultsTitleEn = "TUTORIAL COMPLETE"; }
            else if (Mode == CourseMode.Study) { _end.resultsTitleEs = "RECORRIDO COMPLETADO"; _end.resultsTitleEn = "RUN COMPLETE"; }
            SetCrosshair(false);   // (como al final de los niveles: sin mirilla)
            _end.BeginEnd(_finish);
        }
    }

    private IEnumerator FadeOutLevelMusic(float seconds)
    {
        AudioSource src = _bm != null ? _bm.CurrentSource : null;
        if (src == null || !src.isPlaying) yield break;
        float v0 = src.volume;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            src.volume = Mathf.Lerp(v0, 0f, t / seconds);
            yield return null;
        }
        _bm.StopMusic();
        src.volume = v0;
    }

    /// <summary>Muestra / esconde la mirilla del HUD (con un CanvasGroup, sin tocar su lógica).</summary>
    private static void SetCrosshair(bool visible)
    {
        // (Se apaga el objeto «Crosshair» entero: corazón, pinchos, aros y notas.)
        foreach (CrosshairController c in FindObjectsByType<CrosshairController>(FindObjectsInactive.Include))
            c.gameObject.SetActive(visible);
    }

    /// <summary>Al llegar a la meta: se vacían las jeringuillas (animado) y se pesa el corazón contra la pluma.</summary>
    /// <summary>
    /// Final de la PRÁCTICA: las jeringuillas se vacían, la mano baja suave con el corazón y la música cambia a la de la
    /// sala, sin balanza ni resultados (luego OpenEndRoom deja al jugador libre en la sala).
    /// </summary>
    private IEnumerator PracticeArrival()
    {
        SetCrosshair(false);
        EnergyManager em = EnergyManager.Instance;
        if (em != null) em.SetSyringeDirect(0f, false);
        StartCoroutine(MoveHand(0f, 1f, 0.8f));   // (la mano con el corazón baja suave)
        yield return FadeOutLevelMusic(0.8f);
        if (_lobby != null) _lobby.forcePlay = true;
    }

    private IEnumerator Ceremony()
    {
        EnergyManager em = EnergyManager.Instance;
        if (em != null)
        {
            bool blue = em.IsBlueMode;
            float from = em.SyringeBlood;
            const float dur = 1.6f;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                em.SetSyringeDirect(Mathf.Lerp(from, 0f, Mathf.SmoothStep(0f, 1f, t / dur)), blue);
                yield return null;
            }
            em.SetSyringeDirect(0f, false);
        }
        if (_scale != null && _pm != null)
        {
            // Nota del nivel: aciertos / latidos (la misma precisión que la de los resultados).
            float acc = RunStats.HeartPresses > 0 ? (float)RunStats.Hits / RunStats.HeartPresses : 0f;
            float score = Mathf.InverseLerp(0.4f, 0.95f, acc);
            yield return _scale.Weigh(_pm.transform, Camera.main, score);
        }
        // Después de la balanza: sin mirilla y cambia la música (se apaga la del nivel y suena la de la sala).
        SetCrosshair(false);
        StartCoroutine(MoveHand(0f, 1f, 0.8f));   // (la mano, ya sin corazón, baja suave)
        yield return FadeOutLevelMusic(1.2f);
        if (_lobby != null) _lobby.forcePlay = true;
    }

    /// <summary>Resultados cerrados: el jugador se mueve libre por la sala final (puertas con E).</summary>
    private void OpenEndRoom()
    {
        var cc = _pm != null ? _pm.GetComponent<CharacterController>() : null;
        if (cc != null) cc.enabled = true;
        if (_pm != null) { _pm.enabled = true; _pm.EnableFreeroam(); }
        TutorialGate.UnlockAll();
        HideHudForRoom();   // (en la sala: sin mano, sin mirilla y sin la barra de energía)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        CloseEntrance(_endRoom);   // (detrás de la entrada ya no hay recorrido: se tapa para no caer al vacío)
        // Modo prueba, de vuelta de PRACTICAR una parte (antes de empezar el estudio): sin volver a enseñar la pantalla de
        // la parte ni el aviso; el jugador queda libre y la puerta grande sigue lista.
        if (StudySession.Active && !StudySession.InFreePlay && !StudySession.RunsStarted && StudySession.RoomIntroSeen)
        {
            StudyDoorOpen = true;
            HighlightBigDoor(true);
            return;
        }
        if (StudySession.Active && !StudySession.InFreePlay) { OpenStudyHub(); return; }
        HighlightBigDoor(true);   // (para que se vea cuál es la puerta grande)
        ShowInfo(Loc.T("Sala del tutorial", "Tutorial room"), DoorsHelp());
    }

    // =========================================================================
    // MODO PRUEBA EN LA SALA: cuestionario → siguiente parte por la puerta grande
    // =========================================================================

    private Transform _endRoom;
    private GameObject _entranceCover;

    /// <summary>
    /// Las paredes de wallrun (capa «Jumpable») no son obstáculos, así que chocar de frente con ellas no mataba. A cada
    /// una cuya cara delantera (la que mira al jugador, −Z) quede al aire se le pone delante una losa fina en la capa
    /// «Obstacle» con solo la cara frontal letal: de frente mata; por los lados se sigue corriendo por la pared.
    /// </summary>
    private static void AddFrontKillFaces(Transform section)
    {
        int jumpable = LayerMask.NameToLayer("Jumpable"), obstacle = LayerMask.NameToLayer("Obstacle");
        if (jumpable < 0 || obstacle < 0) return;
        var walls = new List<Collider>();
        foreach (Collider c in section.GetComponentsInChildren<Collider>())
            if (c.gameObject.layer == jumpable && !c.isTrigger && c.bounds.size.y > 3f && c.bounds.size.z > c.bounds.size.x) walls.Add(c);
        foreach (Collider c in walls)
        {
            Bounds b = c.bounds;
            // (Si justo delante hay otra pared seguida, esta cara no se ve: no hace falta.)
            Vector3 probe = new Vector3(b.center.x, b.center.y, b.min.z - 0.3f);
            bool covered = false;
            foreach (Collider o in walls) if (o != c && o.bounds.Contains(probe)) { covered = true; break; }
            if (covered) continue;

            var go = new GameObject("Cara frontal letal");
            go.layer = obstacle;
            go.transform.SetParent(c.transform, true);
            go.transform.position = new Vector3(b.center.x, b.center.y, b.min.z - 0.1f);
            go.transform.rotation = Quaternion.identity;
            var box = go.AddComponent<BoxCollider>();
            Vector3 ls = go.transform.lossyScale;
            box.size = new Vector3(b.size.x * 0.9f / Mathf.Max(0.001f, Mathf.Abs(ls.x)),
                                   b.size.y * 0.98f / Mathf.Max(0.001f, Mathf.Abs(ls.y)),
                                   0.4f / Mathf.Max(0.001f, Mathf.Abs(ls.z)));
            go.AddComponent<ObstacleInfo>().lethalFaces = LethalFaces.Front;
        }
    }

    // ── «Saltar sección»: tras morir 3 veces en la misma sección (tutorial y estudio) ──
    private const int DeathsToSkip = 3;
    private static readonly Dictionary<int, int> s_deathsAt = new Dictionary<int, int>();   // checkpoint → muertes

    private bool CanSkipSection()
    {
        if (Mode != CourseMode.Tutorial && Mode != CourseMode.Study) return false;
        int cp = Mathf.Max(0, _cpReached);
        return cp + 1 < _cps.Count && s_deathsAt.TryGetValue(cp, out int n) && n >= DeathsToSkip;
    }

    /// <summary>El siguiente checkpoint pasa a ser el activo (GameOverManager reaparece después en él).</summary>
    private void SkipToNextCheckpoint()
    {
        int cp = Mathf.Max(0, _cpReached);
        if (cp + 1 >= _cps.Count) return;
        Sec next = _cps[cp + 1];
        float cz = next.seg * _segLen;
        if (next.kind == Kind.Obstacle) cz -= _segLen / 4f;   // (como al pasarlo: un beat antes de la sección)
        float y = CheckpointSystem.HasActive ? CheckpointSystem.Active.pos.y : 1.08f;
        StudyTelemetry.SectionSkipped(cp, StudyTelemetry.CurrentObstacle);
        CheckpointSystem.Activate(new Vector3(0f, y, cz));
        _pendingTip = null;
        Debug.Log($"[Curso] Sección saltada: del checkpoint {cp} al {cp + 1}.");
    }

    /// <summary>
    /// Cartel «EMPEZAR» encima de la PUERTA GRANDE (la que lleva al recorrido de estudio), mirando hacia dentro de la sala.
    /// </summary>
    private static void AddBigDoorSign(Transform room)
    {
        if (room == null || room.Find("Cartel · EMPEZAR") != null) return;
        CourseDoor big = null;
        foreach (CourseDoor d in room.GetComponentsInChildren<CourseDoor>(true)) if (d.Mode == CourseMode.Study) { big = d; break; }
        if (big == null) return;
        // Tamaño y sitio: encima de las hojas de la puerta.
        Bounds b = default; bool any = false;
        foreach (Transform leaf in big.LeafTransforms)
            if (leaf != null)
                foreach (Renderer r in leaf.GetComponentsInChildren<Renderer>())
                { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        if (!any) return;
        Vector3 outward = big.Outward;
        Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);   // (el texto se lee desde dentro de la sala)
        float width = Mathf.Clamp(Vector3.Scale(b.size, new Vector3(Mathf.Abs(outward.z), 0f, Mathf.Abs(outward.x))).magnitude * 0.7f, 6f, 16f);

        var sign = new GameObject("Cartel · EMPEZAR").transform;
        sign.SetParent(room, true);
        sign.SetPositionAndRotation(new Vector3(b.center.x, b.max.y + 2.2f, b.center.z) - outward * (b.extents.magnitude > 0f ? 0.6f : 0f), rot);
        GameObject board = RuntimeMaterials.Primitive(PrimitiveType.Cube);
        board.name = "Tabla";
        Destroy(board.GetComponent<Collider>());
        board.transform.SetParent(sign, false);
        board.transform.localScale = new Vector3(width, 3f, 0.3f);
        board.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.Colored(new Color(0.88f, 0.8f, 0.62f));

        var txt = new GameObject("Texto", typeof(RectTransform));
        txt.transform.SetParent(sign, false);
        txt.transform.localPosition = new Vector3(0f, 0f, -0.2f);
        var t = txt.AddComponent<TextMeshPro>();
        t.text = Loc.T("EMPEZAR", "START");
        t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold;
        t.enableAutoSizing = true; t.fontSizeMin = 1f; t.fontSizeMax = 30f;
        t.color = Color.black;
        t.rectTransform.sizeDelta = new Vector2(width * 0.9f, 2.6f);
        var loc = txt.AddComponent<LocText3D>();
        loc.es = "EMPEZAR"; loc.en = "START";
    }

    /// <summary>Desde la pausa: deja lo que se estaba repitiendo y vuelve a la sala (libre, sin la ceremonia).</summary>
    private void BackToRoomFromPause()
    {
        Selected = CourseMode.Menu;
        s_fromRoom = false;
        s_doorName = null;
        _pendingTip = null;
        CheckpointSystem.Clear();
        s_blinkIn = true;   // (abre los ojos ya en la sala)
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Resalta (o no) la PUERTA GRANDE de la sala final: brilla en dorado y la ilumina una luz.</summary>
    private void HighlightBigDoor(bool on)
    {
        if (_endRoom == null) return;
        foreach (CourseDoor d in _endRoom.GetComponentsInChildren<CourseDoor>(true))
            if (d.Mode == CourseMode.Study) { if (on) DoorHighlight.On(d); else DoorHighlight.Off(d); }
    }

    /// <summary>
    /// Tapa la ENTRADA de la sala final (la pared por la que se llega desde el recorrido): detrás ya no queda suelo (las
    /// secciones de atrás se borran) y por ese hueco se caía al vacío. Se pone un bloque con el material de esa pared,
    /// por fuera de ella, del tamaño de toda la pared.
    /// </summary>
    private void CloseEntrance(Transform room)
    {
        if (room == null || _entranceCover != null) return;
        Renderer back = null;
        float minZ = float.MaxValue;
        Transform walls = room.Find("Walls");
        if (walls != null)
            foreach (Transform w in walls)
            {
                // (La de la entrada: la que está más atrás, en el lado por el que se llega.)
                Vector3 lp = room.InverseTransformPoint(w.position);
                Renderer r = w.GetComponent<Renderer>();
                if (r != null && lp.z < minZ && Mathf.Abs(lp.x) < 1f) { minZ = lp.z; back = r; }
            }
        // El HUECO de la sala es la abertura de esa pared (por donde se llega desde el recorrido). Se rellena con un bloque
        // MARRÓN del tamaño exacto de la pared y metido DENTRO de ella (un poco más fino que la pared): sus caras quedan
        // escondidas en el muro y solo se ve donde está la abertura, a ras de la pared y con su misma altura.
        if (back != null)
        {
            Bounds b = back.bounds;
            Material brown = null;
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Bloque")) { brown = r.sharedMaterial; break; }
            if (brown == null) brown = RuntimeMaterials.Colored(new Color(0.42f, 0.3f, 0.2f));
            GameObject cover = RuntimeMaterials.Primitive(PrimitiveType.Cube);
            cover.name = "Tapa de la entrada";
            cover.transform.SetParent(room, true);
            cover.transform.SetPositionAndRotation(b.center, Quaternion.identity);
            float depth = Mathf.Max(0.05f, b.size.z * 0.8f);
            cover.transform.localScale = new Vector3(b.size.x * 0.995f, b.size.y * 0.995f, depth);
            cover.GetComponent<Renderer>().sharedMaterial = brown;
        }
        _entranceCover = room.gameObject;   // (marca: ya hecho)
        SealRoom(room, back != null ? Mathf.Abs(minZ) : 30.9f);
    }

    /// <summary>
    /// CIERRE INVISIBLE de la sala final: una pared invisible (solo colisión) justo por fuera de cada uno de sus 10 lados,
    /// de toda la altura. Así, aunque quede algún hueco entre paredes, el jugador no puede salir andando y caer al vacío.
    /// (Al cruzar una puerta el jugador va con el CharacterController apagado: no le afecta.)
    /// </summary>
    private static void SealRoom(Transform room, float apothem)
    {
        if (room.Find("Cierre invisible") != null) return;
        var holder = new GameObject("Cierre invisible").transform;
        holder.SetParent(room, false);
        float side = 2f * apothem * Mathf.Tan(18f * Mathf.Deg2Rad) + 2f;   // (lado del decágono, con margen)
        for (int k = 0; k < 10; k++)
        {
            Vector3 dir = Quaternion.Euler(0f, k * 36f, 0f) * Vector3.forward;
            GameObject go = new GameObject();
            go.AddComponent<BoxCollider>();
            bool gap = false;   // (el hueco ya lo tapa el bloque marrón de CloseEntrance: aquí todo invisible)
            go.name = $"Cierre {k}{(gap ? " (tapa un hueco)" : "")}";
            go.transform.SetParent(holder, false);
            // (El que tapa un hueco va pegado a la pared, para rellenarlo; los invisibles, un poco por fuera.)
            go.transform.localPosition = dir * (apothem + (gap ? 0.3f : 0.8f)) + Vector3.up * 25f;
            go.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
            go.transform.localScale = new Vector3(side, 60f, 0.6f);   // (el BoxCollider/cubo mide 1: la escala da el tamaño)
        }
    }

    /// <summary>Caída en la sala final (moviéndose libre): vuelve a su centro, sin nada en curso.</summary>
    private void RespawnInRoom()
    {
        if (_finish == null || _pm == null) return;
        _pm.ResetMotionAfterRewind();
        Vector3 forward = _endRoom != null ? Flat(_endRoom.forward).normalized : Vector3.forward;
        _pm.TeleportToStartPoint(_finish.position, Quaternion.LookRotation(forward.sqrMagnitude > 0.01f ? forward : Vector3.forward, Vector3.up));
        var cc = _pm.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;
        _pm.EnableFreeroam();
        Debug.Log("[Curso] Caída en la sala: de vuelta al centro.");
    }

    /// <summary>¿Se puede cruzar ya la puerta grande hacia la siguiente parte del estudio?</summary>
    public static bool StudyDoorOpen { get; private set; }

    /// <summary>
    /// Al volver a la sala en el modo prueba: StudyHub enseña aquí el cuestionario de la parte y la siguiente (o el final).
    /// Al pulsar «Ir a la puerta grande» el jugador vuelve a moverse y la puerta grande se abre para esa parte.
    /// </summary>
    private void OpenStudyHub()
    {
        if (_pm != null) _pm.enabled = false;   // (quieto mientras responde)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        StudyHub.RoomPartReady = () =>
        {
            StudyHub.RoomPartReady = null;
            if (_pm != null) { _pm.enabled = true; _pm.EnableFreeroam(); }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            StudyDoorOpen = true;
            StudySession.RoomIntroSeen = true;
            HighlightBigDoor(true);
            ShowInfo(StudySession.Current.title,
                     Loc.T("Para empezar, mira la PUERTA GRANDE y pulsa E para cruzarla.", "To start, look at the BIG DOOR and press E to go through it."));
        };
        if (FindAnyObjectByType<StudyHub>() == null) new GameObject("StudyHub").AddComponent<StudyHub>();
    }

    private float _infoHideAt = -1f;

    /// <summary>
    /// Puertas de la sala final:
    ///   • En cada pared que llega al suelo, 2-3 puertas a ras de la pared: tutorial completo, recorrido de estudio y una
    ///     por obstáculo. Al elegir una, el recorrido sale POR ESA PARED (ver SpawnStartRoom).
    ///   • La puerta grande (LevelDoorLeft / LevelDoorRight) → los niveles.
    ///   • ShopRoom → tienda; ContractsRoom → contratos (los puestos de la zona de inicio de los niveles).
    /// </summary>
    private void BuildDoors(Transform room)
    {
        float floorY = room.position.y - endHeightOffset;   // (y del suelo del recorrido)

        // Puerta grande (sus dos hojas, LevelDoorLeft / LevelDoorRight) → recorrido de estudio, saliendo por ella.
        var big = new List<Collider>();
        Transform bigLeaf = null;
        foreach (Transform t in room)
            if (t.name.StartsWith("LevelDoor")) { big.AddRange(t.GetComponentsInChildren<Collider>()); bigLeaf = t; }
        if (big.Count > 0)
        {
            float bigYaw = Yaw(room, bigLeaf);
            CourseDoor.Create(room, room.position + room.forward * 30.9f + Vector3.up * 27f, room.rotation,
                              "Recorrido de estudio", "Study run", new Color(1f, 0.8f, 0.3f),
                              () => Choose(CourseMode.Study, 0, bigYaw), big.ToArray());
        }

        // Tienda y contratos: locales del siglo XIX excavados en el muro, en lugar de los bloques ShopRoom /
        // ContractsRoom. El propio local es lo interactuable (mirarlo y pulsar E).
        foreach (Transform t in room)
        {
            bool shop = t.name == "ShopRoom", contracts = t.name == "ContractsRoom";
            if (!shop && !contracts) continue;
            Vector3 dir = t.position - room.position; dir.y = 0f; dir.Normalize();
            float depth = 2.5f * Mathf.Abs(t.lossyScale.z);   // (medio grosor del bloque, 5 m: su cara interior está a ras de las paredes)
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (Collider c in t.GetComponentsInChildren<Collider>()) c.enabled = false;
            Vector3 face = new Vector3(t.position.x, floorY, t.position.z) - dir * depth;
            GameObject local = CarvedShop.Build(room, face, Quaternion.LookRotation(dir, Vector3.up), contracts);
            FreeroamStand st = shop ? local.AddComponent<ShopStand>() : (FreeroamStand)local.AddComponent<ContractStand>();
            if (shop) st.Configure("[ E ]  Tienda", "[ E ]  Shop", new Color(0.2f, 0.7f, 0.25f), Vector3.zero, 9f, showLabel: false);   // (el cartel ya lo dice)
            else      st.Configure("[ E ]  Contratos", "[ E ]  Contracts", new Color(0.75f, 0.15f, 0.15f), Vector3.zero, 9f, showLabel: false);
        }

        // Puertas del tutorial en las paredes.
        var items = new List<(string es, string en, CourseMode mode, int idx, Color col)>
        {
            ("Tutorial completo", "Whole tutorial", CourseMode.Tutorial, 0, new Color(0.35f, 0.85f, 1f)),
        };
        for (int i = 0; i < obstacles.Count; i++)
            if (Valid(i)) items.Add((obstacles[i].titleEs, obstacles[i].titleEn, CourseMode.Practice, i, new Color(0.8f, 0.8f, 0.85f)));

        var walls = TutorialWalls(room);
        // Orden: en el sentido de las agujas del reloj empezando por la pared de delante a la derecha.
        walls.Sort((a, b) => Yaw(room, a).CompareTo(Yaw(room, b)));
        if (walls.Count == 0) return;
        int per = Mathf.CeilToInt(items.Count / (float)walls.Count), k = 0;
        for (int w = 0; w < walls.Count && k < items.Count; w++)
        {
            int left = items.Count - k, wallsLeft = walls.Count - w;
            int n = Mathf.Min(per, Mathf.CeilToInt(left / (float)wallsLeft));
            Transform wall = walls[w];
            Vector3 dir = wall.position - room.position; dir.y = 0f;
            float dist = dir.magnitude; dir /= Mathf.Max(0.01f, dist);
            Vector3 tangent = Vector3.Cross(Vector3.up, dir);
            float yaw = Yaw(room, wall);
            for (int j = 0; j < n; j++, k++)
            {
                float off = (j - (n - 1) * 0.5f) * 6.5f;
                Vector3 pos = new Vector3(room.position.x, floorY, room.position.z) + dir * (dist - 0.6f) + tangent * off;
                var it = items[k];
                CourseDoor.Create(room, pos, Quaternion.LookRotation(-dir, Vector3.up), it.es, it.en, it.col,
                                  () => Choose(it.mode, it.idx, yaw), null, new Vector3(5f, 8f, 0.2f));
            }
        }
    }

    /// <summary>Ángulo (0…360, sentido horario desde el frente de la sala) de una pared.</summary>
    private static float Yaw(Transform room, Transform wall)
    {
        Vector3 d = room.InverseTransformPoint(wall.position); d.y = 0f;
        float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        return a < 0f ? a + 360f : a;
    }

    // =========================================================================
    // MENÚ (tutorial completado): repetir, practicar un obstáculo o el recorrido de estudio
    // =========================================================================

    private void ShowMenu()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        var back = Box("Menu", _canvas, new Color(0f, 0f, 0f, 0.8f));
        Stretch(back.rectTransform, 0f, 0f, 0f, 0f);
        var panel = Box("Panel", back.transform, PanelCol);
        var phases = new List<int>();
        for (int i = 0; i < obstacles.Count; i++) if (Valid(i)) phases.Add(i);
        int rows = Mathf.CeilToInt(phases.Count / 3f);
        float h = 260f + rows * 80f + 100f;
        Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, h));
        var title = Lbl("Titulo", panel.transform, Loc.T("Tutorial completado", "Tutorial complete"), FontBig, TextAlignmentOptions.Center);
        PlaceTopLeft(title.rectTransform, 40f, 30f, 1220f, 50f);
        var sub = Lbl("Sub", panel.transform, Loc.T("Elige qué quieres practicar:", "Choose what you want to practise:"), FontMid, TextAlignmentOptions.Center);
        sub.color = DimText;
        PlaceTopLeft(sub.rectTransform, 40f, 85f, 1220f, 36f);

        float y = 140f;
        for (int i = 0; i < phases.Count; i++)
        {
            int idx = phases[i];
            var b = Btn("Fase", panel.transform, obstacles[idx].Title, out _);
            PlaceTopLeft((RectTransform)b.transform, 40f + (i % 3) * 410f, y + (i / 3) * 80f, 390f, 64f);
            b.onClick.AddListener(() => Choose(CourseMode.Practice, idx));
        }
        y += rows * 80f + 20f;
        var all = Btn("Todo", panel.transform, Loc.T("Repetir el tutorial completo", "Replay the whole tutorial"), out _);
        PlaceTopLeft((RectTransform)all.transform, 40f, y, 600f, 64f);
        all.onClick.AddListener(() => Choose(CourseMode.Tutorial, 0));
        var study = Btn("Estudio", panel.transform, Loc.T("Recorrido de estudio", "Study run"), out _);
        PlaceTopLeft((RectTransform)study.transform, 660f, y, 600f, 64f);
        study.onClick.AddListener(() => Choose(CourseMode.Study, 0));
        var menu = Btn("Salir", panel.transform, Loc.T("Volver al menú principal", "Back to the main menu"), out _);
        PlaceTopLeft((RectTransform)menu.transform, 350f, y + 84f, 600f, 64f);
        menu.onClick.AddListener(() =>
        {
            Selected = CourseMode.None;
            CheckpointSystem.Clear();
            SceneManager.LoadScene(StudySession.MenuScene);
        });
    }

    private static void Choose(CourseMode mode, int index) => Choose(mode, index, float.NaN);

    /// <summary>Puerta de la sala final (CourseDoor): empieza ese modo saliendo por la pared 'roomYaw' (NaN = sin pared).</summary>
    public static void ChooseFromDoor(CourseMode mode, int index, float roomYaw) => Choose(mode, index, roomYaw);

    /// <summary>Elige qué jugar y recarga la escena. 'roomYaw' = pared de la puerta (NaN = sin sala de salida).</summary>
    private static void Choose(CourseMode mode, int index, float roomYaw)
    {
        PrepareChoice(mode, index, roomYaw);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Deja apuntado qué se juega al recargar la escena (sin recargarla).</summary>
    private static void PrepareChoice(CourseMode mode, int index, float roomYaw)
    {
        s_fromRoom = !float.IsNaN(roomYaw);
        s_roomYaw = s_fromRoom ? roomYaw : 0f;
        Selected = mode;
        PracticeIndex = index;
        _pendingTip = null;
        CheckpointSystem.Clear();
        if (mode == CourseMode.Tutorial) { Explained.Clear(); s_shiftFinal = -1; }   // (se vuelve a explicar todo)
    }

    // =========================================================================
    // INTERFAZ: panel de explicación / consejo
    // =========================================================================

    private void BuildUI()
    {
        UIKit.EnsureEventSystem();
        _canvas = UIKit.CreateCanvas("CursoCanvas", 720, transform).transform;
        // Avisos: caja de texto abajo, sólida, letra a letra; clic para verlo entero, mantener el clic para saltarlo.
        _dialog = DialogBox.Create(_canvas);

        _fade = Box("Fundido", _canvas, Color.black);
        _fade.raycastTarget = false;
        Stretch(_fade.rectTransform, 0f, 0f, 0f, 0f);
        _fade.gameObject.SetActive(false);

        _goal = Lbl("Objetivo", _canvas, "", FontMid + 4f, TextAlignmentOptions.Center);
        _goal.color = new Color(1f, 0.85f, 0.25f);
        _goal.textWrappingMode = TextWrappingModes.Normal;
        Place(_goal.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 460f), new Vector2(1300f, 70f), new Vector2(0.5f, 0f));   // (encima de la caja de texto)
        _goal.gameObject.SetActive(false);
        _fade.transform.SetAsLastSibling();   // (el fundido, por encima de todo)

        // Párpados del parpadeo al cruzar una puerta.
        _lidTop = Box("Párpado arriba", _canvas, Color.black).rectTransform;
        _lidBottom = Box("Párpado abajo", _canvas, Color.black).rectTransform;
        _lidTop.GetComponent<Image>().raycastTarget = _lidBottom.GetComponent<Image>().raycastTarget = false;
        SetLids(0f);
        _lidTop.gameObject.SetActive(false); _lidBottom.gameObject.SetActive(false);
    }

    private void ShowInfo(string title, string text)
    {
        if (_dialog == null) return;
        _dialog.Show(title, Keybinds.Fix(text), singlePage: true);   // (todo en un bloque: mientras se juega no hay clic)
    }

    private void HideInfo() { _dialog?.Hide(); }

    // =========================================================================
    // VALORES POR DEFECTO
    // =========================================================================

    [ContextMenu("Rellenar por defecto")]
    private void FillDefaults()
    {
#if UNITY_EDITOR
        floorPrefab = Prefab("Estudio/Tramo_Suelo");
        endPrefab = Prefab("End");
        coinPrefab = Prefab("Coin");
#endif
        obstacles = DefaultObstacles();
    }

    /// <summary>
    /// Añade al final de la lista los obstáculos por defecto que FALTAN (por prefab), sin tocar los que ya hay ni sus
    /// cambios. Así, al crear prefabs nuevos en Prefabs/Estudio (kayak, mirada…) entran solos en el tutorial.
    /// </summary>
    [ContextMenu("Añadir los obstáculos que falten")]
    private void MergeDefaults()
    {
        if (obstacles == null) obstacles = new List<CourseObstacle>();
        bool changed = false;
        foreach (CourseObstacle d in DefaultObstacles())
        {
            if (d.prefab == null) continue;
            bool has = false;
            foreach (CourseObstacle o in obstacles) if (o != null && o.prefab == d.prefab) { has = true; break; }
            if (has) continue;
            obstacles.Add(d);
            changed = true;
            Debug.Log($"[Curso] Añadido al tutorial: {d.titleEs} ({d.prefab.name}).");
        }
#if UNITY_EDITOR
        if (endPrefab == null) { endPrefab = Prefab("End"); changed |= endPrefab != null; }
        // Música: PORTAPAPELES en el nivel y PORTAPAPELES-LOBBY en la sala (si no existe, la del lobby 1).
        var song = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Songs/PORTAPAPELES.wav");
        if (song != null && (tutorialMusic == null || tutorialMusic.name == "MC")) { tutorialMusic = song; changed = true; }
        var lobby = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Songs/PORTAPAPELES-LOBBY.wav");
        if (lobby != null && (endRoomMusic == null || endRoomMusic.name == "Lobby1" || endRoomMusic.name == "PORTAPAPELES-INTRO"))
        { endRoomMusic = lobby; endRoomMusicBpm = 200f; changed = true; }
        if (endRoomMusic == null)
        {
            endRoomMusic = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Songs/Lobby1.wav");
            changed |= endRoomMusic != null;
        }
        if (_musicVersion < 1) { endRoomMusicBpm = endRoomMusic != null && endRoomMusic.name == "Lobby1" ? 240f : 200f; _musicVersion = 1; changed = true; }
        // Música de la sala: Papercut (medido: 133,33 BPM = 400/3, en 3/4, 120 beats exactos; el primer tiempo fuerte en el beat 2).
        if (_musicVersion < 2)
        {
            var papercut = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Songs/Papercut.wav");
            if (papercut != null)
            {
                endRoomMusic = papercut; endRoomMusicBpm = 400f / 3f; endRoomBeatsPerBar = 3; endRoomDownbeatBeat = 2;
                _musicVersion = 2; changed = true;
            }
        }
        // Acciones por defecto (ActionsVersion): se aplican una vez a los obstáculos que ya había en la lista.
        if (_actionsVersion < ActionsVersion)
        {
            foreach (CourseObstacle d in DefaultObstacles())
                foreach (CourseObstacle o in obstacles)
                    if (o != null && d.prefab != null && o.prefab == d.prefab) { o.actions = d.actions; o.tipEs = d.tipEs; o.tipEn = d.tipEn; }
            _actionsVersion = ActionsVersion;
            changed = true;
            Debug.Log("[Curso] Acciones del tutorial actualizadas con la grabación.");
        }
        if (changed) UnityEditor.EditorUtility.SetDirty(this);
#else
        _ = changed;   // (en el juego exportado no se guarda nada: evita el aviso CS0219)
#endif
    }

    private const int ActionsVersion = 16;   // (14: el dash dice «la A»; 15: «la E» y «la Q»; 16: texto del kayak)   // (8: paredes con una sola parada; 9: el dash vuelve a parar en el salto y en el dash; 10: saltos del kayak)   // (3: textos del kayak corregidos; 4: el salto en el kayak; 5: texto del slam; 6: el dash, guiado también el salto hacia un lado)
    [SerializeField, HideInInspector] private int _musicVersion;
    [SerializeField, HideInInspector] private int _actionsVersion;

#if UNITY_EDITOR
    private static GameObject Prefab(string path) => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{path}.prefab");

    private void OnValidate()
    {
        if (Application.isPlaying) return;
        // (OnValidate no puede cargar assets en algunos momentos de la importación: se hace en el siguiente tick)
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) MergeDefaults(); };
    }
#endif

    private static List<CourseObstacle> DefaultObstacles()
    {
#if UNITY_EDITOR
        GameObject P(string n) => Prefab("Estudio/" + n);
#else
        GameObject P(string n) => null;
#endif
        CourseAction A(TutorialKey k, float beat, string es = "", string en = "", int side = 0)
            => new CourseAction { key = k, beat = beat, explainEs = es, explainEn = en, side = side };

        var list = new List<CourseObstacle>
        {
            new CourseObstacle { titleEs = "Salto", titleEn = "Jump", prefab = P("Obs_Salto"),
                actions = new[] { A(TutorialKey.Jump, 0f, "Durante tu trayecto encontrarás obstáculos que puedes saltar, como este.",
                                                          "Along the way you will find obstacles you can jump over, like this one.") },
                tipEs = "Salta justo al llegar al bloque, cuando la nota llegue al centro del monitor del corazón.",
                tipEn = "Jump right as you reach the block, when the note reaches the centre of the heart monitor." },
            new CourseObstacle { titleEs = "Doble salto", titleEn = "Double jump", prefab = P("Obs_DobleSalto"),
                actions = new[] { A(TutorialKey.Jump, 0f),
                                  A(TutorialKey.DoubleJump, 0.5f, "Este obstáculo es más alto: en el aire, vuelve a saltar para hacer un doble salto.",
                                                                  "This obstacle is taller: jump again in the air to double jump.") },
                tipEs = "Salta al llegar y, medio beat después, vuelve a saltar en el aire.",
                tipEn = "Jump as you arrive and jump again in the air half a beat later." },
            new CourseObstacle { titleEs = "Caída en picado", titleEn = "Slam", prefab = P("Obs_Slam"),
                actions = new[] { A(TutorialKey.Jump, 0f),
                                  A(TutorialKey.Slam, 0.5f, "En el aire puedes caer en picado (slam) para bajar al instante.",
                                                             "In the air you can slam down to land instantly.") },
                tipEs = "Salta el bloque y, medio beat después, cae en picado para no darte con el techo.",
                tipEn = "Jump the block and, half a beat later, slam down so you don't hit the ceiling." },
            new CourseObstacle { titleEs = "Deslizarse", titleEn = "Slide", prefab = P("Obs_Deslizar"),
                actions = new[] { A(TutorialKey.Slide, 0f, "Cuando el techo esté demasiado bajo, deslízate por debajo.", "When the ceiling is too low, slide under it.") },
                tipEs = "Deslízate justo al llegar al muro bajo, ya que el deslizamiento dura poco.",
                tipEn = "Slide right as you reach the low wall: the slide is short." },
            new CourseObstacle { titleEs = "Dash", titleEn = "Dash", prefab = P("Obs_Dash"), alternateSides = true,
                // (Dos paradas: el salto hacia el lado y, ya en el aire, el dash.)
                actions = new[] { A(TutorialKey.Jump, 2f, "Para esquivar este muro, primero salta hacia la IZQUIERDA manteniendo pulsada la A.",
                                                          "To dodge this wall, first jump to the LEFT: hold A while you jump.", -1),
                                  A(TutorialKey.Dash, 3f, "Ahora, en el aire, haz un DASH hacia ese mismo lado, que te impulsa de golpe.",
                                                          "Now, in the air, DASH to that side: it pushes you in an instant.", -1) },
                tipEs = "Salta hacia el lado del hueco y, en el beat siguiente, mantén esa dirección y pulsa la E.",
                tipEn = "Jump towards the side of the gap and, on the next beat, hold that direction and press E." },
            new CourseObstacle { titleEs = "Romper puertas", titleEn = "Break doors", prefab = P("Obs_Puerta"),
                actions = new[] { A(TutorialKey.Block, 0f, "Algunas puertas se pueden romper si pulsas la Q justo al llegar a ellas.",
                                                           "Some doors can be broken: press Q just as you reach them.") },
                tipEs = "Pulsa la Q justo al llegar a la puerta, en el beat.",
                tipEn = "Press Q just as you reach the door, on the beat." },
            new CourseObstacle { titleEs = "Tirolina", titleEn = "Zipline", prefab = P("Obs_Tirolina"), segments = 4, reps = 1, inStudy = false,
                actions = new[] { A(TutorialKey.Jump, 0f),
                                  A(TutorialKey.Zipline, 1f, "Engánchate a la tirolina para cruzar.", "Grab the zipline to get across.") },
                tipEs = "Salta al final del suelo y engánchate a la tirolina mientras estás en el aire.",
                tipEn = "Jump at the end of the floor and grab the zipline in the air." },
            new CourseObstacle { titleEs = "Paredes", titleEn = "Walls", prefab = P("Obs_Paredes"), segments = 4, reps = 1, inStudy = false,
                actions = WallActions(),
                tipEs = "Salta hacia la pared y, desde ella, salta en cada beat hacia la de enfrente.",
                tipEn = "Jump towards the wall and, from it, jump on every beat to the opposite one." },
            new CourseObstacle { titleEs = "Kayak", titleEn = "Kayak", prefab = P("Obs_Kayak"), segments = 4, reps = 1, inStudy = false,
                actions = new[] { A(TutorialKey.None, 1f, "En el kayak se rema con el ratón y AL RITMO: el clic IZQUIERDO te lleva a la DERECHA (remas por la izquierda) " +
                                                          "y el DERECHO, a la IZQUIERDA. Los clics a destiempo también te mueven, pero gastan más energía que un fallo normal.",
                                                          "In the kayak you paddle with the mouse, ON THE BEAT: LEFT click to go RIGHT (you paddle on the left) " +
                                                          "and RIGHT click to go LEFT. Off-beat clicks move you too, but they cost more energy than a normal miss.") },
                tipEs = "Rema solo en el beat: el clic izquierdo te lleva a la derecha y el derecho, a la izquierda.",
                tipEn = "Paddle only on the beat: left click to go right and right click to go left." },
            new CourseObstacle { titleEs = "Kayak: saltos", titleEn = "Kayak: jumps", prefab = P("Obs_Kayak_Salto"), segments = 4, reps = 1, inStudy = false,
                // (Un salto cada 4 beats: los obstáculos están a 28,8 m = 4 beats; se salta medio beat antes. El primero
                //  frena y explica.)
                actions = new[] { A(TutorialKey.Jump, 4f, "En el kayak también hay obstáculos: SALTA para pasarlos.",
                                                          "There are obstacles in the kayak too: JUMP to clear them."),
                                  A(TutorialKey.Jump, 8f), A(TutorialKey.Jump, 12f) },
                tipEs = "Salta justo antes de llegar al obstáculo.",
                tipEn = "Jump just before reaching the obstacle." },
            new CourseObstacle { titleEs = "Mirada", titleEn = "Gaze", prefab = P("Obs_Mirada"), segments = 1, reps = 3, inStudy = true,
                actions = new[] { A(TutorialKey.None, 1f, "Algunos interruptores se activan MIRÁNDOLOS: pon el centro de la pantalla sobre ellos y mantén la mirada un momento.",
                                                          "Some switches are activated by LOOKING AT THEM: put the centre of the screen on it and hold it for a moment.") },
                tipEs = "Apunta al interruptor con el centro de la pantalla y mantén la mirada hasta que se active.",
                tipEn = "Aim the centre of the screen at the switch and keep looking until it activates." },
        };
        return list;

        CourseAction[] WallActions()
        {
            var acts = new List<CourseAction>
            {
                // (Una sola parada: el segundo salto ya no frena.)
                // (Saltos hacia la pared de cada lado, intercalando: ESPACIO + A, ESPACIO + D, …)
                A(TutorialKey.Jump, 0f, "Salta hacia la pared para correr por ella y, desde ahí, salta en cada beat para impulsarte hasta la pared de enfrente.",
                                        "Jump towards the wall to run along it and, from the wall, jump on every beat to push off to the opposite one.", -1),
                A(TutorialKey.Jump, 1f, side: 1),
            };
            for (int b = 2; b <= 15; b++) acts.Add(A(TutorialKey.Jump, b, side: b % 2 == 0 ? -1 : 1));   // (un salto de pared en cada beat, a un lado y al otro)
            acts.Add(A(TutorialKey.DoubleJump, 16f));                         // (y un doble salto al salir)
            return acts.ToArray();
        }
    }
}
