using UnityEngine;

/// <summary>
/// Overlay de debug que muestra si el jugador va sincronizado con la música.
/// Aparece en la esquina superior izquierda de la Game View durante el play mode.
/// Solo existe en el editor — no afecta a las builds.
///
/// No hace falta asignarlo a ningún GameObject: se crea solo al entrar en play mode.
/// </summary>
public class PlayerSyncDisplay : MonoBehaviour
{
    // Se auto-crea al entrar en play mode
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
#if UNITY_EDITOR
        var go = new GameObject("[PlayerSyncDebug]");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerSyncDisplay>();
#endif
    }

#if UNITY_EDITOR
    private BeatGridVisualizer _grid;
    private BeatManager        _bm;
    private PlayerMovement     _pm;
    private CharacterController _cc;
    private Texture2D          _speedTex;

    // Estilos (se inicializan la primera vez que se llama a OnGUI)
    private GUIStyle _bgStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _valueStyle;
    private Texture2D _bgTex;

    private void Start()
    {
        _grid = FindAnyObjectByType<BeatGridVisualizer>();
        _bm   = BeatManager.Instance;
        _pm   = FindAnyObjectByType<PlayerMovement>();
        _cc   = _pm != null ? _pm.GetComponent<CharacterController>() : null;
    }

    private void OnGUI()
    {
        if (_bm == null || _grid == null || _pm == null) return;
        if (_bm.ScheduledStartDSP <= 0.0) return;
        if (!_bm.IsPlaying) return;   // pausa o música no iniciada: no mostrar

        InitStyles();

        // ── Calcular desfase ──────────────────────────────────────────────────
        double now          = AudioSettings.dspTime;
        float  expectedDist = (float)((now - _bm.ScheduledStartDSP) * _grid.walkSpeed);
        float  actualDist   = Vector3.Dot(_pm.transform.position - _grid.Origin, _grid.Forward);
        float  deltaDist    = actualDist - expectedDist;
        float  cell         = _grid.CellSize;
        float  deltaBeats   = cell > 0f ? deltaDist / cell : 0f;
        float  abs          = Mathf.Abs(deltaBeats);

        Color  bg;
        string line1, line2;

        if (abs < 0.1f)
        {
            bg    = new Color(0.08f, 0.42f, 0.08f, 0.92f);
            line1 = "✓  Jugador sincronizado";
            line2 = $"{actualDist:F1} m del origen  •  Δ {deltaDist:+0.00;-0.00} m";
        }
        else if (deltaBeats > 0f)
        {
            bg    = abs > 0.5f ? new Color(0.55f, 0.07f, 0.07f, 0.93f)
                               : new Color(0.52f, 0.42f, 0.00f, 0.93f);
            line1 = $"⚡  ADELANTADO  +{deltaBeats:F2} beats";
            line2 = $"Esperado: {expectedDist:F1} m  •  Real: {actualDist:F1} m  (Δ {deltaDist:+0.0} m)";
        }
        else
        {
            bg    = abs > 0.5f ? new Color(0.55f, 0.07f, 0.07f, 0.93f)
                               : new Color(0.52f, 0.42f, 0.00f, 0.93f);
            line1 = $"⏱  ATRASADO  {deltaBeats:F2} beats";
            line2 = $"Esperado: {expectedDist:F1} m  •  Real: {actualDist:F1} m  (Δ {deltaDist:+0.0;-0.0} m)";
        }

        // ── Velocidad actual ──────────────────────────────────────────────────
        float speed = _cc != null
            ? new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude
            : 0f;
        bool  speedOk    = speed >= _pm.walkSpeed - 0.5f;
        Color speedColor = speedOk ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.3f, 0.3f);
        string speedText = $"{speed:F1} m/s";

        // ── Dibujar panel ─────────────────────────────────────────────────────
        const float x = 10f, y = 10f, w = 360f, h = 72f;

        // Fondo coloreado
        SetBgColor(bg, ref _bgTex);
        GUI.DrawTexture(new Rect(x, y, w, h), _bgTex);

        // Línea 1: estado de sincronía
        GUI.Label(new Rect(x + 10, y + 5,  w - 20, 24), line1, _titleStyle);

        // Línea 2: detalle de metros
        GUI.Label(new Rect(x + 10, y + 26, w - 20, 18), line2, _valueStyle);

        // Línea 3: velocidad
        _valueStyle.normal.textColor = speedColor;
        GUI.Label(new Rect(x + 10, y + 48, 120, 18), $"Velocidad: {speedText}", _valueStyle);
        _valueStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f); // restaurar
    }

    private void InitStyles()
    {
        if (_titleStyle != null) return;

        _titleStyle = new GUIStyle
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 13,
            normal    = { textColor = Color.white }
        };
        _valueStyle = new GUIStyle
        {
            fontSize = 11,
            normal   = { textColor = new Color(1f, 1f, 1f, 0.8f) }
        };
        // _bgTex se crea en SetBgColor
    }

    private void SetBgColor(Color c, ref Texture2D tex)
    {
        if (tex == null) tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, c);
        tex.Apply();
    }

    private void OnDestroy()
    {
        if (_bgTex   != null) Destroy(_bgTex);
        if (_speedTex != null) Destroy(_speedTex);
    }
#endif
}
