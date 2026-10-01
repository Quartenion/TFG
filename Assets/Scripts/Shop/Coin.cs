using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MONEDA recolectable. Colócala en el nivel (objeto vacío + este componente): si no tiene malla propia, se
/// genera una moneda dorada. Gira, late al ritmo y se recoge al pasar cerca (sistema propio, sin triggers).
///   • Está ESCONDIDA bajo el suelo y SALE cuando el jugador está a 2 beats de ella.
///   • El REBOBINADO la devuelve (si la cogiste en el tramo rebobinado) y te quita esa moneda.
///   • Las de la partida se COBRAN al terminar el nivel; con un GAME OVER también te las quedas (se cobran al
///     morir), aunque al reiniciar el nivel vuelven a estar todas.
/// Con la mejora «Imán de monedas» vuelan hacia el jugador.
/// </summary>
[AddComponentMenu("Level/Coin")]
public class Coin : MonoBehaviour, IRewindable
{
    [Tooltip("Monedas que vale.")]
    [SerializeField] private int _value = 1;
    [Tooltip("Distancia (m) al centro del jugador para recogerla.")]
    [SerializeField] private float _collectRadius = 1.8f;
    [Tooltip("Grados que gira en CADA BEAT (a golpes, al ritmo).")]
    [SerializeField] private float _degreesPerBeat = 45f;
    [Tooltip("Parte del beat que tarda en hacer el giro (0.2 = gira rápido justo en el beat y se queda quieta).")]
    [Range(0.05f, 1f)] [SerializeField] private float _turnFraction = 0.2f;
    [Tooltip("Latido de escala en cada beat (0 = nada).")]
    [SerializeField] private float _beatPulse = 0.18f;
    [Tooltip("Sonido al recogerla (opcional).")]
    [SerializeField] private AudioClip _pickupSound;
    [Tooltip("Sonidos de recogida (p. ej. 2). Se alternan y, además, el TONO va subiendo si coges monedas seguidas\n" +
             "(como una escala) y varía un poco al azar: así parecen muchos sonidos distintos. Vacío = 'Pickup Sound'.")]
    [SerializeField] private AudioClip[] _pickupSounds;
    [Range(0f, 1f)][SerializeField] private float _pickupVolume = 0.6f;
    [Tooltip("Color de la moneda generada por código.")]
    [SerializeField] private Color _color = new Color(1f, 0.78f, 0.15f);

    [Header("Salir del suelo")]
    [Tooltip("Beats de antelación: la moneda sale del suelo cuando el jugador está a esta distancia (en beats).")]
    [SerializeField] private float _spawnBeatsAhead = 4f;
    [Tooltip("Metros por debajo de su sitio desde los que sale.")]
    [SerializeField] private float _riseDepth = 1.5f;
    [Tooltip("Beats que tarda en salir.")]
    [SerializeField] private float _riseBeats = 0.5f;

    private Transform _player;
    private CharacterController _cc;
    private Transform _visual;
    private Vector3   _visualScale0, _visualScaleBase;
    private bool      _taken;
    private float     _takeT;
    private Vector3   _takeFrom;
    // «Imán de monedas»: se consulta EN CADA FRAME (antes se leía una sola vez al cargar el nivel, así que si lo
    // comprabas en la tienda del lobby —o lo activabas/desactivabas— no hacía nada hasta reiniciar el nivel).
    private static int   s_magnetFrame = -1;
    private static float s_magnetRadius;
    private static float MagnetRadius
    {
        get
        {
            if (s_magnetFrame != Time.frameCount)
            {
                s_magnetFrame = Time.frameCount;
                int magnet = PlayerProgress.GetLevel(ShopIds.CoinMagnet);
                s_magnetRadius = magnet >= 2 ? 9f : magnet == 1 ? 5f : 0f;
                // Solo con el nivel EN MARCHA (canción sonando, fuera del lobby y ya en la línea de salida): antes de
                // empezar no atrae nada, aunque haya monedas dentro del radio.
                BeatManager bm = BeatManager.Instance;
                bool playing = bm != null && bm.IsPlaying && s_pm != null && !s_pm.IsFreeroam && !s_pm.IsMovingToStart;
                if (!playing) s_magnetRadius = 0f;
            }
            return s_magnetRadius;
        }
    }

    // Sitio de la moneda guardado RESPECTO A SU PADRE: si es hija de algo que se mueve (p. ej. un bloque con
    // BeatOscillator), su sitio se mueve con él (antes se guardaba en coordenadas de mundo y se quedaba atrás).
    private Vector3 _baseLocal;
    private Vector3 _basePos => transform.parent != null ? transform.parent.TransformPoint(_baseLocal) : _baseLocal;
    private string  _uid;
    private bool    _rising, _risen;
    private float   _riseT;
    private int     _gotValue;
    private double  _collectSongTime = double.MaxValue;
    private BeatGridVisualizer _grid;
    private Quaternion _rot0;
    private static PlayerMovement     s_pm;
    private static BeatGridVisualizer s_grid;
    private static int                s_gridSearchFrame = -1;

