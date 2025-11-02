


using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;


public class DowelTapTarget : MonoBehaviour, IPointerDownHandler
{
    [Header("Visuals")]
    [Tooltip("The Image component of the ring that will actually shrink.")]
    public Image shrinkingCircle;

    [Header("Scoring Window (in beats)")]
    public float perfectRange = 0.15f;
    public float goodRange = 0.3f;

    [Header("Animation")]
    public float startScale = 3.0f;
    public float endScale = 0.5f;

    [Header("Hammer")]
    [Tooltip("Prefab of the hammer object")]
    public GameObject hammerPrefab;
    [Tooltip("Transform where hammer will be spawned (optional). If null, will spawn at target's position.")]
    public Transform hammerSpawnPoint;
    [Tooltip("Starting rotation of the hammer (in degrees)")]
    public float startRotation = -45f;
    [Tooltip("Ending rotation of the hammer (in degrees)")]
    public float endRotation = 45f;
    [Tooltip("Duration of the hammer swing in seconds")]
    public float swingDuration = 0.5f;
    [Tooltip("If true, destroy hammer after animation finishes")]
    public bool destroyHammerAfter = true;


    private DowelGameManager gameManager;
    private float targetBeat;
    private float beatsInAdvance;
    private float spawnBeat;

    private bool bWasHit = false;




    public void Initialize(DowelGameManager manager, float targetBeat, float beatsInAdvance)
    {
        this.gameManager = manager;
        this.targetBeat = targetBeat;
        this.beatsInAdvance = beatsInAdvance;
        this.spawnBeat = targetBeat - beatsInAdvance;

        bWasHit = false;

        if (shrinkingCircle == null)
        {
            Debug.LogError("DowelTapTarget is missing its 'Shrinking Circle' Image reference!", this);
            return;
        }


        UpdateVisuals(RhythmConductor.Instance.GetCurrentBeat());
    }

    void Update()
    {
        if (bWasHit || shrinkingCircle == null) return;

        float currentBeat = RhythmConductor.Instance.GetCurrentBeat();


        UpdateVisuals(currentBeat);


        if (currentBeat > targetBeat + goodRange)
        {
            Miss();
        }
    }




    private void UpdateVisuals(float currentBeat)
    {


        float progress = Mathf.InverseLerp(spawnBeat, targetBeat, currentBeat);


        float currentScale = Mathf.Lerp(startScale, endScale, progress);



        shrinkingCircle.rectTransform.localScale = new Vector3(currentScale, currentScale, 1f);
    }




    public void OnPointerDown(PointerEventData eventData)
    {
        if (bWasHit) return;
        bWasHit = true;

        float currentBeat = RhythmConductor.Instance.GetCurrentBeat();
        float hitAccuracy = Mathf.Abs(currentBeat - targetBeat);

        if (hitAccuracy <= perfectRange)
        {
            gameManager.ReportHit(ScoreType.Perfect, this);
        }
        else if (hitAccuracy <= goodRange)
        {
            gameManager.ReportHit(ScoreType.Good, this);
        }
        else
        {
            Miss();
            return;
        }


        StartCoroutine(PlayHammerAndDestroy());
    }

    private IEnumerator PlayHammerAndDestroy()
    {
        GameObject hammerInstance = null;

        if (hammerPrefab != null)
        {

            Vector3 spawnPos = (hammerSpawnPoint != null) ? hammerSpawnPoint.position : this.transform.position;
            hammerInstance = Instantiate(hammerPrefab, spawnPos, Quaternion.Euler(0, 0, startRotation), this.transform.parent);


            float elapsedTime = 0f;
            while (elapsedTime < swingDuration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / swingDuration;
                float currentRotation = Mathf.Lerp(startRotation, endRotation, progress);
                hammerInstance.transform.rotation = Quaternion.Euler(0, 0, currentRotation);
                yield return null;
            }


            hammerInstance.transform.rotation = Quaternion.Euler(0, 0, endRotation);


            yield return new WaitForSeconds(0.1f);

            if (destroyHammerAfter && hammerInstance != null)
            {
                Destroy(hammerInstance);
            }
        }
        else
        {

            yield return new WaitForSeconds(0.1f);
        }

        Destroy(this.gameObject);
    }

    private void Miss()
    {
        if (bWasHit)
        {
            Destroy(this.gameObject);
            return;
        }
        bWasHit = true;
        gameManager.ReportMiss(this);
        Destroy(this.gameObject);
    }
}
