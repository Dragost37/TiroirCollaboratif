using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;





public class DowelGameManager : MonoBehaviour
{
    [Header("Dynamic Beat Map (TWEAK THESE!)")]
    [Tooltip("The normal sensitivity. Finds most peaks. (e.g., 1.3)")]
    public float normalSensitivity = 1.3f;

    [Tooltip("The 'hard' sensitivity. Finds *only* very loud peaks. (e.g., 1.8)")]
    public float hardSensitivity = 1.8f;

    [Tooltip("How many beats to wait after a peak before allowing another. (e.g., 0.25 = 16th note)")]
    public float debounceTimeInBeats = 0.25f;

    [Header("Audio")]
    [Tooltip("The audio source to play when the game starts.")]
    public AudioSource startAudioSource;

    [Header("Spawning Setup")]
    [Tooltip("The 'Shrinking Circle' (DowelTapTarget) prefab to spawn.")]
    public GameObject dowelTapTargetPrefab;

    [Tooltip("The UI RectTransform that defines the area where targets can spawn.")]
    public RectTransform spawnAreaCanvas;

    [Tooltip("How many beats *before* the target time should the circle appear?")]
    public float beatsInAdvance = 2.0f;

    [Header("Manual Timing Adjustment")]
    [Tooltip("Adjust spawn timing in beats. Positive -> spawn earlier, Negative -> spawn later.")]
    [Range(-2f, 2f)]
    public float manualSpawnOffsetBeats = 0f;

    [Header("Early Spawn Lead")]
    [Tooltip("Extra lead time in beats so targets appear earlier (gives players more time to react).")]
    [Range(0f, 4f)]
    public float extraSpawnLeadBeats = 0.5f;

    [Header("Hammer UI")]
    [Tooltip("Canvas or GameObject that contains hammer UI. Will be enabled/disabled when game starts/ends.")]
    public GameObject hammerCanvas;
    [Tooltip("If true, hammer canvas will be shown when the game starts and hidden when it ends.")]
    public bool showHammerCanvasOnPlay = true;


    private int beatMapIndex = 0;
    private bool gameIsActive = false;
    private float lastPeakTime = 0;



    private List<BeatMapEvent> beatMap;


    private class BeatMapEvent
    {
        public float beatToHit;
        public int spawnCount = 1;
    }






    public void StartHammerTime()
    {
        Debug.Log("HAMMER TIME... BEGINS!");

        if (startAudioSource != null)
        {
            startAudioSource.Play();
        }


        StartCoroutine(BeginHammerTime());
    }

    public void Start()
    {
        if (hammerCanvas != null)
        {
            hammerCanvas.SetActive(showHammerCanvasOnPlay);
        }
    }

    private IEnumerator BeginHammerTime()
    {

        if (startAudioSource != null)
        {

            yield return null;
        }


        if (RhythmConductor.Instance != null)
        {
            RhythmConductor.Instance.StartMusic();


            double conductorStartDsp = AudioSettings.dspTime;
            Debug.Log($"[DowelGameManager] Conductor started at dspTime={conductorStartDsp:F6}. Generating beatmap now...");



            yield return StartCoroutine(GenerateBeatMapCoroutine(conductorStartDsp));
        }
        else
        {
            Debug.LogWarning("DowelGameManager: RhythmConductor.Instance is null. Make sure a RhythmConductor exists in the scene.");

            yield return StartCoroutine(GenerateBeatMapCoroutine(AudioSettings.dspTime));
        }


        beatMapIndex = 0;
        gameIsActive = true;

        if (hammerCanvas != null)
        {
            hammerCanvas.SetActive(true);
        }


        RhythmConductor.OnBeat += OnBeat;


        yield return new WaitForSeconds(15f);


        yield return new WaitForSeconds(15f);

        EndHammerTime();
    }




    public void ReportHit(ScoreType score, DowelTapTarget target)
    {
        if (target != null)
        {
            if (score == ScoreType.Perfect)
            {
                StartCoroutine(FlashTargetColor(target, Color.cyan));
            }
            else
            {
                StartCoroutine(FlashTargetColor(target, Color.green));
            }
        }
    }




    public void ReportMiss(DowelTapTarget target)
    {
        if (target != null)
        {
            StartCoroutine(FlashTargetColor(target, Color.red));
        }
    }





