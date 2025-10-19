using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class DragDirectionArrowVisualizer : MonoBehaviour
{
    [Header("Refs UI")]
    [Tooltip("Canvas en Screen Space Overlay (ou Screen Space Camera)")]
    public Canvas canvas;
    [Tooltip("Prefab UI (RectTransform + Image). Pivot sera (0, 0.5).")]
    public RectTransform arrowPrefab;

    [Header("Détection")]
    public float dragStartThresholdPx = 10f;
    public string[] requiredComponentTypeNames = new[] { "DraggablePart" };
    public bool climbToParentWithRequiredComponent = true;
    public bool use2DPhysics = true;

    [Header("Flèche (Overlay)")]
    [Tooltip("Longueur min/max en px")]
    public float minLength = 30f;
    public float maxLength = 200f;
    [Tooltip("Facteur px de déplacement -> px de flèche (plus petit = plus long)")]
    public float speedToLength = 1.0f;
    public Color arrowColor = new Color(0.2f, 0.8f, 0.5f, 1f);
    public float fadeDuration = 0.25f;

    [Header("Lissage (anti-saccades)")]
    [Tooltip("Temps de lissage de la LONGUEUR (sec)")]
    public float lengthSmoothTime = 0.08f;
    [Tooltip("Temps de lissage de la ROTATION (sec)")]
    public float angleSmoothTime = 0.06f;
    [Tooltip("Temps de lissage du DELTA doigt (sec) – stabilise la direction avant calcul angle")]
    public float deltaLowpassTime = 0.05f;
    [Tooltip("Vitesse angulaire max (deg/s). 0 = sans limite")]
    public float maxRotationSpeedDeg = 720f;

    [Header("Logs")]
    public bool enableLogs = false;
    public bool logEveryMove = false;

    // >>> AJOUT : types considérés comme "surface de dessin"
    [Header("Surfaces de dessin (exclusions à la création)")]
    [Tooltip("Si un de ces composants est détecté sous le doigt au moment de OnBegan, la flèche n'est PAS créée.")]
    public string[] drawingSurfaceTypeNames = new[] { "DrawOnPlane" };
    // <<< FIN AJOUT

    private const string LOG = "[DragDirectionArrowVisualizer] ";

    private class Finger
    {
        public int id;
        public Vector2 beganPos, lastPos;
        public bool dragging;
        public Transform target;
        public RectTransform arrowUI;
        public Image arrowImg;

        // Lissage
        public Vector2 emaDelta;     // delta doigt lissé
        public float currLen;        // longueur affichée (lissée)
        public float lenVel;         // vel interne SmoothDamp longueur
        public float currAngle;      // angle affiché (lissé)
        public float angleVel;       // vel interne SmoothDampAngle
    }

    private readonly Dictionary<int, Finger> fingers = new();
    private MultiTouchManager _mt;
    private readonly Dictionary<string, Type> _cachedTypes = new();

    // --- Helpers écran <-> canvas ---
    RectTransform CanvasRT => canvas ? canvas.transform as RectTransform : null;

    Camera UICamera
    {
        get
        {
            if (!canvas) return null;
            return (canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera;
        }
    }

    Vector2 ScreenToCanvas(Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRT, screenPos, UICamera, out var lp);
        return lp;
    }

    void Awake()
    {
        if (!canvas) canvas = FindObjectOfType<Canvas>();
        if (!canvas) Debug.LogError(LOG + "Aucun Canvas trouvé.");
        if (!arrowPrefab) Debug.LogWarning(LOG + "arrowPrefab non assigné.");

        _mt = MultiTouchManager.Instance;
        if (_mt == null) Debug.LogError(LOG + "MultiTouchManager.Instance est null.");
    }

    void OnEnable()
    {
        if (_mt == null) return;
        _mt.OnTouchBegan += OnBegan;
        _mt.OnTouchMoved += OnMoved;
        _mt.OnTouchEnded += OnEnded;
    }

    void OnDisable()
    {
        if (_mt != null)
        {
            _mt.OnTouchBegan -= OnBegan;
            _mt.OnTouchMoved  -= OnMoved;
            _mt.OnTouchEnded  -= OnEnded;
        }
        foreach (var f in fingers.Values)
            if (f.arrowUI) Destroy(f.arrowUI.gameObject);
        fingers.Clear();
    }

    // --- Touch Events ---
    void OnBegan(MultiTouchManager.TouchEvt e)
    {
        var f = new Finger
        {
            id = e.fingerId,
            beganPos = e.position,
            lastPos  = e.position,
            dragging = false,
            emaDelta = Vector2.zero,
            currLen  = minLength,
            lenVel   = 0f,
            currAngle = 0f,
            angleVel  = 0f
        };

        f.target = FindTargetAt(e.position);

        // >>> AJOUT : ne PAS créer la flèche si le doigt commence sur une surface de dessin
        if (IsOnDrawingSurface(e.position))
        {
            fingers[e.fingerId] = f; // on garde la trace du doigt, mais sans flèche
            if (enableLogs) Debug.Log(LOG + $"Began on drawing surface → no arrow (id={e.fingerId})");
            return;
        }
        // <<< FIN AJOUT

        // Crée la flèche UI
        if (arrowPrefab && canvas)
        {
            var inst = Instantiate(arrowPrefab, canvas.transform);
            inst.gameObject.SetActive(false);
            inst.SetAsLastSibling();
            inst.pivot = new Vector2(0f, 0.5f);
            inst.anchorMin = inst.anchorMax = new Vector2(0.5f, 0.5f);
            inst.anchoredPosition = ScreenToCanvas(e.position);

            var img = inst.GetComponent<Image>();
            if (img)
            {
                if (img.sprite == null) img.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,2,2), new Vector2(0f,0.5f));
                img.color = arrowColor;
                img.raycastTarget = false;
            }

            // hauteur par défaut si nécessaire
            var sz = inst.sizeDelta;
            if (sz.y < 1f) sz.y = 18f;
            if (sz.x < minLength) sz.x = minLength;
            inst.sizeDelta = sz;

            f.arrowUI = inst;
            f.arrowImg = img;
        }

        fingers[e.fingerId] = f;

        if (enableLogs) Debug.Log(LOG + $"Began id={e.fingerId} screen={e.position} target={(f.target?f.target.name:"null")}");
    }

    void OnMoved(MultiTouchManager.TouchEvt e)
    {
        if (!fingers.TryGetValue(e.fingerId, out var f)) return;

        // delta instantané doigt (pixels)
        Vector2 rawDelta = e.position - f.lastPos;
        f.lastPos = e.position;

        if (!f.dragging)
        {
            float distFromStart = Vector2.Distance(f.beganPos, e.position);
            if (distFromStart >= dragStartThresholdPx)
            {
                var tr = FindTargetAt(e.position);
                if (tr == null) return;
                if (f.target != null && tr != f.target) tr = f.target;

                f.target = tr;
                f.dragging = true;

                if (f.arrowUI) f.arrowUI.gameObject.SetActive(true);

                if (enableLogs)
                    Debug.Log(LOG + $"DRAG START id={f.id} target={f.target.name} began={f.beganPos} now={e.position}");
            }
            return;
        }

        if (!f.arrowUI) return;

        // ========= LISSAGE =========

        // 1) Low-pass sur le delta doigt (stabilise la direction)
        float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
        float alphaDelta = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, deltaLowpassTime)); // 0..1
        f.emaDelta = Vector2.Lerp(f.emaDelta, rawDelta, alphaDelta);

        // Position UI (sans lissage pour garder la réactivité visuelle du point d’ancrage)
        var canvasPos = ScreenToCanvas(e.position);
        f.arrowUI.anchoredPosition = canvasPos;

        // 2) Cible de longueur depuis la vitesse (en px/frame → normalisé avec speedToLength)
        float targetLen = Mathf.Clamp(f.emaDelta.magnitude / Mathf.Max(0.001f, speedToLength), minLength, maxLength);

        // Lissage de longueur (SmoothDamp)
        f.currLen = Mathf.SmoothDamp(f.currLen, targetLen, ref f.lenVel, Mathf.Max(0.0001f, lengthSmoothTime));
        var size = f.arrowUI.sizeDelta;
        size.x = f.currLen;
        f.arrowUI.sizeDelta = size;

        // 3) Cible d’angle depuis la direction lissée
        Vector2 dir = (f.emaDelta.sqrMagnitude > 1e-6f) ? f.emaDelta.normalized : new Vector2(Mathf.Cos(f.currAngle*Mathf.Deg2Rad), Mathf.Sin(f.currAngle*Mathf.Deg2Rad));
        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // Lissage d’angle (SmoothDampAngle) + limitation de vitesse si demandé
        if (angleSmoothTime > 0f)
        {
            f.currAngle = Mathf.SmoothDampAngle(f.currAngle, targetAngle, ref f.angleVel, angleSmoothTime);
            if (maxRotationSpeedDeg > 0f)
            {
                // Clamp vitesse angulaire
                f.currAngle = Mathf.LerpAngle(f.currAngle, targetAngle, Mathf.Clamp01((maxRotationSpeedDeg * dt) / Mathf.Max(1f, Mathf.Abs(Mathf.DeltaAngle(f.currAngle, targetAngle)))));
            }
        }
        else
        {
            f.currAngle = targetAngle;
        }

        f.arrowUI.localRotation = Quaternion.Euler(0f, 0f, f.currAngle);

        if (logEveryMove && enableLogs)
            Debug.Log(LOG + $"UPDATE id={f.id} pos={canvasPos} len(cur/target)={f.currLen:0}/{targetLen:0} angle(cur/target)={f.currAngle:0}/{targetAngle:0}");
    }

    void OnEnded(MultiTouchManager.TouchEvt e)
    {
        if (!fingers.TryGetValue(e.fingerId, out var f)) return;

        if (enableLogs)
            Debug.Log(LOG + $"END id={f.id} hadArrow={(f.arrowUI!=null)} dragging={f.dragging}");

        if (f.arrowUI && f.arrowImg)
        {
            StartCoroutine(FadeAndDestroy(f.arrowImg, f.arrowUI.gameObject, fadeDuration));
        }
        else if (f.arrowUI)
        {
            Destroy(f.arrowUI.gameObject);
        }
        fingers.Remove(e.fingerId);
    }

    // --- FadeOut ---
    System.Collections.IEnumerator FadeAndDestroy(Image img, GameObject go, float dur)
    {
        if (!img) { Destroy(go); yield break; }
        float t = 0f; var c = img.color;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(1f, 0f, t / Mathf.Max(0.001f, dur));
            img.color = c;
            yield return null;
        }
        Destroy(go);
    }

    // --- Target detection ---
    Transform FindTargetAt(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        if (!cam) return null;

        if (use2DPhysics)
        {
            // meilleur picking 2D : OverlapPointAll
            Vector3 wp3 = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            Vector2 p = new Vector2(wp3.x, wp3.y);
            var hits = Physics2D.OverlapPointAll(p);
            Array.Sort(hits, (a,b) => Compare2D(a,b));
            foreach (var h in hits)
            {
                var tr = ResolveRequiredComponentTransform(h.transform);
                if (tr) return tr;
            }
            return null;
        }
        else
        {
            var ray = cam.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, 1000f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var tr = ResolveRequiredComponentTransform(h.transform);
                if (tr) return tr;
            }
            return null;
        }
    }

    int Compare2D(Collider2D a, Collider2D b)
    {
        var ra = a.GetComponent<Renderer>(); var rb = b.GetComponent<Renderer>();
        int la = ra ? ra.sortingLayerID : 0, lb = rb ? rb.sortingLayerID : 0;
        if (la != lb) return SortingLayer.GetLayerValueFromID(lb).CompareTo(SortingLayer.GetLayerValueFromID(la));
        int oa = ra ? ra.sortingOrder : 0, ob = rb ? rb.sortingOrder : 0;
        if (oa != ob) return ob.CompareTo(oa);
        return a.transform.position.z.CompareTo(b.transform.position.z);
    }

    Transform ResolveRequiredComponentTransform(Transform start)
    {
        Transform t = start;
        do
        {
            if (HasAnyRequiredComponent(t.gameObject)) return t;
            t = climbToParentWithRequiredComponent ? t.parent : null;
        } while (t != null);
        return null;
    }

    bool HasAnyRequiredComponent(GameObject go)
    {
        foreach (var name in requiredComponentTypeNames)
        {
            var type = ResolveTypeByName(name);
            if (type != null && go.GetComponent(type) != null) return true;
        }
        return false;
    }

    Type ResolveTypeByName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return null;
        if (_cachedTypes.TryGetValue(typeName, out var cached)) return cached;

        Type found = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            found = asm.GetTypes().FirstOrDefault(tt =>
                string.Equals(tt.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tt.FullName, typeName, StringComparison.OrdinalIgnoreCase));
            if (found != null) break;
        }
        _cachedTypes[typeName] = found;
        return found;
    }

    // helpers pour détecter une surface de dessin sous le doigt
    bool HasAnyOfTypesInHierarchy(GameObject go, string[] typeNames)
    {
        if (go == null || typeNames == null) return false;
        Transform t = go.transform;
        while (t != null)
        {
            foreach (var name in typeNames)
            {
                var type = ResolveTypeByName(name);
                if (type != null && t.GetComponent(type) != null) return true;
            }
            t = t.parent;
        }
        return false;
    }

    bool IsOnDrawingSurface(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (!cam) return false;

        if (use2DPhysics)
        {
            Vector3 wp3 = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            Vector2 p = new Vector2(wp3.x, wp3.y);
            var hits = Physics2D.OverlapPointAll(p);
            foreach (var h in hits)
            {
                if (HasAnyOfTypesInHierarchy(h.gameObject, drawingSurfaceTypeNames))
                    return true;
            }
            return false;
        }
        else
        {
            var ray = cam.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, 1000f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (HasAnyOfTypesInHierarchy(h.collider.gameObject, drawingSurfaceTypeNames))
                    return true;
            }
            return false;
        }
    }
}
