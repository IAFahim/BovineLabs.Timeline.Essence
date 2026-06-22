using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    [Serializable]
    [TrackClipType(typeof(TimelineEssenceTickClip))]
    [TrackColor(0.2f, 0.6f, 0.9f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Essence/Timeline Tick")]
    public sealed class TimelineEssenceTickTrack : DOTSTrack
    {
    }
}