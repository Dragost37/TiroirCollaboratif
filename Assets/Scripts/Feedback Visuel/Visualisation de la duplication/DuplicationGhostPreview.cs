using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class DuplicationGhostPreview : MonoBehaviour
{
    [Header("Référence du duplicateur")]
    public PartDuplicator duplicator;

    [Header("Apparence du fantôme")]
    [Tooltip("Matériau semi-transparent appliqué au ghost (facultatif).")]
    public Material ghostMaterial;
    [Range(0f, 1f)] public float ghostOpacity = 0.35f;
    [Tooltip("Layer à appliquer au ghost (2 = IgnoreRaycast). -1 = inchangé.")]
    public int ghostLayer = 2;

    private Camera _cam;

    // Gestion locale du geste
    private class Finger { public int id; public Vector2 screenPos; }
    private readonly Dictionary<int, Finger> _fingers = new();
    private readonly List<int> _captured = new(2);

    private bool _armed;
    private bool _tracking;
    private bool _axisChosen;
    private float _downTime;
    private Plane _plane;
    private Vector3 _startW, _originW;
    private Vector3 _axisDir;
    private float _step;
    private int _nextIndex = 1;

    private Vector3 _captureCenterW;
    private float _captureRadiusW;

    // Instance du ghost
    private GameObject _ghost;

    private void Awake()
    {
        _cam = Camera.main;
        if (!duplicator) duplicator = GetComponent<PartDuplicator>();
    }

    private void OnEnable()
    {
        var mt = MultiTouchManager.Instance ?? FindObjectOfType<MultiTouchManager>();
        if (mt != null)
        {
            mt.OnTouchBegan += Began;
            mt.OnTouchMoved += Moved;
            mt.OnTouchEnded += Ended;
        }
    }

    private void OnDisable()
    {
        var mt = MultiTouchManager.Instance ?? FindObjectOfType<MultiTouchManager>();
        if (mt != null)
        {
            mt.OnTouchBegan -= Began;
            mt.OnTouchMoved -= Moved;
            mt.OnTouchEnded -= Ended;
        }
        HideGhost(true);
        _fingers.Clear();
        _captured.Clear();
        _tracking = false;
        _axisChosen = false;
        _nextIndex = 1;
    }

    // ----------------- GESTE TOUCH -----------------

    private void Began(MultiTouchManager.TouchEvt e)
    {
        if (!IsOverThis(e.position)) return;

        _fingers[e.fingerId] = new Finger { id = e.fingerId, screenPos = e.position };

        if (!_captured.Contains(e.fingerId) && _captured.Count < duplicator.requiredFingers)
            _captured.Add(e.fingerId);

        if (_captured.Count == duplicator.requiredFingers)
        {
            _downTime = Time.time;
            _armed = false;
            _tracking = true;

            _plane = (duplicator.axisFrame == PartDuplicator.AxisFrame.ScreenXY)
                ? new Plane(-_cam.transform.forward, transform.position)
                : new Plane(Vector3.forward, new Vector3(transform.position.x, transform.position.y, transform.position.z));

            _startW = ScreenToWorldOnPlane(GetCapturedCentroid(), _plane);
            _originW = transform.position;

            ComputeCaptureBubble(out _captureCenterW, out _captureRadiusW);
            _axisChosen = false;
            _nextIndex = 1;

            EnsureGhostInstance();
            UpdateGhostVisibility(false);
        }
    }

    private void Moved(MultiTouchManager.TouchEvt e)
    {
        if (_fingers.TryGetValue(e.fingerId, out var f))
            f.screenPos = e.position;

        if (!_tracking) return;
        if (!CapturedStillValidInBubble()) { CancelPreview(); return; }

        if (!_armed)
        {
            if (Time.time - _downTime >= duplicator.holdTime) _armed = true;
            else return;
        }

        var currW = ScreenToWorldOnPlane(GetCapturedCentroid(), _plane);
        var delta = currW - _startW;
        var dist = delta.magnitude;

        if (!_axisChosen)
        {
            if (dist < duplicator.axisPickMinMove) return;

            if (duplicator.axisFrame == PartDuplicator.AxisFrame.ScreenXY)
            {
                var right = _cam.transform.right;
                var up = _cam.transform.up;
                float dr = Mathf.Abs(Vector3.Dot(delta.normalized, right));
                float du = Mathf.Abs(Vector3.Dot(delta.normalized, up));
                _axisDir = (dr >= du)
                    ? Mathf.Sign(Vector3.Dot(delta, right)) * right
                    : Mathf.Sign(Vector3.Dot(delta, up)) * up;
            }
            else
            {
                var right = Vector3.right;
                var up = Vector3.up;
                float dr = Mathf.Abs(Vector3.Dot(delta.normalized, right));
                float du = Mathf.Abs(Vector3.Dot(delta.normalized, up));
                _axisDir = (dr >= du)
                    ? Mathf.Sign(Vector3.Dot(delta, right)) * right
                    : Mathf.Sign(Vector3.Dot(delta, up)) * up;
            }

            _step = ComputeModelLengthAlong(_axisDir.normalized) + duplicator.separationMargin;
            if (duplicator.spacingOverride > 0f) _step = duplicator.spacingOverride + duplicator.separationMargin;
            _step = Mathf.Max(_step, 0.0001f);
            _axisChosen = true;
        }

        float signed = Vector3.Dot(currW - _originW, _axisDir.normalized);
        float targetDist = _nextIndex * _step;

        UpdateGhostPreview(targetDist);

        if (signed >= targetDist && _nextIndex < duplicator.maxPerStroke)
            _nextIndex++;
    }

    private void Ended(MultiTouchManager.TouchEvt e)
    {
        if (_fingers.Remove(e.fingerId) && _captured.Contains(e.fingerId))
            FinishPreview();
    }

    // ----------------- OUTILS -----------------

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
        if (rends.Length == 0) { centerW = transform.position; radiusW = 0.1f; return; }
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        centerW = b.center;
        radiusW = b.extents.magnitude * duplicator.captureRadiusFactor;
    }

    private bool CapturedStillValidInBubble()
    {
        float dynRadius = _captureRadiusW;
        if (duplicator.useDynamicCapture && _axisChosen)
        {
            var currW = ScreenToWorldOnPlane(GetCapturedCentroid(), _plane);
            float along = Mathf.Abs(Vector3.Dot(currW - _originW, _axisDir.normalized));
            dynRadius = _captureRadiusW + along + duplicator.dynamicCaptureSlack;
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

    // ----------------- GESTION DU GHOST -----------------

    private void EnsureGhostInstance()
    {
        if (_ghost) return;

        // 👉 Toujours dupliquer CE GameObject
        GameObject source = gameObject;
        _ghost = Instantiate(source, transform.position, transform.rotation, transform.parent);
        _ghost.name = source.name.Replace("(Clone)", "").Trim() + " (Ghost)";

        // Désactiver TOUS les scripts
        foreach (var mb in _ghost.GetComponentsInChildren<MonoBehaviour>(true))
        {
            mb.enabled = false;
        }

        // Forcer les colliders et audio off
        foreach (var col in _ghost.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var rb in _ghost.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        foreach (var au in _ghost.GetComponentsInChildren<AudioSource>(true)) au.enabled = false;

        // Changer le layer
        if (ghostLayer >= 0 && ghostLayer <= 31)
            SetLayerRecursively(_ghost, ghostLayer);

        // Appliquer un matériau semi-transparent
        if (ghostMaterial)
        {
            foreach (var r in _ghost.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = ghostMaterial;
        }
        else
        {
            foreach (var r in _ghost.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m.HasProperty("_Color"))
                    {
                        var c = m.color; c.a = ghostOpacity; m.color = c;
                        TryForceTransparent(m);
                    }
                }
            }
        }

        UpdateGhostVisibility(false);
    }

    private void UpdateGhostPreview(float targetDist)
    {
        bool canShow = _tracking && _armed && _axisChosen && _nextIndex <= duplicator.maxPerStroke;
        if (!canShow) { UpdateGhostVisibility(false); return; }

        EnsureGhostInstance();
        var pos = _originW + _axisDir.normalized * targetDist;
        _ghost.transform.SetPositionAndRotation(pos, transform.rotation);
        UpdateGhostVisibility(true);
    }

    private void UpdateGhostVisibility(bool visible)
    {
        if (!_ghost) return;
        foreach (var r in _ghost.GetComponentsInChildren<Renderer>(true))
            r.enabled = visible;
    }

    private void HideGhost(bool destroy = false)
    {
        if (!_ghost) return;
        UpdateGhostVisibility(false);
        if (destroy) Destroy(_ghost);
    }

    private void FinishPreview()
    {
        HideGhost();
        _captured.Clear();
        _tracking = false;
        _axisChosen = false;
        _nextIndex = 1;
    }

    private void CancelPreview() => FinishPreview();

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursively(t.gameObject, layer);
    }

    private static void TryForceTransparent(Material m)
    {
        if (m.shader && m.shader.name.Contains("Standard"))
        {
            m.SetFloat("_Mode", 3);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
