using System;
using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>IMGUI drawing for the simulator: a full-screen fake ad and a fake banner strip.</summary>
    public sealed class SimulatorAdOverlay : MonoBehaviour
    {
        private static SimulatorAdOverlay _instance;

        private bool _fullscreen;
        private AdFormat _format;
        private string _placement;
        private double _openedAt;
        private float _closeDelay;
        private Action<bool> _onClosed;

        private bool _bannerVisible;
        private float _bannerHeight;
        private BannerPosition _bannerPosition;
        private string _bannerLabel;

        private GUIStyle _title;
        private GUIStyle _button;
        private Texture2D _dim;
        private Texture2D _bannerTex;

        public static SimulatorAdOverlay Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("[AdsKit Simulator]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SimulatorAdOverlay>();
                return _instance;
            }
        }

        public void ShowFullscreen(AdFormat format, string placement, float closeDelay, Action<bool> onClosed)
        {
            _fullscreen = true;
            _format = format;
            _placement = placement;
            _closeDelay = closeDelay;
            _openedAt = KitRunner.Now;
            _onClosed = onClosed;
        }

        public void SetBanner(bool visible, float heightPixels, BannerPosition position, string label)
        {
            _bannerVisible = visible;
            _bannerHeight = heightPixels;
            _bannerPosition = position;
            _bannerLabel = label;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnGUI()
        {
            if (!_fullscreen && !_bannerVisible) return;
            EnsureStyles();
            GUI.depth = -1000;

            if (_bannerVisible)
            {
                var y = _bannerPosition == BannerPosition.Bottom ? Screen.height - _bannerHeight : 0f;
                var rect = new Rect(0, y, Screen.width, _bannerHeight);
                GUI.DrawTexture(rect, _bannerTex);
                GUI.Label(rect, "Simulated " + _bannerLabel + " banner", _title);
            }

            if (!_fullscreen) return;
            var full = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(full, _dim);
            var elapsed = (float)(KitRunner.Now - _openedAt);
            var canClose = elapsed >= _closeDelay;
            var name = _format == AdFormat.Rewarded ? "REWARDED" : _format == AdFormat.AppOpen ? "APP OPEN" : "INTERSTITIAL";
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, Screen.height * 0.15f),
                "Simulated " + name + "\n" + _placement, _title);

            var w = Screen.width * 0.6f;
            var h = Screen.height * 0.07f;
            var x = (Screen.width - w) * 0.5f;
            if (_format == AdFormat.Rewarded)
            {
                var label = canClose ? "Close (reward earned)" : "Reward in " + Mathf.CeilToInt(_closeDelay - elapsed) + "s";
                GUI.enabled = canClose;
                if (GUI.Button(new Rect(x, Screen.height * 0.55f, w, h), label, _button)) Close(true);
                GUI.enabled = true;
                if (GUI.Button(new Rect(x, Screen.height * 0.55f + h * 1.3f, w, h), "Skip (no reward)", _button)) Close(false);
            }
            else
            {
                GUI.enabled = canClose;
                var label = canClose ? "Close" : "Close in " + Mathf.CeilToInt(_closeDelay - elapsed) + "s";
                if (GUI.Button(new Rect(x, Screen.height * 0.55f, w, h), label, _button)) Close(false);
                GUI.enabled = true;
            }
        }

        private void Close(bool rewarded)
        {
            _fullscreen = false;
            var cb = _onClosed;
            _onClosed = null;
            cb?.Invoke(rewarded);
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            var size = Mathf.Max(14, Screen.height / 40);
            _title = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = size,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _button = new GUIStyle(GUI.skin.button) { fontSize = size };
            _dim = MakeTex(new Color(0.08f, 0.1f, 0.18f, 0.97f));
            _bannerTex = MakeTex(new Color(0.15f, 0.45f, 0.85f, 1f));
        }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
