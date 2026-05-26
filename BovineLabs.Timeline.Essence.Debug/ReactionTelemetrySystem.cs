#if UNITY_EDITOR || BL_DEBUG
using System.Diagnostics.CodeAnalysis;
using BovineLabs.Core;
using BovineLabs.Core.Collections;
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
        [ConfigVar("reaction-telemetry.force-draw", false, "Force-enable the Reaction telemetry drawer.")]
        internal static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<K01>();

        [ConfigVar("reaction-telemetry.scale", 0.04f, "Fixed world-space scale for the UI.")]
        internal static readonly SharedStatic<float> Scale = SharedStatic<float>.GetOrCreate<K02>();

        [ConfigVar("reaction-telemetry.offset", 0f, 2.4f, 0f, 0f, "World anchor offset for the panel.")]
        internal static readonly SharedStatic<Vector4> Offset = SharedStatic<Vector4>.GetOrCreate<K03>();

        [ConfigVar("reaction-telemetry.condition-color", 1f, 0.8f, 0.2f, 1f, "Accent for conditions.")]
        internal static readonly SharedStatic<Color> ConditionColor = SharedStatic<Color>.GetOrCreate<K04>();

        [ConfigVar("reaction-telemetry.event-color", 0.95f, 0.5f, 0.3f, 1f, "Accent for events.")]
        internal static readonly SharedStatic<Color> EventColor = SharedStatic<Color>.GetOrCreate<K05>();

        [ConfigVar("reaction-telemetry.cond-bits", 16, "Number of condition bits to display.")]
        internal static readonly SharedStatic<int> CondBits = SharedStatic<int>.GetOrCreate<K06>();

        private struct K01 { } private struct K02 { } private struct K03 { }
        private struct K04 { } private struct K05 { } private struct K06 { }
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
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

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
                        Key = kvp.Key.Value,
                        Value = kvp.Value,
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

            state.Dependency = new RenderJob
            {
                Renderer              = drawer,
                Camera                = drawSystem.CameraCulling,
                Scale                 = ReactionTelemetryConfig.Scale.Data,
                WorldOffset           = ((float4)ReactionTelemetryConfig.Offset.Data).xyz,
                ConditionAccent       = ReactionTelemetryConfig.ConditionColor.Data,
                EventAccent           = ReactionTelemetryConfig.EventColor.Data,
                CondBits              = ReactionTelemetryConfig.CondBits.Data,
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
        private struct RenderJob : IJobChunk
        {
            public Drawer Renderer;
            public CameraCulling Camera;
            public float Scale;
            public float3 WorldOffset;
            public Color ConditionAccent;
            public Color EventAccent;
            public int CondBits;
            public double Time;

            [ReadOnly] public ComponentTypeHandle<LocalToWorld>            TransformHandle;
            [ReadOnly] public ComponentTypeHandle<Active>                  ActiveHandle;
            [ReadOnly] public ComponentTypeHandle<ConditionActive>         ConditionActiveHandle;
            [ReadOnly] public BufferTypeHandle<ConditionValues>            ConditionValuesHandle;
            [ReadOnly] public BufferTypeHandle<ReactionEventHistoryRecord> HistoryHandle;
            [ReadOnly] public EssenceDebugNames                           DebugNames;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var conditions = chunk.GetNativeArray(ref ConditionActiveHandle);
                var valuesAcc = chunk.GetBufferAccessor(ref ConditionValuesHandle);
                var historyAcc = chunk.GetBufferAccessor(ref HistoryHandle);

                var hasActive = chunk.Has(ref ActiveHandle);
                var activeMask = hasActive ? chunk.GetEnabledMaskRO(ref ActiveHandle) : default;
                var hasConditions = conditions.IsCreated;
                var hasValues = valuesAcc.Length > 0;
                var hasHistory = historyAcc.Length > 0;

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var head = transforms[index].Position;
                    var v = View.WorldFacing(Camera, head, Scale).NudgeWorld(WorldOffset);

                    var y = 0f;

                    if (hasActive)
                        y = EmitActiveState(v, y, activeMask[index]);

                    if (hasConditions)
                        y = EmitConditionBits(v, y, (uint)conditions[index].Value.Data);

                    if (hasValues)
                        y = EmitConditionValues(v, y, valuesAcc[index]);

                    if (hasHistory)
                        EmitEventHistory(v, y, historyAcc[index]);
                }
            }

            private float EmitActiveState(in View v, float y, bool isActive)
            {
                var fill = isActive ? 1f : 0f;
                var accent = isActive ? Ink.Live : Ink.Idle;

                var label = new FixedString128Bytes();
                label.Append(isActive ? "ACTIVE" : "IDLE");
                label.Append(" REACTION");

                Glyph.BarRow(Renderer, v, 0f, y, label, fill, accent, TelemetryConfig.TitleSize.Data);
                return Glyph.AdvanceLine(y);
            }

            private float EmitConditionBits(in View v, float y, uint mask)
            {
                var fontSize = TelemetryConfig.FontSize.Data;
                var bits = math.min(CondBits, 32);

                var headerLabel = new FixedString128Bytes();
                headerLabel.Append("Conditions (");
                headerLabel.Append(mask);
                headerLabel.Append(')');
                Glyph.TitleRow(Renderer, v, y, headerLabel, ConditionAccent);
                y = Glyph.AdvanceLine(y);

                for (var i = 0; i < bits; i++)
                {
                    var isSet = (mask & (1u << i)) != 0;
                    var fill = isSet ? 1f : 0f;

                    var bitLabel = new FixedString128Bytes();
                    bitLabel.Append("Bit ");
                    bitLabel.Append(i);
                    bitLabel.Append(isSet ? ": SET" : ": clear");

                    Glyph.BarRow(Renderer, v, 0f, y, bitLabel, fill, ConditionAccent, fontSize);
                    y = Glyph.AdvanceLine(y);
                }

                y = Glyph.AdvanceGroup(y);
                return y;
            }

            private float EmitConditionValues(in View v, float y, DynamicBuffer<ConditionValues> values)
            {
                var fontSize = TelemetryConfig.FontSize.Data;
                var anyNonZero = false;

                for (var i = 0; i < values.Length; i++)
                    if (values[i].Value != 0) { anyNonZero = true; break; }

                if (!anyNonZero) return y;

                var header = new FixedString128Bytes();
                header.Append("Condition Values");
                Glyph.TitleRow(Renderer, v, y, header, ConditionAccent);
                y = Glyph.AdvanceLine(y);

                for (var i = 0; i < values.Length; i++)
                {
                    var raw = values[i].Value;
                    if (raw == 0) continue;

                    var detail = new FixedString128Bytes();
                    detail.Append("Cond[");
                    detail.Append(i);
                    detail.Append("]: ");
                    detail.Append(raw);

                    Glyph.DetailRow(Renderer, v, y, detail, fontSize);
                    y = Glyph.AdvanceLine(y);
                }

                y = Glyph.AdvanceGroup(y);
                return y;
            }

            private float EmitEventHistory(in View v, float y, DynamicBuffer<ReactionEventHistoryRecord> history)
            {
                if (history.Length == 0) return y;

                var fontSize = TelemetryConfig.FontSize.Data;
                ref var names = ref DebugNames.Value.Value.EventNames;

                var header = new FixedString128Bytes();
                header.Append("Event History");
                Glyph.TitleRow(Renderer, v, y, header, EventAccent);
                y = Glyph.AdvanceLine(y);

                for (var i = 0; i < history.Length; i++)
                {
                    var record = history[i];
                    var age = (float)(Time - record.Timestamp);
                    var recency = math.saturate(1f - age * 0.5f);
                    var fadedAccent = Ink.Dim(EventAccent, recency);

                    var eventName = ResolveName(ref names, record.Key);

                    var label = new FixedString128Bytes();
                    label.Append(eventName);
                    label.Append(": ");
                    label.Append(record.Value);
                    label.Append(" (");
                    AppendAge(ref label, age);
                    label.Append(')');

                    Glyph.BarRow(Renderer, v, 0f, y, label, recency, fadedAccent, fontSize);
                    y = Glyph.AdvanceLine(y);
                }

                return y;
            }

            private static void AppendAge(ref FixedString128Bytes str, float ageSeconds)
            {
                var ms = (int)(ageSeconds * 1000f);
                if (ms < 1000)
                {
                    str.Append(ms);
                    str.Append("ms");
                }
                else
                {
                    str.Append(ageSeconds);
                    str.Append('s');
                }
            }

            private static FixedString32Bytes ResolveName(
                ref BlobHashMap<ushort, FixedString32Bytes> names, ushort key)
            {
                if (names.TryGetValue(key, out var named)) return named.Ref;
                var fallback = new FixedString32Bytes();
                fallback.Append(key);
                return fallback;
            }
        }
    }
}
#endif