    private void Start()
    {
        // (Búsquedas compartidas entre TODAS las monedas: con cientos de monedas, buscar en cada una hacía lenta
        //  la carga del nivel y la reaparición en el checkpoint.)
        if (s_pm == null) s_pm = FindAnyObjectByType<PlayerMovement>();
        if (s_grid == null && s_gridSearchFrame != Time.frameCount)
        {
            s_grid = FindAnyObjectByType<BeatGridVisualizer>();
            s_gridSearchFrame = Time.frameCount;
        }
        if (s_pm != null) { _player = s_pm.transform; _cc = s_pm.GetComponent<CharacterController>(); }
        _grid = s_grid;

        _baseLocal = transform.localPosition;
        _rot0    = transform.rotation;
        _uid = $"{Mathf.RoundToInt(_basePos.x * 20)}:{Mathf.RoundToInt(_basePos.y * 20)}:{Mathf.RoundToInt(_basePos.z * 20)}";

        // Visual: el del propio objeto/hijos o una moneda dorada por código.
        Renderer r = GetComponentInChildren<Renderer>();
        if (r == null) _visual = BuildVisual();
        else           _visual = r.transform == transform ? transform : r.transform;
        _visualScaleBase = _visualScale0 = _visual.localScale;


        CoinHUD.Ensure();

        // Reaparición en un checkpoint: las que ya cogiste en esta partida no vuelven a salir.
        if (LevelCoins.IsCollected(_uid)) { _taken = true; gameObject.SetActive(false); return; }

        HideUnderground();
    }

    private void HideUnderground()
    {
        _rising = _risen = false;
        _riseT = 0f;
        transform.position = _basePos + Vector3.down * _riseDepth;
        _visual.localScale = Vector3.zero;
    }

    private Transform BuildVisual()
    {
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        c.name = "CoinVisual";
        Destroy(c.GetComponent<Collider>());                   // sin física: la recogida es por distancia
        c.transform.SetParent(transform, false);
        c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // de canto, mirando al jugador
        c.transform.localScale    = new Vector3(1.1f, 0.08f, 1.1f);
        var mr = c.GetComponent<Renderer>();
        var m  = new Material(mr.sharedMaterial) { color = _color };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", _color * 0.6f);
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return c.transform;
    }

    private float BeatDur()
    {
        BeatManager bm = BeatManager.Instance;
        return bm != null && bm.BeatDuration > 0f ? bm.BeatDuration : 0.5f;
    }

    /// <summary>¿El jugador está a ≤ N beats de la moneda (a lo largo del nivel)?</summary>
    private bool PlayerWithinRiseRange()
    {
        if (_player == null) return true;
        float cellM = _grid != null ? _grid.CellSize : 6f * BeatDur();
        float range = Mathf.Max(0.1f, _spawnBeatsAhead) * cellM;
        if (_grid != null)
        {
            float ahead = Vector3.Dot(_basePos - _player.position, _grid.Forward);
            return ahead <= range;   // (también si ya la ha pasado)
        }
        return Vector3.Distance(_basePos, _player.position) <= range;
    }

