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
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1611:Element parameters should be documented",
        Justification = "Using see cref")]
    public static class ReactionTelemetryConfig
    {
        [ConfigVar("reactiontelemetry.force-draw", false, "Enable the Reaction telemetry drawer.")]
        internal static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<EnabledType>();

        [ConfigVar("reactiontelemetry.scale", 0.05f, "Fixed world-space scale multiplier for the cards.")]
        internal static readonly SharedStatic<float> Scale = SharedStatic<float>.GetOrCreate<ScaleType>();

        [ConfigVar("reactiontelemetry.offset", 0f, 2.4f, 0f, 0f, "World anchor offset for the Reaction card.")]
        internal static readonly SharedStatic<Vector4> Offset = SharedStatic<Vector4>.GetOrCreate<OffsetType>();

        [ConfigVar("reactiontelemetry.condition-color", 1f, 0.8f, 0.2f, 1f, "Accent colour for conditions.")]
        internal static readonly SharedStatic<Color> ConditionColor =
            SharedStatic<Color>.GetOrCreate<ConditionColorType>();

        [ConfigVar("reactiontelemetry.event-color", 0.95f, 0.5f, 0.3f, 1f, "Accent colour for events.")]
        internal static readonly SharedStatic<Color> EventColor =
            SharedStatic<Color>.GetOrCreate<EventColorType>();

        private struct EnabledType { }
        private struct ScaleType { }
        private struct OffsetType { }
        private struct ConditionColorType { }
        private struct EventColorType { }
    }

    [InternalBufferCapacity(8)]
    public struct ReactionEventHistoryRecord : IBufferElementData
    {
        public ushort Key;
        public int    Value;
        public double Timestamp;
    }

    [UpdateInGroup(typeof(ConditionWriteEventsGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [BurstCompile]
    public partial struct ReactionTelemetryHistorySystem : ISystem
    {
        private const double RetentionWindow = 2.0;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!ReactionTelemetryConfig.Enabled.Data &&
                !SystemAPI.HasSingleton<DrawSystem.Singleton>()) return;

            var time = SystemAPI.Time.ElapsedTime;
            var ecb  = new EntityCommandBuffer(state.WorldUpdateAllocator);

            foreach (var (events, entity) in
                SystemAPI.Query<DynamicBuffer<ConditionEvent>>()
                    .WithNone<ReactionEventHistoryRecord>()
                    .WithEntityAccess())
            {
                if (events.Length > 0) ecb.AddBuffer<ReactionEventHistoryRecord>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            foreach (var (events, history) in
                SystemAPI.Query<DynamicBuffer<ConditionEvent>, DynamicBuffer<ReactionEventHistoryRecord>>())
            {
                for (var i = history.Length - 1; i >= 0; i--)
                    if (time - history[i].Timestamp > RetentionWindow)
                        history.RemoveAt(i);

                if (events.Length == 0) continue;

                foreach (var kvp in events.AsMap())
                    history.Insert(0, new ReactionEventHistoryRecord
                    {
                        Key       = kvp.Key.Value,
                        Value     = kvp.Value,
                        Timestamp = time,
                    });
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    [BurstCompile]
    public partial struct ReactionTelemetrySystem : ISystem
    {
        private EntityQuery telemetryQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            telemetryQuery = SystemAPI.QueryBuilder()
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

            var off = (float4)ReactionTelemetryConfig.Offset.Data;

            state.Dependency = new RenderTelemetryJob
            {
                Renderer          = drawer,
                Camera            = drawSystem.CameraCulling,
                Scale             = ReactionTelemetryConfig.Scale.Data,
                TransformHandle   = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                ActiveHandle      = SystemAPI.GetComponentTypeHandle<Active>(true),
                ConditionActiveHandle = SystemAPI.GetComponentTypeHandle<ConditionActive>(true),
                ConditionValuesHandle = SystemAPI.GetBufferTypeHandle<ConditionValues>(true),
                HistoryHandle     = SystemAPI.GetBufferTypeHandle<ReactionEventHistoryRecord>(true),
                DebugNames        = SystemAPI.GetSingleton<EssenceDebugNames>(),
                Time              = SystemAPI.Time.ElapsedTime,
                WorldLift         = off.y,
                ConditionAccent   = ReactionTelemetryConfig.ConditionColor.Data,
                EventAccent       = ReactionTelemetryConfig.EventColor.Data,
            }.Schedule(telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderTelemetryJob : IJobChunk
        {
            private const float SolidSeconds = 1.5f;
            private const float FadeSeconds  = 0.5f;
            private const float PulseSeconds = 0.30f;

            private const float PulseX     = -4.3f;
            private const float StateTextX  = -2.6f;
            private const float MaskLabelX  = -4.0f;
            private const float MaskValueX  =  0.8f;
            private const float IndexLabelX = -4.0f;

            public Drawer Renderer;
            public CameraCulling Camera;
            public float Scale;

            [ReadOnly] public ComponentTypeHandle<LocalToWorld>      TransformHandle;
            [ReadOnly] public ComponentTypeHandle<Active>             ActiveHandle;
            [ReadOnly] public ComponentTypeHandle<ConditionActive>    ConditionActiveHandle;
            [ReadOnly] public BufferTypeHandle<ConditionValues>       ConditionValuesHandle;
            [ReadOnly] public BufferTypeHandle<ReactionEventHistoryRecord> HistoryHandle;
            [ReadOnly] public EssenceDebugNames                       DebugNames;
            public double Time;

            public float WorldLift;
            public Color ConditionAccent;
            public Color EventAccent;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms  = chunk.GetNativeArray(ref TransformHandle);
                var conditions  = chunk.GetNativeArray(ref ConditionActiveHandle);
                var values      = chunk.GetBufferAccessor(ref ConditionValuesHandle);
                var history     = chunk.GetBufferAccessor(ref HistoryHandle);

                var hasActive   = chunk.Has(ref ActiveHandle);
                var activeMask  = hasActive ? chunk.GetEnabledMaskRO(ref ActiveHandle) : default;

                var enumerator  = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var head = transforms[index].Position;
                    var v    = View.WorldFacing(Camera, head, Scale)
                                   .NudgeWorld(new float3(0f, WorldLift, 0f));
                    var pen  = new Pen(0f, Layout.Header, Layout.Leading);

                    Glyph.Title(Renderer, v, "REACTION", Layout.HalfCard, EventAccent);

                    if (hasActive)                  RenderState(v, pen.Take(), activeMask[index]);
                    if (conditions.Length > 0)      RenderMask(v, pen.Take(), conditions[index]);
                    if (values.Length > 0)          RenderValues(v, ref pen, values[index]);
                    if (history.Length > 0)         RenderEvents(v, ref pen, history[index]);

                    Glyph.Frame(Renderer, v,
                        -Layout.HalfCard, Layout.HalfCard,
                        Layout.Pad, pen.Y - Layout.Pad,
                        Ink.Frame, EventAccent);
                }
            }

            private void RenderState(in View v, float y, bool active)
            {
                var color  = active ? Ink.Live : Ink.Idle;
                var radius = active ? 0.30f : 0.20f;
                Glyph.Pulse(Renderer, v, PulseX, y + 0.12f, radius, color);

                var label = new FixedString32Bytes();
                label.Append(active ? "ACTIVE" : "inactive");
                Glyph.Label(Renderer, v, StateTextX, y, label, color);
            }

            private void RenderMask(in View v, float y, in ConditionActive condition)
            {
                var ml = new FixedString32Bytes(); ml.Append("mask");
                Glyph.Label(Renderer, v, MaskLabelX, y, ml, Ink.Muted, Layout.Micro);

                var mv = new FixedString128Bytes();
                mv.Append($"{condition.Value.HumanizedData}");
                Glyph.Text(Renderer, v, MaskValueX, y, mv, ConditionAccent, Layout.Micro);
            }

            private void RenderValues(in View v, ref Pen pen, DynamicBuffer<ConditionValues> buffer)
            {
                var peak = 1;
                for (var i = 0; i < buffer.Length; i++)
                    peak = math.max(peak, math.abs(buffer[i].Value));

                for (var i = 0; i < buffer.Length; i++)
                {
                    var raw = buffer[i].Value;
                    if (raw == 0) continue;

                    var y     = pen.Take();
                    var label = new FixedString32Bytes();
                    label.Append('['); label.Append(i); label.Append(']');
                    Glyph.Label(Renderer, v, IndexLabelX, y, label, Ink.Muted, Layout.Micro);

                    Glyph.Gauge(Renderer, v,
                        Layout.GaugeX0, Layout.GaugeX1,
                        y - 0.25f,
                        raw / (float)peak);

                    var compact = new FixedString128Bytes();
                    Format.Compact(ref compact, raw);
                    Glyph.Text(Renderer, v, Layout.ValueX, y, compact, Ink.Value, Layout.Body);
                }
            }

            private void RenderEvents(in View v, ref Pen pen, DynamicBuffer<ReactionEventHistoryRecord> buffer)
            {
                ref var names = ref DebugNames.Value.Value.EventNames;

                for (var i = 0; i < buffer.Length; i++)
                {
                    var record = buffer[i];
                    var age    = (float)(Time - record.Timestamp);
                    var fade   = math.saturate(
                        1f - (age > SolidSeconds ? (age - SolidSeconds) / FadeSeconds : 0f));
                    var color  = Ink.Dim(EventAccent, fade);

                    var y = pen.Take();

                    if (age < PulseSeconds)
                    {
                        var pulse = new Color(color.r, color.g, color.b,
                            (1f - age / PulseSeconds) * color.a);
                        Glyph.Pulse(Renderer, v,
                            PulseX, y + 0.12f,
                            0.15f + age * 0.5f,
                            pulse);
                    }

                    var label = new FixedString32Bytes();
                    if (names.TryGetValue(record.Key, out var named)) label = named.Ref;
                    else label.Append(record.Key);
                    Glyph.Label(Renderer, v, MaskLabelX + 0.5f, y, label, color);

                    var compact = new FixedString128Bytes();
                    Format.Compact(ref compact, record.Value);
                    Glyph.Text(Renderer, v, Layout.ValueX, y, compact, color, Layout.Body);
                }
            }
        }
    }
}
#endif