using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// TECLAS CONFIGURABLES (Ajustes → Controles). Guarda en PlayerPrefs la tecla de cada acción del teclado.
/// Los clics del ratón (latido / kayak), Esc y P (pausa), TAB (controles) y F2 (consejos) son fijos.
///
/// Uso desde el código:
///   • Acciones del Input System: <see cref="Apply"/> (y volver a llamarlo en <see cref="Changed"/>).
///   • Lecturas directas: <see cref="Pressed"/> / <see cref="Held"/> en vez de Keyboard.current.xKey.
///   • Textos que nombran teclas («[ E ] Tienda», carteles del tutorial): <see cref="Fix"/>.
/// </summary>
public static class Keybinds
{
    public enum Bind { Forward, Back, Left, Right, Jump, Slide, Dash, Slam, Block, Zipline, Interact, Metronome }

    public static readonly Bind[] All = (Bind[])Enum.GetValues(typeof(Bind));

    private static readonly Dictionary<Bind, Key> Defaults = new Dictionary<Bind, Key>
    {
        { Bind.Forward, Key.W }, { Bind.Back, Key.S }, { Bind.Left, Key.A }, { Bind.Right, Key.D },
        { Bind.Jump, Key.Space }, { Bind.Slide, Key.LeftShift }, { Bind.Dash, Key.E }, { Bind.Slam, Key.S },
        { Bind.Block, Key.Q }, { Bind.Zipline, Key.F }, { Bind.Interact, Key.E }, { Bind.Metronome, Key.M },
    };

    /// <summary>Parejas que comparten tecla A PROPÓSITO (no se marcan como conflicto).</summary>
    private static bool SharedOnPurpose(Bind a, Bind b)
        => (a == Bind.Back && b == Bind.Slam) || (a == Bind.Slam && b == Bind.Back)
        || (a == Bind.Dash && b == Bind.Interact) || (a == Bind.Interact && b == Bind.Dash);

    /// <summary>Teclas que no se pueden asignar (pausa / ayuda).</summary>
    public static bool Reserved(Key k) => k == Key.Escape || k == Key.P || k == Key.Tab || k == Key.F2 || k == Key.None;

    /// <summary>Se lanza al cambiar cualquier tecla.</summary>
    public static event Action Changed;

    private static readonly Dictionary<Bind, Key> _cache = new Dictionary<Bind, Key>();

    private static string Pref(Bind b) => "Key_" + b;

    public static Key Default(Bind b) => Defaults[b];

    public static Key Get(Bind b)
    {
        if (_cache.TryGetValue(b, out Key k)) return k;
        k = (Key)PlayerPrefs.GetInt(Pref(b), (int)Defaults[b]);
        if (!Enum.IsDefined(typeof(Key), k) || Reserved(k)) k = Defaults[b];
        _cache[b] = k;
        return k;
    }

    public static void Set(Bind b, Key k)
    {
        if (Reserved(k) || Get(b) == k) return;
        _cache[b] = k;
        PlayerPrefs.SetInt(Pref(b), (int)k);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static void ResetAll()
    {
        foreach (Bind b in All) { _cache[b] = Defaults[b]; PlayerPrefs.DeleteKey(Pref(b)); }
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>¿Otra acción usa la misma tecla (sin contar las que la comparten a propósito)?</summary>
    public static bool Conflicts(Bind b)
    {
        Key k = Get(b);
        foreach (Bind o in All)
            if (o != b && Get(o) == k && !SharedOnPurpose(b, o)) return true;
        return false;
    }

    // ── Lectura ───────────────────────────────────────────────────────────────

    private static KeyControl Control(Bind b)
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return null;
        try { return kb[Get(b)]; } catch { return null; }
    }

    public static bool Pressed(Bind b)  { var c = Control(b); return c != null && c.wasPressedThisFrame; }
    public static bool Held(Bind b)     { var c = Control(b); return c != null && c.isPressed; }

    /// <summary>Ruta del Input System («&lt;Keyboard&gt;/space»).</summary>
    public static string Path(Bind b) => Path(Get(b));

    public static string Path(Key k)
    {
        Keyboard kb = Keyboard.current;
        if (kb != null) { try { return "<Keyboard>/" + kb[k].name; } catch { } }
        string s = k.ToString();
        return "<Keyboard>/" + char.ToLowerInvariant(s[0]) + s.Substring(1);
    }

    /// <summary>Pone la tecla de 'b' en la acción (binding número 'index'; en un compuesto WASD: 1 arriba…4 derecha).</summary>
    public static void Apply(InputAction action, Bind b, int index = 0)
    {
        if (action == null || index < 0 || index >= action.bindings.Count) return;
        if (Get(b) == Defaults[b]) action.RemoveBindingOverride(index);
        else action.ApplyBindingOverride(index, Path(b));
    }

    // ── Nombres ───────────────────────────────────────────────────────────────

    /// <summary>Nombre corto de la tecla para enseñarlo («ESPACIO», «SHIFT», «Q»…).</summary>
    public static string Name(Bind b) => Name(Get(b));

    public static string Name(Key k)
    {
        switch (k)
        {
            case Key.Space:      return Loc.T("ESPACIO", "SPACE");
            case Key.LeftShift:  return "SHIFT";
            case Key.RightShift: return Loc.T("SHIFT DCHO", "RIGHT SHIFT");
            case Key.LeftCtrl:   return "CTRL";
            case Key.RightCtrl:  return Loc.T("CTRL DCHO", "RIGHT CTRL");
            case Key.LeftAlt:    return "ALT";
            case Key.RightAlt:   return "ALT GR";
            case Key.Tab:        return "TAB";
            case Key.Enter:      return "ENTER";
            case Key.Backspace:  return Loc.T("RETROCESO", "BACKSPACE");
            case Key.CapsLock:   return Loc.T("BLOQ MAYÚS", "CAPS LOCK");
            case Key.UpArrow:    return "↑";
            case Key.DownArrow:  return "↓";
            case Key.LeftArrow:  return "←";
            case Key.RightArrow: return "→";
        }
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            try { string d = kb[k].displayName; if (!string.IsNullOrEmpty(d)) return d.ToUpperInvariant(); } catch { }
        }
        string s = k.ToString();
        if (s.StartsWith("Digit")) s = s.Substring(5);
        return s.ToUpperInvariant();
    }

