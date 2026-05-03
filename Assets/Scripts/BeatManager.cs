using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

public class BeatManager : MonoBehaviour
{
    [SerializeField] private float _bpm;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private Intervals[] _intervals;

    public float Bpm => _bpm;
    /// <summary>Duracion en segundos de un beat (1 negra) basado en el BPM.</summary>
    public float BeatDuration => _bpm > 0f ? 60f / _bpm : 0f;
    /// <summary>Tiempo actual de la cancion en segundos.</summary>
    public float SongTime => _audioSource != null ? _audioSource.time : 0f;

    private void Update()
    {
        foreach (Intervals interval in _intervals)
        {
            float sampledTime = (_audioSource.timeSamples / (_audioSource.clip.frequency * interval.GetIntervalLength(_bpm)));
            interval.CheckForNewInterval(sampledTime);
        }
    }

    /// <summary>
    /// Devuelve los segundos absolutos al beat mas cercano (anterior o siguiente).
    /// Util para detectar si un clic cae dentro de una ventana de timing.
    /// </summary>
    public float GetSecondsToNearestBeat()
    {
        if (_audioSource == null || _bpm <= 0f) return float.MaxValue;
        float beatLen = BeatDuration;
        float t = SongTime;
        float modT = t % beatLen;
        return Mathf.Min(modT, beatLen - modT);
    }
}

[System.Serializable]
public class Intervals
{
    [SerializeField] private float _steps;
    [SerializeField] private UnityEvent _trigger;
    private int _lastInterval;

    public float GetIntervalLength(float bpm)
    {
        return 60f / (bpm * _steps);
    }

    public void CheckForNewInterval (float interval)
    {
        if (Mathf.FloorToInt(interval) != _lastInterval) {
            _lastInterval = Mathf.FloorToInt(interval);
            _trigger.Invoke();
        }
    }
}