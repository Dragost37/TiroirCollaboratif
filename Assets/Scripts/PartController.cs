using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PartController : MonoBehaviour
{
    [Header("Snap")]
    public string compatibleSnapTag;
    public float snapDistance = 0.08f; // en mètres
    public float snapAngle = 15f;      // en degrés

    private bool _dragging;
    private int _fingerId = -1;
    private Vector3 _grabOffset;
    private Camera _cam;

    // Verrouillage de la profondeur (Z)
    private float _zDistScreen;  // distance objet-caméra en espace écran
    private float _initialZ;     // Z monde à conserver

    void Start()
    {
        _cam = Camera.main;
        var mt = MultiTouchManager.Instance;
        if (mt != null)
        {
            mt.OnTouchBegan += B;
            mt.OnTouchMoved += M;
            mt.OnTouchEnded += E;
        }
        else
        {
            Debug.LogWarning("[PartController] MultiTouchManager.Instance est null.");
        }
    }

    void OnDestroy()
    {
        var mt = MultiTouchManager.Instance;
        if (mt != null)
        {
            mt.OnTouchBegan -= B;
            mt.OnTouchMoved -= M;
            mt.OnTouchEnded -= E;
        }
    }

    void B(MultiTouchManager.TouchEvt e)
    {
        if (_dragging || _cam == null) return;

        var ray = _cam.ScreenPointToRay(e.position);
        if (Physics.Raycast(ray, out var hit) && hit.collider != null && hit.collider.gameObject == gameObject)
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

    void M(MultiTouchManager.TouchEvt e)
    {
        if (!_dragging || e.fingerId != _fingerId || _cam == null) return;

        // Conversion position écran -> monde à Z constant
        var screen = new Vector3(e.position.x, e.position.y, _zDistScreen);
        var worldUnderFinger = _cam.ScreenToWorldPoint(screen);

        var newPos = worldUnderFinger + _grabOffset;

        // Verrouillage de la profondeur : on garde exactement le même Z monde
        newPos.z = _initialZ;

        transform.position = newPos;
    }

    void E(MultiTouchManager.TouchEvt e)
    {
        if (!_dragging || e.fingerId != _fingerId) return;

        _dragging = false;
        _fingerId = -1;

        TrySnap();
    }

    void TrySnap()
    {
        // Recherche directe des composants SnapPoint (pas besoin de Tag Unity)
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
                best.OnSnapped(this);
            }
        }
    }
}

public class SnapPoint : MonoBehaviour
{
    [Header("Identification")]
    public string snapTag;

    [Header("État")]
    public bool occupied;

    public void OnSnapped(PartController part)
    {
        occupied = true;

        // Feedback audio local (optionnel si un AudioSource est présent)
        var audio = GetComponent<AudioSource>();
        if (audio) audio.Play();

        // Notifier le gestionnaire d'assemblage si présent
        var asm = FindObjectOfType<AssemblyManager>();
        if (asm) asm.ValidateStep(part.gameObject);
    }
}
