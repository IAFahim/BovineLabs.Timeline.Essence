using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    [Serializable]
    [TrackClipType(typeof(TimelineEssenceStatClip))]
    [TrackColor(0.2f, 0.9f, 0.4f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Essence/Timeline Stat")]
    public sealed class TimelineEssenceStatTrack : DOTSTrack
    {
    }
}