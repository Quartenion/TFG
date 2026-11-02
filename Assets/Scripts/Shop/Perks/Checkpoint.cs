using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CHECKPOINT del nivel (para el consumible «Checkpoint» de la tienda).
///
/// SETUP: crea un objeto vacío EN EL CAMINO, a la altura del jugador (donde quieres que reaparezca), y añade este
/// componente. Si está vacío se genera un arco verde. Pon tantos como quieras por nivel.
///
/// FUNCIONAMIENTO:
///   • Al pasarlo, queda ACTIVO (si tienes checkpoints comprados sale el aviso «CHECKPOINT»).
///   • Si mueres después y te queda algún checkpoint comprado, se GASTA uno: se recarga el nivel, apareces aquí, haces
///     el 3-2-1 de siempre y la canción arranca justo en este punto (alineado al beat). Conservas las monedas que
///     llevabas al pasarlo.
///   • No uses checkpoints dentro de la pelea del jefe (la música y las fases del jefe no se pueden rebobinar).
/// </summary>
[AddComponentMenu("Level/Checkpoint")]
public class Checkpoint : MonoBehaviour
{
    [SerializeField] private Color _idleColor   = new Color(0.25f, 0.7f, 0.35f, 0.55f);
    [SerializeField] private Color _activeColor = new Color(0.4f, 1f, 0.5f, 0.9f);

    private PlayerMovement _pm;
    private bool _passed;
    private Renderer[] _visual;
    private float _toastT;

    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();
        if (GetComponentInChildren<Renderer>() == null) BuildVisual();
        _visual = GetComponentsInChildren<Renderer>();

        // ¿Ya se pasó antes (reaparecemos en este checkpoint o en uno posterior)?
        if (CheckpointSystem.HasActive &&
            CheckpointSystem.AlongOf(CheckpointSystem.Active.pos) >= CheckpointSystem.AlongOf(transform.position) - 0.5f)
            SetPassed();
        else
            Tint(_idleColor);
    }

    private void Update()
    {
        if (_toastT > 0f) _toastT -= Time.unscaledDeltaTime;
        if (_passed || _pm == null || _pm.IsFreeroam) return;
        if (BeatManager.Instance == null || !BeatManager.Instance.IsPlaying) return;

        if (CheckpointSystem.AlongOf(_pm.transform.position) >= CheckpointSystem.AlongOf(transform.position))
        {
            CheckpointSystem.Activate(transform.position);
            SetPassed();
            if (PlayerProgress.GetCount(ShopIds.Checkpoint) > 0) _toastT = 1.6f;
        }
    }

    private void SetPassed()
    {
        _passed = true;
        Tint(_activeColor);
    }

    private void Tint(Color c)
    {
        if (_visual == null) return;
        foreach (Renderer r in _visual)
            if (r != null && r.material != null) r.material.color = c;
    }

    private void BuildVisual()
    {
        // Arco: dos postes y un travesaño (sin colisión).
        void Part(Vector3 pos, Vector3 scale)
        {
            GameObject g = RuntimeMaterials.Primitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.name = "CheckpointPart";
            g.transform.SetParent(transform, false);
            g.transform.localPosition = pos;
            g.transform.localScale    = scale;
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Part(new Vector3(-6f, 2f, 0f), new Vector3(0.4f, 5f, 0.4f));
        Part(new Vector3( 6f, 2f, 0f), new Vector3(0.4f, 5f, 0.4f));
        Part(new Vector3( 0f, 4.6f, 0f), new Vector3(12.4f, 0.4f, 0.4f));
    }

    private void OnGUI()
    {
        if (HudHider.IsHidden) return;   // (con la pausa o el game over no se ve ningún aviso)
        if (_toastT <= 0f) return;
        var st = new GUIStyle { font = UIFonts.LegacyTitle, fontSize = 32, alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = new Color(0.5f, 1f, 0.6f, Mathf.Clamp01(_toastT)) } };
        GUI.Label(new Rect(0f, Screen.height * 0.18f, Screen.width, 40f),
                  Loc.T($"CHECKPOINT  ·  te quedan {PlayerProgress.GetCount(ShopIds.Checkpoint)}", $"CHECKPOINT  ·  {PlayerProgress.GetCount(ShopIds.Checkpoint)} left"), st);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 2f, new Vector3(12f, 5f, 0.3f));
        UnityEditor.Handles.Label(transform.position + Vector3.up * 5.5f, "CHECKPOINT");
    }
