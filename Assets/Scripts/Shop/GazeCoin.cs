using UnityEngine;

/// <summary>
/// IRIS: moneda especial escondida por los niveles que se recoge MIRÁNDOLA (mecanismo de GazeSwitch), no tocándola.
/// Con ellos se compran los COSMÉTICOS de la tienda. Cada iris se recoge UNA sola vez en toda la partida (se guarda),
/// y no se pierden al morir.
///
/// SETUP: objeto vacío + este componente (si no tiene malla propia se genera un «ojo» violeta flotante). Colócalo
/// fuera del camino (en alto, detrás de una esquina…): hay que fijarse en él y mantener la mirada unos instantes.
/// Cada iris necesita una posición distinta (su identificador sale de la escena + la posición).
/// </summary>
[AddComponentMenu("Level/Gaze Coin (Iris)")]
public class GazeCoin : MonoBehaviour
{
    [Tooltip("Iris que vale.")]
    [SerializeField] private int _value = 1;
    [Tooltip("Segundos mirándolo para recogerlo.")]
    [SerializeField] private float _holdSeconds = 0.9f;
    [Tooltip("Distancia máxima (m) desde la que se puede recoger mirándolo.")]
    [SerializeField] private float _maxDistance = 45f;
    [Tooltip("Identificador fijo (opcional). Vacío = escena + posición.")]
    [SerializeField] private string _uid = "";
    [SerializeField] private Color _color = new Color(0.65f, 0.35f, 1f);
    [SerializeField] private AudioClip _pickupSound;
    [Range(0f, 1f)] [SerializeField] private float _pickupVolume = 0.8f;

    private GazeSwitch _gaze;
    private Transform _visual;
    private Vector3 _scale0;
    private bool _taken;
    private float _takeT, _popupT;
    private Vector3 _takeFrom;
    private Camera _cam;

    private string Uid => !string.IsNullOrEmpty(_uid) ? _uid
        : $"{gameObject.scene.name}:{Mathf.RoundToInt(transform.position.x * 10)}:{Mathf.RoundToInt(transform.position.y * 10)}:{Mathf.RoundToInt(transform.position.z * 10)}";

    private void Start()
    {
        if (PlayerProgress.IsGazeCoinCollected(Uid)) { gameObject.SetActive(false); return; }

        Renderer r = GetComponentInChildren<Renderer>();
        _visual = r == null ? BuildVisual() : r.transform;
        _scale0 = _visual.localScale;
        if (GetComponentInChildren<Collider>() == null)
        {
            var sc = gameObject.AddComponent<SphereCollider>();   // (la mirada necesita un collider NO trigger)
            sc.radius = 0.6f;
        }

        _gaze = gameObject.AddComponent<GazeSwitch>();
        _gaze.Configure(_holdSeconds, _maxDistance, r != null ? r : _visual.GetComponent<Renderer>());
        var pm = FindAnyObjectByType<PlayerMovement>();
        if (pm != null) _gaze.SetIgnoredRoots(pm.transform);
        _gaze.OnActivated.AddListener(Collect);
    }

    private Transform BuildVisual()
    {
        var pivot = new GameObject("IrisVisual").transform;
        pivot.SetParent(transform, false);

        // «Ojo»: esfera blanca con iris violeta y pupila negra, mirando al jugador.
        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(eye.GetComponent<Collider>());
        eye.transform.SetParent(pivot, false);
        eye.transform.localScale = Vector3.one * 0.9f;
        Tint(eye, new Color(0.95f, 0.93f, 0.9f), 0.15f);

        GameObject iris = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(iris.GetComponent<Collider>());
        iris.transform.SetParent(pivot, false);
        iris.transform.localPosition = new Vector3(0f, 0f, 0.33f);
        iris.transform.localScale = new Vector3(0.5f, 0.5f, 0.3f);
        Tint(iris, _color, 0.9f);

        GameObject pupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(pupil.GetComponent<Collider>());
        pupil.transform.SetParent(pivot, false);
        pupil.transform.localPosition = new Vector3(0f, 0f, 0.42f);
        pupil.transform.localScale = new Vector3(0.22f, 0.22f, 0.15f);
        Tint(pupil, Color.black, 0f);
        return pivot;
    }

    private static void Tint(GameObject g, Color c, float emission)
    {
        var mr = g.GetComponent<Renderer>();
        var m = new Material(mr.sharedMaterial) { color = c };
        if (emission > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emission); }
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void Update()
    {
        if (_popupT > 0f) _popupT -= Time.unscaledDeltaTime;
        if (_cam == null) _cam = Camera.main;
        if (_visual == null) return;

        if (_taken)
        {
            // Vuela hacia la cámara encogiéndose.
            _takeT += Time.deltaTime / 0.45f;
            if (_cam != null) _visual.position = Vector3.Lerp(_takeFrom, _cam.transform.position + _cam.transform.forward * 0.6f, _takeT * _takeT);
            _visual.localScale = _scale0 * Mathf.Max(0f, 1f - _takeT);
            if (_takeT >= 1f) { _visual.gameObject.SetActive(false); if (_popupT <= 0f) gameObject.SetActive(false); }
            return;
        }

        // Flota y "te mira"; late más rápido mientras lo miras (se está cargando).
        float p = _gaze != null ? _gaze.Progress : 0f;
        _visual.localPosition = Vector3.up * (0.2f * Mathf.Sin(Time.time * 1.6f));
        if (_cam != null) _visual.rotation = Quaternion.LookRotation(_cam.transform.position - _visual.position);
        _visual.localScale = _scale0 * (1f + 0.25f * p + 0.05f * Mathf.Sin(Time.time * (4f + 20f * p)));
    }

    private void Collect()
    {
        if (_taken) return;
        _taken = true;
        _takeFrom = _visual.position;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (PlayerProgress.CollectGazeCoin(Uid, _value)) _popupT = 1.8f;
        if (_pickupSound != null) AudioSource.PlayClipAtPoint(_pickupSound, transform.position, _pickupVolume);
    }

    private GUIStyle _style;

    private void OnGUI()
    {
        if (HudHider.IsHidden) return;   // (con la pausa o el game over no se ve ningún aviso)
        if (_popupT <= 0f) return;
        if (_style == null)
            _style = new GUIStyle { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        float a = Mathf.Clamp01(_popupT / 0.5f);
        _style.normal.textColor = new Color(0.8f, 0.6f, 1f, a);
        GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 40f),
                  Loc.T($"+{_value} IRIS   ·   tienes {PlayerProgress.GazeCoins}", $"+{_value} IRIS   ·   you have {PlayerProgress.GazeCoins}"), _style);
        if (_popupT <= 0.02f && _taken) gameObject.SetActive(false);
    }
}
