using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Events;

/// <summary>
/// Controlador del jefe del infierno. Es una máquina de estados con un planificador de beats basado en el reloj DSP
/// (igual que StairFallingObstacle o WaterSpike, todo sale de BeatManager.ScheduledStartDSP), para que los ataques
/// caigan justo en el compás aunque bajen los FPS.
///
/// Flujo, con los valores de la escena del nivel 3.
///   - Intro. En el beat 164 empiezan la animación de escalada y la subida de la plataforma. En el beat 168, el jefe
///     aparece del todo junto con la barra de vida, y en el beat 172 empieza la fase 1, en el primer beat de un compás.
///   - Fase 1. Se repiten rondas en las que el jefe ataca con el tridente, los puños y los barriles, y al final lanza
///     el proyectil desviable, que es la única forma de hacerle daño.
///   - Cada golpe con éxito le quita 1 punto de vida. Con 3 golpes en una fase, pasa a la siguiente (en el siguiente
///     múltiplo de 4). La vida total es 9, es decir, 3 por fase.
///
/// Este componente no dibuja nada, sino que lanza ataques (BossProjectile y BossTridentSweep) y eventos (UnityEvent)
/// a los que se conectan las animaciones, la barra de vida y el generador de plataformas.
///
/// Configuración.
///   1. Se añade al objeto raíz del jefe.
///   2. Se asignan el prefab del proyectil (con BossProjectile) y el BossTridentSweep del jefe.
///   3. Se asigna el punto de salida de los proyectiles (boca o mano) y se ajustan los carriles X.
///   4. Se conectan los eventos (OnClimbStart a la animación, OnAppear a mostrar la barra, OnHpChanged al relleno de
///      la barra y OnPlatformBeat al generador de plataformas).
/// </summary>
[AddComponentMenu("Boss/Boss Controller")]
public partial class BossController : MonoBehaviour   // la fase 3 está en BossControllerPhase3.cs
{
    public enum BossState { Idle, Intro, Phase1, Phase2, Phase3, Defeated }

    // Subclases concretas para que los eventos con parámetro se vean y se puedan conectar en el Inspector.
    [System.Serializable] public class FloatEvent : UnityEvent<float> { }
    [System.Serializable] public class IntEvent   : UnityEvent<int>   { }

    [Header("Referencias")]
    [Tooltip("BeatManager (se usa BeatManager.Instance si se deja vacío).")]
    [SerializeField] private BeatManager _beatManager;
    [Tooltip("Cuerpo del jefe hacia el que vuelven los proyectiles deflectados. Vacío = este transform.")]
    [SerializeField] private Transform _bossBody;
    [Tooltip("Punto de salida de los proyectiles (boca / mano del jefe).")]
    [SerializeField] private Transform _projectileSpawn;
    [Tooltip("Prefab del proyectil (debe tener un BossProjectile).")]
    [SerializeField] private BossProjectile _projectilePrefab;
    [Tooltip("Componente del barrido de cola.")]
    [FormerlySerializedAs("_tailSweep")]
    [SerializeField] private BossTridentSweep _tridentSweep;
    [Tooltip("Puño DERECHO (Ataque C, estampidos). Va primero.")]
    [SerializeField] private BossFistSlam _rightFist;
    [Tooltip("Puño IZQUIERDO (Ataque C, estampidos).")]
    [SerializeField] private BossFistSlam _leftFist;

    [Header("Movimiento")]
    [Tooltip("El jefe avanza con el jugador manteniendo un lead fijo en el eje de avance (mismo speed).")]
    [SerializeField] private bool _moveForward = true;
    [Tooltip("Distancia (en BEATS) a la que el jefe se mantiene por delante del jugador al arrancar. 4 ≈ 41.14 m.")]
    [SerializeField] private float _leadBeats = 4f;

    [Header("Aparición (escalada desde el vacío)")]
    [Tooltip("Metros que el jefe SUBE desde abajo durante la intro (Intro Start Beat → Appear Beat).")]
    [SerializeField] private float _climbDepth = 150f;

    [Header("Beats de la intro (múltiplos de 4)")]
    [SerializeField] private int _introStartBeat = 164;   // empieza a escalar
    [SerializeField] private int _appearBeat     = 172;   // aparición del jefe y de la barra
    [SerializeField] private int _phase1StartBeat = 176;  // empieza el combate

    [Header("Fase 1 — ataques")]
    [Tooltip("Beats de vuelo del barril = nº de botes contra el suelo hasta llegar al jugador. Más = más telegraph.")]
    [SerializeField] private float _projectileFlightBeats = 4f;
    [Tooltip("Beats de wind-up del barrido de cola antes del contacto.")]
    [SerializeField] private float _tailWindupBeats = 2f;
    [Tooltip("Beats que tarda el proyectil deflectado en volver al jefe.")]
    [SerializeField] private float _deflectReturnBeats = 2f;
    [Tooltip("Incluir el ataque de PUÑOS (Ataque C) en la secuencia.")]
    [SerializeField] private bool _enableFistPhrases = true;
    [Tooltip("Beats de aviso (levantarse) del puño antes de estampar. 1 = contacto al beat siguiente.")]
    [SerializeField] private float _fistWindupBeats = 1f;

    [Header("Carriles (X del mundo)")]
    [SerializeField] private float[] _laneX = { -13f, 0f, 13f };

    [Header("Daño")]
    [SerializeField] private int _maxHp = 9;
    [SerializeField] private int _hitsPerPhase = 3;
    [Tooltip("Golpe del jefe: con sangre AZUL la quita toda; sin azul, llena esta parte de la jeringuilla roja\n" +
             "(0.5 = media jeringuilla → dos golpes seguidos sin azul = muerte).")]
    [Range(0.05f, 1f)]
    [SerializeField] private float _bossHitRedFraction = 0.5f;
    [Tooltip("Segundos de invulnerabilidad tras recibir un golpe fuerte (para que un mismo ataque no te pegue varias veces).")]
    [SerializeField] private float _hitInvulnerability = 1f;

    [Header("Vidas extra (una por fase)")]
    [Tooltip("Color de los objetos que dan VIDA EXTRA: barril de la F1, bola de la F2 y orbe de la F3.")]
    [SerializeField] private Color _lifeColor = new Color(0.25f, 1f, 0.45f, 1f);
    [Tooltip("Segundos mirando el orbe de vida de la Fase 3 para cogerlo.")]
    [SerializeField] private float _lifeOrbGazeSeconds = 0.6f;
    private bool _p1LifeSpawned, _p2LifeSpawned, _p3LifeSpawned;
    private int  _p2Volleys;

    /// <summary>El jugador consigue una vida extra (con el barril, la bola o el orbe de vida).</summary>
    public void GiveExtraLife(string how)
    {
        PlayerProgress.AddCount(ShopIds.ExtraLife, 1);
        ReviveFX.ShowMessage(Loc.T("VIDA EXTRA GANADA", "EXTRA LIFE GAINED"), how, new Color(0.2f, 0.9f, 0.35f, 0.4f));
        if (_verbose) Debug.Log($"[Boss] +1 vida extra ({how}). Ahora: {PlayerProgress.GetCount(ShopIds.ExtraLife)}.");
    }

    [Header("Deflect (colores del proyectil especial)")]
    [SerializeField] private Color _normalColor  = new Color(0.5f, 0.12f, 0.02f, 1f);
    [SerializeField] private Color _deflectColor = new Color(1f, 0.85f, 0.2f, 1f);

    [Header("Fase 2 — referencias")]
    [Tooltip("Prefab del pilar infernal (con BossPillar).")]
    [SerializeField] private BossPillar _pillarPrefab;
    [Tooltip("Prefab de la bola de fuego teledirigida (con BossHomingFireball).")]
    [SerializeField] private BossHomingFireball _fireballPrefab;
    [Tooltip("Punto de salida de las bolas de fuego (boca del jefe). Vacío = Projectile Spawn.")]
    [SerializeField] private Transform _fireballSpawn;
    [Tooltip("Cabeza del jefe: sale volando al punto de pisado; hay que saltar+Slam encima.")]
    [SerializeField] private Transform _head;

    [Header("Fase 2 — ajustes")]
    [Tooltip("Escala Y del pilar CORTO (para saltar por encima).")]
    [SerializeField] private float _pillarShortScaleY = 0.5f;
    [Tooltip("Escala Y del pilar ALTO (bloquea el carril, hay que moverse).")]
    [SerializeField] private float _pillarTallScaleY = 2.5f;
    [Tooltip("Beats por delante del jugador a los que emerge el pilar (telegraph antes de llegar).")]
    [SerializeField] private float _pillarLeadBeats = 3f;
    [Tooltip("Beats que persigue la bola de fuego antes de ir recta (recta 1 beat antes del impacto).")]
    [SerializeField] private float _homingBeats = 3f;
    [Tooltip("Beats de la ventana vulnerable (cabeza abajo para el Slam).")]
    [SerializeField] private float _vulnBeats = 4f;
    [Tooltip("Beats en recuperarse (la cabeza vuelve al jefe) tras la ventana.")]
    [SerializeField] private float _recoverBeats = 2f;
    [Tooltip("Offset de altura sobre el SUELO para la cabeza en el punto de pisado (0 = a ras de suelo).")]
    [SerializeField] private float _headGroundOffset = 0f;
    [Tooltip("Radio horizontal para aceptar el Slam sobre la cabeza.")]
    [SerializeField] private float _headHitRadius = 2.5f;

