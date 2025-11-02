using UnityEngine;

public class SnapPointCreator : MonoBehaviour
{
    private DraggablePart _drag;

    private void Awake()
    {
        _drag = GetComponent<DraggablePart>();
    }

    private void OnEnable()
    {
        if (_drag != null)
            _drag.OnReleased += TryCreateSnapPoint;
    }

    private void OnDisable()
    {
        if (_drag != null)
            _drag.OnReleased -= TryCreateSnapPoint;
    }

    private void TryCreateSnapPoint(DraggablePart part)
    {
        Debug.Log("[SnapPointCreator] Relachement d'une vis / tourillon");
        if (part.gameObject.tag != "screw" && part.gameObject.tag != "wood") return;

        Ray ray = new Ray(transform.position + Vector3.up * 0.5f, Vector3.down);
        if (Physics.Raycast(ray, out var hit, 1.0f) && hit.collider.CompareTag("plank"))
        {
            Transform parent = hit.collider.transform;

            GameObject snapGO = new GameObject("AutoSnapPoint");
            snapGO.transform.position = hit.point;
            snapGO.transform.rotation = Quaternion.LookRotation(hit.normal);
            snapGO.transform.localScale = Vector3.one;
            snapGO.transform.SetParent(parent, worldPositionStays: true);

            var newSnap = snapGO.AddComponent<SnapPoint>();
            newSnap.snapTag = "plank";
            newSnap.occupied = false;

            Debug.Log($"[SnapPointCreator] SnapPoint créé sur {hit.collider.name} à {hit.point}");

            transform.rotation = Quaternion.identity;
            transform.position = hit.point + Vector3.up * 0.2f;
            transform.SetParent(parent, true);

            var rb = GetComponent<Rigidbody>();
            if (rb)
            {
                rb.isKinematic = true;
                rb.constraints = RigidbodyConstraints.FreezeAll;
            }

            _drag.DisableDrag();
        }
    }
}
