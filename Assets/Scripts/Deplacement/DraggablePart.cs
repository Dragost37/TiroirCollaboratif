using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DraggablePart : MonoBehaviour
{
    [Header("Snap")]
    public string compatibleSnapTag;
    public float snapDistance = 2f;
    public float snapAngle = 91f;

    [Header("Dessin")]
    public Behaviour drawingTool;

    private bool _dragging;
    private int _fingerId = -1;
    private Vector3 _grabOffset;
    private Camera _cam;

    // Projection sur un plan parallèle à l'écran (répare le "X seulement")
    private Plane _dragPlane;

    // Gestion Rigidbody (répare la physique qui fige Y)
    private Rigidbody _rb;
    private bool _hadRb;
    private bool _rbWasKinematic;
    private RigidbodyConstraints _rbSavedConstraints;

    private void Awake()
    {
        _cam = Camera.main;
        _hadRb = TryGetComponent(out _rb);
    }

    private void OnEnable()
    {
        var mt = MultiTouchManager.Instance;
        if (mt != null)
        {
            mt.OnTouchBegan += OnTouchBegan;
            mt.OnTouchMoved += OnTouchMoved;
            mt.OnTouchEnded += OnTouchEnded;
        }
        else
        {
            Debug.LogWarning("[DraggablePart] MultiTouchManager.Instance est null.");
        }
    }

    private void OnDisable()
    {
        var mt = MultiTouchManager.Instance;
        if (mt != null)
        {
            mt.OnTouchBegan -= OnTouchBegan;
            mt.OnTouchMoved  -= OnTouchMoved;
            mt.OnTouchEnded  -= OnTouchEnded;
        }

        if (drawingTool) drawingTool.enabled = true;
        RestoreRigidbody();
    }

    private void OnTouchBegan(MultiTouchManager.TouchEvt e)
    {
        if (_dragging || _cam == null) return;

        var ray = _cam.ScreenPointToRay(e.position);
        if (Physics.Raycast(ray, out var hit) && hit.collider && hit.collider.gameObject == gameObject)
        {
            _dragging = true;
            _fingerId = e.fingerId;

            if (drawingTool) drawingTool.enabled = false;

            // point précis d'accroche
            var wp = hit.point;
            _grabOffset = transform.position - wp;

            // plan de drag parallèle à l'écran, passant par le point de contact
            _dragPlane = new Plane(-_cam.transform.forward, hit.point);

            // neutraliser la physique pendant le drag
            if (_hadRb && _rb != null)
            {
                _rbWasKinematic      = _rb.isKinematic;
                _rbSavedConstraints  = _rb.constraints;
                _rb.isKinematic      = true;         // la physique ne retouche plus la position
                _rb.constraints      = RigidbodyConstraints.None;
            }
        }
    }

    private void OnTouchMoved(MultiTouchManager.TouchEvt e)
    {
        if (!_dragging || e.fingerId != _fingerId || _cam == null) return;

        // Ray écran -> intersection avec le plan de drag
        var ray = _cam.ScreenPointToRay(e.position);
        if (_dragPlane.Raycast(ray, out var t))
        {
            var worldUnderFinger = ray.GetPoint(t);
            var newPos = worldUnderFinger + _grabOffset;

            // NOTE : on ne verrouille plus Z ; le plan garantit un mouvement XY écran
            transform.position = newPos;
        }
    }

    private void OnTouchEnded(MultiTouchManager.TouchEvt e)
    {
        if (!_dragging || e.fingerId != _fingerId) return;

        _dragging = false;
        _fingerId = -1;

        if (drawingTool) drawingTool.enabled = true;
        RestoreRigidbody();

        TrySnap();
    }

    private void RestoreRigidbody()
    {
        if (_hadRb && _rb != null)
        {
            _rb.isKinematic = _rbWasKinematic;
            _rb.constraints = _rbSavedConstraints;
        }
    }

    private void TrySnap()
    {
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
            if (!string.Equals(sp.snapTag, compatibleSnapTag)) continue;

            // Distance mesurée entre la surface de la pièce et le SnapPoint
            Vector3 closestPoint = col.ClosestPoint(sp.transform.position);
            float d = Vector3.Distance(closestPoint, sp.transform.position);

            if (d < bestDist)
            {
                best = sp;
                bestDist = d;
            }
        }

        Debug.Log($"[DraggablePart] Meilleur snap trouvé : {(best != null ? best.name : "aucun")} à {bestDist} m. avec un angle de {Quaternion.Angle(transform.rotation, best != null ? best.transform.rotation : Quaternion.identity)}°.");

        if (best != null && bestDist <= snapDistance)
        {
            float ang = Quaternion.Angle(transform.rotation, best.transform.rotation);
            if (ang <= snapAngle)
            {
                // Coller la face la plus proche
                Vector3 closestPoint = col.ClosestPoint(best.transform.position);
                Vector3 offset = best.transform.position - closestPoint;
                transform.position += offset;

                // transform.rotation = best.transform.rotation;

                // rattacher à la structure
                transform.SetParent(best.transform.parent, true);
                if (_hadRb && _rb != null)
                {
                    _rb.isKinematic = true;
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                    _rb.constraints = RigidbodyConstraints.FreezeAll;
                }

                foreach (var c in transform.GetComponentsInChildren<Collider>())
                    foreach (var other in transform.parent.GetComponentsInChildren<Collider>())
                        if (c != other)
                            Physics.IgnoreCollision(c, other, true);

                Debug.Log($"[DraggablePart] Pièce '{name}' accrochée au SnapPoint '{best.name}'.");

                best.OnSnapped(gameObject);
            }
        }
    }

}