    [Header("Fase 2 — secuencia y spike/cabeza")]
    [Tooltip("Beats que tarda el spike en subir hasta la barbilla.")]
    [SerializeField] private float _spikeRiseBeats = 1f;
    [Tooltip("Cuánto se adelanta el spike (hacia el jugador) además de la media profundidad de la cabeza, para\n" +
             "que suba por delante del cuerpo, bajo la barbilla, y se vea.")]
    [SerializeField] private float _spikeChinForward = 1.5f;
    [Tooltip("PREFAB del spike (tu modelo). El pivote da igual: se mide solo y se apoya su base en la plataforma,\n" +
             "escalado para llegar justo a la barbilla. Vacío = cono generado por código.")]
    [SerializeField] private GameObject _headSpikePrefab;
    [Tooltip("Altura del modelo a escala 1, SOLO si no se puede medir automáticamente (0 = medir solo).")]
    [SerializeField] private float _spikeModelHeight = 0f;
    [Tooltip("true = escala uniforme (conserva proporciones del modelo). false = solo se estira en altura.")]
    [SerializeField] private bool _spikeUniformScale = false;
    [Tooltip("Radio de la base del spike (solo el cono generado por código). 0 = automático según la cabeza.")]
    [SerializeField] private float _spikeBaseRadius = 0f;
    [Tooltip("Color del spike (se ve igual en cualquier pipeline de render).")]
    [SerializeField] private Color _spikeColor = new Color(1f, 0.3f, 0.08f, 1f);
    [Tooltip("Beats que el spike se queda arriba tras golpear la cabeza antes de hundirse.")]
    [SerializeField] private float _spikeHoldBeats = 0.75f;
    [Tooltip("Beats que tarda la cabeza en salir volando hasta el punto de pisado.")]
    [SerializeField] private float _headFlyBeats = 1f;
    [Tooltip("Altura del arco (pico) del vuelo de la cabeza.")]
    [SerializeField] private float _headFlyArcHeight = 8f;
    [Tooltip("Velocidad hacia arriba con la que REBOTA EL JUGADOR al pisar la cabeza con el Slam.")]
    [SerializeField] private float _playerBounceForce = 22f;
    [Tooltip("Distancia (metros) por delante del jefe HACIA el jugador a la que estampan los puños en el\n" +
             "ataque de ondas (separados del cuerpo para que se vean). Valor positivo.")]
    [SerializeField] private float _fistWaveFront = 14f;
    [Tooltip("Cuánto se acercan los puños al CENTRO (X del jefe) al estampar en el ataque de ondas.\n" +
             "0 = en su propia X, 1 = los dos en el centro. Nunca se cruzan.")]
    [SerializeField, Range(0f, 0.95f)] private float _fistWaveCenterPull = 0.5f;

    [Header("Fase 2 — bolas de fuego / ondas / obstáculos")]
    [Tooltip("Separación lateral (m) entre la primera y la última bola de una ráfaga (desde la boca).")]
    [SerializeField] private float _fireballSpread = 6f;
    [Tooltip("Incluir el ataque de ONDAS (los dos puños estampando + ondas que se saltan) en la secuencia.")]
    [SerializeField] private bool _enableShockwaveAttack = true;
    [Tooltip("Dispersión en Z (metros) de los pilares de una misma ráfaga. Pequeña, para que no se generen\n" +
             "tan lejos que se despawneen antes de que llegue el jugador.")]
    [SerializeField] private float _obstacleDepthSpread = 10f;
    [Tooltip("X mínima donde pueden salir los pilares (mundo). Rango libre, no carriles fijos.")]
    [SerializeField] private float _obstacleMinX = -20f;
    [Tooltip("X máxima donde pueden salir los pilares (mundo).")]
    [SerializeField] private float _obstacleMaxX = 20f;

    // -=+=- Dificultad por ronda -=+=-
    // Se fija al empezar cada ronda según el daño hecho en la fase actual. Con 0 golpes es fácil [0], con 1 golpe es
    // media [1] y con 2 o más, difícil [2]. Al cambiar de fase, vuelve a fácil.

    // Los intervalos van en beats, por lo que 2 es un ataque cada 2 beats, 1 es uno por beat y 0,5, uno cada medio beat.

    // Frecuencias máximas de la fase 1, que se aplican siempre, independientemente de lo que se ponga en el Inspector.
    private const int   MinTridentEveryBeats = 2;   // tridente: como máximo un barrido cada 2 beats
    private const float MinFistInterval      = 1f;  // puños: como máximo un golpe por beat
    private const float MinBarrelInterval    = 0.5f;// barriles: hasta un lanzamiento cada medio beat

    [System.Serializable]
    public class Phase1Tier
    {
        [Tooltip("Barridos de tridente por ronda (1-8).")]                                     [Range(1, 8)] public int tridentSweeps = 2;
        [Tooltip("Beats ENTEROS entre barridos de tridente (mín. 2). Cae siempre en beat completo.")]
        [Range(2, 4)] public int tridentEveryBeats = 2;
        [Tooltip("Estampidos de puño por ronda (0-12).")]                                      [Range(0, 12)] public int fistSlams = 4;
        [Tooltip("Beats entre estampidos de puño (mín. 1 = uno por beat; alternan der/izq).")] [Range(1f, 4f)] public float fistInterval = 2f;
        [Tooltip("Lanzamientos de barriles antes del deflectable (0-16).")]                    [Range(0, 16)] public int barrels = 2;
        [Tooltip("Beats entre lanzamientos de barriles (0.5 = cada medio beat).")]             [Range(0.5f, 2f)] public float barrelInterval = 1f;
        [Tooltip("Barriles por lanzamiento, en carriles distintos (siempre queda uno libre).")] [Range(1, 2)] public int barrelsPerThrow = 1;
        public Phase1Tier() { }
        public Phase1Tier(int sweeps, int sweepEvery, int fists, float fistInt, int barrels, float barrelInt, int perThrow)
        {
            tridentSweeps = sweeps; tridentEveryBeats = sweepEvery; fistSlams = fists; fistInterval = fistInt;
            this.barrels = barrels; barrelInterval = barrelInt; barrelsPerThrow = perThrow;
        }
    }

    [System.Serializable]
    public class Phase2Tier
    {
        [Tooltip("Bolas de fuego por ráfaga (mín/máx).")]                  public int fireballsMin = 2, fireballsMax = 3;
        [Tooltip("Beats entre ráfagas de bolas de fuego.")]                [Range(0.25f, 4f)] public float fireballInterval = 1f;
        [Tooltip("Beats entre estampidos de puño con onda.")]              [Range(0.5f, 4f)] public float waveInterval = 1f;
        [Tooltip("Multiplicador de velocidad de las ondas.")]              public float waveSpeedMul = 1f;
        [Tooltip("Probabilidad de que los puños estampen A LA VEZ.")]      [Range(0f, 1f)] public float bothTogetherChance = 0.5f;
        [Tooltip("Pilares por ráfaga (mín/máx).")]                         public int obstaclesMin = 3, obstaclesMax = 5;
        [Tooltip("Beats entre ráfagas de pilares (1 = cada beat, 0.5 = cada medio beat).")] [Range(0.25f, 2f)] public float obstacleInterval = 1f;
        public Phase2Tier() { }
        public Phase2Tier(int fbMin, int fbMax, float fbInt, float waveInt, float waveMul, float both,
                          int obMin, int obMax, float obInt)
        {
            fireballsMin = fbMin; fireballsMax = fbMax; fireballInterval = fbInt;
            waveInterval = waveInt; waveSpeedMul = waveMul; bothTogetherChance = both;
            obstaclesMin = obMin; obstaclesMax = obMax; obstacleInterval = obInt;
        }
    }

    [Header("Dificultad por ronda — [0] Fácil · [1] Media · [2] Difícil")]
    [SerializeField] private Phase1Tier[] _phase1DifficultyTable =
    {
        //             tridente (número, cada) · puños (número, cada) · barriles (número de lanzamientos, cada, por lanzamiento)
        new Phase1Tier(2, 3,                4, 2f,             4,  1f,   1),   // fácil
        new Phase1Tier(4, 2,                6, 1f,             6,  0.5f, 1),   // media
        new Phase1Tier(6, 2,                8, 1f,             10, 0.5f, 2),   // difícil (máximos: tridente cada 2 beats, puño cada beat)
    };
    [SerializeField] private Phase2Tier[] _phase2DifficultyTable =
    {
        //             fuego (mín., máx., cada) · ondas (cada, velocidad ×, a la vez) · pilares (mín., máx., cada)
        new Phase2Tier(2, 3, 1f,              2f,   1.10f, 0.4f,             2, 3, 1f),     // fácil
        new Phase2Tier(3, 5, 0.75f,           1f,   1.30f, 0.7f,             3, 4, 1f),     // media
        new Phase2Tier(4, 6, 0.5f,            0.5f, 1.55f, 1.0f,             3, 5, 0.5f),   // difícil
    };
    public IntEvent OnDifficultyChanged;   // 0 fácil, 1 media y 2 difícil (para la interfaz o la música)

    [Header("Eventos")]
    public UnityEvent OnClimbStart;              // beat 164
    public UnityEvent OnAppear;                  // beat 168 en la escena (se muestra la barra de vida)
    public UnityEvent OnPhase1Start;             // beat 172 en la escena
    public IntEvent OnPhaseChanged;              // número de la nueva fase (2, 3...)
    public FloatEvent OnHpChanged;               // de 0 a 1, para el relleno de la barra
    public UnityEvent OnBossHit;                 // reacción al recibir daño
    public UnityEvent OnFaceplant;               // fase 2: cae de bruces (con la cabeza vulnerable)
    public UnityEvent OnRecover;                 // fase 2: se recupera (con la cabeza arriba)
    public UnityEvent OnDefeated;
    public UnityEvent OnPlatformBeat;            // cada 4 beats sube una plataforma nueva

    public enum DebugStartPhase { Normal, Phase1, Phase2, Phase3 }

    [Header("Debug")]
    [Tooltip("Arranca directamente en una fase para testear. Normal = intro + Fase 1 como siempre.")]
    [SerializeField] private DebugStartPhase _debugStartPhase = DebugStartPhase.Normal;
    [Tooltip("Desactiva el jefe por completo (para probar el nivel sin él).")]
    [SerializeField] private bool _disabled = false;
    [Tooltip("TEST del modo Normal: si te teletransportas a la arena (LevelStartManager), activa esto para que\n" +
             "la intro (escalada + Fase 1) empiece nada más darle a Play, sin esperar al beat 164. Déjalo en OFF\n" +
             "para jugar el nivel entero desde el principio.")]
    [SerializeField] private bool _autoStartOnPlay = false;
    [Tooltip("TEST: fuerza la dificultad de TODAS las rondas. -1 = automática (según el daño hecho en la fase),\n" +
             "0 = fácil, 1 = media, 2 = difícil.")]
    [SerializeField, Range(-1, 2)] private int _debugForceDifficulty = -1;
    [SerializeField] private bool _verbose = true;

