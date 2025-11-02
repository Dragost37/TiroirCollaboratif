using UnityEngine;

public class PlankAssembler : MonoBehaviour
{
    private DraggablePart _drag;

    [Header("Paramètres de snap")]
    public float snapDistance = 2f;
    public float snapAngle = 91f;

    private void Awake()
    {
        _drag = GetComponent<DraggablePart>();
    }

    private void OnEnable()
    {
        if (_drag != null)
            _drag.OnReleased += TryAssemble;
    }

    private void OnDisable()
    {
        if (_drag != null)
            _drag.OnReleased -= TryAssemble;
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

                foreach (var c in transform.GetComponentsInChildren<Collider>())
                    foreach (var other in transform.parent.GetComponentsInChildren<Collider>())
                        if (c != other)
                            Physics.IgnoreCollision(c, other, true);

                best.OnSnapped(gameObject);
                _drag.DisableDrag();
            }
        }
    }
}