    private void AddBeat(float beatToHit, int spawnCount)
    {

        if (beatToHit > lastPeakTime + debounceTimeInBeats)
        {
            beatMap.Add(new BeatMapEvent { beatToHit = beatToHit, spawnCount = spawnCount });
            lastPeakTime = beatToHit;
        }
    }





    private IEnumerator GenerateBeatMapCoroutine(double conductorStartDsp)
    {
        beatMap = new List<BeatMapEvent>();
        lastPeakTime = 0f;



        AudioClip clip = null;
        float beatDuration = 0f;

        if (RhythmConductor.Instance == null)
        {
            Debug.LogWarning("[DowelGameManager] RhythmConductor not found. Will try startAudioSource.clip as fallback.");
        }
        else
        {
            if (RhythmConductor.Instance.musicSource != null && RhythmConductor.Instance.musicSource.clip != null)
            {
                clip = RhythmConductor.Instance.musicSource.clip;
                beatDuration = RhythmConductor.Instance.beatDuration;
                Debug.Log($"[DowelGameManager] Using RhythmConductor.musicSource.clip ('{clip.name}') for analysis.");
            }
            else
            {
                Debug.LogWarning("[DowelGameManager] RhythmConductor.musicSource.clip is null.");
            }
        }


        if (clip == null && startAudioSource != null && startAudioSource.clip != null)
        {
            clip = startAudioSource.clip;
            Debug.Log($"[DowelGameManager] Falling back to startAudioSource.clip ('{clip.name}') for analysis.");

            if (RhythmConductor.Instance != null)
            {
                beatDuration = RhythmConductor.Instance.beatDuration;
            }
            else
            {
                beatDuration = 60f / 120f;
                Debug.LogWarning("[DowelGameManager] No conductor available to provide beatDuration. Defaulting to 120 BPM for analysis.");
            }
        }

        if (clip == null)
        {
            Debug.LogError("[DowelGameManager] No AudioClip available for analysis (conductor.clip and startAudioSource.clip both null). Cannot generate beat map.");
            yield break;
        }


        if (beatDuration <= 0f || float.IsNaN(beatDuration) || float.IsInfinity(beatDuration))
        {
            Debug.LogWarning("[DowelGameManager] Invalid beatDuration detected. Defaulting to 120 BPM (0.5s per beat).");
            beatDuration = 60f / 120f;
        }

        Debug.Log($"[DowelGameManager] Generating beatmap from clip '{clip.name}' (length {clip.length:F2}s) with beatDuration {beatDuration:F3}s");


        float currentDsp = (float)AudioSettings.dspTime;
        float beatOffsetBeats = (currentDsp - (float)conductorStartDsp) / beatDuration;

        if (conductorStartDsp <= 0.0)
        {
            beatOffsetBeats = 0f;
        }
        Debug.Log($"[DowelGameManager] beatOffsetBeats={beatOffsetBeats:F3} (conductorStartDsp={conductorStartDsp:F6}, currentDsp={currentDsp:F6})");


        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);



        int windowSize = 1024;
        int channels = clip.channels;


        int historySize = Mathf.Max(1, (clip.frequency / windowSize));
        float[] energyHistory = new float[historySize];

        int currentHistoryIndex = 0;


        int processedWindows = 0;
        for (int i = 0; i < samples.Length; i += windowSize * channels)
        {

            float currentWindowEnergy = 0f;
            int samplesInWindow = 0;
            for (int j = 0; j < windowSize * channels; j += channels)
            {
                if (i + j >= samples.Length) break;
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                {
                    sum += Mathf.Abs(samples[i + j + c]);
                }
                currentWindowEnergy += sum / channels;
                samplesInWindow++;
            }

            if (samplesInWindow > 0)
                currentWindowEnergy /= samplesInWindow;


            float historyAverage = 0f;
            for (int j = 0; j < historySize; j++)
            {
                historyAverage += energyHistory[j];
            }
            historyAverage /= historySize;


            float beatToHit = 0f;
            float timeInSeconds = (float)(i / channels) / (float)clip.frequency;
            float beatAtThisTime = timeInSeconds / beatDuration;


            beatToHit = beatAtThisTime + beatOffsetBeats;

            if (currentWindowEnergy > historyAverage * hardSensitivity)
            {
                AddBeat(beatToHit, 2);
            }
            else if (currentWindowEnergy > historyAverage * normalSensitivity)
            {
                AddBeat(beatToHit, 1);
            }



            energyHistory[currentHistoryIndex] = currentWindowEnergy;
            currentHistoryIndex = (currentHistoryIndex + 1) % historySize;

            processedWindows++;

            if ((processedWindows & 0x3F) == 0)
            {
                yield return null;
            }
        }

