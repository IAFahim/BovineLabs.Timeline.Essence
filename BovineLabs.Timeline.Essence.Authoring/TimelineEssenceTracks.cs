using System;
using System.ComponentModel;
using BovineLabs.Essence.Authoring;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    [Serializable]
    [TrackClipType(typeof(TimelineEssenceEventClip))]
    [TrackBindingType(typeof(StatAuthoring))]
    [TrackColor(0.9f, 0.4f, 0.2f)]
    [DisplayName("BovineLabs/Essence/Timeline Event")]
    public sealed class TimelineEssenceEventTrack : DOTSTrack {}
}
