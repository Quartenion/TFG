/// <summary>
/// GUION DEL TUTORIAL DEL NIVEL 1 (sacado de la grabación de una partida completa, TutorialRecordings/).
/// Cada paso: en su BEAT el tiempo se frena hasta pararse, se oscurece la pantalla, se ilumina el obstáculo y se pide
/// la acción. El jugador la hace (ese mismo instante, en el beat) y el juego vuelve a arrancar.
///   • beat      : beat de la canción en el que hay que hacer la acción.
///   • key       : la tecla que se pide (ver TutorialKey).
///   • text/en   : explicación. CON texto = el juego se FRENA y se para (primera vez de cada movimiento).
///                 SIN texto = no se frena: solo sale el aviso «Pulsa X para …» arriba y la nota en el monitor.
///   • highlight : ruta en la jerarquía del objeto que se ilumina (vacía = ninguno).
///   • side      : solo para el dash: -1 = izquierda, +1 = derecha.
/// Para cambiar un aviso: edita su fila. Para quitarlo: bórrala.
/// </summary>
public static class TutorialLevel1Steps
{
    public struct Step
    {
        public float beat;
        public TutorialKey key;
        public string text, en;
        public string highlight;
        public int side;
        /// <summary>Objeto que se ilumina (lo usa el nivel tutorial, que crea los obstáculos en tiempo de ejecución).</summary>
        public UnityEngine.Transform target;
        /// <summary>Al pararse, la vista gira sola hacia el objetivo (interruptor de mirada, tirolina).</summary>
        public bool look;
        /// <summary>Explicación que sale JUSTO DESPUÉS de hacer la acción, sin reanudar (p. ej. moverse en la tirolina).</summary>
        public string follow;
        /// <summary>Parte del HUD de la que habla la explicación (se resalta mientras está parado).</summary>
        public SyringeHighlight.Side hud;
    }

    /// <summary>Hasta este beat dura el tutorial (después el nivel sigue normal).</summary>
    public const float EndBeat = 192f;

    private const string T = "Nivel/Timeline/";

    private static Step S(float beat, TutorialKey key, string highlight, string text = "", string en = "", int side = 0)
        => new Step { beat = beat, key = key, highlight = string.IsNullOrEmpty(highlight) ? "" : T + highlight, text = text, en = en, side = side };

