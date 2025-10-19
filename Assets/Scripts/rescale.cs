using UnityEngine;
using System.Collections.Generic;

public class rescale : MonoBehaviour
{
    [Header("Scale Settings")]
    [Tooltip("Minimum scale of the object")]
    public float minScale = 0.1f;

    [Tooltip("Maximum scale of the object")]
    public float maxScale = 10f;

    [Tooltip("How smooth the scaling is (higher = smoother)")]
    [Range(1f, 20f)]
    public float scaleSmoothness = 10f;

    [Header("Finger Requirements")]
    [Tooltip("Scale threshold to require 2 fingers (thumb + index)")]
    public float twoFingerThreshold = 1.0f;

    [Tooltip("Scale threshold to require 3 fingers")]
    public float threeFingerThreshold = 2.0f;

    [Tooltip("Scale threshold to require 4 fingers")]
    public float fourFingerThreshold = 4.0f;

    [Tooltip("Scale threshold to require 5 fingers")]
    public float fiveFingerThreshold = 7.0f;

    [Header("Pinch Detection")]
    [Tooltip("Distance threshold to detect a pinch")]
    public float pinchThreshold = 0.03f;

    [Tooltip("Sensitivity of scale changes")]
    public float scaleSpeed = 2.0f;

    [Header("Hand Tracking (Assign in Inspector)")]
    [Tooltip("Transform for thumb tip")]
    public Transform thumbTip;

    [Tooltip("Transform for index finger tip")]
    public Transform indexTip;

    [Tooltip("Transform for middle finger tip")]
    public Transform middleTip;

    [Tooltip("Transform for ring finger tip")]
    public Transform ringTip;

    [Tooltip("Transform for pinky finger tip")]
    public Transform pinkyTip;

    [Header("Debug")]
    public bool showDebugInfo = true;

    // Private variables
    private Vector3 targetScale;
    private float initialPinchDistance;
    private bool isPinching = false;
    private int activePinchCount = 0;
    private int requiredFingers = 1;

    // Finger data structure
    private class FingerPair
    {
        public Transform fingerTip;
        public bool isPinching;
        public float pinchDistance;
    }

    private List<FingerPair> fingers = new List<FingerPair>();

    void Start()
    {
        targetScale = transform.localScale;
        InitializeFingers();
    }

    void InitializeFingers()
    {
        // Initialize finger tracking (index is always included, others are optional)
        if (indexTip != null)
        {
            fingers.Add(new FingerPair { fingerTip = indexTip });
        }

        if (middleTip != null)
        {
            fingers.Add(new FingerPair { fingerTip = middleTip });
        }

        if (ringTip != null)
        {
            fingers.Add(new FingerPair { fingerTip = ringTip });
        }

        if (pinkyTip != null)
        {
            fingers.Add(new FingerPair { fingerTip = pinkyTip });
        }
    }

