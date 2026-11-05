using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// Pantallas del modo prueba en el menú principal. Se crea sola en "MainMenu".
///   - El botón "JUGAR" abre el consentimiento informado.
///   - Antes de cada parte se explica qué se va a jugar, y después se muestra el cuestionario corto (de 1 a 7).
///   - Después de las partes principales, el jugador puede seguir al siguiente nivel, repetir el nivel en práctica
///     libre (con todo desbloqueado y sin registrar datos) o terminar.
///   - Al final se muestran el código y el cuestionario de Google Forms.
/// </summary>
public class StudyHub : MonoBehaviour
{
    /// <summary>Preguntas del cuestionario corto (1 es nada de acuerdo y 7, totalmente de acuerdo).</summary>
    public static string[] Questions => new[]
    {
        Loc.T("Me he divertido.", "I had fun."),
        Loc.T("Sentía que controlaba lo que hacía.", "I felt in control of what I was doing."),
        Loc.T("Estaba totalmente concentrado/a en el juego.", "I was fully focused on the game."),
        Loc.T("Mis acciones iban al ritmo de la música de forma natural.", "My actions naturally followed the rhythm of the music."),
        Loc.T("El nivel me ha resultado difícil.", "I found the level difficult."),
        Loc.T("La barra de energía me obligaba a estar pendiente del ritmo.", "The energy bar forced me to keep track of the rhythm."),
        Loc.T("Cuando fallaba al hacer clic, entendía por qué (los fallos me parecían justos).", "When I missed a click, I understood why (misses felt fair)."),
        Loc.T("Me habría gustado seguir jugando.", "I would have liked to keep playing."),
    };

