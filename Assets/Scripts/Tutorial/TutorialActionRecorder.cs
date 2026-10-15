using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// GRABADORA DE ACCIONES (solo en el editor, para construir el tutorial del nivel 1).
/// Menú «Tools/Debug: Grabar acciones del nivel 1» (se marca con ✓). Con la grabación activada, al jugar Level1
/// apunta cada acción del jugador (salto, doble salto, dash, deslizamiento, slam, wallrun, walljump, tirolina,
/// bloqueo y clics al ritmo) con su BEAT exacto, la posición del jugador y el obstáculo más cercano que tiene delante.
/// El movimiento lateral NO se graba (solo su posición X en cada acción).
/// Al terminar el intento (fin del nivel, muerte o salir de Play) guarda un CSV en «TutorialRecordings/» (raíz del
/// proyecto). Sirve para saber dónde y cuándo poner cada aviso del tutorial.
/// </summary>
public class TutorialActionRecorder : MonoBehaviour
{
#if UNITY_EDITOR
    private const string PrefKey = "TutorialRecorder.On";
    private const string MenuPath = "Tools/Debug: Grabar acciones del nivel 1";
    private const string SceneName = "Level1";

    [UnityEditor.MenuItem(MenuPath)]
    private static void Toggle()
    {
        bool on = !UnityEditor.EditorPrefs.GetBool(PrefKey, false);
        UnityEditor.EditorPrefs.SetBool(PrefKey, on);
        UnityEditor.Menu.SetChecked(MenuPath, on);
        Debug.Log(on ? "[Grabadora] ACTIVADA: juega el nivel 1 entero." : "[Grabadora] desactivada.");
    }

    [UnityEditor.MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        UnityEditor.Menu.SetChecked(MenuPath, UnityEditor.EditorPrefs.GetBool(PrefKey, false));
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (!UnityEditor.EditorPrefs.GetBool(PrefKey, false) || s.name != SceneName) return;
        if (FindAnyObjectByType<TutorialActionRecorder>() != null) return;
        new GameObject("TutorialActionRecorder").AddComponent<TutorialActionRecorder>();
    }
#endif

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private PlayerMovement _pm;
    private BlockAbility _block;
    private RhythmInput _rhythm;
    private GameOverManager _gameOver;
    private readonly StringBuilder _csv = new StringBuilder();
    private int _rows;
    private bool _saved;
    private string _end = "salir de Play";

    // Estado del fotograma anterior (para distinguir el tipo de salto).
    private bool _wasGrounded, _wasOnZipline;

    private void Start()
    {
        _csv.AppendLine("n;accion;beat;beat_cercano;desfase_ms;tiempo_s;x;y;z;en_suelo;obstaculo;obst_x;obst_y;obst_z;obst_dist_z;obst_ruta");
        DontDestroyOnLoad(gameObject);   // (para poder guardar aunque se recargue la escena al morir)
    }

    private void Hook()
    {
        if (_pm == null)
        {
            _pm = FindAnyObjectByType<PlayerMovement>();
            if (_pm == null) return;
            if (_pm.OnJumpExecuted   == null) _pm.OnJumpExecuted   = new UnityEngine.Events.UnityEvent();
            if (_pm.OnDashStarted    == null) _pm.OnDashStarted    = new UnityEngine.Events.UnityEvent<Vector3>();
            if (_pm.OnSlideStarted   == null) _pm.OnSlideStarted   = new UnityEngine.Events.UnityEvent();
            if (_pm.OnWallrunStarted == null) _pm.OnWallrunStarted = new UnityEngine.Events.UnityEvent();
            if (_pm.OnWallrunEnded   == null) _pm.OnWallrunEnded   = new UnityEngine.Events.UnityEvent();
            if (_pm.OnWalljumped     == null) _pm.OnWalljumped     = new UnityEngine.Events.UnityEvent();
            if (_pm.OnSlamStarted    == null) _pm.OnSlamStarted    = new UnityEngine.Events.UnityEvent();
            if (_pm.OnZiplineAttached== null) _pm.OnZiplineAttached= new UnityEngine.Events.UnityEvent();

            _pm.OnJumpExecuted.AddListener(OnJump);
            _pm.OnDashStarted.AddListener(d => Log(Vector3.Dot(d, _pm.transform.right) < -0.1f ? "Dash izquierda"
                                                 : Vector3.Dot(d, _pm.transform.right) > 0.1f ? "Dash derecha" : "Dash"));
            _pm.OnSlideStarted.AddListener(() => Log("Deslizarse"));
            _pm.OnWallrunStarted.AddListener(() => Log("Wallrun"));
            _pm.OnWalljumped.AddListener(() => Log("Walljump"));
            _pm.OnSlamStarted.AddListener(() => Log("Slam"));
            _pm.OnZiplineAttached.AddListener(() => Log("Tirolina"));
        }
        if (_block == null)
        {
            _block = FindAnyObjectByType<BlockAbility>();
            if (_block != null)
            {
                if (_block.OnBlockStarted == null) _block.OnBlockStarted = new UnityEngine.Events.UnityEvent();
                _block.OnBlockStarted.AddListener(() => Log("Bloquear"));
            }
        }
        if (_rhythm == null)
        {
            _rhythm = FindAnyObjectByType<RhythmInput>();
            if (_rhythm != null)
            {
                if (_rhythm.OnTimingResult == null) _rhythm.OnTimingResult = new UnityEngine.Events.UnityEvent<TimingResult>();
                _rhythm.OnTimingResult.AddListener(r => Log("Clic " + r));
            }
        }
        if (_gameOver == null) _gameOver = FindAnyObjectByType<GameOverManager>();
    }