    private void Update()
    {
        if (_taken) { UpdatePickup(); return; }

        // 1) Escondida bajo el suelo hasta que el jugador está a 2 beats; entonces sale (con un pequeño rebote).
        if (!_risen)
        {
            if (!_rising)
            {
                if (!PlayerWithinRiseRange()) return;
                _rising = true;
            }
            _riseT += Time.deltaTime / Mathf.Max(0.05f, _riseBeats * BeatDur());
            float u = Mathf.Clamp01(_riseT);
            const float c1 = 1.70158f, c3 = c1 + 1f;                  // ease-out back
            float e = 1f + c3 * Mathf.Pow(u - 1f, 3f) + c1 * Mathf.Pow(u - 1f, 2f);
            transform.position = Vector3.LerpUnclamped(_basePos + Vector3.down * _riseDepth, _basePos, e);
            _visual.localScale = _visualScale0 * Mathf.Clamp01(u * 2f);
            if (u >= 1f) { _risen = true; transform.localPosition = _baseLocal; }
        }

        // 2) Giro A GOLPES (X grados en cada beat) + latido al ritmo.
        BeatManager bm = BeatManager.Instance;
        if (bm != null && bm.ScheduledStartDSP > 0.0 && bm.BeatDuration > 0f)
        {
            double bt = (AudioSettings.dspTime - bm.ScheduledStartDSP) / bm.BeatDuration;
            if (bt < 0.0) bt = 0.0;
            float frac = (float)(bt - Math.Floor(bt));
            float k = Mathf.Clamp01(frac / _turnFraction);
            k = 1f - (1f - k) * (1f - k) * (1f - k);                 // ease-out: arranca rápido en el beat y frena
            float yaw = _degreesPerBeat * ((float)Math.Floor(bt) + k);
            transform.rotation = Quaternion.AngleAxis(yaw % 360f, Vector3.up) * _rot0;
        }
        if (_risen && _beatPulse > 0f && bm != null && bm.ScheduledStartDSP > 0.0 && bm.BeatDuration > 0f)
        {
            double b = (AudioSettings.dspTime - bm.ScheduledStartDSP) / bm.BeatDuration;
            float  p = (float)(b - Math.Floor(b));
            _visual.localScale = _visualScale0 * (1f + _beatPulse * Mathf.Exp(-p * 6f));
        }

        if (_player == null) return;
        Vector3 center = PlayerCenter();
        float d = Vector3.Distance(center, transform.position);

        // Imán: se acerca volando (ya fuera del suelo).
        float mr = MagnetRadius;
        if (_risen && mr > 0f && d < mr)
            transform.position = Vector3.MoveTowards(transform.position, center, (25f + (mr - d) * 6f) * Time.deltaTime);

        if (d <= _collectRadius) Collect();
    }

    private Vector3 PlayerCenter()
    {
        if (_cc == null) return _player.position + Vector3.up;
        return _player.position + Vector3.up * (_cc.center.y * _player.lossyScale.y);
    }

    private void Collect()
    {
        _taken    = true;
        _takeT    = 0f;
        _takeFrom = transform.position;
        _collectSongTime = SongClock();
        // «Moneda de la suerte» (tienda): 10 % de valer el doble.
        bool lucky = PlayerProgress.GetLevel(ShopIds.LuckyCoin) > 0 && UnityEngine.Random.value < 0.1f;
        _gotValue = lucky ? _value * 2 : _value;
        LevelCoins.Collect(_gotValue, _uid);
        if (lucky) _visualScale0 = _visualScaleBase * 1.6f;   // (se ve más grande al recogerla)
        CoinPickupAudio.Play(_pickupSounds, _pickupSound, _pickupVolume);
    }

    /// <summary>Animación de recogida: salta hacia el jugador encogiéndose y desaparece.</summary>
    private void UpdatePickup()
    {
        _takeT += Time.deltaTime / 0.2f;
        Vector3 target = _player != null ? PlayerCenter() + Vector3.up * 0.5f : _takeFrom + Vector3.up;
        transform.position = Vector3.Lerp(_takeFrom, target, _takeT);
        _visual.localScale = _visualScale0 * Mathf.Max(0f, 1f - _takeT);
        // Se desactiva (no se destruye) para que el Rebobinado pueda devolverla.
        if (_takeT >= 1f) gameObject.SetActive(false);
    }

    // ── Rebobinado ────────────────────────────────────────────────────────────

    private static double SongClock()
    {
        BeatManager bm = BeatManager.Instance;
        return bm != null && bm.ScheduledStartDSP > 0.0 ? AudioSettings.dspTime - bm.ScheduledStartDSP : double.MaxValue;
    }

    /// <summary>REBOBINADO: si el reloj vuelve a antes de cogerla, la moneda vuelve (bajo el suelo) y se descuenta.</summary>
    public void OnTimeRewind()
    {
        if (!_taken || _visual == null || SongClock() >= _collectSongTime) return;
        LevelCoins.Uncollect(_gotValue, _uid);
        _taken = false;
        _collectSongTime = double.MaxValue;
        _visualScale0 = _visualScaleBase;
        gameObject.SetActive(true);
        HideUnderground();   // (vuelve a salir cuando el jugador esté a 2 beats)
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(Application.isPlaying && _visual != null ? _basePos : transform.position, 0.55f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, _collectRadius);
    }
#endif
}

/// <summary>
/// Monedas recogidas en la PARTIDA ACTUAL. Al terminar el nivel, LevelEndManager las COBRA (BankRun) y pasan al
/// monedero (PlayerProgress). Con un GAME OVER también se cobran (BankOnDeath): te las quedas aunque mueras, y al
/// reiniciar el nivel vuelven a estar todas las monedas. Al reaparecer en un CHECKPOINT se conservan (y las ya
/// cogidas no vuelven a salir).
/// </summary>
public static class LevelCoins
{
    public static int RunCoins { get; private set; }
    public static event Action<int> OnRunCoinsChanged;

