using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Ready-made <see cref="IAdsGameHooks"/>: pauses <see cref="AudioListener"/> and <see cref="Time.timeScale"/>
    /// during full-screen ads. Override what your game does differently.
    /// </summary>
    public abstract class AdsGameHooksBase : IAdsGameHooks
    {
        private float _savedTimeScale = 1f;
        private bool _savedAudioPause;
        private bool _paused;

        public abstract int CurrentLevel { get; }

        public virtual bool AdsRemoved => false;

        public virtual void OnFullscreenAdOpened()
        {
            if (_paused) return;
            _paused = true;
            _savedTimeScale = Time.timeScale;
            _savedAudioPause = AudioListener.pause;
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }

        public virtual void OnFullscreenAdClosed()
        {
            if (!_paused) return;
            _paused = false;
            Time.timeScale = _savedTimeScale;
            AudioListener.pause = _savedAudioPause;
        }

        public virtual void SetAdLoadingIndicator(bool visible) { }

        public virtual void OnRewardedUnavailable(string placementId)
        {
            KitLog.Info("Rewarded unavailable: " + placementId);
        }
    }
}
