using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// REGISTRO DE DATOS del modo prueba: durante la prueba (StudySession.Active) y, después de completarla, también en el
/// modo normal y en la práctica libre (StudySession.PostStudyPlay), con el mismo código y la parte «libre».
/// Un «intento» va desde que empieza a sonar la música del nivel hasta que el jugador llega a la meta, muere o sale.
/// Por cada intento (y por cada cuestionario dentro del juego) se genera UNA línea de texto separada por «;» que:
///   1. se añade a un CSV local (Application.persistentDataPath/Estudio/&lt;código&gt;.csv) y
///   2. se envía al formulario oculto de Google Forms (StudySettings), que la guarda en una hoja de cálculo.
/// La primera fila de ese CSV (Header / QHeader) dice qué es cada columna.
/// </summary>
public class StudyTelemetry : MonoBehaviour
{
    public const string Header =
        "tipo;participante;fecha_utc;version;parte;nivel;config;orden;intento;resultado;duracion_s;beats_alcanzados;" +
        "precision_sync;latidos;aciertos;perfect;good;fallos;mejor_racha;desfase_medio_ms;desfase_abs_medio_ms;desfase_sd_ms;" +
        "energia_gastada_acciones;energia_perdida_fallos;energia_perdida_azul;energia_recuperada;energia_media;energia_min;" +
        "tiempo_azul_s;entradas_azul;saltos;dashes;slides;wallruns;monedas;mejoras_equipadas;" +
        "monedas_recogidas;monedas_nivel_total;contratos_activos;contratos_fallados;ronda;obstaculo;orden_obstaculos" + RowTail;

    /// <summary>Obstáculo en el que está el jugador ahora (lo pone CourseBuilder): con qué se ha chocado al morir.</summary>
    public static string CurrentObstacle = "";
    /// <summary>Orden de los obstáculos del recorrido de esta parte (lo pone CourseBuilder; vacío en el tutorial).</summary>
    public static string CourseOrder = "";

    public const string QHeader = "tipo;participante;fecha_utc;version;parte;nivel;config;orden;p1;p2;p3;p4;p5;p6;p7;p8;preferencia" + RowTail;

    public const string ContractHeader = "tipo;participante;fecha_utc;version;parte;nivel;evento;contrato;dificultad;recompensa;monedas" + RowTail;

    public const string ShopHeader = "tipo;participante;fecha_utc;version;parte;nivel;evento;objeto;precio;monedas_restantes;muescas_usadas;muescas_total;duracion_s" + RowTail;

    public const string AbandonHeader = "tipo;participante;fecha_utc;version;parte;nivel;intentos" + RowTail;

    /// <summary>Al final de CADA fila: nº de fila de la sesión (para quitar duplicados) y verificación (HMAC).</summary>
    private const string RowTail = ";fila;verificacion";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static StudyTelemetry _runner;

    // ── Intento en curso ──────────────────────────────────────────────────────
    private static bool  _open;
    private static float _startSong;
    private static int   _n, _perf, _good, _miss;
    private static double _sumOff, _sumAbs, _sumSq;
    private static float _actionCost, _missLost, _blueLost, _recovered;
    private static double _energyIntegral; private static float _energyTime, _energyMin, _blueTime;
    // Copia de lo último visto (por si el intento se cierra al cambiar de escena, cuando ya no existe el nivel).
    private static string _level;
    private static float _songNow, _beatDur = 1f, _maxE = 100f;
    private static int _presses, _bestStreak, _blueEntries, _jumps, _dashes, _slides, _wallruns;
    private static int _coins; private static string _equipped = "";
    private static int _runCoins, _levelCoins; private static string _contracts = "", _broken = "";

