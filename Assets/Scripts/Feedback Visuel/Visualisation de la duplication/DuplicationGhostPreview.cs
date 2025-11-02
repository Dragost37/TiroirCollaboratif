using System; // <- pour Func<>
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class DuplicationGhostPreview : MonoBehaviour
{
    [Header("Référence du duplicateur")]
    private PartDuplicator duplicator;

    [Header("Feedback visuel (marqueur)")]
    [Tooltip("Prefab à utiliser comme marqueur (facultatif). S'il est vide, un cube transparent sera créé.")]
    public GameObject markerPrefab;
    [Tooltip("Matériau du marqueur (facultatif). Si vide, un matériau transparent sera créé à la volée.")]
    public Material markerMaterial;
    [Tooltip("Couleur appliquée si aucun matériau n'est fourni (A=opacité).")]
    public Color markerColor = new Color(1f, 0.35f, 0f, 0.45f);
    [Tooltip("Échelle de base du marker (X,Y, Z hors longueur).")]
    public Vector3 markerScale = new Vector3(0.25f, 0.25f, 0.25f);
    [Tooltip("Aligner la rotation du marker sur l'axe de duplication.")]
    public bool alignMarkerToAxis = true;
    [Tooltip("Allonger le marker le long de l'axe de duplication (sur Z local).")]
    public bool stretchMarkerAlongAxis = true;
    [Tooltip("Longueur du marker (si stretch activé).")]
    public float markerLength = 0.5f;
    [Tooltip("Layer à appliquer au marqueur (-1 = inchangé). Evite 2=IgnoreRaycast si ta caméra ne le rend pas.")]
    public int markerLayer = -1;
    [Tooltip("Afficher une ligne entre l'objet et le marqueur.")]
    public bool showDirectionLine = true;
    [Tooltip("Largeur de la ligne directionnelle.")]
    public float lineWidth = 0.02f;

    [Header("Overrides geste (facultatif)")]
    [Tooltip("Si vrai, le ghost utilise ces valeurs au lieu de celles du PartDuplicator pour s'armer et choisir l'axe (et le switch).")]
    public bool useLocalGestureOverrides = true;
    [Min(0f)] public float holdTimeOverride = 0f;            // aucun délai d'armement
    [Min(0f)] public float axisPickMinMoveOverride = 0.001f; // mouvement min très petit

    [Header("Position du ghost")]
    [Tooltip("Si > 0, le marqueur est placé à cette distance fixe le long de l'axe choisi (ignore la taille du modèle et la marge).")]
    public float fixedGhostDistance = 0f;

    // --- NOUVEAU : changement d'axe en cours de geste (mêmes réglages que PartDuplicator) ---
    [Header("Changement d'axe en cours de geste")]
    [Tooltip("Autoriser le changement d'axe pendant le mouvement (si useLocalGestureOverrides=false, prend le réglage du duplicator).")]
    public bool allowAxisSwitching = true;
    [Tooltip("Distance minimale parcourue avant une (ré)évaluation.")]
    public float axisSwitchMinMove = 0.02f;
    [Tooltip("Adhérence à l'axe courant (1 = très collant, 0.5 ~ 60°).")]
    [Range(0.5f, 0.99f)] public float axisStickiness = 0.85f;
    [Tooltip("Anti-flutter: délai min entre deux bascules (secondes).")]
    public float axisSwitchCooldown = 0.07f;
    // ----------------------------------------------------------------------------------------

    [Header("DEBUG visibilité")]
    public bool debugAlwaysVisibleWhileTracking = false;
    public bool debugShowBeforeAxisChosen = false;
    public bool debugIgnoreHoldTime = true;
    public bool debugLogs = false;

    private Camera _cam;

    private float HoldTime => (debugIgnoreHoldTime) ? 0f
                       : (useLocalGestureOverrides && duplicator != null) ? holdTimeOverride
                       : (duplicator != null ? duplicator.holdTime : 0f);

    private float AxisPickMinMove => (useLocalGestureOverrides && duplicator != null) ? axisPickMinMoveOverride
                             : (duplicator != null ? duplicator.axisPickMinMove : 0.01f);

    // Accès aux réglages de switch (override local ou via duplicator)
    private bool AllowAxisSwitching => useLocalGestureOverrides
        ? allowAxisSwitching
        : (duplicator != null ? duplicator.allowAxisSwitching : true);

    private float AxisSwitchMinMove => useLocalGestureOverrides
        ? axisSwitchMinMove
        : (duplicator != null ? duplicator.axisSwitchMinMove : 0.02f);

    private float AxisStickiness => useLocalGestureOverrides
        ? axisStickiness
        : (duplicator != null ? duplicator.axisStickiness : 0.85f);

    private float AxisSwitchCooldown => useLocalGestureOverrides
        ? axisSwitchCooldown
        : (duplicator != null ? duplicator.axisSwitchCooldown : 0.07f);

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

    // Instances de feedback
    private GameObject _marker;
    private LineRenderer _line;

    // state debug
    private Vector3 _lastTargetPos;

    // anti-flutter switch
    private float _lastAxisSwitchTime = -999f;

    // ---------- Auto-assign duplicator sur le même GameObject ----------
    private void Reset()                { EnsureDuplicatorReference(); }
    private void OnValidate()           { EnsureDuplicatorReference(); }
    private void Awake()                { _cam = Camera.main; EnsureDuplicatorReference(); }
    private void EnsureDuplicatorReference()
    {
        if (duplicator == null)
            duplicator = GetComponent<PartDuplicator>(); // même GameObject
    }
    // -------------------------------------------------------------------

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
        HideFeedback(true);
        _fingers.Clear();
        _captured.Clear();
        _tracking = false;
        _axisChosen = false;
        _nextIndex = 1;
    }

    // ----------------- GESTE TOUCH -----------------

    private void Began(MultiTouchManager.TouchEvt e)
    {
        if (duplicator == null) return;
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
            _lastAxisSwitchTime = -999f;

            EnsureFeedbackInstances();
            SetFeedbackVisible(debugAlwaysVisibleWhileTracking);
            if (debugLogs) Debug.Log("[GhostPrev] Began: tracking started", this);
        }
    }

    private void Moved(MultiTouchManager.TouchEvt e)
    {
        if (_fingers.TryGetValue(e.fingerId, out var f))
            f.screenPos = e.position;

        if (!_tracking || duplicator == null) return;
        if (!CapturedStillValidInBubble()) { if (debugLogs) Debug.Log("[GhostPrev] Cancel: out of bubble", this); CancelPreview(); return; }

        // Armement
        if (!_armed)
        {
            if (Time.time - _downTime >= HoldTime) _armed = true;
            else if (!debugAlwaysVisibleWhileTracking) return;
        }

        var currW = ScreenToWorldOnPlane(GetCapturedCentroid(), _plane);
        var delta = currW - _startW;
        var dist = delta.magnitude;

        // (Ré)évaluation de l'axe (inclut le switch en cours de geste)
        Func<Vector3, float> stepFunc = (Vector3 dir) =>
        {
            if (fixedGhostDistance > 0f)
                return Mathf.Max(fixedGhostDistance, 0.0001f);

            float baseLen = ComputeModelLengthAlong(dir.normalized) + duplicator.separationMargin;
            if (duplicator.spacingOverride > 0f)
                baseLen = duplicator.spacingOverride + duplicator.separationMargin;
            return Mathf.Max(baseLen, 0.0001f);
        };

        bool pickedOrSwitched = TryPickOrSwitchAxis(
            delta, dist, currW,
            ref _axisChosen, ref _axisDir, ref _step,
            stepFunc,
            duplicator.maxPerStroke, ref _nextIndex, _originW
        );

        // Si axe pas encore fixé et qu'on ne veut pas de preview avant: sortie
        if (!_axisChosen && !debugShowBeforeAxisChosen)
        {
            if (debugAlwaysVisibleWhileTracking)
                UpdateVisualPreview(Mathf.Max(fixedGhostDistance > 0f ? fixedGhostDistance : 0.0001f, 0.0001f));
            return;
        }

        float signed = Vector3.Dot(currW - _originW, _axisDir.normalized);
        float targetDist = _nextIndex * _step;

        UpdateVisualPreview(targetDist);

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
        radiusW = b.extents.magnitude * (duplicator != null ? duplicator.captureRadiusFactor : 5f);
    }

    private bool CapturedStillValidInBubble()
    {
        float dynRadius = _captureRadiusW;
        if (duplicator != null && duplicator.useDynamicCapture && _axisChosen)
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

    // ----------------- FEEDBACK VISUEL -----------------

    private void EnsureFeedbackInstances()
    {
        if (_marker == null)
        {
            if (markerPrefab != null)
            {
                _marker = Instantiate(markerPrefab, transform.position, transform.rotation, transform.parent);
            }
            else
            {
                _marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _marker.transform.SetParent(transform.parent, worldPositionStays: true);
                _marker.transform.position = transform.position;
                var col = _marker.GetComponent<Collider>();
                if (col) Destroy(col);
                var mr = _marker.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    if (markerMaterial != null)
                    {
                        mr.sharedMaterial = markerMaterial;
                    }
                    else
                    {
                        var shader = Shader.Find("Standard");
                        Material mat = shader ? new Material(shader) : new Material(Shader.Find("Unlit/Color"));
                        TryForceTransparent(mat);
                        mat.color = markerColor;
                        mr.sharedMaterial = mat;
                    }
                }
            }

            _marker.name = $"{name} (Marker)";

            if (markerLayer >= 0 && markerLayer <= 31)
                SetLayerRecursively(_marker, markerLayer);

            foreach (var mb in _marker.GetComponentsInChildren<MonoBehaviour>(true))
                mb.enabled = false;
        }

        if (showDirectionLine && _line == null)
        {
            var go = new GameObject($"{name} (MarkerLine)");
            go.transform.SetParent(transform.parent, worldPositionStays: true);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.startWidth = lineWidth;
            _line.endWidth = lineWidth;
            _line.positionCount = 2;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _line.material = new Material(shader);
            _line.material.color = new Color(markerColor.r, markerColor.g, markerColor.b, Mathf.Clamp01(markerColor.a + 0.2f));

            if (markerLayer >= 0 && markerLayer <= 31)
                _line.gameObject.layer = markerLayer;
        }
    }

    private void UpdateVisualPreview(float targetDist)
    {
        if (duplicator == null) { SetFeedbackVisible(false); return; }

        bool canShow = _tracking
                       && (_armed || debugAlwaysVisibleWhileTracking || debugIgnoreHoldTime)
                       && (_axisChosen || debugShowBeforeAxisChosen)
                       && _nextIndex <= duplicator.maxPerStroke;

        if (!canShow) { SetFeedbackVisible(debugAlwaysVisibleWhileTracking); return; }

        EnsureFeedbackInstances();
        var pos = _originW + _axisDir.normalized * targetDist;
        _lastTargetPos = pos;

        if (alignMarkerToAxis)
        {
            Vector3 upRef = (_cam != null) ? _cam.transform.up : Vector3.up;
            _marker.transform.rotation = Quaternion.LookRotation(_axisDir.normalized, upRef);
        }
        else
        {
            _marker.transform.rotation = transform.rotation;
        }

        if (stretchMarkerAlongAxis)
        {
            _marker.transform.localScale = new Vector3(
                Mathf.Max(0.0001f, markerScale.x),
                Mathf.Max(0.0001f, markerScale.y),
                Mathf.Max(0.0001f, markerLength)
            );
        }
        else
        {
            _marker.transform.localScale = markerScale;
        }

        _marker.transform.position = pos;
        SetFeedbackVisible(true);

        if (showDirectionLine && _line != null)
        {
            _line.enabled = true;
            _line.SetPosition(0, _originW);
            _line.SetPosition(1, pos);
        }

        if (debugLogs) Debug.Log($"[GhostPrev] Marker @ {pos} (dist={targetDist}, idx={_nextIndex})", this);
    }

    private void SetFeedbackVisible(bool visible)
    {
        if (_marker != null)
        {
            foreach (var r in _marker.GetComponentsInChildren<Renderer>(true))
                r.enabled = visible;
        }
        if (_line != null)
            _line.enabled = visible && showDirectionLine;
    }

    private void HideFeedback(bool destroy = false)
    {
        SetFeedbackVisible(false);
        if (destroy)
        {
            if (_marker != null) Destroy(_marker);
            if (_line != null) Destroy(_line.gameObject);
            _marker = null;
            _line = null;
        }
    }

    private void FinishPreview()
    {
        HideFeedback();
        _captured.Clear();
        _tracking = false;
        _axisChosen = false;
        _nextIndex = 1;
    }

    private void CancelPreview() => FinishPreview();

    // ----------------- Helpers -----------------

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursively(t.gameObject, layer);
    }

    private static void TryForceTransparent(Material m)
    {
        if (m == null || m.shader == null) return;
        var sname = m.shader.name;
        if (sname.Contains("Standard"))
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

    private void OnDrawGizmosSelected()
    {
        if (!_tracking) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(_originW == Vector3.zero ? transform.position : _originW, 0.03f);

        if (_axisChosen)
        {
            var pos = (_originW == Vector3.zero ? transform.position : _originW) + _axisDir.normalized * Mathf.Max(_step, 0.0001f);
            Gizmos.DrawLine(_originW, pos);
            Gizmos.DrawWireCube(_lastTargetPos == Vector3.zero ? pos : _lastTargetPos, new Vector3(0.05f, 0.05f, markerLength > 0 ? markerLength : 0.05f));
        }
    }

    // ----------- Helper local : (re)choix et switch d'axe avec hystérésis -----------
    private bool TryPickOrSwitchAxis(
        Vector3 delta, float dist, Vector3 currWorldPos,
        ref bool axisChosen, ref Vector3 axisDir, ref float step,
        Func<Vector3, float> computeStep,
        int maxPerStroke, ref int nextIndex, Vector3 originW)
    {
        // Assez de déplacement ? (max entre seuil initial & seuil de réévaluation)
        float minMove = Mathf.Max(AxisPickMinMove, AxisSwitchMinMove);
        if (dist < minMove) return false;

        // Candidats (droite/haut selon le cadre d'axes)
        Vector3 right, up;
        if (duplicator != null && duplicator.axisFrame == PartDuplicator.AxisFrame.ScreenXY && _cam != null)
        {
            right = _cam.transform.right;
            up    = _cam.transform.up;
        }
        else
        {
            right = Vector3.right;
            up    = Vector3.up;
        }

        Vector3 nd = delta.normalized;
        float dr = Mathf.Abs(Vector3.Dot(nd, right));
        float du = Mathf.Abs(Vector3.Dot(nd, up));
        Vector3 cand = (dr >= du)
            ? Mathf.Sign(Vector3.Dot(delta, right)) * right
            : Mathf.Sign(Vector3.Dot(delta, up))    * up;

        if (!axisChosen)
        {
            axisDir = cand;
            step    = Mathf.Max(computeStep(axisDir), 0.0001f);
            axisChosen = true;
            _lastAxisSwitchTime = Time.time;
            if (debugLogs) Debug.Log($"[GhostPrev] Axis picked dir={axisDir}, step={step}", this);
            return true;
        }

        if (!AllowAxisSwitching) return false;
        if (Time.time - _lastAxisSwitchTime < AxisSwitchCooldown) return false;

        float currAlign  = Mathf.Abs(Vector3.Dot(nd, axisDir.normalized));
        float candAlign  = Mathf.Abs(Vector3.Dot(nd, cand.normalized));

        // Switch si candidat suffisamment aligné ET meilleur que l'actuel
        if (candAlign >= AxisStickiness && candAlign > currAlign)
        {
            axisDir = cand;
            step    = Mathf.Max(computeStep(axisDir), 0.0001f);
            _lastAxisSwitchTime = Time.time;

            // Recalibrer l'index à partir de la position courante projetée sur le NOUVEL axe
            float signed = Vector3.Dot((currWorldPos - originW), axisDir.normalized);
            int idx = Mathf.FloorToInt(signed / step) + 1;
            nextIndex = Mathf.Clamp(idx, 1, maxPerStroke);

            if (debugLogs) Debug.Log($"[GhostPrev] Axis switched dir={axisDir}, step={step}, nextIndex={nextIndex}", this);
            return true;
        }

        return false;
    }
}
