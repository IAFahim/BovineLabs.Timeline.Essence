using System;
using BovineLabs.Core;
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
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct TimelineEssenceIntrinsicSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount> _intrinsicChanges;
        private NativeParallelHashSet<Entity> _uniqueKeySet;
        private NativeList<Entity> _uniqueKeys;

        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _linkSourceLookup;
        private UnsafeBufferLookup<EntityLinkEntry> _linkLookup;
        private IntrinsicWriter.SingletonData _intrinsicWriterSingletonData;
        private IntrinsicWriter.Lookup _writers;

        // Writer-existence pre-check: IntrinsicWriter only requires the target's Intrinsic buffer (EssenceConfig is
        // a global singleton; a key missing from it is a loud config error, not a transient miss). Checking the
        // buffer up-front folds the ApplyJob "no writer" silent-drop into the retry.
        private BufferLookup<Intrinsic> _intrinsicsLookup;

        private EntityQuery _activeClipQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _intrinsicChanges =
                new NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>(64, Allocator.Persistent);
            _uniqueKeySet = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            _uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            state.RequireForUpdate<EssenceConfig>();

            _activeClipQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TrackBinding, TimelineEssenceIntrinsicData, ClipActive>()
                .WithPresent<TimelineEssenceDeliveryPending>()
                .Build(ref state);

            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _linkSourceLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _linkLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _intrinsicsLookup = state.GetBufferLookup<Intrinsic>(true);
            _intrinsicWriterSingletonData.Create(ref state);
            _writers.Create(ref state);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _intrinsicChanges.Dispose();
            _uniqueKeySet.Dispose();
            _uniqueKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _linkSourceLookup.Update(ref state);
            _linkLookup.Update(ref state);
            _intrinsicsLookup.Update(ref state);
            _writers.Update(ref state, _intrinsicWriterSingletonData);
            _uniqueKeySet.Clear();

            var activeClipCount = _activeClipQuery.CalculateEntityCount();
            if (activeClipCount > _uniqueKeySet.Capacity) _uniqueKeySet.Capacity = activeClipCount;

            state.Dependency = new GatherJob
            {
                IntrinsicChanges = _intrinsicChanges.AsWriter(),
                UniqueKeys = _uniqueKeySet.AsParallelWriter(),
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup,
                Intrinsics = _intrinsicsLookup
            }.ScheduleParallel(state.Dependency);

            // A clip that ends still owing a delivery never resolved its target/binding/writer — surface it once
            // (instead of a silent drop) and clear the latch so the next activation re-arms cleanly.
            var hasLogger = SystemAPI.TryGetSingleton<BLLogger>(out var logger);
            state.Dependency = new DiagnoseMissedJob
            {
                LogEnabled = hasLogger,
                Logger = logger
            }.ScheduleParallel(state.Dependency);

            state.Dependency = _intrinsicChanges.Apply(state.Dependency, out var reader);

            state.Dependency = new GetKeysJob
            {
                UniqueKeys = _uniqueKeys,
                UniqueKeySet = _uniqueKeySet
            }.Schedule(state.Dependency);

            state.Dependency = new ApplyJob
            {
                Keys = _uniqueKeys.AsDeferredJobArray(),
                GroupChanges = reader,
                Writers = _writers
            }.Schedule(_uniqueKeys, 64, state.Dependency);

            state.Dependency = _intrinsicChanges.Clear(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        // EnabledRef params would otherwise implicitly filter to ENABLED — but the edge frame has
        // ClipActivePrevious disabled and the latch starts disabled, so match them by presence and read the state.
        [WithPresent(typeof(ClipActivePrevious))]
        [WithPresent(typeof(TimelineEssenceDeliveryPending))]
        private partial struct GatherJob : IJobEntity
        {
            public NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>.ParallelWriter IntrinsicChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;

            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;
            [ReadOnly] public BufferLookup<Intrinsic> Intrinsics;

            private void Execute(
                in TrackBinding binding,
                in TimelineEssenceIntrinsicData data,
                EnabledRefRO<ClipActivePrevious> clipActivePrevious,
                EnabledRefRW<TimelineEssenceDeliveryPending> pending)
            {
                var isEdge = !clipActivePrevious.ValueRO;
                var hasPayload = data.Intrinsic.Value != 0;

                // Resolve the target only when something is (or just became) owed — and treat a missing Intrinsic
                // buffer as not-yet-resolved so it retries instead of being dropped in ApplyJob.
                var resolved = false;
                var target = Entity.Null;
                if ((isEdge || pending.ValueRO) && hasPayload && binding.Value != Entity.Null
                    && TimelineEssenceResolver.TryResolveLinkedTarget(data.RouteTo, data.RouteLinkKey, binding.Value,
                        TargetsLookup, LinkSources, Links, out target)
                    && Intrinsics.HasBuffer(target))
                {
                    resolved = true;
                }

                var outcome = EssenceDeliveryGate.Evaluate(isEdge, pending.ValueRO, hasPayload, resolved, out var next);
                pending.ValueRW = next;

                if (outcome == EssenceDeliveryGate.Outcome.Fire)
                {
                    IntrinsicChanges.Add(target, new IntrinsicAmount(data.Intrinsic, data.Amount));
                    UniqueKeys.Add(target);
                }
            }
        }

        // Clip ended while still owing a delivery: the target/binding/buffer never resolved during the whole
        // active window. Clear the latch (so the next activation re-arms) and warn once instead of dropping silently.
        [BurstCompile]
        [WithAll(typeof(TimelineEssenceIntrinsicData))]
        [WithDisabled(typeof(ClipActive))]
        [WithAll(typeof(TimelineEssenceDeliveryPending))] // only walk clips that still owe a delivery, not every inactive clip
        private partial struct DiagnoseMissedJob : IJobEntity
        {
            public bool LogEnabled;
            public BLLogger Logger;

            private void Execute(EnabledRefRW<TimelineEssenceDeliveryPending> pending)
            {
                if (!pending.ValueRO)
                {
                    return;
                }

                pending.ValueRW = false;

                if (LogEnabled)
                {
                    Logger.LogWarning512(
                        "[Essence] Intrinsic clip ended without delivering: target/binding/buffer never resolved during the clip. Check routeTo and that the target carries an Intrinsic buffer (StatAuthoring).");
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