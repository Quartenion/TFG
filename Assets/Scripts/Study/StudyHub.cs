using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static SimpleUI;

/// <summary>
/// PANTALLAS DEL MODO PRUEBA en el menú principal (se crea sola en «MainMenu»):
///   • Botón «JUGAR» → consentimiento informado.
///   • Antes de cada parte: qué se va a jugar.  • Después: cuestionario corto (1-7).
///   • Tras la parte 3 (y la 4 y la 5): seguir al siguiente nivel, repetir el nivel en PRÁCTICA LIBRE (todo
///     desbloqueado, sin registrar datos) o terminar.  • Final: código + cuestionario de Google Forms.
/// </summary>
public class StudyHub : MonoBehaviour
{
    /// <summary>Preguntas del cuestionario corto (1 = nada de acuerdo, 7 = totalmente de acuerdo).</summary>
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

        // Sin sesión: no se enseña nada. La prueba se empieza con el botón Play del menú cuando el MainMenuManager
        // tiene marcado «Playtest» (versión que se reparte a los jugadores).
        if (!StudySession.Active) return;

        // Vuelta de la práctica libre: todo como estaba y otra vez a elegir.
        if (StudySession.FreePlay) { StudySession.EndFreePlay(); ShowContinueChoice(); return; }

        if (StudySession.AwaitingReturn)
        {
            StudySession.ReturnedToMenu();
            if (StudySession.Current.questionnaire) { ShowQuestionnaire(); return; }
        }
        else if (StudySession.Step >= 0) { ShowPartIntro(); return; }   // (p. ej. se volvió al menú desde la pausa)
        AfterPart();
    }

    // ── Pantallas ─────────────────────────────────────────────────────────────

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
    private void Awake() => _instance = this;
    private void OnDestroy() { if (_instance == this) _instance = null; }

    /// <summary>Abre el consentimiento (lo llama el botón Play del menú en la versión de prueba).</summary>
    public static void OpenConsent()
    {
        if (_instance == null) _instance = new GameObject("StudyHub").AddComponent<StudyHub>();
        if (_instance._canvas == null) _instance.Start();
        _instance.ShowConsent();
    }

    private void ShowConsent()
    {
        Transform p = NewScreen(Loc.T("Prueba del juego · Trabajo Fin de Grado", "Game test · Bachelor's Thesis"));
        Body(p, Loc.English ?
            "Thank you for taking part. You are going to try a first-person rhythm game that is part of a Bachelor's Thesis " +
            "at the University of Granada.\n\n" +
            "IMPORTANT: this is a PROTOTYPE in development and does NOT represent the final product. Graphics, sound, difficulty " +
            "and balance may change, and there may be bugs.\n\n" +
            "• Duration: about 30 minutes (3 required parts of about 15 min and 2 optional ones).\n" +
            "• While you play, the game records gameplay data (hits, misses, energy, deaths, time). " +
            "After each part you will answer a few short questions, and at the end a questionnaire on Google Forms.\n" +
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
            "• Duración: unos 30 minutos (3 partes obligatorias de unos 15 min y 2 opcionales).\n" +
            "• Mientras juegas, el juego registra datos de la partida (aciertos, fallos, energía, muertes, tiempo). " +
            "Después de cada parte responderás unas preguntas cortas, y al final un cuestionario en Google Forms.\n" +
            "• Todo es ANÓNIMO: se te asignará un código; no se recoge tu nombre, correo ni ningún dato que te identifique. " +
            "Los datos se usarán solo para este TFG.\n" +
            "• Participar es voluntario y puedes dejarlo cuando quieras (cierra el juego). Debes tener 18 años o más.\n\n" +
            "Antes de empezar: necesitas un RATÓN con clic izquierdo y derecho (no se puede jugar con el touchpad), " +
            "usa AURICULARES CON CABLE (los Bluetooth retrasan el sonido) y juega en un sitio tranquilo. " +
            "Durante la partida, TAB muestra los controles.",
            100f, 620f);
        Bottom(p, Loc.T("Acepto y quiero participar", "I agree and want to take part"), 40f, () => { StudySession.Begin(); ShowPartIntro(); });
        Bottom(p, Loc.T("Volver", "Back"), 860f, () => { Destroy(_screen); _screen = null; });
    }

    private void ShowPartIntro()
    {
        StudySession.Part part = StudySession.Current;
        int total = StudySession.Plan.Length;
        Transform p = NewScreen(Loc.T($"Parte {StudySession.Step + 1} de {total} · {part.title}", $"Part {StudySession.Step + 1} of {total} · {part.title}"));
        Body(p, part.description +
                Loc.T($"\n\nSi mueres puedes reintentar (hasta {StudySettings.MaxAttemptsPerPart} intentos); después se pasa a la siguiente parte." +
                      "\nTAB: controles.   Tu código de participante: ",
                      $"\n\nIf you die you can retry (up to {StudySettings.MaxAttemptsPerPart} attempts); after that you move on to the next part." +
                      "\nTAB: controls.   Your participant code: ") + StudySession.Participant,
             100f, 600f);
        Bottom(p, Loc.T("Empezar", "Start"), 40f, StudySession.StartPart);
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

        // Tras la 2.ª partida del Nivel 1: ¿cuál ha gustado más?
        string pref = null;
        if (part.preference)
        {
            var label = Lbl("Pref", p, Loc.T("¿Qué partida del Nivel 1 te ha gustado más?", "Which Level 1 run did you like more?"), FontSmall + 1f, TextAlignmentOptions.MidlineLeft);
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

    /// <summary>Después de una parte: la siguiente, la elección de seguir (tras la 3.ª) o el final.</summary>
    private void AfterPart()
    {
        StudySession.NextPart();
        if (StudySession.Step >= StudySession.MainParts) { ShowContinueChoice(); return; }
        ShowPartIntro();
    }

    /// <summary>
    /// Tras el Nivel 3: «puesto final» para cobrar los contratos cumplidos (en la prueba se vuelve al menú en vez de
    /// a la escena final, así que el puesto se abre aquí). Si no hay nada que cobrar, se va directo al final.
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
    /// Tras la parte 3 (y las siguientes): CONTINUAR al siguiente nivel, REPETIR el nivel jugado en práctica libre
    /// (todo desbloqueado, no se registra nada) o TERMINAR.
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
            ? Loc.T("Ya has completado las 3 partes principales. ¡Gracias!\n\n", "You have completed the 3 main parts. Thank you!\n\n")
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
                      "Si no se abre, apunta el código y pídele el enlace a quien te pasó el juego.",
                      "One last step: a short questionnaire on Google Forms (about 3 minutes).\n\n" +
                      $"Your participant code is:   {StudySession.Participant}\n\n" +
                      "The button below opens the questionnaire in your browser with your code already filled in. " +
                      "If it does not open, write down the code and ask whoever gave you the game for the link."), 100f, 500f);
        string url = string.IsNullOrEmpty(StudySettings.FinalFormUrl) ? null : StudySettings.FinalFormUrl.Replace("CODIGO", Uri.EscapeDataString(StudySession.Participant));
        Bottom(p, Loc.T("Abrir el cuestionario final", "Open the final questionnaire"), 40f, () => { if (url != null && StudyTelemetry.IsSafeUrl(url)) Application.OpenURL(url); });
        Bottom(p, Loc.T("Salir del juego", "Quit the game"), 860f, () => { StudySession.Finish(); Application.Quit(); });
    }
}
