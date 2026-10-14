using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Keeps a full-screen UI rect clear of the bottom banner: its bottom edge moves up by the real banner height
    /// (only while a banner is visible). Put it on the root RectTransform of each screen / UI layer.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class AdsBannerInset : MonoBehaviour
    {
        private RectTransform _rect;
        private Canvas _canvas;

        private void OnEnable()
        {
            _rect = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
            Ads.BannerHeightChanged += Apply;
            Apply(Ads.BannerHeightPixels);
        }

        private void OnDisable() => Ads.BannerHeightChanged -= Apply;

        private void Apply(float pixels)
        {
            if (_rect == null) return;
            var scale = _canvas != null && _canvas.rootCanvas.scaleFactor > 0f ? _canvas.rootCanvas.scaleFactor : 1f;
            _rect.offsetMin = new Vector2(_rect.offsetMin.x, pixels / scale);
        }
    }
}
