using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class DraggablePart : MonoBehaviour
{
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
    private Collider _collider;

    public System.Action<DraggablePart> OnReleased; // vénement notifiant la fin du drag

    private void Awake()
    {
        _cam = Camera.main;
        _hadRb = TryGetComponent(out _rb);
        _collider = GetComponent<Collider>();

        if (_rb)
        {
            _rb.useGravity = false;
            _rb.isKinematic = true;
        }
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
    }

    private void OnDisable()
    {
        var mt = MultiTouchManager.Instance;
        if (mt != null)
        {
            mt.OnTouchBegan -= OnTouchBegan;
            mt.OnTouchMoved -= OnTouchMoved;
            mt.OnTouchEnded -= OnTouchEnded;
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
                _rbWasKinematic = _rb.isKinematic;
                _rbSavedConstraints = _rb.constraints;
                _rb.isKinematic = true;
                _rb.constraints = RigidbodyConstraints.None;
            }
        }
    }

    private void OnTouchMoved(MultiTouchManager.TouchEvt e)
    {
        if (!_dragging || e.fingerId != _fingerId || _cam == null) return;

        var ray = _cam.ScreenPointToRay(e.position);
        if (_dragPlane.Raycast(ray, out var t))
        {
            var worldUnderFinger = ray.GetPoint(t);
            transform.position = worldUnderFinger + _grabOffset;
        }
    }

    private void OnTouchEnded(MultiTouchManager.TouchEvt e)
    {
        if (!_dragging || e.fingerId != _fingerId) return;

        _dragging = false;
        _fingerId = -1;

        if (drawingTool) drawingTool.enabled = true;
        RestoreRigidbody();

        OnReleased?.Invoke(this); // 👈 notifie les scripts abonnés
    }

    private void RestoreRigidbody()
    {
        if (_hadRb && _rb != null)
        {
            _rb.isKinematic = _rbWasKinematic;
            _rb.constraints = _rbSavedConstraints;
        }
    }

    public void DisableDrag()
    {
        if (_collider) _collider.enabled = false;
        enabled = false;
    }
}
