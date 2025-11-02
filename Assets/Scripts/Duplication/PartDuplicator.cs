using System; // <- pour Func<>
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

    // ---------- NOUVEAU : Changement d'axe en cours de geste ----------
    [Header("Changement d'axe en cours de geste")]
    [Tooltip("Autoriser le changement d'axe pendant le mouvement.")]
    public bool allowAxisSwitching = true;

    [Tooltip("Distance minimale parcourue avant une (ré)évaluation.")]
    public float axisSwitchMinMove = 0.02f;

    [Tooltip("Adhérence à l'axe courant (1 = très collant, 0.5 ≈ 60°). Le switch se fait si le nouvel axe dépasse ce seuil ET aligne mieux le mouvement.")]
    [Range(0.5f, 0.99f)] public float axisStickiness = 0.85f;

    [Tooltip("Anti-flutter : délai min entre deux bascules (secondes).")]
    public float axisSwitchCooldown = 0.07f;
    // -------------------------------------------------------------------

    // ---------- DEBUG VISU DE LA BULLE ----------
    [Header("Debug capture bubble")]
    [Tooltip("Afficher la bulle de capture (statique et dynamique).")]
    public bool showCaptureDebug = false;

    [Tooltip("Touche pour activer/désactiver la visualisation en runtime (None = désactivé).")]
    public KeyCode debugToggleKey = KeyCode.F9;

    [Tooltip("Couleur de la bulle de base (statique).")]
    public Color captureBubbleColor = new Color(0f, 1f, 1f, 0.25f);

    [Tooltip("Couleur de la bulle dynamique (base + along + slack).")]
    public Color dynamicBubbleColor = new Color(1f, 0.5f, 0f, 0.25f);

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

    // Capture bubble (base)
    private Vector3 _captureCenterW;
    private float   _captureRadiusW;

    // ---- DEBUG: valeurs courantes dynamiques (pour le dessin) ----
    private Vector3 _currentDynamicCenterW;
    private float   _currentDynamicRadiusW;

    // Source runtime
    private GameObject _runtimeSource;

    // NEW: mémorisation des états initiaux
    private bool _dragWasEnabled;           // NEW
    private bool _drawingToolWasEnabled;    // NEW

    // NOUVEAU : anti-flutter pour bascules répétées
    private float _lastAxisSwitchTime = -999f;

    private void Awake()
    {
        _cam  = Camera.main;
        _drag = GetComponent<DraggablePart>();

        // NEW: capture l'état actuel comme valeur de repli (si OnDisable survient sans geste)
        _dragWasEnabled        = _drag ? _drag.enabled : false;
        _drawingToolWasEnabled = drawingTool ? drawingTool.enabled : false;
    }

    private void Update()
    {
        // Toggle runtime de la visualisation si souhaité
        if (debugToggleKey != KeyCode.None && Input.GetKeyDown(debugToggleKey))
            showCaptureDebug = !showCaptureDebug;
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
        // NEW: restaurer l'état précédent, ne pas forcer à true
        if (_drag)       _drag.enabled       = _dragWasEnabled;        // NEW
        if (drawingTool) drawingTool.enabled = _drawingToolWasEnabled; // NEW

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

                // NEW: mémoriser l'état courant puis désactiver
                if (_drag)
                {
                    _dragWasEnabled = _drag.enabled;  // NEW
                    _drag.enabled   = false;          // NEW (inchangé sur le fond)
                }
                if (drawingTool)
                {
                    _drawingToolWasEnabled = drawingTool.enabled; // NEW
                    drawingTool.enabled    = false;               // NEW
                }

                _downTime = Time.time;
                _armed    = false;

                // Plan de geste
                _plane = (axisFrame == AxisFrame.ScreenXY)
                    ? new Plane(-_cam.transform.forward, transform.position)
                    : new Plane(Vector3.forward, new Vector3(transform.position.x, transform.position.y, transform.position.z));

                _startW  = ScreenToWorldOnPlane(GetCapturedCentroid(), _plane);
                _originW = transform.position;

                // Bulle de capture (base)
                ComputeCaptureBubble(out _captureCenterW, out _captureRadiusW);

                // Initialiser la bulle dynamique pour la visu
                _currentDynamicCenterW = _captureCenterW;
                _currentDynamicRadiusW = _captureRadiusW;

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

        // --- (Ré)évaluation de l'axe (inclut le switch en cours de geste) ---
        System.Func<Vector3, float> stepFunc = (Vector3 dir) =>
        {
            float s = ComputeModelLengthAlong(dir.normalized) + separationMargin;
            if (spacingOverride > 0f) s = spacingOverride + separationMargin;
            return Mathf.Max(s, 0.0001f);
        };

        bool pickedOrSwitched = TryPickOrSwitchAxis(
            delta, dist, currW,
            ref _axisChosen, ref _axisDir, ref _step,
            stepFunc,
            maxPerStroke, ref _nextIndex, _originW
        );

        if (!_axisChosen) return; // pas encore assez de mouvement pour trancher

        // --- Mise à jour de la bulle dynamique pour la visualisation ---
        if (showCaptureDebug)
        {
            float along = 0f;
            if (useDynamicCapture && _axisChosen)
            {
                along = Mathf.Abs(Vector3.Dot(currW - _originW, _axisDir.normalized));
            }
            _currentDynamicCenterW = _captureCenterW;
            _currentDynamicRadiusW = _captureRadiusW + along + (useDynamicCapture ? dynamicCaptureSlack : 0f);
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
        // NEW: restaurer au lieu de forcer à true
        if (_drag)       _drag.enabled       = _dragWasEnabled;        // NEW
        if (drawingTool) drawingTool.enabled = _drawingToolWasEnabled; // NEW

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

        // Réinitialisation debug
        _currentDynamicCenterW = _captureCenterW;
        _currentDynamicRadiusW = _captureRadiusW;

        // NOTE: on NE touche PAS à _dragWasEnabled / _drawingToolWasEnabled ici.
    }

    // --------------- DESSIN DES BULLES (Scene view) ---------------
    private void OnDrawGizmos()
    {
        if (!showCaptureDebug) return;

        Vector3 centerBase;
        float   radiusBase;

        if (Application.isPlaying)
        {
            centerBase = _captureCenterW != Vector3.zero ? _captureCenterW : transform.position;
            radiusBase = (_captureRadiusW > 0f) ? _captureRadiusW : 0.1f;
        }
        else
        {
            var rends = GetComponentsInChildren<Renderer>();
            if (rends.Length == 0)
            {
                centerBase = transform.position;
                radiusBase = 0.1f * captureRadiusFactor;
            }
            else
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                centerBase = b.center;
                radiusBase = b.extents.magnitude * captureRadiusFactor;
            }
        }

        Gizmos.color = captureBubbleColor;
        Gizmos.DrawWireSphere(centerBase, Mathf.Max(radiusBase, 0.0001f));
        Gizmos.color = new Color(captureBubbleColor.r, captureBubbleColor.g, captureBubbleColor.b, captureBubbleColor.a * 0.6f);
        Gizmos.DrawSphere(centerBase, Mathf.Max(radiusBase, 0.0001f));

        float dynRadius = Application.isPlaying ? Mathf.Max(_currentDynamicRadiusW, radiusBase) : radiusBase;
        Vector3 dynCenter = Application.isPlaying ? _currentDynamicCenterW : centerBase;

        if (useDynamicCapture)
        {
            Gizmos.color = dynamicBubbleColor;
            Gizmos.DrawWireSphere(dynCenter, Mathf.Max(dynRadius, 0.0001f));
            Gizmos.color = new Color(dynamicBubbleColor.r, dynamicBubbleColor.g, dynamicBubbleColor.b, dynamicBubbleColor.a * 0.6f);
            Gizmos.DrawSphere(dynCenter, Mathf.Max(dynRadius, 0.0001f));
        }
    }

    // ----------- Helper local : (re)choix et switch d'axe avec hystérésis -----------
    private bool TryPickOrSwitchAxis(
        Vector3 delta, float dist, Vector3 currWorldPos,
        ref bool axisChosen, ref Vector3 axisDir, ref float step,
        Func<Vector3, float> computeStep,
        int maxPerStroke, ref int nextIndex, Vector3 originW)
    {
        // Assez de déplacement ? (prend le max entre le seuil initial et le seuil de réévaluation)
        float minMove = Mathf.Max(axisPickMinMove, axisSwitchMinMove);
        if (dist < minMove) return false;

        // Candidats (droite/haut en Screen ou World)
        Vector3 right, up;
        if (axisFrame == AxisFrame.ScreenXY && _cam != null)
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
            return true;
        }

        if (!allowAxisSwitching) return false;
        if (Time.time - _lastAxisSwitchTime < axisSwitchCooldown) return false;

        float currAlign  = Mathf.Abs(Vector3.Dot(nd, axisDir.normalized));
        float candAlign  = Mathf.Abs(Vector3.Dot(nd, cand.normalized));

        // Switch si candidat suffisamment aligné ET meilleur que l'actuel
        if (candAlign >= axisStickiness && candAlign > currAlign)
        {
            axisDir = cand;
            step    = Mathf.Max(computeStep(axisDir), 0.0001f);
            _lastAxisSwitchTime = Time.time;

            // Recalibrer le cran cible sur le NOUVEL axe (cohérent avec la position courante)
            float signed = Vector3.Dot((currWorldPos - originW), axisDir.normalized);
            int idx = Mathf.FloorToInt(signed / step) + 1;
            nextIndex = Mathf.Clamp(idx, 1, maxPerStroke);

            return true;
        }

        return false;
    }
}
