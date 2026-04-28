using System;
using BovineLabs.Core.Collections;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Essence;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Essence.Data;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.Essence
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct TimelineEssenceIntrinsicSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount> intrinsicChanges;
        private NativeList<Entity> uniqueKeys;
        private ComponentLookup<Targets> targetsLookup;
        private ComponentLookup<TargetsCustom> customsLookup;
        private IntrinsicWriter.Lookup writers;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            intrinsicChanges = new NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>(64, Allocator.Persistent);
            uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            targetsLookup = state.GetComponentLookup<Targets>(true);
            customsLookup = state.GetComponentLookup<TargetsCustom>(true);
            writers.Create(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            intrinsicChanges.Dispose();
            uniqueKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            targetsLookup.Update(ref state);
            customsLookup.Update(ref state);
            writers.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());

            state.Dependency = new GatherJob
            {
                IntrinsicChanges = intrinsicChanges.AsWriter(),
                TargetsLookup = targetsLookup,
                CustomsLookup = customsLookup
            }.ScheduleParallel(state.Dependency);

            state.Dependency = intrinsicChanges.Apply(state.Dependency, out var reader);

            state.Dependency = new GetKeysJob
            {
                UniqueKeys = uniqueKeys,
                GroupChanges = reader
            }.Schedule(state.Dependency);

            state.Dependency = new ApplyJob
            {
                Keys = uniqueKeys,
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
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> CustomsLookup;

            private void Execute(in TrackBinding binding, in TimelineEssenceIntrinsicData data)
            {
                if (data.Intrinsic.Value == 0 || binding.Value == Entity.Null) return;

                if (TimelineEssenceResolver.TryResolveTarget(data.RouteTo, binding.Value, TargetsLookup, CustomsLookup, out var target))
                {
                    IntrinsicChanges.Add(target, new IntrinsicAmount(data.Intrinsic, data.Amount));
                }
            }
        }

        [BurstCompile]
        private struct GetKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;

            public void Execute() => GroupChanges.GetUniqueKeyArray(UniqueKeys);
        }

        [BurstCompile]
        private struct ApplyJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeList<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public IntrinsicWriter.Lookup Writers;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!Writers.TryGet(key, out var writer))) return;

                var values = new NativeList<IntrinsicAmount>(Allocator.Temp);
                GroupChanges.TryGetFirstValue(key, out var value, out var it);
                values.Add(value);

                while (GroupChanges.TryGetNextValue(out value, ref it))
                {
                    var existingIndex = values.IndexOf(value);
                    if (Hint.Unlikely(existingIndex == -1)) values.Add(value);
                    else values.ElementAt(existingIndex).Amount += value.Amount;
                }

                foreach (var i in values) writer.Add(i.Intrinsic, i.Amount);
                values.Dispose();
            }
        }

        private struct IntrinsicAmount : IEquatable<IntrinsicAmount>
        {
            public readonly IntrinsicKey Intrinsic;
            public int Amount;

            public IntrinsicAmount(IntrinsicKey intrinsic, int amount) { Intrinsic = intrinsic; Amount = amount; }
            public bool Equals(IntrinsicAmount other) => Intrinsic.Equals(other.Intrinsic);
            public override int GetHashCode() => Intrinsic.GetHashCode();
        }
    }
}
