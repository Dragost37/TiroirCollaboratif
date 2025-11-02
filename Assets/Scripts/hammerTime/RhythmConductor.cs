
using UnityEngine;
using System.Collections;






[RequireComponent(typeof(AudioSource))]
public class RhythmConductor : MonoBehaviour
{

    public static RhythmConductor Instance { get; private set; }

    [Header("Music & Timing")]
    [Tooltip("The Beats Per Minute (BPM) of the music track.")]
    public float bpm = 120.0f;

    [Tooltip("The AudioSource playing the music.")]
    public AudioSource musicSource;

    [Header("Beat State")]
    [Tooltip("The duration of a single beat in seconds.")]
    public float beatDuration;

    [Tooltip("The current position in the song, measured in beats.")]
    public float songPositionInBeats;






    public static event System.Action<int> OnBeat;




    private float dspSongStartTime;

    private int lastBeatFired = 0;

    void Awake()
    {

        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this;

        }
    }

    void Start()
    {
        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }
    }




    public void StartMusic()
    {

        beatDuration = 60f / bpm;



        dspSongStartTime = (float)AudioSettings.dspTime;


        musicSource.Play();
        lastBeatFired = 0;
    }

    public void StopMusic()
    {
        musicSource.Stop();
    }




    public void ChangeTrack(AudioClip newClip, float newBpm)
    {
        musicSource.Stop();
        musicSource.clip = newClip;
        bpm = newBpm;
        StartMusic();
    }

    void Update()
    {
        if (!musicSource.isPlaying)
        {
            return;
        }




        float songPositionInSeconds = (float)(AudioSettings.dspTime - dspSongStartTime);


        songPositionInBeats = songPositionInSeconds / beatDuration;




        if (songPositionInBeats > lastBeatFired + 1)
        {

            lastBeatFired++;


            OnBeat?.Invoke(lastBeatFired);
        }
    }





    public float GetCurrentBeat()
    {
        return songPositionInBeats;
    }
}
