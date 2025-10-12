using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class TouchVisualizer : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Image UI (RectTransform) à instancier pour chaque doigt (désactivée par défaut).")]
    public RectTransform touchPrefab;

    [Tooltip("Prefab du petit point de traînée (Image UI). Optionnel : si nul, pas de traînée.")]
    public RectTransform trailPrefab;

    [Tooltip("Canvas cible. Laisser vide pour auto-détection (parent puis FindObjectOfType).")]
    public Canvas canvas;

    [Header("Trail")]
    [Tooltip("Laisser ~10–24 px pour une traînée fluide sans surcoût.")]
    public float trailEveryPixels = 16f;
    [Tooltip("Durée de vie d’un élément de traînée (sec).")]
    public float trailLifetime = 0.35f;
    [Tooltip("Échelle initiale → finale pendant le fade.")]
    public Vector2 trailScaleStartEnd = new Vector2(1f, 0.6f);
    [Tooltip("Alpha initial (0..1). L’alpha finale est 0.")]
    [Range(0f, 1f)] public float trailStartAlpha = 0.9f;
    [Tooltip("Nombre max d’éléments de traînée par doigt (pour limiter le surcoût). 0 = illimité.")]
    public int maxTrailPerFinger = 32;

    // per-finger runtime data
    private class FingerData
    {
        public RectTransform dot;
        public Vector2 lastTrailPosCanvas;
        public int trailCount;
    }

    private readonly Dictionary<int, FingerData> fingers = new Dictionary<int, FingerData>();
    private MultiTouchManager _mt;
    private bool _ready;

    // simple pool for trails to avoid GC
    private readonly Stack<RectTransform> pool = new Stack<RectTransform>();

    void Awake()
    {
        if (!canvas) canvas = GetComponentInParent<Canvas>();
        if (!canvas) canvas = FindObjectOfType<Canvas>();

        if (!touchPrefab)
        {
            Debug.LogError("[TouchVisualizer] 'touchPrefab' n'est pas assigné."); return;
        }
        if (!canvas)
        {
            Debug.LogError("[TouchVisualizer] Aucun Canvas trouvé/assigné."); return;
        }

        _mt = MultiTouchManager.Instance;
        if (_mt == null)
        {
            Debug.LogError("[TouchVisualizer] MultiTouchManager.Instance est null."); return;
        }

        _ready = true;
    }

    void OnEnable()
    {
        if (!_ready) return;
        _mt.OnTouchBegan += Began;
        _mt.OnTouchMoved += Moved;
        _mt.OnTouchEnded += Ended;
    }

    void OnDisable()
    {
        if (_mt != null)
        {
            _mt.OnTouchBegan -= Began;
            _mt.OnTouchMoved -= Moved;
            _mt.OnTouchEnded -= Ended;
        }
        foreach (var kv in fingers)
            if (kv.Value.dot) Destroy(kv.Value.dot.gameObject);
        fingers.Clear();

        while (pool.Count > 0) Destroy(pool.Pop()?.gameObject);
    }

    void Began(MultiTouchManager.TouchEvt e)
    {
        if (!_ready) return;

        var dot = Instantiate(touchPrefab, canvas.transform);
        dot.gameObject.SetActive(true);
        dot.SetAsLastSibling(); // au-dessus

        var fd = new FingerData { dot = dot, trailCount = 0 };
        fingers[e.fingerId] = fd;

        // position initiale + init origin for trail spacing
        MoveAndMaybeTrail(fd, e.position, forceTrailOrigin: true);
    }

    void Moved(MultiTouchManager.TouchEvt e)
    {
        if (!_ready) return;
        if (!fingers.TryGetValue(e.fingerId, out var fd)) return;

        MoveAndMaybeTrail(fd, e.position, forceTrailOrigin: false);
    }

    void Ended(MultiTouchManager.TouchEvt e)
    {
        if (!fingers.TryGetValue(e.fingerId, out var fd)) return;

        if (fd.dot) Destroy(fd.dot.gameObject);
        fingers.Remove(e.fingerId);
    }

    // ---------- helpers ----------
    void MoveAndMaybeTrail(FingerData fd, Vector2 screenPos, bool forceTrailOrigin)
    {
        if (!canvas) return;

        // camera for conversion
        Camera uiCam = null;
        if (canvas.renderMode == RenderMode.ScreenSpaceCamera || canvas.renderMode == RenderMode.WorldSpace)
            uiCam = canvas.worldCamera;

        var canvasRT = canvas.transform as RectTransform;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, uiCam, out var lp))
        {
            // move main dot
            fd.dot.anchoredPosition = lp;

            // trail
            if (trailPrefab)
            {
                if (forceTrailOrigin) fd.lastTrailPosCanvas = lp;

                float dist = Vector2.Distance(lp, fd.lastTrailPosCanvas);
                if (dist >= trailEveryPixels && (maxTrailPerFinger <= 0 || fd.trailCount < maxTrailPerFinger))
                {
                    SpawnTrail(lp);
                    fd.lastTrailPosCanvas = lp;
                    fd.trailCount++;
                }
            }
        }
    }

    void SpawnTrail(Vector2 anchoredPos)
    {
        // pool or new
        RectTransform tr = (pool.Count > 0) ? pool.Pop() : Instantiate(trailPrefab, canvas.transform);
        tr.gameObject.SetActive(true);
        tr.SetAsFirstSibling();  // derrière le dot (option : FirstSibling)
        tr.anchoredPosition = anchoredPos;
        tr.localScale = Vector3.one * trailScaleStartEnd.x;

        // set initial color alpha if there's an Image
        var img = tr.GetComponent<Image>();
        if (img)
        {
            var c = img.color; c.a = trailStartAlpha; img.color = c;
        }

        // launch fade
        var fader = tr.GetComponent<UITrailFade>();
        if (!fader) fader = tr.gameObject.AddComponent<UITrailFade>();
        fader.Play(trailLifetime, trailScaleStartEnd, () =>
        {
            tr.gameObject.SetActive(false);
            pool.Push(tr);
        });
    }
}
