using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ventana «Tools/Beat Tracking/Analizador de tempo»:
///   • Analiza una canción (o la del BeatManager de la escena abierta) y da su BPM y su primer beat.
///   • «Aplicar al BeatManager» escribe el BPM detectado en el BeatManager (con Deshacer).
///   • «Evaluar todas las canciones» analiza Assets/Songs y guarda BeatTracking_Evaluacion.csv (raíz del proyecto)
///     comparando con el BPM de referencia (el que se usó al componer: editable en la lista) → datos para la memoria.
/// El algoritmo está en BeatTracker.cs.
/// </summary>
public class BeatTrackingWindow : EditorWindow
{
    private AudioClip _clip;
    private BeatTracker.Result? _result;
    private string _status = "";
    private Vector2 _scroll;
    private float _maxSeconds = 120f;

    private const string SongsFolder = "Assets/Songs";
    private const string CsvName = "BeatTracking_Evaluacion.csv";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [MenuItem("Tools/Beat Tracking/Analizador de tempo")]
    private static void Open() => GetWindow<BeatTrackingWindow>("Beat Tracking");

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.LabelField("Detección automática del tempo (BPM) y del primer beat", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Analiza la envolvente de ataques de la canción y busca el tren de beats que mejor encaja. " +
                                "Ver BeatTracker.cs para el método.", MessageType.None);

        _maxSeconds = EditorGUILayout.Slider("Segundos a analizar", _maxSeconds, 10f, 300f);

        // ── Una canción ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Una canción", EditorStyles.boldLabel);
        _clip = (AudioClip)EditorGUILayout.ObjectField("Canción", _clip, typeof(AudioClip), false);

        BeatManager bm = FindAnyObjectByType<BeatManager>();
        AudioClip bmClip = SceneClip(bm);
        using (new EditorGUI.DisabledScope(bmClip == null))
            if (GUILayout.Button(bmClip != null ? $"Usar la canción del BeatManager de la escena ({bmClip.name}, {bm.Bpm:0.##} BPM)"
                                                : "Usar la canción del BeatManager de la escena (no hay)"))
            { _clip = bmClip; _result = null; }

        using (new EditorGUI.DisabledScope(_clip == null))
            if (GUILayout.Button("Analizar", GUILayout.Height(28))) AnalyzeOne();