    // -=+=- Estado -=+=-
    private BossState _state = BossState.Idle;
    private bool  _ready;
    private int   _lastBeat = int.MinValue;
    private int   _phase1Loop;           // número de frase reproducida
    private int   _phase1Origin;         // beat en el que empezó la fase 1 (para el planificador y la depuración)
    private bool  _tailFromRightNext = true;  // alterna el lado del barrido (derecha o izquierda)
    private int   _lastLane = -1;        // último carril usado, para no repetirlo
    private int   _hp;
    private int   _hitsThisPhase;
    private int   _pendingPhase = -1;    // fase a la que cambiar
    private int   _phaseChangeBeat = -1; // beat (múltiplo de 4) en el que cambiar
    private PlayerMovement _pm;
    private GameOverManager _gameOver;
    private float _followForwardDist;    // distancia fija del jefe por delante del jugador
    private bool  _followInit;
    private float _homeY;                 // altura de reposo del jefe (el destino de la escalada)

    // Fase 2
    private enum Phase2Sub { Attacking, SpikeRising, HeadFlying, Vulnerable, Recovering }
    private Phase2Sub _p2sub = Phase2Sub.Attacking;
    private int       _phase2StartBeat;
    private double    _vulnEndDSP, _recoverEndDSP;
    private double    _spikeStartDSP, _spikeEndDSP;
    private double    _headFlyStartDSP, _headFlyEndDSP;
    private Vector3   _headFlyFromWorld;
    private Vector3   _headRestLocalPos;
    private Quaternion _headRestLocalRot;
    private bool      _headCaptured;
    // Pincho. Raíz con el pivote en la base (_spikeObj) y un modelo hijo (_spikeModel, el prefab o el cono).
    private Transform _spikeObj;
    private Transform _spikeModel;
    private bool      _spikeMeasured;
    private float     _spikeH;               // altura del pincho (del suelo a la cabeza)
    private float     _spikeGroundY;         // superficie bajo la barbilla
    private CharacterController _playerCC;
    private float     _lastGroundY;   // superficie del suelo (los pies del jugador cuando toca el suelo)

    // -=+=- Valores que leen los ataques -=+=-
    public Transform PlayerT { get; private set; }
    public Transform BossT => _bossBody != null ? _bossBody : transform;
    public float BeatDuration => BeatManager.BeatDurationOf(_beatManager);
    public float DeflectReturnBeats => _deflectReturnBeats;
    public Color NormalColor => _normalColor;
    public Color DeflectColor => _deflectColor;
    public Color LifeColor    => _lifeColor;

    // -=+=- Dificultad de la ronda actual -=+=-
    private int _roundDifficulty;   // 0 fácil, 1 media y 2 difícil (se fija al empezar cada ronda)


    /// <summary>Multiplicador de la velocidad de las ondas en la ronda actual. Lo lee BossShockwave.</summary>
    public float WaveSpeedMultiplier => P2Tier.waveSpeedMul;

    private Phase1Tier P1Tier => (_phase1DifficultyTable != null && _phase1DifficultyTable.Length > 0)
        ? _phase1DifficultyTable[Mathf.Clamp(_roundDifficulty, 0, _phase1DifficultyTable.Length - 1)]
        : new Phase1Tier(2, 3, 4, 2f, 4, 1f, 1);
    private Phase2Tier P2Tier => (_phase2DifficultyTable != null && _phase2DifficultyTable.Length > 0)
        ? _phase2DifficultyTable[Mathf.Clamp(_roundDifficulty, 0, _phase2DifficultyTable.Length - 1)]
        : new Phase2Tier(2, 3, 1f, 1f, 1f, 0.5f, 3, 5, 1f);

    // -=+=- Planificador de ataques por DSP, que permite lanzarlos a medio beat, a un cuarto de beat, etc. -=+=-
    private readonly System.Collections.Generic.List<ScheduledAttack> _schedule =
        new System.Collections.Generic.List<ScheduledAttack>();
    private struct ScheduledAttack { public double dsp; public System.Action act; }

    /// <summary>Programa una acción para el instante DSP indicado. Se ejecuta en el Update de ese fotograma.</summary>
    private void Schedule(double dsp, System.Action act) => _schedule.Add(new ScheduledAttack { dsp = dsp, act = act });

    /// <summary>Instante DSP de un beat (con decimales) respecto a un beat absoluto de referencia.</summary>
    private double DspAt(int refBeat, float relBeats) => DspForBeat(refBeat + (double)relBeats);

    /// <summary>Ejecuta, en orden, las acciones programadas cuyo momento ya ha llegado.</summary>
    private void RunSchedule()
    {
        if (_schedule.Count == 0) return;
        double now = BeatManager.DspNow;   // el mismo instante actual que el resto del juego
        var due = new System.Collections.Generic.List<ScheduledAttack>();
        for (int i = _schedule.Count - 1; i >= 0; i--)
            if (_schedule[i].dsp <= now) { due.Add(_schedule[i]); _schedule.RemoveAt(i); }
        due.Sort((a, b) => a.dsp.CompareTo(b.dsp));
        foreach (var s in due) s.act?.Invoke();
    }

    /// <summary>Al empezar una ronda, la dificultad depende de los golpes hechos en esta fase (0 fácil, 1 media y 2 o más difícil).</summary>
    private void LockRoundDifficulty()
    {
        int d = _debugForceDifficulty >= 0 ? Mathf.Clamp(_debugForceDifficulty, 0, 2)   // prueba
                                           : Mathf.Clamp(_hitsThisPhase, 0, 2);          // automática
        if (d != _roundDifficulty) OnDifficultyChanged?.Invoke(d);
        _roundDifficulty = d;
        if (_verbose)
        {
            string[] names = { "FÁCIL", "MEDIA", "DIFÍCIL" };
            Debug.Log($"[Boss] Nueva ronda (fase {CurrentPhaseNumber()}): dificultad {names[d]} " +
                      (_debugForceDifficulty >= 0 ? "(FORZADA por debug)." : $"({_hitsThisPhase} golpes en esta fase)."));
        }
    }

    public double DspForBeat(double beat) =>
        (_beatManager != null ? _beatManager.DspAtBeat(beat) : beat * BeatDuration);

    // -=+=- Ciclo de vida -=+=-

    private void Awake()
    {
        _hp = _maxHp;
        InitPhase3GazeSwitches();   // BossControllerPhase3.cs: apagados hasta que caiga el escudo
    }

    private void Start()
    {
        if (_beatManager == null) _beatManager = BeatManager.Instance;
        _pm = FindAnyObjectByType<PlayerMovement>();
        if (_pm != null) PlayerT = _pm.transform;
        _gameOver = FindAnyObjectByType<GameOverManager>();
        if (_pm != null) _playerCC = _pm.GetComponent<CharacterController>();
        if (_playerCC != null) _lastGroundY = _playerCC.bounds.min.y;
        _homeY = transform.position.y;   // altura final a la que sube tras la escalada
        if (_head != null)
        {
            _headRestLocalPos = _head.localPosition;
            _headRestLocalRot = _head.localRotation;
            _headCaptured = true;
        }
        OnHpChanged?.Invoke(1f);
        if (!_disabled) InitVictory();   // BossControllerPhase3.cs: el final queda escondido y el fin del nivel, bloqueado
    }

    private void Update()
    {
        if (_disabled || _beatManager == null) return;

        // Se espera a que la música esté programada, igual que los obstáculos.
        if (!_ready)
        {
            if (!_beatManager.HasClock) return;
            _lastBeat = CurrentBeat() - 1;   // no se vuelven a lanzar los beats que ya han pasado
            _ready = true;

            // Prueba del modo normal. La intro se mueve al instante actual para no esperar al beat 164.
            if (_autoStartOnPlay && _debugStartPhase == DebugStartPhase.Normal)
            {
                int b0    = CurrentBeat();
                int start = ((b0 / 4) + 1) * 4;                   // siguiente múltiplo de 4
                int introSpan  = Mathf.Max(1, _appearBeat - _introStartBeat);
                int appearSpan = Mathf.Max(0, _phase1StartBeat - _appearBeat);
                _introStartBeat  = start;
                _appearBeat      = start + introSpan;
                _phase1StartBeat = _appearBeat + appearSpan;
                OnPlatformBeat?.Invoke();   // el suelo ya está bajo el jugador mientras el jefe escala
                if (_verbose) Debug.Log($"[Boss] Auto-start: intro rebaseada a beat {start} (aparece {_appearBeat}, Fase 1 {_phase1StartBeat}).");
            }
        }

        // Cada beat entero se lanza exactamente una vez, según el reloj DSP.
        int beat = CurrentBeat();
        while (_lastBeat < beat)
        {
            _lastBeat++;
            OnBeat(_lastBeat);
        }

        RunSchedule();   // ataques programados a mitad de beat (con la frecuencia según la dificultad)

        FollowPlayer();
        ApplyClimb();
        UpdatePhase2();
        UpdatePhase3();
        UpdateVictory();

        // Las mejoras de la tienda sí funcionan en la pelea, y solo el Rebobinado está bloqueado (ver TimeRewinder).
    }

    /// <summary>El jefe avanza con el jugador. Se coloca _leadBeats beats por delante y mantiene esa distancia.</summary>
    private void FollowPlayer()
    {
        if (!_moveForward || PlayerT == null) return;
        if (_state == BossState.Idle || _state == BossState.Defeated) return;

        Vector3 fwd = _pm != null ? _pm.RunForward : Vector3.forward;
        float cur = Vector3.Dot(transform.position - PlayerT.position, fwd);
        if (!_followInit)
        {
            // Empieza exactamente _leadBeats beats por delante del jugador (4 beats son unos 41,14 m).
            float walk = _pm != null ? _pm.walkSpeed : 12f;
            _followForwardDist = _leadBeats * BeatDuration * walk;
            _followInit = true;
        }
        transform.position += fwd * (_followForwardDist - cur);
    }

    /// <summary>Escalada de la intro. El jefe sube desde -climbDepth hasta su altura entre Intro y Appear.</summary>
    private void ApplyClimb()
    {
        if (!_ready) return;
        Vector3 p = transform.position;
        p.y = _homeY + ClimbOffset() - _p3SinkOffset;   // en la fase 3 se hunde en la lava
        transform.position = p;
    }

    private float ClimbOffset()
    {
        if (_debugStartPhase != DebugStartPhase.Normal) return 0f;   // depuración: el jefe ya está arriba
        double beatExact = _beatManager.ClockBeats;
        if (beatExact <= _introStartBeat) return -_climbDepth;   // escondido bajo el suelo
        if (beatExact >= _appearBeat)     return 0f;             // ya en su sitio
        float t = (float)((beatExact - _introStartBeat) / Mathf.Max(1, _appearBeat - _introStartBeat));
        float eased = 1f - (1f - t) * (1f - t);                  // sale y se asienta frenando al final
        return Mathf.Lerp(-_climbDepth, 0f, eased);
    }

