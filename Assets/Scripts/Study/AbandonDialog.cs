using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AVISO «¿ABANDONAR LA PRUEBA?» (modo prueba). Sale al intentar volver al menú desde la pausa o al pulsar SALIR en el
/// game over. Si se confirma: se registra el abandono (los datos de la partida SÍ se envían, sirven para ver dónde
/// lo dejan los jugadores), la sesión queda descalificada y se vuelve al menú, donde la prueba empieza de cero.
/// </summary>
public static class AbandonDialog
{
    private static GameObject _dialog;

    public static bool IsOpen => _dialog != null;

    /// <param name="stayLabel">Texto del botón para no abandonar (p. ej. «Seguir jugando» o «Volver»).</param>
    public static void Show(string stayLabel)
    {
        if (_dialog != null) return;
        UIKit.EnsureEventSystem();
        Canvas c = UIKit.CreateCanvas("AbandonarPrueba", 1000);
        _dialog = c.gameObject;
        Transform root = c.transform;

        var back = SimpleUI.Box("Fondo", root, SimpleUI.Back);
        SimpleUI.Stretch(back.rectTransform, 0f, 0f, 0f, 0f);

        var box = SimpleUI.Box("Aviso", root, SimpleUI.PanelCol);
        SimpleUI.Place(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 520f));

        var title = SimpleUI.Lbl("Titulo", box.transform, Loc.T("¿ABANDONAR LA PRUEBA?", "LEAVE THE TEST?"), SimpleUI.FontBig, TextAlignmentOptions.Center);
        SimpleUI.PlaceTopLeft(title.rectTransform, 40f, 40f, 920f, 60f);

        var body = SimpleUI.Lbl("Texto", box.transform,
            Loc.T("Si sales ahora, abandonarás la prueba y quedarás DESCALIFICADO/A de la encuesta: " +
                  "tus datos no se tendrán en cuenta.\n\nLa prueba se reiniciará desde el principio (con un código nuevo).",
                  "If you leave now, you will quit the test and be DISQUALIFIED from the survey: " +
                  "your data will not be used.\n\nThe test will restart from the beginning (with a new code)."),
            SimpleUI.FontMid, TextAlignmentOptions.TopLeft);
        SimpleUI.PlaceTopLeft(body.rectTransform, 40f, 130f, 920f, 230f);

        var stay = SimpleUI.Btn("Quedarse", box.transform, stayLabel, out _);
        SimpleUI.PlaceBottomLeft((RectTransform)stay.transform, 40f, 40f, 440f, 80f);
        stay.onClick.AddListener(Close);

        var leave = SimpleUI.Btn("Abandonar", box.transform, Loc.T("Abandonar la prueba", "Leave the test"), out _);
        SimpleUI.Place((RectTransform)leave.transform, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(440f, 80f), new Vector2(1f, 0f));
        leave.onClick.AddListener(Leave);
    }

    public static void Close()
    {
        if (_dialog != null) Object.Destroy(_dialog);
        _dialog = null;
    }

    /// <summary>Abandona: registra y envía el abandono, descalifica la sesión y vuelve al menú principal.</summary>
    private static void Leave()
    {
        StudySession.Abort();
        Close();
        Time.timeScale = 1f;
        AudioListener.pause = false;
        AudioListener.volume = GameSettings.Volume;
        SceneManager.LoadScene(StudySession.MenuScene);
    }
}
