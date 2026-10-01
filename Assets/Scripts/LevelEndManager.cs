using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Gestiona la secuencia de fin de nivel:
///   1. Detecta que el jugador ha pasado endZ.
///   2. Desactiva el control y lo mueve automáticamente al finish point
///      mientras la cámara mira alrededor.
///   3. Al llegar, lanza el trigger de animación de celebración.
///   4. Muestra los resultados y empieza a cargar la siguiente escena en background.
///   5. Tras resultsDisplayTime segundos, activa la escena ya cargada (sin lag-spike).
/// </summary>
[AddComponentMenu("Level/Level End Manager")]
public class LevelEndManager : MonoBehaviour
{
    // ── Fases ─────────────────────────────────────────────────────────────────

    public enum Phase { Playing, AutoMoving, Animating, ShowingResults }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Referencias")]
    [SerializeField] private PlayerMovement      _player;
    [SerializeField] private BeatManager         _beatManager;
    [SerializeField] private BeatGridVisualizer  _grid;

    [Header("Detección de fin")]
    [Tooltip("Coordenada Z a partir de la cual se considera que el nivel ha terminado.")]
    [SerializeField] private float _endZ = 100f;

    [Header("Punto de llegada")]
    [Tooltip("Transform vacío donde el jugador se detiene al terminar.")]
    [SerializeField] private Transform _finishPoint;
    [Tooltip("Velocidad (m/s) del movimiento automático al finish point.")]
    [SerializeField] private float _autoMoveSpeed = 8f;
    [Tooltip("Distancia para considerar que el jugador ha llegado.")]
    [SerializeField] private float _arrivalThreshold = 0.15f;

    [Header("Mirada alrededor (durante auto-movimiento)")]
    [Tooltip("Transform de la cámara. Si se deja vacío se usa Camera.main.")]
    [SerializeField] private Transform _cameraRig;
    [Tooltip("Ángulo máximo de giro lateral mientras camina al finish point (°).")]
    [SerializeField] private float _lookAroundYaw   = 35f;
    [Tooltip("Ángulo máximo hacia arriba mientras camina al finish point (°). Valores altos = mira mucho al techo.")]
    [SerializeField] private float _lookAroundPitch = 55f;
    [Tooltip("Velocidad de la oscilación (ciclos por segundo).")]
    [SerializeField] private float _lookAroundSpeed = 0.22f;
    [Tooltip("Segundos que tarda el look-around en alcanzar su intensidad máxima al empezar.")]
    [SerializeField] private float _lookAroundFadeIn = 1.5f;
    [Tooltip("Segundos que tarda la cámara en volver suavemente a mirar al frente al llegar.")]
    [SerializeField] private float _cameraResetDuration = 0.8f;

    [Header("Animación (al llegar al finish point)")]
    [Tooltip("Animator del personaje del jugador.")]
    [SerializeField] private Animator _animator;
    [Tooltip("Nombre del trigger de celebración en el Animator.")]
    [SerializeField] private string _celebrationTrigger = "Celebration";
    [Tooltip("Duración en segundos de la animación antes de mostrar resultados.")]
    [SerializeField] private float _animationDuration = 3f;

    [Header("Resultados")]
    [Tooltip("Tolerancia en metros para considerar que el jugador iba sincronizado en un beat.")]
    [SerializeField] private float _syncTolerance = 1.5f;
    [Tooltip("Tiempo mínimo (s) que se muestran los resultados antes de aceptar input.")]
    [SerializeField] private float _minDisplayTime = 1f;

    [Header("Siguiente nivel")]
    [Tooltip("Nombre de la escena en Build Settings. Vacío = no hace nada.")]
    [SerializeField] private string _nextSceneName;

    // ── Estado ────────────────────────────────────────────────────────────────

    public Phase CurrentPhase { get; private set; } = Phase.Playing;

    /// <summary>True desde que el jugador llega a la meta: ya no se puede morir ni cuentan los latidos.</summary>
    public static bool Finished { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InitFinished()
    {
        Finished = false;
        SceneManager.sceneLoaded -= ResetFinished;
        SceneManager.sceneLoaded += ResetFinished;
    }
    private static void ResetFinished(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) Finished = false; }

