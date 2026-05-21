using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    [Serializable]
    [TrackClipType(typeof(TimelineEssenceIntrinsicClip))]
    [TrackColor(0.2f, 0.6f, 0.9f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Essence/Timeline Intrinsic")]
    public sealed class TimelineEssenceIntrinsicTrack : DOTSTrack
    {
    }
}