using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Sincroniza la cuenta atrás y el inicio del jugador con los beats de la música.
///
/// MODO AUTOMATIC:
///   - La música arranca inmediatamente.
///   - countdownSeconds define cuándo empieza el jugador (= duración de la intro).
///   - Los últimos (countdownNumbers × beatsPerNumber) beats muestran la cuenta atrás.
///   - Cada número y el "¡YA!" aparecen exactamente en un OnBeat().
///   - El jugador empieza a moverse en ese mismo OnBeat().
///
///   Ejemplo a 180 BPM (beatDur = 0.333 s), countdownSeconds = 13.33 s:
///     totalBeats = 40, countdownNumbers = 3, beatsPerNumber = 4
///     → "3" en beat 28 (t=9.33 s)
///     → "2" en beat 32 (t=10.67 s)
///     → "1" en beat 36 (t=12.00 s)
///     → "¡YA!" + jugador en beat 40 (t=13.33 s)
/// </summary>
public class GameStarter : MonoBehaviour
{
    public enum StartMode { Automatic, PressAnyKey, PressSpace }

    [Header("Referencias")]
    public BeatManager    beatManager;
    public PlayerMovement playerMovement;
    public RhythmInput    rhythmInput;
    public EnergyManager  energyManager;

    [Header("Modo de inicio")]
    public StartMode startMode = StartMode.Automatic;

    [Header("Intro — solo en modo Automatic")]
    [Tooltip("Duración de la intro de la canción en segundos.\n" +
             "El jugador empieza a correr cuando la intro acaba.\n" +
             "Ej: 13.33 s = 40 beats a 180 BPM.")]
    public float countdownSeconds = 13.33f;

    [Tooltip("Cuántos números muestra la cuenta atrás (3 → '3 2 1').")]
    [Range(1, 8)]
    public int countdownNumbers = 3;

    [Tooltip("Cuántos beats dura cada número.\n" +
             "4 beats a 180 BPM = 1 compás = 1.33 s.")]
    [Range(1, 16)]
    public int beatsPerNumber = 4;

    [Header("UI (opcional)")]
    public TMP_Text promptText;

    // ── Estado de la cuenta atrás ──────────────────────────────────────────
    private bool _started;
    private int  _beatCount;          // beats recibidos desde que arrancó la música
    private int  _totalBeats;         // beat en el que el jugador empieza
    private int  _countdownStartBeat; // beat en el que empieza a mostrarse la cuenta atrás
    private bool _movementStarted;

    // =========================================================================

    private void Start()
    {
        Time.timeScale       = 1f;
        AudioListener.pause  = false;
        AudioListener.volume = 1f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        if (rhythmInput   != null) rhythmInput.enabled   = false;
        if (energyManager != null) energyManager.enabled = false;

        if (startMode == StartMode.Automatic)
            StartCoroutine(AutoStart());
        else
            SetPrompt(startMode == StartMode.PressSpace
                ? "Pulsa ESPACIO para empezar"
                : "Pulsa cualquier tecla para empezar");
    }

    private void OnDestroy()
    {
        // Seguridad: nunca dejar el listener colgado
        if (beatManager != null)
            beatManager.OnBeat.RemoveListener(OnBeatTick);
    }

    private void Update()
    {
        if (_started || startMode == StartMode.Automatic) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        bool triggered = startMode == StartMode.PressSpace
            ? kb.spaceKey.wasPressedThisFrame
            : kb.anyKey.wasPressedThisFrame;

        if (triggered) StartCoroutine(LaunchImmediate());
    }

    // =========================================================================
    // MODO AUTOMATIC — cuenta atrás y arranque sincronizados a OnBeat
    // =========================================================================

    private IEnumerator AutoStart()
    {
        if (_started) yield break;
        _started = true;

        if (beatManager == null || playerMovement == null)
        {
            Debug.LogError("[GameStarter] Faltan referencias.", this);
            yield break;
        }

        float beatDur = beatManager.BeatDuration;
        _totalBeats         = Mathf.RoundToInt(countdownSeconds / beatDur);
        _countdownStartBeat = Mathf.Max(0, _totalBeats - countdownNumbers * beatsPerNumber);
        _beatCount          = 0;
        _movementStarted    = false;

        Debug.Log($"[GameStarter] Intro: {countdownSeconds:F2} s = {_totalBeats} beats a {beatManager.Bpm} BPM. " +
                  $"Cuenta atrás desde beat {_countdownStartBeat}.");

        beatManager.OnBeat.AddListener(OnBeatTick);
        beatManager.StartMusic();

        // Esperar a que OnBeatTick active el movimiento
        while (!_movementStarted)
            yield return null;

        // Borrar texto 1 beat después del "¡YA!"
        yield return new WaitForSeconds(beatDur);
        SetPrompt("");
    }

    /// <summary>
    /// Se ejecuta en cada beat mientras esperamos el inicio.
    /// Muestra la cuenta atrás y activa el movimiento en el beat correcto.
    /// </summary>
    private void OnBeatTick()
    {
        if (_movementStarted) return;

        // ¿Ya estamos en la zona de cuenta atrás?
        if (_beatCount >= _countdownStartBeat)
        {
            int beatInCountdown = _beatCount - _countdownStartBeat;

            // Cambiar número al inicio de cada segmento
            if (beatInCountdown % beatsPerNumber == 0)
            {
                int n = countdownNumbers - beatInCountdown / beatsPerNumber;
                if (n > 0)
                {
                    SetPrompt(n.ToString());
                    Debug.Log($"[GameStarter] Beat {_beatCount} → \"{n}\"");
                }
            }

            // Beat final → "¡YA!" + arranque
            if (_beatCount >= _totalBeats)
            {
                SetPrompt("¡YA!");
                Debug.Log($"[GameStarter] Beat {_beatCount} → ¡YA! El jugador arranca.");

                playerMovement.StartMoving();

                if (rhythmInput   != null) rhythmInput.enabled   = true;
                if (energyManager != null) energyManager.enabled = true;

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;

                _movementStarted = true;
                beatManager.OnBeat.RemoveListener(OnBeatTick);
            }
        }

        _beatCount++;
    }

    // =========================================================================
    // MODOS PRESSSPACE / PRESSANYKEY
    // =========================================================================

    private IEnumerator LaunchImmediate()
    {
        if (_started) yield break;
        _started = true;

        if (beatManager == null || playerMovement == null)
        {
            Debug.LogError("[GameStarter] Faltan referencias.", this);
            yield break;
        }

        if (rhythmInput   != null) rhythmInput.enabled   = true;
        if (energyManager != null) energyManager.enabled = true;

        beatManager.StartMusic();
        playerMovement.ScheduleStart(beatManager.ScheduledStartDSP);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        SetPrompt("¡YA!");
        yield return new WaitForSeconds(0.5f);
        SetPrompt("");
    }

    // =========================================================================

    private void SetPrompt(string text)
    {
        if (promptText != null) promptText.text = text;
    }
}
