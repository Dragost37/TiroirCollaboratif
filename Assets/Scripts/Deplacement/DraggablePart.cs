using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DraggablePart : MonoBehaviour
{
    [Header("Snap")]
    public string compatibleSnapTag;
    public float snapDistance = 0.08f;
    public float snapAngle    = 15f;

    [Header("Dessin")]
    public Behaviour drawingTool;

    [Header("Drag")]
    [Tooltip("Délai (en secondes) à maintenir avant que le drag ne démarre.")]
    public float activationDelay = 0.10f;

    private bool _dragging;
    private int _fingerId = -1;
    private Vector3 _grabOffset;
    private Camera _cam;

    // Plan de glisse (parallèle à l'écran)
    private Plane _dragPlane;

    // Gestion Rigidbody pendant le drag
    private Rigidbody _rb;
    private bool _hadRb;
    private bool _rbWasKinematic;
    private RigidbodyConstraints _rbSavedConstraints;

    // Armement (délai avant démarrage)
    private bool   _arming;
    private int    _armingFingerId = -1;
    private float  _armingSince;
    private Vector3 _armingGrabPoint; // point d'accroche au moment du touch began

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
            mt.OnTouchMoved  += OnTouchMoved;
            mt.OnTouchEnded  += OnTouchEnded;
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

        // Reset armement
        _arming = false;
        _armingFingerId = -1;
    }

    private void OnTouchBegan(MultiTouchManager.TouchEvt e)
    {
        if (_cam == null) return;
        if (_dragging) return; // cet objet ne gère qu’un drag à la fois

        var ray = _cam.ScreenPointToRay(e.position);
        if (Physics.Raycast(ray, out var hit) && hit.collider && hit.collider.gameObject == gameObject)
        {
            // Démarrage ARMÉ (délai) – on ne bouge pas encore l’objet
            _arming = true;
            _armingFingerId = e.fingerId;
            _armingSince = Time.time;
            _armingGrabPoint = hit.point; // point précis d’accroche initial
        }
    }

    private void OnTouchMoved(MultiTouchManager.TouchEvt e)
    {
        // Si on est en armement, vérifier si le délai est écoulé pour démarrer le drag
        if (!_dragging && _arming && e.fingerId == _armingFingerId)
        {
            if (Time.time - _armingSince >= activationDelay)
            {
                StartDrag(e, _armingGrabPoint);
            }
        }

        if (!_dragging || e.fingerId != _fingerId || _cam == null) return;

        var ray = _cam.ScreenPointToRay(e.position);
        if (_dragPlane.Raycast(ray, out var t))
        {
            var worldUnderFinger = ray.GetPoint(t);
            var newPos = worldUnderFinger + _grabOffset;
            transform.position = newPos; // mouvement libre sur le plan (XY écran)
        }
    }

    private void OnTouchEnded(MultiTouchManager.TouchEvt e)
    {
        // Annuler l’armement si on n’a pas encore démarré
        if (_arming && e.fingerId == _armingFingerId)
        {
            _arming = false;
            _armingFingerId = -1;
        }

        if (!_dragging || e.fingerId != _fingerId) return;

        _dragging = false;
        _fingerId = -1;

        if (drawingTool) drawingTool.enabled = true;
        RestoreRigidbody();

        TrySnap();
    }

    private void StartDrag(MultiTouchManager.TouchEvt e, Vector3 grabPointWorld)
    {
        _arming = false;
        _armingFingerId = -1;

        _dragging = true;
        _fingerId = e.fingerId;

        if (drawingTool) drawingTool.enabled = false;

        // offset depuis le point d’accroche initial
        _grabOffset = transform.position - grabPointWorld;

        // plan de drag parallèle à l'écran, passant par la pièce
        _dragPlane = new Plane(-_cam.transform.forward, transform.position);

        // neutraliser la physique pendant le drag
        if (_hadRb && _rb != null)
        {
            _rbWasKinematic     = _rb.isKinematic;
            _rbSavedConstraints = _rb.constraints;
            _rb.isKinematic     = true;
            _rb.constraints     = RigidbodyConstraints.None;
        }
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
