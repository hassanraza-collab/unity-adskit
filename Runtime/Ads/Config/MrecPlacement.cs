using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>
    /// The 300x250 MREC (e.g. on the exit panel). The game places it next to a RectTransform with
    /// <see cref="Ads.ShowMrec"/> and hides it with <see cref="Ads.HideMrec"/>. Remote block: mrec
    /// </summary>
    [CreateAssetMenu(menuName = "AdsKit/Placements/MREC", fileName = "Mrec")]
    public sealed class MrecPlacement : AdPlacement
    {
        public AdUnitDefinition adUnit;

        public override AdFormat Format => AdFormat.Mrec;
    }
}