    public static bool IsOpen => _open;
    private static string _part = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_runner != null) return;
        var go = new GameObject("StudyTelemetry");
        DontDestroyOnLoad(go);
        _runner = go.AddComponent<StudyTelemetry>();
        SceneManager.activeSceneChanged += (from, to) => { if (_open) EndAttempt("salida"); };
    }

    // ── Llamadas desde el juego ───────────────────────────────────────────────

    /// <summary>Clic del jugador: desfase con el beat (s, + = tarde) y resultado.</summary>
    public static void Click(float offsetSeconds, TimingResult r)
    {
        if (!_open) return;
        _n++;
        if (r == TimingResult.Perfect) _perf++; else if (r == TimingResult.Good) _good++; else _miss++;
        double ms = offsetSeconds * 1000.0;
        _sumOff += ms; _sumAbs += Math.Abs(ms); _sumSq += ms * ms;
    }

    public static void ActionCost(float amount) { if (_open) _actionCost += amount; }
    public static void MissPenalty(float amount) { if (_open) _missLost += amount; }
    public static void BlueLost(float amount)   { if (_open) _blueLost += amount; }
    public static void Recovered(float amount)  { if (_open) _recovered += amount; }

    /// <summary>Cierra el intento (completado, muerte…) y lo registra. 'accuracy' = % de sincronía (solo si llega a la meta).</summary>
    public static void EndAttempt(string result, float accuracy = -1f)
    {
        if (!_open) return;
        _open = false;
        StudySession.NotifyAttemptEnded(result == "completado");

        Snapshot();
        float dur = Mathf.Max(0f, _songNow - _startSong);
        float beats = _beatDur > 0f ? _songNow / _beatDur : 0f;
        double mean = _n > 0 ? _sumOff / _n : 0, meanAbs = _n > 0 ? _sumAbs / _n : 0;
        double sd = _n > 1 ? Math.Sqrt(Math.Max(0, (_sumSq - _n * mean * mean) / (_n - 1))) : 0;
        float maxE = _maxE;

        var f = new List<string>
        {
            "intento", Who, DateTime.UtcNow.ToString("s", Inv), StudySettings.Version,
            _part, _level, StudyConfig.Current.ToString(),
            OrderLabel, StudySession.Attempts.ToString(Inv), result,
            F(dur), F(beats), accuracy >= 0f ? F(accuracy) : "",
            _presses.ToString(Inv), (_perf + _good).ToString(Inv), _perf.ToString(Inv), _good.ToString(Inv),
            _miss.ToString(Inv), _bestStreak.ToString(Inv), F(mean), F(meanAbs), F(sd),
            F(_actionCost / maxE * 100f), F(_missLost / maxE * 100f), F(_blueLost / maxE * 100f), F(_recovered / maxE * 100f),
            F(_energyTime > 0f ? (float)(_energyIntegral / _energyTime) * 100f : 0f), F(_energyMin * 100f),
            F(_blueTime), _blueEntries.ToString(Inv), _jumps.ToString(Inv), _dashes.ToString(Inv),
            _slides.ToString(Inv), _wallruns.ToString(Inv), _coins.ToString(Inv), _equipped,
            _runCoins.ToString(Inv), _levelCoins.ToString(Inv), _contracts, _broken,
            StudySession.Active && !Extra ? StudySession.Current.round.ToString(Inv) : "", Clean(CurrentObstacle), Clean(CourseOrder),
        };
        Submit(string.Join(";", f), Header);
    }

    // ── Quién, qué parte y si se registra ──
    /// <summary>¿Se registra ahora? Durante la prueba (no en su práctica libre) o jugando después de completarla.</summary>
    private static bool Recording => (StudySession.Active && !StudySession.FreePlay) || StudySession.PostStudyPlay;
    private static bool Extra => StudySession.PostStudyPlay;
    private static string Who => StudySession.Active && !string.IsNullOrEmpty(StudySession.Participant)
                                 ? StudySession.Participant : StudySession.SavedParticipant;
    private static string PartLabel => Extra ? "libre" : (StudySession.Step + 1).ToString(Inv);
    private static string OrderLabel => StudySession.Active ? (StudySession.OrderAB ? "AB" : "BA") : StudySession.SavedOrder;
    private static bool Simulated => StudySession.Active ? StudySession.Simulated
                                     : (StudySession.SavedSimulated || Who.StartsWith("TEST-"));

    private static string Clean(string s) => (s ?? "").Replace(";", ",");

    /// <summary>Respuestas del cuestionario corto del juego (1-7) tras una parte.</summary>
    public static void Questionnaire(int part, string level, StudyConfig.Profile cfg, int[] answers, string preference)
    {
        var f = new List<string>
        {
            "cuestionario", Who, DateTime.UtcNow.ToString("s", Inv), StudySettings.Version,
            part.ToString(Inv), level, cfg.ToString(), OrderLabel,
        };
        for (int i = 0; i < 8; i++) f.Add(i < answers.Length && answers[i] > 0 ? answers[i].ToString(Inv) : "");
        f.Add(preference ?? "");
        Submit(string.Join(";", f), QHeader);
    }

    /// <summary>Contratos (partes 4-5): firmar, rescindir, cumplido y cobrar.</summary>
    public static void ContractEvent(string evt, string id)
    {
        if (!Recording) return;
        ContractDef c = ContractCatalog.Get(id);
        var f = new List<string>
        {
            "contrato", Who, DateTime.UtcNow.ToString("s", Inv), StudySettings.Version,
            PartLabel, SceneManager.GetActiveScene().name, evt, id,
            c != null ? c.tier.ToString(Inv) : "", c != null ? c.reward.ToString(Inv) : "", PlayerProgress.Coins.ToString(Inv),
        };
        Submit(string.Join(";", f), ContractHeader);
    }

    /// <summary>
    /// El participante se SALTA una sección (botón del game over tras morir 3 veces en ella): fila «salto_seccion» con
    /// el nº de checkpoint desde el que salta y el obstáculo en el que estaba.
    /// </summary>
    public static void SectionSkipped(int checkpoint, string obstacle)
    {
        if (!Recording) return;
        var f = new List<string>
        {
            "salto_seccion", Who, DateTime.UtcNow.ToString("s", Inv), StudySettings.Version,
            PartLabel, SceneManager.GetActiveScene().name, StudySession.Attempts.ToString(Inv),
            checkpoint.ToString(Inv), Clean(obstacle),
        };
        Submit(string.Join(";", f), null);
    }

    /// <summary>El participante abandona la prueba desde la pausa (queda descalificado).</summary>
    public static void Abandon()
    {
        if (!StudySession.Active) return;
        var f = new List<string>
        {
            "abandono", Who, DateTime.UtcNow.ToString("s", Inv), StudySettings.Version,
            (StudySession.Step + 1).ToString(Inv), SceneManager.GetActiveScene().name, StudySession.Attempts.ToString(Inv),
        };
        Submit(string.Join(";", f), null);
    }

    /// <summary>Tienda (partes 4-5): abrir/cerrar (con duración), compras, equipar/quitar y muescas.</summary>
    public static void ShopEvent(string evt, string item, int price, float duration = -1f)
    {
        if (!Recording) return;
        var f = new List<string>
        {
            "tienda", Who, DateTime.UtcNow.ToString("s", Inv), StudySettings.Version,
            PartLabel, SceneManager.GetActiveScene().name, evt, item, price.ToString(Inv),
            PlayerProgress.Coins.ToString(Inv), Notches.Used.ToString(Inv), Notches.Total.ToString(Inv),
            duration >= 0f ? F(duration) : "",
        };
        Submit(string.Join(";", f), ShopHeader);
    }

    private static string F(double v) => v.ToString("0.###", Inv);

    // ── Muestreo continuo ─────────────────────────────────────────────────────

    private void Update()
    {
        if (!Recording) { if (_open) EndAttempt("salida"); return; }
        BeatManager bm = BeatManager.Instance;
        EnergyManager em = EnergyManager.Instance;
        bool playing = bm != null && bm.IsPlaying && !LevelEndManager.Finished && (StudySession.InLevel || Extra);

        if (!_open && playing && !IsFreeroam() && !PlayerDead()) BeginAttempt(bm);
        if (!_open) return;
        Snapshot();
        if (em == null) return;

        float e = em.MaxSyringe > 0f ? em.Energy / em.MaxSyringe : 0f;   // 0..1 rojo, 1..2 azul
        float dt = Time.deltaTime;
        _energyIntegral += e * dt; _energyTime += dt;
        if (e < _energyMin) _energyMin = e;
        if (em.IsBlueMode) _blueTime += dt;
    }

    /// <summary>Copia los valores del nivel mientras existe.</summary>
    private static void Snapshot()
    {
        BeatManager bm = BeatManager.Instance;
        // (Solo mientras suena la música del nivel: al cargar otra escena RunStats se pone a cero.)
        if (bm == null || !bm.IsPlaying) return;
        _songNow = bm.SongTime; _beatDur = bm.BeatDuration;
        _runCoins = LevelCoins.RunCoins;
        var broken = new List<string>();
        foreach (ContractDef c in ContractCatalog.ActiveHere()) if (c.broken != null && c.broken()) broken.Add(c.id);
        _broken = string.Join("|", broken);
        if (EnergyManager.Instance != null) _maxE = EnergyManager.Instance.MaxSyringe;
        {
            _presses = RunStats.HeartPresses; _bestStreak = RunStats.BestStreak; _blueEntries = RunStats.BlueEntries;
            _jumps = RunStats.Jumps; _dashes = RunStats.Dashes; _slides = RunStats.Slides; _wallruns = RunStats.Wallruns;
        }
    }

    private static GameOverManager _gom;
    private static bool PlayerDead()
    {
        if (_gom == null) _gom = FindAnyObjectByType<GameOverManager>();
        return _gom != null && _gom.IsDead;
    }

    private static PlayerMovement _pm;
    private static bool IsFreeroam()
    {
        if (_pm == null) _pm = FindAnyObjectByType<PlayerMovement>();
        return _pm != null && _pm.IsFreeroam;
    }

    private static void BeginAttempt(BeatManager bm)
    {
        _open = true;
        _level = SceneManager.GetActiveScene().name;
        _part = PartLabel;
        _coins = PlayerProgress.Coins;
        _equipped = PlayerProgress.Suspended ? "" : string.Join("|", PlayerProgress.EquippedUpgrades);
        _levelCoins = FindObjectsByType<Coin>().Length;   // monedas que hay en el nivel (para el % recogido)
        _runCoins = 0;
        var active = ContractCatalog.ActiveHere();
        _contracts = string.Join("|", active.ConvertAll(c => c.id));
        _broken = "";
        _startSong = bm.SongTime;
        _songNow = _startSong;
        _n = _perf = _good = _miss = 0;
        _sumOff = _sumAbs = _sumSq = 0;
        _actionCost = _missLost = _blueLost = _recovered = 0f;
        _energyIntegral = 0; _energyTime = 0f; _energyMin = 2f; _blueTime = 0f;
        StudySession.NotifyAttemptStarted();
    }

    // ── Guardado y envío ──────────────────────────────────────────────────────

    private static int _rowSeq;
    private static string _rowSession;

    private static void Submit(string line, string header)
    {
        // Sin saltos de línea (cada envío es UNA fila) y con nº de fila + verificación al final.
        line = line.Replace("\r", " ").Replace("\n", " ");
        // (El nº de fila se guarda por código: al volver a abrir el juego para jugar en modo normal sigue contando, así
        //  no se repite y no parece un duplicado.)
        string who = Who;
        if (_rowSession != who) { _rowSession = who; _rowSeq = PlayerPrefs.GetInt("Study.Row." + who, 0); }
        line += ";" + (++_rowSeq).ToString(Inv);
        PlayerPrefs.SetInt("Study.Row." + who, _rowSeq); PlayerPrefs.Save();
        line += ";" + Sign(line);

        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "Estudio");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{who}.csv");
            if (!File.Exists(path)) File.AppendAllText(path, Header + "\n" + QHeader + "\n" + ShopHeader + "\n" + ContractHeader + "\n" + AbandonHeader + "\n");
            File.AppendAllText(path, line + "\n");
        }
        catch (Exception e) { Debug.LogWarning($"[Estudio] No se pudo guardar el CSV: {e.Message}"); }

        Debug.Log($"[Estudio] {line}");
        if (!Simulated && _runner != null && IsSafeUrl(StudySettings.DataFormUrl) && !string.IsNullOrEmpty(StudySettings.DataEntryId))
            _runner.StartCoroutine(Post(line));
    }

    /// <summary>Solo HTTPS y solo al host permitido (Google Forms).</summary>
    public static bool IsSafeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri u) && u.Scheme == Uri.UriSchemeHttps &&
        string.Equals(u.Host, StudySettings.AllowedHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>Verificación de la fila: 8 primeros caracteres de HMAC-SHA256(fila, RowKey).</summary>
    private static string Sign(string line)
    {
        using (var h = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(StudySettings.RowKey)))
        {
            byte[] mac = h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(line));
            return BitConverter.ToString(mac, 0, 4).Replace("-", "").ToLowerInvariant();
        }
    }

    private static IEnumerator Post(string line)
    {
        for (int tries = 0; tries < 3; tries++)
        {
            var form = new WWWForm();
            form.AddField(StudySettings.DataEntryId, line);
            using (UnityWebRequest req = UnityWebRequest.Post(StudySettings.DataFormUrl, form))
            {
                req.timeout = StudySettings.RequestTimeoutSeconds;
                req.redirectLimit = 3;
                req.downloadHandler = new DownloadHandlerBuffer();   // (la respuesta no se lee ni se interpreta)
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) yield break;
#if UNITY_WEBGL && !UNITY_EDITOR
                // En la web, Google no deja leer la respuesta (CORS) pero la fila SÍ llega: no se reintenta (duplicados).
                yield break;
#endif
                // Error del servidor que rechaza la fila (4xx): reintentar no sirve.
                if (req.result == UnityWebRequest.Result.ProtocolError && req.responseCode >= 400 && req.responseCode < 500)
                {
                    Debug.LogWarning($"[Estudio] El formulario rechazó la fila (HTTP {req.responseCode}). Queda en el CSV local.");
                    yield break;
                }
                Debug.LogWarning($"[Estudio] Envío fallido ({req.error}), reintento {tries + 1}/3.");
            }
            yield return new WaitForSecondsRealtime(3f);
        }
    }
}
