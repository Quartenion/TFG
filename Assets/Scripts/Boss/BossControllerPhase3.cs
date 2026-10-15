using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FASE 3 del jefe — Persecución en la lava (parte de <see cref="BossController"/>).
///
/// TRANSICIÓN (≈7 beats):
///   0-2  El jefe PIERDE EL EQUILIBRIO (se tambalea cada vez más).
///   2    Intenta AGARRARSE: estampa los dos puños contra la plataforma de delante.
///   2.5  La plataforma tiembla y aparece un ATAÚD-KAYAK (prefab Coffin) YA FLOTANDO en la lava justo en
///        la Z donde caerá el jugador (predicción con su velocidad y la gravedad).
///   4    El suelo SE HUNDE: jugador y jefe caen a la vez; el jugador aterriza en el kayak. El jefe RASCA la
///        cara de la plataforma de arriba a abajo con zarpazos alternos (dejando surcos) hasta la lava.
///   4-7  El jefe se HUNDE en la lava (sin tapar nunca brazos ni cabeza) y deja de tambalearse.
///
/// CICLOS (3): cada uno con la dificultad de su ronda (fácil → media → difícil). Con ESCUDO, uno a uno:
///   1. PINCHOS en la lava (capa Obstacle, solo la cara frontal es letal): altos → carril, bajos → saltar.
///   2. TRIDENTE LANZADO (mismo arco y velocidad que el barrido, suelto y girando; la otra mano lo coge) → saltar.
///   3. BLOODSUCKERS que CHUPAN SANGRE de verdad. Vuelan con el jefe; se eliminan MIRÁNDOLOS.
///   4. Muertos todos → cae el escudo y se marca la parte a mirar (brazo izq → brazo der → cabeza).
///   5. Mirarla 'Gaze Hold Beats' → 1 golpe:
///        · 1ª mano → se rebela y le da 4 puñetazos en la cara (por su lado, en el beat) y vuelve a su sitio;
///        · 2ª mano → los DOS puños se intercalan pegándole en la cara (6 golpes, uno por beat);
///   (los BloodSuckers, al mirarlos, se giran contra el jefe y le chupan la sangre; con todos girados cae el escudo)
///        · cabeza → las dos manos la agarran y la hunden en la lava → derrotado.
/// FINAL: sale terreno de la lava, el kayak se rompe (el jugador sale despedido y cae en él); el loop del jefe
///   NO se corta (termina su vuelta), luego LDNM-END y, al acabar la canción entera, EMERGE el END.
/// </summary>
public partial class BossController
{
    // ── Configuración ────────────────────────────────────────────────────────

    [System.Serializable]
    public class Phase3Tier
    {
        [Tooltip("BloodSuckers de la oleada (siempre IMPAR: si pones un número par se añade uno).")]
        [Range(1, 10)] public int bloodSuckers = 3;
        [Tooltip("Pinchos de lava por ráfaga (mín/máx).")]
        public int spikesMin = 1, spikesMax = 2;
        [Tooltip("Beats entre ráfagas de pinchos.")]
        [Range(0.5f, 4f)] public float spikeInterval = 2f;
        [Tooltip("Probabilidad de pincho ALTO (cambiar de carril). El resto son bajos (saltar).")]
        [Range(0f, 1f)] public float tallChance = 0.7f;
        [Tooltip("Beats que dura la TANDA de pinchos (1er ataque del ciclo).")]
        [Range(2f, 32f)] public float spikeSectionBeats = 8f;
        [Tooltip("Lanzamientos de TRIDENTE (2º ataque del ciclo), uno detrás de otro. 0 = ninguno.")]
        [Range(0, 5)] public int tridentThrows = 1;

        public Phase3Tier() { }
        public Phase3Tier(int suckers, int sMin, int sMax, float sInt, float tall, float section, int throws)
        {
            bloodSuckers = suckers; spikesMin = sMin; spikesMax = sMax; spikeInterval = sInt; tallChance = tall;
            spikeSectionBeats = section; tridentThrows = throws;
        }
    }

    [Header("Fase 3 — tridente lanzado")]
    [Tooltip("Altura del paso respecto al TORSO del jugador sentado en el kayak (0 = a la altura del torso).")]
    [SerializeField] private float _throwPassHeightOffset = 0f;

    [Header("Fase 3 — caída con el suelo")]
    [Tooltip("Gravedad (m/s²) del jugador y de los bloques del suelo mientras caen juntos a la lava.")]
    [SerializeField] private float _p3FallGravity = 28f;

    [Header("Fase 3 — kayak en la lava")]
    [Tooltip("Prefab del ataúd-kayak (Coffin, con KayakController). Aparece bajo el jugador al romperse el suelo.")]
    [SerializeField] private KayakController _kayakPrefab;
    [Tooltip("Generador de plataformas del jefe (se derrumba al empezar la fase). Vacío = se busca solo.")]
    [SerializeField] private BossPlatformSpawner _platformSpawner;
    [Tooltip("Prefab del BloodSucker (BloodSucker + GazeSwitch + Collider). Vacío = se genera uno sencillo por código.")]
    [SerializeField] private GameObject _bloodSuckerPrefab;
    [Tooltip("Partes a mirar EN ORDEN. Vacío = puño izquierdo, puño derecho y cabeza.")]
    [SerializeField] private Transform[] _phase3Targets;
    [Tooltip("GazeSwitch a mirar EN ORDEN: [0] mano izquierda, [1] mano derecha, [2] cabeza. Ponlos tú en la\n" +
             "escena (con su Collider NO trigger); el jefe los tiene apagados y activa solo el que toca.\n" +
             "Si alguno está vacío, se crea uno automático sobre la parte.")]
    [SerializeField] private GazeSwitch[] _phase3GazeSwitches = new GazeSwitch[3];
    [Tooltip("Beats que hay que mantener la mirada en la parte para destruirla.")]
    [SerializeField] private float _gazeHoldBeats = 2f;
    [Tooltip("Beats de mirada para eliminar un BloodSucker (también los de prefab). 1 = justo un beat.")]
    [SerializeField] private float _suckerGazeBeats = 1f;
    [Tooltip("Distancia máxima de la mirada (el jefe va lejos, por delante).")]
    [SerializeField] private float _gazeMaxDistance = 160f;
    [Tooltip("Metros máximos que se hunde el jefe en la lava.")]
    [SerializeField] private float _phase3SinkDepth = 12f;
    [Tooltip("Margen (m) que mantienen brazos y cabeza por encima de la lava al hundirse.")]
    [SerializeField] private float _phase3PartsAboveLava = 3f;
    [Tooltip("Color del escudo.")]
    [SerializeField] private Color _shieldColor = new Color(0.3f, 0.8f, 1f, 0.22f);
    [Tooltip("Color de la diana que marca la parte a mirar.")]
    [SerializeField] private Color _targetMarkerColor = new Color(1f, 0.9f, 0.2f, 0.9f);
    [Tooltip("Zona de vuelo de los BloodSuckers: ± ancho en X alrededor del jefe.")]
    [SerializeField] private float _suckerAreaHalfWidth = 26f;
    [Tooltip("Altura sobre la lava (mín/máx) de los BloodSuckers.")]
    [SerializeField] private Vector2 _suckerAreaHeight = new Vector2(4f, 20f);
    [Tooltip("Distancia por delante del jefe, HACIA el jugador (mín/máx), de los BloodSuckers.")]
    [SerializeField] private Vector2 _suckerAreaFront = new Vector2(6f, 26f);
    [Tooltip("Separación mínima que se intenta dejar entre BloodSuckers (m).")]
    [SerializeField] private float _suckerMinSpacing = 7f;

    [Header("Fase 3 — dificultad por ciclo — [0] Fácil · [1] Media · [2] Difícil")]
    [SerializeField] private Phase3Tier[] _phase3Tiers =
    {
        //             bichos · pinchos (mín, máx, cada) · % altos · beats de pinchos · lanzamientos
        new Phase3Tier(5,        1, 2, 2f,               0.7f,     8f,                1),   // fácil  (brazo izq)
        new Phase3Tier(7,        2, 3, 1f,               0.7f,     8f,                2),   // media  (brazo der)
        new Phase3Tier(9,        2, 4, 1f,               0.6f,     10f,               3),   // difícil (cabeza)
    };

    // ── Estado ──────────────────────────────────────────────────────────────

    // Ciclo: Attacking (pinchos → tridente) → Suckers (BloodSuckers que chupan) → Exposed (mirar) → PartBreaking
    private enum Phase3Sub { Transition, Attacking, Suckers, Exposed, PartBreaking, Finished }
    private Phase3Sub _p3sub = Phase3Sub.Transition;

    // Tiempos de la transición (en beats desde el inicio de la fase).
    private const float P3WobbleEnd     = 2f;    // se tambalea cada vez más hasta aquí
    private const float P3GrabBeat      = 2f;    // se AGARRA al borde de la plataforma con los dos puños
    private const float P3CrumbleBeat   = 2.5f;  // la plataforma empieza a DESMORONARSE (tiembla)…
    private const float P3ShakeBeats    = 1.5f;  //   …durante 1.5 beats
    private const float P3FallBeat      = 4f;    // se rompe: todo CAE (suelo, jefe agarrado y jugador → kayak)
    private const float P3PlatformFall  = 4f;    // beats que tarda el suelo en hundirse (lento: da tiempo a rascarlo)
    private const float P3ReleaseBeat   = 6.5f;  // ya en la lava: suelta el borde (puños a su sitio)
    private const float P3SinkEnd       = 7f;    // termina de caer/hundirse
    private const float P3CycleStart    = 8f;    // empieza el primer ciclo
    private const float P3WobbleDeg     = 12f;
    private const float P3HangMaxDrop   = 4f;    // lo que se hunde colgado ANTES de que se rompa el suelo (m)

    private double     _p3Start;
    private bool       _p3CrumbleDone, _p3BreakDone, _p3Released;
    private float      _p3GroundY;               // superficie de la plataforma (donde se agarra)
    private float      _p3SinkOffset;            // metros hundido (lo aplica ApplyClimb)
    private float      _p3SinkTarget;
    private Quaternion _p3BodyRestRot;
    private int        _p3Cycle;
    private double     _p3BreakStart;
    private Transform  _p3Part;
    private Vector3    _p3PartScale0;
    private double     _p3NextSpikeDSP;
    private float      _p3LavaY;
    private bool       _p3LavaFound;
    private KayakController _p3Kayak;
    private BossLifeOrb     _p3LifeOrb;   // orbe de vida (sustituye a un BloodSucker, una vez en la fase)

    private readonly List<BloodSucker> _p3Suckers       = new List<BloodSucker>();
    private readonly List<Vector3>     _p3SuckerOffsets = new List<Vector3>();
    private GameObject _p3Shield;
    private GazeSwitch _p3TargetGaze;

    private Phase3Tier P3Tier => (_phase3Tiers != null && _phase3Tiers.Length > 0)
        ? _phase3Tiers[Mathf.Clamp(_roundDifficulty, 0, _phase3Tiers.Length - 1)]
        : new Phase3Tier(3, 1, 2, 2f, 0.7f, 8f, 1);

    // ── Entrada ─────────────────────────────────────────────────────────────

