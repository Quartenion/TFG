/// <summary>Acciones que el tutorial puede pedir. DoubleJump usa la tecla de salto, pero en el aire.</summary>
public enum TutorialKey { None, Jump, Dash, Slide, Slam, Zipline, Block, DoubleJump }

/// <summary>
/// «Puerta» del tutorial:
///   • Mientras el tiempo está PARADO (ver <see cref="TutorialDirector"/>) solo se deja hacer la acción que se explica.
///   • Durante el tutorial, las acciones que aún NO se han enseñado están BLOQUEADAS (Lock/Unlock).
/// Fuera del tutorial no hace nada (todo permitido).
/// </summary>
public static class TutorialGate
{
    /// <summary>¿Está el tiempo parado por el tutorial (con un aviso en pantalla)?</summary>
    public static bool Frozen { get; set; }

    /// <summary>¿Está el tutorial controlando el tiempo (frenando, parado o reanudando)? Bloquea el menú de pausa.</summary>
    public static bool Busy { get; set; }

    /// <summary>Acción que se está pidiendo mientras está parado (None = ninguna: solo ENTER para seguir).</summary>
    public static TutorialKey Expected { get; set; }

    /// <summary>
    /// Acción que se va a explicar mientras el tiempo FRENA hacia la parada: no se puede hacer antes de ver el mensaje
    /// (antes, saltar mientras frenaba —p. ej. en el kayak— se saltaba la explicación).
    /// </summary>
    public static TutorialKey HoldBack { get; set; }

    private static int _locked;   // máscara de acciones aún no enseñadas

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
