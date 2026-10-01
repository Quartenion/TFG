using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Orquestador del jefe del infierno. Máquina de estados + scheduler de BEATS anclado al reloj DSP
/// (mismo modelo que StairFallingObstacle/WaterSpike: todo sale de BeatManager.ScheduledStartDSP),
/// para que los ataques caigan clavados al compás aunque baje el frame-rate.
///
/// FLUJO:
///   • Intro (beat 164): empieza la animación de escalada + sube la plataforma. Beat 172: aparición
///     principal + barra de vida. Beat 176: empieza la Fase 1 (downbeat).
///   • Fase 1: reproduce en bucle una FRASE de 16 beats (el "chart") intercalando Ataque A
///     (proyectiles rebotantes), Ataque B (barrido de cola) y el proyectil DEFLECTABLE (único daño).
///   • Cada golpe con éxito = −1 HP. A los 3 golpes de la fase → avanza a la siguiente (en el
///     siguiente múltiplo de 4). HP total = 9 (3 por fase).
///
/// Este componente NO dibuja nada: dispara ataques (BossProjectile / BossTailSweep) y emite eventos
/// (UnityEvent) para que enganches animaciones, la barra de vida y el generador de plataformas.
///
/// SETUP:
///   1. Pon esto en el objeto raíz del jefe.
///   2. Asigna el prefab de proyectil (con BossProjectile) y el BossTailSweep del jefe.
///   3. Asigna el punto de salida de proyectiles (boca/mano) y ajusta los carriles X.
///   4. Engancha los eventos: OnClimbStart→animación, OnAppear→mostrar barra, OnHpChanged→fill de la
///      barra, OnPlatformBeat→tu spawner de plataformas.
/// </summary>
[AddComponentMenu("Boss/Boss Controller")]
public partial class BossController : MonoBehaviour   // la Fase 3 está en BossControllerPhase3.cs
{
    public enum BossState { Idle, Intro, Phase1, Phase2, Phase3, Defeated }

    // Subclases concretas para que los eventos con parámetro se vean y se cableen en el Inspector.
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
    [SerializeField] private BossTailSweep _tailSweep;
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
    [SerializeField] private int _appearBeat     = 172;   // aparición principal + barra
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

    /// <summary>El jugador consigue una VIDA EXTRA (barril / bola / orbe de vida).</summary>
    public void GiveExtraLife(string how)
    {
        PlayerProgress.AddCount(ShopIds.ExtraLife, 1);
        ReviveFX.ShowMessage(Loc.T("+1 VIDA EXTRA", "+1 EXTRA LIFE"), how, new Color(0.2f, 0.9f, 0.35f, 0.4f));
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

    // ── DIFICULTAD POR RONDA ─────────────────────────────────────────────────
    // Se fija al empezar cada ronda según el daño hecho EN LA FASE ACTUAL:
    //   0 golpes → [0] FÁCIL · 1 golpe → [1] MEDIA · 2+ golpes → [2] DIFÍCIL. Al cambiar de fase vuelve a fácil.

    // Los INTERVALOS van en beats: 2 = un ataque cada 2 beats, 1 = uno por beat, 0.5 = uno cada medio beat.

    // Topes de frecuencia de la Fase 1 (se aplican SIEMPRE, ponga lo que ponga el inspector):
    private const int   MinTridentEveryBeats = 2;   // tridente: como mucho 1 barrido cada 2 beats
    private const float MinFistInterval      = 1f;  // puños:    como mucho 1 estampido por beat
    private const float MinBarrelInterval    = 0.5f;// barriles: hasta 1 lanzamiento cada medio beat

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
        //             tridente (nº, cada) · puños (nº, cada) · barriles (nº lanzamientos, cada, por lanzamiento)
        new Phase1Tier(2, 3,                4, 2f,             4,  1f,   1),   // fácil
        new Phase1Tier(4, 2,                6, 1f,             6,  0.5f, 1),   // media
        new Phase1Tier(6, 2,                8, 1f,             10, 0.5f, 2),   // difícil (topes: tridente 1/2 beats, puño 1/beat)
    };
    [SerializeField] private Phase2Tier[] _phase2DifficultyTable =
    {
        //             fuego (mín,máx,cada) · ondas (cada, vel×, a la vez) · pilares (mín,máx,cada)
        new Phase2Tier(2, 3, 1f,              2f,   1.10f, 0.4f,             2, 3, 1f),     // fácil
        new Phase2Tier(3, 5, 0.75f,           1f,   1.30f, 0.7f,             3, 4, 1f),     // media
        new Phase2Tier(4, 6, 0.5f,            0.5f, 1.55f, 1.0f,             3, 5, 0.5f),   // difícil
    };
    public IntEvent OnDifficultyChanged;   // 0 fácil · 1 media · 2 difícil (para UI/música si quieres)

    [Header("Eventos")]
    public UnityEvent OnClimbStart;              // beat 164
    public UnityEvent OnAppear;                  // beat 172 (mostrar barra de vida)
    public UnityEvent OnPhase1Start;             // beat 176
    public IntEvent OnPhaseChanged;              // nº de fase nueva (2, 3...)
    public FloatEvent OnHpChanged;               // 0..1 para el fill de la barra
    public UnityEvent OnBossHit;                 // flinch al recibir daño
    public UnityEvent OnFaceplant;               // Fase 2: cae de morros (cabeza vulnerable)
    public UnityEvent OnRecover;                 // Fase 2: se recupera (cabeza arriba)
    public UnityEvent OnDefeated;
    public UnityEvent OnPlatformBeat;            // cada 4 beats: sube una plataforma nueva

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

