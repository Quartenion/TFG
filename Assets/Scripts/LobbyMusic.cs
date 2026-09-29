using System.Collections;
using UnityEngine;

/// <summary>
/// MÚSICA DE LA SECCIÓN INTERMEDIA (el "lobby" de freeroam antes de cada nivel, con la tienda, los contratos y la puerta).
///   • Suena la canción del lobby mientras el jugador está en freeroam (entra con un fade suave).
///   • Al abrir la puerta / empezar la secuencia del nivel (se acaba el freeroam) se desvanece y se para.
///   • PASOS del jugador AL RITMO de esa canción (en cada beat, o cada medio beat), solo si anda por el suelo,
///     programados con el reloj de audio (PlayScheduled) para ir clavados a la música.
///   • Los pasos suenan con REVERB (sala cerrada) y ECO (con el retardo a medio beat, para que también vaya a ritmo).
///
/// SETUP (uno por escena de nivel): Tools → Lobby → Crear música del lobby en la escena, o añade este componente a
/// cualquier objeto. Asigna la canción y su BPM. Los pasos, si los dejas vacíos, se cogen del FootstepAudio del jugador.
/// </summary>
[AddComponentMenu("Audio/Lobby Music")]
public class LobbyMusic : MonoBehaviour
{
    [Header("Música")]
    [Tooltip("Canción que suena en esta sección intermedia (freeroam).")]
    public AudioClip music;
    [Range(0f, 1f)] public float musicVolume = 0.6f;
    [Tooltip("BPM de la canción del lobby (para que los pasos vayan a su ritmo).")]
    public float bpm = 120f;
    [Tooltip("Segundos desde el inicio del clip hasta el primer beat (si la canción tiene silencio al principio).")]
    public float firstBeatOffset = 0f;
    [Tooltip("Segundos del fade de entrada / de salida de la música.")]
    public float fadeIn = 1.5f, fadeOut = 0.8f;

    [Header("Pasos")]
    [Tooltip("Clips de pasos. Vacío = los del FootstepAudio del jugador.")]
    public AudioClip[] footstepClips;
    [Range(0f, 1f)] public float footstepVolume = 0.7f;
    [Tooltip("Pasos por beat: 1 = un paso por beat, 2 = dos (a corcheas).")]
    [Range(1, 2)] public int stepsPerBeat = 1;
    [Tooltip("Variación aleatoria de tono para que no suenen robóticos.")]
    public float pitchVariance = 0.06f;
    [Tooltip("Velocidad mínima (m/s) para considerar que el jugador anda.")]
    public float minSpeed = 0.6f;
    [Tooltip("Ajuste fino (segundos) de cuándo suena cada paso respecto al beat. Negativo = antes, positivo = después.\n" +
             "Se puede tocar en Play para cuadrarlo de oído.")]
    [Range(-0.2f, 0.2f)] public float stepOffset = 0f;

    [Header("Sala cerrada: reverb + eco (solo en los pasos)")]
    public AudioReverbPreset reverbPreset = AudioReverbPreset.StoneCorridor;
    [Tooltip("Retardo del eco en beats (1 = el eco cae en el beat siguiente; 0.5 lo haría caer a contratiempo).")]
    public float echoDelayBeats = 1f;
    [Range(0f, 1f)] public float echoDecay = 0.35f;
    [Range(0f, 1f)] public float echoWetMix = 0.35f;

    private const float ScheduleLead = 0.12f;

    private AudioSource _music;
    private AudioSource[] _steps;
    private int _nextStep, _lastClip = -1;
    private PlayerMovement _pm;
    private CharacterController _cc;
    private double _musicStartDSP;
    private double _loopStartDSP = -1;          // instante DSP en el que empezó la vuelta actual de la canción
    private double _lastStepDSP  = double.MinValue;
    private bool _playing, _wasFreeroam;
    private Coroutine _fade;

    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();
        if (_pm != null) _cc = _pm.GetComponent<CharacterController>();
        if ((footstepClips == null || footstepClips.Length == 0) && _pm != null)
        {
            var fa = _pm.GetComponentInChildren<FootstepAudio>();
            if (fa != null) footstepClips = fa.Clips;
        }

        // Cada paso con 1.5 s de silencio al final: así la reverb y el eco tienen cola (si el clip acaba, Unity los corta).
        if (footstepClips != null)
        {
            var padded = new AudioClip[footstepClips.Length];
            for (int i = 0; i < footstepClips.Length; i++) padded[i] = PadWithSilence(footstepClips[i], 1.5f);
            footstepClips = padded;
        }

        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.priority = 10;

