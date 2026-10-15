using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using static SimpleUI;

/// <summary>
/// AVISO DE PRÁCTICA LIBRE (modo prueba): mientras se repite un nivel en práctica libre, arriba en el centro se
/// recuerda que es solo práctica y que para seguir con la prueba hay que volver al menú (Esc → Menú, o terminar el
/// nivel), donde se puede continuar con el siguiente nivel. Se crea solo al cargar un nivel en práctica libre.
/// </summary>
public class FreePlayBanner : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        AutoSpawn.Register(OnSceneLoaded);
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (!StudySession.InFreePlay || FindAnyObjectByType<FreePlayBanner>() != null) return;
        new GameObject("FreePlayBanner").AddComponent<FreePlayBanner>();
    }

    private Canvas _canvas;
    private TextMeshProUGUI _text;

    private void Start()
    {
        _canvas = UIKit.CreateCanvas("FreePlayCanvas", 85, transform);
        var box = Box("Aviso", _canvas.transform, new Color(0f, 0f, 0f, 0.55f));
        Place(box.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(980f, 70f), new Vector2(0.5f, 1f));
        _text = Lbl("Texto", box.transform, "", FontSmall + 1f, TextAlignmentOptions.Center);
        Stretch(_text.rectTransform, 16f, 16f, 6f, 6f);
        _text.color = new Color(1f, 0.8f, 0.35f);
        Refresh();
        Loc.OnChanged += Refresh;
        Keybinds.Changed += Refresh;
    }

    private void OnDestroy() { Loc.OnChanged -= Refresh; Keybinds.Changed -= Refresh; }

    private void Refresh()
    {
        if (_text == null) return;
        _text.text = Loc.T(
            "PRÁCTICA LIBRE · No se registran datos. Para seguir con la prueba (siguiente nivel), vuelve al menú: Esc → Menú.",
            "FREE PRACTICE · No data is recorded. To continue the test (next level), go back to the menu: Esc → Menu.");
    }

    private void Update()
    {
        if (!StudySession.InFreePlay) { Destroy(gameObject); return; }
        // (Escondido con el menú de pausa / game over, como el resto del HUD.)
        if (_canvas != null && _canvas.enabled == HudHider.IsHidden) _canvas.enabled = !HudHider.IsHidden;
    }
}
