using System.Collections.Generic;
using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// One ad unit, with its id per mediation provider. The <see cref="Key"/> matches the
    /// <c>ad_units.&lt;provider&gt;.&lt;key&gt;</c> entry in the Remote Config JSON, which overrides these ids.
    /// </summary>
    [CreateAssetMenu(menuName = "AdsKit/Ad Unit", fileName = "AdUnit")]
    public sealed class AdUnitDefinition : ScriptableObject
    {
        [Tooltip("Remote Config key (e.g. inter_play_button). Defaults to the asset name.")]
        [SerializeField] private string key;

        [SerializeField] private AdFormat format = AdFormat.Interstitial;

        [Tooltip("Ad unit id per provider (max, admob, simulator).")]
        [SerializeField] private List<ProviderAdUnitId> ids = new List<ProviderAdUnitId>();

        public string Key => string.IsNullOrEmpty(key) ? name : key;
        public AdFormat Format => format;
        public IReadOnlyList<ProviderAdUnitId> Ids => ids;

        public string GetId(string provider)
        {
            foreach (var entry in ids)
                if (entry.provider == provider) return entry.androidId;
            return null;
        }

#if UNITY_EDITOR
        public void EditorSetup(string unitKey, AdFormat unitFormat)
        {
            key = unitKey;
            format = unitFormat;
        }

        public void EditorSetId(string provider, string id)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i].provider != provider) continue;
                ids[i] = new ProviderAdUnitId { provider = provider, androidId = id };
                return;
            }
            ids.Add(new ProviderAdUnitId { provider = provider, androidId = id });
        }
#endif
    }
}