    void Update()
    {
        if (thumbTip == null || fingers.Count == 0)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning("Hand tracking transforms not assigned! Please assign thumb and at least index finger in the inspector.");
            }
            return;
        }

        // Determine how many fingers are required based on current scale
        UpdateRequiredFingers();

        // Detect pinches for each finger
        DetectPinches();

        // Calculate scale change if enough fingers are pinching
        if (activePinchCount >= requiredFingers)
        {
            CalculateScale();
        }

        // Smoothly interpolate to target scale
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * scaleSmoothness);

        // Debug information
        if (showDebugInfo)
        {
            DisplayDebugInfo();
        }
    }

    void UpdateRequiredFingers()
    {
        float currentScale = transform.localScale.x; // Assuming uniform scale

        if (currentScale >= fiveFingerThreshold)
        {
            requiredFingers = 5;
        }
        else if (currentScale >= fourFingerThreshold)
        {
            requiredFingers = 4;
        }
        else if (currentScale >= threeFingerThreshold)
        {
            requiredFingers = 3;
        }
        else if (currentScale >= twoFingerThreshold)
        {
            requiredFingers = 2;
        }
        else
        {
            requiredFingers = 1;
        }
    }

    void DetectPinches()
    {
        activePinchCount = 0;
        float totalPinchDistance = 0f;
        int validPinches = 0;

        foreach (var finger in fingers)
        {
            if (finger.fingerTip == null) continue;

            // Calculate distance between thumb and this finger
            finger.pinchDistance = Vector3.Distance(thumbTip.position, finger.fingerTip.position);

            // Check if this finger is pinching
            if (finger.pinchDistance < pinchThreshold)
            {
                finger.isPinching = true;
                activePinchCount++;
                totalPinchDistance += finger.pinchDistance;
                validPinches++;
            }
            else
            {
                finger.isPinching = false;
            }
        }

        // Store the average pinch distance for scaling calculations
        if (validPinches > 0 && activePinchCount >= requiredFingers)
        {
            if (!isPinching)
            {
                isPinching = true;
                initialPinchDistance = totalPinchDistance / validPinches;
            }
        }
        else
        {
            isPinching = false;
        }
    }

    void CalculateScale()
    {
        if (!isPinching) return;

        // Calculate average distance of active pinches
        float totalDistance = 0f;
        int count = 0;

        foreach (var finger in fingers)
        {
            if (finger.isPinching)
            {
                totalDistance += finger.pinchDistance;
                count++;
            }
        }

        if (count == 0) return;

        float averageDistance = totalDistance / count;

        // Calculate scale change based on pinch distance change
        float distanceRatio = averageDistance / initialPinchDistance;
        float scaleChange = (distanceRatio - 1f) * scaleSpeed * Time.deltaTime;

        // Apply scale change
        Vector3 newScale = targetScale + Vector3.one * scaleChange;

        // Clamp scale
        float clampedScale = Mathf.Clamp(newScale.x, minScale, maxScale);
        targetScale = Vector3.one * clampedScale;

        // Update initial distance for next frame
        initialPinchDistance = averageDistance;
    }

    void DisplayDebugInfo()
    {
        string debugText = $"Current Scale: {transform.localScale.x:F2}\n";
        debugText += $"Required Fingers: {requiredFingers}\n";
        debugText += $"Active Pinches: {activePinchCount}\n";
        debugText += $"Pinching: {isPinching}\n";

        for (int i = 0; i < fingers.Count; i++)
        {
            string fingerName = GetFingerName(i);
            if (fingers[i].fingerTip != null)
            {
                debugText += $"{fingerName}: {(fingers[i].isPinching ? "PINCH" : "Open")} ({fingers[i].pinchDistance:F3}m)\n";
            }
        }

        // Only log occasionally to avoid spam
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log(debugText);
        }
    }

    string GetFingerName(int index)
    {
        string[] names = { "Index", "Middle", "Ring", "Pinky" };
        return index < names.Length ? names[index] : "Unknown";
    }

    // Visual debug helpers
    void OnDrawGizmos()
    {
        if (!showDebugInfo || thumbTip == null) return;

        // Draw lines from thumb to each finger
        Gizmos.color = Color.yellow;

        if (indexTip != null)
        {
            Gizmos.color = Vector3.Distance(thumbTip.position, indexTip.position) < pinchThreshold ? Color.green : Color.red;
            Gizmos.DrawLine(thumbTip.position, indexTip.position);
        }

        if (middleTip != null)
        {
            Gizmos.color = Vector3.Distance(thumbTip.position, middleTip.position) < pinchThreshold ? Color.green : Color.red;
            Gizmos.DrawLine(thumbTip.position, middleTip.position);
        }

        if (ringTip != null)
        {
            Gizmos.color = Vector3.Distance(thumbTip.position, ringTip.position) < pinchThreshold ? Color.green : Color.red;
            Gizmos.DrawLine(thumbTip.position, ringTip.position);
        }

        if (pinkyTip != null)
        {
            Gizmos.color = Vector3.Distance(thumbTip.position, pinkyTip.position) < pinchThreshold ? Color.green : Color.red;
            Gizmos.DrawLine(thumbTip.position, pinkyTip.position);
        }
    }
}
