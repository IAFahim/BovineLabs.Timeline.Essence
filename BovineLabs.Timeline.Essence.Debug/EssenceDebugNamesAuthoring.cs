#if UNITY_EDITOR || BL_DEBUG
using System;
using System.Collections.Generic;
using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Core.Collections;
using BovineLabs.Essence.Authoring;
using BovineLabs.Reaction.Authoring.Core;
using Unity.Collections;
using Unity.Entities;
using Object = UnityEngine.Object;

namespace BovineLabs.Essence.Debug
{
    internal sealed class EssenceDebugNamesBaker : Baker<SettingsAuthoring>
    {
        // EssenceDebugNames now lives in the runtime BovineLabs.Timeline.Essence assembly, so the
        // baked type resolves in player builds (a component serialized into a subscene must be
        // runtime). This baker + the debug systems that read it stay editor-only.
        public override void Bake(SettingsAuthoring authoring)
        {
            var essence = AuthoringSettingsUtility.GetSettings<EssenceSettings>();
            var reaction = AuthoringSettingsUtility.GetSettings<ReactionSettings>();

            DependsOn(essence);
            DependsOn(reaction);

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<EssenceDebugNames.Data>();

            BakeNames(builder, ref root.StatNames, essence.StatSchemas, schema => (ushort)schema.Key);
            BakeNames(builder, ref root.IntrinsicNames, essence.IntrinsicSchemas, schema => (ushort)schema.Key);
            BakeNames(builder, ref root.EventNames, reaction.ConditionEvents, schema => (ushort)schema.Key);

            var blob = builder.CreateBlobAssetReference<EssenceDebugNames.Data>(Allocator.Persistent);
            AddBlobAsset(ref blob, out _);

            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new EssenceDebugNames { Value = blob });
            builder.Dispose();
        }

        private void BakeNames<T>(
            BlobBuilder builder,
            ref BlobHashMap<ushort, FixedString32Bytes> map,
            IReadOnlyList<T> schemas,
            Func<T, ushort> key)
            where T : Object
        {
            var hashMap = builder.AllocateHashMap(ref map, schemas.Count);
            foreach (var schema in schemas)
            {
                if (schema == null) continue;
                DependsOn(schema);
                hashMap.Add(key(schema), new FixedString32Bytes(schema.name));
            }
        }
    }
}
#endif