#endif
}

/// <summary>
/// Estado del checkpoint activo (sobrevive a la recarga de la escena). Al morir, GameOverManager llama a TryRespawn:
/// si hay checkpoint activo en este nivel y te queda alguno comprado, se gasta y se recarga el nivel; el
/// StartSequenceManager lo recoge (ConsumePending) y te coloca aquí con la canción en su punto.
/// </summary>
public static class CheckpointSystem
{
    public struct State
    {
        public string  scene;
        public Vector3 pos;          // dónde reaparece el jugador (ya alineado al beat)
        public float   songOffset;   // segundo de la canción en el que se retoma
        public int     runCoins;     // monedas de la partida al pasarlo
    }

    private static State _active;
    private static bool  _hasActive, _pending;

    public static bool  HasActive      => _hasActive && _active.scene == SceneManager.GetActiveScene().name;
    public static bool  PendingRespawn => _pending && HasActive;
    public static State Active         => _active;

    /// <summary>Distancia a lo largo del eje de avance del nivel (BeatGridVisualizer; si no hay, +Z).</summary>
    public static float AlongOf(Vector3 p)
    {
        var grid = Object.FindAnyObjectByType<BeatGridVisualizer>();
        return grid != null ? grid.DistanceAlong(p) : p.z;
    }

    /// <summary>Activa un checkpoint en 'pos': calcula el segundo de la canción (alineado a beat entero).</summary>
    public static void Activate(Vector3 pos)
    {
        var grid = Object.FindAnyObjectByType<BeatGridVisualizer>();
        var pm   = Object.FindAnyObjectByType<PlayerMovement>();
        var bm   = BeatManager.Instance;
        float speed = grid != null ? grid.walkSpeed : (pm != null ? pm.walkSpeed : 12f);
        Vector3 fwd = grid != null ? grid.Forward : Vector3.forward;

        float along   = AlongOf(pos);
        float offset  = speed > 0f ? along / speed : 0f;
        float beatDur = bm != null && bm.BeatDuration > 0f ? bm.BeatDuration : 0.5f;
        float snapped = Mathf.Max(0f, Mathf.Round(offset / beatDur) * beatDur);   // en un beat entero
        pos += fwd * ((snapped - offset) * speed);                                  // y la posición a juego

        _active = new State
        {
            scene      = SceneManager.GetActiveScene().name,
            pos        = pos,
            songOffset = snapped,
            runCoins   = LevelCoins.RunCoins,
        };
        _hasActive = true;
        Debug.Log($"[Checkpoint] Activo en {pos} · canción {snapped:F2} s.");
    }

    /// <summary>Al morir: ¿se puede reaparecer en el checkpoint? (gasta uno). true → recargar el nivel.</summary>
    /// <summary>
    /// Checkpoints GRATIS (nivel tutorial / estudio, ver CourseBuilder): al morir siempre se reaparece en el último, sin
    /// gastar el consumible. Lo activa CourseBuilder en su escena y se apaga al cambiar de escena.
    /// </summary>
    public static bool FreeRespawn { get; set; }

    public static bool TryRespawn()
    {
        if (!HasActive) return false;
        if (!FreeRespawn && !PlayerProgress.TryUseConsumable(ShopIds.Checkpoint)) return false;
        _pending = true;
        return true;
    }

    /// <summary>
    /// Empezar YA desde el checkpoint activo (sin haber muerto): la escena actual lo recoge en StartSequenceManager.Start.
    /// Lo usa la práctica de un obstáculo del nivel tutorial.
    /// </summary>
    public static void ForcePending() { if (_hasActive) _pending = true; }

    /// <summary>La escena recargada recoge el checkpoint pendiente.</summary>
    public static State ConsumePending()
    {
        _pending = false;
        return _active;
    }

    /// <summary>Olvida el checkpoint (nivel terminado, reinicio manual…).</summary>
    public static void Clear()
    {
        _hasActive = false;
        _pending   = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        _hasActive = _pending = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (m == LoadSceneMode.Single && _hasActive && s.name != _active.scene) Clear();   // otro nivel
        if (m == LoadSceneMode.Single && s.name != _freeScene) FreeRespawn = false;
    }

    private static string _freeScene;

    /// <summary>Activa los checkpoints gratis en la escena actual (se apagan solos al cambiar de escena).</summary>
    public static void EnableFreeRespawn()
    {
        FreeRespawn = true;
        _freeScene = SceneManager.GetActiveScene().name;
    }
}