    // ── Estado ────────────────────────────────────────────────────────────────
    private BossState _state = BossState.Idle;
    private bool  _ready;
    private int   _lastBeat = int.MinValue;
    private int   _phase1Loop;           // nº de frase reproducida
    private int   _phase1Origin;         // beat en el que empezó la Fase 1 (para el chart; soporta debug)
    private bool  _tailFromRightNext = true;  // alterna el lado del barrido (der/izq)
    private int   _lastLane = -1;        // último carril usado (para no repetir)
    private int   _hp;
    private int   _hitsThisPhase;
    private int   _pendingPhase = -1;    // fase a la que cambiar
    private int   _phaseChangeBeat = -1; // beat (múltiplo de 4) en el que cambiar
    private PlayerMovement _pm;
    private GameOverManager _gameOver;
    private float _followForwardDist;    // lead fijo del jefe respecto al jugador
    private bool  _followInit;
    private float _homeY;                 // altura de reposo del jefe (destino de la escalada)

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
    // Spike: raíz con pivote en la base (_spikeObj) + modelo hijo (_spikeModel: tu prefab o el cono).
    private Transform _spikeObj;
    private Transform _spikeModel;
    private bool      _spikeMeasured;
    private float     _spikeH;               // altura del spike (suelo → cabeza)
    private float     _spikeGroundY;         // superficie bajo la barbilla
    private CharacterController _playerCC;
    private float     _lastGroundY;   // superficie del suelo (pies del jugador cuando toca suelo)

    // ── Getters para los ataques ────────────────────────────────────────────
    public Transform PlayerT { get; private set; }
    public Transform BossT => _bossBody != null ? _bossBody : transform;
    public float BeatDuration => _beatManager != null ? _beatManager.BeatDuration : (60f / 120f);
    public float DeflectReturnBeats => _deflectReturnBeats;
    public Color NormalColor => _normalColor;
    public Color DeflectColor => _deflectColor;
    public Color LifeColor    => _lifeColor;

    // ── Dificultad de la ronda actual ─────────────────────────────────────────
    private int _roundDifficulty;   // 0 fácil · 1 media · 2 difícil (fijada al empezar cada ronda)


    /// <summary>Multiplicador de velocidad de las ondas para la ronda actual (lo lee BossShockwave).</summary>
    public float WaveSpeedMultiplier => P2Tier.waveSpeedMul;

    private Phase1Tier P1Tier => (_phase1DifficultyTable != null && _phase1DifficultyTable.Length > 0)
        ? _phase1DifficultyTable[Mathf.Clamp(_roundDifficulty, 0, _phase1DifficultyTable.Length - 1)]
        : new Phase1Tier(2, 3, 4, 2f, 4, 1f, 1);
    private Phase2Tier P2Tier => (_phase2DifficultyTable != null && _phase2DifficultyTable.Length > 0)
        ? _phase2DifficultyTable[Mathf.Clamp(_roundDifficulty, 0, _phase2DifficultyTable.Length - 1)]
        : new Phase2Tier(2, 3, 1f, 1f, 1f, 0.5f, 3, 5, 1f);

    // ── Planificador de ataques por DSP (permite lanzar a medio beat, cuarto de beat, etc.) ──
    private readonly System.Collections.Generic.List<ScheduledAttack> _schedule =
        new System.Collections.Generic.List<ScheduledAttack>();
    private struct ScheduledAttack { public double dsp; public System.Action act; }

    /// <summary>Programa una acción para el instante DSP indicado (se ejecuta en el Update de ese frame).</summary>
    private void Schedule(double dsp, System.Action act) => _schedule.Add(new ScheduledAttack { dsp = dsp, act = act });

    /// <summary>DSP de un beat (con decimales) relativo a un beat absoluto de referencia.</summary>
    private double DspAt(int refBeat, float relBeats) => DspForBeat(refBeat + (double)relBeats);

    /// <summary>Ejecuta, en orden de tiempo, las acciones programadas que ya han llegado.</summary>
    private void RunSchedule()
    {
        if (_schedule.Count == 0) return;
        double now = AudioSettings.dspTime;
        var due = new System.Collections.Generic.List<ScheduledAttack>();
        for (int i = _schedule.Count - 1; i >= 0; i--)
            if (_schedule[i].dsp <= now) { due.Add(_schedule[i]); _schedule.RemoveAt(i); }
        due.Sort((a, b) => a.dsp.CompareTo(b.dsp));
        foreach (var s in due) s.act?.Invoke();
    }

    /// <summary>Al empezar una ronda: la dificultad = golpes hechos en ESTA fase (0→fácil, 1→media, 2+→difícil).</summary>
    private void LockRoundDifficulty()
    {
        int d = _debugForceDifficulty >= 0 ? Mathf.Clamp(_debugForceDifficulty, 0, 2)   // test
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
        (_beatManager != null ? _beatManager.ScheduledStartDSP : 0.0) + beat * BeatDuration;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

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
        if (!_disabled) InitVictory();   // BossControllerPhase3.cs: END escondido y fin de nivel bloqueado
    }

    private void Update()
    {
        if (_disabled || _beatManager == null) return;

        // Esperar a que la música esté programada (mismo gate que los obstáculos).
        if (!_ready)
        {
            if (_beatManager.ScheduledStartDSP <= 0.0) return;
            _lastBeat = CurrentBeat() - 1;   // no re-disparar beats ya pasados
            _ready = true;

            // TEST modo Normal: rebasea la intro a "ahora" para no esperar al beat 164.
            if (_autoStartOnPlay && _debugStartPhase == DebugStartPhase.Normal)
            {
                int b0    = CurrentBeat();
                int start = ((b0 / 4) + 1) * 4;                   // próximo múltiplo de 4
                int introSpan  = Mathf.Max(1, _appearBeat - _introStartBeat);
                int appearSpan = Mathf.Max(0, _phase1StartBeat - _appearBeat);
                _introStartBeat  = start;
                _appearBeat      = start + introSpan;
                _phase1StartBeat = _appearBeat + appearSpan;
                OnPlatformBeat?.Invoke();   // suelo YA bajo el jugador mientras el jefe escala
                if (_verbose) Debug.Log($"[Boss] Auto-start: intro rebaseada a beat {start} (aparece {_appearBeat}, Fase 1 {_phase1StartBeat}).");
            }
        }

        // Disparar cada beat entero exactamente una vez, anclado al DSP.
        int beat = CurrentBeat();
        while (_lastBeat < beat)
        {
            _lastBeat++;
            OnBeat(_lastBeat);
        }

        RunSchedule();   // ataques programados a mitad de beat (frecuencia según dificultad)

        FollowPlayer();
        ApplyClimb();
        UpdatePhase2();
        UpdatePhase3();
        UpdateVictory();

        // (Las mejoras de la tienda SÍ funcionan en la pelea; solo el Rebobinado está bloqueado: ver TimeRewinder.)
    }