    public static readonly Step[] Steps =
    {
        // ── Saltos ──
        S(7f,  TutorialKey.Jump, "Segmento 002 · beats 8-11/Obstáculos/Cube",
            "Durante tu trayecto encontrarás obstáculos que puedes saltar, como este.",
            "Along the way you will find obstacles you can jump over, like this one."),
        S(15f, TutorialKey.Jump, "Segmento 004 · beats 16-19/Obstáculos/Cube (4)"),
        S(23f, TutorialKey.Jump, "Segmento 006 · beats 24-27/Obstáculos/Cube (5)"),

        // ── Doble salto y slam ──
        S(29f, TutorialKey.Jump, "Segmento 007 · beats 28-31/Obstáculos/Cube (6)"),
        S(30f, TutorialKey.DoubleJump, "Segmento 007 · beats 28-31/Obstáculos/Cube (6)",
            "Este obstáculo es más alto: en el aire, vuelve a saltar para hacer un doble salto.",
            "This obstacle is taller: jump again in the air to double jump."),
        S(31f, TutorialKey.Slam, "Segmento 007 · beats 28-31/Obstáculos/Cube (7)",
            "En el aire puedes caer en picado (slam) para bajar al instante.", "In the air you can slam down to land instantly."),

        // ── Deslizarse ──
        S(39f, TutorialKey.Slide, "Segmento 009 · beats 36-39/Escenario/Mine Structure/Tight Walls/Top Tight",
            "Cuando el techo esté demasiado bajo, deslízate por debajo.", "When the ceiling is too low, slide under it."),
        S(47f, TutorialKey.Slide, "Segmento 011 · beats 44-47/Escenario/Mine Structure/Tight Walls/Top Tight"),
        S(55f, TutorialKey.Slide, "Segmento 013 · beats 52-55/Escenario/Mine Structure/Tight Walls/Top Tight"),
        S(61f, TutorialKey.Slide, "Segmento 015 · beats 60-63/Escenario/Mine Structure/Tight Walls/Top Tight"),
        // (62,25 y no 62: en el beat 62 el jugador aún está bajo el techo y al saltar se queda atascado contra él;
        //  en la grabación el salto fue en el 62,22)
        S(62.25f, TutorialKey.Jump,  "Segmento 015 · beats 60-63/Obstáculos/Cube (5)",
            "Puedes cortar el deslizamiento saltando.", "You can cancel a slide by jumping."),
        S(63f, TutorialKey.Slam,  "Segmento 015 · beats 60-63/Obstáculos/Cube (5)"),

        // ── Dash ──
        S(71f, TutorialKey.Jump, "Segmento 017 · beats 68-71/Escenario/Mine Structure/Arch/Support/Right Side Beam"),
        S(72f, TutorialKey.Dash, "Segmento 017 · beats 68-71/Escenario/Mine Structure/Arch/Support/Beam Top",
            "El dash te impulsa de golpe hacia un lado para esquivar.", "The dash pushes you sideways in an instant to dodge.", side: -1),
        S(79f, TutorialKey.Jump, "Segmento 019 · beats 76-79/Escenario/Mine Structure/Arch/Support/Left Side Beam"),
        S(80f, TutorialKey.Dash, "Segmento 019 · beats 76-79/Escenario/Mine Structure/Arch/Support/Beam Top", side: +1),
        S(87f, TutorialKey.Jump, "Segmento 021 · beats 84-87/Escenario/Mine Structure/Arch/Support/Right Side Beam"),
        S(88f, TutorialKey.Dash, "Segmento 021 · beats 84-87/Escenario/Mine Structure/Arch/Support/Beam Top", side: -1),

        // ── Saltos encadenados ──
        S(98f,  TutorialKey.Jump, "Segmento 024 · beats 96-99/Escenario/Mine Structure/Arch/Support/Left Side Beam"),
        S(100f, TutorialKey.Jump, "Segmento 024 · beats 96-99/Escenario/Mine Structure/Arch/Support/Left Diagonal Beam"),
        S(102f, TutorialKey.DoubleJump, "Segmento 026 · beats 104-107/Escenario/Mine Structure/Arch/Cube (1)"),
        S(104f, TutorialKey.Jump, "Segmento 026 · beats 104-107/Escenario/Mine Structure/Arch/Cube (2)"),
        S(106f, TutorialKey.DoubleJump, "Segmento 026 · beats 104-107/Escenario/Mine Structure/Arch/Cube (3)"),
        S(108f, TutorialKey.Jump, "Segmento 027 · beats 108-111/Escenario/Mine Structure/Arch/Cube (2)"),
        S(110f, TutorialKey.DoubleJump, "Segmento 027 · beats 108-111/Escenario/Mine Structure/Arch/Cube (1)"),
        S(112f, TutorialKey.Jump, "Segmento 028 · beats 112-115/Escenario/Mine Structure/Arch/Cube (1)"),

        // ── Tirolina ──
        S(112.75f, TutorialKey.Zipline, "",
            "Engánchate a la tirolina para cruzar.", "Grab the zipline to get across."),
        S(122f, TutorialKey.Jump, "Segmento 030 · beats 120-123/Escenario/Mine Structure/Arch/Cube (1)"),

        // ── Romper puertas ──
        S(135f, TutorialKey.Block, "Segmento 034 · beats 136-139/Obstáculos/Door (1)",
            "Algunas puertas se pueden romper. Rómpela justo antes de llegar.", "Some doors can be broken. Break it just before you reach it."),
        S(143f, TutorialKey.Block, "Segmento 036 · beats 144-147/Obstáculos/Door (1)"),
        S(151f, TutorialKey.Block, ""),
        S(157f, TutorialKey.Block, "Segmento 039 · beats 156-159/Obstáculos/Door (1)"),
        S(158f, TutorialKey.Block, "Segmento 039 · beats 156-159/Obstáculos/Cube (2)"),
        S(159f, TutorialKey.Block, "Segmento 039 · beats 156-159/Obstáculos/Cube (5)"),

        // ── Paredes ──
        S(168f, TutorialKey.Jump, "Segmento 042 · beats 168-171/Escenario/Mine Structure/Cube (15)",
            "Salta hacia la pared para correr por ella.", "Jump towards the wall to run along it."),
        S(170f, TutorialKey.Jump, "Segmento 042 · beats 168-171/Escenario/Mine Structure/Cube (15)",
            "Desde la pared, salta para impulsarte a la pared de enfrente.", "From the wall, jump to push off to the opposite wall."),
        S(172f, TutorialKey.Jump, "Segmento 043 · beats 172-175/Escenario/Mine Structure/Cube (15)"),
        S(174f, TutorialKey.Jump, "Segmento 043 · beats 172-175/Escenario/Mine Structure/Cube (16)"),
        S(176f, TutorialKey.Jump, "Segmento 044 · beats 176-179/Escenario/Mine Structure/Cube (15)"),
        S(178f, TutorialKey.Jump, "Segmento 044 · beats 176-179/Escenario/Mine Structure/Cube (16)"),
        S(180f, TutorialKey.Jump, "Segmento 045 · beats 180-183/Escenario/Mine Structure/Cube (15)"),
        S(182f, TutorialKey.Jump, "Segmento 045 · beats 180-183/Escenario/Mine Structure/Cube (16)"),
        S(184f, TutorialKey.Jump, "Segmento 046 · beats 184-187/Escenario/Mine Structure/Cube (15)"),
        S(186f, TutorialKey.Jump, "Segmento 046 · beats 184-187/Escenario/Mine Structure/Cube (16)"),
        S(188f, TutorialKey.Jump, "Segmento 046 · beats 184-187/Escenario/Mine Structure/Cube (16)"),
    };

