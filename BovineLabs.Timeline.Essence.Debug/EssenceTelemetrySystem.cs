// BovineLabs.Reaction.Debug/ReactionTelemetrySystem.cs
#if UNITY_EDITOR || BL_DEBUG
using System.Diagnostics.CodeAnalysis;
using BovineLabs.Core;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Core.Extensions;
using BovineLabs.Essence.Debug;
using BovineLabs.Quill;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Active;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Groups;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Reaction.Debug
{
    [Configurable]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1611:Element parameters should be documented", Justification = "Using see cref")]
    public static class ReactionTelemetrySystemConfig
    {
        [ConfigVar("reactiontelemetry.force-draw", false, "Enable the Reaction telemetry drawer.")]
        internal static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<EnabledType>();

        // --- Layout Config ---
        [ConfigVar("reactiontelemetry.offset", 0f, 2.2f, 0f, 0f, "Offset for Reaction Telemetry (Center).")]
        internal static readonly SharedStatic<Vector4> Offset = SharedStatic<Vector4>.GetOrCreate<OffsetType>();

        [ConfigVar("reactiontelemetry.condition-color", 1f, 0.8f, 0.2f, 1f, "Color for Conditions.")]
        internal static readonly SharedStatic<Color> ConditionColor = SharedStatic<Color>.GetOrCreate<ConditionColorType>();

        [ConfigVar("reactiontelemetry.event-color", 0.9f, 0.4f, 0.2f, 1f, "Color for Events.")]
        internal static readonly SharedStatic<Color> EventColor = SharedStatic<Color>.GetOrCreate<EventColorType>();

        private struct EnabledType { }
        private struct OffsetType { }
        private struct ConditionColorType { }
        private struct EventColorType { }
    }

    [InternalBufferCapacity(8)]
    public struct ReactionEventHistoryRecord : IBufferElementData
    {
        public ushort Key;
        public int Value;
        public double Timestamp;
    }

    [UpdateInGroup(typeof(ConditionWriteEventsGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [BurstCompile]
    public partial struct ReactionTelemetryHistorySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!ReactionTelemetrySystemConfig.Enabled.Data && !SystemAPI.HasSingleton<DrawSystem.Singleton>()) return;
            
            var time = SystemAPI.Time.ElapsedTime;
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

            foreach (var (events, entity) in SystemAPI.Query<DynamicBuffer<ConditionEvent>>().WithNone<ReactionEventHistoryRecord>().WithEntityAccess())
            {
                if (events.Length > 0) ecb.AddBuffer<ReactionEventHistoryRecord>(entity);
            }
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            foreach (var (events, history) in SystemAPI.Query<DynamicBuffer<ConditionEvent>, DynamicBuffer<ReactionEventHistoryRecord>>())
            {
                // Prune events older than 2 seconds to make space
                for (int i = history.Length - 1; i >= 0; i--)
                {
                    if (time - history[i].Timestamp > 2.0) history.RemoveAt(i);
                }

                if (events.Length == 0) continue;

                foreach(var kvp in events.AsMap())
                {
                    // Push newest event to top of stack
                    history.Insert(0, new ReactionEventHistoryRecord {
                        Key = kvp.Key.Value,
                        Value = kvp.Value,
                        Timestamp = time
                    });
                }
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    [BurstCompile]
    public partial struct ReactionTelemetrySystem : ISystem
    {
        private EntityQuery telemetryQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.telemetryQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalToWorld>()
                .WithAny<Active, ConditionActive, ReactionEventHistoryRecord, ConditionValues>()
                .Build();
            
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<EssenceDebugNames>(); 
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<DrawSystem.Singleton>()) return;
            ref var drawSystem = ref SystemAPI.GetSingletonRW<DrawSystem.Singleton>().ValueRW;

            Drawer drawer;
            if (!ReactionTelemetrySystemConfig.Enabled.Data)
            {
                drawer = drawSystem.CreateDrawer<ReactionTelemetrySystem>();
                if (!drawer.IsEnabled) return;
            }
            else drawer = drawSystem.CreateDrawer();

            state.Dependency = new RenderTelemetryJob
            {
                Renderer = drawer,
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                ActiveHandle = SystemAPI.GetComponentTypeHandle<Active>(true),
                ConditionActiveHandle = SystemAPI.GetComponentTypeHandle<ConditionActive>(true),
                ConditionValuesHandle = SystemAPI.GetBufferTypeHandle<ConditionValues>(true),
                HistoryHandle = SystemAPI.GetBufferTypeHandle<ReactionEventHistoryRecord>(true),
                DebugNames = SystemAPI.GetSingleton<EssenceDebugNames>(),
                Time = SystemAPI.Time.ElapsedTime,
                
                Offset = ((float4)ReactionTelemetrySystemConfig.Offset.Data).xyz,
                ConditionColor = ReactionTelemetrySystemConfig.ConditionColor.Data,
                EventColor = ReactionTelemetrySystemConfig.EventColor.Data
            }.Schedule(this.telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderTelemetryJob : IJobChunk
        {
            public Drawer Renderer;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public ComponentTypeHandle<Active> ActiveHandle;
            [ReadOnly] public ComponentTypeHandle<ConditionActive> ConditionActiveHandle;
            [ReadOnly] public BufferTypeHandle<ConditionValues> ConditionValuesHandle;
            [ReadOnly] public BufferTypeHandle<ReactionEventHistoryRecord> HistoryHandle;
            [ReadOnly] public EssenceDebugNames DebugNames;
            public double Time;

            public float3 Offset;
            public Color ConditionColor;
            public Color EventColor;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref this.TransformHandle);
                var hasActive = chunk.Has(ref this.ActiveHandle);
                var conds = chunk.GetNativeArray(ref this.ConditionActiveHandle);
                var cvsAccessor = chunk.GetBufferAccessor(ref this.ConditionValuesHandle);
                var historyAccessor = chunk.GetBufferAccessor(ref this.HistoryHandle);

                var ColorActive = new Color(0.3f, 0.9f, 0.4f, 1f);
                var ColorInactive = new Color(0.9f, 0.3f, 0.3f, 1f);
                var HeaderTint = new Color(1f, 1f, 1f, 0.3f);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var origin = transforms[index].Position;
                    var cursor = origin + this.Offset;

                    this.Renderer.Line(origin, cursor, HeaderTint);
                    this.Renderer.Text32(cursor, "[ REACTION ]", HeaderTint, 12f);
                    cursor.y -= 0.2f;

                    if (hasActive)
                    {
                        var mask = chunk.GetEnabledMaskRO(ref this.ActiveHandle);
                        bool isActive = mask[index];
                        this.Renderer.Text32(cursor, isActive ? "State: ACTIVE" : "State: INACTIVE", isActive ? ColorActive : ColorInactive, 11f);
                        cursor.y -= 0.15f;
                    }

                    if (conds.Length > 0)
                    {
                        this.Renderer.Text64(cursor, $"Mask: {conds[index].Value.HumanizedData}", this.ConditionColor, 11f);
                        cursor.y -= 0.15f;
                    }

                    if (cvsAccessor.Length > 0)
                    {
                        var cv = cvsAccessor[index];
                        for (int i = 0; i < cv.Length; i++)
                        {
                            if (cv[i].Value != 0) // Only render populated values to avoid noise
                            {
                                this.Renderer.Text64(cursor, $"[VAL] Idx {i} : {cv[i].Value}", this.ConditionColor, 11f);
                                cursor.y -= 0.15f;
                            }
                        }
                    }

                    if (historyAccessor.Length > 0)
                    {
                        var evList = historyAccessor[index];
                        ref var names = ref this.DebugNames.Value.Value.EventNames;

                        // Render top to bottom
                        for (int i = 0; i < evList.Length; i++)
                        {
                            var ev = evList[i];
                            float age = (float)(this.Time - ev.Timestamp);
                            
                            // 1.5s solid, 0.5s fade out
                            float alpha = math.clamp(1f - (age > 1.5f ? (age - 1.5f) / 0.5f : 0f), 0f, 1f);
                            Color c = this.EventColor; 
                            c.a = alpha;

                            var format = new FixedString128Bytes();
                            format.Append("> ");
                            if (names.TryGetValue(ev.Key, out var namePtr)) format.Append(namePtr.Ref);
                            else format.Append(ev.Key);
                            
                            int spacesNeeded = math.max(1, 18 - format.Length);
                            for (int s = 0; s < spacesNeeded; s++) format.Append(' ');
                                
                            format.Append(ev.Value);

                            this.Renderer.Text128(cursor, format, c, 11f);
                            cursor.y -= 0.15f;
                        }
                    }
                }
            }
        }
    }
}
#endif