using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Essence.Authoring
{
    [Serializable]
    [TrackClipType(typeof(TickDistributionClip))]
    [TrackColor(0.2f, 0.8f, 0.4f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Timeline/Essence/Tick Distribution")]
    public class TickDistributionTrack : DOTSTrack
    {
    }
}