    private void EnterPhase3()
    {
        _p3sub       = Phase3Sub.Transition;
        _p3Start     = AudioSettings.dspTime;
        _p3CrumbleDone = _p3BreakDone = _p3Released = false;
        _rightFistGrabLogged = false;
        _p3Cycle     = 0;
        _p3SinkOffset = 0f;
        _p3SeatOffset = float.MaxValue;

        // Restos de la Fase 2: cabeza a su sitio y fuera el spike.
        if (_head != null && _headCaptured)
        {
            _head.localPosition = _headRestLocalPos;
            _head.localRotation = _headRestLocalRot;
        }
        if (_spikeObj != null) { Destroy(_spikeObj.gameObject); _spikeObj = null; }
        _p2sub = Phase2Sub.Attacking;

        if (_platformSpawner == null) _platformSpawner = FindAnyObjectByType<BossPlatformSpawner>();
        _p3BodyRestRot = BossT.localRotation;

        // La mirada solo cuenta si lo PRIMERO que toca el rayo es la parte objetivo: se apagan el resto de
        // colliders del jefe (p. ej. HitBoss, que ya no hace falta) para que no tapen brazos ni cabeza.
        Transform[] targets = P3Targets();
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            bool isTarget = false;
            foreach (Transform t in targets)
                if (t != null && c.transform.IsChildOf(t)) { isTarget = true; break; }
            if (_phase3GazeSwitches != null)
                foreach (GazeSwitch g in _phase3GazeSwitches)
                    if (g != null && c.transform.IsChildOf(g.transform)) { isTarget = true; break; }
            if (!isTarget) c.enabled = false;
        }

        // Los GazeSwitch de la fase, APAGADOS hasta que les toque.
        if (_phase3GazeSwitches != null)
            foreach (GazeSwitch g in _phase3GazeSwitches)
                if (g != null) { g.ResetSwitch(); g.enabled = false; }

        // ¿Cuánto BAJA el jefe? Primero lo MISMO que cae el jugador (de la plataforma a la lava) —esto faltaba:
        // antes solo se calculaba el hundimiento extra y, con los puños a ras de suelo, salía ~0 → no bajaba—,
        // y además se HUNDE un poco en la lava, sin tapar nunca brazos ni cabeza.
        _p3GroundY = (_playerCC != null && _playerCC.enabled) ? _playerCC.bounds.min.y : _lastGroundY;
        float lavaY  = LavaY();
        float fall   = Mathf.Max(0f, _p3GroundY - lavaY);
        float lowest = float.MaxValue;
        foreach (Transform part in P3Targets())
        {
            if (part == null) continue;
            Renderer r = part.GetComponentInChildren<Renderer>();
            float y = r != null ? r.bounds.min.y : part.position.y;
            lowest = Mathf.Min(lowest, y);
        }
        float partsAboveGround = lowest < float.MaxValue ? lowest - _p3GroundY : _phase3SinkDepth;
        float submerge = Mathf.Clamp(partsAboveGround - _phase3PartsAboveLava, 0f, _phase3SinkDepth);
        _p3SinkTarget = fall + submerge;

