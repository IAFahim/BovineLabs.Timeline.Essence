using BovineLabs.Core.Collections;
using BovineLabs.Core.Iterators;
using BovineLabs.Core.ObjectManagement;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Testing;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.Essence.Tests
{
    // Shared fixture for the Timeline.Essence system tests. Provides the two Reaction bootstrap singletons that the
    // ConditionEventWriter now depends on (ConditionConfig + ConditionEventPayloadAllocator) — the same pair
    // BovineLabs.Reaction.Tests/ConditionEventWriterTests + EssenceTestsFixture create. These became mandatory once
    // the TimelineEssence* systems gained RequireForUpdate<ConditionConfig>/<ConditionEventPayloadAllocator> and the
    // writer switched to TryGetPayloadType (an event key must be registered in the config or its write is dropped).
    public abstract class TimelineEssenceTestFixture : ECSTestsFixture
    {
        // Register event keys 0..MaxEventKey as Int32 payloads so any BLId in that range is a valid event target.
        protected const int MaxEventKey = 256;

        private DoubleRewindableAllocators payloadAllocators;

        public override void Setup()
        {
            base.Setup();

            this.payloadAllocators = new DoubleRewindableAllocators(Allocator.Persistent, 16 * 1024);
            this.Manager.AddComponentData(this.Manager.CreateEntity(),
                new ConditionEventPayloadAllocator { Handle = this.payloadAllocators.Allocator.Handle });

            this.Manager.AddComponentData(this.Manager.CreateEntity(), this.CreateConditionConfig());
        }

        public override void TearDown()
        {
            this.payloadAllocators.Dispose();
            base.TearDown();
        }

        // A ConditionEventWriter needs both the ConditionEvent map buffer and the EventsDirty enableable to be valid.
        protected void GiveTargetAWriter(Entity target)
        {
            this.Manager.AddBuffer<ConditionEvent>(target).Initialize();
            this.Manager.AddComponent<EventsDirty>(target);
            this.Manager.SetComponentEnabled<EventsDirty>(target, true);
        }

        private ConditionConfig CreateConditionConfig()
        {
            var eventType = ConditionTypes.NameToKey(ConditionTypes.EventType);

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ConditionConfig.Data>();
            var map = builder.AllocateHashMap(ref root.PayloadTypes, MaxEventKey + 1, 2);
            for (var key = 0; key <= MaxEventKey; key++)
            {
                map.Add(new EventSubscriberKey(key, eventType), ConditionPayloadType.Int32);
            }

            var blob = builder.CreateBlobAssetReference<ConditionConfig.Data>(Allocator.Persistent);
            builder.Dispose();
            this.BlobAssetStore.TryAdd(ref blob);
            return new ConditionConfig { Value = blob };
        }
    }
}