    /// <summary>El jefe avanza con el jugador: se coloca a _leadBeats beats por delante y mantiene ese lead.</summary>
    private void FollowPlayer()
    {
        if (!_moveForward || PlayerT == null) return;
        if (_state == BossState.Idle || _state == BossState.Defeated) return;

        Vector3 fwd = _pm != null ? _pm.RunForward : Vector3.forward;
        float cur = Vector3.Dot(transform.position - PlayerT.position, fwd);
        if (!_followInit)
        {
            // Arranca EXACTAMENTE a _leadBeats beats por delante del jugador (4 beats ≈ 41.14 m).
            float walk = _pm != null ? _pm.walkSpeed : 12f;
            _followForwardDist = _leadBeats * BeatDuration * walk;
            _followInit = true;
        }
        transform.position += fwd * (_followForwardDist - cur);
    }

    /// <summary>Escalada de la intro: el jefe sube desde -climbDepth hasta su altura entre Intro y Appear.</summary>
    private void ApplyClimb()
    {
        if (!_ready) return;
        Vector3 p = transform.position;
        p.y = _homeY + ClimbOffset() - _p3SinkOffset;   // en la Fase 3 se hunde en la lava
        transform.position = p;
    }

    private float ClimbOffset()
    {
        if (_debugStartPhase != DebugStartPhase.Normal) return 0f;   // debug: el jefe ya está arriba
        double beatExact = (AudioSettings.dspTime - _beatManager.ScheduledStartDSP) / BeatDuration;
        if (beatExact <= _introStartBeat) return -_climbDepth;   // escondido bajo el suelo
        if (beatExact >= _appearBeat)     return 0f;             // ya en su sitio
        float t = (float)((beatExact - _introStartBeat) / Mathf.Max(1, _appearBeat - _introStartBeat));
        float eased = 1f - (1f - t) * (1f - t);                  // ease-out (emerge y se asienta)
        return Mathf.Lerp(-_climbDepth, 0f, eased);
    }

    private int CurrentBeat()
    {
        double b = (AudioSettings.dspTime - _beatManager.ScheduledStartDSP) / BeatDuration;
        return Mathf.FloorToInt((float)b);
    }

    // ── Scheduler: qué pasa en cada beat entero ────────────────────────────────