        if (_verbose) Debug.Log($"[Boss] FASE 3: pierde el equilibrio… (suelo Y={_p3GroundY:F1}, lava Y={lavaY:F1} → " +
                                $"baja {fall:F1} m hasta la lava + se hunde {submerge:F1} m).");
    }

    // ── Bucle por frame ─────────────────────────────────────────────────────

    private void UpdatePhase3()
    {
        if (_state != BossState.Phase3) return;
        double now = AudioSettings.dspTime;
        float  tb  = (float)((now - _p3Start) / BeatDuration);
        TrackSeatHeight();

        switch (_p3sub)
        {
            case Phase3Sub.Transition:
                UpdateP3Transition(tb);
                break;

            case Phase3Sub.Attacking:
                UpdateP3Shield(now);
                UpdateP3Attacks(now);   // UN ataque cada vez: pinchos → tridente(s) → BloodSuckers
                break;

            case Phase3Sub.Suckers:
                UpdateP3Shield(now);
                UpdateP3SpawnQueue();   // van saliendo de uno en uno, uno por beat
                UpdateP3Suckers();
                if (AllSuckersTurned()) ExposeP3Part();   // todos girados contra el jefe → cae el escudo
                break;

            case Phase3Sub.Exposed:
            case Phase3Sub.PartBreaking:
                UpdateP3Suckers();   // siguen con el jefe, chupándole la sangre
                break;
        }

        // (2º paso del mismo frame: lo propio de cada estado)
        switch (_p3sub)
        {
            case Phase3Sub.Exposed:
                UpdateP3Marker(now);
                DebugP3Gaze();
                break;

            case Phase3Sub.PartBreaking:
                UpdateP3PartBreak(now);   // mientras se rompe la parte, no ataca
                break;
        }
    }

    // ── Transición: tambaleo → agarre → derrumbe + kayak → hundirse ─────────

    private void UpdateP3Transition(float tb)
    {
        // Tambaleo: crece hasta P3WobbleEnd y luego se va apagando mientras cae.
        float amp = tb < P3WobbleEnd
            ? P3WobbleDeg * Mathf.SmoothStep(0f, 1f, tb / P3WobbleEnd)
            : P3WobbleDeg * Mathf.Clamp01(1f - (tb - P3WobbleEnd) / (P3SinkEnd - P3WobbleEnd));
        float roll = Mathf.Sin(tb * Mathf.PI * 2f) * amp;   // un vaivén por beat
        BossT.localRotation = _p3BodyRestRot * Quaternion.Euler(0f, 0f, roll);

        // 1) Se AGARRA al borde con los dos puños (y los mantiene ahí, bajando con el borde cuando cae).
        if (tb >= P3GrabBeat && !_p3Released)
        {
            if (tb < P3GrabBeat + 0.05f && _verbose && _rightFist != null && !_rightFistGrabLogged)
            { _rightFistGrabLogged = true; Debug.Log("[Boss] Se agarra al borde de la plataforma…"); }
            HoldP3Edge(tb);
        }

        // 2) La plataforma se DESMORONA (tiembla) y a P3FallBeat se rompe y cae a la lava. En ese momento el
        //    kayak ya está FLOTANDO en la lava, justo donde va a caer el jugador.
        if (!_p3CrumbleDone && tb >= P3CrumbleBeat)
        {
            _p3CrumbleDone = true;
            // El jugador cae con la MISMA gravedad que los bloques del suelo (y el kayak se predice con ella).
            if (_pm != null) _pm.SetGravityOverride(_p3FallGravity);
            if (_platformSpawner != null)
            {
                _platformSpawner.SetShardGravity(_p3FallGravity);
                _platformSpawner.Collapse(P3PlatformFall, 220f, P3ShakeBeats, 0.35f);
            }
            SpawnFloatingKayak((P3FallBeat - tb) * BeatDuration);
            if (_verbose) Debug.Log("[Boss] La plataforma se desmorona…");
        }

        // 3) Se rompe: el jefe y el jugador caen A LA VEZ (el jugador, en caída libre, al kayak).
        if (!_p3BreakDone && tb >= P3FallBeat)
        {
            _p3BreakDone = true;
            if (_verbose) Debug.Log("[Boss] ¡Se rompe! El jefe cae a la lava arrastrando la plataforma.");
        }
        if (_p3BreakDone) CatchPlayerWithKayak();
        // Ya en el kayak (o terminada la transición): gravedad normal otra vez.
        if (_pm != null && _p3BreakDone && (_pm.IsOnKayak || tb >= P3CycleStart)) _pm.SetGravityOverride(0f);

        // 4) Ya en la lava: suelta el borde y los puños vuelven a su sitio.
        if (!_p3Released && tb >= P3ReleaseBeat)
        {
            _p3Released = true;
            StopScrapeTrails();
            if (_rightFist != null) _rightFist.ManualRelease(BeatDuration, 1.5f);
            if (_leftFist  != null) _leftFist.ManualRelease(BeatDuration, 1.5f);
        }

        // Caída del cuerpo: colgado se hunde un poco mientras el suelo se desmorona; al romperse cae con él
        // hasta la lava (lo mismo que el jugador) y un poco más.
        float hang = _p3SinkTarget > 0.01f ? Mathf.Min(0.3f, P3HangMaxDrop / _p3SinkTarget) : 0f;
        float s;
        if (tb < P3CrumbleBeat)   s = 0f;
        else if (tb < P3FallBeat) s = hang * Mathf.SmoothStep(0f, 1f, (tb - P3CrumbleBeat) / (P3FallBeat - P3CrumbleBeat));
        else                      s = hang + (1f - hang) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((tb - P3FallBeat) / (P3SinkEnd - P3FallBeat)));
        _p3SinkOffset = _p3SinkTarget * s;

        if (tb >= P3CycleStart)
        {
            BossT.localRotation = _p3BodyRestRot;
            StartP3Cycle(0);
        }
    }

    private bool _rightFistGrabLogged;

    // Rascado de la plataforma al caer
    private const float P3ScrapeStrokeBeats = 0.5f;   // un zarpazo por mano cada medio beat (alternas)
    private const float P3ScrapeReach       = 3f;     // metros que sube la mano en cada zarpazo
    private readonly List<GameObject> _p3ScrapeTrails = new List<GameObject>();

    /// <summary>
    /// Los puños en el BORDE de la plataforma de delante (hacia el jugador):
    ///   • Agarre → derrumbe: se aferran arriba, temblando con el suelo.
    ///   • Al romperse: el jefe se CAE y RASCA la cara de la plataforma DE ARRIBA A ABAJO con zarpazos
    ///     alternos (cada mano sube rápido y arrastra hacia abajo), dejando surcos, hasta llegar a la lava.
    /// </summary>
    private void HoldP3Edge(float tb)
    {
        Vector3 bp   = transform.position;
        float   dirZ = PlayerT != null ? Mathf.Sign(PlayerT.position.z - bp.z) : 1f;
        float   lava = LavaY();

        bool falling = tb >= P3FallBeat;
        if (falling && _p3ScrapeTrails.Count == 0) StartScrapeTrails();

        int i = 0;
        foreach (BossFistSlam fist in new[] { _rightFist, _leftFist })
        {
            i++;
            if (fist == null || !fist.gameObject.activeInHierarchy) continue;
            float half   = fist.HalfHeight;
            float top    = _p3GroundY + half;               // apoyado en el borde
            float bottom = lava + half * 0.3f;              // ya en la lava
            float x = Mathf.Lerp(fist.RestWorldPosition.x, bp.x, _fistWaveCenterPull);
            float z = bp.z + dirZ * _fistWaveFront;
            float y;

            if (!falling)
            {
                // Aferrado arriba; tiembla con el suelo que se desmorona.
                float shake = tb >= P3CrumbleBeat ? 0.25f * Mathf.Sin((float)AudioSettings.dspTime * 40f + i) : 0f;
                y = top + shake;
            }
            else
            {
                // Baja de ARRIBA A ABAJO a la vez que cae el cuerpo…
                float f = Mathf.Clamp01((tb - P3FallBeat) / (P3ReleaseBeat - P3FallBeat));
                f = f * f * (3f - 2f * f);
                float baseY = Mathf.Lerp(top, bottom, f);

                // …a ZARPAZOS: sube rápido (30 %) y arrastra hacia abajo (70 %). Las manos van alternas.
                float ph = (tb - P3FallBeat) / P3ScrapeStrokeBeats + (i == 1 ? 0f : 0.5f);
                float p  = ph - Mathf.Floor(ph);
                float stroke = p < 0.3f ? p / 0.3f : 1f - (p - 0.3f) / 0.7f;
                float rise   = p < 0.3f ? Mathf.Sin(Mathf.PI * p / 0.3f) : 0f;
                y = Mathf.Max(bottom, baseY + stroke * P3ScrapeReach * (1f - 0.6f * f));
                z -= dirZ * 1.2f * rise;                      // al subir se separa un poco; al bajar, clava
            }
            fist.ManualHold(new Vector3(x, y, z), falling ? 30f : 12f);
        }
    }

    /// <summary>Surcos/chispas que dejan las manos al rascar (TrailRenderer temporal en cada puño).</summary>
    private void StartScrapeTrails()
    {
        foreach (BossFistSlam fist in new[] { _rightFist, _leftFist })
        {
            if (fist == null) continue;
            var go = new GameObject("ScrapeTrail");
            go.transform.SetParent(fist.transform, false);
            // En la cara de la mano que roza la plataforma (abajo-delante).
            go.transform.localPosition = Vector3.zero;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.6f;
            tr.minVertexDistance = 0.2f;
            tr.startWidth = Mathf.Max(0.6f, fist.HalfHeight * 0.8f);
            tr.endWidth   = 0f;
            tr.material   = new Material(Shader.Find("Sprites/Default"));
            tr.startColor = new Color(1f, 0.55f, 0.15f, 0.9f);   // chispas
            tr.endColor   = new Color(0.25f, 0.2f, 0.2f, 0f);    // polvo
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _p3ScrapeTrails.Add(go);
        }
    }

    private void StopScrapeTrails()
    {
        foreach (GameObject go in _p3ScrapeTrails) if (go != null) Destroy(go, 0.6f);
        _p3ScrapeTrails.Clear();
    }

    /// <summary>
    /// Pone el ataúd-kayak YA TUMBADO Y FLOTANDO en la lava, en el punto EXACTO donde caerá el jugador:
    ///   Z = donde está + lo que avanza corriendo hasta que se rompe el suelo + lo que avanza mientras cae
    ///       (tiempo de caída libre = √(2·altura / gravedad del jugador)).
    ///   X = la del jugador. Orientado hacia donde avanza.
    /// El jugador cae libre (no controla nada en el aire) y, al entrar en su caja de activación, queda sentado.
    /// </summary>
    private void SpawnFloatingKayak(float secondsUntilFall)
    {
        if (_kayakPrefab == null || PlayerT == null)
        {
            Debug.LogWarning("[Boss] Fase 3: no hay Kayak Prefab (Coffin) asignado: el jugador caerá a la lava.", this);
            return;
        }

        Vector3 run  = _pm != null ? _pm.RunForward : Vector3.forward; run.y = 0f;
        run = run.sqrMagnitude > 0.001f ? run.normalized : Vector3.forward;
        float   walk = _pm != null ? _pm.walkSpeed : 12f;
        float   g    = _pm != null ? Mathf.Max(1f, _pm.CurrentGravity) : 30f;

        float kayakY = LavaY() + _kayakPrefab.floatHeight;
        float height = Mathf.Max(0f, PlayerT.position.y - kayakY);            // pivote del jugador → kayak
        float tFall  = Mathf.Sqrt(2f * height / g);
        Vector3 land = PlayerT.position + run * (walk * (Mathf.Max(0f, secondsUntilFall) + tFall));
        land.y = kayakY;

        // Orientación TUMBADA: la misma que tendría tras volcarse (yaw hacia donde avanza + giro de volcado).
        float yaw = Mathf.Atan2(run.x, run.z) * Mathf.Rad2Deg;
        Quaternion flat = Quaternion.Euler(0f, yaw, 0f) *
                          Quaternion.AngleAxis(_kayakPrefab.tiltAngle, _kayakPrefab.tiltAxis.normalized);

        _p3Kayak = Instantiate(_kayakPrefab, land, flat);
        _p3Kayak.name             = "BossKayak";
        _p3Kayak.startHorizontal  = true;                          // ya está tumbado: al entrar, flota directamente
        _p3Kayak.activationBoxSize = new Vector3(6f, 8f, 8f);      // caja amplia: recoge al jugador que cae
        if (_p3Kayak.gameOverManager == null) _p3Kayak.gameOverManager = _gameOver;

        if (_verbose) Debug.Log($"[Boss] Kayak flotando en la lava donde caerá el jugador (caída {tFall:F2} s, Z={land.z:F1}).");
    }

    /// <summary>
    /// Red de seguridad mientras cae: si el jugador llega casi a la lava sin haber entrado en el kayak (la
    /// predicción falló por poco), el kayak se coloca bajo él para recogerlo en vez de dejarlo morir.
    /// </summary>
    private void CatchPlayerWithKayak()
    {
        if (_p3Kayak == null || PlayerT == null || _pm == null || _pm.IsOnKayak) return;
        Vector3 k = _p3Kayak.transform.position;
        if (PlayerT.position.y > k.y + 3f) return;   // aún cae: el kayak espera donde está
        _p3Kayak.transform.position = new Vector3(PlayerT.position.x, k.y, PlayerT.position.z);
    }

    // ── Ciclos: escudo + BloodSuckers → parte expuesta → golpe ──────────────

    private void StartP3Cycle(int cycle)
    {
        _p3Cycle = cycle;
        LockRoundDifficulty();   // ciclo 1 → fácil, 2 → media, 3 → difícil (según golpes de esta fase)
        Phase3Tier t = P3Tier;

        ClearP3Suckers();   // los de la oleada anterior (que chupaban al jefe) se desintegran
        ShieldOn();
        // Flujo del ciclo: 1 beat de respiro → PINCHOS → TRIDENTE(S) → BLOODSUCKERS → parte expuesta.
        _p3Step       = P3Step.Intro;
        _p3StepEnd    = AudioSettings.dspTime + BeatDuration;
        _p3ThrowsLeft = Mathf.Max(0, t.tridentThrows);
        _p3sub = Phase3Sub.Attacking;

        if (_verbose) Debug.Log($"[Boss] F3 ciclo {cycle + 1}: escudo → pinchos → {_p3ThrowsLeft} tridente(s) → " +
                                $"{t.bloodSuckers} BloodSuckers (objetivo: {P3TargetName(cycle)}).");
    }

    /// <summary>Tercer paso del ciclo: la oleada de BloodSuckers (chupan sangre de verdad).</summary>
    private void StartP3SuckerWave()
    {
        SpawnP3Suckers(P3Tier.bloodSuckers);
        _p3sub = Phase3Sub.Suckers;
        if (_verbose) Debug.Log($"[Boss] F3: ¡BloodSuckers! Míralos para eliminarlos antes de que te dejen seco.");
    }

    private Transform[] P3Targets()
    {
        if (_phase3Targets != null && _phase3Targets.Length > 0) return _phase3Targets;
        return new[] { _leftFist != null ? _leftFist.transform : null,
                       _rightFist != null ? _rightFist.transform : null,
                       _head };
    }

    private string P3TargetName(int i) =>
        (_phase3Targets != null && _phase3Targets.Length > 0) ? $"parte {i + 1}"
        : i == 0 ? "brazo izquierdo" : i == 1 ? "brazo derecho" : "cabeza";

    // BloodSuckers ──────────────────────────────────────────────────────────

    // La oleada NO sale de golpe: los BloodSuckers aparecen DE UNO EN UNO, uno por beat, y van llenando la pantalla.
    // Cada uno se elimina (se vuelve contra el jefe) mirándolo UN beat. Antes salían todos a la vez y, chupando
    // juntos, dejaban al jugador sin sangre antes de que pudiera mirarlos.
    private readonly Queue<(Vector3 offset, bool life)> _p3SpawnQueue = new Queue<(Vector3, bool)>();
    private readonly List<Vector3> _p3PlannedOffsets = new List<Vector3>();
    private double _p3NextSpawnDsp;

    /// <summary>Planifica la oleada (huecos bien repartidos) y la va soltando de uno en uno con UpdateP3SpawnQueue.</summary>
    private void SpawnP3Suckers(int n)
    {
        n = Mathf.Max(1, n);
        if (n % 2 == 0) n++;   // siempre IMPAR: la diadema queda simétrica con uno justo encima del jefe
        _p3Suckers.Clear();
        _p3SuckerOffsets.Clear();
        _p3SpawnQueue.Clear();
        _p3PlannedOffsets.Clear();

        // Una vez en la Fase 3: uno de los huecos de BloodSucker lo ocupa el ORBE DE VIDA (míralo → vida extra).
        // Se añade un hueco más para que los BloodSuckers sigan siendo impares (la diadema queda simétrica).
        int slots   = n;
        int lifeIdx = -1;
        if (!_p3LifeSpawned) { slots = n + 1; lifeIdx = Random.Range(0, slots); }

        for (int i = 0; i < slots; i++)
        {
            Vector3 offset = PickSuckerOffset(i, slots);
            _p3PlannedOffsets.Add(offset);
            _p3SpawnQueue.Enqueue((offset, i == lifeIdx));
        }

        // El primero en el próximo beat; los demás, uno por beat.
        BeatManager bm = _beatManager;
        _p3NextSpawnDsp = bm != null && bm.HasClock
            ? bm.DspAtBeat(System.Math.Floor(bm.ClockBeats) + 1.0)
            : AudioSettings.dspTime;
        UpdateP3SpawnQueue();
    }

    /// <summary>Suelta los BloodSuckers pendientes cuyo beat ya ha llegado (uno por beat).</summary>
    private void UpdateP3SpawnQueue()
    {
        while (_p3SpawnQueue.Count > 0 && AudioSettings.dspTime >= _p3NextSpawnDsp)
        {
            var (offset, life) = _p3SpawnQueue.Dequeue();
            SpawnOneP3Sucker(offset, life);
            _p3NextSpawnDsp += BeatDuration;
        }
    }

    private void SpawnOneP3Sucker(Vector3 offset, bool life)
    {
        if (life)
        {
            _p3LifeSpawned = true;
            Vector3 off = offset;
            if (_p3LifeOrb != null) Destroy(_p3LifeOrb.gameObject);
            _p3LifeOrb = BossLifeOrb.Create(() => SuckerWorld(off), LifeColor, _lifeOrbGazeSeconds, _gazeMaxDistance,
                                            PlayerCamera(), () => GiveExtraLife(Loc.T("¡Orbe de vida!", "Life orb!")),
                                            PlayerT, _p3Kayak != null ? _p3Kayak.transform : null);
            return;
        }
        Vector3 pos = SuckerWorld(offset);

        GameObject go = _bloodSuckerPrefab != null
            ? Instantiate(_bloodSuckerPrefab, pos, Quaternion.identity)
            : BuildCodeSucker(pos);

        BloodSucker bs = go.GetComponentInChildren<BloodSucker>();
        if (bs == null) bs = go.AddComponent<BloodSucker>();
        // Chupan sangre DE VERDAD, como los originales (con el drenaje de su prefab / valor por defecto).
        // Al MIRARLOS no mueren: se giran hacia el escudo y le chupan la sangre al JEFE.
        bs.SetBasePosition(pos);
        bs.SetConvertTarget(SuckerTarget());

        // Mirarlo EXACTAMENTE un beat (_suckerGazeBeats) lo elimina, también en los de prefab.
        GazeSwitch gz = go.GetComponentInChildren<GazeSwitch>();
        if (gz != null) gz.Configure(_suckerGazeBeats * BeatDuration, _gazeMaxDistance);

        _p3Suckers.Add(bs);
        _p3SuckerOffsets.Add(offset);
        RefreshGazeHUD();
    }

    /// <summary>
    /// Hueco para un BloodSucker, bien REPARTIDO: cada uno en su franja de ancho (de izquierda a derecha, en
    /// orden aleatorio) y, dentro de ella, la altura/profundidad que más lejos quede de los ya colocados
    /// (mejor de 16 candidatos). Así cubren toda la zona sin amontonarse ni taparse.
    /// </summary>
    private Vector3 PickSuckerOffset(int i, int n)
    {
        if (i == 0) _suckerSlots = ShuffledSlots(n);
        int slot = _suckerSlots != null && i < _suckerSlots.Length ? _suckerSlots[i] : i;
        float w  = 2f * _suckerAreaHalfWidth / n;                    // ancho de cada franja
        float x0 = -_suckerAreaHalfWidth + slot * w;

        Vector3 best = Vector3.zero; float bestD = -1f;
        for (int c = 0; c < 16; c++)
        {
            Vector3 cand = new Vector3(Random.Range(x0 + w * 0.15f, x0 + w * 0.85f),
                                       Random.Range(_suckerAreaHeight.x, _suckerAreaHeight.y),
                                       Random.Range(_suckerAreaFront.x, _suckerAreaFront.y));
            float d = float.MaxValue;
            foreach (Vector3 o in _p3PlannedOffsets) d = Mathf.Min(d, Vector3.Distance(o, cand));
            if (d > bestD) { bestD = d; best = cand; }
            if (d >= _suckerMinSpacing * 2f) break;                  // ya está muy despejado
        }
        return best;
    }

    private int[] _suckerSlots;

    private static int[] ShuffledSlots(int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = i;
        for (int i = n - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (a[i], a[j]) = (a[j], a[i]); }
        return a;
    }

    /// <summary>BloodSucker sencillo por código (si no hay prefab): esfera roja con mirada.</summary>
    private GameObject BuildCodeSucker(Vector3 pos)
    {
        GameObject go = RuntimeMaterials.Primitive(PrimitiveType.Sphere);   // trae su SphereCollider (para la mirada)
        go.name = "BloodSucker (código)";
        go.transform.position   = pos;
        go.transform.localScale = Vector3.one * 1.6f;
        var r = go.GetComponent<Renderer>();
        if (r != null) r.material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.55f, 0.02f, 0.05f, 1f) };
        go.AddComponent<GazeSwitch>().Configure(_suckerGazeBeats * BeatDuration, _gazeMaxDistance);
        go.AddComponent<BloodSucker>();
        return go;
    }

    /// <summary>Posición del bicho: relativa al JEFE (viaja con él), por delante hacia el jugador, sobre la lava.</summary>
    private Vector3 SuckerWorld(Vector3 offset)
    {
        Vector3 bp = transform.position;
        float dirZ = PlayerT != null ? Mathf.Sign(PlayerT.position.z - bp.z) : 1f;
        return new Vector3(bp.x + offset.x, LavaY() + offset.y, bp.z + dirZ * offset.z);
    }

    /// <summary>Hacia dónde miran/chupan los BloodSuckers convertidos: el torso (centro del escudo).</summary>
    private Transform SuckerTarget()
    {
        if (_p3TorsoRenderer == null) _p3TorsoRenderer = FindTorsoRenderer();
        return _p3TorsoRenderer != null ? _p3TorsoRenderer.transform : BossT;
    }

    /// <summary>¿Todos los BloodSuckers de la oleada se han vuelto contra el jefe y ya le miran?</summary>
    private bool AllSuckersTurned()
    {
        if (_p3SpawnQueue.Count > 0) return false;   // aún faltan por salir
        foreach (BloodSucker bs in _p3Suckers)
        {
            if (bs == null) continue;   // (si alguno se destruyó por otra vía, no bloquea)
            if (!bs.IsConverted || !bs.IsFacingTarget) return false;
            if (!_p3RingPolar.TryGetValue(bs, out Vector4 st) || st.w < 0.5f) return false;   // aún no ha llegado
        }
        return true;
    }

    /// <summary>Los bichos de la oleada anterior se desintegran (al empezar otro ciclo).</summary>
    private void ClearP3Suckers()
    {
        foreach (BloodSucker bs in _p3Suckers) if (bs != null) bs.DisintegrateNow();
        _p3SpawnQueue.Clear();
        _p3PlannedOffsets.Clear();
        _p3Ring.Clear();
        _p3RingPolar.Clear();
        _p3Suckers.Clear();
        _p3SuckerOffsets.Clear();
    }

    // Diadema de ojos: los BloodSuckers convertidos se colocan en ARCO alrededor del escudo, por ENCIMA del jefe,
    // en un plano vertical de cara al jugador. Uno siempre justo encima; los demás alternan izq/der.
    [Header("Fase 3 — diadema de BloodSuckers")]
    [Tooltip("Metros por fuera del escudo a los que se colocan los ojos.")]
    [SerializeField] private float _suckerRingMargin = 3f;
    [Tooltip("Medio arco de la diadema (grados desde arriba hacia cada lado). 90 = semicírculo.")]
    [SerializeField] private float _suckerRingHalfArc = 80f;
    [Tooltip("Rapidez con la que vuelan alrededor del jefe hasta su sitio.")]
    [SerializeField] private float _suckerRingSpeed = 2.5f;

    private float _p3RingRadius = 20f;
    private readonly List<BloodSucker> _p3Ring = new List<BloodSucker>();                 // en orden de conversión
    private readonly Dictionary<BloodSucker, Vector4> _p3RingPolar =
        new Dictionary<BloodSucker, Vector4>();   // x = ángulo (º), y = radio, z = profundidad, w = 1 si ya ha llegado

    private void UpdateP3Suckers()
    {
        Vector3 center = TorsoCenter();
        float   dirZ   = PlayerT != null ? Mathf.Sign(PlayerT.position.z - center.z) : 1f;
        Vector3 toP    = new Vector3(0f, 0f, dirZ == 0f ? 1f : dirZ);   // hacia el jugador (delante de la diadema)
        Vector3 right  = Vector3.right, up = Vector3.up;

        // Registrar los recién convertidos (en orden) con su posición actual en coordenadas polares.
        for (int i = _p3Suckers.Count - 1; i >= 0; i--)
        {
            if (_p3Suckers[i] == null) { _p3Suckers.RemoveAt(i); _p3SuckerOffsets.RemoveAt(i); continue; }   // eliminado
            BloodSucker bs = _p3Suckers[i];
            if (bs.IsConverted && !_p3RingPolar.ContainsKey(bs))
            {
                Vector3 rel = bs.transform.position - center;
                float ang = Mathf.Atan2(Vector3.Dot(rel, up), Vector3.Dot(rel, right)) * Mathf.Rad2Deg;
                float rad = new Vector2(Vector3.Dot(rel, right), Vector3.Dot(rel, up)).magnitude;
                _p3RingPolar[bs] = new Vector4(ang, rad, Vector3.Dot(rel, toP), 0f);
                _p3Ring.Add(bs);
            }
        }
        _p3Ring.RemoveAll(b => b == null);

        // Los que aún te chupan a ti: siguen con el jefe, por delante.
        for (int i = 0; i < _p3Suckers.Count; i++)
            if (!_p3Suckers[i].IsConverted) _p3Suckers[i].SetBasePosition(SuckerWorld(_p3SuckerOffsets[i]));

        // Los convertidos: vuelan ALREDEDOR del jefe (interpolando ángulo y radio) hasta su hueco en la diadema.
        int   k    = _p3Ring.Count;
        int   half = Mathf.Max(1, k / 2);                                  // huecos por lado
        float step = k <= 1 ? 0f : _suckerRingHalfArc / half;
        float t    = 1f - Mathf.Exp(-_suckerRingSpeed * Time.deltaTime);
        for (int j = 0; j < k; j++)
        {
            BloodSucker bs = _p3Ring[j];
            // j = 0 arriba; luego +1, −1, +2, −2… (alternando izquierda / derecha)
            int   o      = j == 0 ? 0 : ((j + 1) / 2) * (j % 2 == 1 ? 1 : -1);
            float target = 90f + o * step;

            Vector4 st = _p3RingPolar[bs];
            st.x = Mathf.LerpAngle(st.x, target, t);
            st.y = Mathf.Lerp(st.y, _p3RingRadius, t);
            st.z = Mathf.Lerp(st.z, 1.5f, t);
            bool arrived = Mathf.Abs(Mathf.DeltaAngle(st.x, target)) < 4f && Mathf.Abs(st.y - _p3RingRadius) < 0.8f;
            st.w = arrived ? 1f : 0f;
            _p3RingPolar[bs] = st;

            float a = st.x * Mathf.Deg2Rad;
            bs.SetBasePosition(center + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * st.y + toP * st.z);
        }
    }

    // Escudo ─────────────────────────────────────────────────────────────────

    private void ShieldOn()
    {
        if (_p3Shield != null) return;

        // CENTRO = el del TORSO (no el de todas las mallas: el tridente, los puños o los bichos lo descentraban).
        _p3TorsoRenderer = FindTorsoRenderer();
        Vector3 center = TorsoCenter();

        // RADIO: lo justo para envolver torso, cabeza y puños (sin el tridente).
        float radius = 5f;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || IsTridentPart(r.transform)) continue;
            if (r is TrailRenderer || r is LineRenderer) continue;
            if (!IsTorsoPart(r) && !IsP3TargetPart(r.transform)) continue;
            Bounds b = r.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 c = new Vector3((k & 1) == 0 ? b.min.x : b.max.x,
                                        (k & 2) == 0 ? b.min.y : b.max.y,
                                        (k & 4) == 0 ? b.min.z : b.max.z);
                radius = Mathf.Max(radius, Vector3.Distance(center, c));
            }
        }

        _p3Shield = RuntimeMaterials.Primitive(PrimitiveType.Sphere);
        _p3Shield.name = "BossShield";
        Destroy(_p3Shield.GetComponent<Collider>());   // sin collider: no bloquea la mirada ni al kayak
        var mr = _p3Shield.GetComponent<Renderer>();
        mr.material = new Material(Shader.Find("Sprites/Default")) { color = _shieldColor };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _p3ShieldBaseScale = radius * 2f * 1.05f;
        _p3RingRadius      = _p3ShieldBaseScale * 0.5f + _suckerRingMargin;
        _p3Shield.transform.localScale = Vector3.one * _p3ShieldBaseScale;
        _p3Shield.transform.position = center;
    }

    private Renderer _p3TorsoRenderer;

    /// <summary>Malla del TORSO: la del propio cuerpo (BossT) o, si no tiene, la más grande del jefe que no sea
    /// tridente, puño ni cabeza.</summary>
    private Renderer FindTorsoRenderer()
    {
        Renderer own = BossT.GetComponent<Renderer>();
        if (own != null) return own;
        Renderer best = null; float bestVol = 0f;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is TrailRenderer || r is LineRenderer) continue;
            if (IsTridentPart(r.transform) || IsP3TargetPart(r.transform)) continue;
            Vector3 s = r.bounds.size; float vol = s.x * s.y * s.z;
            if (vol > bestVol) { bestVol = vol; best = r; }
        }
        return best;
    }

    private Vector3 TorsoCenter() =>
        _p3TorsoRenderer != null ? _p3TorsoRenderer.bounds.center : BossT.position;

    private bool IsTorsoPart(Renderer r) => r == _p3TorsoRenderer;

    private bool IsTridentPart(Transform t) => _tridentSweep != null && t.IsChildOf(_tridentSweep.transform);

    private bool IsP3TargetPart(Transform t)
    {
        foreach (Transform p in P3Targets()) if (p != null && t.IsChildOf(p)) return true;
        return false;
    }

    private float _p3ShieldBaseScale = 1f;

    private void UpdateP3Shield(double now)
    {
        if (_p3Shield == null) return;
        _p3Shield.transform.position = TorsoCenter();   // sigue al torso (también al tambalearse/hundirse)
        // Latido suave al ritmo (1 por beat).
        float pulse = 1f + 0.03f * Mathf.Sin((float)(now / BeatDuration) * Mathf.PI * 2f);
        _p3Shield.transform.localScale = Vector3.one * (_p3ShieldBaseScale * pulse);
    }

    private void ShieldOff()
    {
        if (_p3Shield != null) Destroy(_p3Shield);
        _p3Shield = null;
    }

    // Parte expuesta (mirada) ──────────────────────────────────────────────

    private void ExposeP3Part()
    {
        ShieldOff();
        Transform[] targets = P3Targets();
        Transform part = _p3Cycle < targets.Length ? targets[_p3Cycle] : null;
        GazeSwitch userGz = (_phase3GazeSwitches != null && _p3Cycle < _phase3GazeSwitches.Length)
            ? _phase3GazeSwitches[_p3Cycle] : null;
        if (part == null && userGz != null) part = userGz.transform;

        if (part == null || !part.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"[Boss] F3: no hay parte para el ciclo {_p3Cycle + 1}; cuenta como golpe.", this);
            BeginP3PartBreak(null);
            return;
        }

        Bounds pb = PartBounds(part);
        float worldR = Mathf.Max(1.5f, Mathf.Max(pb.extents.x, pb.extents.y, pb.extents.z) * 1.3f);

        GazeSwitch gz;
        if (userGz != null)
        {
            // El GazeSwitch que has puesto tú en la escena.
            gz = userGz;
            gz.enabled = true;
            if (gz.GetComponentInChildren<Collider>() == null)
                Debug.LogWarning($"[Boss] F3: el GazeSwitch '{gz.name}' no tiene Collider: no se podrá mirar.", gz);
        }
        else
        {
            // Automático: esfera (collider NO trigger) que envuelve la parte, en la capa 'Ignore Raycast' y con el
            // rayo limitado a esa capa → nada la tapa (ni lava, ni kayak, ni otros colliders del jefe).
            _p3GazeTarget = new GameObject("GazeTarget");
            _p3GazeTarget.transform.SetParent(part, false);
            _p3GazeTarget.transform.position = pb.center;
            _p3GazeTarget.layer = 2;
            var sc = _p3GazeTarget.AddComponent<SphereCollider>();
            Vector3 ls = part.lossyScale;
            sc.radius = worldR / Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
            gz = _p3GazeTarget.AddComponent<GazeSwitch>();
            gz.SetObstruction(1 << 2);
        }

        gz.Configure(_gazeHoldBeats * BeatDuration, _gazeMaxDistance, part.GetComponentInChildren<Renderer>());
        gz.SetCamera(PlayerCamera());
        // El jugador y su kayak NUNCA tapan la mirada (la cámara va sentada dentro del ataúd).
        gz.SetIgnoredRoots(PlayerT, _p3Kayak != null ? _p3Kayak.transform : null);
        gz.ResetSwitch();
        gz.OnActivated.RemoveListener(OnP3PartGazed);
        gz.OnActivated.AddListener(OnP3PartGazed);
        _p3TargetGaze  = gz;
        _p3Part        = part;
        _p3PartRadius  = worldR;
        _p3GazeDebugT  = 0f;

        CreateP3Marker();
        RefreshGazeHUD();
        _p3sub = Phase3Sub.Exposed;
        if (_verbose) Debug.Log($"[Boss] F3: ¡escudo roto! Mira '{part.name}' ({P3TargetName(_p3Cycle)}) " +
                                $"{_gazeHoldBeats} beats. GazeSwitch: '{gz.name}'.");
    }

    /// <summary>
    /// Al arrancar: localiza los GazeSwitch de la Fase 3 (los del array o, si falta alguno, el que ya tenga la
    /// parte: LeftFist, RightFist, BossHead) y los APAGA, para que no se pueda dañar al jefe hasta que caiga
    /// el escudo. Además anula los "TakeDamage" puestos a mano en su OnActivated: el golpe ya lo cuenta el jefe
    /// al destruir la parte (si no, contaría doble).
    /// </summary>
    private void InitPhase3GazeSwitches()
    {
        Transform[] targets = P3Targets();
        int n = Mathf.Max(targets.Length, _phase3GazeSwitches != null ? _phase3GazeSwitches.Length : 0);
        var list = new GazeSwitch[n];
        for (int i = 0; i < n; i++)
        {
            GazeSwitch g = (_phase3GazeSwitches != null && i < _phase3GazeSwitches.Length) ? _phase3GazeSwitches[i] : null;
            if (g == null && i < targets.Length && targets[i] != null) g = targets[i].GetComponentInChildren<GazeSwitch>(true);
            list[i] = g;
            if (g == null) continue;

            for (int k = 0; k < g.OnActivated.GetPersistentEventCount(); k++)
                if (g.OnActivated.GetPersistentTarget(k) == this &&
                    g.OnActivated.GetPersistentMethodName(k) == nameof(TakeDamage))
                    g.OnActivated.SetPersistentListenerState(k, UnityEngine.Events.UnityEventCallState.Off);

            g.enabled = false;
        }
        _phase3GazeSwitches = list;
    }

    private float _p3GazeDebugT;

    /// <summary>Depuración (con Verbose): qué toca el rayo de la mirada y cuánto lleva cargado.</summary>
    private void DebugP3Gaze()
    {
        if (!_verbose || _p3TargetGaze == null) return;
        _p3GazeDebugT += Time.deltaTime;
        if (_p3GazeDebugT < 0.5f) return;
        _p3GazeDebugT = 0f;
        Debug.Log($"[Boss] F3 mirada → el rayo toca: '{_p3TargetGaze.LastHitName}' · carga {_p3TargetGaze.Progress:P0} " +
                  $"· switch '{_p3TargetGaze.name}' {(_p3TargetGaze.isActiveAndEnabled ? "activo" : "APAGADO")}");
    }

    private Camera PlayerCamera()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.isActiveAndEnabled) return cam;
        return PlayerT != null ? PlayerT.GetComponentInChildren<Camera>() : cam;
    }

    private GameObject _p3GazeTarget;
    private float      _p3PartRadius = 3f;

    private static Bounds PartBounds(Transform part)
    {
        bool any = false; Bounds b = new Bounds(part.position, Vector3.one * 3f);
        foreach (Renderer r in part.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private void OnP3PartGazed()
    {
        if (_p3sub != Phase3Sub.Exposed) return;
        if (_p3TargetGaze != null)
        {
            _p3TargetGaze.OnActivated.RemoveListener(OnP3PartGazed);
            _p3TargetGaze.enabled = false;
        }
        BeginP3PartBreak(_p3Part);
    }

    private void BeginP3PartBreak(Transform part)
    {
        DestroyP3Marker();
        if (_p3TargetGaze != null)
        {
            _p3TargetGaze.OnActivated.RemoveListener(OnP3PartGazed);
            _p3TargetGaze.enabled = false;
        }
        if (_p3GazeTarget != null) { Destroy(_p3GazeTarget); _p3GazeTarget = null; }
        _p3TargetGaze = null;
        _p3Part = part;
        _p3PartScale0 = part != null ? part.localScale : Vector3.one;
        _p3BreakStart = AudioSettings.dspTime;
        _p3PartDone   = false;
        SetupP3Break(part);
        _p3sub = Phase3Sub.PartBreaking;
    }

    private bool _p3PartDone;   // el golpe ya ha contado

    // ── Castigo al mirar una parte ─────────────────────────────────────────
    //  • Mano (izq / der): el puño se REBELA y le PEGA en la cara a su jefe, por su lado, EN EL BEAT
    //    ('P3PunchCount' puñetazos). Después vuelve a su sitio (sigue sirviendo para el tridente).
    //  • Cabeza (la última): las DOS manos la agarran y la EMPUJAN hacia la lava → derrotado.

    private enum P3BreakMode { Punch, HeadPush, Shrink }
    private P3BreakMode  _p3BreakMode;
    private BossFistSlam _p3PunchFist;
    private BossFistSlam[] _p3Punchers = new BossFistSlam[0];   // 1 puño (1ª vez) o los 2 intercalados (2ª vez)
    private Vector3[]    _p3PunchFrom = new Vector3[0];
    private bool[]       _p3PunchReleased = new bool[0];
    private int          _p3PunchFirstBeat, _p3PunchesDone, _p3PunchTotal;
    private float        _p3PunchStartB;
    private const int    P3PunchCount     = 4;   // 1ª vez: un puño, 4 golpes
    private const int    P3PunchCountBoth = 6;   // 2ª vez: los dos intercalados, 6 golpes (3 cada uno)
    private const float  P3GrabBeats    = 1f;     // las manos van a por la cabeza
    private const float  P3PushBeats    = 3f;     // la hunden en la lava
    private float        _p3PushBaseSink, _p3PushExtra;
    private double       _p3PushStart;
    private readonly Vector3[] _p3GrabFrom = new Vector3[2];

    // Retroceso de la cabeza al recibir el puñetazo.
    private float  _p3HeadKick, _p3HeadKickSide;

    private BossFistSlam FistOf(Transform part)
    {
        if (part == null) return null;
        if (_leftFist  != null && BossTridentSweep.SameHand(part, _leftFist.transform))  return _leftFist;
        if (_rightFist != null && BossTridentSweep.SameHand(part, _rightFist.transform)) return _rightFist;
        return null;
    }

    private void SetupP3Break(Transform part)
    {
        bool last = _p3Cycle >= P3Targets().Length - 1;
        _p3PunchFist = FistOf(part);

        if (last && _head != null)
        {
            _p3BreakMode    = P3BreakMode.HeadPush;
            _p3PushStart    = AudioSettings.dspTime;
            _p3PushBaseSink = _p3SinkOffset;
            // Hasta que la cabeza entera quede unos metros bajo la lava.
            Bounds hb = PartBounds(_head);
            _p3PushExtra = Mathf.Max(4f, hb.max.y - LavaY() + 4f);
            int i = 0;
            foreach (BossFistSlam f in new[] { _leftFist, _rightFist })
                _p3GrabFrom[i++] = f != null ? f.transform.position : Vector3.zero;
            if (_verbose) Debug.Log("[Boss] F3: ¡las manos agarran la cabeza y la hunden en la lava!");
        }
        else if (_p3PunchFist != null && _p3PunchFist.gameObject.activeInHierarchy)
        {
            _p3BreakMode = P3BreakMode.Punch;
            // 1ª vez: solo el puño mirado. 2ª vez: los DOS, intercalados (empieza el recién mirado).
            BossFistSlam other = _p3PunchFist == _leftFist ? _rightFist : _leftFist;
            bool both = _p3Cycle >= 1 && other != null && other.gameObject.activeInHierarchy;
            _p3Punchers   = both ? new[] { _p3PunchFist, other } : new[] { _p3PunchFist };
            _p3PunchTotal = both ? P3PunchCountBoth : P3PunchCount;
            _p3PunchFrom     = new Vector3[_p3Punchers.Length];
            _p3PunchReleased = new bool[_p3Punchers.Length];
            for (int j = 0; j < _p3Punchers.Length; j++) _p3PunchFrom[j] = _p3Punchers[j].transform.position;

            _p3PunchFirstBeat = CurrentBeat() + 2;   // 1 beat para echarse atrás; pega en beats enteros
            _p3PunchStartB    = (float)((AudioSettings.dspTime - DspForBeat(_p3PunchFirstBeat)) / BeatDuration);
            _p3PunchesDone    = 0;
            if (_verbose) Debug.Log(both ? "[Boss] F3: ¡los DOS puños le pegan en la cara, intercalados!"
                                         : $"[Boss] F3: ¡el {_p3PunchFist.name} se rebela y le pega en la cara!");
        }
        else _p3BreakMode = P3BreakMode.Shrink;
    }

    private void UpdateP3PartBreak(double now)
    {
        UpdateP3HeadKick();
        switch (_p3BreakMode)
        {
            case P3BreakMode.Punch:    UpdateP3Punch(now);    break;
            case P3BreakMode.HeadPush: UpdateP3HeadPush(now); break;
            default:                   UpdateP3Shrink(now);   break;
        }
    }

    /// <summary>Punto de la CARA donde pega un puño de ese lado (side = +1 derecha / −1 izquierda, en X mundo).</summary>
    private Vector3 FaceContact(BossFistSlam fist, float side, float inset = 0.9f)
    {
        Bounds hb = _head != null ? PartBounds(_head) : new Bounds(BossT.position + Vector3.up * 10f, Vector3.one * 6f);
        float half = fist != null ? fist.HalfHeight : 2.5f;
        return hb.center + Vector3.right * side * (hb.extents.x + half * inset);
    }

    private float SideOf(BossFistSlam fist)
    {
        Vector3 hc = _head != null ? PartBounds(_head).center : BossT.position;
        float s = Mathf.Sign(fist.RestWorldPosition.x - hc.x);
        return s == 0f ? 1f : s;
    }

    /// <summary>
    /// Puñetazos EN EL BEAT a la cara, por el lado de cada puño. Con 2 puños se intercalan (golpe n → puño n % 2):
    /// cada puño rebota tras su golpe, espera echado atrás y vuelve a pegar en su siguiente beat.
    /// </summary>
    private void UpdateP3Punch(double now)
    {
        int   count = _p3Punchers.Length;
        float b     = (float)((now - DspForBeat(_p3PunchFirstBeat)) / BeatDuration);   // 0 = 1er golpe

        for (int j = 0; j < count; j++)
        {
            BossFistSlam fist = _p3Punchers[j];
            if (fist == null || _p3PunchReleased[j]) continue;

            float   side     = SideOf(fist);
            Vector3 contact  = FaceContact(fist, side);
            Vector3 windback = contact + Vector3.right * side * 7f + Vector3.up * 1.5f;

            int   lastIdx = j + ((_p3PunchTotal - 1 - j) / count) * count;   // último golpe de este puño
            float q       = (b - j) / count;                                   // en "periodos" de este puño
            Vector3 target;

            if (b < j - 0.2f)
            {
                // Se coloca y se echa atrás hasta su primer golpe.
                float a = Mathf.InverseLerp(_p3PunchStartB, j - 0.2f, b);
                target = Vector3.Lerp(_p3PunchFrom[j], windback, a * a * (3f - 2f * a));
            }
            else if (b < j)
            {
                target = Vector3.Lerp(windback, contact, Sq(Mathf.InverseLerp(j - 0.2f, j, b)));   // 1er ¡pum!
            }
            else if (b > lastIdx)
            {
                float a = Mathf.Clamp01((b - lastIdx) / 0.3f);                                     // rebote final
                target = Vector3.Lerp(contact, windback, 1f - Sq(1f - a));
                if (b >= lastIdx + 0.4f)
                {
                    _p3PunchReleased[j] = true;
                    fist.ManualRelease(BeatDuration, 1f);                                           // a su sitio
                    continue;
                }
            }
            else
            {
                float pb = (q - Mathf.Floor(q)) * count;   // beats desde su último golpe
                if (pb < 0.3f)              target = Vector3.Lerp(contact, windback, 1f - Sq(1f - pb / 0.3f));
                else if (pb < count - 0.2f) target = windback;
                else                        target = Vector3.Lerp(windback, contact, Sq((pb - (count - 0.2f)) / 0.2f));
            }
            fist.ManualHold(target, 60f);
        }

        // Golpes que llegan (en el beat): la cabeza retrocede hacia el otro lado.
        while (_p3PunchesDone < _p3PunchTotal && b >= _p3PunchesDone)
        {
            BossFistSlam hitter = _p3Punchers[_p3PunchesDone % count];
            _p3PunchesDone++;
            _p3HeadKick     = 1f;
            _p3HeadKickSide = hitter != null ? SideOf(hitter) : 1f;
            if (!_p3PartDone) { _p3PartDone = true; TakeDamage(1); }   // cuenta 1 golpe (con el 1er puñetazo)
            if (_state == BossState.Defeated) { _p3sub = Phase3Sub.Finished; return; }
        }

        if (b >= _p3PunchTotal + 1f) StartP3Cycle(_p3Cycle + 1);
    }

    private static float Sq(float x) => x * x;

    private void UpdateP3HeadPush(double now)
    {
        float b = (float)((now - _p3PushStart) / BeatDuration);

        // Hundir: el jefe entero baja (ease-in, cada vez con más fuerza) mientras las manos le sujetan la cabeza.
        float pu = Mathf.Clamp01((b - P3GrabBeats) / P3PushBeats);
        _p3SinkOffset = _p3PushBaseSink + _p3PushExtra * pu * pu;

        // La cabeza forcejea.
        if (_head != null && _headCaptured && b > P3GrabBeats)
        {
            float st = 6f * (1f - pu) * Mathf.Sin((float)now * 30f);
            _head.localRotation = _headRestLocalRot * Quaternion.Euler(st, 0f, st * 0.6f);
        }

        int i = 0;
        foreach (BossFistSlam f in new[] { _leftFist, _rightFist })
        {
            int k = i++;
            if (f == null || !f.gameObject.activeInHierarchy) continue;
            Vector3 grip = FaceContact(f, SideOf(f), 0.6f);      // apretando por los lados
            float g = Mathf.Clamp01(b / P3GrabBeats);
            Vector3 target = b < P3GrabBeats
                ? Vector3.Lerp(_p3GrabFrom[k], grip + Vector3.up * 2f * Mathf.Sin(g * Mathf.PI), g * g * (3f - 2f * g))
                : grip;
            f.ManualHold(target, b < P3GrabBeats ? 20f : 60f);
        }

        if (b >= P3GrabBeats + P3PushBeats)
        {
            _p3PartDone = true;
            TakeDamage(1);
            if (_state != BossState.Defeated) Defeat();   // la cabeza bajo la lava: fin del jefe
            _p3sub = Phase3Sub.Finished;
        }
    }

    /// <summary>(Partes que no son puño ni cabeza) La parte tiembla, se encoge y desaparece.</summary>
    private void UpdateP3Shrink(double now)
    {
        float u = (float)((now - _p3BreakStart) / BeatDuration);
        if (!_p3PartDone)
        {
            if (_p3Part != null)
            {
                float k = Mathf.Clamp01(u);
                float shake = 0.08f * (1f - k) * Mathf.Sin((float)now * 60f);
                _p3Part.localScale = _p3PartScale0 * Mathf.Max(0.001f, 1f - k * k + shake);
            }
            if (u < 1f) return;
            if (_p3Part != null) _p3Part.gameObject.SetActive(false);
            _p3PartDone = true;
            bool last = _p3Cycle >= P3Targets().Length - 1;
            TakeDamage(1);
            if (_state == BossState.Defeated) { _p3sub = Phase3Sub.Finished; return; }
            if (last) { Defeat(); _p3sub = Phase3Sub.Finished; }
            return;
        }
        if (u >= 2f) StartP3Cycle(_p3Cycle + 1);
    }

    /// <summary>La cabeza retrocede y se ladea con cada puñetazo (y vuelve sola).</summary>
    private void UpdateP3HeadKick()
    {
        if (_head == null || !_headCaptured || _p3BreakMode == P3BreakMode.HeadPush) return;
        _p3HeadKick = Mathf.MoveTowards(_p3HeadKick, 0f, Time.deltaTime / (0.45f * BeatDuration));
        float k = _p3HeadKick * _p3HeadKick;
        Vector3 worldOff = Vector3.right * (-_p3HeadKickSide) * 1.6f * k;
        _head.localPosition = _headRestLocalPos +
                              (_head.parent != null ? _head.parent.InverseTransformVector(worldOff) : worldOff);
        _head.localRotation = _headRestLocalRot * Quaternion.Euler(0f, _p3HeadKickSide * 10f * k, -_p3HeadKickSide * 14f * k);
    }

    // Indicador de la parte a mirar ───────────────────────────────────────
    // Halo que envuelve la parte (se llena de blanco al cargar la mirada) + haz de luz vertical visible desde
    // lejos + texto "¡MÍRALO!" encima, mirando a la cámara.

    private GameObject _p3Marker;      // raíz del indicador
    private Renderer   _p3HaloR, _p3BeamR;
    private TextMesh   _p3HintText;

    private void CreateP3Marker()
    {
        DestroyP3Marker();
        _p3Marker = new GameObject("GazeTargetIndicator");

        GameObject halo = RuntimeMaterials.Primitive(PrimitiveType.Sphere);
        halo.name = "Halo";
        Destroy(halo.GetComponent<Collider>());
        halo.transform.SetParent(_p3Marker.transform, false);
        _p3HaloR = halo.GetComponent<Renderer>();
        _p3HaloR.material = new Material(Shader.Find("Sprites/Default")) { color = _targetMarkerColor };
        _p3HaloR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        GameObject beam = RuntimeMaterials.Primitive(PrimitiveType.Cylinder);
        beam.name = "Beam";
        Destroy(beam.GetComponent<Collider>());
        beam.transform.SetParent(_p3Marker.transform, false);
        _p3BeamR = beam.GetComponent<Renderer>();
        _p3BeamR.material = new Material(Shader.Find("Sprites/Default")) { color = _targetMarkerColor };
        _p3BeamR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var textGo = new GameObject("Hint");
        textGo.transform.SetParent(_p3Marker.transform, false);
        _p3HintText = textGo.AddComponent<TextMesh>();
        _p3HintText.text          = Loc.T("¡MÍRALO!", "LOOK AT IT!");
        _p3HintText.anchor        = TextAnchor.LowerCenter;
        _p3HintText.alignment     = TextAlignment.Center;
        _p3HintText.fontSize      = 64;
        _p3HintText.characterSize = 0.12f;
        _p3HintText.fontStyle     = FontStyle.Bold;
        _p3HintText.color         = _targetMarkerColor;
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f != null)
        {
            _p3HintText.font = f;
            MeshRenderer tmr = textGo.GetComponent<MeshRenderer>();
            if (tmr == null) tmr = textGo.AddComponent<MeshRenderer>();
            tmr.sharedMaterial = f.material;
        }
    }

    private void UpdateP3Marker(double now)
    {
        if (_p3Marker == null || _p3Part == null) return;
        Bounds  b      = PartBounds(_p3Part);
        float   charge = _p3TargetGaze != null ? _p3TargetGaze.Progress : 0f;
        float   beatPh = (float)(now / BeatDuration) * Mathf.PI * 2f;
        float   pulse  = 1f + 0.12f * Mathf.Sin(beatPh);                 // late al ritmo
        Color   col    = Color.Lerp(_targetMarkerColor, Color.white, charge);

        // Halo alrededor de la parte (más opaco cuanto más cargada la mirada).
        Transform halo = _p3HaloR.transform;
        halo.position   = b.center;
        halo.localScale = Vector3.one * (_p3PartRadius * 2f * pulse);
        Color hc = col; hc.a = Mathf.Lerp(0.25f, 0.6f, charge) * (0.8f + 0.2f * Mathf.Sin(beatPh));
        _p3HaloR.material.color = hc;

        // Haz vertical que sale de la parte hacia arriba (se ve desde lejos).
        const float beamH = 40f;
        Transform beam = _p3BeamR.transform;
        beam.position   = new Vector3(b.center.x, b.max.y + beamH * 0.5f, b.center.z);
        beam.localScale = new Vector3(0.8f * pulse, beamH * 0.5f, 0.8f * pulse);   // el cilindro mide 2 de alto
        Color bc = col; bc.a = 0.35f;
        _p3BeamR.material.color = bc;

        // Texto encima, mirando a la cámara y con tamaño constante en pantalla.
        Transform tt = _p3HintText.transform;
        tt.position = new Vector3(b.center.x, b.max.y + 2f, b.center.z);
        Camera cam = Camera.main;
        if (cam != null)
        {
            tt.rotation = Quaternion.LookRotation(tt.position - cam.transform.position, Vector3.up);
            float dist = Vector3.Distance(cam.transform.position, tt.position);
            tt.localScale = Vector3.one * Mathf.Max(1f, dist * 0.06f);
        }
        _p3HintText.color = col;
    }

    private void DestroyP3Marker()
    {
        if (_p3Marker != null) Destroy(_p3Marker);
        _p3Marker = null;
    }

    // Secuencia de ataques del ciclo: UNO cada vez ───────────────────────────

    private enum P3Step { Intro, Spikes, SpikesRest, Throw, ThrowRest }
    private P3Step _p3Step = P3Step.Intro;
    private double _p3StepEnd;
    private int    _p3ThrowsLeft;
    private bool   _p3ThrowStarted;

    /// <summary>
    /// PINCHOS ('spikeSectionBeats') → respiro hasta que pasan los últimos → TRIDENTE lanzado ('tridentThrows'
    /// veces, uno tras otro) → BLOODSUCKERS. Nada se solapa.
    /// </summary>
    private void UpdateP3Attacks(double now)
    {
        Phase3Tier t = P3Tier;
        switch (_p3Step)
        {
            case P3Step.Intro:
                if (now < _p3StepEnd) break;
                _p3Step         = P3Step.Spikes;
                _p3StepEnd      = now + Mathf.Max(2f, t.spikeSectionBeats) * BeatDuration;
                _p3NextSpikeDSP = now;
                if (_verbose) Debug.Log("[Boss] F3: tanda de pinchos");
                break;

            case P3Step.Spikes:
                if (now < _p3StepEnd) { UpdateP3Spikes(now); break; }
                // Respiro: deja que los últimos pinchos (que salen por delante) lleguen y pasen.
                _p3Step    = P3Step.SpikesRest;
                _p3StepEnd = now + (Mathf.Max(1f, _pillarLeadBeats) + 1f) * BeatDuration;
                break;

            case P3Step.SpikesRest:
            case P3Step.ThrowRest:
                if (now < _p3StepEnd) break;
                if (_p3ThrowsLeft > 0 && _tridentSweep != null) StartP3Throw();
                else StartP3SuckerWave();
                break;

            case P3Step.Throw:
                if (_tridentSweep == null) { _p3ThrowsLeft = 0; _p3Step = P3Step.ThrowRest; _p3StepEnd = now; break; }
                if (_tridentSweep.IsThrowInProgress) _p3ThrowStarted = true;
                // Termina cuando lo ha cogido la otra mano (encadenado: lo sujeta para el siguiente) o, en el
                // último, cuando ya está flotando en su sitio. (O si nunca llegó a arrancar.)
                if ((_p3ThrowStarted && !_tridentSweep.IsThrowInProgress) || (!_p3ThrowStarted && now > _p3StepEnd))
                {
                    _p3ThrowsLeft--;
                    _p3Step    = P3Step.ThrowRest;
                    _p3StepEnd = now + BeatDuration;
                }
                break;
        }
    }

    /// <summary>
    /// Lanza el tridente: MISMO wind-up, arco y velocidad que el barrido de la Fase 1 (empieza en un beat y
    /// el paso por el jugador cae en beat entero), pero soltado, a la altura del torso sentado → SALTAR.
    /// Lo coge la otra mano (el puño izquierdo) al final del arco.
    /// </summary>
    private void StartP3Throw()
    {
        int    b0      = CurrentBeat() + 1;                                   // arranca en el próximo beat
        int    windup  = Mathf.Max(1, Mathf.RoundToInt(_tailWindupBeats));   // igual que el barrido
        double startAt = DspForBeat(b0);
        double contact = DspForBeat(b0 + windup);

        Schedule(startAt, () =>
        {
            if (_state != BossState.Phase3 || _p3sub != Phase3Sub.Attacking || _tridentSweep == null) return;
            GetP3ThrowHands(out Transform throwHand, out Transform catchHand);
            if (_verbose) Debug.Log($"[Boss] F3: lanza con '{(throwHand != null ? throwHand.name : "—")}', " +
                                    $"lo coge '{(catchHand != null ? catchHand.name : "—")}'.");
            // Si quedan más lanzamientos, la mano que lo coge lo SUJETA y lo relanza; solo el último lo deja en su sitio.
            _tridentSweep.Throw(this, contact, SeatedTorsoY() + _throwPassHeightOffset, throwHand, catchHand,
                             -1f, _p3ThrowsLeft > 1);
        });

        _p3Step         = P3Step.Throw;
        _p3ThrowStarted = false;
        _p3StepEnd      = startAt + BeatDuration;   // margen por si no arranca
        if (_verbose) Debug.Log("[Boss] F3: ¡lanza el tridente! (salta cuando pase)");
    }

    /// <summary>
    /// SIMÉTRICO: lanza la mano del lado donde FLOTA ahora el tridente y lo coge la otra, que lo deja flotando
    /// en el lado simétrico (X → −X). El siguiente lanzamiento sale de ese lado, y así alternando.
    /// Si una mano ya está destruida, la que queda lo lanza y el tridente vuelve solo a su sitio.
    /// </summary>
    private void GetP3ThrowHands(out Transform throwHand, out Transform catchHand)
    {
        Transform lf = _leftFist  != null && _leftFist.gameObject.activeInHierarchy  ? _leftFist.transform  : null;
        Transform rf = _rightFist != null && _rightFist.gameObject.activeInHierarchy ? _rightFist.transform : null;

        if (lf == null || rf == null) { throwHand = lf != null ? lf : rf; catchHand = null; return; }

        Vector3 idle = _tridentSweep != null ? _tridentSweep.IdleWorldPosition : transform.position;
        bool leftCloser = (_leftFist.RestWorldPosition - idle).sqrMagnitude <= (_rightFist.RestWorldPosition - idle).sqrMagnitude;
        throwHand = leftCloser ? lf : rf;
        catchHand = leftCloser ? rf : lf;
    }

    // Altura del jugador SENTADO: la mínima registrada sobre la lava (saltar solo la sube).
    private float _p3SeatOffset = float.MaxValue;

    private void TrackSeatHeight()
    {
        if (PlayerT == null || _pm == null || !_pm.IsOnKayak) return;
        _p3SeatOffset = Mathf.Min(_p3SeatOffset, PlayerT.position.y - LavaY());
    }

    /// <summary>Altura del TORSO del jugador sentado en el kayak (por donde pasa el tridente). No depende de
    /// si en ese momento está saltando.</summary>
    private float SeatedTorsoY()
    {
        if (PlayerT == null) return LavaY() + 2f;
        if (_p3SeatOffset == float.MaxValue) return LavaY() + 2.2f;   // aún no se ha sentado: estimación
        float cy = _playerCC != null ? _playerCC.center.y * PlayerT.lossyScale.y : 1f;
        return LavaY() + _p3SeatOffset + cy;
    }

    // Pinchos en la lava ─────────────────────────────────────────────────────

    private void UpdateP3Spikes(double now)
    {
        if (now < _p3NextSpikeDSP) return;
        Phase3Tier t = P3Tier;
        int lo = Mathf.Max(1, Mathf.Min(t.spikesMin, t.spikesMax));
        int hi = Mathf.Max(lo, t.spikesMax);
        int n  = Random.Range(lo, hi + 1);
        for (int i = 0; i < n; i++)
        {
            float x = Random.Range(_obstacleMinX, _obstacleMaxX);
            bool tall = Random.value < t.tallChance;
            SpawnLavaSpike(x, tall, Random.Range(0f, _obstacleDepthSpread));
        }
        double gap = Mathf.Max(0.5f, t.spikeInterval) * BeatDuration;
        _p3NextSpikeDSP = System.Math.Max(_p3NextSpikeDSP + gap, now + gap * 0.5);
    }

    /// <summary>Pincho que emerge de la LAVA por delante. Capa Obstacle + ObstacleInfo (solo la cara FRONTAL
    /// es letal): el kayak muere si lo embiste de proa; rozarlo de lado no.</summary>
    private void SpawnLavaSpike(float worldX, bool tall, float extraLead)
    {
        if (_pillarPrefab == null || PlayerT == null) return;

        Vector3 fwd  = _pm != null ? _pm.RunForward : Vector3.forward;
        float   walk = _pm != null ? _pm.walkSpeed : 12f;
        float   lead = walk * _pillarLeadBeats * BeatDuration + extraLead;

        Vector3 top = PlayerT.position + fwd * lead;
        top.x = worldX;
        top.y = LavaY();

        BossPillar pillar = Instantiate(_pillarPrefab, top, Quaternion.identity);
        Vector3 s = pillar.transform.localScale;
        s.y = tall ? _pillarTallScaleY : _pillarShortScaleY;
        pillar.transform.localScale = s;

        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        if (obstacleLayer >= 0) SetLayerRecursive(pillar.gameObject, obstacleLayer);
        ObstacleInfo info = pillar.GetComponent<ObstacleInfo>();
        if (info == null) info = pillar.gameObject.AddComponent<ObstacleInfo>();
        info.lethalFaces = LethalFaces.Front;

        pillar.Init(this, AudioSettings.dspTime);
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    // Utilidades ────────────────────────────────────────────────────────────

    /// <summary>Altura de la superficie de la LAVA (capa Water) bajo el jugador. Se cachea.</summary>
    private float LavaY()
    {
        if (_p3LavaFound) return _p3LavaY;
        Vector3 from = (PlayerT != null ? PlayerT.position : transform.position) + Vector3.up * 200f;
        int water = LayerMask.GetMask("Water");
        if (water != 0 && Physics.Raycast(from, Vector3.down, out RaycastHit hit, 1000f, water, QueryTriggerInteraction.Collide))
        {
            _p3LavaY = hit.point.y;
            _p3LavaFound = true;
            return _p3LavaY;
        }
        return _lastGroundY - 2f;   // sin lava detectada: aproximación bajo el suelo
    }

    private void RefreshGazeHUD()
    {
        var hud = FindAnyObjectByType<GazeChargeUI>();
        if (hud != null) hud.RefreshSwitches();
    }

    /// <summary>Limpia todo lo de la Fase 3 (al derrotar al jefe).</summary>
    private void CleanupPhase3()
    {
        foreach (BloodSucker bs in _p3Suckers) if (bs != null) Destroy(bs.gameObject);
        _p3Suckers.Clear();
        if (_p3LifeOrb != null) { Destroy(_p3LifeOrb.gameObject); _p3LifeOrb = null; }
        _p3SuckerOffsets.Clear();
        _p3Ring.Clear();
        _p3RingPolar.Clear();
        if (_pm != null) _pm.SetGravityOverride(0f);
        ShieldOff();
        DestroyP3Marker();
        StopScrapeTrails();
        if (_p3TargetGaze != null) _p3TargetGaze.OnActivated.RemoveListener(OnP3PartGazed);
        if (_p3GazeTarget != null) { Destroy(_p3GazeTarget); _p3GazeTarget = null; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  FINAL: el jefe cae → sale terreno de la lava → se rompe el kayak → END al acabar la canción
    // ══════════════════════════════════════════════════════════════════════════

    [Header("Final (al derrotar al jefe)")]
    [Tooltip("END (prefab o el objeto de la escena). Sale del suelo COMO UNA PLATAFORMA MÁS del camino, colocado\n" +
             "para que el jugador esté DENTRO justo cuando termina LDNM-END. Vacío = letras 'END' por código.")]
    [SerializeField] private Transform _endObject;
    [Tooltip("Metros que el jugador ya ha entrado en el END cuando termina la canción.")]
    [SerializeField] private float _endInsideMeters = 8f;
    [Tooltip("El END empieza a salir cuando su entrada está a esta distancia del jugador (como las plataformas).")]
    [SerializeField] private float _endAppearAheadMeters = 110f;
    [Tooltip("Ajuste FINO de la altura del END (m). Se aplica en vivo: cámbialo en Play y verás cómo queda.")]
    [SerializeField] private float _endHeightOffset = -0.3f;
    [Tooltip("Beats que tarda el END en levantarse.")]
    [SerializeField] private float _endRiseBeats = 3f;
    [Tooltip("Altura del terreno nuevo sobre la lava (m).")]
    [SerializeField] private float _victoryTerrainAboveLava = 4f;
    [Tooltip("Impulso vertical con el que sale despedido el jugador al romperse el kayak.")]
    [SerializeField] private float _victoryJumpVelocity = 18f;
    [Tooltip("Música del jefe (para saber cuándo termina LDNM-END). Vacío = se busca sola.")]
    [SerializeField] private BossMusicController _musicController;
    [Tooltip("Sin música: beats hasta el final.")]
    [SerializeField] private float _endFallbackBeats = 16f;
    [Tooltip("Beats que tarda el terreno en salir de la lava (animado, como el resto de plataformas).")]
    [SerializeField] private float _victoryRiseBeats = 4f;
    [Tooltip("Metros BAJO la lava desde los que empieza a subir el terreno.")]
    [SerializeField] private float _victoryRiseDepth = 12f;

    private enum VictoryStep { None, WaitBreak, Rising, Terrain, Done }
    private VictoryStep _victory = VictoryStep.None;
    private double      _victoryT0, _victoryBreakAt, _victoryRiseLeft, _endRiseStart;
    private float       _victoryTopY, _victoryLogT;
    private Transform   _endInst;          // el END que sale (instancia)
    private bool        _endSpawned;
    private Vector3     _endFrom, _endTo;
    private LevelEndManager _lem;
    private Vector3 EndTarget => _endTo + Vector3.up * _endHeightOffset;

    /// <summary>
    /// Al empezar: si el END es un objeto de la ESCENA lo esconde (si es un PREFAB no se toca: se instancia al
    /// final). Y bloquea el LevelEndManager: el fin de nivel lo lanza el jefe cuando acaba la canción.
    /// </summary>
    private void InitVictory()
    {
        if (_endObject != null && _endObject.gameObject.scene.IsValid()) _endObject.gameObject.SetActive(false);
        BlockLevelEnd();
    }

    private void BlockLevelEnd()
    {
        if (_lem == null) _lem = FindAnyObjectByType<LevelEndManager>();
        if (_lem != null) _lem.SetBlocked(true);
    }

    private void BeginVictory()
    {
        BlockLevelEnd();
        if (_musicController == null) _musicController = FindAnyObjectByType<BossMusicController>();
        if (_platformSpawner == null) _platformSpawner = FindAnyObjectByType<BossPlatformSpawner>();
        _victoryT0   = DspForBeat(CurrentBeat() + 1);   // en el próximo beat
        _victoryTopY = LavaY() + _victoryTerrainAboveLava;
        _endSpawned  = false;
        _victory     = VictoryStep.WaitBreak;
    }

    /// <summary>DSP en el que termina la canción entera (LDNM-END).</summary>
    private double SongEndDSP =>
        _musicController != null && _musicController.EndFinishDSP > 0.0
            ? _musicController.EndFinishDSP
            : _victoryT0 + _endFallbackBeats * BeatDuration;

    private Vector3 VictoryForward
    {
        get
        {
            Vector3 f = _platformSpawner != null ? _platformSpawner.ForwardAxis : (_pm != null ? _pm.RunForward : Vector3.forward);
            f.y = 0f;
            return f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
        }
    }

    private void UpdateVictory()
    {
        // (Ajuste en vivo de la altura del END, también cuando ya ha terminado todo.)
        if (_victory == VictoryStep.Done)
        {
            if (_endInst != null) _endInst.position = EndTarget;
            return;
        }
        if (_victory == VictoryStep.None) return;
        double now = AudioSettings.dspTime;

        switch (_victory)
        {
            case VictoryStep.WaitBreak:
            {
                if (now < _victoryT0) break;
                // 1) El terreno EMERGE de la lava bajo el jugador, ANIMADO (sube desde abajo con ease-out)…
                float riseBeats = Mathf.Max(1f, _victoryRiseBeats);
                float depth     = _victoryTerrainAboveLava + Mathf.Max(1f, _victoryRiseDepth);
                if (_platformSpawner != null) _platformSpawner.Resume(_victoryTopY, riseBeats, depth);

                // 2) …y el ataúd se rompe cuando la cara de arriba del terreno asoma por la lava.
                float f = Mathf.Clamp01((depth - _victoryTerrainAboveLava - 1f) / depth);
                float u = 1f - Mathf.Sqrt(1f - f);
                _victoryBreakAt  = now + u * riseBeats * BeatDuration;
                _victoryRiseLeft = (1f - u) * riseBeats * BeatDuration;
                if (_verbose) Debug.Log("[Boss] FINAL: el terreno sale de la lava. El loop sigue hasta acabar.");
                _victory = VictoryStep.Rising;
                break;
            }

            case VictoryStep.Rising:
            {
                if (now < _victoryBreakAt) break;
                float g = _pm != null ? Mathf.Max(1f, _pm.CurrentGravity) : 30f;
                float v = Mathf.Max(_victoryJumpVelocity, g * (float)_victoryRiseLeft * 0.5f + 2f);
                bool seated = _pm != null && _pm.IsOnKayak;
                if (_p3Kayak != null) _p3Kayak.BreakApart(v);
                if (_pm != null && (!seated || _p3Kayak == null)) _pm.Bounce(v);
                if (_verbose) Debug.Log($"[Boss] FINAL: ¡se rompe el kayak! (salto {v:F1} m/s)");
                _victory = VictoryStep.Terrain;
                break;
            }

            case VictoryStep.Terrain:
            {
                double songEnd = SongEndDSP;
                Vector3 fwd    = VictoryForward;
                float   walk   = _pm != null ? _pm.walkSpeed : 12f;

                // ¿Dónde estará el jugador cuando acabe la canción? El END va AHÍ (con el jugador ya dentro).
                float playerAlong = PlayerT != null ? Vector3.Dot(PlayerT.position, fwd) : 0f;
                float along       = playerAlong + walk * (float)System.Math.Max(0.0, songEnd - now);
                float entrance    = along - _endInsideMeters;

                // Sale del suelo como una plataforma más, cuando entra en el rango de "por delante".
                if (!_endSpawned && (entrance - playerAlong <= _endAppearAheadMeters || now >= songEnd))
                    SpawnEnd(Mathf.Max(entrance, playerAlong + 5f), now);

                if (_endSpawned && _endInst != null)
                {
                    float ru = Mathf.Clamp01((float)((now - _endRiseStart) / (Mathf.Max(0.1f, _endRiseBeats) * BeatDuration)));
                    _endInst.position = Vector3.Lerp(_endFrom, EndTarget, 1f - (1f - ru) * (1f - ru));
                }

                if (_verbose && (_victoryLogT += Time.deltaTime) > 2f)
                {
                    _victoryLogT = 0f;
                    Debug.Log($"[Boss] FINAL: la canción acaba en {songEnd - now:F1} s · END {(_endSpawned ? "fuera" : "aún bajo tierra")}.");
                }

                // Termina LDNM-END: el jugador está dentro del END → fin de nivel (auto-movimiento + mirar alrededor).
                if (now >= songEnd)
                {
                    if (_endInst != null) _endInst.position = EndTarget;
                    if (_lem == null) _lem = FindAnyObjectByType<LevelEndManager>();
                    if (_lem != null)
                    {
                        Transform finish = _endInst != null ? FindDeep(_endInst, "EndPoint") : null;
                        _lem.BeginEnd(finish != null ? finish : _endInst);
                    }
                    Debug.Log("[Boss] FINAL: termina LDNM-END → fin de nivel.");
                    _victory = VictoryStep.Done;
                }
                break;
            }
        }
    }

    /// <summary>
    /// Saca el END del suelo como una plataforma: su ENTRADA en 'entranceAlong' (eje de avance), centrado en el
    /// camino y con su base a ras del terreno. El suelo se genera hasta el fondo del END y ahí se para.
    /// </summary>
    private void SpawnEnd(float entranceAlong, double now)
    {
        _endSpawned = true;
        Vector3 fwd = VictoryForward;

        if (_endObject == null) _endInst = BuildBlockEnd(fwd);
        else if (_endObject.gameObject.scene.IsValid()) _endInst = _endObject;                          // objeto de la escena
        else _endInst = Instantiate(_endObject.gameObject).transform;                                     // prefab → instancia
        _endInst.gameObject.SetActive(true);
        _endInst.name = "END";

        // Medidas reales con las MALLAS (fiable aunque acabe de activarse).
        Bounds b = WorldMeshBounds(_endInst);
        float frontAlong = Vector3.Dot(b.center, fwd) - Vector3.Dot(b.extents, new Vector3(Mathf.Abs(fwd.x), Mathf.Abs(fwd.y), Mathf.Abs(fwd.z)));
        float depthAlong = 2f * (Vector3.Dot(b.center, fwd) - frontAlong);

        float centerX = _platformSpawner != null ? _platformSpawner.transform.position.x : 0f;
        Vector3 delta = fwd * (entranceAlong - frontAlong);
        delta.x += centerX - (b.center.x + delta.x);
        // El PIVOTE del END es la cara de arriba de su suelo (0.5 de grosor por debajo): pivote a ras del terreno,
        // igual que un jugador de pie en él estaría a y = 0 con el END en y = 0.
        delta.y  = _victoryTopY - _endInst.position.y;

        _endTo   = _endInst.position + delta;
        _endFrom = _endTo - Vector3.up * (b.size.y + 20f);
        _endInst.position = _endFrom;
        _endRiseStart = now;

        // Suelo hasta el fondo del END (por si no tiene suelo propio) y ahí se acaba.
        if (_platformSpawner != null) _platformSpawner.StopAfter(entranceAlong + depthAlong);

        Debug.Log($"[Boss] FINAL: sale el END '{_endInst.name}' (tamaño {b.size}) → destino {_endTo}. " +
                  "El jugador estará dentro cuando acabe la canción.", _endInst);
    }

    /// <summary>Bounds en mundo de todas las mallas (MeshFilter/SkinnedMesh) bajo 't'.</summary>
    private static Bounds WorldMeshBounds(Transform t)
    {
        bool any = false; Bounds w = new Bounds(t.position, Vector3.one);
        foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds lb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = mf.transform.TransformPoint(c);
                if (!any) { w = new Bounds(p, Vector3.zero); any = true; } else w.Encapsulate(p);
            }
        }
        if (!any)
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { w = r.bounds; any = true; } else w.Encapsulate(r.bounds);
            }
        return w;
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

    /// <summary>
    /// END por código con LETRAS DE BLOQUES (cubos): se ve con cualquier pipeline de render (el texto 3D
    /// antiguo puede no verse). Letras de 5×5 bloques de 2 m (≈10 m de alto), mirando al jugador.
    /// </summary>
    private Transform BuildBlockEnd(Vector3 fwd)
    {
        string[][] letters =
        {
            new[] { "XXXX", "X...", "XXX.", "X...", "XXXX" },          // E
            new[] { "X...X", "XX..X", "X.X.X", "X..XX", "X...X" },     // N
            new[] { "XXX.", "X..X", "X..X", "X..X", "XXX." },          // D
        };
        const float cell = 2f, gap = 1.5f;

        var root = new GameObject("END");
        root.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        float totalW = 0f;
        foreach (string[] L in letters) totalW += L[0].Length * cell + gap;
        totalW -= gap;

        Material mat = null;
        float x0 = -totalW * 0.5f;
        foreach (string[] L in letters)
        {
            for (int row = 0; row < L.Length; row++)
                for (int col = 0; col < L[row].Length; col++)
                {
                    if (L[row][col] != 'X') continue;
                    GameObject c = RuntimeMaterials.Primitive(PrimitiveType.Cube);
                    Destroy(c.GetComponent<Collider>());
                    c.transform.SetParent(root.transform, false);
                    c.transform.localScale    = Vector3.one * cell;
                    c.transform.localPosition = new Vector3(x0 + (col + 0.5f) * cell, (L.Length - row - 0.5f) * cell, 0f);
                    var r = c.GetComponent<Renderer>();
                    if (mat == null) { mat = new Material(r.sharedMaterial); mat.color = new Color(1f, 0.85f, 0.2f); }
                    r.sharedMaterial = mat;
                }
            x0 += L[0].Length * cell + gap;
        }
        return root.transform;
    }
}
