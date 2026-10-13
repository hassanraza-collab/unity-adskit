using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// Base for every placement asset. The <see cref="Id"/> is what the game passes to <see cref="Ads"/>
    /// and what the Remote Config JSON uses to override the values below.
    /// </summary>
    public abstract class AdPlacement : ScriptableObject
    {
        [Tooltip("Placement id used in code and Remote Config. Defaults to the asset name.")]
        [SerializeField] private string id;

        [Tooltip("Remote key: enabled")]
        public bool enabled = true;

        [Tooltip("Show only from this session number on (1 = first session). Remote key: min_sessions")]
        [Min(0)] public int minSessions;

        [Tooltip("Show only once the player has reached this level. Remote key: min_level")]
        [Min(0)] public int minLevel;

        public string Id => string.IsNullOrEmpty(id) ? name : id;

        public abstract AdFormat Format { get; }

#if UNITY_EDITOR
        public void EditorSetId(string placementId) => id = placementId;
#endif
    }
}
