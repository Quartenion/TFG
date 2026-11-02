using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// FONDO DEL MENÚ: negro con un resplandor de fuego rojo que sube desde abajo (parpadea un poco) y chispas que
/// suben, se mecen y se apagan. Todo es UI (va dentro del Canvas, detrás de los paneles), sin texturas externas.
/// Se añade con «Tools/Menú principal/Añadir fondo de fuego».
/// </summary>
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("UI/Menu Fire Background")]
public class MenuFireBackground : MonoBehaviour
{
    [Header("Fondo")]
    [SerializeField] private Color _backColor = new Color(0.03f, 0.005f, 0.005f, 1f);

    [Header("Resplandor (desde abajo)")]
    [SerializeField] private Color _glowColor = new Color(1f, 0.12f, 0.03f, 1f);
    [Tooltip("Altura del resplandor (fracción de la pantalla).")]
    [Range(0.1f, 1f)] [SerializeField] private float _glowHeight = 0.6f;
    [Tooltip("Cuánto parpadea el resplandor (0 = fijo).")]
    [Range(0f, 0.5f)] [SerializeField] private float _glowFlicker = 0.18f;

    [Header("Chispas")]
    [SerializeField] private int _maxSparks = 120;
    [Tooltip("Chispas nuevas por segundo.")]
    [SerializeField] private float _sparksPerSecond = 45f;
    [SerializeField] private Vector2 _sparkSpeed = new Vector2(120f, 420f);   // px/s (pantalla 1920×1080)
    [SerializeField] private Vector2 _sparkSize = new Vector2(3f, 8f);
    [SerializeField] private Vector2 _sparkLife = new Vector2(1.5f, 4f);
    [Tooltip("Cuánto se mecen a los lados.")]
    [SerializeField] private float _sparkWiggle = 60f;
    [SerializeField] private Color _sparkHot  = new Color(1f, 0.85f, 0.4f, 1f);
    [SerializeField] private Color _sparkCold = new Color(1f, 0.15f, 0.02f, 1f);

    private struct Spark { public RectTransform rt; public RawImage img; public Vector2 pos, vel; public float age, life, size, seed; public bool alive; }

    private RectTransform _rt;
    private RawImage _glow, _glowCore;
    private Spark[] _sparks;
    private float _spawnAcc;
    private int _last;
    private Texture2D _glowTex, _dotTex;

    private void Awake()
    {
        _rt = (RectTransform)transform;
        _glowTex = MakeGlowTexture();
        _dotTex  = MakeDotTexture();

        // Ocupa toda la pantalla y no bloquea clics.
        _rt.anchorMin = Vector2.zero; _rt.anchorMax = Vector2.one;
        _rt.offsetMin = Vector2.zero; _rt.offsetMax = Vector2.zero;

        var back = NewChild("Fondo").gameObject.AddComponent<Image>();
        back.color = _backColor; back.raycastTarget = false;

        _glow     = NewGlow("Resplandor", _glowHeight, 1f);
        _glowCore = NewGlow("Resplandor (núcleo)", _glowHeight * 0.45f, 0.8f);

        BuildSparks();
    }

    /// <summary>
    /// Crea las chispas. También se llama si se pierden: al recompilar un script con el juego en marcha, Unity recarga los
    /// componentes y el array de chispas (no serializable) vuelve vacío → NullReferenceException en Spawn.
    /// </summary>
    private void BuildSparks()
    {
        Transform old = transform.Find("Chispas");
        if (old != null) Destroy(old.gameObject);
        if (_dotTex == null) _dotTex = MakeDotTexture();
        var sparkRoot = NewChild("Chispas");
        _sparks = new Spark[Mathf.Max(1, _maxSparks)];
        for (int i = 0; i < _sparks.Length; i++)
        {
            var rt = NewChild("Chispa", sparkRoot);
            rt.anchorMin = rt.anchorMax = Vector2.zero;   // posición en px desde la esquina inferior izquierda
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = _dotTex; img.raycastTarget = false;
            rt.gameObject.SetActive(false);
            _sparks[i] = new Spark { rt = rt, img = img };
        }
        // Empieza con chispas ya en pantalla, no vacío.
        for (int i = 0; i < _sparks.Length / 2; i++) { Spawn(); Step(ref _sparks[_last], Random.Range(0f, 2.5f)); }
    }

