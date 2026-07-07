#if UNITY_EDITOR || BL_DEBUG
using System.Diagnostics.CodeAnalysis;
using BovineLabs.Core;
using BovineLabs.Core.Collections;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.ObjectManagement;
using BovineLabs.Essence.Debug;
using BovineLabs.Quill;
using BovineLabs.Reaction.Data.Active;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Groups;
using BovineLabs.Timeline.Core.Debug;
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
        [ConfigVar("reactiontelemetry.draw-enabled", false, "Force-enable the Reaction telemetry drawer.")]
        public static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<Tags.Enabled>();

        [ConfigVar("reactiontelemetry.scale", 0.04f, "Fixed world-space scale for the UI.")]
        public static readonly SharedStatic<float> Scale = SharedStatic<float>.GetOrCreate<Tags.Scale>();

        [ConfigVar("reactiontelemetry.offset", 0f, 2.4f, 0f, 0f, "World anchor offset for the panel.")]
        public static readonly SharedStatic<Vector4> Offset = SharedStatic<Vector4>.GetOrCreate<Tags.Offset>();

        [ConfigVar("reactiontelemetry.condition-color", 1f, 0.8f, 0.2f, 1f, "Accent for conditions.")]
        public static readonly SharedStatic<Color> ConditionColor =
            SharedStatic<Color>.GetOrCreate<Tags.ConditionColor>();

        [ConfigVar("reactiontelemetry.event-color", 0.95f, 0.5f, 0.3f, 1f, "Accent for events.")]
        public static readonly SharedStatic<Color> EventColor = SharedStatic<Color>.GetOrCreate<Tags.EventColor>();

        [ConfigVar("reactiontelemetry.cond-bits", 16, "Number of condition bits to display.")]
        public static readonly SharedStatic<int> CondBits = SharedStatic<int>.GetOrCreate<Tags.CondBits>();

        private struct Tags
        {
            public struct Enabled
            {
            }

            public struct Scale
            {
            }

            public struct Offset
            {
            }

            public struct ConditionColor
            {
            }

            public struct EventColor
            {
            }

            public struct CondBits
            {
            }
        }
    }

    [InternalBufferCapacity(8)]
    public struct ReactionEventHistoryRecord : IBufferElementData, ITimestampedRecord
    {
        public BLId Key;
        public int Value;
        public double Timestamp;

        double ITimestampedRecord.Timestamp => Timestamp;
    }

    [UpdateInGroup(typeof(ConditionWriteEventsGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation)]
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
                if (events.Length > 0)
                    ecb.AddBuffer<ReactionEventHistoryRecord>(entity);

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            foreach (var (events, history) in
                     SystemAPI.Query<DynamicBuffer<ConditionEvent>, DynamicBuffer<ReactionEventHistoryRecord>>())
            {
                while (history.Length > 0 && time - history[history.Length - 1].Timestamp > RetentionWindow)
                    history.RemoveAt(history.Length - 1);

                if (events.Length == 0) continue;

                foreach (var kvp in events.AsMap())
                    history.Insert(0, new ReactionEventHistoryRecord
                    {
                        Key = kvp.Key.Value,
                        Value = kvp.Value.Read<int>(),
                        Timestamp = time
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

        public void OnUpdate(ref SystemState state)
        {
            if (!TimelineDebugUtility.TryGetDrawer<ReactionTelemetrySystem>(ref state,
                    ReactionTelemetryConfig.Enabled.Data, out var drawer))
                return;

            state.Dependency = new RenderJob
            {
                Renderer = drawer,
                Camera = SystemAPI.GetSingleton<DrawSystem.Singleton>().CameraCulling,
                Scale = ReactionTelemetryConfig.Scale.Data,
                WorldOffset = ((float4)ReactionTelemetryConfig.Offset.Data).xyz,
                ConditionAccent = ReactionTelemetryConfig.ConditionColor.Data,
                EventAccent = ReactionTelemetryConfig.EventColor.Data,
                CondBits = ReactionTelemetryConfig.CondBits.Data,
                Time = SystemAPI.Time.ElapsedTime,
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                LocalTransformHandle = SystemAPI.GetComponentTypeHandle<LocalTransform>(true),
                ParentHandle = SystemAPI.GetComponentTypeHandle<Parent>(true),
                ActiveHandle = SystemAPI.GetComponentTypeHandle<Active>(true),
                ConditionActiveHandle = SystemAPI.GetComponentTypeHandle<ConditionActive>(true),
                ConditionValuesHandle = SystemAPI.GetBufferTypeHandle<ConditionValues>(true),
                HistoryHandle = SystemAPI.GetBufferTypeHandle<ReactionEventHistoryRecord>(true),
                DebugNames = SystemAPI.GetSingleton<EssenceDebugNames>()
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

            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public ComponentTypeHandle<LocalTransform> LocalTransformHandle;
            [ReadOnly] public ComponentTypeHandle<Parent> ParentHandle;

            [ReadOnly] public ComponentTypeHandle<Active> ActiveHandle;
            [ReadOnly] public ComponentTypeHandle<ConditionActive> ConditionActiveHandle;
            [ReadOnly] public BufferTypeHandle<ConditionValues> ConditionValuesHandle;
            [ReadOnly] public BufferTypeHandle<ReactionEventHistoryRecord> HistoryHandle;
            [ReadOnly] public EssenceDebugNames DebugNames;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var hasLocalTransform = chunk.Has(ref LocalTransformHandle);
                var hasParent = chunk.Has(ref ParentHandle);
                var localTransforms = hasLocalTransform ? chunk.GetNativeArray(ref LocalTransformHandle) : default;
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
                    var head = hasLocalTransform && !hasParent
                        ? localTransforms[index].Position
                        : transforms[index].Position;
                    var v = View.WorldFacing(Camera, head, Scale).NudgeWorld(WorldOffset);

                    var y = 0f;

                    if (hasActive)
                        y = EmitActiveState(v, y, activeMask[index]);

                    if (hasConditions)
                        y = EmitConditionBits(v, y, conditions[index].Value.Data);

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
                if (isActive)
                {
                    label.Append('A');
                    label.Append('C');
                    label.Append('T');
                    label.Append('I');
                    label.Append('V');
                    label.Append('E');
                }
                else
                {
                    label.Append('I');
                    label.Append('D');
                    label.Append('L');
                    label.Append('E');
                }

                label.Append(' ');
                label.Append('R');
                label.Append('E');
                label.Append('A');
                label.Append('C');
                label.Append('T');
                label.Append('I');
                label.Append('O');
                label.Append('N');

                Glyph.BarRow(Renderer, v, 0f, y, label, fill, accent, TelemetryConfig.TitleSize.Data);
                return Glyph.AdvanceLine(y);
            }

            private float EmitConditionBits(in View v, float y, uint mask)
            {
                var fontSize = TelemetryConfig.FontSize.Data;
                var bits = math.min(CondBits, 32);

                var headerLabel = new FixedString128Bytes();
                headerLabel.Append('C');
                headerLabel.Append('o');
                headerLabel.Append('n');
                headerLabel.Append('d');
                headerLabel.Append('i');
                headerLabel.Append('t');
                headerLabel.Append('i');
                headerLabel.Append('o');
                headerLabel.Append('n');
                headerLabel.Append('s');
                headerLabel.Append(' ');
                headerLabel.Append('(');
                headerLabel.Append(mask);
                headerLabel.Append(')');
                Glyph.TitleRow(Renderer, v, y, headerLabel, ConditionAccent);
                y = Glyph.AdvanceLine(y);

                for (var i = 0; i < bits; i++)
                {
                    var isSet = (mask & (1u << i)) != 0;
                    var fill = isSet ? 1f : 0f;

                    var bitLabel = new FixedString128Bytes();
                    bitLabel.Append('B');
                    bitLabel.Append('i');
                    bitLabel.Append('t');
                    bitLabel.Append(' ');
                    bitLabel.Append(i);
                    if (isSet)
                    {
                        bitLabel.Append(':');
                        bitLabel.Append(' ');
                        bitLabel.Append('S');
                        bitLabel.Append('E');
                        bitLabel.Append('T');
                    }
                    else
                    {
                        bitLabel.Append(':');
                        bitLabel.Append(' ');
                        bitLabel.Append('c');
                        bitLabel.Append('l');
                        bitLabel.Append('e');
                        bitLabel.Append('a');
                        bitLabel.Append('r');
                    }

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
                    if (values[i].Byte != 0)
                    {
                        anyNonZero = true;
                        break;
                    }

                if (!anyNonZero) return y;

                var header = new FixedString128Bytes();
                header.Append('C');
                header.Append('o');
                header.Append('n');
                header.Append('d');
                header.Append('i');
                header.Append('t');
                header.Append('i');
                header.Append('o');
                header.Append('n');
                header.Append(' ');
                header.Append('V');
                header.Append('a');
                header.Append('l');
                header.Append('u');
                header.Append('e');
                header.Append('s');
                Glyph.TitleRow(Renderer, v, y, header, ConditionAccent);
                y = Glyph.AdvanceLine(y);

                for (var i = 0; i < values.Length; i++)
                {
                    var raw = values[i].Byte;
                    if (raw == 0) continue;

                    var detail = new FixedString128Bytes();
                    detail.Append('C');
                    detail.Append('o');
                    detail.Append('n');
                    detail.Append('d');
                    detail.Append('[');
                    detail.Append(i);
                    detail.Append(']');
                    detail.Append(':');
                    detail.Append(' ');
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
                header.Append('E');
                header.Append('v');
                header.Append('e');
                header.Append('n');
                header.Append('t');
                header.Append(' ');
                header.Append('H');
                header.Append('i');
                header.Append('s');
                header.Append('t');
                header.Append('o');
                header.Append('r');
                header.Append('y');
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
                    label.Append(':');
                    label.Append(' ');
                    label.Append(record.Value);
                    label.Append(' ');
                    label.Append('(');
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
                    str.Append('m');
                    str.Append('s');
                }
                else
                {
                    str.Append(ageSeconds);
                    str.Append('s');
                }
            }

            private static FixedString32Bytes ResolveName(
                ref BlobHashMap<BLId, FixedString32Bytes> names, BLId key)
            {
                if (names.TryGetValue(key, out var named)) return named.Ref;
                var fallback = new FixedString32Bytes();
                fallback.Append(key.ID);
                return fallback;
            }
        }
    }
}
#endif