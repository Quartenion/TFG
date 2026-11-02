using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Muestra un ARO DE CARGA en el HUD con el progreso del <see cref="GazeSwitch"/> que el jugador
/// está mirando. Un solo aro vale para TODOS los interruptores de mirada de la escena:
///   • muestra el que más cargado esté (el que estás mirando),
///   • se vacía si dejas de mirar (mientras el switch se descarga),
///   • se oculta al completarse (switch activado) o cuando no estás cargando ninguno.
/// Los interruptores se leen de <see cref="GazeSwitch.All"/>, así que también valen los que aparecen al jugar
/// (nivel tutorial, jefe). Si la escena no tiene aro, el primer GazeSwitch crea uno (<see cref="Ensure"/>).
///
/// SETUP (opcional: si no lo pones, se crea solo):
///   1. En el Canvas del HUD crea una Image centrada (donde el crosshair): Image Type = Filled,
///      Fill Method = Radial 360, con un sprite de aro. Ponle Fill Amount = 0.
///   2. Añade este componente (al Canvas o a un objeto del HUD) y arrastra esa Image a "Ring".
/// </summary>
[AddComponentMenu("HUD/Gaze Charge UI")]
public class GazeChargeUI : MonoBehaviour
{
    [Header("Aro")]
    [Tooltip("Image del HUD con Image Type = Filled, Fill Method = Radial 360.")]
    [SerializeField] private Image _ring;

    [Tooltip("Por debajo de este progreso el aro se oculta (para no ver el aro vacío).")]
    [SerializeField] private float _showThreshold = 0.01f;

    [Header("Color (opcional)")]
    [SerializeField] private bool  _colorLerp = true;
    [SerializeField] private Color _colorStart = new Color(0.2f, 0.9f, 1f, 1f);
    [SerializeField] private Color _colorFull  = new Color(0.3f, 1f, 0.45f, 1f);

    [Header("Suavizado (opcional)")]
    [Tooltip("Velocidad con la que el aro sigue al progreso real. 0 = instantáneo.")]
    [SerializeField] private float _followSpeed = 0f;

    private CanvasGroup  _group;
    private float        _shown;   // valor mostrado (para suavizado)
    private static GazeChargeUI _instance;

    private void OnEnable()  { if (_instance == null) _instance = this; }
    private void OnDisable() { if (_instance == this) _instance = null; }

    private void Start() => SetupRing();

    private void SetupRing()
    {
        if (_ring == null) return;
        _group = _ring.GetComponent<CanvasGroup>();
        if (_group == null) _group = _ring.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _ring.fillAmount = 0f;
    }

    private void LateUpdate()
    {
        if (_ring == null) return;

        // Progreso objetivo = el del switch más cargado que aún NO se ha activado.
        float target = 0f;
        foreach (GazeSwitch s in GazeSwitch.All)
        {
            if (s == null || s.IsActivated) continue;
            if (s.Progress > target) target = s.Progress;
        }

        // Seguir el objetivo (instantáneo o suavizado).
        if (_followSpeed > 0f)
            _shown = Mathf.MoveTowards(_shown, target, _followSpeed * Time.deltaTime);
        else
            _shown = target;

        _ring.fillAmount = _shown;
        if (_colorLerp) _ring.color = Color.Lerp(_colorStart, _colorFull, _shown);
        if (_group != null) _group.alpha = _shown > _showThreshold && !HudHider.IsHidden ? 1f : 0f;
    }

    /// <summary>(Compatibilidad) Ya no hace falta: los interruptores se leen de GazeSwitch.All.</summary>
    public void RefreshSwitches() { }

    /// <summary>Si la escena no tiene aro de carga, crea uno en el centro de la pantalla (aro generado por código).</summary>
    public static void Ensure()
    {
        if (_instance != null || !Application.isPlaying) return;
        _instance = FindAnyObjectByType<GazeChargeUI>();
        if (_instance != null) return;

        Canvas c = UIKit.CreateCanvas("AroMiradaCanvas", 60, null);
        var go = new GameObject("AroMirada", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(c.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(84f, 84f);
        var img = go.GetComponent<Image>();
        img.sprite = RingSprite();
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)Image.Origin360.Top;
        img.fillClockwise = true;
        img.raycastTarget = false;

        _instance = c.gameObject.AddComponent<GazeChargeUI>();
        _instance._ring = img;
        _instance.SetupRing();
    }

    private static Sprite _ringSprite;

    public static Sprite RingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01((1f - d) * N * 0.5f) * Mathf.Clamp01((d - 0.78f) * N * 0.5f);   // aro de 0,78 a 1
            px[y * N + x] = new Color32(255, 255, 255, (byte)(255 * a));
        }
        tex.SetPixels32(px); tex.Apply(false);
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _ringSprite;
    }
}