    private void OnBeat(int beat)
    {
        // Plataforma nueva cada 4 beats (a partir de la intro; o desde ya en modo debug).
        if ((_debugStartPhase != DebugStartPhase.Normal || beat >= _introStartBeat) && (beat % 4 == 0))
            OnPlatformBeat?.Invoke();

        // Cambio de fase pendiente (siempre en múltiplo de 4).
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
                        case DebugStartPhase.Phase2: EnterPhase(2); Phase2Beat(beat); break;   // dispara ya el rel 0 (fireballs)
                        case DebugStartPhase.Phase3: EnterPhase(3); break;
                    }
                }
                else if (beat >= _introStartBeat) EnterIntro();
                break;

            case BossState.Intro:
                if (beat >= _appearBeat && !_appeared) DoAppear();
                if (beat >= _phase1StartBeat) { EnterPhase(1); Phase1Beat(beat); }  // dispara ya el rel 0
                break;

            case BossState.Phase1:
                Phase1Beat(beat);
                break;

            case BossState.Phase2:
                Phase2Beat(beat);
                break;
        }
    }

    // ── Intro ───────────────────────────────────────────────────────────────

    private bool _appeared;

    /// <summary>¿Está en marcha la pelea (el jefe ha aparecido y no ha sido derrotado)?</summary>
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

    // ── Cambio de fase ─────────────────────────────────────────────────────────

    private void EnterPhase(int phase)
    {
        _hitsThisPhase = 0;
        _roundDifficulty = 0;   // cada fase empieza en FÁCIL
        _schedule.Clear();      // nada pendiente de la fase anterior

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
                EnterPhase3();   // BossControllerPhase3.cs: derrumbe → kayak → escudo/BloodSuckers/mirada
                break;
        }
    }

    // ── Fase 1: rondas de duración VARIABLE (según la dificultad) ─────────────────
    //
    // Cada ronda se planifica al empezar: TRIDENTE → PUÑOS → BARRILES → DEFLECTABLE, uno detrás de otro.
    // La duración sale de cuánto hay que meter y se redondea a MÚLTIPLO DE 4 (cuadra con el compás).
    //   Ej. (tridente, puños, barriles) = (2,4,2) → 16 beats · (4,5,3) → 20 · (6,6,4) → 28.

    private int _p1RoundStart, _p1RoundLen;   // inicio (beat absoluto) y duración de la ronda

    private void Phase1Beat(int beat)
    {
        if (beat < _phase1Origin) return;

        // ¿Toca ronda nueva? (la primera, o se acabó la anterior) → se planifica ENTERA de golpe.
        if (_p1RoundLen <= 0 || beat - _p1RoundStart >= _p1RoundLen)
        {
            if (_p1RoundLen > 0) _phase1Loop++;
            StartPhase1Round(beat);
        }
    }

    /// <summary>
    /// Fija la dificultad de la ronda y PROGRAMA cada ataque en su instante exacto (en beats con decimales, así
    /// se puede atacar a medio beat). Orden: TRIDENTE → PUÑOS → BARRILES → DEFLECTABLE (la ventana de daño).
    /// La frecuencia de cada ataque sale de la tabla (tridentEveryBeats / fistInterval) y la ronda dura lo que haga
    /// falta, redondeado a múltiplo de 4 para cuadrar con el compás.
    /// </summary>
    private void StartPhase1Round(int beat)
    {
        LockRoundDifficulty();   // dificultad según el daño hecho en esta fase
        Phase1Tier t = P1Tier;
        _p1RoundStart = beat;

        // 1) TRIDENTE: SIEMPRE en beats COMPLETOS (el contacto de cada barrido cae en un beat entero).
        //    El primero con su wind-up normal; los siguientes encadenados cada 'tridentEveryBeats' beats
        //    (el barrido dura 1 beat: cada uno arranca al acabar el anterior, desde el lado donde se quedó).
        int   nTail     = Mathf.Clamp(t.tridentSweeps, 1, 8);
        float tailInt   = Mathf.Clamp(t.tridentEveryBeats, MinTridentEveryBeats, 4);   // entero, mín. cada 2 beats
        float firstHit  = Mathf.Max(1, Mathf.RoundToInt(_tailWindupBeats));   // entero
        // Para que SIEMPRE haya al menos medio beat de aviso (anticipación) entre barridos, si van muy seguidos
        // el barrido en sí se hace más rápido: 1 beat → barrido de 0.5 beats + 0.5 de aviso. El contacto sigue
        // cayendo en beat completo (el barrido está centrado en él).
        float sweepDur = Mathf.Clamp(tailInt - 0.5f, 0.5f, 1f);
        for (int i = 0; i < nTail; i++)
        {
            float contact = firstHit + i * tailInt;
            float prevEnd = contact - tailInt + sweepDur * 0.5f;          // fin del barrido anterior
            float call    = i == 0 ? 0f : prevEnd;
            double contactDSP = DspAt(beat, contact);
            Schedule(DspAt(beat, call), () => { if (_state == BossState.Phase1) TriggerTailAt(contactDSP, sweepDur); });
        }
        float cursor = firstHit + (nTail - 1) * tailInt + 1f;          // fin del último barrido + margen

        // 2) PUÑOS: cada 'fistInterval' beats, alternando der/izq. El aviso (levantarse) nunca es más largo que
        //    el intervalo, así a cada puño le da tiempo a caer antes de volver a lanzarse.
        int   nFist   = _enableFistPhrases ? Mathf.Clamp(t.fistSlams, 0, 12) : 0;
        float fistInt = Mathf.Max(MinFistInterval, t.fistInterval);   // tope: 1 estampido por beat
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

        // 3) BARRILES: 'barrels' lanzamientos cada 'barrelInterval' beats (hasta medio beat), de 1 o 2 barriles
        //    en carriles distintos (siempre queda un carril libre). Al final, el DEFLECTABLE (la ventana de daño).
        int   nBarrel   = Mathf.Clamp(t.barrels, 0, 16);
        float barrelInt = Mathf.Max(MinBarrelInterval, t.barrelInterval);
        int   perThrow  = Mathf.Clamp(t.barrelsPerThrow, 1, Mathf.Max(1, (_laneX != null ? _laneX.Length : 3) - 1));
        float barrelStart = Mathf.Ceil(cursor);
        // Una vez en la Fase 1: en uno de los lanzamientos sale además el BARRIL DE VIDA (verde) en un carril libre.
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
        Schedule(deflectDSP, () => { if (_state == BossState.Phase1) SpawnProjectile(1, true, deflectDSP); });   // → daño

        // Duración: hasta que el deflectable llega al jugador (+3) y redondeada a múltiplo de 4.
        int needed  = deflect + 3;
        _p1RoundLen = Mathf.Max(4, ((needed + 3) / 4) * 4);

        if (_verbose) Debug.Log($"[Boss] Ronda F1: {nTail} tridentes (cada {tailInt} b), {nFist} puños (cada {fistInt} b), " +
                                $"{nBarrel}×{perThrow} barriles (cada {barrelInt} b) → {_p1RoundLen} beats.");
    }

    /// <summary>Lanza 'count' barriles a la vez en carriles DISTINTOS al azar (nunca todos: siempre queda uno libre).</summary>
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
            // Barajar los carriles y coger los primeros 'count'.
            int[] order = new int[lanes];
            for (int i = 0; i < lanes; i++) order[i] = i;
            for (int i = lanes - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
            for (int i = 0; i < count; i++) { SpawnProjectile(order[i], false, spawnDSP); used.Add(order[i]); }
        }

        // BARRIL DE VIDA (una vez en la Fase 1): en un carril LIBRE; devuélvelo con [Q] para llevarte la vida.
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

    /// <summary>Lanza un barril. 'plannedDSP' = instante previsto del lanzamiento (si viene del planificador),
    /// para que aterrice exactamente a ritmo aunque el frame llegue unos ms tarde.</summary>
    private void SpawnProjectile(int laneIndex, bool deflectable, double plannedDSP = -1, bool life = false)
    {
        if (_projectilePrefab == null || PlayerT == null) return;
        laneIndex = Mathf.Clamp(laneIndex, 0, _laneX.Length - 1);
        _lastLane = laneIndex;   // para que el siguiente barril no repita carril

        Vector3 spawnPos = _projectileSpawn != null ? _projectileSpawn.position : BossT.position;
        // Conservar la rotación del prefab (el barril ya viene girado 90° en Z).
        BossProjectile p = Instantiate(_projectilePrefab, spawnPos, _projectilePrefab.transform.rotation);

        double spawnDSP = plannedDSP > 0 ? plannedDSP : AudioSettings.dspTime;
        double landDSP  = spawnDSP + _projectileFlightBeats * BeatDuration;
        p.Init(this, _laneX[laneIndex], landDSP, deflectable);
        if (life) p.MakeLifeBarrel();

        // Tutorial: «Q» encima de los primeros barriles que se pueden devolver.
        if (deflectable && _qHintsShown < TutorialHintsPerAttack &&
            TutorialHint.Attach(p.transform, "Q", life ? "vida extra" : "devuélvelo", 0.5f) != null)
            _qHintsShown++;
    }

    // Tutorial del jefe (solo con los consejos activados): carteles en las primeras veces de cada ataque.
    private const int TutorialHintsPerAttack = 3;
    private int _qHintsShown, _slamHintsShown;

    private void TriggerTailAt(double contactDSP, float sweepBeats)
    {
        if (_tailSweep == null) return;
        bool high = Random.value < 0.5f;            // al azar: alto (agacharse) o bajo (salto)
        _tailSweep.Sweep(this, high, _tailFromRightNext, contactDSP, sweepBeats);
        _tailFromRightNext = !_tailFromRightNext;   // intercala el lado (der ↔ izq)
    }

    // ── Fase 2: pilares + bolas de fuego + faceplant ────────────────────────────

    private bool _p2NewRoundPending;   // la cabeza acaba de volver: la ronda siguiente empieza en el próximo compás

    private void Phase2Beat(int beat)
    {
        // Tras el spike/cabeza, la ronda nueva arranca en el siguiente COMPÁS (múltiplo de 4) una vez
        // recuperado. Antes el contador seguía corriendo durante la secuencia de la cabeza y el rel 0 caía
        // en mitad de ella → los fireballs (que van al principio) se saltaban siempre.
        if (_p2NewRoundPending)
        {
            if (_p2sub != Phase2Sub.Attacking || beat % 4 != 0) return;
            _phase2StartBeat   = beat;
            _p2NewRoundPending = false;
        }

        int since = beat - _phase2StartBeat;
        if (since < 0) return;
        int rel = since % Phase2Length;   // (solo da la vuelta sola si no hay cabeza/spike)
        if (rel == 0) LockRoundDifficulty();          // nueva ronda → dificultad según el daño hecho en esta fase
        if (_p2sub != Phase2Sub.Attacking) return;   // durante spike/vuelo/pisado/recuperación no lanza ataques

        _p2Beat = beat;   // beat exacto que se procesa (referencia para programar a medio beat)
        Phase2Chart(rel);
    }

    // ── Estructura de la ronda de Fase 2 (en beats desde el inicio de la ronda) ──
    private const float FireSectionBeats = 7f;    // 1) bolas de fuego: rel 0-6
    private const int   WaveStartRel     = 7;     // 2) ondas: empiezan en rel 7…
    private const float WaveSectionBeats = 14f;   //    …y duran 14 beats (rel 7-20)
    private const int   ObstStartRel     = 22;    // 3) obstáculos: rel 22-28, apagándose
    private static readonly float[] ObstacleRamp = { 1f, 1f, 1f, 0.8f, 0.6f, 0.4f, 0.2f };
    private const int   SpikeRel         = 29;    // 4) spike JUSTO al acabar los obstáculos (parece su último pincho)
    private const int   Phase2Length     = 36;    // múltiplo de 4

    /// <summary>Ronda de Fase 2 EN ORDEN: 1) BOLAS DE FUEGO, 2) ONDAS (puños, varios patrones), 3) OBSTÁCULOS
    /// (pilares que se van apagando) y al terminar → el SPIKE sube y golpea la cabeza (ventana de daño).</summary>
    private void Phase2Chart(int rel)
    {
        bool waves = _enableShockwaveAttack && _rightFist != null && _leftFist != null;

        // 1) BOLAS DE FUEGO: ráfagas cada 'fireballInterval' beats durante FireSectionBeats.
        if (rel == 0) ScheduleFireballs();

        // 2) ONDAS: los puños estampan DELANTE del jefe con uno de los patrones (a la vez, alternos, al azar,
        //    rítmico) durante WaveSectionBeats. Las ondas se esquivan SALTANDO.
        if (rel == WaveStartRel && waves) ScheduleWaves();

        // 3) OBSTÁCULOS: ráfagas en X libre que se van APAGANDO hacia el final (y 2 beats despejados) para que
        //    se vea bien la cabeza salir volando.
        int oi = rel - ObstStartRel;
        if (oi >= 0 && oi < ObstacleRamp.Length) ObstacleBeat(ObstacleRamp[oi]);

        // 4) Fin de los obstáculos → el SPIKE sube y golpea la barbilla.
        if (rel == SpikeRel) TriggerHeadSpike();
    }
    private int _p2Beat;                             // beat absoluto que se está procesando en la Fase 2

    /// <summary>¿Sigue en pie el ataque programado? (misma fase y sin la cabeza fuera).</summary>
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

    // Manos: 0 = izquierda, 1 = derecha, 2 = las dos a la vez.
    private const int HandL = 0, HandR = 1, HandBoth = 2;

    /// <summary>
    /// Patrones RÍTMICOS del ataque de ondas: (mano, duración hasta el siguiente golpe en "medios pasos").
    /// Un medio paso = la mitad del 'waveInterval' de la dificultad (mínimo medio beat). Con intervalo 1:
    ///   síncopa = IZQ (beat) · DER (½) · DER (½) · …   galope = IZQ (½) · IZQ (½) · DER (beat) · …
    /// Cada vez se elige uno al azar y, a veces, en espejo (izq ↔ der).
    /// </summary>
    private static readonly (int hand, float halves)[][] WaveRhythms =
    {
        new[] { (HandL, 2f), (HandR, 1f), (HandR, 1f) },                                      // síncopa
        new[] { (HandL, 1f), (HandL, 1f), (HandR, 2f) },                                      // galope
        new[] { (HandL, 2f), (HandR, 1f), (HandR, 1f), (HandL, 1f), (HandR, 1f), (HandL, 2f) }, // redoble
        new[] { (HandL, 1f), (HandR, 1f), (HandL, 1f), (HandR, 1f), (HandBoth, 4f) },         // redoble + remate
    };
    private static readonly string[] WaveRhythmNames = { "síncopa", "galope", "redoble", "redoble con remate" };

    /// <summary>
    /// Programa el ataque de ondas de la ronda (7 beats) con uno de estos patrones, al azar:
    ///   • A LA VEZ (probabilidad 'bothTogetherChance'): los dos puños juntos cada 2 intervalos.
    ///   • ALTERNOS: der, izq, der, izq… cada 'waveInterval' beats.
    ///   • ALTERNOS AL AZAR: cada golpe con una mano al azar, cada 'waveInterval' beats.
    ///   • RÍTMICO: uno de los patrones de <see cref="WaveRhythms"/> (síncopa, galope, redoble…).
    /// Se programan los CONTACTOS (caen justo en el ritmo) y el aviso de cada puño se calcula para que el golpe
    /// anterior de esa misma mano ya haya caído y soltado su onda antes de volver a levantarse.
    /// </summary>
    private void ScheduleWaves()
    {
        Phase2Tier tier = P2Tier;
        float gap = Mathf.Max(0.5f, tier.waveInterval);

        // 1) Lista de golpes: (instante de CONTACTO relativo a la sección, mano). El primero a 1 beat (aviso completo).
        //    El ataque se parte en DOS MITADES y a mitad CAMBIA de patrón (la 2ª nunca repite el tipo de la 1ª).
        var hits = new System.Collections.Generic.List<(float t, int hand)>();
        float t = 1f;
        int kind1 = AddWavePattern(hits, ref t, WaveSectionBeats * 0.5f, gap, tier, -1,    out string name1);
        AddWavePattern(hits, ref t, WaveSectionBeats, gap, tier, kind1, out string name2);
        string pattern = $"{name1} → {name2}";

        // 2) Programar cada golpe. Aviso = como mucho 1 beat y nunca más que el tiempo desde el golpe anterior
        //    de ESA mano (así ya ha tocado el suelo y soltado su onda antes de levantarse otra vez).
        int   b     = _p2Beat;
        float lastL = 0f, lastR = 0f;
        float maxWu = Mathf.Max(0.5f, _fistWindupBeats);
        foreach (var hit in hits)
        {
            float prev = hit.hand == HandL ? lastL : hit.hand == HandR ? lastR : Mathf.Max(lastL, lastR);
            float wu   = Mathf.Min(maxWu, hit.t - prev);
            if (wu < 0.25f) continue;                                // demasiado pegado: se descarta (no pasa con los patrones actuales)

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

    // Tipos de patrón de ondas.
    private const int WaveTogether = 0, WaveAlternate = 1, WaveRandomAlt = 2, WaveRhythm = 3;

    /// <summary>
    /// Añade golpes a 'hits' desde 't' hasta 'end' con un patrón elegido al azar (distinto de 'excludeKind').
    /// Devuelve el tipo elegido y deja 't' en el siguiente golpe (así la 2ª mitad sigue el ritmo sin cortes).
    /// </summary>
    private int AddWavePattern(System.Collections.Generic.List<(float t, int hand)> hits, ref float t, float end,
                               float gap, Phase2Tier tier, int excludeKind, out string name)
    {
        // Elegir tipo: 'a la vez' con su probabilidad (si no está excluido); si no, uno de los otros tres.
        int kind;
        if (excludeKind != WaveTogether && Random.value < tier.bothTogetherChance) kind = WaveTogether;
        else
        {
            var options = new System.Collections.Generic.List<int> { WaveAlternate, WaveRandomAlt, WaveRhythm };
            options.Remove(excludeKind);
            kind = options[Random.Range(0, options.Count)];
        }

        float rhythmHalf = Mathf.Max(0.5f, gap * 0.5f);   // medio paso de los patrones rítmicos (nunca < ½ beat)

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

    /// <summary>Un puño estampa (no letal) DELANTE del jefe y suelta una onda expansiva. La onda mata; se salta.</summary>
    private void TriggerFistWave(bool right, double contactDSP)
    {
        BossFistSlam fist = right ? _rightFist : _leftFist;
        if (fist == null) return;
        fist.Slam(this, contactDSP, emitShockwave: true, lethal: false, trackPlayer: false,
                  offsetX: 0f, frontZ: _fistWaveFront, centerPull: _fistWaveCenterPull);
    }

    /// <summary>
    /// Un beat de obstáculos con una INTENSIDAD 0..1 (escala cuántos pilares salen). A intensidad plena se
    /// reparten más ráfagas DENTRO del beat, cada 'obstacleInterval' beats (0.5 = medio beat, 0.33 = tresillo);
    /// al final de la oleada solo sale una por beat y cada vez más flojas, para dejar ver la cabeza salir volando.
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

    /// <summary>Ráfaga de obstáculos: entre Min y Max pilares, en X LIBRE (Obstacle Min/Max X, no carriles),
    /// alto/bajo al azar y escalonados en Z para que no salgan encimados.</summary>
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
        float   lead    = walk * _pillarLeadBeats * BeatDuration + extraLead;   // telegraph + escalonado
        float   groundY = _playerCC != null ? _playerCC.bounds.min.y : PlayerT.position.y;

        Vector3 top = PlayerT.position + fwd * lead;
        top.x = worldX;
        top.y = groundY;

        BossPillar pillar = Instantiate(_pillarPrefab, top, Quaternion.identity);
        Vector3 s = pillar.transform.localScale;
        s.y = tall ? _pillarTallScaleY : _pillarShortScaleY;   // alto (carril) / corto (saltar)
        pillar.transform.localScale = s;
        pillar.Init(this, AudioSettings.dspTime);
    }

    /// <summary>Ráfaga de bolas de fuego: entre Min y Max en este beat, repartidas en X desde la boca.</summary>
    private void FireballVolley()
    {
        Phase2Tier t = P2Tier;
        int lo = Mathf.Max(1, Mathf.Min(t.fireballsMin, t.fireballsMax));
        int hi = Mathf.Max(lo, t.fireballsMax);
        int n  = Random.Range(lo, hi + 1);
        // Una vez en la Fase 2: una de las bolas de alguna ráfaga es la BOLA DE VIDA (verde): tócala para la vida.
        _p2Volleys++;
        int lifeIdx = !_p2LifeSpawned && (Random.value < 0.4f || _p2Volleys >= 3) ? Random.Range(0, n) : -1;
        for (int i = 0; i < n; i++)
        {
            // Repartidas a lo ancho (y un poco en altura) para que no salgan todas del mismo punto.
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
        fb.Init(this, _homingBeats, 1f);   // persigue _homingBeats y va recta 1 beat antes del impacto
        if (life) { fb.MakeLife(); _p2LifeSpawned = true; }
    }

    /// <summary>Fin de los obstáculos: el spike empieza a subir hacia la barbilla del jefe.</summary>
    private void TriggerHeadSpike()
    {
        if (_head == null) return;
        _p2sub = Phase2Sub.SpikeRising;
        _spikeStartDSP = AudioSettings.dspTime;
        _spikeEndDSP   = _spikeStartDSP + _spikeRiseBeats * BeatDuration;
        SpawnHeadSpike();   // cono generado por código bajo la barbilla
        if (_verbose) Debug.Log("[Boss] Spike subiendo hacia la barbilla.");
    }

    // ── Spike generado por código ──────────────────────────────────────────────

    private float _spikeRaise01;   // 0 = escondido bajo el suelo, 1 = tocando la cabeza
    private float _spikeChinDist;  // cuánto se adelanta el spike (hacia el jugador) respecto al centro de la cabeza

    /// <summary>XZ del spike: bajo la BARBILLA (cara de la cabeza que mira al jugador), no bajo el centro,
    /// que queda encima del torso y taparía el spike por completo.</summary>
    private Vector3 SpikeXZ()
    {
        Vector3 hp = HeadRestWorld;
        float dirZ = PlayerT != null ? Mathf.Sign(PlayerT.position.z - hp.z) : 1f;
        return new Vector3(hp.x, 0f, hp.z + dirZ * _spikeChinDist);
    }

    /// <summary>Posición de reposo de la cabeza en el mundo (sigue al jefe aunque la cabeza salga volando).</summary>
    private Vector3 HeadRestWorld =>
        (_headCaptured && _head.parent != null) ? _head.parent.TransformPoint(_headRestLocalPos) : _head.position;

    /// <summary>
    /// Crea el spike: un CONO generado por código (malla propia, pivote en la base, altura 1 que se escala a la
    /// altura suelo→barbilla). Antes se usaba el prefab del pilar, pero es una malla de ProBuilder que se genera
    /// en runtime: al instanciarlo sus bounds salían a 0 y la escala calculada se disparaba (spike invisible).
    /// </summary>
    private void SpawnHeadSpike()
    {
        if (_spikeObj != null) Destroy(_spikeObj.gameObject);

        // Bajo la barbilla: delante del centro de la cabeza (media profundidad de la cabeza + margen).
        Renderer headR = _head.GetComponentInChildren<Renderer>();
        Bounds hb = headR != null ? headR.bounds : new Bounds(HeadRestWorld, Vector3.one * 2f);
        _spikeChinDist = hb.extents.z * 0.8f + _spikeChinForward;

        Vector3 sp = SpikeXZ();
        _spikeGroundY = GroundYBelow(sp.x, sp.z, _lastGroundY);
        _spikeH = Mathf.Max(1f, hb.min.y - _spikeGroundY + 0.3f);   // +0.3: que "entre" un poco en la barbilla

        // Raíz con el pivote en la BASE del spike: es lo que se sube/baja. El modelo va de hijo.
        var root = new GameObject("BossHeadSpike");
        root.transform.position = new Vector3(sp.x, _spikeGroundY - _spikeH, sp.z);   // empieza escondido
        _spikeObj = root.transform;

        if (_headSpikePrefab != null)
        {
            // TU MODELO: se instancia de hijo y se mide (puede tardar un frame si genera la malla en runtime).
            GameObject model = Instantiate(_headSpikePrefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            foreach (Collider c in model.GetComponentsInChildren<Collider>()) c.enabled = false;  // decorativo
            _spikeModel    = model.transform;
            _spikeMeasured = MeasureSpikeModel();
        }
        else
        {
            // Sin prefab: cono generado por código (unitario Ø1 × alto 1, base en y=0) → la raíz escala la altura.
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
    /// Mide tu modelo a escala 1 y lo encaja: centra su X/Z en la raíz, apoya su BASE en y=0 de la raíz y escala
    /// la raíz para que mida justo _spikeH (suelo → barbilla). Devuelve false si aún no hay malla que medir
    /// (se reintenta cada frame); si pones Spike Model Height, usa ese valor en lugar de medir.
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
            if (r.bounds.size.sqrMagnitude < 1e-6f) continue;   // malla aún sin generar
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
            return false;   // todavía no se puede medir: se reintenta el próximo frame
        }

        float k = _spikeH / h;
        _spikeObj.localScale = _spikeUniformScale ? Vector3.one * k : new Vector3(1f, k, 1f);
        return true;
    }

    /// <summary>Coloca el spike según _spikeRaise01: su base va de −altura (escondido) a la superficie.</summary>
    private void PlaceSpike()
    {
        if (_spikeObj == null) return;
        Vector3 sp = SpikeXZ();
        float baseY = _spikeGroundY - _spikeH * (1f - _spikeRaise01);
        _spikeObj.position = new Vector3(sp.x, baseY, sp.z);   // pivote del cono = centro de la base
    }

    /// <summary>Malla de cono unitario: base de diámetro 1 en y=0 (centrada), punta en y=1.</summary>
    private static Mesh BuildConeMesh(int segments)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();

        // Laterales (vértices propios por cara para un sombreado facetado limpio).
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
        // Tapa inferior.
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

    /// <summary>Superficie de la plataforma en (x,z) por raycast, ignorando jefe, jugador, pilares y el spike.</summary>
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
            if (_spikeObj != null && t.IsChildOf(_spikeObj)) continue;             // el propio spike
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

    /// <summary>Fase 2 por frame: spike → la cabeza sale volando → vulnerable (Slam) → rebote → recupera.</summary>
    private void UpdatePhase2()
    {
        if (_state != BossState.Phase2 || _head == null || PlayerT == null) return;
        double now = AudioSettings.dspTime;

        // Altura del SUELO: pies del jugador SOLO cuando toca suelo (así no sube al saltar).
        if (_playerCC != null && _playerCC.isGrounded) _lastGroundY = _playerCC.bounds.min.y;

        AnimateSpike(now);

        switch (_p2sub)
        {
            case Phase2Sub.SpikeRising:
                if (now >= _spikeEndDSP) StartHeadFly(now);   // el spike chocó con la barbilla
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

    /// <summary>El spike sube en SpikeRising y se retrae en cualquier otro estado.</summary>
    private void AnimateSpike(double now)
    {
        // Sube hasta la barbilla en SpikeRising, se queda arriba un momento y luego se hunde en ~1 beat.
        if (_spikeObj == null) return;
        if (!_spikeMeasured) _spikeMeasured = MeasureSpikeModel();   // tu modelo aún generando su malla
        if (_p2sub == Phase2Sub.SpikeRising)
        {
            float u = _spikeEndDSP > _spikeStartDSP
                ? Mathf.Clamp01((float)((now - _spikeStartDSP) / (_spikeEndDSP - _spikeStartDSP)))
                : 1f;
            _spikeRaise01 = u * u * u;   // ease-IN: arranca lento y golpea rápido (se siente el impacto)
        }
        else if (now < _spikeEndDSP + _spikeHoldBeats * BeatDuration)
        {
            _spikeRaise01 = 1f;          // se queda ARRIBA tras el golpe (se lee que ha lanzado la cabeza)
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
    private Renderer _headRenderer;   // caja de la cabeza (para aplastar al jugador si le cae encima)
    private bool     _headCrushDone;

    /// <summary>La cabeza sale volando (con arco) hacia UNO DE LOS 3 CARRILES al azar.</summary>
    private void StartHeadFly(double now)
    {
        _p2sub = Phase2Sub.HeadFlying;
        _headFlyStartDSP  = now;
        _headFlyEndDSP    = now + _headFlyBeats * BeatDuration;
        _headFlyFromWorld = _head.position;
        _headLaneX        = (_laneX != null && _laneX.Length > 0) ? _laneX[Random.Range(0, _laneX.Length)] : 0f;
        _headCrushDone    = false;
        // Tutorial: «SALTA + S» encima de la cabeza desde que sale volando y mientras está en el suelo (las primeras veces).
        _slamHintsShown++;
        if (_head.GetComponent<TutorialHint>() == null)
        {
            TutorialHint h = TutorialHint.Attach(_head, "SALTA + S", "sobre la cabeza", 0.8f, 8f,
                () => (_p2sub == Phase2Sub.HeadFlying || _p2sub == Phase2Sub.Vulnerable) && _slamHintsShown <= TutorialHintsPerAttack);
            if (h != null) { h.color = Color.black; h.outlineColor = new Color(1f, 1f, 1f, 0.85f); }   // (este, en negro)
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

        // Si te cae ENCIMA (tramo final de la caída) → aplastado. Esquívalo cambiando de carril al verla venir.
        if (u >= 0.6f) CheckHeadCrush();

        if (u >= 1f)
        {
            CheckHeadCrush();
            // Al estrellarse contra el suelo suelta una ONDA EXPANSIVA (hay que saltarla).
            float gy = GroundYBelow(_head.position.x, _head.position.z, _lastGroundY);
            BossShockwave.Spawn(this, new Vector3(_head.position.x, gy, _head.position.z));

            _p2sub = Phase2Sub.Vulnerable;
            _vulnEndDSP = now + _vulnBeats * BeatDuration;

        }
    }

    /// <summary>La cabeza aplasta al jugador si su caja le toca mientras cae (una sola vez por caída).</summary>
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
        // Cabeza en SU CARRIL, a ras de SUELO (no sube al saltar) y a la Z del jugador.
        _head.position = new Vector3(_headLaneX, _lastGroundY + _headGroundOffset, PlayerT.position.z);

        if (_pm != null && _pm.IsSlamming)
        {
            Vector3 d = _head.position - PlayerT.position; d.y = 0f;
            if (d.magnitude <= _headHitRadius)
            {
                TakeDamage(1);
                _pm.Bounce(_playerBounceForce);   // rebota el JUGADOR; la cabeza vuelve al jefe
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
            _p2NewRoundPending = true;   // la siguiente ronda (empezando por los fireballs) arranca en el próximo compás
        }
    }

    // ── Daño al jefe ───────────────────────────────────────────────────────────

    /// <summary>Aplica daño al jefe. Lo llaman los proyectiles deflectados (y en fases futuras, otras mecánicas).</summary>
    public void TakeDamage(int amount = 1)
    {
        if (_state == BossState.Defeated || amount <= 0) return;

        _hp = Mathf.Max(0, _hp - amount);
        _hitsThisPhase += amount;
        OnHpChanged?.Invoke(_maxHp > 0 ? _hp / (float)_maxHp : 0f);
        OnBossHit?.Invoke();
        if (_verbose) Debug.Log($"[Boss] ¡Golpe! HP {_hp}/{_maxHp} (hits fase: {_hitsThisPhase}).");

        if (_hp <= 0) { Defeat(); return; }

        // ¿Fase superada? Programa el cambio en el siguiente múltiplo de 4. (La Fase 3 es la última: su final
        // lo gestiona ella misma al destruir la cabeza.)
        if (_state != BossState.Phase3 && _hitsThisPhase >= _hitsPerPhase)
        {
            int cur = CurrentBeat();
            _phaseChangeBeat = cur - (cur % 4) + 4;   // siguiente downbeat
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
        CleanupPhase3();   // quita escudo, BloodSuckers y dianas que quedaran
        if (_verbose) Debug.Log("[Boss] Derrotado.");
        OnDefeated?.Invoke();
        BeginVictory();    // BossControllerPhase3.cs: sale terreno de la lava, se rompe el kayak, END al acabar la canción
    }

    // ── Info para el jugador (hit) ─────────────────────────────────────────────

    /// <summary>Un ataque (proyectil) acierta al jugador: mismo golpe que cualquier otro ataque del jefe.</summary>
    public void HitPlayer() => KillPlayer();

    private float _invulnerableUntil = -1f;

    /// <summary>
    /// Ataque FUERTE que no se ha esquivado: ya NO es game over directo; quita mucha sangre (_heavyHitBlood) y da un
    /// momento de invulnerabilidad. Si la jeringuilla se llena, se muere por sangre (vidas extra / Adrenalina /
    /// checkpoint funcionan con normalidad).
    /// </summary>
    public void KillPlayer()
    {
        if (Time.time < _invulnerableUntil) return;
        if (_gameOver != null && _gameOver.IsDead) return;
        _invulnerableUntil = Time.time + _hitInvulnerability;
        if (EnergyManager.Instance != null) EnergyManager.Instance.TakeBossHit(_bossHitRedFraction);
        CameraShaker.Instance?.Shake(0.55f);
        if (_verbose) Debug.Log($"[Boss] Golpe al jugador: fuera el azul, o +{_bossHitRedFraction * 100f:0}% de jeringuilla roja.");
    }
}
