using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AVISO «¿ABANDONAR LA PRUEBA?» (modo prueba). Sale al intentar volver al menú desde la pausa o al pulsar SALIR en el
/// game over. El texto cambia en las partes 4 y 5 (las tres primeras ya se han enviado completas). Si se confirma: se registra el abandono (los datos de la partida SÍ se envían, sirven para ver dónde
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

        // Partes 4 y 5 (Step 3 y 4): las tres primeras ya están completas.
        bool lateParts = StudySession.Step >= 3;
        string text = lateParts
            ? Loc.T("Si sales ahora, abandonarás la prueba. Se enviarán los datos de las partes 1, 2 y 3, " +
                    "y solo una parte de los de las partes 4 y 5.\n\nLa prueba se reiniciará desde el principio (con un código nuevo).",
                    "If you leave now, you will quit the test. The data from parts 1, 2 and 3 will be sent, " +
                    "and only part of the data from parts 4 and 5.\n\nThe test will restart from the beginning (with a new code).")
            : Loc.T("Si sales ahora, abandonarás la prueba. Solo se enviarán los datos recogidos hasta este momento." +
                    "\n\nLa prueba se reiniciará desde el principio (con un código nuevo).",
                    "If you leave now, you will quit the test. Only the data collected so far will be sent." +
                    "\n\nThe test will restart from the beginning (with a new code).");
        var body = SimpleUI.Lbl("Texto", box.transform, text, SimpleUI.FontMid, TextAlignmentOptions.TopLeft);
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