    private static readonly HashSet<string> _collected = new HashSet<string>();   // monedas del nivel ya cogidas

    public static bool IsCollected(string uid) => uid != null && _collected.Contains(uid);

    /// <summary>Suma monedas (bonos de la tienda, contratos…).</summary>
    public static void Collect(int value) => Collect(value, null);

    /// <summary>Suma monedas de una moneda concreta del nivel (se recuerda para no volver a sacarla).</summary>
    public static void Collect(int value, string uid)
    {
        RunCoins += Mathf.Max(0, value);
        if (uid != null) _collected.Add(uid);
        CoinHUD.Ensure();
        OnRunCoinsChanged?.Invoke(RunCoins);
    }

    /// <summary>Rebobinado: se devuelve una moneda (y se quita del contador).</summary>
    public static void Uncollect(int value, string uid)
    {
        RunCoins = Mathf.Max(0, RunCoins - Mathf.Max(0, value));
        if (uid != null) _collected.Remove(uid);
        OnRunCoinsChanged?.Invoke(RunCoins);
    }

    /// <summary>
    /// Cobra las monedas de la partida (con la mejora «Bolsa de oro»: ×1.5 si la precisión ≥ 80 %).
    /// Devuelve cuántas se han añadido al monedero.
    /// </summary>
    public static int BankRun(float accuracyPercent)
    {
        int n = RunCoins;
        if (PlayerProgress.GetLevel(ShopIds.CoinBonus) > 0 && accuracyPercent >= 80f)
            n = Mathf.CeilToInt(n * 1.5f);
        PlayerProgress.AddCoins(n);
        RunCoins = 0;
        _collected.Clear();
        OnRunCoinsChanged?.Invoke(0);
        return n;
    }

    /// <summary>GAME OVER: las monedas cogidas pasan al monedero igualmente (sin bonos). Devuelve cuántas.</summary>
    public static int BankOnDeath()
    {
        int n = RunCoins;
        if (n > 0) PlayerProgress.AddCoins(n);
        RunCoins = 0;
        _collected.Clear();
        OnRunCoinsChanged?.Invoke(0);
        return n;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        RunCoins = 0;
        _collected.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (m != LoadSceneMode.Single) return;
        if (CheckpointSystem.PendingRespawn) return;   // checkpoint: misma partida, se conservan
        RunCoins = 0;                                   // escena nueva (o reinicio): se empieza de cero
        _collected.Clear();
        OnRunCoinsChanged?.Invoke(0);
    }
}

/// <summary>
/// Sonido de recogida de monedas con VARIEDAD: alterna entre los clips que le pases y cambia el tono:
///   • Monedas seguidas (menos de 'ComboWindow' s entre una y otra) → cada una un pelín más aguda, siguiendo una
///     escala (do-re-mi…), y al llegar arriba vuelve a empezar. Suena a "racha".
///   • Un poco de variación aleatoria en cada una.
/// Usa unas pocas fuentes 2D propias (PlayClipAtPoint no permite cambiar el tono).
/// </summary>
public static class CoinPickupAudio
{
    private const float ComboWindow = 0.9f;                          // s máx. entre monedas para seguir la escala
    private static readonly float[] Scale = { 0, 2, 4, 5, 7, 9, 11, 12 };   // semitonos (escala mayor)
    private const float RandomSemitones = 0.35f;

    private static AudioSource[] _sources;
    private static int _next, _combo, _clipIdx;
    private static float _lastTime = -10f;

    public static void Play(AudioClip[] clips, AudioClip fallback, float volume)
    {
        AudioClip clip = null;
        if (clips != null && clips.Length > 0)
        {
            for (int tries = 0; tries < clips.Length && clip == null; tries++)
            {
                clip = clips[_clipIdx % clips.Length];
                _clipIdx++;
            }
        }
        if (clip == null) clip = fallback;
        if (clip == null) return;

        EnsureSources();
        float now = Time.time;
        _combo = now - _lastTime <= ComboWindow ? _combo + 1 : 0;
        _lastTime = now;

        float semis = Scale[_combo % Scale.Length] + UnityEngine.Random.Range(-RandomSemitones, RandomSemitones);
        AudioSource src = _sources[_next];
        _next = (_next + 1) % _sources.Length;
        src.pitch = Mathf.Pow(2f, semis / 12f);
        src.PlayOneShot(clip, volume);
    }

    private static void EnsureSources()
    {
        if (_sources != null && _sources[0] != null) return;
        var go = new GameObject("CoinPickupAudio");
        _sources = new AudioSource[6];
        for (int i = 0; i < _sources.Length; i++)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.priority = 64;
            _sources[i] = s;
        }
    }
}
