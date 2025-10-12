using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DraggablePart : MonoBehaviour
{
    [Header("Snap")]
    [Tooltip("Doit correspondre au SnapPoint.snapTag")]
    public string compatibleSnapTag;
    [Tooltip("Distance max pour autoriser l'accroche (mètres)")]
    public float snapDistance = 0.08f;
    [Tooltip("Angle max (degrés) entre la rotation de la pièce et du point d'accroche")]
    public float snapAngle = 15f;

    private bool _dragging;
    private int _fingerId = -1;
    private Vector3 _grabOffset;
    private Camera _cam;

    // Verrouillage de la profondeur (Z)
    private float _zDistScreen;  // distance objet-caméra en espace écran
    private float _initialZ;     // Z monde à conserver

    private void Awake()
    {
        _cam = Camera.main;
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
            mt.OnTouchMoved -= OnTouchMoved;
            mt.OnTouchEnded -= OnTouchEnded;
        }
    }

    private void OnTouchBegan(MultiTouchManager.TouchEvt e)
    {
        // 👉 Ne pas commencer de drag si plusieurs doigts sont posés
        if (_dragging || _cam == null) return;
        if (Input.touchCount > 1) return;  // <--- sécurité multitouch

        var ray = _cam.ScreenPointToRay(e.position);
        if (Physics.Raycast(ray, out var hit) && hit.collider && hit.collider.gameObject == gameObject)
        {
            _dragging = true;
            _fingerId = e.fingerId;

            // Point d’accroche précis sur le mesh
            var wp = hit.point;
            _grabOffset = transform.position - wp;

            // Conserver la profondeur (Z) et mémoriser la distance écran
            _initialZ = transform.position.z;
            _zDistScreen = _cam.WorldToScreenPoint(transform.position).z;
        }
    }

    private void OnTouchMoved(MultiTouchManager.TouchEvt e)
    {
        if (!_dragging || e.fingerId != _fingerId || _cam == null) return;

        // Conversion position écran -> monde à Z constant
        var screen = new Vector3(e.position.x, e.position.y, _zDistScreen);
        var worldUnderFinger = _cam.ScreenToWorldPoint(screen);

        var newPos = worldUnderFinger + _grabOffset;
        newPos.z = _initialZ; // verrouillage profondeur
        transform.position = newPos;
    }

    private void OnTouchEnded(MultiTouchManager.TouchEvt e)
    {
        if (!_dragging || e.fingerId != _fingerId) return;

        _dragging = false;
        _fingerId = -1;

        TrySnap();
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