    private Transform _canvas;
    private GameObject _screen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        AutoSpawn.Register(OnSceneLoaded);
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name != StudySession.MenuScene || FindAnyObjectByType<StudyHub>() != null) return;
        new GameObject("StudyHub").AddComponent<StudyHub>();
    }

    private void Start()
    {
        UIKit.EnsureEventSystem();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _canvas = UIKit.CreateCanvas("StudyCanvas", 500, transform).transform;

        // Sin sesión no se muestra nada. La prueba se empieza con el botón Play del menú cuando el MainMenuManager tiene
        // marcado "Playtest", que es la versión que se reparte a los jugadores.
        if (!StudySession.Active) return;

        // Al volver de la práctica libre, todo queda como estaba y el jugador vuelve a elegir.
        if (StudySession.FreePlay) { StudySession.EndFreePlay(); ShowContinueChoice(); return; }

        if (StudySession.AwaitingReturn)
        {
            StudySession.ReturnedToMenu();
            if (StudySession.Current.questionnaire) { ShowQuestionnaire(); return; }
        }
        else if (StudySession.Step >= 0) { ShowPartIntro(); return; }   // por ejemplo, si se volvió al menú desde la pausa
        AfterPart();
    }

    // -=+=- Pantallas -=+=-

    private Transform NewScreen(string title)
    {
        if (_screen != null) Destroy(_screen);
        var back = Box("Screen", _canvas, new Color(0.05f, 0.05f, 0.05f, 0.97f));
        Stretch(back.rectTransform, 0f, 0f, 0f, 0f);
        _screen = back.gameObject;
        var panel = Box("Panel", back.transform, PanelCol);
        Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, 900f));
        var t = Lbl("Title", panel.transform, title, FontBig, TextAlignmentOptions.Left);
        PlaceTopLeft(t.rectTransform, 40f, 30f, 1220f, 50f);
        return panel.transform;
    }

    private static TextMeshProUGUI Body(Transform p, string text, float top, float height)
    {
        var l = Lbl("Body", p, text, FontMid, TextAlignmentOptions.TopLeft);
        PlaceTopLeft(l.rectTransform, 40f, top, 1220f, height);
        return l;
    }

    private static Button Bottom(Transform p, string label, float x, Action onClick)
    {
        var b = Btn(label, p, label, out _);
        PlaceBottomLeft((RectTransform)b.transform, x, 30f, 400f, 64f);
        b.onClick.AddListener(() => onClick());
        return b;
    }

    private static StudyHub _instance;

    /// <summary>
    /// Sala del tutorial. El participante ya ha leído la siguiente parte y va hacia la puerta grande. Lo usa CourseBuilder
    /// para devolverle el control y abrir esa puerta.
    /// </summary>
    public static Action RoomPartReady;
    private void Awake() => _instance = this;
    private void OnDestroy() { if (_instance == this) _instance = null; }

    /// <summary>Abre el consentimiento. Lo llama el botón Play del menú en la versión de prueba.</summary>
    public static void OpenConsent()
    {
        if (_instance == null) _instance = new GameObject("StudyHub").AddComponent<StudyHub>();
        if (_instance._canvas == null) _instance.Start();
        _instance.ShowConsent();
    }

    private void ShowConsent()
    {
        Transform p = NewScreen(Loc.T("Prueba del juego · Trabajo Fin de Grado", "Game test · Bachelor's Thesis"));
        var consent = Body(p, Loc.English ?
            "Thank you for taking part. You are going to try a first-person rhythm game that is part of a Bachelor's Thesis " +
            "at the University of Granada.\n\n" +
            "IMPORTANT: this is a PROTOTYPE in development and does NOT represent the final product. Graphics, sound, difficulty " +
            "and balance may change, and there may be bugs.\n\n" +
            "• Duration: about 15 minutes (a tutorial and then two short runs, which are the study).\n" +
            "• While you play, the game records gameplay data (hits, misses, energy, deaths, time). " +
            "After each part you will answer a few short questions, and at the end a questionnaire on Google Forms. " +
            "If you keep playing the normal mode after the test, the game will also record that data, with the same code.\n" +
            "• Everything is ANONYMOUS: you will be given a code; your name, email or any identifying data are not collected. " +
            "The data will only be used for this thesis.\n" +
            "• Taking part is voluntary and you can stop whenever you want (close the game). You must be 18 or older.\n\n" +
            "Before you start: you need a MOUSE with left and right buttons (it cannot be played with a touchpad), " +
            "use WIRED HEADPHONES (Bluetooth delays the sound) and play somewhere quiet. " +
            "During the game, TAB shows the controls." :
            "Gracias por participar. Vas a probar un juego rítmico en primera persona que forma parte de un Trabajo Fin de Grado " +
            "de la Universidad de Granada.\n\n" +
            "IMPORTANTE: es un PROTOTIPO en desarrollo y NO representa el producto final. Los gráficos, el sonido, la dificultad " +
            "y el equilibrio pueden cambiar, y puede haber fallos.\n\n" +
            "• Duración: unos 15 minutos (un tutorial y después dos recorridos cortos, que son el estudio).\n" +
            "• Mientras juegas, el juego registra datos de la partida (aciertos, fallos, energía, muertes, tiempo). " +
            "Después de cada parte responderás unas preguntas cortas, y al final un cuestionario en Google Forms. " +
            "Si después de la prueba sigues jugando al modo normal, también se registrarán esos datos, con el mismo código.\n" +
            "• Todo es ANÓNIMO: se te asignará un código; no se recoge tu nombre, correo ni ningún dato que te identifique. " +
            "Los datos se usarán solo para este TFG.\n" +
            "• Participar es voluntario y puedes dejarlo cuando quieras (cierra el juego). Debes tener 18 años o más.\n\n" +
            "Antes de empezar: necesitas un RATÓN con clic izquierdo y derecho (no se puede jugar con el touchpad), " +
            "usa AURICULARES CON CABLE (los Bluetooth retrasan el sonido) y juega en un sitio tranquilo. " +
            "Durante la partida, TAB muestra los controles.",
            100f, 620f);
        // Si el texto no cabe, la letra se reduce un poco, pero nunca crece.
        consent.fontSizeMax = consent.fontSize; consent.fontSizeMin = 14f; consent.enableAutoSizing = true;
        // Una vez aceptado, primero se juega el tutorial, en el que no se registra nada, y el estudio empieza en su sala, al cruzar la puerta grande.
        Bottom(p, Loc.T("Acepto y quiero participar", "I agree and want to take part"), 40f, () =>
        {
            StudySession.Begin();
            Destroy(_screen); _screen = null;
            CheckpointSystem.Clear();
            SceneManager.LoadScene(StudySession.CourseScene);
        });
        Bottom(p, Loc.T("Volver", "Back"), 860f, () => { Destroy(_screen); _screen = null; });
    }

    private void ShowPartIntro()
    {
        StudySession.Part part = StudySession.Current;
        int total = StudySession.Plan.Length;
        bool course = part.scene == StudySession.CourseScene;
        if (part.shop) StudySession.GiveShopBudget();   // presupuesto fijo, para que todos puedan comprar algo
        Transform p = NewScreen(Loc.T($"Parte {StudySession.Step + 1} de {total} · {part.title}", $"Part {StudySession.Step + 1} of {total} · {part.title}"));
        string rules = course
            ? Loc.T("\n\nSi chocas o te quedas sin energía, podrás volver al último punto de control desde la pantalla de game over, " +
                    "y al reaparecer verás un consejo sobre el obstáculo.",
                    "\n\nIf you crash or run out of energy, you can go back to the last checkpoint from the game over screen, " +
                    "and when you respawn you will see a tip about the obstacle.")
            : Loc.T($"\n\nSi mueres puedes reintentar (hasta {StudySettings.MaxAttemptsPerPart} intentos); después se pasa a la siguiente parte.",
                    $"\n\nIf you die you can retry (up to {StudySettings.MaxAttemptsPerPart} attempts); after that you move on to the next part.");
        string coins = part.shop ? Loc.T($"\nMonedas: {PlayerProgress.Coins}.", $"\nCoins: {PlayerProgress.Coins}.") : "";
        // En la sala, antes de empezar el estudio, se pueden repetir partes del tutorial por las demás puertas.
        string practice = !StudySession.RunsStarted && SceneManager.GetActiveScene().name != StudySession.MenuScene
            ? Loc.T("\n\nAntes de empezar el estudio, puedes volver a jugar cualquier parte del tutorial acercándote a una de las " +
                    "puertas de la sala. Una vez cruces la puerta grande, el estudio empezará y ya no podrás repetirlas.",
                    "\n\nBefore the study starts, you can replay any part of the tutorial by going to one of the doors in the room. " +
                    "Once you go through the big door, the study will start and you will not be able to replay them.")
            : "";
        Body(p, part.description + rules + practice + coins +
                Loc.T("\nTAB: controles.   Tu código de participante: ", "\nTAB: controls.   Your participant code: ") + StudySession.Participant,
             100f, 600f);
        if (SceneManager.GetActiveScene().name == StudySession.MenuScene)
            Bottom(p, Loc.T("Empezar", "Start"), 40f, StudySession.StartPart);
        else
            // En la sala del tutorial, la parte empieza al cruzar la puerta grande.
            Bottom(p, Loc.T("Ir a la puerta grande", "Go to the big door"), 40f, () =>
            {
                Destroy(_screen); _screen = null;
                RoomPartReady?.Invoke();
            });
        if (part.shop)
        {
            // La tienda y los contratos se abren desde aquí, ya que la escena del recorrido no tiene zona de inicio.
            Bottom(p, Loc.T("Abrir la tienda", "Open the shop"), 450f, () =>
            {
                _screen.SetActive(false);
                ShopUI.Open(() => { Destroy(_screen); _screen = null; ShowPartIntro(); });
            });
            Bottom(p, Loc.T("Ver contratos", "See contracts"), 860f, () =>
            {
                _screen.SetActive(false);
                ContractUI.Open(() => { Destroy(_screen); _screen = null; ShowPartIntro(); });
            });
        }
    }

    private void ShowQuestionnaire()
    {
        StudySession.Part part = StudySession.Current;
        Transform p = NewScreen(Loc.T($"Preguntas · {part.title}", $"Questions · {part.title}"));
        Body(p, Loc.T("1 = nada de acuerdo   ·   7 = totalmente de acuerdo", "1 = strongly disagree   ·   7 = strongly agree"), 85f, 30f).color = DimText;

        string[] questions = Questions;
        int[] answers = new int[questions.Length];
        var rows = new List<Image[]>();
        float y = 125f;
        for (int q = 0; q < questions.Length; q++)
        {
            var label = Lbl("Q", p, $"{q + 1}. {questions[q]}", FontSmall + 1f, TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(label.rectTransform, 40f, y, 700f, 58f);
            var buttons = new Image[7];
            for (int v = 0; v < 7; v++)
            {
                int qi = q, val = v + 1;
                var b = Btn("V", p, val.ToString(), out _);
                PlaceTopLeft((RectTransform)b.transform, 760f + v * 70f, y + 4f, 60f, 50f);
                buttons[v] = b.image;
                b.onClick.AddListener(() =>
                {
                    answers[qi] = val;
                    for (int k = 0; k < 7; k++) rows[qi][k].color = k == val - 1 ? SelCol : BtnCol;
                });
            }
            rows.Add(buttons);
            y += 62f;
        }

        // Después del segundo recorrido se pregunta qué configuración ha gustado más.
        string pref = null;
        if (part.preference)
        {
            var label = Lbl("Pref", p, Loc.T("¿Qué recorrido te ha gustado más?", "Which run did you like more?"), FontSmall + 1f, TextAlignmentOptions.MidlineLeft);
            PlaceTopLeft(label.rectTransform, 40f, y + 6f, 600f, 50f);
            string[] opts = { Loc.T("La anterior", "The previous one"), Loc.T("Esta", "This one"), Loc.T("Igual", "Same") };
            var prefButtons = new Image[opts.Length];
            for (int i = 0; i < opts.Length; i++)
            {
                int idx = i;
                var b = Btn("P", p, opts[i], out _);
                PlaceTopLeft((RectTransform)b.transform, 660f + i * 200f, y + 6f, 190f, 50f);
                prefButtons[i] = b.image;
                b.onClick.AddListener(() =>
                {
                    StudyConfig.Profile prev = StudySession.Plan[StudySession.Step - 1].config;
                    pref = idx == 0 ? prev.ToString() : idx == 1 ? part.config.ToString() : "igual";
                    for (int k = 0; k < prefButtons.Length; k++) prefButtons[k].color = k == idx ? SelCol : BtnCol;
                });
            }
        }

        var warn = Body(p, "", 0f, 0f);
        PlaceBottomLeft(warn.rectTransform, 460f, 40f, 800f, 44f);
        Bottom(p, Loc.T("Continuar", "Continue"), 40f, () =>
        {
            if (Array.IndexOf(answers, 0) >= 0 || (part.preference && pref == null)) { warn.text = Loc.T("Responde a todas las preguntas.", "Answer all the questions."); return; }
            StudyTelemetry.Questionnaire(StudySession.Step + 1, part.scene, part.config, answers, pref);
            AfterPart();
        });
    }

    /// <summary>Después de una parte se muestra la siguiente, la elección de seguir o el final.</summary>
    private void AfterPart()
    {
        StudySession.NextPart();
        if (StudySession.Step >= StudySession.Plan.Length) { StudySession.MarkStudyCompleted(); ShowEnd(); return; }   // estudio terminado, cuestionario final
        ShowPartIntro();
    }

    /// <summary>
    /// Después del nivel 3 se abre el puesto final para cobrar los contratos cumplidos, ya que en la prueba se vuelve al
    /// menú en lugar de ir a la escena final. Si no hay nada que cobrar, se pasa directamente al final.
    /// </summary>
    private void ShowFinalStand()
    {
        int toClaim = 0, total = 0;
        foreach (ContractDef c in ContractCatalog.All)
            if (PlayerProgress.IsContractClaimable(c.id)) { toClaim++; total += c.reward; }
        if (toClaim == 0) { ShowEnd(); return; }

        Transform p = NewScreen(Loc.T("Puesto final de contratos", "Final contract stand"));
        Body(p, Loc.T($"Tienes {toClaim} contrato(s) cumplido(s) sin cobrar ({total} monedas).\n\n" +
                      "Abre el puesto de contratos para cobrarlos antes de terminar.",
                      $"You have {toClaim} completed contract(s) not yet claimed ({total} coins).\n\n" +
                      "Open the contract stand to claim them before finishing."), 100f, 300f);
        Bottom(p, Loc.T("Abrir el puesto de contratos", "Open the contract stand"), 40f, () =>
        {
            _screen.SetActive(false);
            ContractUI.Open(() => { Destroy(_screen); _screen = null; ShowEnd(); });
        });
        Bottom(p, Loc.T("Terminar sin cobrar", "Finish without claiming"), 860f, ShowEnd);
    }

    /// <summary>
    /// Después de las partes principales, el jugador puede continuar al siguiente nivel, repetir el nivel jugado en
    /// práctica libre (con todo desbloqueado y sin registrar nada) o terminar.
    /// </summary>
    private void ShowContinueChoice()
    {
        bool more = StudySession.Step < StudySession.Plan.Length;
        StudySession.Part last = StudySession.LastPlayed;
        string lastName = LevelName(last.scene);
        bool afterMain = StudySession.Step == StudySession.MainParts;

        Transform p = NewScreen(afterMain ? Loc.T("¡Parte principal terminada!", "Main part finished!")
                                          : Loc.T($"¡{lastName} terminado!", $"{lastName} finished!"));
        string intro = afterMain
            ? Loc.T($"Ya has completado las {StudySession.MainParts} partes. ¡Gracias!\n\n", $"You have completed the {StudySession.MainParts} parts. Thank you!\n\n")
            : "";
        string next = more
            ? Loc.T($"• CONTINUAR: {StudySession.Current.title}. " +
                    (afterMain ? "Los niveles 2 y 3 (unos 15 minutos más) tienen mecánicas distintas (kayak, enemigos que se desactivan mirándolos y un jefe final) y ayudan mucho al estudio.\n\n" : "\n\n"),
                    $"• CONTINUE: {StudySession.Current.title}. " +
                    (afterMain ? "Levels 2 and 3 (about 15 more minutes) have different mechanics (kayak, enemies you disable by looking at them and a final boss) and they help the study a lot.\n\n" : "\n\n"))
            : "";
        string practice = Loc.T(
            $"• PRACTICAR: repite el {lastName} con TODO desbloqueado: la tienda con todas las mejoras, {StudySession.FreePlayCoins} monedas " +
            "para probarlas, contratos y sin límite de intentos. Es solo para practicar: no se guarda nada ni se registran datos, " +
            "y al volver todo queda como estaba. Cuando termines, vuelve al menú (Esc → Menú, o acaba el nivel) y desde aquí " +
            "podrás continuar con el siguiente nivel.\n\n",
            $"• PRACTICE: replay {lastName} with EVERYTHING unlocked: the shop with all upgrades, {StudySession.FreePlayCoins} coins " +
            "to try them, contracts and unlimited attempts. It is just for practice: nothing is saved and no data is recorded, " +
            "and when you come back everything is as it was. When you are done, go back to the menu (Esc → Menu, or finish " +
            "the level) and you will be able to continue with the next level from here.\n\n");
        string finish = Loc.T("• TERMINAR: ir al cuestionario final.", "• FINISH: go to the final questionnaire.");
        Body(p, intro + next + practice + finish, 100f, 620f);

        if (more) Bottom(p, Loc.T("Continuar", "Continue"), 40f, ShowPartIntro);
        Bottom(p, Loc.T($"Practicar {lastName}", $"Practice {lastName}"), more ? 450f : 40f, StudySession.StartFreePlay);
        Bottom(p, Loc.T("Terminar", "Finish"), 860f, ShowFinalStand);
    }

    private static string LevelName(string scene)
        => scene.StartsWith("Level") ? Loc.T("Nivel ", "Level ") + scene.Substring(5) : scene;

    private void ShowEnd()
    {
        Transform p = NewScreen(Loc.T("¡Gracias por participar!", "Thank you for taking part!"));
        Body(p, Loc.T("Falta un último paso: un cuestionario corto en Google Forms (unos 3 minutos).\n\n" +
                      $"Tu código de participante es:   {StudySession.Participant}\n\n" +
                      "El botón de abajo abre el cuestionario en el navegador con tu código ya puesto. " +
                      "Si no se abre, apunta el código y pídele el enlace a quien te pasó el juego.\n\n" +
                      "Después, desde el menú, ya puedes jugar al modo normal (JUGAR).",
                      "One last step: a short questionnaire on Google Forms (about 3 minutes).\n\n" +
                      $"Your participant code is:   {StudySession.Participant}\n\n" +
                      "The button below opens the questionnaire in your browser with your code already filled in. " +
                      "If it does not open, write down the code and ask whoever gave you the game for the link.\n\n" +
                      "Afterwards, the normal mode (PLAY) is unlocked in the menu."), 100f, 500f);
        string url = string.IsNullOrEmpty(StudySettings.FinalFormUrl) ? null : StudySettings.FinalFormUrl.Replace("CODIGO", Uri.EscapeDataString(StudySession.Participant));
        Bottom(p, Loc.T("Abrir el cuestionario final", "Open the final questionnaire"), 40f, () => { if (url != null && StudyTelemetry.IsSafeUrl(url)) Application.OpenURL(url); });
        // Al volver al menú, la prueba queda terminada y JUGAR (el modo normal) ya está desbloqueado.
        Bottom(p, Loc.T("Volver al menú", "Back to menu"), 450f, () =>
        {
            Destroy(_screen); _screen = null;
            StudySession.Finish();
            CheckpointSystem.Clear();
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(StudySession.MenuScene);
        });
        Bottom(p, Loc.T("Salir del juego", "Quit the game"), 860f, () => { StudySession.Finish(); Application.Quit(); });
    }
}
