using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.Essence
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(EntityLinkTargetPatchSystem))]
    public partial struct TimelineEssenceStatSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var addStats = new NativeQueue<StatMutation>(state.WorldUpdateAllocator);
            var removeStats = new NativeQueue<StatMutation>(state.WorldUpdateAllocator);

            var targetsLookup = SystemAPI.GetComponentLookup<Targets>(true);
            var customsLookup = SystemAPI.GetComponentLookup<TargetsCustom>(true);
            var linkSourcesLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            var linksLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);

            var gatherAddJob = new GatherAddJob
            {
                Mutations = addStats.AsParallelWriter(),
                TargetsLookup = targetsLookup,
                CustomsLookup = customsLookup,
                LinkSourcesLookup = linkSourcesLookup,
                LinksLookup = linksLookup
            };
            var gatherRemoveJob = new GatherRemoveJob
            {
                Mutations = removeStats.AsParallelWriter(),
                TargetsLookup = targetsLookup,
                CustomsLookup = customsLookup,
                LinkSourcesLookup = linkSourcesLookup,
                LinksLookup = linksLookup
            };

            state.Dependency = JobHandle.CombineDependencies(
                gatherAddJob.ScheduleParallel(state.Dependency),
                gatherRemoveJob.ScheduleParallel(state.Dependency)
            );

            state.Dependency = new ApplyJob
            {
                Adds = addStats,
                Removes = removeStats,
                StatModifiers = SystemAPI.GetBufferLookup<StatModifiers>(),
                StatChangeds = SystemAPI.GetComponentLookup<StatChanged>()
            }.Schedule(state.Dependency);
        }

        private struct StatMutation
        {
            public Entity Target;
            public Entity Source;
            public StatModifier Modifier;
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithDisabled(typeof(ClipActivePrevious))]
        private partial struct GatherAddJob : IJobEntity
        {
            public NativeQueue<StatMutation>.ParallelWriter Mutations;
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> CustomsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSourcesLookup;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> LinksLookup;

            private void Execute(Entity clipEntity, in TrackBinding binding, in TimelineEssenceStatData data)
            {
                if (data.Stat.Value == 0 || binding.Value == Entity.Null) return;

                if (TimelineEssenceResolver.TryResolveTarget(data.RouteTo, data.RouteLinkKey, binding.Value, TargetsLookup, CustomsLookup, LinkSourcesLookup, LinksLookup, out var target))
                {
                    var modifier = new StatModifier { Type = data.Stat, ModifyType = data.ModifyType };

                    if (data.ModifyType == StatModifyType.Added)
                        modifier.Value = (int)data.Value;
                    else
                        modifier.ValueFloat = data.Value;

                    Mutations.Enqueue(new StatMutation
                    {
                        Target = target,
                        Source = clipEntity,
                        Modifier = modifier
                    });
                }
            }
        }

        [BurstCompile]
        [WithDisabled(typeof(ClipActive))]
        [WithAll(typeof(ClipActivePrevious))]
        private partial struct GatherRemoveJob : IJobEntity
        {
            public NativeQueue<StatMutation>.ParallelWriter Mutations;
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> CustomsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSourcesLookup;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> LinksLookup;

            private void Execute(Entity clipEntity, in TrackBinding binding, in TimelineEssenceStatData data)
            {
                if (data.Stat.Value == 0 || binding.Value == Entity.Null) return;

                if (TimelineEssenceResolver.TryResolveTarget(data.RouteTo, data.RouteLinkKey, binding.Value, TargetsLookup, CustomsLookup, LinkSourcesLookup, LinksLookup, out var target))
                    Mutations.Enqueue(new StatMutation { Target = target, Source = clipEntity });
            }
        }

        [BurstCompile]
        private struct ApplyJob : IJob
        {
            public NativeQueue<StatMutation> Adds;
            public NativeQueue<StatMutation> Removes;
            public BufferLookup<StatModifiers> StatModifiers;
            public ComponentLookup<StatChanged> StatChangeds;

            public void Execute()
            {
                while (Removes.TryDequeue(out var remove))
                {
                    if (!StatModifiers.TryGetBuffer(remove.Target, out var buffer)) continue;

                    StatChangeds.SetComponentEnabled(remove.Target, true);
                    var array = buffer.AsNativeArray();
                    for (var i = array.Length - 1; i >= 0; i--)
                        if (array[i].SourceEntity == remove.Source)
                        {
                            buffer.RemoveAtSwapBack(i);
                            break; // 1 modifier per clip entity
                        }
                }

                while (Adds.TryDequeue(out var add))
                {
                    if (!StatModifiers.TryGetBuffer(add.Target, out var buffer)) continue;

                    StatChangeds.SetComponentEnabled(add.Target, true);
                    buffer.Add(new StatModifiers
                    {
                        SourceEntity = add.Source,
                        Value = add.Modifier
                    });
                }
            }
        }
    }
}