    private int CurrentBeat()
    {
        double b = _beatManager.ClockBeats;
        return Mathf.FloorToInt((float)b);
    }

    // -=+=- Planificador: qué pasa en cada beat entero -=+=-

    private void OnBeat(int beat)
    {
        // Una plataforma nueva cada 4 beats, desde la intro (o desde el principio en el modo de depuración).
        if ((_debugStartPhase != DebugStartPhase.Normal || beat >= _introStartBeat) && (beat % 4 == 0))
            OnPlatformBeat?.Invoke();

        // Cambio de fase pendiente, siempre en un múltiplo de 4.
        if (_pendingPhase > 0 && beat >= _phaseChangeBeat)
        {
            EnterPhase(_pendingPhase);
            _pendingPhase = -1;
        }

        switch (_state)
        {
            case BossState.Idle:
                if (_debugStartPhase != DebugStartPhase.Normal)
                {
                    if (!_appeared) DoAppear();
                    switch (_debugStartPhase)
                    {
                        case DebugStartPhase.Phase1: EnterPhase(1); Phase1Beat(beat); break;
                        case DebugStartPhase.Phase2: EnterPhase(2); Phase2Beat(beat); break;   // lanza ya el beat relativo 0 (bolas de fuego)
                        case DebugStartPhase.Phase3: EnterPhase(3); break;
                    }
                }
                else if (beat >= _introStartBeat) EnterIntro();
                break;

            case BossState.Intro:
                if (beat >= _appearBeat && !_appeared) DoAppear();
                if (beat >= _phase1StartBeat) { EnterPhase(1); Phase1Beat(beat); }  // lanza ya el beat relativo 0
                break;

            case BossState.Phase1:
                Phase1Beat(beat);
                break;

            case BossState.Phase2:
                Phase2Beat(beat);
                break;
        }
    }

    // -=+=- Intro -=+=-

    private bool _appeared;

    /// <summary>Indica si la pelea está en marcha, es decir, si el jefe ha aparecido y no ha sido derrotado.</summary>
    public bool FightInProgress => _appeared && _state != BossState.Defeated;

    private void EnterIntro()
    {
        _state = BossState.Intro;
        if (_verbose) Debug.Log($"[Boss] Intro (beat {_introStartBeat}): empieza a escalar.");
        OnClimbStart?.Invoke();
    }

    private void DoAppear()
    {
        _appeared = true;
        if (_verbose) Debug.Log($"[Boss] Aparición (beat {_appearBeat}): barra de vida.");
        OnAppear?.Invoke();
    }

    // -=+=- Cambio de fase -=+=-

    private void EnterPhase(int phase)
    {
        _hitsThisPhase = 0;
        _roundDifficulty = 0;   // cada fase empieza en fácil
        _schedule.Clear();      // no queda nada pendiente de la fase anterior

        switch (phase)
        {
            case 1:
                _state = BossState.Phase1;
                _phase1Loop = 0;
                _phase1Origin = CurrentBeat();
                _p1RoundLen = 0;   // la primera ronda se planifica en el primer beat
                if (_verbose) Debug.Log($"[Boss] FASE 1 (beat {_phase1Origin}).");
                OnPhase1Start?.Invoke();
                break;

            case 2:
                _state = BossState.Phase2;
                _phase2StartBeat = CurrentBeat();
                _p2sub = Phase2Sub.Attacking;
                _p2NewRoundPending = false;
                if (_verbose) Debug.Log($"[Boss] FASE 2 (beat {_phase2StartBeat}).");
                OnPhaseChanged?.Invoke(2);
                break;

            case 3:
                _state = BossState.Phase3;
                OnPhaseChanged?.Invoke(3);
                EnterPhase3();   // BossControllerPhase3.cs: derrumbe, kayak, escudo, BloodSuckers y mirada
                break;
        }
    }

    // -=+=- Fase 1: rondas de duración variable, según la dificultad -=+=-
    // Cada ronda se planifica al empezar, con el tridente, los puños, los barriles y el proyectil desviable, uno
    // detrás de otro. La duración depende de cuántos ataques hay, y se redondea a un múltiplo de 4 para que cuadre con
    // el compás. Por ejemplo, con (tridente, puños, barriles) = (2, 4, 2) son 16 beats, con (4, 5, 3) son 20 y con
    // (6, 6, 4) son 28.

    private int _p1RoundStart, _p1RoundLen;   // inicio (beat absoluto) y duración de la ronda

    private void Phase1Beat(int beat)
    {
        if (beat < _phase1Origin) return;

        // Si toca una ronda nueva (la primera, o si se ha acabado la anterior), se planifica entera de una vez.
        if (_p1RoundLen <= 0 || beat - _p1RoundStart >= _p1RoundLen)
        {
            if (_p1RoundLen > 0) _phase1Loop++;
            StartPhase1Round(beat);
        }
    }

    /// <summary>
    /// Fija la dificultad de la ronda y programa cada ataque en su instante exacto (en beats con decimales, para poder
    /// atacar a medio beat). El orden es tridente, puños, barriles y proyectil desviable (la ventana de daño). La
    /// frecuencia de cada ataque sale de la tabla (tridentEveryBeats y fistInterval), y la ronda dura lo necesario,
    /// redondeado a un múltiplo de 4 para que cuadre con el compás.
    /// </summary>
    private void StartPhase1Round(int beat)
    {
        LockRoundDifficulty();   // dificultad según el daño hecho en esta fase
        Phase1Tier t = P1Tier;
        _p1RoundStart = beat;

        // 1) Tridente, siempre en beats completos, de forma que el contacto de cada barrido cae en un beat entero. El
        //    primero tiene su preparación normal, y los siguientes se encadenan cada 'tridentEveryBeats' beats (el barrido
        //    dura 1 beat, así que cada uno empieza al acabar el anterior, desde el lado en el que se quedó).
        int   nTail     = Mathf.Clamp(t.tridentSweeps, 1, 8);
        float tailInt   = Mathf.Clamp(t.tridentEveryBeats, MinTridentEveryBeats, 4);   // entero, como mínimo cada 2 beats
        float firstHit  = Mathf.Max(1, Mathf.RoundToInt(_tailWindupBeats));   // entero
        // Para que siempre haya al menos medio beat de aviso entre barridos, si van muy seguidos el barrido se hace más
        // rápido (con 1 beat, el barrido dura 0,5 beats y el aviso, otros 0,5). El contacto sigue cayendo en un beat
        // completo, ya que el barrido está centrado en él.
        float sweepDur = Mathf.Clamp(tailInt - 0.5f, 0.5f, 1f);
        for (int i = 0; i < nTail; i++)
        {
            float contact = firstHit + i * tailInt;
            float prevEnd = contact - tailInt + sweepDur * 0.5f;          // fin del barrido anterior
            float call    = i == 0 ? 0f : prevEnd;
            double contactDSP = DspAt(beat, contact);
            Schedule(DspAt(beat, call), () => { if (_state == BossState.Phase1) TriggerTailAt(contactDSP, sweepDur); });
        }
        float cursor = firstHit + (nTail - 1) * tailInt + 1f;          // fin del último barrido más un margen

        // 2) Puños, cada 'fistInterval' beats, alternando derecha e izquierda. El aviso (al levantarse) nunca dura más que
        //    el intervalo, para que a cada puño le dé tiempo a caer antes de volver a lanzarse.
        int   nFist   = _enableFistPhrases ? Mathf.Clamp(t.fistSlams, 0, 12) : 0;
        float fistInt = Mathf.Max(MinFistInterval, t.fistInterval);   // máximo: un golpe por beat
        float fistWu  = Mathf.Clamp(fistInt, 0.5f, Mathf.Max(0.5f, _fistWindupBeats));
        float fistStart = Mathf.Ceil(cursor);
        for (int j = 0; j < nFist; j++)
        {
            float call  = fistStart + j * fistInt;
            bool  right = (j % 2) == 0;
            double contactDSP = DspAt(beat, call + fistWu);
            Schedule(DspAt(beat, call), () => { if (_state == BossState.Phase1) TriggerFistAt(right, contactDSP); });
        }
        if (nFist > 0) cursor = fistStart + (nFist - 1) * fistInt + fistWu;

        // 3) Barriles, con 'barrels' lanzamientos cada 'barrelInterval' beats (hasta medio beat), de 1 o 2 barriles en
        //    carriles distintos, de forma que siempre queda un carril libre. Al final llega el proyectil desviable (la
        //    ventana de daño).
        int   nBarrel   = Mathf.Clamp(t.barrels, 0, 16);
        float barrelInt = Mathf.Max(MinBarrelInterval, t.barrelInterval);
        int   perThrow  = Mathf.Clamp(t.barrelsPerThrow, 1, Mathf.Max(1, (_laneX != null ? _laneX.Length : 3) - 1));
        float barrelStart = Mathf.Ceil(cursor);
        // Una vez en la fase 1, en uno de los lanzamientos sale además el barril de vida (verde), en un carril libre.
        int lifeThrow = !_p1LifeSpawned && nBarrel > 0 ? Random.Range(0, nBarrel) : -1;
        for (int k = 0; k < nBarrel; k++)
        {
            double spawnDSP = DspAt(beat, barrelStart + k * barrelInt);
            bool withLife = k == lifeThrow;
            Schedule(spawnDSP, () => { if (_state == BossState.Phase1) ThrowBarrels(perThrow, spawnDSP, withLife); });
        }
        float lastThrow = barrelStart + Mathf.Max(0, nBarrel - 1) * barrelInt;
        int deflect = nBarrel > 0 ? Mathf.CeilToInt(lastThrow) + 1 : Mathf.CeilToInt(barrelStart);
        double deflectDSP = DspAt(beat, deflect);
        Schedule(deflectDSP, () => { if (_state == BossState.Phase1) SpawnProjectile(1, true, deflectDSP); });   // daño

        // Dura hasta que el proyectil desviable llega al jugador (más 3 beats), redondeado a un múltiplo de 4.
        int needed  = deflect + 3;
        _p1RoundLen = Mathf.Max(4, ((needed + 3) / 4) * 4);

        if (_verbose) Debug.Log($"[Boss] Ronda F1: {nTail} tridentes (cada {tailInt} b), {nFist} puños (cada {fistInt} b), " +
                                $"{nBarrel}×{perThrow} barriles (cada {barrelInt} b) → {_p1RoundLen} beats.");
    }

