using System;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class UIRipple : MonoBehaviour
{
    Image _img;
    float _t, _dur, _startAlpha, _endAlpha = 0f, _startScale, _endScale;
    Action _onDone;
    bool _playing;

    void Awake() { _img = GetComponent<Image>(); }

    public void Play(float duration, float startScale, float endScale, float startAlpha, Action onDone = null)
    {
        _dur = Mathf.Max(0.01f, duration);
        _startScale = startScale;
        _endScale = endScale;
        _startAlpha = Mathf.Clamp01(startAlpha);
        _onDone = onDone;
        _t = 0f;
        _playing = true;

        transform.localScale = Vector3.one * _startScale;
        var c = _img.color; c.a = _startAlpha; _img.color = c;
    }

    void Update()
    {
        if (!_playing) return;
        _t += Time.unscaledDeltaTime;
        float u = Mathf.Clamp01(_t / _dur);

        // ease-out
        float e = 1f - Mathf.Pow(1f - u, 3f);

        transform.localScale = Vector3.one * Mathf.Lerp(_startScale, _endScale, e);
        var c = _img.color; c.a = Mathf.Lerp(_startAlpha, _endAlpha, e); _img.color = c;

        if (_t >= _dur)
        {
            _playing = false;
            _onDone?.Invoke();
        }
    }
}
