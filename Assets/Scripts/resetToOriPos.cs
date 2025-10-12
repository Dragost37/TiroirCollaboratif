using UnityEngine;
using System.Collections;

public class resetToOriPos : MonoBehaviour
{
    [Header("Reset Settings")]
    [Tooltip("Time in seconds of inactivity before resetting rotation")]
    public float inactivityTimeout = 120f;

    [Tooltip("Duration of the reset animation in seconds")]
    public float resetDuration = 1.5f;

    [Tooltip("Use smooth interpolation for reset (true) or instant reset (false)")]
    public bool smoothReset = true;

    [Tooltip("Animation curve for smooth reset (only used if smoothReset is true)")]
    public AnimationCurve resetCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Quaternion originalRotation;

    private float lastActivityTime;
    private bool isResetting = false;

    private MultiFingerCircleRotate rotateScript;

    void Start()
    {
        originalRotation = transform.rotation;

        rotateScript = GetComponent<MultiFingerCircleRotate>();

        ResetActivityTimer();
    }

    void Update()
    {
                if (isResetting) return;

        if (Time.time - lastActivityTime >= inactivityTimeout)
        {
            StartCoroutine(ResetRotation());
        }
    }

    public void ResetActivityTimer()
    {
        lastActivityTime = Time.time;
    }

    private IEnumerator ResetRotation()
    {
        isResetting = true;

        if (smoothReset)
        {
            Quaternion startRotation = transform.rotation;
            float elapsed = 0f;

            while (elapsed < resetDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed
                float curveValue = resetCurve.Evaluate(normalizedTime);

                transform.rotation = Quaternion.Slerp(startRotation, originalRotation, curveValue);
                yield return null;
            }

            transform.rotation = originalRotation;
        }
        else
        {
            transform.rotation = originalRotation;
        }



        isResetting = false;
    }
}
