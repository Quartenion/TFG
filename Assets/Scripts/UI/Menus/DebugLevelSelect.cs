using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// MENÚ SECRETO DE DEPURACIÓN (para capturas y pruebas en el juego exportado).
///
/// En el menú principal, Ctrl + Mayús + D abre una ventana para cargar directamente cualquier nivel o parte del
/// nivel tutorial. Al abrirlo se activa el MODO DEPURACIÓN hasta cerrar el juego: no se registra ni se envía ningún
/// dato al formulario (ver StudyTelemetry.Recording). El resto del juego funciona igual.
/// </summary>
public class DebugLevelSelect : MonoBehaviour
{
    /// <summary>¿Se ha abierto el menú de depuración en esta ejecución? (Mientras tanto no se registran datos.)</summary>
    public static bool Active { get; private set; }

    private bool _open;
    private Rect _win = new Rect(0, 0, 360, 370);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name != StudySession.MenuScene) return;
        if (FindAnyObjectByType<DebugLevelSelect>() != null) return;
        new GameObject("DebugLevelSelect").AddComponent<DebugLevelSelect>();
    }

    private void Update()
    {
        Keyboard k = Keyboard.current;
        if (k == null) return;
        bool ctrl = k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed;
        bool shift = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
        if (ctrl && shift && k.dKey.wasPressedThisFrame)
        {
            _open = !_open;
            if (_open)
            {
                Active = true;
                Debug.Log("[Debug] Menú de depuración abierto: no se registran datos hasta cerrar el juego.");
            }
        }
        if (_open && k.escapeKey.wasPressedThisFrame) _open = false;
    }

    private void OnGUI()
    {
        if (!_open) return;
        _win.x = (Screen.width - _win.width) * 0.5f;
        _win.y = (Screen.height - _win.height) * 0.5f;
        GUI.Window(91357, _win, DrawWindow, "DEPURACIÓN · no se envían datos");
    }

    private void DrawWindow(int id)
    {
        GUILayout.Space(6);
        GUILayout.Label("Cargar directamente:");
        if (GUILayout.Button("Nivel 1 (la mina)", GUILayout.Height(30))) Load("Level1");
        if (GUILayout.Button("Nivel 2 (el purgatorio)", GUILayout.Height(30))) Load("Level2");
        if (GUILayout.Button("Nivel 3 (el infierno)", GUILayout.Height(30))) Load("Level3");
        GUILayout.Space(6);
        if (GUILayout.Button("Tutorial completo", GUILayout.Height(30))) LoadCourse(CourseMode.Tutorial);
        if (GUILayout.Button("Sala de las puertas", GUILayout.Height(30))) LoadCourse(CourseMode.Menu);
        if (GUILayout.Button("Recorrido del estudio (sin datos)", GUILayout.Height(30))) LoadCourse(CourseMode.Study);
        GUILayout.Space(6);
        if (GUILayout.Button("Borrar TODOS los datos guardados", GUILayout.Height(26))) ResetAll();
        if (GUILayout.Button("Cerrar (Esc)")) _open = false;
    }

    /// <summary>
    /// Como un equipo nuevo: borra los PlayerPrefs (prueba completada, código, tutorial hecho, explicaciones vistas,
    /// ajustes y teclas) y la partida guardada (monedas, mejoras, contratos). El tutorial vuelve a frenar y explicar todo.
    /// </summary>
    private static void ResetAll()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        PlayerProgress.ResetAll();
        TutorialDirector.ResetOnceExplanations();
        CourseBuilder.ResetTutorialRun();
        Debug.Log("[Debug] Datos guardados borrados: el juego está como recién instalado.");
    }

    private static void Prepare()
    {
        if (StudySession.Active) StudySession.Finish();
        CheckpointSystem.Clear();
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    private static void Load(string scene)
    {
        Prepare();
        CourseBuilder.Selected = CourseMode.None;
        if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
        else Debug.LogWarning($"[Debug] La escena «{scene}» no está en Build Settings.");
    }

    private static void LoadCourse(CourseMode mode)
    {
        Prepare();
        CourseBuilder.DebugSelect(mode);
        SceneManager.LoadScene(StudySession.CourseScene);
    }
}
