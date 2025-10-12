using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PartDuplicator : MonoBehaviour
{
    public enum AxisFrame { ScreenXY, WorldXY }

    [Header("Source")]
    [Tooltip("Prefab à instancier. Laisser vide pour dupliquer CE GameObject.")]
    public GameObject prefab;
    [Tooltip("Si coché, la source utilisée sera automatiquement cet objet au moment de la sélection.")]
    public bool autoUseSelfAsSourceOnSelect = true;

    [Header("Cadre d'axes")]
    public AxisFrame axisFrame = AxisFrame.ScreenXY;

    [Header("Déclenchement")]
    public int   requiredFingers   = 2;
    public float holdTime          = 0.05f;
    public float axisPickMinMove   = 0.01f;

    [Header("Espacement & limites")]
    public float spacingOverride   = 0f;
    public float separationMargin  = 1f;
    public int   maxPerStroke      = 5;
    public float minSpawnInterval  = 0.05f;

    [Header("Bulle de capture (sécurité multi-user)")]
    public float captureRadiusFactor = 5f;
    public bool  useDynamicCapture   = true;
    public float dynamicCaptureSlack = 0.05f;

    [Header("Intégration dessin")]
    [Tooltip("Composant de dessin à désactiver pendant la duplication (ex: LineDrawer, Painter, etc.).")]
    public Behaviour drawingTool;

    private Camera _cam;

    // ---- Ownership global des doigts (un doigt -> un duplicateur) ----
    private static readonly Dictionary<int, PartDuplicator> s_FingerOwners = new();

    private static bool TryClaimFinger(int fingerId, PartDuplicator owner)
    {
        if (s_FingerOwners.TryGetValue(fingerId, out var current))
            return current == owner; // déjà à moi -> OK
        s_FingerOwners[fingerId] = owner;
        return true;
    }

    private static void ReleaseFinger(int fingerId, PartDuplicator owner)
    {
        if (s_FingerOwners.TryGetValue(fingerId, out var current) && current == owner)
            s_FingerOwners.Remove(fingerId);
    }

    private static void ReleaseAllFor(PartDuplicator owner)
    {
        var toFree = new List<int>();
        foreach (var kv in s_FingerOwners)
            if (kv.Value == owner) toFree.Add(kv.Key);
        foreach (var id in toFree) s_FingerOwners.Remove(id);
    }

    // ----- Données instance -----
    private class Finger { public int id; public Vector2 screenPos; }
    private readonly Dictionary<int, Finger> _ownedFingers = new(); // doigts réellement "claim"
    private readonly List<int> _captured = new(2);                  // sous-ensemble pour le geste

    // État duplication
    private bool  _armed;
    private bool  _duplicating;
    private float _downTime;

    // Plan & repères
    private Plane   _plane;
    private Vector3 _startW;
    private Vector3 _originW;

    // Axe & crans
    private bool    _axisChosen;
    private Vector3 _axisDir;
    private float   _step;
    private int     _nextIndex;
    private int     _spawned;
    private float   _lastSpawnTime;

    private DraggablePart _drag;

    // Capture bubble
    private Vector3 _captureCenterW;
    private float   _captureRadiusW;

    // Source runtime
    private GameObject _runtimeSource;

    private void Awake()
    {
        _cam  = Camera.main;
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
        else
        {
            Debug.LogWarning("[PartDuplicator] MultiTouchManager.Instance est null.");
        }
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

        // Sécurités locale et globale
        if (_drag) _drag.enabled = true;
        if (drawingTool) drawingTool.enabled = true;
        ReleaseAllFor(this);
        ResetState();
    }

    // ----------------- Events -----------------

    private void Began(MultiTouchManager.TouchEvt e)
    {
        // On ne s'intéresse qu’aux touch qui frappent CET objet
        if (!IsOverThis(e.position)) return;

        // Tenter de réserver ce doigt. Si déjà pris par un autre duplicateur → on ignore.
        if (!TryClaimFinger(e.fingerId, this)) return;

        // Enregistrer ce doigt comme "owned"
        _ownedFingers[e.fingerId] = new Finger { id = e.fingerId, screenPos = e.position };

        // Ajouter aux "captured" jusqu’à atteindre le quota
        if (_captured.Count < requiredFingers)
        {
            _captured.Add(e.fingerId);

            if (_captured.Count == requiredFingers)
            {
                // Source runtime
                _runtimeSource = autoUseSelfAsSourceOnSelect ? gameObject : (prefab ? prefab : gameObject);

                // Désactiver le drag & le dessin localement pendant le trait
                if (_drag) _drag.enabled = false;
                if (drawingTool) drawingTool.enabled = false;

                _downTime = Time.time;
                _armed    = false;

                // Plan de geste
                _plane = (axisFrame == AxisFrame.ScreenXY)
                    ? new Plane(-_cam.transform.forward, transform.position)
                    : new Plane(Vector3.forward, new Vector3(transform.position.x, transform.position.y, transform.position.z));

                _startW  = ScreenToWorldOnPlane(GetCapturedCentroid(), _plane);
                _originW = transform.position;

                // Bulle de capture
                ComputeCaptureBubble(out _captureCenterW, out _captureRadiusW);

                _axisChosen     = false;
                _nextIndex      = 1;
                _spawned        = 0;
                _lastSpawnTime  = -999f;
                _duplicating    = true;
            }
        }
    }

    private void Moved(MultiTouchManager.TouchEvt e)
    {
        // On ne traite que les doigts que NOUS possédons
        if (!_ownedFingers.TryGetValue(e.fingerId, out var f)) return;

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
        var dist  = delta.magnitude;

        if (!_axisChosen)
        {
            if (dist < axisPickMinMove) return;

            if (axisFrame == AxisFrame.ScreenXY)
            {
                var right = _cam.transform.right;
                var up    = _cam.transform.up;
                float dr = Mathf.Abs(Vector3.Dot(delta.normalized, right));
                float du = Mathf.Abs(Vector3.Dot(delta.normalized, up));
                _axisDir = (dr >= du)
                    ? Mathf.Sign(Vector3.Dot(delta, right)) * right
                    : Mathf.Sign(Vector3.Dot(delta, up))    * up;
            }
            else // WorldXY
            {
                var right = Vector3.right;
                var up    = Vector3.up;
                float dr = Mathf.Abs(Vector3.Dot(delta.normalized, right));
                float du = Mathf.Abs(Vector3.Dot(delta.normalized, up));
                _axisDir = (dr >= du)
                    ? Mathf.Sign(Vector3.Dot(delta, right)) * right
                    : Mathf.Sign(Vector3.Dot(delta, up))    * up;
            }

            _step = ComputeModelLengthAlong(_axisDir.normalized) + separationMargin;
            if (spacingOverride > 0f) _step = spacingOverride + separationMargin;
            _step = Mathf.Max(_step, 0.0001f);

            _axisChosen = true;
        }

        float signed     = Vector3.Dot(currW - _originW, _axisDir.normalized);
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
        // Si ce doigt n'était pas à moi, j'ignore
        bool wasOwned = _ownedFingers.Remove(e.fingerId);
        ReleaseFinger(e.fingerId, this);

        if (!wasOwned) return;

        // Si c’était un doigt capturé, on stoppe le trait
        if (_captured.Contains(e.fingerId))
            FinishDuplication();
    }

    // ----------------- Helpers -----------------

    private Vector2 GetCapturedCentroid()
    {
        if (_captured.Count == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero; int count = 0;
        foreach (var id in _captured)
        {
            if (_ownedFingers.TryGetValue(id, out var f)) { sum += f.screenPos; count++; }
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
        if (rends.Length == 0) { centerW = transform.position; radiusW = 0.1f; return; }
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
            if (!_ownedFingers.TryGetValue(id, out var f)) return false;

            // OK si encore sur l'objet
            if (IsOverThis(f.screenPos)) continue;

            // Sinon, dans le plan, doit rester dans la bulle
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
        var source = _runtimeSource ? _runtimeSource : (prefab ? prefab : gameObject);
        var parent = transform.parent;
        var clone  = Instantiate(source, pos, rot, parent);
        clone.name = source.name.Replace("(Clone)", "").Trim() + " (Clone)";

        var audio = clone.GetComponent<AudioSource>();
        if (audio) audio.Play();
    }

    private void CancelDuplication() => FinishDuplication();

    private void FinishDuplication()
    {
        if (_drag)       _drag.enabled = true;
        if (drawingTool) drawingTool.enabled = true;

        // on libère les doigts capturés (ceux réellement détenus)
        foreach (var id in _captured)
            ReleaseFinger(id, this);

        ResetState();
    }

    private void ResetState()
    {
        _runtimeSource = null;
        _captured.Clear();
        _armed = false;
        _duplicating = false;
        _axisChosen = false;
        _nextIndex = 1;
        _spawned = 0;
        _lastSpawnTime = -999f;
        _ownedFingers.Clear();
    }
}
