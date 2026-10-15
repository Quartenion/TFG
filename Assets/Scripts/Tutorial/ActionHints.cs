using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// PISTAS (Ajustes → PISTAS: SÍ / NO): durante TODO el nivel 1 caen notas en el monitor del corazón con la acción que
/// toca y su tecla (las de <see cref="TutorialLevel1Hints"/>). Son solo de referencia: si se ignoran no pasa nada.
/// Si la haces cerca de su beat, la nota se pone verde; si no, se apaga.
/// Mientras el tutorial está en marcha, sus pasos ya tienen nota propia, así que las pistas no las repiten.
/// </summary>
public class ActionHints : MonoBehaviour
{
    private const string PrefKey = "Hints.On";
    private const string SceneName = "Level1";
    private static readonly Color HintCol = new Color(0.92f, 0.92f, 0.92f);

    /// <summary>¿Están activadas las pistas? (Ajustes)</summary>
    public static bool On
    {
        get => PlayerPrefs.GetInt(PrefKey, 0) == 1;
        set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); Refresh(); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Refresh();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Refresh();

    /// <summary>Crea o quita las pistas en la escena actual según el ajuste (se puede cambiar desde la pausa).</summary>
    private static void Refresh()
    {
        ActionHints existing = FindAnyObjectByType<ActionHints>();
        bool want = On && SceneManager.GetActiveScene().name == SceneName && !StudySession.Active;
        if (want && existing == null) new GameObject("ActionHints").AddComponent<ActionHints>();
        else if (!want && existing != null) Destroy(existing.gameObject);
    }

    private BeatManager _bm;
    private PlayerMovement _pm;
    private BlockAbility _block;
    private HeartMonitorNotes _lane;
    private int _next;
    private readonly HashSet<int> _hit = new HashSet<int>();
    private TutorialKey _act = TutorialKey.None;

    private void Start()
    {
        Canvas c = UIKit.CreateCanvas("PistasCanvas", 690, transform);   // (debajo del tutorial)
        _lane = new HeartMonitorNotes((RectTransform)c.transform);
    }

    private void OnDestroy() => _lane?.Clear();

    private void Hook()
    {
        if (_pm != null) return;
        _bm = BeatManager.Instance;
        _pm = FindAnyObjectByType<PlayerMovement>();
        if (_pm == null) return;
        _pm.OnJumpExecuted ??= new UnityEngine.Events.UnityEvent();
        _pm.OnJumpExecuted.AddListener(() => _act = TutorialKey.Jump);
        _pm.OnWalljumped ??= new UnityEngine.Events.UnityEvent();
        _pm.OnWalljumped.AddListener(() => _act = TutorialKey.Jump);
        _pm.OnDashStarted ??= new UnityEngine.Events.UnityEvent<Vector3>();
        _pm.OnDashStarted.AddListener(_ => _act = TutorialKey.Dash);
        _pm.OnSlideStarted ??= new UnityEngine.Events.UnityEvent();
        _pm.OnSlideStarted.AddListener(() => _act = TutorialKey.Slide);
        _pm.OnSlamStarted ??= new UnityEngine.Events.UnityEvent();
        _pm.OnSlamStarted.AddListener(() => _act = TutorialKey.Slam);
        _pm.OnZiplineAttached ??= new UnityEngine.Events.UnityEvent();
        _pm.OnZiplineAttached.AddListener(() => _act = TutorialKey.Zipline);
        _block = FindAnyObjectByType<BlockAbility>();
        if (_block != null)
        {
            _block.OnBlockStarted ??= new UnityEngine.Events.UnityEvent();
            _block.OnBlockStarted.AddListener(() => _act = TutorialKey.Block);
        }
    }

    private void Update()
    {
        Hook();
        if (_bm == null) _bm = BeatManager.Instance;
        if (_pm == null || _bm == null || !_bm.HasClock || _pm.IsFreeroam || _bm.BeatDuration <= 0f) { _act = TutorialKey.None; return; }

        float beatNow = _bm.CorrectedTimeUnclamped / _bm.BeatDuration;
        var all = TutorialLevel1Hints.All;

        // Saltar las que ya pasaron (p. ej. empezando en un checkpoint).
        while (_next < all.Length && all[_next].beat < beatNow - 0.5f) _next++;

        // Crear las que tocan (salvo las que ya enseña el tutorial).
        for (int i = _next; i < all.Length && all[i].beat <= beatNow + HeartMonitorNotes.LeadBeats; i++)
        {
            if (_lane.Has(i) || _hit.Contains(i)) continue;
            if (TutorialDirector.StepsRunning && all[i].beat <= TutorialLevel1Steps.EndBeat) continue;
            _lane.Spawn(i, all[i].beat, TutorialDirector.NoteLabel(all[i].key, all[i].side), HintCol);
        }

        // Acierto: la acción correcta cerca de su beat → verde. Pasada → se apaga.
        foreach (int id in new List<int>(_lane.Ids))
        {
            float b = all[id].beat;
            if (_act != TutorialKey.None && !_hit.Contains(id) && Mathf.Abs(beatNow - b) <= 0.75f && Matches(_act, all[id].key))
            {
                _hit.Add(id); _lane.Hit(id); _act = TutorialKey.None;
            }
            else if (beatNow > b + 0.4f) _lane.Fade(id);
        }

        _lane.Update(beatNow);
        _act = TutorialKey.None;
    }

    private static bool Matches(TutorialKey done, TutorialKey wanted)
        => done == wanted || (wanted == TutorialKey.DoubleJump && done == TutorialKey.Jump);
}
