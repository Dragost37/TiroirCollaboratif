using UnityEngine;

public class rotateHammer : MonoBehaviour
{
    [Tooltip("If assigned, this RectTransform will be rotated. Otherwise the object's own RectTransform / Transform will be used.")]
    public RectTransform targetRect;

    [Tooltip("Maximum swing angle on Z axis (degrees). The hammer will swing between -angle and +angle).")]
    public float angle = 40f;

    [Tooltip("Time in seconds for a single half-swing (from -angle to +angle). Full back-and-forth = duration * 2.")]
    public float duration = 0.25f;

    [Tooltip("If true the swing repeats continuously. If false the hammer does a single back-and-forth swing when Play() is called.")]
    public bool loop = true;

    [Tooltip("Automatically start swinging on Start() if true.")]
    public bool playOnStart = true;

    [Tooltip("If true, the animation uses unscaled time (ignores timeScale).")]
    public bool useUnscaledTime = false;

    [Tooltip("If true, the hammer rotation will be reset to zero when Stop() is called.")]
    public bool resetOnStop = true;

    private bool isPlaying = false;
    private Coroutine animCoroutine;

    void Start()
    {
        if (targetRect == null)
            targetRect = GetComponent<RectTransform>();


        SetRotationZ(0f);

        if (playOnStart)
            StartCoroutine(StartDelayedPlay());
    }

    public void Play()
    {
        if (isPlaying) return;
        Debug.Log($"rotateHammer: Play() called on '{gameObject.name}'");
        isPlaying = true;
        animCoroutine = StartCoroutine(SwingCoroutine());
    }

    private System.Collections.IEnumerator StartDelayedPlay()
    {

        yield return null;
        Play();
    }

    private System.Collections.IEnumerator SwingCoroutine()
    {
        if (duration <= 0f)
        {

            SetRotationZ(angle);
            yield break;
        }

        float elapsed = 0f;

        float totalTime = loop ? float.PositiveInfinity : duration * 2f;

        while (elapsed < totalTime && isPlaying)
        {
            float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            elapsed += delta;


            float t = Mathf.PingPong(elapsed, duration) / duration;


            float z = Mathf.Lerp(-angle, angle, t);

            SetRotationZ(z);

            yield return null;
        }


        if (!loop && isPlaying)
        {

            float finishElapsed = 0f;
            float finishDuration = 0.1f;
            float startZ = GetCurrentZ();
            while (finishElapsed < finishDuration)
            {
                float d = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                finishElapsed += d;
                float u = Mathf.Clamp01(finishElapsed / finishDuration);
                float z = Mathf.Lerp(startZ, 0f, Mathf.SmoothStep(0f, 1f, u));
                SetRotationZ(z);
                yield return null;
            }
            SetRotationZ(0f);
        }


        if (!loop)
        {
            isPlaying = false;
            animCoroutine = null;
        }
    }

    private void SetRotationZ(float z)
    {
        if (targetRect != null)
        {
            targetRect.localRotation = Quaternion.Euler(0f, 0f, z);
        }
        else
        {
            transform.localRotation = Quaternion.Euler(0f, 0f, z);
        }
    }

    private float GetCurrentZ()
    {
        if (targetRect != null)
            return targetRect.localEulerAngles.z > 180f ? targetRect.localEulerAngles.z - 360f : targetRect.localEulerAngles.z;
        else
        {
            float z = transform.localEulerAngles.z;
            return z > 180f ? z - 360f : z;
        }
    }
}
