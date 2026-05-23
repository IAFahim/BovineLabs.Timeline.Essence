#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Core.Collections;
using BovineLabs.Essence.Authoring;
using BovineLabs.Reaction.Authoring.Core;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Essence.Debug
{
    public struct EssenceDebugNames : IComponentData
    {
        public BlobAssetReference<Data> Value;

        public struct Data
        {
            public BlobHashMap<ushort, FixedString32Bytes> StatNames;
            public BlobHashMap<ushort, FixedString32Bytes> IntrinsicNames;
            public BlobHashMap<ushort, FixedString32Bytes> EventNames;
        }
    }

    public class EssenceDebugNamesBaker : Baker<SettingsAuthoring>
    {
        public override void Bake(SettingsAuthoring authoring)
        {
            var essenceSettings = AuthoringSettingsUtility.GetSettings<EssenceSettings>();
            var reactionSettings = AuthoringSettingsUtility.GetSettings<ReactionSettings>();

            DependsOn(essenceSettings);
            DependsOn(reactionSettings);

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<EssenceDebugNames.Data>();

            var statBuilder = builder.AllocateHashMap(ref root.StatNames, essenceSettings.StatSchemas.Count);
            foreach (var stat in essenceSettings.StatSchemas)
            {
                if (stat != null)
                {
                    DependsOn(stat);
                    statBuilder.Add(stat.Key, new FixedString32Bytes(stat.name));
                }
            }

            var intrinsicBuilder = builder.AllocateHashMap(ref root.IntrinsicNames, essenceSettings.IntrinsicSchemas.Count);
            foreach (var intrinsic in essenceSettings.IntrinsicSchemas)
            {
                if (intrinsic != null)
                {
                    DependsOn(intrinsic);
                    intrinsicBuilder.Add(intrinsic.Key, new FixedString32Bytes(intrinsic.name));
                }
            }

            var eventBuilder = builder.AllocateHashMap(ref root.EventNames, reactionSettings.ConditionEvents.Count);
            foreach (var evt in reactionSettings.ConditionEvents)
            {
                if (evt != null)
                {
                    DependsOn(evt);
                    eventBuilder.Add(evt.Key, new FixedString32Bytes(evt.name));
                }
            }

            var blob = builder.CreateBlobAssetReference<EssenceDebugNames.Data>(Allocator.Persistent);
            AddBlobAsset(ref blob, out _);

            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new EssenceDebugNames { Value = blob });
            blob.Dispose();
        }
    }
}
#endif