    // Beat accuracy
    private int   _totalBeats  = 0;
    private int   _syncedBeats = 0;
    private float _accuracy    = 0f;    // PRECISIÓN AL RITMO: % de latidos (clics / remadas) acertados
    private float _syncAccuracy = -1f;  // sincronía del MOVIMIENTO con la rejilla de beats (telemetría; −1 = no medible)

    // Componentes
    private CharacterController _cc;

    // Look-around
    private Quaternion _camRotOnStart;
    private float      _lookTimer;

    // Async load
    private AsyncOperation _loadOp;
    private float          _resultsTimer;
    private bool           _sceneActivated;

    // UI
    private GUIStyle  _titleStyle;
    private GUIStyle  _bodyStyle;
    private GUIStyle  _hintStyle;
    private GUIStyle  _gradeStyle;
    private Texture2D _panelTex;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        Finished = false;
        if (_player      == null) _player      = FindAnyObjectByType<PlayerMovement>();
        if (_beatManager == null) _beatManager  = BeatManager.Instance;
        if (_grid        == null) _grid         = FindAnyObjectByType<BeatGridVisualizer>();

        if (_player != null)
        {
            // Buscar CharacterController en el propio objeto Y en hijos
            _cc = _player.GetComponent<CharacterController>();
            if (_cc == null) _cc = _player.GetComponentInChildren<CharacterController>();
        }