        // Varias fuentes de pasos (cada una en su hijo con su propia reverb + eco) para poder programarlos solapados.
        _steps = new AudioSource[3];
        float beat = 60f / Mathf.Max(1f, bpm);
        for (int i = 0; i < _steps.Length; i++)
        {
            var go = new GameObject("LobbyStep" + i);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            var rev = go.AddComponent<AudioReverbFilter>();
            rev.reverbPreset = reverbPreset;
            var echo = go.AddComponent<AudioEchoFilter>();
            echo.delay      = Mathf.Clamp(echoDelayBeats * beat * 1000f, 10f, 5000f);
            echo.decayRatio = echoDecay;
            echo.wetMix     = echoWetMix;
            echo.dryMix     = 1f;
            _steps[i] = src;
        }
    }

    private void Update()
    {
        bool freeroam = _pm != null && _pm.IsFreeroam;

        if (freeroam && !_playing) StartMusic();
        else if (!freeroam && _playing && _wasFreeroam) StopMusic();
        _wasFreeroam = freeroam;

        if (_playing && freeroam) ScheduleSteps();
    }

    // ── Música ────────────────────────────────────────────────────────────────

    private void StartMusic()
    {
        _playing = true;
        _lastStepDSP  = double.MinValue;
        _loopStartDSP = -1;
        if (music == null) return;   // (sin canción no hay reloj: tampoco pasos)
        _music.clip = music;
        _music.volume = 0f;
        _musicStartDSP = AudioSettings.dspTime + 0.1;
        _music.PlayScheduled(_musicStartDSP);
        if (_fade != null) StopCoroutine(_fade);
        _fade = StartCoroutine(FadeTo(musicVolume, fadeIn, false));
    }

    private void StopMusic()
    {
        _playing = false;
        if (_fade != null) StopCoroutine(_fade);
        _fade = StartCoroutine(FadeTo(0f, fadeOut, true));
        foreach (AudioSource s in _steps) if (s != null) s.Stop();   // (pasos ya programados: fuera)
    }

    private IEnumerator FadeTo(float target, float time, bool stopAtEnd)
    {
        float from = _music.volume, el = 0f;
        while (el < time)
        {
            el += Time.unscaledDeltaTime;
            _music.volume = Mathf.Lerp(from, target, Mathf.Clamp01(el / Mathf.Max(0.01f, time)));
            yield return null;
        }
        _music.volume = target;
        if (stopAtEnd) _music.Stop();
        _fade = null;
    }

    // ── Pasos a ritmo ─────────────────────────────────────────────────────────

    private void ScheduleSteps()
    {
        if (music == null || footstepClips == null || footstepClips.Length == 0) return;
        double now = AudioSettings.dspTime;
        if (now < _musicStartDSP) return;            // (aún no ha empezado a sonar)
        if (!_music.isPlaying) return;

        double step    = 60.0 / Mathf.Max(1f, bpm) / Mathf.Max(1, stepsPerBeat);
        double loopLen = (double)music.samples / music.frequency;

        // Dónde está REALMENTE la canción (muestra que suena ahora) → instante DSP en el que empezó esta vuelta.
        // Se corrige solo si se desvía (arranque tardío del clip, vuelta del bucle, tirones…): así los pasos
        // siguen a la música de verdad y no a un reloj teórico.
        double pos = (double)_music.timeSamples / music.frequency;
        double estLoopStart = now - pos;
        if (_loopStartDSP < 0 || System.Math.Abs(estLoopStart - _loopStartDSP) > 0.03)
            _loopStartDSP = estLoopStart;

        // Próximo beat (o medio beat) de esta vuelta; si ya no cabe, el primero de la siguiente.
        double inLoop = now - _loopStartDSP - firstBeatOffset;
        long idx = (long)System.Math.Floor(inLoop / step) + 1;
        double beatT = firstBeatOffset + idx * step;
        if (beatT >= loopLen - 0.001) beatT = loopLen + firstBeatOffset;
        double at = _loopStartDSP + beatT + stepOffset;

        if (at - now > ScheduleLead || at < now) return;
        if (at < _lastStepDSP + step * 0.5) return;   // (ese beat ya está programado)
        _lastStepDSP = at;

        if (!IsWalking()) return;   // (el beat pasa igual; simplemente no hay paso)

        AudioClip clip = PickClip();
        if (clip == null) return;
        AudioSource src = _steps[_nextStep];
        _nextStep = (_nextStep + 1) % _steps.Length;
        src.clip   = clip;
        src.volume = footstepVolume;
        src.pitch  = 1f + Random.Range(-pitchVariance, pitchVariance);
        src.PlayScheduled(at);
    }

    private bool IsWalking()
    {
        if (_pm == null || !_pm.enabled) return false;   // (tienda / contratos abiertos: quieto)
        if (!_pm.IsGroundedNow) return false;   // (más fiable que CharacterController.isGrounded, que parpadea)
        if (_cc == null) return true;
        Vector3 v = _cc.velocity; v.y = 0f;
        return v.magnitude >= minSpeed;
    }

    private static AudioClip PadWithSilence(AudioClip c, float seconds)
    {
        if (c == null) return null;
        try
        {
            if (c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
            int ch = c.channels, n = c.samples;
            var data = new float[n * ch];
            if (!c.GetData(data, 0)) return c;

            // Quitar el silencio del principio: el golpe del paso tiene que sonar JUSTO en el beat programado.
            float peak = 0f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            int first = 0;
            if (peak > 0f)
                for (int i = 0; i < data.Length; i++)
                    if (Mathf.Abs(data[i]) >= peak * 0.15f) { first = i / ch; break; }
            first = Mathf.Max(0, first - Mathf.RoundToInt(0.002f * c.frequency));   // (2 ms de margen para no cortar el ataque)
            int keep = n - first;

            int extra = Mathf.RoundToInt(seconds * c.frequency);
            var outData = new float[(keep + extra) * ch];
            System.Array.Copy(data, first * ch, outData, 0, keep * ch);
            var clip = AudioClip.Create(c.name + "_tail", keep + extra, ch, c.frequency, false);
            clip.SetData(outData, 0);
            return clip;
        }
        catch { return c; }   // (clip en streaming / comprimido: se usa tal cual)
    }

    private AudioClip PickClip()
    {
        int n = footstepClips.Length;
        for (int tries = 0; tries < 12; tries++)
        {
            int i = Random.Range(0, n);
            if (footstepClips[i] == null || (i == _lastClip && n > 1)) continue;
            _lastClip = i;
            return footstepClips[i];
        }
        foreach (AudioClip c in footstepClips) if (c != null) return c;
        return null;
    }
}
