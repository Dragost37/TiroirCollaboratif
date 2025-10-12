using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Throwable : MonoBehaviour
{
    [Header("Throw settings")]
    [Tooltip("Facteur d'amortissement de la vitesse (plus haut = ralentit plus vite).")]
    public float inertiaDamping = 2.5f;
    [Tooltip("Vitesse minimale (en m/s) requise pour déclencher un lancer.")]
    public float throwThreshold = 0.5f;
    [Tooltip("Délai max (en secondes) entre un mouvement et un relâchement pour être considéré comme un lancer.")]
    public float recentTripleTimeout = 0.25f;

    private Rigidbody _rb;
    private Camera _cam;

    // Touches qui ont commencé sur cet objet (fingerId)
    private readonly HashSet<int> _touchesOnObject = new();

    // Mémo de la dernière "triple" valide : vélocité + timestamp + fingerIds utilisés
    private Vector3 _recentTripleVelocity;
    private float _recentTripleTime;
    private bool _recentTriple;
    private List<int> _recentTripleFingerIds = new();

    // Indique si l'objet est actuellement en vol
    public bool IsThrown { get; set; }

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _cam = Camera.main;

        var mt = MultiTouchManager.Instance;
        if (mt != null)
        {
            mt.OnTouchBegan += OnTouchBegan;
            mt.OnTouchMoved += OnTouchMoved;
            mt.OnTouchEnded += OnTouchEnded;
        }
        else Debug.LogWarning("[Throwable] MultiTouchManager non trouvé.");
    }

    void OnDestroy()
    {
        var mt = MultiTouchManager.Instance;
        if (mt != null)
        {
            mt.OnTouchBegan -= OnTouchBegan;
            mt.OnTouchMoved -= OnTouchMoved;
            mt.OnTouchEnded -= OnTouchEnded;
        }
    }

    // MultiTouch events

    void OnTouchBegan(MultiTouchManager.TouchEvt e)
    {
        // Test raycast pour savoir si le touch a commencé sur ce GameObject
        if (_cam == null) return;

        var ray = _cam.ScreenPointToRay(e.position);
        if (Physics.Raycast(ray, out var hit) && hit.collider != null && hit.collider.gameObject == gameObject)
        {
            _touchesOnObject.Add(e.fingerId);
        }
    }

    void OnTouchMoved(MultiTouchManager.TouchEvt e)
    {
        // On ne calcule la vélocité que si au moins 3 touches actives ont commencé sur l'objet
        if (_cam == null) return;
        if (_touchesOnObject.Count < 3) return;

        // Récupère la liste des touches courantes qui appartiennent à _touchesOnObject
        var relevantTouches = new List<Touch>();
        foreach (var t in Input.touches)
        {
            if (_touchesOnObject.Contains(t.fingerId)) relevantTouches.Add(t);
        }

        if (relevantTouches.Count == 3)
        {
            // Moyenne des deltas écran (pixels)
            Vector2 avgDelta = Vector2.zero;
            foreach (var t in relevantTouches) avgDelta += t.deltaPosition;
            avgDelta /= relevantTouches.Count;

            // Convertir delta écran -> delta monde à la profondeur de l'objet
            float z = _cam.WorldToScreenPoint(transform.position).z;
            Vector3 worldAtOrigin = _cam.ScreenToWorldPoint(new Vector3(0f, 0f, z));
            Vector3 worldAtDelta = _cam.ScreenToWorldPoint(new Vector3(avgDelta.x, avgDelta.y, z));
            Vector3 worldDelta = worldAtDelta - worldAtOrigin;

            _recentTripleVelocity = worldDelta / Mathf.Max(0.0001f, Time.deltaTime);
            _recentTriple = true;
            _recentTripleTime = Time.time;

            // Stocke exactement les fingerIds utilisés pour cette estimation
            _recentTripleFingerIds.Clear();
            foreach (var t in relevantTouches) _recentTripleFingerIds.Add(t.fingerId);
        }
    }

    void OnTouchEnded(MultiTouchManager.TouchEvt e)
    {
        // Avant d'enlever la touche des sets, vérifie si le relâchement doit déclencher le lancer.
        // On ne lance que si :
        //  - on a récemment enregistré un _recentTriple valable,
        //  - le délai est dans la fenêtre recentTripleTimeout,
        //  - le fingerId qui s'est levé faisait partie des fingerIds qui ont produit la vélocité.
        if (_recentTriple && (Time.time - _recentTripleTime) <= recentTripleTimeout)
        {
            if (_recentTripleFingerIds.Contains(e.fingerId) && _recentTripleVelocity.magnitude > throwThreshold)
            {
                _rb.linearVelocity = _recentTripleVelocity;
                IsThrown = true;
                _recentTriple = false;
                _recentTripleFingerIds.Clear();
            }
        }

        // Enfin, retire la touche de la liste des touches qui ont commencé sur l'objet
        _touchesOnObject.Remove(e.fingerId);
    }

    void FixedUpdate()
    {
        // Simulation physique (inertie + amortissement)
        if (IsThrown)
        {
            _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * inertiaDamping);
            if (_rb.linearVelocity.magnitude < 0.01f)
            {
                _rb.linearVelocity = Vector3.zero;
                IsThrown = false;
            }
        }
    }
}
