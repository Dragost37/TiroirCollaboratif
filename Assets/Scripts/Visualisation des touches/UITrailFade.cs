using System;
using UnityEngine;
using UnityEngine.UI;

public class UITrailFade : MonoBehaviour
{
    private float _t, _life;
    private Vector2 _scaleStartEnd;
    private Action _onDone;
    private Image _img;
    private RectTransform _rt;
    private float _startAlpha;

    public void Play(float lifetime, Vector2 scaleStartEnd, Action onDone)
    {
        _life = Mathf.Max(0.01f, lifetime);
        _scaleStartEnd = scaleStartEnd;
        _onDone = onDone;

        if (!_rt) _rt = transform as RectTransform;
        if (!_img) _img = GetComponent<Image>();

        if (_img) _startAlpha = _img.color.a;

        _t = 0f;
        enabled = true;
    }

    void OnEnable()
    {
        if (!_rt) _rt = transform as RectTransform;
        if (!_img) _img = GetComponent<Image>();
    }

    void Update()
    {
        _t += Time.unscaledDeltaTime; // UI → souvent mieux en unscaled
        float u = Mathf.Clamp01(_t / _life);

        // scale
        float s = Mathf.Lerp(_scaleStartEnd.x, _scaleStartEnd.y, u);
        _rt.localScale = new Vector3(s, s, 1f);

        // alpha
        if (_img)
        {
            var c = _img.color;
            c.a = Mathf.Lerp(_startAlpha, 0f, u);
            _img.color = c;
        }

        if (_t >= _life)
        {
            enabled = false;
            _onDone?.Invoke();
        }
    }
}
