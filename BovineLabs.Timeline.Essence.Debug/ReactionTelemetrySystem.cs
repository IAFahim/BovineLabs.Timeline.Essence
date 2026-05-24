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

        [ConfigVar("reactiontelemetry.full-details", true, "Show full details including event history.")]
        internal static readonly SharedStatic<bool> FullDetails = SharedStatic<bool>.GetOrCreate<FullDetailsType>();

        [ConfigVar("reactiontelemetry.scale", 0.04f, "Fixed world-space scale for the UI.")]
        internal static readonly SharedStatic<float> Scale = SharedStatic<float>.GetOrCreate<ScaleType>();

        [ConfigVar("reactiontelemetry.offset", 0f, 2.4f, 0f, 0f, "World anchor offset for the Reaction data.")]
        internal static readonly SharedStatic<Vector4> Offset = SharedStatic<Vector4>.GetOrCreate<OffsetType>();

        [ConfigVar("reactiontelemetry.condition-color", 1f, 0.8f, 0.2f, 1f, "Accent colour for conditions.")]
        internal static readonly SharedStatic<Color> ConditionColor =
            SharedStatic<Color>.GetOrCreate<ConditionColorType>();

        [ConfigVar("reactiontelemetry.event-color", 0.95f, 0.5f, 0.3f, 1f, "Accent colour for events.")]
        internal static readonly SharedStatic<Color> EventColor =
            SharedStatic<Color>.GetOrCreate<EventColorType>();

        [ConfigVar("reactiontelemetry.visual-active-color", 0.30f, 0.92f, 0.45f, 1f, "Beacon colour when active.")]
        internal static readonly SharedStatic<Color> VisualActiveColor =
            SharedStatic<Color>.GetOrCreate<ActiveColorType>();

        [ConfigVar("reactiontelemetry.visual-idle-color", 0.55f, 0.22f, 0.22f, 0.60f, "Beacon colour when idle.")]
        internal static readonly SharedStatic<Color> VisualIdleColor =
            SharedStatic<Color>.GetOrCreate<IdleColorType>();

        private struct EnabledType { }    private struct FullDetailsType { }
        private struct ScaleType { }      private struct OffsetType { }
        private struct ConditionColorType { } private struct EventColorType { }
        private struct ActiveColorType { }    private struct IdleColorType { }
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

            state.Dependency = new RenderTelemetryJob
            {
                Renderer              = drawer,
                Camera                = drawSystem.CameraCulling,
                Scale                 = ReactionTelemetryConfig.Scale.Data,
                WorldOffset           = ((float4)ReactionTelemetryConfig.Offset.Data).xyz,
                ConditionAccent       = ReactionTelemetryConfig.ConditionColor.Data,
                EventAccent           = ReactionTelemetryConfig.EventColor.Data,
                FullDetails           = ReactionTelemetryConfig.FullDetails.Data,
                VisualOverlay         = TelemetryVisualConfig.VisualOverlay.Data,
                BeaconLod             = TelemetryVisualConfig.BeaconLod.Data,
                VisualActiveColor     = ReactionTelemetryConfig.VisualActiveColor.Data,
                VisualIdleColor       = ReactionTelemetryConfig.VisualIdleColor.Data,
                VisualConditionColor  = ReactionTelemetryConfig.ConditionColor.Data,
                CondBits              = TelemetryVisualConfig.CondBits.Data,
                RippleMaxR            = TelemetryVisualConfig.RippleMaxR.Data,
                RippleLife            = TelemetryVisualConfig.RippleLife.Data,
                RippleOffsetX        = TelemetryVisualConfig.RippleOffsetX.Data,
                ElapsedTime           = (float)SystemAPI.Time.ElapsedTime,
                Time                  = SystemAPI.Time.ElapsedTime,
                TransformHandle       = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                ActiveHandle          = SystemAPI.GetComponentTypeHandle<Active>(true),
                ConditionActiveHandle = SystemAPI.GetComponentTypeHandle<ConditionActive>(true),
                ConditionValuesHandle = SystemAPI.GetBufferTypeHandle<ConditionValues>(true),
                HistoryHandle         = SystemAPI.GetBufferTypeHandle<ReactionEventHistoryRecord>(true),
                DebugNames            = SystemAPI.GetSingleton<EssenceDebugNames>(),
            }.Schedule(telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderTelemetryJob : IJobChunk
        {
            public Drawer Renderer;
            public CameraCulling Camera;
            public float Scale;
            public float3 WorldOffset;
            public Color ConditionAccent;
            public Color EventAccent;
            public bool FullDetails;

            public bool VisualOverlay;
            public float BeaconLod;
            public Color VisualActiveColor;
            public Color VisualIdleColor;
            public Color VisualConditionColor;
            public int CondBits;
            public float RippleMaxR;
            public float RippleLife;
            public float RippleOffsetX;
            public float ElapsedTime;

            public double Time;

            [ReadOnly] public ComponentTypeHandle<LocalToWorld>              TransformHandle;
            [ReadOnly] public ComponentTypeHandle<Active>                    ActiveHandle;
            [ReadOnly] public ComponentTypeHandle<ConditionActive>           ConditionActiveHandle;
            [ReadOnly] public BufferTypeHandle<ConditionValues>              ConditionValuesHandle;
            [ReadOnly] public BufferTypeHandle<ReactionEventHistoryRecord>   HistoryHandle;
            [ReadOnly] public EssenceDebugNames                              DebugNames;

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
                    var v    = View.WorldFacing(Camera, head, Scale).NudgeWorld(WorldOffset);

                    var isActive  = hasActive && activeMask[index];
                    var condMask  = conditions.IsCreated ? (uint)conditions[index].Value.Data : 0u;
                    var histBuf   = history.Length > 0  ? history[index] : default;
                    var valueBuf  = values.Length  > 0  ? values[index]  : default;

                    if (VisualOverlay && v.Distance < BeaconLod)
                        DrawVisuals(v, hasActive, isActive, conditions.IsCreated, condMask, histBuf, histBuf.IsCreated);

                    var y = 0f;

                    if (hasActive)                 y = RenderState(v, y, isActive);
                    if (conditions.IsCreated)      y = RenderMask(v, y, conditions[index]);
                    if (values.Length > 0)         y = RenderValues(v, y, valueBuf);
                    if (history.Length > 0)        y = RenderEvents(v, y, histBuf);
                }
            }

            private void DrawVisuals(in View v, bool hasActive, bool isActive,
                bool hasConditions, uint condMask,
                DynamicBuffer<ReactionEventHistoryRecord> history, bool hasHistory)
            {
                var lineHeight   = TelemetryLayoutConfig.LineHeight.Data;

                if (hasActive)
                {
                    var beaconGlyphR = lineHeight * 0.45f;
                    var beaconColor  = isActive ? VisualActiveColor : VisualIdleColor;
                    VisualGlyph.Beacon(Renderer, v, -16.0f, -beaconGlyphR, beaconGlyphR, beaconColor);
                    if (isActive)
                        VisualGlyph.BeaconPulse(Renderer, v, -16.0f, -beaconGlyphR,
                            beaconGlyphR * 1.65f, ElapsedTime, VisualActiveColor);
                }

                if (hasConditions)
                {
                    var condRingGlyphR = lineHeight * 2.5f;
                    var clearColor = new Color(
                        VisualConditionColor.r * 0.08f,
                        VisualConditionColor.g * 0.08f,
                        VisualConditionColor.b * 0.08f, 0.25f);
                    VisualGlyph.ConditionRing(Renderer, v,
                        -16.0f, -(condRingGlyphR + lineHeight * 0.5f),
                        condRingGlyphR, condMask, CondBits,
                        VisualConditionColor, clearColor);
                }

                if (!hasHistory) return;
                var condRingOffset = hasConditions ? (lineHeight * 2.5f * 2f + lineHeight) : lineHeight * 2f;
                for (var i = 0; i < history.Length; i++)
                {
                    var rec  = history[i];
                    var age  = ElapsedTime - (float)rec.Timestamp;
                    var kc   = VisualGlyph.KeyColor(rec.Key, 0.75f, 0.90f);
                    VisualGlyph.Ripple(Renderer, v,
                        RippleOffsetX, -condRingOffset,
                        age, RippleLife, RippleMaxR, kc);
                }
            }

            private float RenderState(in View v, float y, bool active)
            {
                var titleSize  = TelemetryLayoutConfig.TitleSize.Data;
                var lineHeight = TelemetryLayoutConfig.LineHeight.Data;

                var text = new FixedString128Bytes();
                text.Append(active ? "[ACTIVE] " : "[IDLE] ");
                text.Append("REACTION");

                Glyph.Text(Renderer, v, 0f, y, text, active ? Ink.Live : Ink.Idle, titleSize);
                return y - lineHeight;
            }

            private float RenderMask(in View v, float y, in ConditionActive condition)
            {
                var fontSize   = TelemetryLayoutConfig.FontSize.Data;
                var lineHeight = TelemetryLayoutConfig.LineHeight.Data;

                var text = new FixedString128Bytes();
                text.Append("Cond. Mask: ");
                text.Append(condition.Value.Data);

                Glyph.Text(Renderer, v, 0f, y, text, ConditionAccent, fontSize);
                return y - lineHeight;
            }

            private float RenderValues(in View v, float y, DynamicBuffer<ConditionValues> buffer)
            {
                if (!FullDetails) return y;

                var fontSize   = TelemetryLayoutConfig.FontSize.Data;
                var lineHeight = TelemetryLayoutConfig.LineHeight.Data;
                var indent     = TelemetryLayoutConfig.Indent.Data;

                for (var i = 0; i < buffer.Length; i++)
                {
                    var raw = buffer[i].Value;
                    if (raw == 0) continue;

                    var text = new FixedString128Bytes();
                    text.Append("-> Cond[");
                    text.Append(i);
                    text.Append("]: ");
                    text.Append(raw);

                    Glyph.Text(Renderer, v, indent, y, text, Ink.Value, fontSize);
                    y -= lineHeight;
                }
                return y;
            }

            private float RenderEvents(in View v, float y, DynamicBuffer<ReactionEventHistoryRecord> buffer)
            {
                if (!FullDetails) return y;

                var fontSize   = TelemetryLayoutConfig.FontSize.Data;
                var lineHeight = TelemetryLayoutConfig.LineHeight.Data;
                var indent     = TelemetryLayoutConfig.Indent.Data;

                ref var names = ref DebugNames.Value.Value.EventNames;

                Glyph.Text(Renderer, v, 0f, y, "History:", Ink.Label, fontSize);
                y -= lineHeight;

                for (var i = 0; i < buffer.Length; i++)
                {
                    var record = buffer[i];
                    var age    = (float)(Time - record.Timestamp);
                    var fade   = math.saturate(1f - age * 0.5f);
                    var color  = Ink.Dim(EventAccent, fade);

                    var text = new FixedString128Bytes();
                    text.Append("-> [");
                    if (names.TryGetValue(record.Key, out var named)) text.Append(named.Ref);
                    else text.Append(record.Key);
                    text.Append("]: ");
                    text.Append(record.Value);

                    Glyph.Text(Renderer, v, indent, y, text, color, fontSize);
                    y -= lineHeight;
                }
                return y;
            }
        }
    }
}
#endif