        Debug.Log($"Dynamic Beat Map Generated: {beatMap.Count} events. (Clip='{(clip != null ? clip.name : "null")}', beatDuration={beatDuration:F3}s, beatOffsetBeats={beatOffsetBeats:F3})");
        yield break;
    }





    private void OnBeat(int currentBeat)
    {
        if (!gameIsActive || beatMap == null || beatMapIndex >= beatMap.Count)
        {
            return;
        }

        float currentConductorBeat = RhythmConductor.Instance.GetCurrentBeat();


        float nextHitBeat = beatMap[beatMapIndex].beatToHit;





        float effectiveLeadBeats = beatsInAdvance + manualSpawnOffsetBeats + extraSpawnLeadBeats;
        while (currentConductorBeat >= (nextHitBeat - effectiveLeadBeats))
        {

            int count = beatMap[beatMapIndex].spawnCount;

            for (int i = 0; i < count; i++)
            {
                SpawnRandomTarget(nextHitBeat);
            }

            beatMapIndex++;

            nextHitBeat = beatMap[beatMapIndex].beatToHit;
        }
    }




    private void SpawnRandomTarget(float targetBeat)
    {
        if (dowelTapTargetPrefab == null || spawnAreaCanvas == null)
        {
            Debug.LogError("DowelGameManager is missing Prefab or Spawn Area!");
            return;
        }


        Rect spawnRect = spawnAreaCanvas.rect;
        float randX = Random.Range(spawnRect.xMin, spawnRect.xMax);
        float randY = Random.Range(spawnRect.yMin, spawnRect.yMax);
        Vector2 randomAnchoredPos = new Vector2(randX, randY);


        GameObject targetGO = Instantiate(dowelTapTargetPrefab, spawnAreaCanvas);



        targetGO.GetComponent<RectTransform>().anchoredPosition = randomAnchoredPos;


        DowelTapTarget tapTarget = targetGO.GetComponent<DowelTapTarget>();
        if (tapTarget != null)
        {

            float initLeadBeats = beatsInAdvance + manualSpawnOffsetBeats + extraSpawnLeadBeats;
            tapTarget.Initialize(this, targetBeat, initLeadBeats);
        }
        else
        {
            Debug.LogError("DowelTapTarget prefab is missing its script!", targetGO);
        }
    }


    private void EndHammerTime()
    {
        Debug.Log("Beat Map Complete! Hammer Time ending.");
        gameIsActive = false;

        if (hammerCanvas != null)
        {
            hammerCanvas.SetActive(false);
        }


        RhythmConductor.OnBeat -= OnBeat;



        RhythmConductor.Instance.StopMusic();



        DowelTapTarget[] remainingTargets = FindObjectsOfType<DowelTapTarget>(true);
        for (int i = 0; i < remainingTargets.Length; i++)
        {
            if (remainingTargets[i] != null)
            {
                Destroy(remainingTargets[i].gameObject);
            }
        }
    }

    private IEnumerator FlashTargetColor(DowelTapTarget target, Color feedbackColor)
    {
        if (target == null || target.shrinkingCircle == null)
        {
            yield break;
        }

        Color originalColor = target.shrinkingCircle.color;

        target.shrinkingCircle.color = feedbackColor;

        yield return new WaitForSeconds(0.15f);

        float fadeDuration = 0.2f;
        float timer = 0;
        while (timer < fadeDuration && target != null && target.shrinkingCircle != null)
        {
            timer += Time.deltaTime;
            target.shrinkingCircle.color = Color.Lerp(feedbackColor, originalColor, timer / fadeDuration);
            yield return null;
        }

        if (target != null && target.shrinkingCircle != null)
        {
            target.shrinkingCircle.color = originalColor;
        }
    }

    void OnDestroy()
    {

        RhythmConductor.OnBeat -= OnBeat;
    }
}


public enum ScoreType
{
    Miss,
    Good,
    Perfect
}
