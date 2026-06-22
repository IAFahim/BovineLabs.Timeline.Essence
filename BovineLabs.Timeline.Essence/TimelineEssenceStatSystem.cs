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
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct TimelineEssenceStatSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _linkSourceLookup;
        private UnsafeBufferLookup<EntityLinkEntry> _linkLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _linkSourceLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _linkLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var addStats = new NativeQueue<StatMutation>(state.WorldUpdateAllocator);
            var removeStats = new NativeQueue<StatMutation>(state.WorldUpdateAllocator);

            _targetsLookup.Update(ref state);
            _linkSourceLookup.Update(ref state);
            _linkLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var gatherAddJob = new GatherAddJob
            {
                Mutations = addStats.AsParallelWriter(),
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup
            };
            var gatherRemoveJob = new GatherRemoveJob
            {
                Mutations = removeStats.AsParallelWriter()
            };

            state.Dependency = gatherRemoveJob.ScheduleParallel(state.Dependency);
            state.Dependency = gatherAddJob.ScheduleParallel(state.Dependency);

            // Track each clip entity's currently-applied modifier in an ICleanupComponentData. If a clip entity is
            // destroyed while still active (director/sub-scene torn down before the deactivation edge fires), neither
            // GatherAddJob nor GatherRemoveJob runs the balancing remove and the modifier would leak onto the live
            // target forever. The cleanup component survives the destroy as a zombie, letting GatherDestroyedJob
            // reclaim the orphaned modifier by SourceEntity = the (now-dead) clip entity.
            state.Dependency = new AttachCleanupJob { ECB = ecb }.ScheduleParallel(state.Dependency);
            state.Dependency = new SyncCleanupJob().ScheduleParallel(state.Dependency);
            state.Dependency = new GatherDestroyedJob
            {
                Mutations = removeStats.AsParallelWriter(),
                ECB = ecb
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new ApplyJob
            {
                Adds = addStats,
                Removes = removeStats,
                StatModifiers = SystemAPI.GetBufferLookup<StatModifiers>(),
                StatChangeds = SystemAPI.GetComponentLookup<StatChanged>()
            }.Schedule(state.Dependency);
        }

        // Lingers on a destroyed clip entity as a zombie so the orphaned StatModifier can be reclaimed from the still
        // -live target. Target mirrors TimelineEssenceStatState.AppliedTarget (Entity.Null when nothing is applied).
        private struct StatModifierCleanup : ICleanupComponentData
        {
            public Entity Target;
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
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;

            private void Execute(Entity clipEntity, in TrackBinding binding, in TimelineEssenceStatData data,
                ref TimelineEssenceStatState state)
            {
                if (data.Stat.Value == 0 || binding.Value == Entity.Null) return;

                if (TimelineEssenceResolver.TryResolveLinkedTarget(data.RouteTo, data.RouteLinkKey, binding.Value,
                        TargetsLookup, LinkSources, Links, out var target))
                {
                    var modifier = new StatModifier { Type = data.Stat, ModifyType = data.ModifyType };

                    if (data.ModifyType == StatModifyType.Added)
                        modifier.Value = (int)data.Value;
                    else
                        modifier.ValueFloat = data.Value;

                    state.AppliedTarget = target;

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

            private void Execute(Entity clipEntity, in TrackBinding binding, in TimelineEssenceStatData data,
                in TimelineEssenceStatState state)
            {
                if (data.Stat.Value == 0 || binding.Value == Entity.Null) return;

                if (state.AppliedTarget == Entity.Null) return;

                Mutations.Enqueue(new StatMutation { Target = state.AppliedTarget, Source = clipEntity });
            }
        }

        // Attaches the cleanup marker to every stat clip entity once, seeding it with the current applied target so
        // reclamation is correct even if the entity is destroyed the same frame the marker is added.
        [BurstCompile]
        [WithNone(typeof(StatModifierCleanup))]
        private partial struct AttachCleanupJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;

            private void Execute([EntityIndexInQuery] int sortKey, Entity clipEntity, in TimelineEssenceStatState state)
            {
                ECB.AddComponent(sortKey, clipEntity, new StatModifierCleanup { Target = state.AppliedTarget });
            }
        }

        // Keeps the zombie-surviving marker in sync with the live applied target every frame.
        [BurstCompile]
        private partial struct SyncCleanupJob : IJobEntity
        {
            private void Execute(in TimelineEssenceStatState state, ref StatModifierCleanup cleanup)
            {
                cleanup.Target = state.AppliedTarget;
            }
        }

        // Runs on clip entities that were destroyed while a modifier was still applied: the cleanup marker outlives the
        // entity's IComponentData, so TimelineEssenceStatState is gone. Reclaim the orphaned modifier from the target
        // (no-op if it was already removed by a normal deactivation edge) and release the zombie.
        [BurstCompile]
        [WithNone(typeof(TimelineEssenceStatState))]
        private partial struct GatherDestroyedJob : IJobEntity
        {
            public NativeQueue<StatMutation>.ParallelWriter Mutations;
            public EntityCommandBuffer.ParallelWriter ECB;

            private void Execute([EntityIndexInQuery] int sortKey, Entity clipEntity, in StatModifierCleanup cleanup)
            {
                if (cleanup.Target != Entity.Null)
                    Mutations.Enqueue(new StatMutation { Target = cleanup.Target, Source = clipEntity });

                ECB.RemoveComponent<StatModifierCleanup>(sortKey, clipEntity);
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
                            break;
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