    /// <summary>Qué hace cada acción (para la pantalla de Controles).</summary>
    public static string Label(Bind b)
    {
        switch (b)
        {
            case Bind.Forward:   return Loc.T("ADELANTE", "FORWARD");
            case Bind.Back:      return Loc.T("ATRÁS", "BACK");
            case Bind.Left:      return Loc.T("IZQUIERDA", "LEFT");
            case Bind.Right:     return Loc.T("DERECHA", "RIGHT");
            case Bind.Jump:      return Loc.T("SALTAR", "JUMP");
            case Bind.Slide:     return Loc.T("DESLIZARSE", "SLIDE");
            case Bind.Dash:      return "DASH";
            case Bind.Slam:      return Loc.T("SLAM EN EL AIRE", "SLAM IN THE AIR");
            case Bind.Block:     return Loc.T("BLOQUEAR", "BLOCK");
            case Bind.Zipline:   return Loc.T("TIROLINA", "ZIPLINE");
            case Bind.Interact:  return Loc.T("INTERACTUAR", "INTERACT");
            case Bind.Metronome: return Loc.T("METRÓNOMO", "METRONOME");
        }
        return b.ToString();
    }

    /// <summary>
    /// Cambia en un texto los nombres de las teclas POR DEFECTO por las elegidas («[ E ] Tienda» → «[ R ] Tienda»).
    /// Si no se ha cambiado nada, devuelve el texto tal cual.
    /// </summary>
    public static string Fix(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        bool any = false;
        foreach (Bind b in All) if (Get(b) != Defaults[b]) { any = true; break; }
        if (!any) return s;

        // WASD (las cuatro juntas) primero, para que no lo toquen las letras sueltas.
        string wasd = Name(Bind.Forward) + Name(Bind.Left) + Name(Bind.Back) + Name(Bind.Right);
        string wasdSp = Name(Bind.Forward) + " " + Name(Bind.Left) + " " + Name(Bind.Back) + " " + Name(Bind.Right);
        s = s.Replace("W A S D", "\u0001").Replace("WASD", "\u0002");

        // Una sola pasada (si Q pasa a ser E, esa E nueva no se vuelve a cambiar). Solo palabras sueltas en
        // MAYÚSCULAS: «E» sí, «EMPEZAR» no. (A y D sueltas no: «SALTA A LA PARED» no es una tecla.)
        s = Token.Replace(s, m =>
        {
            Bind b;
            switch (m.Value)
            {
                case "ESPACIO": case "SPACE": b = Bind.Jump; break;
                case "SHIFT": b = Bind.Slide;     break;
                case "Q":     b = Bind.Block;     break;
                case "E":     b = Bind.Interact;  break;
                case "F":     b = Bind.Zipline;   break;
                case "S":     b = Bind.Slam;      break;
                case "M":     b = Bind.Metronome; break;
                default: return m.Value;
            }
            return Get(b) == Defaults[b] ? m.Value : Name(b);
        });
        return s.Replace("\u0001", wasdSp).Replace("\u0002", wasd);
    }

    private static readonly Regex Token =
        new Regex(@"(?<![\p{L}\p{N}])(?:ESPACIO|SPACE|SHIFT|Q|E|F|S|M)(?![\p{L}\p{N}])", RegexOptions.Compiled);
}
