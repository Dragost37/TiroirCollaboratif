using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class TorqueInputHandler : MonoBehaviour
{
    // --- (All your existing public variables are the same) ---
    [Header("Game Settings")]
    public float minTargetTorque = 80f;
    public float maxTargetTorque = 120f;
    public float torquePerDegree = 0.2f;
    public float torqueBuffer = 5f;

    [Header("Visuals")]
    public Image screwVisual;
    public Color baseColor = Color.white;
    public Color progressColor = new Color(1f, 0.8f, 0f);
    public Color successColor = Color.green;
    public Color failColor = Color.red;
    public float failFlashDuration = 0.5f;

    [Header("State (Read-Only)")]
    [SerializeField] public bool isFinished = false;
    [SerializeField] private float targetTorque; // Randomized on start

    private float currentTorque = 0f;
    private float maxTorqueLimit;
    private Vector2 lastPosition;
    private Vector2 centerPoint;
    private int currentPointerId = int.MinValue;
    private bool isPointerActive = false;
    private bool isResetting = false;

    public GameManager gameManager;

    // --- (Start() is the same) ---
    void Start()
    {
        // Randomize target torque within the specified range
        targetTorque = Random.Range(minTargetTorque, maxTargetTorque);
        maxTorqueLimit = targetTorque + torqueBuffer;
        centerPoint = RectTransformUtility.WorldToScreenPoint(null, screwVisual.transform.position);
        Debug.Log($"[{gameObject.name}] Target torque set to: {targetTorque:F1} (Range: {minTargetTorque}-{maxTargetTorque})");
        ResetPlayer();
    }

    // --- (These are the new public functions for the EventTrigger) ---

    public void HandlePointerDown(BaseEventData eventData)
    {
        // Ignore if finished, resetting, or already controlled
        if (isFinished || isResetting || isPointerActive) return;

        PointerEventData pointerData = eventData as PointerEventData;
        if (pointerData == null) return;

        isPointerActive = true;
        currentPointerId = pointerData.pointerId;
        lastPosition = pointerData.position;
        centerPoint = RectTransformUtility.WorldToScreenPoint(null, screwVisual.transform.position);
    }

    public void HandlePointerDrag(BaseEventData eventData)
    {
        if (isFinished || isResetting) return;

        PointerEventData pointerData = eventData as PointerEventData;
        // Only process drags from the touch we are tracking
        if (pointerData == null || pointerData.pointerId != currentPointerId) return;

        Vector2 currentPosition = pointerData.position;
        Vector2 vOld = lastPosition - centerPoint;
        Vector2 vNew = currentPosition - centerPoint;

        float angle = Vector2.SignedAngle(vOld, vNew);

        if (angle < -0.1f)
        {
            float angleMagnitude = Mathf.Abs(angle);
            float torqueToAdd = angleMagnitude * torquePerDegree;
            float newTorque = currentTorque + torqueToAdd;

            // Clamp to maxTorqueLimit
            if (newTorque > maxTorqueLimit)
            {
                torqueToAdd = maxTorqueLimit - currentTorque;
                float actualAngle = -(torqueToAdd / torquePerDegree);
                screwVisual.transform.Rotate(0, 0, actualAngle);
                currentTorque = maxTorqueLimit;
            }
            else
            {
                currentTorque = newTorque;
                screwVisual.transform.Rotate(0, 0, angle);
            }
        }

        CheckTorqueState();
        UpdateVisuals();
        Debug.Log($"[{gameObject.name}] Torque: {currentTorque:F1}/{targetTorque:F1} (Max: {maxTorqueLimit:F1})");

        lastPosition = currentPosition;
    }

    public void HandlePointerUp(BaseEventData eventData)
    {
        PointerEventData pointerData = eventData as PointerEventData;
        if (pointerData != null && pointerData.pointerId == currentPointerId)
        {
            isPointerActive = false;
            currentPointerId = int.MinValue;
        }
    }


    // --- (All other functions are the same as before) ---

    void CheckTorqueState()
    {
        if (currentTorque > maxTorqueLimit)
        {
            StartCoroutine(FailSequence());
        }
        else if (currentTorque >= targetTorque && currentTorque <= maxTorqueLimit && !isFinished)
        {
            isFinished = true;
            currentTorque = targetTorque;
            currentPointerId = int.MinValue;
            gameManager.PlayerFinished();
            screwVisual.color = successColor;
        }
    }

    void UpdateVisuals()
    {
        if (isFinished || isResetting) return;
        float progress = Mathf.Clamp01(currentTorque / targetTorque);
        screwVisual.color = Color.Lerp(baseColor, progressColor, progress);
    }

    private IEnumerator FailSequence()
    {
        isResetting = true;
        currentTorque = 0;
        isPointerActive = false;
        currentPointerId = int.MinValue;

        screwVisual.color = failColor;
        yield return new WaitForSeconds(failFlashDuration);

        Debug.Log($"[{gameObject.name}] FAILED! Exceeded torque limit. Restarting game...");

        // Restart the game through GameManager
        if (gameManager != null)
        {
            gameManager.RestartGame();
        }
    }

    public void ResetPlayer()
    {
        StopAllCoroutines();
        currentTorque = 0;
        isFinished = false;
        isResetting = false;
        isPointerActive = false;
        currentPointerId = int.MinValue;
        screwVisual.color = baseColor;
        screwVisual.transform.localRotation = Quaternion.identity;
        Debug.Log($"[{gameObject.name}] Player reset. Torque: 0");
    }
}
