using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Throwable))]
public class Attractor : MonoBehaviour
{
    [Header("Attraction settings")]
    [Tooltip("Force d’attraction appliquée à l’objet (accélération en m/s²).")]
    public float attractForce = 100f;
    [Tooltip("Distance maximale (en mètres) à laquelle l'attraction agit.")]
    public float attractRadius = 999f;

    private Camera _cam;
    private Rigidbody _rb;
    private Throwable _throwable;
    private bool _attracting;
    private Vector3 _attractTarget;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _throwable = GetComponent<Throwable>();
        _cam = Camera.main;

        var mt = MultiTouchManager.Instance;
        if (mt)
        {
            mt.OnTouchMoved += OnTouchMoved;
        }
    }

    void OnDestroy()
    {
        var mt = MultiTouchManager.Instance;
        if (mt)
        {
            mt.OnTouchMoved -= OnTouchMoved;
        }
    }

    void OnTouchMoved(MultiTouchManager.TouchEvt e)
    {
        // Activation de l'attraction SEULEMENT si l'objet a été lancé
        if (!_throwable.IsThrown) return;

        if (Input.touchCount == 3 && _cam)
        {
            Touch[] touches = Input.touches;

            // Vérifie que les trois doigts sont proches les uns des autres
            float maxDistance = 150f;
            float d01 = Vector2.Distance(touches[0].position, touches[1].position);
            float d02 = Vector2.Distance(touches[0].position, touches[2].position);
            float d12 = Vector2.Distance(touches[1].position, touches[2].position);

            float avgDist = (d01 + d02 + d12) / 3f;

            if (avgDist > maxDistance)
            {
                _attracting = false;
                return;
            }

            Vector2 avg = (touches[0].position + touches[1].position + touches[2].position) / 3f;

            float z = _cam.WorldToScreenPoint(transform.position).z;
            Vector3 screen = new Vector3(avg.x, avg.y, z);
            _attractTarget = _cam.ScreenToWorldPoint(screen);

            _attracting = true;
        }
        else
        {
            _attracting = false;
        }
    }

    // Simulation physique (attraction)
    void FixedUpdate()
    {
        if (_attracting && _throwable.IsThrown)
        {
            Vector3 dir = _attractTarget - transform.position;
            float dist = dir.magnitude;
            if (dist < attractRadius)
            {
                _rb.AddForce(dir.normalized * attractForce, ForceMode.Acceleration);
            }

            if (dist < 0.05f)
            {
                _rb.linearVelocity = Vector3.zero;
                _attracting = false;
                _throwable.IsThrown = false; // stop l’état de vol
            }
        }
    }
}