    private void OnJump()
    {
        // El estado es el del fotograma ANTERIOR al salto.
        if (_wasOnZipline) Log("Salto (soltar tirolina)");
        else if (_wasGrounded) Log("Salto");
        else Log("Doble salto");
    }

    private void Update()
    {
        Hook();
        if (_pm == null || _saved) return;

        if (_gameOver != null && _gameOver.IsDead) { _end = "muerte"; Save(); return; }
        if (LevelEndManager.Finished) { _end = "fin del nivel"; Save(); return; }
    }

    private void LateUpdate()
    {
        if (_pm == null) return;
        _wasGrounded  = _pm.IsGroundedNow;
        _wasOnZipline = _pm.IsOnZipline;
    }

    private void Log(string action)
    {
        if (_saved || _pm == null || _pm.IsFreeroam) return;   // (solo dentro del nivel, con la música)
        BeatManager bm = BeatManager.Instance;
        if (bm == null || !bm.HasClock) return;

        double beat = bm.CorrectedTimeUnclamped / bm.BeatDuration;
        double near = System.Math.Round(beat * 2.0) / 2.0;   // beat o medio beat más cercano
        double offMs = (beat - near) * bm.BeatDuration * 1000.0;
        Vector3 p = _pm.transform.position;

        Collider obs = NearestObstacleAhead(p, _pm.transform.forward);
        string oName = "", oPath = "", ox = "", oy = "", oz = "", od = "";
        if (obs != null)
        {
            Vector3 c = obs.bounds.center;
            oName = obs.name; oPath = PathOf(obs.transform);
            ox = F(c.x); oy = F(c.y); oz = F(c.z); od = F(c.z - p.z);
        }

        _rows++;
        _csv.AppendLine(string.Join(";", _rows.ToString(Inv), action, beat.ToString("0.000", Inv), near.ToString("0.0", Inv),
            offMs.ToString("0", Inv), bm.CorrectedTimeUnclamped.ToString("0.000", Inv), F(p.x), F(p.y), F(p.z),
            _wasGrounded ? "1" : "0", oName, ox, oy, oz, od, oPath));
    }

    /// <summary>El obstáculo (layer «Obstacle») más cercano por delante del jugador, en un pasillo de ±6 m.</summary>
    private static Collider NearestObstacleAhead(Vector3 pos, Vector3 fwd)
    {
        int layer = LayerMask.NameToLayer("Obstacle");
        if (layer < 0) return null;
        Vector3 center = pos + Vector3.forward * 20f;
        Collider[] hits = Physics.OverlapBox(center, new Vector3(6f, 8f, 21f), Quaternion.identity, 1 << layer,
                                             QueryTriggerInteraction.Collide);
        Collider best = null;
        float bestScore = float.MaxValue;
        foreach (Collider h in hits)
        {
            Bounds b = h.bounds;
            float dz = b.max.z - pos.z;                     // (aún no lo ha pasado del todo)
            if (dz < -0.5f) continue;
            float dx = Mathf.Max(0f, Mathf.Abs(b.center.x - pos.x) - b.extents.x);
            float score = Mathf.Max(0f, b.min.z - pos.z) + dx * 3f;   // cerca y en su carril
            if (score < bestScore) { bestScore = score; best = h; }
        }
        return best;
    }

    private static string PathOf(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    private static string F(float v) => v.ToString("0.00", Inv);

    private void OnApplicationQuit() => Save(false);
    private void OnDestroy() => Save(false);

    private void Save(bool destroy = true)
    {
        if (_saved || _rows == 0) return;
        _saved = true;
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "TutorialRecordings");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"Nivel1_{System.DateTime.Now:yyyyMMdd_HHmmss}_{(_end == "fin del nivel" ? "completo" : _end.Replace(' ', '_'))}.csv");
        File.WriteAllText(file, _csv.ToString());
        Debug.Log($"[Grabadora] {_rows} acciones guardadas ({_end}): {file}");
        if (destroy) Destroy(gameObject);
    }
}