        if (_player == null)
            Debug.LogError("[LevelEndManager] ¡No se encontró PlayerMovement en la escena!", this);
        if (_finishPoint == null)
            Debug.LogWarning("[LevelEndManager] _finishPoint no asignado — se usará _endZ como fallback.", this);
        if (_cc == null)
            Debug.LogWarning("[LevelEndManager] CharacterController no encontrado — el auto-move se saltará.", this);
    }

    private void OnEnable()
    {
        if (_beatManager != null)
            _beatManager.OnBeat.AddListener(OnBeat);
    }

    private void OnDisable()
    {
        if (_beatManager != null)
            _beatManager.OnBeat.RemoveListener(OnBeat);
    }

    private void Update()
    {
        switch (CurrentPhase)
        {
            case Phase.Playing:        UpdatePlaying();        break;
            case Phase.AutoMoving:     UpdateAutoMoving();     break;
            case Phase.ShowingResults: UpdateShowingResults(); break;
        }
    }

    private bool _blocked;

    /// <summary>Bloquea el fin de nivel por Z (p. ej. mientras dura el jefe: el final lo lanza él con BeginEnd).</summary>
    public void SetBlocked(bool blocked) => _blocked = blocked;

    /// <summary>Cambia la escena que se carga al terminar (el modo prueba vuelve al menú).</summary>
    public void SetNextScene(string scene) => _nextSceneName = scene;

    /// <summary>Lanza YA la secuencia de fin (auto-movimiento + mirar alrededor) hacia 'finishPoint'.</summary>
    public void BeginEnd(Transform finishPoint)
    {
        if (CurrentPhase != Phase.Playing) return;
        if (finishPoint != null) _finishPoint = finishPoint;
        _blocked = false;
        StartEndSequence();
    }

    // ── Phase: Playing ────────────────────────────────────────────────────────

    private void UpdatePlaying()
    {
        if (_player == null || _blocked) return;

        if (_player.transform.position.z >= _endZ)
        {
            Debug.Log($"[LevelEndManager] Fin de nivel detectado (Z={_player.transform.position.z:F1} >= {_endZ}).");
            StartEndSequence();
        }
    }

    private void StartEndSequence()
    {
        CurrentPhase = Phase.AutoMoving;
        CheckpointSystem.Clear();   // nivel superado

        // «Sangre de oro» (tienda): +1 moneda por cada % de energía que quede al terminar.
        if (PlayerProgress.GetLevel(ShopIds.BloodGold) > 0 && EnergyManager.Instance != null)
            LevelCoins.Collect(Mathf.RoundToInt(Mathf.Clamp01(EnergyManager.Instance.EnergyPercent) * 100f));

        // Precisión al ritmo = latidos acertados (Perfect + Good) / latidos pulsados. Es lo que el jugador entiende por
        // «precisión»: la usan la nota, los contratos y las mejoras. (Antes se mostraba la sincronía de la POSICIÓN con la
        // rejilla, que no mide los clics, salía 0 % en los niveles sin rejilla y se reiniciaba al volver a un checkpoint.)
        _accuracy = RunStats.HeartPresses > 0 ? 100f * RunStats.Hits / RunStats.HeartPresses : 0f;
        _syncAccuracy = _totalBeats > 0 ? (float)_syncedBeats / _totalBeats * 100f : -1f;
        StudyTelemetry.EndAttempt("completado", _syncAccuracy);   // (modo prueba: columna precision_sync)

        // Contratos firmados: los cumplidos se cobran ahora (van directos al monedero).
        _contractsDone = ContractRunner.EvaluateLevelEnd(new LevelResult
        {
            accuracy      = _accuracy,
            energyPercent = EnergyManager.Instance != null ? Mathf.Clamp01(EnergyManager.Instance.EnergyPercent) : 0f,
            runCoins      = LevelCoins.RunCoins,
            songBeats     = ContractCatalog.SongBeatsNow(),
        });

        _player.enabled = false;

        // Desactivar el CharacterController para poder mover por transform directamente
        if (_cc != null) _cc.enabled = false;

        // Guardar rotación de la cámara para el look-around
        Transform cam = GetCamera();
        _camRotOnStart = cam != null ? cam.localRotation : Quaternion.identity;
        _lookTimer = 0f;

        // La canción NO se corta: sigue hasta el final durante el paseo y los resultados (hasta cambiar de escena).
        // Para que no pase nada raro mientras tanto: se congela la energía y se desactivan los latidos del jugador.
        Finished = true;
        if (EnergyManager.Instance != null) EnergyManager.Instance.SetPaused(true);
        var rhythm = FindAnyObjectByType<RhythmInput>();
        if (rhythm != null) rhythm.enabled = false;

        Debug.Log($"[LevelEndManager] Secuencia de fin iniciada. Precisión: {_accuracy:F1}%  |  CC={(_cc != null ? "desactivado" : "null")}");
    }

    // ── Phase: AutoMoving ─────────────────────────────────────────────────────

    private void UpdateAutoMoving()
    {
        if (_finishPoint == null)
        {
            Debug.LogWarning("[LevelEndManager] Sin finish point — llegada inmediata.");
            ArriveAtFinishPoint();
            return;
        }

        // Target: X=0, Z del finish point. Y fija en la del jugador (no mover verticalmente).
        Vector3 target = new Vector3(0f, _player.transform.position.y, _finishPoint.position.z);

        // Mover en XZ (CC desactivado en StartEndSequence)
        _player.transform.position = Vector3.MoveTowards(
            _player.transform.position, target, _autoMoveSpeed * Time.deltaTime);

        // Rotar cuerpo hacia destino (solo eje Y)
        Vector3 dir     = target - _player.transform.position;
        Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);
        if (flatDir.sqrMagnitude > 0.01f)
        {
            _player.transform.rotation = Quaternion.Slerp(
                _player.transform.rotation,
                Quaternion.LookRotation(flatDir),
                8f * Time.deltaTime);
        }

        // Look-around: mira hacia arriba y a los lados mientras camina
        Transform cam = GetCamera();
        if (cam != null)
        {
            _lookTimer += Time.deltaTime;
            float t2 = _lookTimer * _lookAroundSpeed * Mathf.PI * 2f;

            // Fade-in suave: de 0 a 1 durante _lookAroundFadeIn segundos
            float intensity = _lookAroundFadeIn > 0f
                ? Mathf.SmoothStep(0f, 1f, _lookTimer / _lookAroundFadeIn)
                : 1f;

            // Yaw: oscila de lado a lado
            float yaw = Mathf.Sin(t2) * _lookAroundYaw * intensity;

            // Pitch: sesgado hacia arriba con pequeña oscilación
            float pitchBase = -_lookAroundPitch * 0.75f;
            float pitchOsc  =  Mathf.Sin(t2 * 0.6f) * _lookAroundPitch * 0.25f;
            float pitch     = (pitchBase + pitchOsc) * intensity;

            cam.localRotation = _camRotOnStart * Quaternion.Euler(pitch, yaw, 0f);
        }

        // Llegada
        if (Vector3.Distance(_player.transform.position, target) <= _arrivalThreshold)
        {
            _player.transform.position = target;
            ArriveAtFinishPoint();
        }
    }

    private void ArriveAtFinishPoint()
    {
        CurrentPhase = Phase.Animating;

        if (_finishPoint != null)
            _player.transform.position = _finishPoint.position;

        Debug.Log("[LevelEndManager] Jugador llegó al finish point. Reseteando cámara...");
        StartCoroutine(AnimationAndResults());
    }

    private IEnumerator AnimationAndResults()
    {
        // 1. Volver cuerpo del jugador Y cámara suavemente a mirar el eje +Z
        Transform cam       = GetCamera();
        Quaternion bodyFrom = _player.transform.rotation;
        Quaternion bodyTo   = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        Quaternion camFrom  = cam != null ? cam.localRotation : Quaternion.identity;
        Quaternion camTo    = Quaternion.identity;

        if (_cameraResetDuration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < _cameraResetDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / _cameraResetDuration);
                _player.transform.rotation = Quaternion.Slerp(bodyFrom, bodyTo, t);
                if (cam != null) cam.localRotation = Quaternion.Slerp(camFrom, camTo, t);
                yield return null;
            }
        }
        _player.transform.rotation = bodyTo;
        if (cam != null) cam.localRotation = camTo;

        // 2. Disparar animación de celebración
        if (_animator != null && !string.IsNullOrEmpty(_celebrationTrigger))
        {
            _animator.SetTrigger(_celebrationTrigger);
            Debug.Log($"[LevelEndManager] Trigger '{_celebrationTrigger}' disparado.");
        }
        else
        {
            Debug.LogWarning($"[LevelEndManager] Sin Animator o trigger vacío. Animator={_animator}");
        }

        // 3. Esperar duración de la animación
        yield return new WaitForSeconds(Mathf.Max(0.1f, _animationDuration));

        EnterShowingResults();
    }

    // ── Phase: ShowingResults ─────────────────────────────────────────────────

    private int _coinsBanked;
    private System.Collections.Generic.List<ContractDef> _contractsDone = new System.Collections.Generic.List<ContractDef>();

    private void EnterShowingResults()
    {
        CurrentPhase  = Phase.ShowingResults;
        _resultsTimer = 0f;

        // «Pago por precisión» (tienda): +1 moneda por cada % de precisión.
        if (PlayerProgress.GetLevel(ShopIds.AccuracyPay) > 0) LevelCoins.Collect(Mathf.RoundToInt(_accuracy));

        // Cobrar las monedas de la partida (pasan al monedero; con «Bolsa de oro» ×1.5 si precisión ≥ 80 %).
        _coinsBanked = LevelCoins.BankRun(_accuracy);

        // «Interés» (tienda): +5 % del monedero (máx. 100).
        if (PlayerProgress.GetLevel(ShopIds.Interest) > 0)
        {
            int interest = Mathf.Min(100, Mathf.FloorToInt(PlayerProgress.Coins * 0.05f));
            PlayerProgress.AddCoins(interest);
            _coinsBanked += interest;
        }

        Debug.Log($"[LevelEndManager] Mostrando resultados. Precisión={_accuracy:F1}%. Cargando '{_nextSceneName}'...");

        // Empezar carga en background para evitar lag-spike
        StartAsyncLoad();
    }

    private void UpdateShowingResults()
    {
        _resultsTimer += Time.deltaTime;

        // Esperar tiempo mínimo de display antes de aceptar input
        if (_resultsTimer < _minDisplayTime) return;

        // Cualquier tecla carga el siguiente nivel
        bool anyKey = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
        if (!_sceneActivated && anyKey)
        {
            _sceneActivated = true;
            Debug.Log("[LevelEndManager] Input detectado — cargando siguiente nivel.");
            if (_loadOp != null)
                _loadOp.allowSceneActivation = true;
            else if (!string.IsNullOrEmpty(_nextSceneName))
                SceneManager.LoadScene(_nextSceneName);
        }
    }

    // ── Async load ────────────────────────────────────────────────────────────

    private void StartAsyncLoad()
    {
        if (string.IsNullOrEmpty(_nextSceneName)) return;

        _loadOp = SceneManager.LoadSceneAsync(_nextSceneName);
        // Mantener la escena pausada hasta que sea el momento
        _loadOp.allowSceneActivation = false;
    }

    // ── Beat tracking ─────────────────────────────────────────────────────────

    private void OnBeat()
    {
        if (CurrentPhase != Phase.Playing) return;
        if (_player == null || _grid == null || _beatManager == null) return;
        if (TimeRewinder.IsRewinding) return;   // (los beats "hacia atrás" del rebobinado no cuentan)

        _totalBeats++;

        double now          = AudioSettings.dspTime;
        float  expectedDist = (float)((now - _beatManager.ScheduledStartDSP) * _grid.walkSpeed);
        float  actualDist   = Vector3.Dot(
            _player.transform.position - _grid.Origin, _grid.Forward);

        if (Mathf.Abs(actualDist - expectedDist) <= _syncTolerance)
            _syncedBeats++;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Transform GetCamera()
    {
        if (_cameraRig != null) return _cameraRig;
        return Camera.main != null ? Camera.main.transform : null;
    }

    // ── OnGUI ─────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (HudHider.IsHidden) return;   // (con la pausa o el game over no se ve ningún aviso)
        if (CurrentPhase != Phase.ShowingResults) return;

        InitStyles();

        const float pw = 480f;
        float ph = 384f + (_contractsDone.Count > 0 ? 30f + 26f * _contractsDone.Count : 0f);
        float px = (Screen.width  - pw) * 0.5f;
        float py = (Screen.height - ph) * 0.5f;

        if (_panelTex == null)
        {
            _panelTex = new Texture2D(1, 1);
            _panelTex.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.1f, 0.93f));
            _panelTex.Apply();
        }
        GUI.DrawTexture(new Rect(px, py, pw, ph), _panelTex);

        float x = px + 30f;
        float y = py + 20f;

        // Título
        GUI.Label(new Rect(x, y, pw - 60f, 40f), Loc.T("NIVEL COMPLETADO", "LEVEL COMPLETE"), _titleStyle);
        y += 50f;

        // Nota
        string grade = RunStats.HeartPresses > 0 ? GetGrade(_accuracy) : "—";
        _gradeStyle.normal.textColor = GetGradeColor(_accuracy);
        GUI.Label(new Rect(px + pw - 110f, py + 15f, 90f, 70f), grade, _gradeStyle);

        // Separador
        DrawLine(new Rect(x, y, pw - 60f, 2f));
        y += 12f;

        // Precisión
        DrawStat(ref y, x, pw, Loc.T("Precisión al ritmo", "Rhythm accuracy"),
            Loc.T($"{_accuracy:F1}%  ({RunStats.Hits}/{RunStats.HeartPresses} latidos)", $"{_accuracy:F1}%  ({RunStats.Hits}/{RunStats.HeartPresses} beats)"));
        DrawStat(ref y, x, pw, "Perfect / Good / " + Loc.T("Fallos", "Misses"),
            $"{RunStats.Perfects} / {RunStats.Hits - RunStats.Perfects} / {RunStats.Misses}");
        DrawStat(ref y, x, pw, Loc.T("Mejor racha", "Best streak"), RunStats.BestStreak.ToString());

        // Monedas cobradas
        DrawStat(ref y, x, pw, Loc.T("Monedas", "Coins"), Loc.T($"+{_coinsBanked}  (monedero: {PlayerProgress.Coins})", $"+{_coinsBanked}  (wallet: {PlayerProgress.Coins})"));

        // Contratos cumplidos en este nivel
        if (_contractsDone.Count > 0)
        {
            int total = 0;
            foreach (ContractDef c in _contractsDone) total += c.reward;
            DrawStat(ref y, x, pw, Loc.T("Contratos cumplidos", "Contracts completed"), Loc.T($"{total} por cobrar", $"{total} to claim"));
            foreach (ContractDef c in _contractsDone)
            {
                GUI.Label(new Rect(x + 16f, y, pw - 76f, 24f), Loc.T($"· {c.title}   ({c.reward}, cóbralo en el puesto)", $"· {c.title}   ({c.reward}, claim it at the stand)"), _bodyStyle);
                y += 26f;
            }
        }

        y += 20f;

        // "Press any key" — parpadeo suave
        if (_resultsTimer >= _minDisplayTime)
        {
            float alpha = Mathf.Abs(Mathf.Sin(Time.time * 2f));
            var blinkStyle = new GUIStyle(_hintStyle)
            {
                fontSize  = 16,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(1f, 1f, 1f, alpha) }
            };
            GUI.Label(new Rect(px, py + ph - 60f, pw, 30f), Loc.T("PULSA CUALQUIER TECLA PARA CONTINUAR", "PRESS ANY KEY TO CONTINUE"), blinkStyle);
        }

        // Indicador de carga en background (pequeño, abajo del todo)
        if (_loadOp != null && _loadOp.progress < 0.9f)
        {
            string loadStatus = Loc.T($"Cargando... {_loadOp.progress / 0.9f * 100f:F0}%", $"Loading... {_loadOp.progress / 0.9f * 100f:F0}%");
            GUI.Label(new Rect(px, py + ph - 28f, pw, 20f), loadStatus, _hintStyle);
        }
    }

    // ── Helpers de UI ─────────────────────────────────────────────────────────

    private void DrawStat(ref float y, float x, float pw, string label, string value)
    {
        GUI.Label(new Rect(x, y, 220f, 28f), label, _bodyStyle);
        _bodyStyle.alignment = TextAnchor.MiddleRight;
        GUI.Label(new Rect(x + 220f, y, pw - 280f, 28f), value, _bodyStyle);
        _bodyStyle.alignment = TextAnchor.MiddleLeft;
        y += 32f;
    }

    private void DrawLine(Rect r)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.15f));
        tex.Apply();
        GUI.DrawTexture(r, tex);
        Destroy(tex);
    }

    private static string GetGrade(float acc)
    {
        if (acc >= 95f) return "S";
        if (acc >= 85f) return "A";
        if (acc >= 70f) return "B";
        if (acc >= 50f) return "C";
        return "D";
    }

    private static Color GetGradeColor(float acc)
    {
        if (acc >= 95f) return new Color(1f, 0.85f, 0.1f);
        if (acc >= 85f) return new Color(0.4f, 0.9f, 1f);
        if (acc >= 70f) return new Color(0.4f, 1f, 0.5f);
        if (acc >= 50f) return new Color(1f, 0.75f, 0.2f);
        return new Color(1f, 0.35f, 0.35f);
    }

    private void InitStyles()
    {
        if (_titleStyle != null) return;

        _titleStyle = new GUIStyle
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 26,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = Color.white }
        };
        _bodyStyle = new GUIStyle
        {
            fontSize  = 15,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = new Color(0.85f, 0.85f, 0.85f) }
        };
        _hintStyle = new GUIStyle
        {
            fontSize  = 12,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(0.6f, 0.6f, 0.6f) }
        };
        _gradeStyle = new GUIStyle
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 52,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white }
        };
    }

    private void OnDestroy()
    {
        if (_panelTex != null) Destroy(_panelTex);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 1f, 0.3f, 0.7f);
        Vector3 lc = new Vector3(0f, 1f, _endZ);
        Gizmos.DrawLine(lc + Vector3.left * 10f, lc + Vector3.right * 10f);
        Gizmos.DrawLine(lc, lc + Vector3.up * 3f);

        var s = new GUIStyle { normal = { textColor = new Color(0.1f, 1f, 0.3f) },
                               fontStyle = FontStyle.Bold, fontSize = 11 };
        UnityEditor.Handles.Label(lc + Vector3.up * 3.2f, $"FIN  Z={_endZ}", s);

        if (_finishPoint != null)
        {
            Gizmos.color = new Color(1f, 0.7f, 0.1f, 0.9f);
            Gizmos.DrawSphere(_finishPoint.position, 0.3f);
            UnityEditor.Handles.color = new Color(1f, 0.7f, 0.1f, 0.5f);
            UnityEditor.Handles.DrawDottedLine(lc, _finishPoint.position, 5f);
            UnityEditor.Handles.Label(_finishPoint.position + Vector3.up * 0.5f, "Finish Point", s);
        }
    }
#endif
}
