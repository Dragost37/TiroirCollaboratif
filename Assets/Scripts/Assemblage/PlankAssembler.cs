using UnityEngine;

public class PlankAssembler : MonoBehaviour
{
    private DraggablePart _drag;

    [Header("Param�tres de snap")]
    public float snapDistance = 2f;
    public float snapAngle = 91f;

    public GameManager gameManager;
    public GameObject ScrewGameCanva;
    [Header("UI")]
    public float hideCanvasDelay = 0.5f;

    private void Awake()
    {
        _drag = GetComponent<DraggablePart>();
        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager>();
    }

    private void OnEnable()
    {
        if (_drag != null)
            _drag.OnReleased += TryAssemble;
        if (gameManager != null)
            gameManager.OnGameEnded += HandleGameEnded;
    }

    private void OnDisable()
    {
        if (_drag != null)
            _drag.OnReleased -= TryAssemble;
        if (gameManager != null)
            gameManager.OnGameEnded -= HandleGameEnded;
    }

    private void TryAssemble(DraggablePart part)
    {
        if (part.gameObject.tag != "plank") return;

#if UNITY_2023_1_OR_NEWER
        var snaps = Object.FindObjectsByType<SnapPoint>(FindObjectsSortMode.None);
#else
        var snaps = Object.FindObjectsOfType<SnapPoint>();
#endif
        SnapPoint best = null;
        float bestDist = float.MaxValue;
        Collider col = GetComponent<Collider>();

        foreach (var sp in snaps)
        {
            if (!sp || sp.occupied) continue;
            if (!string.Equals(sp.snapTag, "plank")) continue;

            Vector3 closestPoint = col.ClosestPoint(sp.transform.position);
            float d = Vector3.Distance(closestPoint, sp.transform.position);
            if (d < bestDist)
            {
                best = sp;
                bestDist = d;
            }
        }

        if (best != null && bestDist <= snapDistance && best.transform.parent != transform)
        {
            float ang = Quaternion.Angle(transform.rotation, best.transform.rotation);
            Debug.Log($"[PlankAssembler] Meilleur snap trouv� � distance {bestDist:F2} et angle {ang:F2}");
            if (ang <= snapAngle)
            {
                Vector3 closestPoint = col.ClosestPoint(best.transform.position);
                Vector3 offset = best.transform.position - closestPoint;
                transform.position += offset;

                transform.SetParent(best.transform.parent, true);
                var rb = GetComponent<Rigidbody>();
                if (rb)
                {
                    rb.isKinematic = true;
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }

                RotationTrigger rotation = GetComponent<RotationTrigger>();
                if (rotation)
                    rotation.enabled = false;
                RotationTrigger parentRotation = transform.parent.GetComponent<RotationTrigger>();
                if (parentRotation)
                    parentRotation.enabled = false;

                foreach (var c in transform.GetComponentsInChildren<Collider>())
                    foreach (var other in transform.parent.GetComponentsInChildren<Collider>())
                        if (c != other)
                            Physics.IgnoreCollision(c, other, true);

                ScrewGameCanva.SetActive(true);
                gameManager.StartMinigame();

                best.OnSnapped(gameObject);
                _drag.DisableDrag();
            }
        }
    }

    private void HandleGameEnded()
    {
        // hide the canvas after a short delay
        StartCoroutine(HideCanvasAfterDelay());
    }

    private System.Collections.IEnumerator HideCanvasAfterDelay()
    {
        if (ScrewGameCanva == null)
            yield break;

        yield return new WaitForSeconds(hideCanvasDelay);
        if (ScrewGameCanva != null)
            ScrewGameCanva.SetActive(false);
    }
}
