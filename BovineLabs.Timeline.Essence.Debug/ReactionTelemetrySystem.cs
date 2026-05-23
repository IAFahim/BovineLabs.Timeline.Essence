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
    public static class ReactionTelemetryConfig
    {
        [ConfigVar("reactiontelemetry.force-draw", false, "Enable the Reaction telemetry drawer.")]
        internal static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<EnabledType>();

        [ConfigVar("reactiontelemetry.offset", 0f, 2.4f, 0f, 0f, "Offset for the Reaction card (Center).")]
        internal static readonly SharedStatic<Vector4> Offset = SharedStatic<Vector4>.GetOrCreate<OffsetType>();

        [ConfigVar("reactiontelemetry.condition-color", 1f, 0.8f, 0.2f, 1f, "Accent for conditions.")]
        internal static readonly SharedStatic<Color> ConditionColor = SharedStatic<Color>.GetOrCreate<ConditionColorType>();

        [ConfigVar("reactiontelemetry.event-color", 0.95f, 0.5f, 0.3f, 1f, "Accent for events.")]
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
        private const double RetentionWindow = 2.0;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!ReactionTelemetryConfig.Enabled.Data && !SystemAPI.HasSingleton<DrawSystem.Singleton>()) return;

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
                for (var i = history.Length - 1; i >= 0; i--)
                {
                    if (time - history[i].Timestamp > RetentionWindow) history.RemoveAt(i);
                }

                if (events.Length == 0) continue;

                foreach (var kvp in events.AsMap())
                {
                    history.Insert(0, new ReactionEventHistoryRecord
                    {
                        Key = kvp.Key.Value,
                        Value = kvp.Value,
                        Timestamp = time,
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
            if (!ReactionTelemetryConfig.Enabled.Data)
            {
                drawer = drawSystem.CreateDrawer<ReactionTelemetrySystem>();
                if (!drawer.IsEnabled) return;
            }
            else
            {
                drawer = drawSystem.CreateDrawer();
            }

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

                Offset = ((float4)ReactionTelemetryConfig.Offset.Data).xyz,
                ConditionAccent = ReactionTelemetryConfig.ConditionColor.Data,
                EventAccent = ReactionTelemetryConfig.EventColor.Data,
            }.Schedule(this.telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderTelemetryJob : IJobChunk
        {
            private const float SolidSeconds = 1.5f;
            private const float FadeSeconds = 0.5f;
            private const float PulseSeconds = 0.3f;

            public Drawer Renderer;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public ComponentTypeHandle<Active> ActiveHandle;
            [ReadOnly] public ComponentTypeHandle<ConditionActive> ConditionActiveHandle;
            [ReadOnly] public BufferTypeHandle<ConditionValues> ConditionValuesHandle;
            [ReadOnly] public BufferTypeHandle<ReactionEventHistoryRecord> HistoryHandle;
            [ReadOnly] public EssenceDebugNames DebugNames;
            public double Time;

            public float3 Offset;
            public Color ConditionAccent;
            public Color EventAccent;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref this.TransformHandle);
                var conditions = chunk.GetNativeArray(ref this.ConditionActiveHandle);
                var values = chunk.GetBufferAccessor(ref this.ConditionValuesHandle);
                var history = chunk.GetBufferAccessor(ref this.HistoryHandle);

                var hasActive = chunk.Has(ref this.ActiveHandle);
                var activeMask = hasActive ? chunk.GetEnabledMaskRO(ref this.ActiveHandle) : default;

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var top = transforms[index].Position + this.Offset;
                    var pen = new Pen(top, Layout.Header, Layout.Row);

                    Glyph.Title(this.Renderer, top, "REACTION", this.EventAccent);

                    if (hasActive)
                    {
                        this.RenderState(pen.Take(), activeMask[index]);
                    }

                    if (conditions.Length > 0)
                    {
                        this.RenderMask(pen.Take(), conditions[index]);
                    }

                    if (values.Length > 0)
                    {
                        this.RenderValues(ref pen, values[index]);
                    }

                    if (history.Length > 0)
                    {
                        this.RenderEvents(ref pen, history[index]);
                    }

                    Glyph.Frame(this.Renderer, top, Layout.CardWidth, top.y - pen.Cursor.y + Layout.Pad, Ink.Frame, this.EventAccent);
                }
            }

            private void RenderState(float3 row, bool active)
            {
                var color = active ? Ink.Live : Ink.Idle;
                Glyph.Pulse(this.Renderer, new float3(row.x + 0.03f, row.y + 0.03f, row.z), active ? 0.035f : 0.022f, color);
                Glyph.Label(this.Renderer, new float3(row.x + 0.12f, row.y, row.z), active ? "ACTIVE" : "INACTIVE", color);
            }

            private void RenderMask(float3 row, in ConditionActive condition)
            {
                Glyph.Label(this.Renderer, row, "mask", Ink.Muted);
                Glyph.Readout(
                    this.Renderer,
                    new float3(row.x + 0.32f, row.y, row.z),
                    $"{condition.Value.HumanizedData}",
                    this.ConditionAccent,
                    Layout.Body);
            }

            private void RenderValues(ref Pen pen, DynamicBuffer<ConditionValues> buffer)
            {
                var peak = 1;
                for (var i = 0; i < buffer.Length; i++)
                {
                    peak = math.max(peak, math.abs(buffer[i].Value));
                }

                for (var i = 0; i < buffer.Length; i++)
                {
                    var raw = buffer[i].Value;
                    if (raw == 0) continue;

                    var row = pen.Take();

                    var label = new FixedString32Bytes();
                    label.Append('[');
                    label.Append(i);
                    label.Append(']');
                    Glyph.Label(this.Renderer, row, label, Ink.Muted);

                    Glyph.Gauge(this.Renderer, new float3(row.x + Layout.GaugeColumn, row.y + 0.03f, row.z), Layout.BarWidth, raw / (float)peak);

                    var value = new FixedString128Bytes();
                    Format.Compact(ref value, raw);
                    Glyph.Readout(this.Renderer, new float3(row.x + Layout.ValueColumn, row.y, row.z), value, Ink.Value, Layout.Body);
                }
            }

            private void RenderEvents(ref Pen pen, DynamicBuffer<ReactionEventHistoryRecord> buffer)
            {
                ref var names = ref this.DebugNames.Value.Value.EventNames;

                for (var i = 0; i < buffer.Length; i++)
                {
                    var record = buffer[i];
                    var age = (float)(this.Time - record.Timestamp);
                    var fade = math.saturate(1f - (age > SolidSeconds ? (age - SolidSeconds) / FadeSeconds : 0f));
                    var color = Ink.Dim(this.EventAccent, fade);

                    var row = pen.Take();

                    if (age < PulseSeconds)
                    {
                        Glyph.Pulse(this.Renderer, new float3(row.x + 0.02f, row.y + 0.03f, row.z), 0.02f + age * 0.12f, Ink.Dim(this.EventAccent, 1f - age / PulseSeconds));
                    }

                    var label = new FixedString32Bytes();
                    if (names.TryGetValue(record.Key, out var named)) label = named.Ref;
                    else label.Append(record.Key);
                    Glyph.Label(this.Renderer, new float3(row.x + 0.1f, row.y, row.z), label, color);

                    var value = new FixedString128Bytes();
                    Format.Compact(ref value, record.Value);
                    Glyph.Readout(this.Renderer, new float3(row.x + Layout.ValueColumn, row.y, row.z), value, color, Layout.Body);
                }
            }
        }
    }
}
#endif