    private void Update()
    {
        if (_rt == null) _rt = (RectTransform)transform;
        if (_sparks == null) BuildSparks();   // (recompilado con el juego en marcha)
        if (_glow == null || _glowCore == null) return;
        float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;

        // Parpadeo del fuego: mezcla de dos ondas + ruido.
        float flick = 1f - _glowFlicker * (0.5f + 0.5f * Mathf.PerlinNoise(t * 2.3f, 0.37f) * Mathf.Sin(t * 7.1f) + 0.3f * Mathf.PerlinNoise(t * 9f, 1.7f));
        _glow.color     = _glowColor * new Color(1f, 1f, 1f, 0.85f * flick);
        _glowCore.color = Color.Lerp(_glowColor, _sparkHot, 0.35f) * new Color(1f, 1f, 1f, 0.55f * flick);

        _spawnAcc += _sparksPerSecond * dt;
        while (_spawnAcc >= 1f) { _spawnAcc -= 1f; Spawn(); }

        for (int i = 0; i < _sparks.Length; i++)
            if (_sparks[i].alive) Step(ref _sparks[i], dt);
    }

    private void Spawn()
    {
        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparks[i].alive) continue;
            Vector2 size = _rt.rect.size;
            ref Spark s = ref _sparks[i];
            s.alive = true; s.age = 0f;
            s.life = Random.Range(_sparkLife.x, _sparkLife.y);
            s.size = Random.Range(_sparkSize.x, _sparkSize.y);
            s.seed = Random.value * 100f;
            s.pos  = new Vector2(Random.Range(0f, size.x), Random.Range(-20f, size.y * 0.12f));
            s.vel  = new Vector2(Random.Range(-40f, 40f), Random.Range(_sparkSpeed.x, _sparkSpeed.y));
            s.rt.gameObject.SetActive(true);
            _last = i;
            return;
        }
    }

    private void Step(ref Spark s, float dt)
    {
        s.age += dt;
        if (s.age >= s.life || s.pos.y > _rt.rect.height + 40f) { s.alive = false; s.rt.gameObject.SetActive(false); return; }

        // Sube frenándose un poco y se mece de lado a lado.
        float sway = (Mathf.PerlinNoise(s.seed, s.age * 1.3f) - 0.5f) * 2f * _sparkWiggle;
        s.vel.y *= 1f - 0.25f * dt;
        Vector2 v = new Vector2(s.vel.x + sway, s.vel.y);
        s.pos += v * dt;

        float k = s.age / s.life;                                   // 0 → 1
        float fade = Mathf.Clamp01(s.age * 6f) * (1f - k * k);      // aparece rápido, se apaga al final
        Color c = Color.Lerp(_sparkHot, _sparkCold, k);
        c.a = fade * (0.7f + 0.3f * Mathf.Sin(s.seed + s.age * 25f)); // titila
        s.img.color = c;

        // Estela alargada en la dirección del movimiento.
        float len = s.size * (1.5f + v.magnitude / 120f);
        s.rt.sizeDelta = new Vector2(s.size, len);
        s.rt.anchoredPosition = s.pos;
        s.rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f);
    }

    // ── Construcción ──────────────────────────────────────────────────────────

    private RectTransform NewChild(string name, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent != null ? parent : transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        go.hideFlags = HideFlags.DontSave;   // se regenera al arrancar: no ensucia la escena
        return rt;
    }

    private RawImage NewGlow(string name, float height, float width)
    {
        var rt = NewChild(name);
        rt.anchorMin = new Vector2(0.5f - width * 0.5f - 0.15f, 0f);
        rt.anchorMax = new Vector2(0.5f + width * 0.5f + 0.15f, height);
        var img = rt.gameObject.AddComponent<RawImage>();
        img.texture = _glowTex; img.raycastTarget = false;
        return img;
    }

    /// <summary>Degradado: intenso abajo en el centro, se desvanece hacia arriba y hacia los lados.</summary>
    private static Texture2D MakeGlowTexture()
    {
        const int W = 128, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MenuGlow" };
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H;
                float a = Mathf.Pow(1f - v, 2.2f) * Mathf.Clamp01(1f - u * u);
                px[y * W + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    /// <summary>Punto blando (centro opaco, bordes difuminados).</summary>
    private static Texture2D MakeDotTexture()
    {
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MenuSpark" };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d));
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    private void OnDestroy()
    {
        if (_glowTex != null) Destroy(_glowTex);
        if (_dotTex != null) Destroy(_dotTex);
    }
}
