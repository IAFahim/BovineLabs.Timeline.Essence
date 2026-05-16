using System;
using BovineLabs.Core.Collections;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Essence;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Essence.Data;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.Essence
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(EntityLinkTargetPatchSystem))]
    public partial struct TimelineEssenceIntrinsicSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount> intrinsicChanges;
        private NativeParallelHashSet<Entity> uniqueKeySet;
        private NativeList<Entity> uniqueKeys;
        private ComponentLookup<Targets> targetsLookup;
        private ComponentLookup<TargetsCustom> customsLookup;
        private UnsafeComponentLookup<EntityLinkSource> linkSourcesLookup;
        private UnsafeBufferLookup<EntityLinkEntry> linksLookup;
        private IntrinsicWriter.Lookup writers;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            intrinsicChanges =
                new NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>(64, Allocator.Persistent);
            uniqueKeySet = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            state.RequireForUpdate<EssenceConfig>();
            targetsLookup = state.GetComponentLookup<Targets>(true);
            customsLookup = state.GetComponentLookup<TargetsCustom>(true);
            linkSourcesLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            linksLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            writers.Create(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            intrinsicChanges.Dispose();
            uniqueKeySet.Dispose();
            uniqueKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            targetsLookup.Update(ref state);
            customsLookup.Update(ref state);
            linkSourcesLookup.Update(ref state);
            linksLookup.Update(ref state);
            writers.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());
            uniqueKeySet.Clear();

            state.Dependency = new GatherJob
            {
                IntrinsicChanges = intrinsicChanges.AsWriter(),
                UniqueKeys = uniqueKeySet.AsParallelWriter(),
                TargetsLookup = targetsLookup,
                CustomsLookup = customsLookup,
                LinkSourcesLookup = linkSourcesLookup,
                LinksLookup = linksLookup
            }.ScheduleParallel(state.Dependency);

            state.Dependency = intrinsicChanges.Apply(state.Dependency, out var reader);

            state.Dependency = new GetKeysJob
            {
                UniqueKeys = uniqueKeys,
                UniqueKeySet = uniqueKeySet
            }.Schedule(state.Dependency);

            state.Dependency = new ApplyJob
            {
                Keys = uniqueKeys.AsDeferredJobArray(),
                GroupChanges = reader,
                Writers = writers
            }.Schedule(uniqueKeys, 64, state.Dependency);

            state.Dependency = intrinsicChanges.Clear(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithDisabled(typeof(ClipActivePrevious))]
        private partial struct GatherJob : IJobEntity
        {
            public NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>.ParallelWriter IntrinsicChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> CustomsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSourcesLookup;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> LinksLookup;

            private void Execute(in TrackBinding binding, in TimelineEssenceIntrinsicData data)
            {
                if (data.Intrinsic.Value == 0 || binding.Value == Entity.Null) return;

                if (TimelineEssenceResolver.TryResolveTarget(data.RouteTo, data.RouteLinkKey, binding.Value, TargetsLookup, CustomsLookup, LinkSourcesLookup, LinksLookup, out var target))
                {
                    IntrinsicChanges.Add(target, new IntrinsicAmount(data.Intrinsic, data.Amount));
                    UniqueKeys.Add(target);
                }
            }
        }

        [BurstCompile]
        private struct GetKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelHashSet<Entity> UniqueKeySet;

            public void Execute()
            {
                UniqueKeys.Clear();
                foreach (var key in UniqueKeySet)
                    UniqueKeys.Add(key);
            }
        }

        [BurstCompile]
        private struct ApplyJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public IntrinsicWriter.Lookup Writers;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!Writers.TryGet(key, out var writer))) return;

                var values = new FixedList4096Bytes<IntrinsicAmount>();

                if (GroupChanges.TryGetFirstValue(key, out var value, out var it))
                {
                    AddOrAccumulate(ref values, value, ref writer);

                    while (GroupChanges.TryGetNextValue(out value, ref it))
                        AddOrAccumulate(ref values, value, ref writer);
                }

                foreach (var i in values) writer.Add(i.Intrinsic, i.Amount);
            }

            private static void AddOrAccumulate(ref FixedList4096Bytes<IntrinsicAmount> values, IntrinsicAmount value,
                ref IntrinsicWriter writer)
            {
                for (var i = 0; i < values.Length; i++)
                    if (values[i].Intrinsic.Equals(value.Intrinsic))
                    {
                        var existing = values[i];
                        existing.Amount += value.Amount;
                        values[i] = existing;
                        return;
                    }

                if (values.Length < values.Capacity)
                {
                    values.Add(value);
                    return;
                }

                writer.Add(value.Intrinsic, value.Amount);
            }
        }

        private struct IntrinsicAmount : IEquatable<IntrinsicAmount>
        {
            public readonly IntrinsicKey Intrinsic;
            public int Amount;

            public IntrinsicAmount(IntrinsicKey intrinsic, int amount)
            {
                Intrinsic = intrinsic;
                Amount = amount;
            }

            public bool Equals(IntrinsicAmount other)
            {
                return Intrinsic.Equals(other.Intrinsic);
            }

            public override int GetHashCode()
            {
                return Intrinsic.GetHashCode();
            }
        }
    }
}