    // ── Explicaciones que salen UNA sola vez (por evento, no por beat) ──

    /// <summary>Beat de la explicación de la energía (antes del primer salto).</summary>
    public const float EnergyBeat = 3f;

    public static string EnergyText => Loc.T(
        "Tu corazón pierde sangre poco a poco: va pasando a la jeringuilla roja (izquierda), y cada acción (saltar, deslizarte, hacer dash…) " +
        "hace que pase más. Si la jeringuilla roja se llena del todo, te quedas sin energía y es GAME OVER.\n" +
        "Haz clic AL RITMO de la música para devolver sangre al corazón. No es obligatorio hacer clic en todos los beats: " +
        "si no haces clic, solo pierdes sangre poco a poco.",
        "Your heart slowly loses blood: it flows into the red syringe (left), and every action (jumping, sliding, dashing…) " +
        "makes more of it flow. If the red syringe fills up completely, you run out of energy and it's GAME OVER.\n" +
        "Click ON THE BEAT of the music to send blood back to your heart. You don't have to click on every beat: " +
        "if you don't click, you only lose blood slowly.");

    public static string MissText => Loc.T(
        "Has fallado el latido. Cada clic fuera de ritmo te hace perder más sangre de golpe. " +
        "Si no estás seguro, mejor no hagas clic: sin clic solo pierdes sangre poco a poco.",
        "You missed the beat. Every off-beat click makes you lose extra blood at once. " +
        "If you're not sure, it's better not to click: without clicking you only lose blood slowly.");

    public static string BlueText => Loc.T(
        "¡Sangre azul! Tu corazón está lleno, así que los aciertos ahora llenan la jeringuilla azul: energía extra para hacer más acciones. " +
        "Si la llenas del todo entras en SOBRECARGA: durante 4 beats las acciones no gastan energía.",
        "Blue blood! Your heart is full, so hits now fill the blue syringe: extra energy for more actions. " +
        "If you fill it completely you enter OVERLOAD: for 4 beats your actions cost no energy.");

    public static string BlueMissText => Loc.T(
        "Cuidado: si fallas el beat con sangre azul, la pierdes toda de golpe.",
        "Careful: if you miss the beat with blue blood, you lose all of it at once.");
}
