using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// Affiche un point sous chaque doigt pendant le contact + un ripple à l’appui.
/// Dépend uniquement de MultiTouchManager (pas des scripts de geste).
public class TouchContactVisualizer : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Prefab UI (Image/RectTransform) pour le point de contact. 32x32 conseillé.")]
    public RectTransform dotPrefab;

    [Tooltip("Prefab UI optionnel pour un ripple (Image). S’il est nul, pas de ripple.")]
    public RectTransform ripplePrefab;

    [Tooltip("Canvas cible (Screen Space – Overlay recommandé).")]
    public Canvas canvas;

    [Header("Look")]
    [Tooltip("Léger pulse du dot pendant le contact.")]
    public bool pulseWhileHeld = true;
    [Range(0f, 1f)] public float pulseAmplitude = 0.08f;
    public float pulseSpeed = 14f;

    // runtime
    private class Finger
    {
        public RectTransform dot;
        public RectTransform ripple; // instancié uniquement à Began, auto-fade
    }

    private readonly Dictionary<int, Finger> _fingers = new();
    private MultiTouchManager _mt;
    private bool _ready;

    void Awake()
    {
        if (!canvas) canvas = GetComponentInParent<Canvas>();
        if (!canvas) canvas = FindObjectOfType<Canvas>();
        if (!dotPrefab) { Debug.LogError("[TouchContactVisualizer] dotPrefab manquant."); return; }

        _mt = MultiTouchManager.Instance;
        if (_mt == null) { Debug.LogError("[TouchContactVisualizer] MultiTouchManager.Instance est null."); return; }

        _ready = true;
    }

    void OnEnable()
    {
        if (!_ready) return;
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

        foreach (var kv in _fingers)
        {
            if (kv.Value.dot)    Destroy(kv.Value.dot.gameObject);
            if (kv.Value.ripple) Destroy(kv.Value.ripple.gameObject);
        }
        _fingers.Clear();
    }

    void Update()
    {
        if (!pulseWhileHeld) return;
        float t = Time.time * pulseSpeed;
        foreach (var kv in _fingers)
        {
            var f = kv.Value;
            if (!f.dot) continue;
            float s = 1f + pulseAmplitude * Mathf.Sin(t + kv.Key * 0.37f);
            f.dot.localScale = new Vector3(s, s, 1f);
        }
    }

    // --- MultiTouch events ---
    void OnBegan(MultiTouchManager.TouchEvt e)
    {
        if (!_ready) return;

        var dot = Instantiate(dotPrefab, canvas.transform);
        dot.gameObject.SetActive(true);
        dot.SetAsLastSibling();

        // position initiale
        dot.anchoredPosition = ScreenToCanvas(e.position);

        // ripple one-shot
        RectTransform ripple = null;
        if (ripplePrefab)
        {
            ripple = Instantiate(ripplePrefab, canvas.transform);
            ripple.gameObject.SetActive(true);
            ripple.SetAsFirstSibling(); // sous le dot
            ripple.anchoredPosition = dot.anchoredPosition;

            var fx = ripple.gameObject.GetComponent<UIRipple>() ?? ripple.gameObject.AddComponent<UIRipple>();
            fx.Play(duration: 0.35f, startScale: 0.6f, endScale: 1.8f, startAlpha: 0.35f, onDone: () =>
            {
                if (ripple) Destroy(ripple.gameObject);
            });
        }

        _fingers[e.fingerId] = new Finger { dot = dot, ripple = ripple };
    }

    void OnMoved(MultiTouchManager.TouchEvt e)
    {
        if (!_fingers.TryGetValue(e.fingerId, out var f)) return;
        f.dot.anchoredPosition = ScreenToCanvas(e.position);
        if (f.ripple) f.ripple.anchoredPosition = f.dot.anchoredPosition; // si ripple encore en vie
    }

    void OnEnded(MultiTouchManager.TouchEvt e)
    {
        if (!_fingers.TryGetValue(e.fingerId, out var f)) return;
        if (f.dot) Destroy(f.dot.gameObject);
        if (f.ripple) Destroy(f.ripple.gameObject);
        _fingers.Remove(e.fingerId);
    }

    // --- helpers ---
    Vector2 ScreenToCanvas(Vector2 screenPos)
    {
        Camera uiCam = null;
        if (canvas.renderMode == RenderMode.ScreenSpaceCamera || canvas.renderMode == RenderMode.WorldSpace)
            uiCam = canvas.worldCamera;

        var canvasRT = canvas.transform as RectTransform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, uiCam, out var lp);
        return lp;
    }
}