        if (_result.HasValue)
        {
            var r = _result.Value;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("BPM detectado", r.bpm.ToString("0.00", Inv), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Desfase del primer beat", $"{r.firstBeat * 1000f:+0;-0} ms  (≈ 0 = la canción empieza justo en un beat)");
            EditorGUILayout.LabelField("Confianza", $"{r.confidence:0.00}  ({ConfidenceText(r.confidence)})");
            EditorGUILayout.LabelField("Pulso más fuerte", $"{r.strongestBpm:0.##} BPM" +
                (Mathf.Abs(r.strongestBpm - r.bpm) > 1f ? "  (se eligió un múltiplo más rápido: ver método)" : ""));
            EditorGUILayout.LabelField("Analizado", $"{r.analyzedSeconds:0.#} s");

            if (bm != null && SceneClip(bm) == _clip)
            {
                float err = (r.bpm - bm.Bpm) / bm.Bpm * 100f;
                EditorGUILayout.HelpBox($"El BeatManager tiene {bm.Bpm:0.##} BPM → diferencia {err:+0.00;-0.00} %.", MessageType.Info);
                if (GUILayout.Button($"Aplicar {Mathf.Round(r.bpm * 100f) / 100f:0.##} BPM al BeatManager"))
                    ApplyBpm(bm, Mathf.Round(r.bpm * 100f) / 100f);
                if (GUILayout.Button($"Aplicar redondeado ({Mathf.Round(r.bpm):0} BPM)"))
                    ApplyBpm(bm, Mathf.Round(r.bpm));
            }
        }

        // ── Evaluación ──
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Evaluación (para la memoria)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox($"Analiza todas las canciones de {SongsFolder} y las compara con su BPM de referencia " +
                                $"(el de composición; 0 = sin referencia). Guarda {CsvName} en la carpeta del proyecto.", MessageType.None);
        foreach (AudioClip c in SongClips())
        {
            float refBpm = GetReference(c.name);
            float v = EditorGUILayout.FloatField(c.name, refBpm);
            if (!Mathf.Approximately(v, refBpm)) EditorPrefs.SetFloat(RefKey(c.name), v);
        }
        if (GUILayout.Button("Evaluar todas las canciones", GUILayout.Height(28))) EvaluateAll();

        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    // ── Acciones ──────────────────────────────────────────────────────────────

    private void AnalyzeOne()
    {
        if (!TryAnalyze(_clip, out BeatTracker.Result r)) return;
        _result = r;
        _status = "";
    }

    private void EvaluateAll()
    {
        var sb = new StringBuilder("cancion;duracion_s;bpm_detectado;desfase_primer_beat_s;confianza;pulso_mas_fuerte;bpm_referencia;error_pct;acierto_exacto_4pct;acierto_octava_4pct\n");
        int n = 0, withRef = 0, exact = 0, octave = 0;
        var clips = SongClips();
        for (int i = 0; i < clips.Count; i++)
        {
            AudioClip c = clips[i];
            EditorUtility.DisplayProgressBar("Beat tracking", c.name, i / (float)clips.Count);
            if (!TryAnalyze(c, out BeatTracker.Result r)) continue;
            n++;
            float refBpm = GetReference(c.name);
            string err = "", ok1 = "", ok2 = "";
            if (refBpm > 0f)
            {
                withRef++;
                bool a1 = Mathf.Abs(r.bpm - refBpm) / refBpm <= 0.04f;
                bool a2 = a1;
                foreach (float m in new[] { 2f, 0.5f, 3f, 1f / 3f })
                    if (Mathf.Abs(r.bpm - refBpm * m) / (refBpm * m) <= 0.04f) a2 = true;
                if (a1) exact++;
                if (a2) octave++;
                err = ((r.bpm - refBpm) / refBpm * 100f).ToString("0.00", Inv);
                ok1 = a1 ? "1" : "0"; ok2 = a2 ? "1" : "0";
            }
            sb.AppendLine(string.Join(";", c.name, c.length.ToString("0.0", Inv), r.bpm.ToString("0.00", Inv),
                r.firstBeat.ToString("0.000", Inv), r.confidence.ToString("0.00", Inv), r.strongestBpm.ToString("0.00", Inv),
                refBpm > 0f ? refBpm.ToString("0.##", Inv) : "", err, ok1, ok2));
        }
        EditorUtility.ClearProgressBar();
        string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), CsvName);
        File.WriteAllText(path, sb.ToString());
        _status = $"{n} canciones analizadas. Con referencia: {withRef} → tempo exacto {exact}/{withRef}, " +
                  $"correcto salvo octava {octave}/{withRef}.\nGuardado en {path}";
        Debug.Log("[BeatTracking] " + _status);
    }

    private bool TryAnalyze(AudioClip clip, out BeatTracker.Result result)
    {
        result = default;
        if (clip == null) return false;
        if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            _status = $"«{clip.name}»: pon «Load Type = Decompress On Load» en su importación para poder analizarla.";
            return false;
        }
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0))
        {
            _status = $"No se pudieron leer las muestras de «{clip.name}».";
            return false;
        }
        result = BeatTracker.Analyze(data, clip.channels, clip.frequency, _maxSeconds);
        return true;
    }

    private static void ApplyBpm(BeatManager bm, float bpm)
    {
        var so = new SerializedObject(bm);
        SerializedProperty p = so.FindProperty("_bpm");
        if (p == null) return;
        p.floatValue = bpm;
        so.ApplyModifiedProperties();   // (con Deshacer)
        Debug.Log($"[BeatTracking] BeatManager → {bpm:0.##} BPM. Guarda la escena.");
    }

    // ── Utilidades ────────────────────────────────────────────────────────────

    private static AudioClip SceneClip(BeatManager bm)
    {
        if (bm == null) return null;
        var p = new SerializedObject(bm).FindProperty("_audioSource");
        var src = p != null ? p.objectReferenceValue as AudioSource : null;
        return src != null ? src.clip : null;
    }

    private static List<AudioClip> SongClips()
    {
        var list = new List<AudioClip>();
        if (!AssetDatabase.IsValidFolder(SongsFolder)) return list;
        foreach (string g in AssetDatabase.FindAssets("t:AudioClip", new[] { SongsFolder }))
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g));
            if (c != null) list.Add(c);
        }
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    private static string RefKey(string clip) => "BeatTracking_Ref_" + clip;

    /// <summary>BPM de referencia (guardado en el editor). Por defecto, el de composición de las canciones del juego.</summary>
    private static float GetReference(string clip)
    {
        if (EditorPrefs.HasKey(RefKey(clip))) return EditorPrefs.GetFloat(RefKey(clip));
        string up = clip.ToUpperInvariant();
        if (up.StartsWith("DESCENSO")) return 180f;   // Nivel 1
        if (up.StartsWith("EPLAC"))    return 120f;   // Nivel 2
        if (up.StartsWith("LDNM"))     return 70f;    // Nivel 3 + jefe
        if (up == "LOBBY1") return 60f;
        if (up == "LOBBY2") return 120f;
        if (up == "LOBBY3") return 100f;
        if (up == "GAMEOVER") return 120f;
        return 0f;
    }

    private static string ConfidenceText(float c) =>
        c >= 3f ? "pulso muy claro" : c >= 2f ? "pulso claro" : c >= 1.5f ? "dudoso: revísalo de oído" : "sin pulso claro";
}