    /// <summary>Lanza 'count' barriles a la vez en carriles distintos al azar, sin ocuparlos todos, para que siempre quede uno libre.</summary>
    private void ThrowBarrels(int count, double spawnDSP, bool withLife = false)
    {
        int lanes = _laneX != null ? _laneX.Length : 0;
        if (lanes == 0) return;
        count = Mathf.Clamp(count, 1, Mathf.Max(1, lanes - 1));
        var used = new System.Collections.Generic.List<int>();
        if (count == 1)
        {
            int lane = RandomLaneExcept(_lastLane);
            SpawnProjectile(lane, false, spawnDSP);
            used.Add(lane);
        }
        else
        {
            // Se barajan los carriles y se cogen los primeros 'count'.
            int[] order = new int[lanes];
            for (int i = 0; i < lanes; i++) order[i] = i;
            for (int i = lanes - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
            for (int i = 0; i < count; i++) { SpawnProjectile(order[i], false, spawnDSP); used.Add(order[i]); }
        }

        // Barril de vida (una vez en la fase 1), en un carril libre. Si el jugador lo devuelve con Q, consigue la vida.
        if (withLife && !_p1LifeSpawned)
        {
            var free = new System.Collections.Generic.List<int>();
            for (int i = 0; i < lanes; i++) if (!used.Contains(i)) free.Add(i);
            if (free.Count > 0)
            {
                _p1LifeSpawned = true;
                int keepLast = _lastLane;
                SpawnProjectile(free[Random.Range(0, free.Count)], false, spawnDSP, true);
                _lastLane = keepLast;
            }
        }
    }

    private void TriggerFistAt(bool right, double contactDSP)
    {
        BossFistSlam fist = right ? _rightFist : _leftFist;
        if (fist == null) return;
        fist.Slam(this, contactDSP);
    }

    private int RandomLaneExcept(int safeLane)
    {
        if (_laneX == null || _laneX.Length == 0) return 0;
        if (_laneX.Length == 1) return 0;
        int idx;
        do { idx = Random.Range(0, _laneX.Length); } while (idx == safeLane);
        return idx;
    }

    /// <summary>
    /// Lanza un barril. 'plannedDSP' es el instante previsto del lanzamiento (si viene del planificador), para que
    /// aterrice exactamente al ritmo aunque el fotograma llegue unos milisegundos tarde.
    /// </summary>
    private void SpawnProjectile(int laneIndex, bool deflectable, double plannedDSP = -1, bool life = false)
    {
        if (_projectilePrefab == null || PlayerT == null) return;
        laneIndex = Mathf.Clamp(laneIndex, 0, _laneX.Length - 1);
        _lastLane = laneIndex;   // para que el siguiente barril no repita carril

        Vector3 spawnPos = _projectileSpawn != null ? _projectileSpawn.position : BossT.position;
        // Se mantiene la rotación del prefab, ya que el barril ya viene girado 90° en Z.
        BossProjectile p = Instantiate(_projectilePrefab, spawnPos, _projectilePrefab.transform.rotation);

        double spawnDSP = plannedDSP > 0 ? plannedDSP : AudioSettings.dspTime;
        double landDSP  = spawnDSP + _projectileFlightBeats * BeatDuration;
        p.Init(this, _laneX[laneIndex], landDSP, deflectable);
        if (life) p.MakeLifeBarrel();

        // Tutorial: "Q" encima de los primeros barriles que se pueden devolver.
        if (deflectable && _qHintsShown < TutorialHintsPerAttack &&
            TutorialHint.Attach(p.transform, "Q", life ? "vida extra" : "devuélvelo", 0.5f) != null)
            _qHintsShown++;
    }

    // Tutorial del jefe (solo con los consejos activados): carteles las primeras veces de cada ataque.
    private const int TutorialHintsPerAttack = 3;
    private int _qHintsShown, _slamHintsShown;

    private void TriggerTailAt(double contactDSP, float sweepBeats)
    {
        if (_tridentSweep == null) return;
        bool high = Random.value < 0.5f;            // al azar, alto (agacharse) o bajo (saltar)
        _tridentSweep.Sweep(this, high, _tailFromRightNext, contactDSP, sweepBeats);
        _tailFromRightNext = !_tailFromRightNext;   // alterna el lado (derecha e izquierda)
    }

    // -=+=- Fase 2: pilares, bolas de fuego y caída de bruces -=+=-

    private bool _p2NewRoundPending;   // la cabeza acaba de volver, así que la siguiente ronda empieza en el próximo compás

    private void Phase2Beat(int beat)
    {
        // Después del pincho y la cabeza, la ronda nueva empieza en el siguiente compás (múltiplo de 4), una vez que el
        // jefe se ha recuperado, para que las bolas de fuego del principio de la ronda no se salten.
        if (_p2NewRoundPending)
        {
            if (_p2sub != Phase2Sub.Attacking || beat % 4 != 0) return;
            _phase2StartBeat   = beat;
            _p2NewRoundPending = false;
        }

        int since = beat - _phase2StartBeat;
        if (since < 0) return;
        int rel = since % Phase2Length;   // solo da la vuelta solo si no hay cabeza ni pincho
        if (rel == 0) LockRoundDifficulty();          // nueva ronda, con la dificultad según el daño hecho en esta fase
        if (_p2sub != Phase2Sub.Attacking) return;   // durante el pincho, el vuelo, el pisotón y la recuperación no lanza ataques

        _p2Beat = beat;   // beat exacto que se procesa (referencia para programar a medio beat)
        Phase2Chart(rel);
    }

    // -=+=- Estructura de la ronda de la fase 2, en beats desde el inicio de la ronda -=+=-
    private const float FireSectionBeats = 7f;    // 1) bolas de fuego, del beat relativo 0 al 6
    private const int   WaveStartRel     = 7;     // 2) ondas, que empiezan en el beat relativo 7...
    private const float WaveSectionBeats = 14f;   //    ...y duran 14 beats (del 7 al 20)
    private const int   ObstStartRel     = 22;    // 3) obstáculos, del beat relativo 22 al 28, que se van apagando
    private static readonly float[] ObstacleRamp = { 1f, 1f, 1f, 0.8f, 0.6f, 0.4f, 0.2f };
    private const int   SpikeRel         = 29;    // 4) pincho, justo al acabar los obstáculos (parece su último pincho)
    private const int   Phase2Length     = 36;    // múltiplo de 4

    /// <summary>
    /// Ronda de la fase 2, en este orden: 1) bolas de fuego, 2) ondas (puños, con varios patrones), 3) obstáculos (pilares
    /// que se van apagando) y, al terminar, el pincho sube y golpea la cabeza (la ventana de daño).
    /// </summary>
    private void Phase2Chart(int rel)
    {
        bool waves = _enableShockwaveAttack && _rightFist != null && _leftFist != null;

        // 1) Bolas de fuego, en ráfagas cada 'fireballInterval' beats durante FireSectionBeats.
        if (rel == 0) ScheduleFireballs();

        // 2) Ondas. Los puños golpean el suelo delante del jefe con uno de los patrones (a la vez, alternos, al azar o
        //    rítmico) durante WaveSectionBeats. Las ondas se esquivan saltando.
        if (rel == WaveStartRel && waves) ScheduleWaves();

        // 3) Obstáculos, en ráfagas en una X libre, que se van apagando hacia el final (con 2 beats despejados) para que se
        //    vea bien cómo sale volando la cabeza.
        int oi = rel - ObstStartRel;
        if (oi >= 0 && oi < ObstacleRamp.Length) ObstacleBeat(ObstacleRamp[oi]);

        // 4) Al terminar los obstáculos, el pincho sube y golpea la barbilla.
        if (rel == SpikeRel) TriggerHeadSpike();
    }
    private int _p2Beat;                             // beat absoluto que se está procesando en la fase 2

    /// <summary>Indica si el ataque programado sigue en pie, es decir, si se está en la misma fase y la cabeza no está fuera.</summary>
    private bool Phase2Attacking => _state == BossState.Phase2 && _p2sub == Phase2Sub.Attacking;

    /// <summary>Programa las ráfagas de bolas de fuego de la ronda, cada 'fireballInterval' beats.</summary>
    private void ScheduleFireballs()
    {
        if (_fireballPrefab == null)
        {
            Debug.LogWarning("[Boss] No hay Fireball Prefab asignado en el BossController: no saldrán bolas de fuego.", this);
            return;
        }
        int   b   = _p2Beat;
        float gap = Mathf.Max(0.25f, P2Tier.fireballInterval);
        if (_verbose) Debug.Log($"[Boss] Bolas de fuego: ráfaga cada {gap} b durante {FireSectionBeats} beats.");
        for (float x = 0f; x < FireSectionBeats - 0.001f; x += gap)
            Schedule(DspAt(b, x), () => { if (Phase2Attacking) FireballVolley(); });
    }

    // Manos: 0 es la izquierda, 1 la derecha y 2, las dos a la vez.
    private const int HandL = 0, HandR = 1, HandBoth = 2;

    /// <summary>
    /// Patrones rítmicos del ataque de ondas, con la mano y la duración hasta el siguiente golpe en "medios pasos". Un
    /// medio paso es la mitad del 'waveInterval' de la dificultad (como mínimo, medio beat). Con un intervalo de 1, la
    /// síncopa es izquierda (beat), derecha (½), derecha (½)..., y el galope, izquierda (½), izquierda (½), derecha
    /// (beat)... Cada vez se elige uno al azar y, a veces, en espejo (cambiando izquierda por derecha).
    /// </summary>
    private static readonly (int hand, float halves)[][] WaveRhythms =
    {
        new[] { (HandL, 2f), (HandR, 1f), (HandR, 1f) },                                      // síncopa
        new[] { (HandL, 1f), (HandL, 1f), (HandR, 2f) },                                      // galope
        new[] { (HandL, 2f), (HandR, 1f), (HandR, 1f), (HandL, 1f), (HandR, 1f), (HandL, 2f) }, // redoble
        new[] { (HandL, 1f), (HandR, 1f), (HandL, 1f), (HandR, 1f), (HandBoth, 4f) },         // redoble y remate
    };
    private static readonly string[] WaveRhythmNames = { "síncopa", "galope", "redoble", "redoble con remate" };

    /// <summary>
    /// Programa el ataque de ondas de la ronda (7 beats) con uno de estos patrones, elegido al azar.
    ///   - A la vez (con probabilidad 'bothTogetherChance'), los dos puños juntos cada 2 intervalos.
    ///   - Alternos, derecha, izquierda, derecha, izquierda... cada 'waveInterval' beats.
    ///   - Alternos al azar, cada golpe con una mano al azar, cada 'waveInterval' beats.
    ///   - Rítmico, uno de los patrones de <see cref="WaveRhythms"/> (síncopa, galope, redoble...).
    /// Se programan los contactos, que caen justo en el ritmo, y el aviso de cada puño se calcula para que el golpe
    /// anterior de esa misma mano ya haya caído y soltado su onda antes de volver a levantarse.
    /// </summary>
    private void ScheduleWaves()
    {
        Phase2Tier tier = P2Tier;
        // Entre dos ondas tiene que caber un salto completo, ya que, si llegaran cada medio beat (en dificultad alta o con
        // los medios pasos de los patrones rítmicos), el jugador todavía estaría en el aire y la siguiente le alcanzaría al caer.
        float gap = Mathf.Max(MinWaveGap, tier.waveInterval);

        // 1) Lista de golpes, con el instante del contacto respecto a la sección y la mano. El primero llega a 1 beat (con
        //    el aviso completo). El ataque se divide en dos mitades y a mitad cambia de patrón (la segunda nunca repite el
        //    tipo de la primera).
        var hits = new System.Collections.Generic.List<(float t, int hand)>();
        float t = 1f;
        int kind1 = AddWavePattern(hits, ref t, WaveSectionBeats * 0.5f, gap, tier, -1,    out string name1);
        AddWavePattern(hits, ref t, WaveSectionBeats, gap, tier, kind1, out string name2);
        string pattern = $"{name1} → {name2}";

        // 2) Se programa cada golpe. El aviso dura como mucho 1 beat y nunca más que el tiempo desde el golpe anterior de
        //    esa mano, para que ya haya tocado el suelo y soltado su onda antes de volver a levantarse.
        int   b     = _p2Beat;
        float lastL = 0f, lastR = 0f;
        float maxWu = Mathf.Max(0.5f, _fistWindupBeats);
        foreach (var hit in hits)
        {
            float prev = hit.hand == HandL ? lastL : hit.hand == HandR ? lastR : Mathf.Max(lastL, lastR);
            float wu   = Mathf.Min(maxWu, hit.t - prev);
            if (wu < 0.25f) continue;                                // demasiado seguido, así que se descarta (no ocurre con los patrones actuales)

            double contactDSP = DspAt(b, hit.t);
            int    hand       = hit.hand;
            Schedule(DspAt(b, hit.t - wu), () =>
            {
                if (!Phase2Attacking) return;
                if (hand != HandL) TriggerFistWave(true,  contactDSP);   // derecha (o las dos)
                if (hand != HandR) TriggerFistWave(false, contactDSP);   // izquierda (o las dos)
            });

            if (hand != HandR) lastL = hit.t;
            if (hand != HandL) lastR = hit.t;
        }

        if (_verbose) Debug.Log($"[Boss] Ondas: patrón {pattern}, {hits.Count} golpes (intervalo {gap} b).");
    }

    /// <summary>Separación mínima entre dos ondas (en beats), que es lo que dura un salto del jugador en este nivel.</summary>
    private float MinWaveGap => Mathf.Max(0.5f, _pm != null ? _pm.jumpAirTimeBeats : 1f);

    // Tipos de patrón de ondas
    private const int WaveTogether = 0, WaveAlternate = 1, WaveRandomAlt = 2, WaveRhythm = 3;

    /// <summary>
    /// Añade golpes a 'hits' desde 't' hasta 'end' con un patrón elegido al azar (distinto de 'excludeKind'). Devuelve el
    /// tipo elegido y deja 't' en el siguiente golpe, para que la segunda mitad siga el ritmo sin cortes.
    /// </summary>
    private int AddWavePattern(System.Collections.Generic.List<(float t, int hand)> hits, ref float t, float end,
                               float gap, Phase2Tier tier, int excludeKind, out string name)
    {
        // Se elige el tipo. "A la vez" tiene su propia probabilidad (si no está excluido) y, si no sale, se elige uno de los otros tres.
        int kind;
        if (excludeKind != WaveTogether && Random.value < tier.bothTogetherChance) kind = WaveTogether;
        else
        {
            var options = new System.Collections.Generic.List<int> { WaveAlternate, WaveRandomAlt, WaveRhythm };
            options.Remove(excludeKind);
            kind = options[Random.Range(0, options.Count)];
        }

        float rhythmHalf = Mathf.Max(MinWaveGap, gap * 0.5f);   // "medio paso" de los patrones rítmicos (nunca menos que un salto)

        switch (kind)
        {
            case WaveTogether:
                name = "a la vez";
                for (; t < end; t += 2f * gap) hits.Add((t, HandBoth));
                break;

            case WaveAlternate:
            {
                name = "alternos";
                int h = HandR;                                        // derecha primero
                for (; t < end; t += gap, h = 1 - h) hits.Add((t, h));
                break;
            }

            case WaveRandomAlt:
                name = "alternos al azar";
                for (; t < end; t += gap) hits.Add((t, Random.value < 0.5f ? HandL : HandR));
                break;

            default:
            {
                int   p      = Random.Range(0, WaveRhythms.Length);
                bool  mirror = Random.value < 0.5f;
                name = WaveRhythmNames[p] + (mirror ? " (espejo)" : "");
                var seq = WaveRhythms[p];
                for (int i = 0; t < end; i++)
                {
                    var step = seq[i % seq.Length];
                    int hand = step.hand == HandBoth ? HandBoth : (mirror ? 1 - step.hand : step.hand);
                    hits.Add((t, hand));
                    t += step.halves * rhythmHalf;
                }
                break;
            }
        }
        return kind;
    }

    /// <summary>Un puño golpea el suelo (sin matar) delante del jefe y suelta una onda expansiva. La onda mata, y se esquiva saltando.</summary>
    private void TriggerFistWave(bool right, double contactDSP)
    {
        BossFistSlam fist = right ? _rightFist : _leftFist;
        if (fist == null) return;
        fist.Slam(this, contactDSP, emitShockwave: true, lethal: false, trackPlayer: false,
                  offsetX: 0f, frontZ: _fistWaveFront, centerPull: _fistWaveCenterPull);
    }

    /// <summary>
    /// Un beat de obstáculos con una intensidad de 0 a 1, que determina cuántos pilares salen. Con intensidad máxima se
    /// reparten más ráfagas dentro del beat, cada 'obstacleInterval' beats (0,5 es medio beat y 0,33, un tresillo), y al
    /// final de la oleada solo sale una por beat, cada vez más débil, para dejar ver cómo sale volando la cabeza.
    /// </summary>
    private void ObstacleBeat(float intensity = 1f)
    {
        if (intensity <= 0f) return;
        SpawnPillarBurst(intensity);
        if (intensity < 0.99f) return;

        int   b   = _p2Beat;
        float gap = Mathf.Clamp(P2Tier.obstacleInterval, 0.25f, 1f);
        for (float x = gap; x < 0.999f; x += gap)
            Schedule(DspAt(b, x), () => { if (Phase2Attacking) SpawnPillarBurst(intensity); });
    }

    /// <summary>
    /// Ráfaga de obstáculos, de Min a Max pilares, en una X libre (Obstacle Min y Max X, sin carriles), altos o bajos al
    /// azar y escalonados en Z para que no salgan unos encima de otros.
    /// </summary>
    private void SpawnPillarBurst(float intensity = 1f)
    {
        if (_pillarPrefab == null || PlayerT == null) return;
        Phase2Tier t = P2Tier;
        int lo = Mathf.Max(1, Mathf.Min(t.obstaclesMin, t.obstaclesMax));
        int hi = Mathf.Max(lo, t.obstaclesMax);
        int n  = Mathf.Max(1, Mathf.RoundToInt(Random.Range(lo, hi + 1) * Mathf.Clamp01(intensity)));
        for (int i = 0; i < n; i++)
        {
            float x = Random.Range(_obstacleMinX, _obstacleMaxX);
            bool tall = Random.value < 0.5f;
            float extraLead = Random.Range(0f, _obstacleDepthSpread);
            SpawnPillarAtX(x, tall, extraLead);
        }
    }

    private void SpawnPillarAtX(float worldX, bool tall, float extraLead = 0f)
    {
        if (_pillarPrefab == null || PlayerT == null) return;

        Vector3 fwd     = _pm != null ? _pm.RunForward : Vector3.forward;
        float   walk    = _pm != null ? _pm.walkSpeed : 12f;
        float   lead    = walk * _pillarLeadBeats * BeatDuration + extraLead;   // aviso y escalonado
        float   groundY = _playerCC != null ? _playerCC.bounds.min.y : PlayerT.position.y;

        Vector3 top = PlayerT.position + fwd * lead;
        top.x = worldX;
        top.y = groundY;

        BossPillar pillar = Instantiate(_pillarPrefab, top, Quaternion.identity);
        Vector3 s = pillar.transform.localScale;
        s.y = tall ? _pillarTallScaleY : _pillarShortScaleY;   // alto (cambiar de carril) o bajo (saltar)
        pillar.transform.localScale = s;
        pillar.Init(this, AudioSettings.dspTime);
    }

    /// <summary>Ráfaga de bolas de fuego, de Min a Max en este beat, repartidas en X desde la boca.</summary>
    private void FireballVolley()
    {
        Phase2Tier t = P2Tier;
        int lo = Mathf.Max(1, Mathf.Min(t.fireballsMin, t.fireballsMax));
        int hi = Mathf.Max(lo, t.fireballsMax);
        int n  = Random.Range(lo, hi + 1);
        // Una vez en la fase 2, una de las bolas de alguna ráfaga es la bola de vida (verde), que da la vida al tocarla.
        _p2Volleys++;
        int lifeIdx = !_p2LifeSpawned && (Random.value < 0.4f || _p2Volleys >= 3) ? Random.Range(0, n) : -1;
        for (int i = 0; i < n; i++)
        {
            // Se reparten a lo ancho (y un poco en altura) para que no salgan todas del mismo punto.
            float f = n > 1 ? i / (float)(n - 1) : 0.5f;
            Vector3 offset = new Vector3(Mathf.Lerp(-_fireballSpread, _fireballSpread, f),
                                         Random.Range(-1f, 1.5f), 0f);
            SpawnFireball(offset, i == lifeIdx);
        }
    }

    private void SpawnFireball() => SpawnFireball(Vector3.zero);

    private void SpawnFireball(Vector3 offset, bool life = false)
    {
        if (_fireballPrefab == null || PlayerT == null) return;
        Vector3 spawnPos = (_fireballSpawn != null ? _fireballSpawn.position
                         : (_projectileSpawn != null ? _projectileSpawn.position : BossT.position)) + offset;
        BossHomingFireball fb = Instantiate(_fireballPrefab, spawnPos, Quaternion.identity);
        fb.Init(this, _homingBeats, 1f);   // persigue al jugador durante _homingBeats y va recta 1 beat antes del impacto
        if (life) { fb.MakeLife(); _p2LifeSpawned = true; }
    }

    /// <summary>Fin de los obstáculos. El pincho empieza a subir hacia la barbilla del jefe.</summary>
    private void TriggerHeadSpike()
    {
        if (_head == null) return;
        _p2sub = Phase2Sub.SpikeRising;
        _spikeStartDSP = AudioSettings.dspTime;
        _spikeEndDSP   = _spikeStartDSP + _spikeRiseBeats * BeatDuration;
        SpawnHeadSpike();   // cono generado por código bajo la barbilla
        if (_verbose) Debug.Log("[Boss] Spike subiendo hacia la barbilla.");
    }

    // -=+=- Pincho generado por código -=+=-

    private float _spikeRaise01;   // 0 es escondido bajo el suelo y 1, tocando la cabeza
    private float _spikeChinDist;  // cuánto se adelanta el pincho (hacia el jugador) respecto al centro de la cabeza

    /// <summary>
    /// Posición XZ del pincho, bajo la barbilla (la cara de la cabeza que mira al jugador) y no bajo el centro, que queda
    /// encima del torso y lo taparía por completo.
    /// </summary>
    private Vector3 SpikeXZ()
    {
        Vector3 hp = HeadRestWorld;
        float dirZ = PlayerT != null ? Mathf.Sign(PlayerT.position.z - hp.z) : 1f;
        return new Vector3(hp.x, 0f, hp.z + dirZ * _spikeChinDist);
    }

    /// <summary>Posición de reposo de la cabeza en el mundo, que sigue al jefe aunque la cabeza salga volando.</summary>
    private Vector3 HeadRestWorld =>
        (_headCaptured && _head.parent != null) ? _head.parent.TransformPoint(_headRestLocalPos) : _head.position;

    /// <summary>
    /// Crea el pincho, un cono generado por código (con malla propia, el pivote en la base y altura 1, que se escala a la
    /// altura del suelo a la barbilla). No se usa el prefab del pilar, ya que es una malla de ProBuilder que se genera
    /// durante la partida y, al instanciarlo, sus bounds valían 0 y la escala calculada no era válida.
    /// </summary>
    private void SpawnHeadSpike()
    {
        if (_spikeObj != null) Destroy(_spikeObj.gameObject);

        // Bajo la barbilla, delante del centro de la cabeza (a media profundidad de la cabeza más un margen).
        Renderer headR = _head.GetComponentInChildren<Renderer>();
        Bounds hb = headR != null ? headR.bounds : new Bounds(HeadRestWorld, Vector3.one * 2f);
        _spikeChinDist = hb.extents.z * 0.8f + _spikeChinForward;

        Vector3 sp = SpikeXZ();
        _spikeGroundY = GroundYBelow(sp.x, sp.z, _lastGroundY);
        _spikeH = Mathf.Max(1f, hb.min.y - _spikeGroundY + 0.3f);   // +0,3, para que entre un poco en la barbilla

        // Raíz con el pivote en la base del pincho, que es lo que sube y baja. El modelo va como hijo.
        var root = new GameObject("BossHeadSpike");
        root.transform.position = new Vector3(sp.x, _spikeGroundY - _spikeH, sp.z);   // empieza escondido
        _spikeObj = root.transform;

        if (_headSpikePrefab != null)
        {
            // El modelo se instancia como hijo y se mide, lo que puede tardar un fotograma si genera la malla durante la partida.
            GameObject model = Instantiate(_headSpikePrefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            foreach (Collider c in model.GetComponentsInChildren<Collider>()) c.enabled = false;  // decorativo
            _spikeModel    = model.transform;
            _spikeMeasured = MeasureSpikeModel();
        }
        else
        {
            // Sin prefab, se usa el cono generado por código (de diámetro 1 y altura 1, con la base en y = 0), y la raíz escala la altura.
            float radius = _spikeBaseRadius > 0f ? _spikeBaseRadius : Mathf.Clamp(hb.extents.x * 0.35f, 0.8f, 6f);
            var cone = new GameObject("SpikeCone");
            cone.transform.SetParent(root.transform, false);
            cone.AddComponent<MeshFilter>().sharedMesh = BuildConeMesh(16);
            var mr = cone.AddComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Sprites/Default")) { color = _spikeColor };  // cualquier pipeline
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cone.transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
            root.transform.localScale = new Vector3(1f, _spikeH, 1f);
            _spikeModel    = cone.transform;
            _spikeMeasured = true;
        }

        _spikeRaise01 = 0f;
        PlaceSpike();
        if (_verbose) Debug.Log($"[Boss] Spike creado bajo la barbilla ({(_headSpikePrefab != null ? _headSpikePrefab.name : "cono")}): " +
                                $"suelo Y={_spikeGroundY:F2}, altura={_spikeH:F2}, medido={_spikeMeasured}.", root);
    }

    /// <summary>
    /// Mide el modelo a escala 1 y lo ajusta. Centra su X y Z en la raíz, apoya su base en y = 0 de la raíz y escala la
    /// raíz para que mida justo _spikeH (del suelo a la barbilla). Devuelve false si todavía no hay malla que medir (se
    /// vuelve a intentar en cada fotograma). Si se indica Spike Model Height, se usa ese valor en lugar de medir.
    /// </summary>
    private bool MeasureSpikeModel()
    {
        if (_spikeObj == null || _spikeModel == null) return true;

        _spikeObj.localScale     = Vector3.one;
        _spikeModel.localPosition = Vector3.zero;

        Vector3 rootPos = _spikeObj.position;
        bool valid = false;
        Bounds b = default;
        foreach (Renderer r in _spikeModel.GetComponentsInChildren<Renderer>())
        {
            if (r.bounds.size.sqrMagnitude < 1e-6f) continue;   // la malla todavía no se ha generado
            if (!valid) { b = r.bounds; valid = true; } else b.Encapsulate(r.bounds);
        }

        float h;
        if (_spikeModelHeight > 0f)
        {
            h = _spikeModelHeight;
            if (valid) _spikeModel.localPosition = new Vector3(rootPos.x - b.center.x, rootPos.y - b.min.y, rootPos.z - b.center.z);
        }
        else if (valid && b.size.y > 0.001f)
        {
            h = b.size.y;
            _spikeModel.localPosition = new Vector3(rootPos.x - b.center.x, rootPos.y - b.min.y, rootPos.z - b.center.z);
        }
        else
        {
            return false;   // todavía no se puede medir, así que se vuelve a intentar en el próximo fotograma
        }

        float k = _spikeH / h;
        _spikeObj.localScale = _spikeUniformScale ? Vector3.one * k : new Vector3(1f, k, 1f);
        return true;
    }

    /// <summary>Coloca el pincho según _spikeRaise01. Su base va de -altura (escondido) a la superficie.</summary>
    private void PlaceSpike()
    {
        if (_spikeObj == null) return;
        Vector3 sp = SpikeXZ();
        float baseY = _spikeGroundY - _spikeH * (1f - _spikeRaise01);
        _spikeObj.position = new Vector3(sp.x, baseY, sp.z);   // el pivote del cono es el centro de la base
    }

    /// <summary>Malla de un cono unitario, con la base de diámetro 1 en y = 0 (centrada) y la punta en y = 1.</summary>
    private static Mesh BuildConeMesh(int segments)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();

        // Laterales, con vértices propios por cara para un sombreado facetado limpio
        for (int i = 0; i < segments; i++)
        {
            float a0 = i       / (float)segments * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            int b = verts.Count;
            verts.Add(new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f));
            verts.Add(new Vector3(0f, 1f, 0f));
            verts.Add(new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f));
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
        }
        // Tapa inferior
        int center = verts.Count;
        verts.Add(Vector3.zero);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i       / (float)segments * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            int b = verts.Count;
            verts.Add(new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f));
            verts.Add(new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f));
            tris.Add(center); tris.Add(b); tris.Add(b + 1);
        }

        var mesh = new Mesh { name = "SpikeCone" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Superficie de la plataforma en (x, z) mediante un raycast, ignorando al jefe, al jugador, los pilares y el pincho.</summary>
    private float GroundYBelow(float x, float z, float fallback)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, fallback + 60f, z), Vector3.down, 250f,
                                               ~0, QueryTriggerInteraction.Ignore);
        float best = fallback, bestDiff = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(transform)) continue;                                  // jefe
            if (PlayerT != null && t.IsChildOf(PlayerT)) continue;                 // jugador
            if (h.collider.GetComponentInParent<BossPillar>() != null) continue;   // pilares
            if (_spikeObj != null && t.IsChildOf(_spikeObj)) continue;             // el propio pincho
            float diff = Mathf.Abs(h.point.y - fallback);
            if (diff < bestDiff) { bestDiff = diff; best = h.point.y; }
        }
        return best;
    }

    private void EnterRecover(double now)
    {
        _p2sub = Phase2Sub.Recovering;
        _recoverEndDSP = now + _recoverBeats * BeatDuration;
        OnRecover?.Invoke();
    }

    /// <summary>Fase 2 en cada fotograma. El pincho lanza la cabeza, la cabeza queda vulnerable (slam), el jugador rebota y el jefe se recupera.</summary>
    private void UpdatePhase2()
    {
        if (_state != BossState.Phase2 || _head == null || PlayerT == null) return;
        double now = AudioSettings.dspTime;

        // Altura del suelo, con los pies del jugador solo cuando toca el suelo, para que no suba al saltar.
        if (_playerCC != null && _playerCC.isGrounded) _lastGroundY = _playerCC.bounds.min.y;

        AnimateSpike(now);

        switch (_p2sub)
        {
            case Phase2Sub.SpikeRising:
                if (now >= _spikeEndDSP) StartHeadFly(now);   // el pincho ha chocado con la barbilla
                break;

            case Phase2Sub.HeadFlying:
                UpdateHeadFly(now);
                break;

            case Phase2Sub.Vulnerable:
                UpdateHeadVulnerable(now);
                break;

            case Phase2Sub.Recovering:
                UpdateHeadRecover(now);
                break;
        }
    }

    /// <summary>El pincho sube en SpikeRising y se retrae en cualquier otro estado.</summary>
    private void AnimateSpike(double now)
    {
        // Sube hasta la barbilla en SpikeRising, se queda arriba un momento y después se hunde en un beat aproximadamente.
        if (_spikeObj == null) return;
        if (!_spikeMeasured) _spikeMeasured = MeasureSpikeModel();   // el modelo todavía está generando su malla
        if (_p2sub == Phase2Sub.SpikeRising)
        {
            float u = _spikeEndDSP > _spikeStartDSP
                ? Mathf.Clamp01((float)((now - _spikeStartDSP) / (_spikeEndDSP - _spikeStartDSP)))
                : 1f;
            _spikeRaise01 = u * u * u;   // empieza lento y golpea rápido, para que se note el impacto
        }
        else if (now < _spikeEndDSP + _spikeHoldBeats * BeatDuration)
        {
            _spikeRaise01 = 1f;          // se queda arriba después del golpe, para que se vea que ha lanzado la cabeza
        }
        else
        {
            _spikeRaise01 = Mathf.MoveTowards(_spikeRaise01, 0f, Time.deltaTime / Mathf.Max(0.05f, BeatDuration));
            if (_spikeRaise01 <= 0f)
            {
                Destroy(_spikeObj.gameObject);
                _spikeObj = null;
                return;
            }
        }
        PlaceSpike();
    }

    private float    _headLaneX;      // carril (X) en el que cae la cabeza esta vez
    private Renderer _headRenderer;   // caja de la cabeza, para aplastar al jugador si le cae encima
    private bool     _headCrushDone;

    /// <summary>La cabeza sale volando, en arco, hacia uno de los 3 carriles al azar.</summary>
    private void StartHeadFly(double now)
    {
        _p2sub = Phase2Sub.HeadFlying;
        _headFlyStartDSP  = now;
        _headFlyEndDSP    = now + _headFlyBeats * BeatDuration;
        _headFlyFromWorld = _head.position;
        _headLaneX        = (_laneX != null && _laneX.Length > 0) ? _laneX[Random.Range(0, _laneX.Length)] : 0f;
        _headCrushDone    = false;
        // Tutorial: "SALTA + S" encima de la cabeza desde que sale volando y mientras está en el suelo (las primeras veces).
        _slamHintsShown++;
        if (_head.GetComponent<TutorialHint>() == null)
        {
            TutorialHint h = TutorialHint.Attach(_head, "SALTA + S", "sobre la cabeza", 0.8f, 8f,
                () => (_p2sub == Phase2Sub.HeadFlying || _p2sub == Phase2Sub.Vulnerable) && _slamHintsShown <= TutorialHintsPerAttack);
            if (h != null) { h.color = Color.black; h.outlineColor = new Color(1f, 1f, 1f, 0.85f); }   // este, en negro
        }
        if (_headRenderer == null) _headRenderer = _head.GetComponentInChildren<Renderer>();
        OnFaceplant?.Invoke();
        if (_verbose) Debug.Log($"[Boss] La cabeza sale volando hacia el carril X={_headLaneX}.");
    }

    private void UpdateHeadFly(double now)
    {
        float u = _headFlyEndDSP > _headFlyStartDSP
            ? Mathf.Clamp01((float)((now - _headFlyStartDSP) / (_headFlyEndDSP - _headFlyStartDSP)))
            : 1f;

        Vector3 target = new Vector3(_headLaneX, _lastGroundY + _headGroundOffset, PlayerT.position.z);
        Vector3 p = Vector3.Lerp(_headFlyFromWorld, target, u);
        p.y += _headFlyArcHeight * Mathf.Sin(u * Mathf.PI);        // arco de vuelo
        _head.position = p;

        // Si cae encima del jugador (en el tramo final de la caída), lo aplasta. Se esquiva cambiando de carril al verla venir.
        if (u >= 0.6f) CheckHeadCrush();

        if (u >= 1f)
        {
            CheckHeadCrush();
            // Al estrellarse contra el suelo, suelta una onda expansiva que hay que saltar.
            float gy = GroundYBelow(_head.position.x, _head.position.z, _lastGroundY);
            BossShockwave.Spawn(this, new Vector3(_head.position.x, gy, _head.position.z));

            _p2sub = Phase2Sub.Vulnerable;
            _vulnEndDSP = now + _vulnBeats * BeatDuration;

        }
    }

    /// <summary>La cabeza aplasta al jugador si su caja le toca mientras cae, una sola vez por caída.</summary>
    private void CheckHeadCrush()
    {
        if (_headCrushDone || _headRenderer == null || _playerCC == null) return;
        if (_headRenderer.bounds.Intersects(_playerCC.bounds))
        {
            _headCrushDone = true;
            if (_verbose) Debug.Log("[Boss] ¡La cabeza ha aplastado al jugador!");
            KillPlayer();
        }
    }

    private void UpdateHeadVulnerable(double now)
    {
        // La cabeza queda en su carril, a ras del suelo (no sube al saltar) y en la Z del jugador.
        _head.position = new Vector3(_headLaneX, _lastGroundY + _headGroundOffset, PlayerT.position.z);

        if (_pm != null && _pm.IsSlamming)
        {
            Vector3 d = _head.position - PlayerT.position; d.y = 0f;
            if (d.magnitude <= _headHitRadius)
            {
                TakeDamage(1);
                _pm.Bounce(_playerBounceForce);   // el jugador rebota y la cabeza vuelve al jefe
                EnterRecover(now);
                return;
            }
        }

        if (now >= _vulnEndDSP) EnterRecover(now);
    }

    private void UpdateHeadRecover(double now)
    {
        if (!_headCaptured) { _p2sub = Phase2Sub.Attacking; _p2NewRoundPending = true; return; }
        _head.localPosition = Vector3.Lerp(_head.localPosition, _headRestLocalPos, Time.deltaTime * 6f);
        _head.localRotation = Quaternion.Slerp(_head.localRotation, _headRestLocalRot, Time.deltaTime * 6f);
        if (now >= _recoverEndDSP)
        {
            _head.localPosition = _headRestLocalPos;
            _head.localRotation = _headRestLocalRot;
            _p2sub = Phase2Sub.Attacking;
            _p2NewRoundPending = true;   // la siguiente ronda (que empieza por las bolas de fuego) empieza en el próximo compás
        }
    }

    // -=+=- Daño al jefe -=+=-

    /// <summary>Aplica daño al jefe. Lo llaman los proyectiles desviados y otras mecánicas.</summary>
    public void TakeDamage(int amount = 1)
    {
        if (_state == BossState.Defeated || amount <= 0) return;

        _hp = Mathf.Max(0, _hp - amount);
        _hitsThisPhase += amount;
        OnHpChanged?.Invoke(_maxHp > 0 ? _hp / (float)_maxHp : 0f);
        OnBossHit?.Invoke();
        if (_verbose) Debug.Log($"[Boss] ¡Golpe! HP {_hp}/{_maxHp} (hits fase: {_hitsThisPhase}).");

        if (_hp <= 0) { Defeat(); return; }

        // Si se ha superado la fase, se programa el cambio en el siguiente múltiplo de 4. La fase 3 es la última, y su
        // final lo gestiona ella misma al destruir la cabeza.
        if (_state != BossState.Phase3 && _hitsThisPhase >= _hitsPerPhase)
        {
            int cur = CurrentBeat();
            _phaseChangeBeat = cur - (cur % 4) + 4;   // siguiente primer beat de compás
            _pendingPhase    = CurrentPhaseNumber() + 1;
            if (_verbose) Debug.Log($"[Boss] Fase superada → cambio en beat {_phaseChangeBeat}.");
        }
    }

    private int CurrentPhaseNumber()
    {
        switch (_state)
        {
            case BossState.Phase1: return 1;
            case BossState.Phase2: return 2;
            case BossState.Phase3: return 3;
            default: return 0;
        }
    }

    private void Defeat()
    {
        if (_state == BossState.Defeated) return;
        _state = BossState.Defeated;
        _schedule.Clear();
        CleanupPhase3();   // quita el escudo, los BloodSuckers y las dianas que queden
        if (_verbose) Debug.Log("[Boss] Derrotado.");
        OnDefeated?.Invoke();
        BeginVictory();    // BossControllerPhase3.cs: sale el terreno de la lava, se rompe el kayak y aparece el final al acabar la canción
    }

    // -=+=- Golpes al jugador -=+=-

    /// <summary>Un ataque (proyectil) alcanza al jugador, con el mismo golpe que cualquier otro ataque del jefe.</summary>
    public void HitPlayer() => KillPlayer();

    private float _invulnerableUntil = -1f;

    /// <summary>
    /// Ataque fuerte que no se ha esquivado. Quita mucha sangre (_heavyHitBlood) y da un momento de invulnerabilidad. Si
    /// la jeringuilla se llena, el jugador muere por la sangre (las vidas extra, la Adrenalina y los puntos de control
    /// funcionan con normalidad).
    /// </summary>
    public void KillPlayer()
    {
        if (Time.time < _invulnerableUntil) return;
        if (_gameOver != null && _gameOver.IsDead) return;
        _invulnerableUntil = Time.time + _hitInvulnerability;
        GameOverManager.LastBossHitTime = Time.time;   // razón del game over si el jugador se queda sin sangre
        if (EnergyManager.Instance != null) EnergyManager.Instance.TakeBossHit(_bossHitRedFraction);
        CameraShaker.Instance?.Shake(0.55f);
        if (_verbose) Debug.Log($"[Boss] Golpe al jugador: fuera el azul, o +{_bossHitRedFraction * 100f:0}% de jeringuilla roja.");
    }
}
