using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PartDuplicator : MonoBehaviour
{
    public enum AxisFrame { ScreenXY, WorldXY }

    [Header("Source")]
    [Tooltip("Prefab à instancier. Laisser vide pour dupliquer ce GameObject.")]
    public GameObject prefab;

    [Header("Cadre d'axes")]
    public AxisFrame axisFrame = AxisFrame.ScreenXY;

    [Header("Déclenchement")]
    [Tooltip("Deux doigts doivent commencer sur CET objet.")]
    public int requiredFingers = 2;
    [Tooltip("Durée minimale (s) de maintien avant d'armer la duplication.")]
    public float holdTime = 0.05f;
    [Tooltip("Distance min (m) pour choisir l'axe après le hold.")]
    public float axisPickMinMove = 0.01f;

    [Header("Espacement & limites")]
    [Tooltip("Si > 0, force l'espacement. Si = 0, calcul automatique depuis la taille du modèle.")]
    public float spacingOverride = 0f;
    [Tooltip("Marge additionnelle anti chevauchement (m).")]
    public float separationMargin = 2f;
    [Tooltip("Nombre max de clones par trait.")]
    public int maxPerStroke = 5;
    [Tooltip("Délai minimal (s) entre deux spawns, anti-rafale.")]
    public float minSpawnInterval = 0.05f;

    [Header("Bulle de capture (sécurité multi-user)")]
    [Tooltip("Rayon = taille_boundingBox * facteur. Sert de rayon de base.")]
    public float captureRadiusFactor = 5f;
    [Tooltip("Active une bulle dynamique qui s'étire avec le geste.")]
    public bool useDynamicCapture = false;
    [Tooltip("Marge (m) ajoutée au rayon dynamique pour plus de tolérance.")]
    public float dynamicCaptureSlack = 0.05f;

    private Camera _cam;

    private class Finger { public int id; public Vector2 screenPos; public bool beganOnThis; }
    private readonly Dictionary<int, Finger> _fingers = new();
    private readonly List<int> _captured = new(2);

    // État duplication
    private bool _armed;
    private bool _duplicating;
    private float _downTime;

    // Plan & repères
    private Plane _plane;
    private Vector3 _startW;
    private Vector3 _originW;

    // Axe & crans
    private bool _axisChosen;
    private Vector3 _axisDir;   // X+/X-/Y+/Y-
    private float _step;        // espacement effectif (auto/override) + marge
    private int _nextIndex;     // cran attendu (1,2,...)
    private int _spawned;       // nb clones ce trait
    private float _lastSpawnTime;

    private DraggablePart _drag;

    // Capture bubble
    private Vector3 _captureCenterW;
    private float _captureRadiusW;

    private void Awake()
    {
        _cam = Camera.main;
        _drag = GetComponent<DraggablePart>();
    }

    private void OnEnable()
    {
        var mt = MultiTouchManager.Instance ?? FindObjectOfType<MultiTouchManager>();
        if (mt != null)
        {
            mt.OnTouchBegan += Began;
            mt.OnTouchMoved  += Moved;
            mt.OnTouchEnded  += Ended;
        }
        else Debug.LogWarning("[PartDuplicator] MultiTouchManager.Instance est null.");
    }

    private void OnDisable()
    {
        var mt = MultiTouchManager.Instance ?? FindObjectOfType<MultiTouchManager>();
        if (mt != null)
        {
            mt.OnTouchBegan -= Began;
            mt.OnTouchMoved  -= Moved;
            mt.OnTouchEnded  -= Ended;
        }
        if (_drag) _drag.enabled = true;
        ResetState();
    }

    private void Began(MultiTouchManager.TouchEvt e)
    {
        var f = new Finger { id = e.fingerId, screenPos = e.position, beganOnThis = IsOverThis(e.position) };
        _fingers[e.fingerId] = f;

        if (_captured.Count < requiredFingers && f.beganOnThis)
        {
            _captured.Add(e.fingerId);

            if (_captured.Count == requiredFingers)
            {
                _downTime = Time.time;
                _armed = false;

                _plane = (axisFrame == AxisFrame.ScreenXY)
                    ? new Plane(-_cam.transform.forward, transform.position)
                    : new Plane(Vector3.forward, new Vector3(transform.position.x, transform.position.y, transform.position.z));

                _startW  = ScreenToWorldOnPlane(GetCapturedCentroid(), _plane);
                _originW = transform.position;

                ComputeCaptureBubble(out _captureCenterW, out _captureRadiusW);

                if (_drag) _drag.enabled = false;

                _axisChosen = false;
                _nextIndex = 1;
                _spawned = 0;
                _lastSpawnTime = -999f;
                _duplicating = true;
            }
        }
    }

    private void Moved(MultiTouchManager.TouchEvt e)
    {
        if (_fingers.TryGetValue(e.fingerId, out var f))
            f.screenPos = e.position;

        if (!_duplicating) return;

        if (!CapturedStillValidInBubble()) { CancelDuplication(); return; }

        if (!_armed)
        {
            if (Time.time - _downTime >= holdTime) _armed = true;
            else return;
        }

        var currW = ScreenToWorldOnPlane(GetCapturedCentroid(), _plane);
        var delta = currW - _startW;
        var dist = delta.magnitude;

        if (!_axisChosen)
        {
            if (dist < axisPickMinMove) return;

            if (axisFrame == AxisFrame.ScreenXY)
            {
                var right = _cam.transform.right;
                var up    = _cam.transform.up;
                float dr = Mathf.Abs(Vector3.Dot(delta.normalized, right));
                float du = Mathf.Abs(Vector3.Dot(delta.normalized, up));
                _axisDir = (dr >= du) ? Mathf.Sign(Vector3.Dot(delta, right)) * right
                                      : Mathf.Sign(Vector3.Dot(delta, up))   * up;
            }
            else
            {
                var right = Vector3.right;
                var up    = Vector3.up;
                float dr = Mathf.Abs(Vector3.Dot(delta.normalized, right));
                float du = Mathf.Abs(Vector3.Dot(delta.normalized, up));
                _axisDir = (dr >= du) ? Mathf.Sign(Vector3.Dot(delta, right)) * right
                                      : Mathf.Sign(Vector3.Dot(delta, up))   * up;
            }

            _step = ComputeModelLengthAlong(_axisDir.normalized) + separationMargin;
            if (spacingOverride > 0f) _step = spacingOverride + separationMargin;
            _step = Mathf.Max(_step, 0.0001f);

            _axisChosen = true;
        }

        float signed = Vector3.Dot(currW - _originW, _axisDir.normalized);
        float targetDist = _nextIndex * _step;

        if (signed >= targetDist && _spawned < maxPerStroke && (Time.time - _lastSpawnTime) >= minSpawnInterval)
        {
            var pos = _originW + _axisDir.normalized * targetDist;
            SpawnCloneAt(pos, transform.rotation);

            _spawned++;
            _nextIndex++;
            _lastSpawnTime = Time.time;

            if (_spawned >= maxPerStroke)
            {
                FinishDuplication();
                return;
            }
        }
    }

    private void Ended(MultiTouchManager.TouchEvt e)
    {
        _fingers.Remove(e.fingerId);
        if (_captured.Contains(e.fingerId)) FinishDuplication();
    }

    // ---------- Helpers ----------

    private Vector2 GetCapturedCentroid()
    {
        if (_captured.Count == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero; int count = 0;
        foreach (var id in _captured)
        {
            if (_fingers.TryGetValue(id, out var f)) { sum += f.screenPos; count++; }
        }
        return count > 0 ? sum / count : Vector2.zero;
    }

    private bool IsOverThis(Vector2 screenPos)
    {
        if (_cam == null) return false;
        var ray = _cam.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out var hit) && hit.collider && hit.collider.gameObject == gameObject;
    }

    private Vector3 ScreenToWorldOnPlane(Vector2 screen, Plane plane)
    {
        var ray = _cam.ScreenPointToRay(screen);
        return plane.Raycast(ray, out var t) ? ray.GetPoint(t) : transform.position;
    }

    private void ComputeCaptureBubble(out Vector3 centerW, out float radiusW)
    {
        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0)
        {
            centerW = transform.position;
            radiusW = 0.1f;
            return;
        }
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        centerW = b.center;
        radiusW = b.extents.magnitude * captureRadiusFactor;
    }

    private bool CapturedStillValidInBubble()
    {
        float dynRadius = _captureRadiusW;
        if (useDynamicCapture && _axisChosen)
        {
            var currW = ScreenToWorldOnPlane(GetCapturedCentroid(), _plane);
            float along = Mathf.Abs(Vector3.Dot(currW - _originW, _axisDir.normalized));
            dynRadius = _captureRadiusW + along + dynamicCaptureSlack;
        }

        foreach (var id in _captured)
        {
            if (!_fingers.TryGetValue(id, out var f)) return false;
            if (IsOverThis(f.screenPos)) continue;

            var pw = ScreenToWorldOnPlane(f.screenPos, _plane);
            if (Vector3.Distance(pw, _captureCenterW) > dynRadius) return false;
        }
        return true;
    }

    private float ComputeModelLengthAlong(Vector3 dir)
    {
        dir = dir.normalized;
        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 0.05f;

        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        var ad = new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));
        return 2f * Vector3.Dot(ad, b.extents);
    }

    private void SpawnCloneAt(Vector3 pos, Quaternion rot)
    {
        var source = prefab ? prefab : gameObject;
        var parent = transform.parent;
        var clone = Instantiate(source, pos, rot, parent);
        clone.name = source.name.Replace("(Clone)", "").Trim() + " (Clone)";

        var audio = clone.GetComponent<AudioSource>();
        if (audio) audio.Play();
    }

    private void CancelDuplication() => FinishDuplication();

    private void FinishDuplication()
    {
        if (_drag) _drag.enabled = true;
        ResetState();
    }

    private void ResetState()
    {
        _captured.Clear();
        _armed = false;
        _duplicating = false;
        _axisChosen = false;
        _nextIndex = 1;
        _spawned = 0;
        _lastSpawnTime = -999f;
    }
}
