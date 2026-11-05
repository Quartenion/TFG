/// <summary>Acciones que el tutorial puede pedir. DoubleJump usa la tecla de salto, pero en el aire.</summary>
public enum TutorialKey { None, Jump, Dash, Slide, Slam, Zipline, Block, DoubleJump }

/// <summary>
/// "Puerta" del tutorial.
///   - Mientras el tiempo está parado (ver <see cref="TutorialDirector"/>), solo se deja hacer la acción que se explica.
///   - Durante el tutorial, las acciones que todavía no se han enseñado están bloqueadas (Lock y Unlock).
/// Fuera del tutorial no hace nada, por lo que todo está permitido.
/// </summary>
public static class TutorialGate
{
    /// <summary>Indica si el tiempo está parado por el tutorial, con un aviso en pantalla.</summary>
    public static bool Frozen { get; set; }

    /// <summary>Indica si el tutorial está controlando el tiempo (frenando, parado o reanudando). Bloquea el menú de pausa.</summary>
    public static bool Busy { get; set; }

    /// <summary>Acción que se pide mientras el tiempo está parado (None si no se pide ninguna y solo hay que pulsar ENTER para seguir).</summary>
    public static TutorialKey Expected { get; set; }

    /// <summary>
    /// Acción que se va a explicar mientras el tiempo frena hasta pararse. No se puede hacer antes de ver el mensaje, ya
    /// que, si no, el jugador podría saltarse la explicación (por ejemplo, saltando mientras el kayak frena).
    /// </summary>
    public static TutorialKey HoldBack { get; set; }

    private static int _locked;   // máscara de las acciones que todavía no se han enseñado

    public static void Lock(TutorialKey k)   => _locked |= 1 << (int)k;
    public static void Unlock(TutorialKey k) => _locked &= ~(1 << (int)k);
    public static void UnlockAll()           => _locked = 0;
    public static bool IsLocked(TutorialKey k) => (_locked & (1 << (int)k)) != 0;

    public static bool Allows(TutorialKey k)
    {
        if (IsLocked(k)) return false;
        if (HoldBack != TutorialKey.None && (k == HoldBack || (k == TutorialKey.Jump && HoldBack == TutorialKey.DoubleJump))) return false;
        if (!Frozen) return true;
        return k == Expected || (k == TutorialKey.Jump && Expected == TutorialKey.DoubleJump);
    }
}
