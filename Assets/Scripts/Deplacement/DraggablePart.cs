using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DraggablePart : MonoBehaviour
{
    [Header("Snap")]
    public string compatibleSnapTag;
    public float snapDistance = 0.08f;
    public float snapAngle = 15f;

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
        if (Input.touchCount > 1) return; // on évite les conflits à 2 doigts

        var ray = _cam.ScreenPointToRay(e.position);
        if (Physics.Raycast(ray, out var hit) && hit.collider && hit.collider.gameObject == gameObject)
        {
            _dragging = true;
            _fingerId = e.fingerId;

            if (drawingTool) drawingTool.enabled = false;

            // point précis d'accroche
            var wp = hit.point;
            _grabOffset = transform.position - wp;

            // plan de drag parallèle à l'écran, passant par la pièce
            _dragPlane = new Plane(-_cam.transform.forward, transform.position);

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

        foreach (var sp in snaps)
        {
            if (!sp || sp.occupied) continue;
            if (!string.Equals(sp.snapTag, compatibleSnapTag)) continue;

            float d = Vector3.Distance(transform.position, sp.transform.position);
            if (d < bestDist)
            {
                best = sp;
                bestDist = d;
            }
        }

        if (best != null && bestDist <= snapDistance)
        {
            float ang = Quaternion.Angle(transform.rotation, best.transform.rotation);
            if (ang <= snapAngle)
            {
                transform.position = best.transform.position;
                transform.rotation = best.transform.rotation;
                best.OnSnapped(gameObject);
            }
        }
    }
}
