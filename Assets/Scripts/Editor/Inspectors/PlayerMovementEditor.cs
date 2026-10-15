using UnityEditor;
using UnityEngine;

// ═════════════════════════════════════════════════════════════════════════════
// OVERLAY — siempre visible en la esquina superior-izquierda de la Scene View
//           durante el play mode, sin necesidad de seleccionar el jugador.
// ═════════════════════════════════════════════════════════════════════════════

[InitializeOnLoad]
public static class PlayerSyncOverlay
{
    // Estilos cacheados (se crean una sola vez)
    private static GUIStyle _boxStyle;
    private static GUIStyle _titleStyle;
    private static GUIStyle _valueStyle;

    static PlayerSyncOverlay()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += () => { if (Application.isPlaying) SceneView.RepaintAll(); };
    }

    private static void OnSceneGUI(SceneView sv)
    {
        if (!Application.isPlaying) return;

        var pm   = Object.FindAnyObjectByType<PlayerMovement>();
        var grid = Object.FindAnyObjectByType<BeatGridVisualizer>();
        var bm   = BeatManager.Instance;

        if (pm == null || grid == null || bm == null || bm.ScheduledStartDSP <= 0.0) return;

        // ── Calcular desfase ──────────────────────────────────────────────────
        double now          = AudioSettings.dspTime;
        float  expectedDist = (float)((now - bm.ScheduledStartDSP) * grid.walkSpeed);
        float  actualDist   = grid.DistanceAlong(pm.transform.position);
        float  deltaDist    = actualDist - expectedDist;
        float  cell         = grid.CellSize;
        float  deltaBeats   = cell > 0f ? deltaDist / cell : 0f;
        float  abs          = Mathf.Abs(deltaBeats);

        Color  bgColor;
        string icon, statusText;

        if (abs < 0.1f)
        {
            bgColor    = new Color(0.1f, 0.45f, 0.1f, 0.92f);
            icon       = "✓";
            statusText = "Sincronizado";
        }
        else if (deltaBeats > 0f)
        {
            bgColor    = abs > 0.5f ? new Color(0.55f, 0.08f, 0.08f, 0.92f)
                                    : new Color(0.55f, 0.45f, 0.0f,  0.92f);
            icon       = "⚡";
            statusText = "ADELANTADO";
        }
        else
        {
            bgColor    = abs > 0.5f ? new Color(0.55f, 0.08f, 0.08f, 0.92f)
                                    : new Color(0.55f, 0.45f, 0.0f,  0.92f);
            icon       = "⏱";
            statusText = "ATRASADO";
        }

        // ── Dibujar panel en la Scene View ────────────────────────────────────
        InitStyles(bgColor);

        Handles.BeginGUI();

        float panelW = 210f;
        float panelH = 72f;
        float margin = 8f;

        Rect area = new Rect(margin, margin, panelW, panelH);
        GUI.backgroundColor = bgColor;
        GUI.Box(area, GUIContent.none, _boxStyle);
        GUI.backgroundColor = Color.white;

        GUILayout.BeginArea(new Rect(area.x + 8, area.y + 6, area.width - 16, area.height - 12));

        // Título
        GUILayout.Label("Sincronía del jugador", _titleStyle);

        // Estado principal
        string mainLine = abs < 0.1f
            ? $"{icon}  En sincronía"
            : $"{icon}  {statusText}  {(deltaBeats >= 0 ? "+" : "")}{deltaBeats:F2} beats";
        GUILayout.Label(mainLine, _valueStyle);

        // Detalle en metros
        string detail = abs < 0.1f
            ? $"Δ {deltaDist:+0.0;-0.0} m  •  {actualDist:F1} m del origen"
            : $"Δ {deltaDist:+0.0;-0.0} m  (esperado: {expectedDist:F1} m)";
        GUILayout.Label(detail, _valueStyle);

        GUILayout.EndArea();

        Handles.EndGUI();
    }

    private static void InitStyles(Color bgColor)
    {
        if (_boxStyle == null)
        {
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding  = new RectOffset(0, 0, 0, 0),
                margin   = new RectOffset(0, 0, 0, 0),
                border   = new RectOffset(4, 4, 4, 4),
            };
        }

        if (_titleStyle == null)
        {
            _titleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                fontSize  = 10,
                normal    = { textColor = new Color(1f, 1f, 1f, 0.65f) }
            };
        }

        if (_valueStyle == null)
        {
            _valueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                fontSize  = 11,
                normal    = { textColor = Color.white }
            };
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// INSPECTOR — info adicional en el Inspector cuando el jugador está seleccionado
// ═════════════════════════════════════════════════════════════════════════════

[CustomEditor(typeof(PlayerMovement))]
public class PlayerMovementEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var pm   = (PlayerMovement)target;
        var grid = FindAnyObjectByType<BeatGridVisualizer>();
        var bm   = FindAnyObjectByType<BeatManager>();

        if (grid == null || bm == null) return;

        float actualDist   = grid.DistanceAlong(pm.transform.position);
        float cell         = grid.CellSize;
        float beatAtPlayer = cell > 0f ? actualDist / cell : 0f;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Posición en el nivel ──────────────────", EditorStyles.centeredGreyMiniLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                $"Inicio en beat {beatAtPlayer:F1}  •  {actualDist:F1} m del origen",
                MessageType.Info);
        }
    }
}
