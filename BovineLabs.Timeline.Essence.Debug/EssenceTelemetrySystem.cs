#if UNITY_EDITOR || BL_DEBUG
using System.Diagnostics.CodeAnalysis;
using BovineLabs.Core;
using BovineLabs.Core.Collections;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Core.ObjectManagement;
using BovineLabs.Essence.Data;
using BovineLabs.Quill;
using BovineLabs.Timeline.Core;
using BovineLabs.Timeline.Core.Debug;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Essence.Debug
{
    [Configurable]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1611:Element parameters should be documented",
        Justification = "Using see cref")]
    public static class EssenceTelemetryConfig
    {
        [ConfigVar("essencetelemetry.draw-enabled", false, "Force-enable the Essence telemetry drawer.")]
        public static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<Tags.Enabled>();

        [ConfigVar("essencetelemetry.scale", 0.04f, "Fixed world-space scale for the UI.")]
        public static readonly SharedStatic<float> Scale = SharedStatic<float>.GetOrCreate<Tags.Scale>();

        [ConfigVar("essencetelemetry.stat-offset", 0f, 1.3f, 0f, 0f, "World anchor offset for the stats panel.")]
        public static readonly SharedStatic<Vector4> StatOffset = SharedStatic<Vector4>.GetOrCreate<Tags.StatOffset>();

        [ConfigVar("essencetelemetry.intrinsic-offset", 0f, 1.3f, 0f, 0f,
            "World anchor offset for the intrinsics panel.")]
        public static readonly SharedStatic<Vector4> IntrinsicOffset =
            SharedStatic<Vector4>.GetOrCreate<Tags.IntrinsicOffset>();

        [ConfigVar("essencetelemetry.stat-color", 0.42f, 0.68f, 0.98f, 1f, "Accent for stats.")]
        public static readonly SharedStatic<Color> StatColor = SharedStatic<Color>.GetOrCreate<Tags.StatColor>();

        [ConfigVar("essencetelemetry.intrinsic-color", 0.74f, 0.56f, 1f, 1f, "Accent for intrinsics.")]
        public static readonly SharedStatic<Color> IntrinsicColor =
            SharedStatic<Color>.GetOrCreate<Tags.IntrinsicColor>();

        [ConfigVar("essencetelemetry.stat-filter", "", "Filter stats by name prefix.")]
        public static readonly SharedStatic<FixedString32Bytes> StatFilter =
            SharedStatic<FixedString32Bytes>.GetOrCreate<Tags.StatFilter>();

        [ConfigVar("essencetelemetry.intrinsic-filter", "", "Filter intrinsics by name prefix.")]
        public static readonly SharedStatic<FixedString32Bytes> IntrinsicFilter =
            SharedStatic<FixedString32Bytes>.GetOrCreate<Tags.IntrinsicFilter>();

        [ConfigVar("essencetelemetry.bars", true, "Show bars in telemetry debug.")]
        public static readonly SharedStatic<bool> Bars = SharedStatic<bool>.GetOrCreate<Tags.Bars>();

        [ConfigVar("essencetelemetry.health-stat", "",
            "Stat name (substring) drawn as a pulsing health beacon above the entity. Empty = off. " +
            "Requires the telemetry visual overlay (bovinelabs.telemetry.visual-overlay).")]
        public static readonly SharedStatic<FixedString32Bytes> HealthStat =
            SharedStatic<FixedString32Bytes>.GetOrCreate<Tags.HealthStat>();

        private struct Tags
        {
            public struct HealthStat
            {
            }

            public struct Enabled
            {
            }

            public struct Scale
            {
            }

            public struct StatOffset
            {
            }

            public struct IntrinsicOffset
            {
            }

            public struct StatColor
            {
            }

            public struct IntrinsicColor
            {
            }

            public struct StatFilter
            {
            }

            public struct IntrinsicFilter
            {
            }

            public struct Bars
            {
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    [BurstCompile]
    public partial struct EssenceTelemetrySystem : ISystem
    {
        private EntityQuery telemetryQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            telemetryQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalToWorld>()
                .WithAny<Stat, Intrinsic>()
                .Build();

            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<EssenceDebugNames>();
            state.RequireForUpdate<EssenceConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!TimelineDebugUtility.TryGetDrawer<EssenceTelemetrySystem>(ref state,
                    EssenceTelemetryConfig.Enabled.Data, out var drawer))
                return;

            state.Dependency = new RenderJob
            {
                Renderer = drawer,
                Camera = SystemAPI.GetSingleton<DrawSystem.Singleton>().CameraCulling,
                Scale = EssenceTelemetryConfig.Scale.Data,
                StatWorldOffset = ((float4)EssenceTelemetryConfig.StatOffset.Data).xyz,
                IntrinsicWorldOffset = ((float4)EssenceTelemetryConfig.IntrinsicOffset.Data).xyz,
                StatAccent = EssenceTelemetryConfig.StatColor.Data,
                StatFilter = EssenceTelemetryConfig.StatFilter.Data,
                IntrinsicAccent = EssenceTelemetryConfig.IntrinsicColor.Data,
                IntrinsicFilter = EssenceTelemetryConfig.IntrinsicFilter.Data,
                ShowBars = EssenceTelemetryConfig.Bars.Data,
                PanelSpacing = TelemetryConfig.PanelSpacing.Data,
                UseLogFill = TelemetryConfig.LogFill.Data,
                DrawBeacon = TelemetryVisualConfig.VisualOverlay.Data &&
                             !EssenceTelemetryConfig.HealthStat.Data.IsEmpty,
                HealthStat = EssenceTelemetryConfig.HealthStat.Data,
                Time = (float)SystemAPI.Time.ElapsedTime,
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                LocalTransformHandle = SystemAPI.GetComponentTypeHandle<LocalTransform>(true),
                ParentHandle = SystemAPI.GetComponentTypeHandle<Parent>(true),
                StatHandle = SystemAPI.GetBufferTypeHandle<Stat>(true),
                IntrinsicHandle = SystemAPI.GetBufferTypeHandle<Intrinsic>(true),
                TrendHandle = SystemAPI.GetBufferTypeHandle<StatTrendSample>(true),
                StatDefaultsHandle = SystemAPI.GetComponentTypeHandle<StatDefaults>(true),
                StatModifiersHandle = SystemAPI.GetBufferTypeHandle<StatModifiers>(true),
                DebugNames = SystemAPI.GetSingleton<EssenceDebugNames>(),
                Config = SystemAPI.GetSingleton<EssenceConfig>()
            }.Schedule(telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderJob : IJobChunk
        {
            public Drawer Renderer;
            public CameraCulling Camera;
            public float Scale;
            public float3 StatWorldOffset;
            public float3 IntrinsicWorldOffset;
            public float PanelSpacing;
            public Color StatAccent;
            public FixedString32Bytes StatFilter;
            public bool ShowBars;
            public Color IntrinsicAccent;
            public FixedString32Bytes IntrinsicFilter;
            public bool UseLogFill;
            public bool DrawBeacon;
            public FixedString32Bytes HealthStat;
            public float Time;

            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public ComponentTypeHandle<LocalTransform> LocalTransformHandle;
            [ReadOnly] public ComponentTypeHandle<Parent> ParentHandle;

            [ReadOnly] public BufferTypeHandle<Stat> StatHandle;
            [ReadOnly] public BufferTypeHandle<Intrinsic> IntrinsicHandle;
            [ReadOnly] public BufferTypeHandle<StatTrendSample> TrendHandle;
            [ReadOnly] public ComponentTypeHandle<StatDefaults> StatDefaultsHandle;
            [ReadOnly] public BufferTypeHandle<StatModifiers> StatModifiersHandle;
            [ReadOnly] public EssenceDebugNames DebugNames;
            [ReadOnly] public EssenceConfig Config;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var hasLocalTransform = chunk.Has(ref LocalTransformHandle);
                var hasParent = chunk.Has(ref ParentHandle);
                var localTransforms = hasLocalTransform ? chunk.GetNativeArray(ref LocalTransformHandle) : default;
                var statBuffers = chunk.GetBufferAccessor(ref StatHandle);
                var intrinsicBuffers = chunk.GetBufferAccessor(ref IntrinsicHandle);
                var trendBuffers = chunk.GetBufferAccessor(ref TrendHandle);

                var hasStats = statBuffers.Length > 0;
                var hasIntrinsics = intrinsicBuffers.Length > 0;
                var hasTrends = trendBuffers.Length > 0;
                var hasBoth = hasStats && hasIntrinsics;

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var head = hasLocalTransform && !hasParent
                        ? localTransforms[index].Position
                        : transforms[index].Position;

                    if (DrawBeacon && hasStats)
                        DrawHealthBeacon(head, index, chunk, statBuffers[index]);

                    if (hasStats)
                        EmitStatsForEntity(head, index, chunk, statBuffers, trendBuffers, hasTrends, hasBoth);

                    if (hasIntrinsics)
                        EmitIntrinsicsForEntity(head, index, chunk, intrinsicBuffers, statBuffers, hasStats, hasBoth);
                }
            }

            private void EmitStatsForEntity(float3 head, int index, in ArchetypeChunk chunk,
                BufferAccessor<Stat> statBuffers, BufferAccessor<StatTrendSample> trendBuffers,
                bool hasTrends, bool hasBoth)
            {
                var statView = View.WorldFacing(Camera, head, Scale)
                    .NudgeWorld(StatWorldOffset);
                if (hasBoth) statView = statView.Shift(-PanelSpacing * 0.5f, 0f);

                EmitStatPanel(statView, index, chunk,
                    statBuffers[index],
                    hasTrends ? trendBuffers[index] : default);
            }

            private void EmitIntrinsicsForEntity(float3 head, int index, in ArchetypeChunk chunk,
                BufferAccessor<Intrinsic> intrinsicBuffers, BufferAccessor<Stat> statBuffers,
                bool hasStats, bool hasBoth)
            {
                var intrView = View.WorldFacing(Camera, head, Scale)
                    .NudgeWorld(IntrinsicWorldOffset);
                if (hasBoth) intrView = intrView.Shift(PanelSpacing * 0.5f, 0f);

                EmitIntrinsicPanel(intrView, index, chunk,
                    intrinsicBuffers[index],
                    hasStats ? statBuffers[index] : default);
            }

            private void EmitStatPanel(in View v, int entityIndex, in ArchetypeChunk chunk,
                DynamicBuffer<Stat> stats, DynamicBuffer<StatTrendSample> trends)
            {
                var fontSize = TelemetryConfig.FontSize.Data;
                ref var names = ref DebugNames.Value.Value.StatNames;

                var hasDefaults = chunk.Has(ref StatDefaultsHandle);
                var hasModifiers = chunk.Has(ref StatModifiersHandle);
                var defaultsArr = hasDefaults ? chunk.GetNativeArray(ref StatDefaultsHandle) : default;
                var modifiersAcc = hasModifiers ? chunk.GetBufferAccessor(ref StatModifiersHandle) : default;

                var y = 0f;

                var title = new FixedString128Bytes();
                title.Append('S');
                title.Append('T');
                title.Append('A');
                title.Append('T');
                title.Append('S');
                Glyph.TitleRow(Renderer, v, y, title, StatAccent);
                y = Glyph.AdvanceLine(y);
                y = Glyph.AdvanceGroup(y);

                foreach (var stat in stats.AsMap())
                {
                    var name = ResolveName(ref names, new BLId(stat.Key.Value));
                    if (IsFiltered(name, StatFilter)) continue;

                    var fill = ShowBars
                        ? ComputeStatFill(new BLId(stat.Key.Value), stat.Value.Value,
                            defaultsArr, entityIndex, hasDefaults, UseLogFill)
                        : 0f;

                    var label = new FixedString128Bytes();
                    label.Append(name);
                    label.Append(':');
                    label.Append(' ');
                    label.Append(stat.Value.Value);
                    AppendTrendDelta(ref label, new BLId(stat.Key.Value), trends);

                    if (ShowBars)
                        Glyph.BarRow(Renderer, v, 0f, y, label, fill, StatAccent, fontSize);
                    else
                        Glyph.Text(Renderer, v, 0f, y, label, Ink.Value, fontSize);
                    y = Glyph.AdvanceLine(y);

                    if (hasDefaults)
                        y = EmitStatDefaults(v, y, new BLId(stat.Key.Value), defaultsArr[entityIndex], fontSize);

                    if (hasModifiers)
                        y = EmitStatModifiers(v, y, new BLId(stat.Key.Value), modifiersAcc[entityIndex], fontSize);

                    y = Glyph.AdvanceGroup(y);
                }
            }

            private float EmitStatDefaults(in View v, float y, BLId statKey,
                StatDefaults defaults, float fontSize)
            {
                ref var baseArray = ref defaults.Value.Value.Default;
                for (var i = 0; i < baseArray.Length; i++)
                {
                    if (new BLId(baseArray[i].Type.Value) != statKey) continue;

                    var detail = new FixedString128Bytes();
                    detail.Append('B');
                    detail.Append('a');
                    detail.Append('s');
                    detail.Append('e');
                    detail.Append(':');
                    detail.Append(' ');
                    FormatModifier(ref detail, baseArray[i]);
                    Glyph.DetailRow(Renderer, v, y, detail, fontSize);
                    y = Glyph.AdvanceLine(y);
                }

                return y;
            }

            private float EmitStatModifiers(in View v, float y, BLId statKey,
                DynamicBuffer<StatModifiers> mods, float fontSize)
            {
                for (var i = 0; i < mods.Length; i++)
                {
                    if (new BLId(mods[i].Value.Type.Value) != statKey) continue;

                    var detail = new FixedString128Bytes();
                    detail.Append('M');
                    detail.Append('o');
                    detail.Append('d');
                    detail.Append(':');
                    detail.Append(' ');
                    FormatModifier(ref detail, mods[i].Value);
                    detail.Append(' ');
                    detail.Append('S');
                    detail.Append('r');
                    detail.Append('c');
                    detail.Append(':');
                    detail.Append(mods[i].SourceEntity.Index);
                    Glyph.DetailRow(Renderer, v, y, detail, fontSize);
                    y = Glyph.AdvanceLine(y);
                }

                return y;
            }

            private void EmitIntrinsicPanel(in View v, int entityIndex, in ArchetypeChunk chunk,
                DynamicBuffer<Intrinsic> intrinsics, DynamicBuffer<Stat> stats)
            {
                var fontSize = TelemetryConfig.FontSize.Data;
                ref var names = ref DebugNames.Value.Value.IntrinsicNames;
                ref var configs = ref Config.Value.Value.IntrinsicDatas;

                var y = 0f;

                var title = new FixedString128Bytes();
                title.Append('I');
                title.Append('N');
                title.Append('T');
                title.Append('R');
                title.Append('I');
                title.Append('N');
                title.Append('S');
                title.Append('I');
                title.Append('C');
                title.Append('S');
                Glyph.TitleRow(Renderer, v, y, title, IntrinsicAccent);
                y = Glyph.AdvanceLine(y);
                y = Glyph.AdvanceGroup(y);

                foreach (var intrinsic in intrinsics.AsMap())
                {
                    var name = ResolveName(ref names, new BLId(intrinsic.Key.Value));
                    if (IsFiltered(name, IntrinsicFilter)) continue;

                    var resolved = ResolveIntrinsicRange(intrinsic.Key, stats, ref configs);
                    var fill = resolved.Range > 0
                        ? UseLogFill
                            ? Glyph.LogFill(intrinsic.Value, resolved.Min, resolved.Max)
                            : Glyph.LinearFill(intrinsic.Value, resolved.Min, resolved.Max)
                        : 0.5f;

                    var label = new FixedString128Bytes();
                    label.Append(name);
                    label.Append(':');
                    label.Append(' ');
                    label.Append(intrinsic.Value);

                    if (resolved.Range > 0)
                    {
                        label.Append(' ');
                        label.Append('[');
                        label.Append(resolved.Min);
                        label.Append('.');
                        label.Append('.');
                        label.Append(resolved.Max);
                        label.Append(']');
                    }

                    if (ShowBars)
                        Glyph.BarRow(Renderer, v, 0f, y, label, fill, IntrinsicAccent, fontSize);
                    else
                        Glyph.Text(Renderer, v, 0f, y, label, Ink.Value, fontSize);
                    y = Glyph.AdvanceLine(y);

                    if (resolved.HasStatBounds)
                    {
                        var detail = new FixedString128Bytes();
                        detail.Append('M');
                        detail.Append('i');
                        detail.Append('n');
                        detail.Append(':');
                        detail.Append(' ');
                        detail.Append(resolved.Min);
                        detail.Append(' ');
                        detail.Append('|');
                        detail.Append(' ');
                        detail.Append('M');
                        detail.Append('a');
                        detail.Append('x');
                        detail.Append(':');
                        detail.Append(' ');
                        detail.Append(resolved.Max);
                        detail.Append(' ');
                        detail.Append('(');
                        detail.Append('s');
                        detail.Append('t');
                        detail.Append('a');
                        detail.Append('t');
                        detail.Append('-');
                        detail.Append('d');
                        detail.Append('r');
                        detail.Append('i');
                        detail.Append('v');
                        detail.Append('e');
                        detail.Append('n');
                        detail.Append(')');
                        Glyph.DetailRow(Renderer, v, y, detail, fontSize);
                        y = Glyph.AdvanceLine(y);
                    }

                    y = Glyph.AdvanceGroup(y);
                }
            }

            private const float BeaconHeight = 1.9f;
            private const float BeaconRadius = 1.5f;

            private void DrawHealthBeacon(float3 head, int entityIndex, in ArchetypeChunk chunk,
                DynamicBuffer<Stat> stats)
            {
                ref var names = ref DebugNames.Value.Value.StatNames;
                var hasDefaults = chunk.Has(ref StatDefaultsHandle);
                var defaultsArr = hasDefaults ? chunk.GetNativeArray(ref StatDefaultsHandle) : default;

                foreach (var stat in stats.AsMap())
                {
                    var name = ResolveName(ref names, new BLId(stat.Key.Value));
                    if (name.IndexOf(HealthStat) == -1)
                        continue;

                    var fill = ComputeStatFill(new BLId(stat.Key.Value), stat.Value.Value, defaultsArr, entityIndex,
                        hasDefaults, false);
                    var view = View.WorldFacing(Camera, head, Scale).NudgeWorld(new float3(0f, BeaconHeight, 0f));
                    VisualGlyph.BeaconPulse(Renderer, view, 0f, 0f, BeaconRadius, Time,
                        VisualGlyph.HealthGradient(fill));
                    return;
                }
            }

            private static float ComputeStatFill(BLId key, float value,
                NativeArray<StatDefaults> defaultsArr, int entityIndex, bool hasDefaults, bool useLog)
            {
                var max = 100f;

                if (hasDefaults && defaultsArr.IsCreated)
                {
                    ref var arr = ref defaultsArr[entityIndex].Value.Value.Default;
                    for (var i = 0; i < arr.Length; i++)
                        if (new BLId(arr[i].Type.Value) == key && arr[i].ModifyType == StatModifyType.Added)
                        {
                            max = math.max(1f, arr[i].Value);
                            break;
                        }
                }

                return useLog
                    ? Glyph.LogFill(value, 0f, max)
                    : Glyph.LinearFill(value, 0f, max);
            }

            private struct ResolvedRange
            {
                public int Min;
                public int Max;
                public int Range;
                public bool HasStatBounds;
            }

            private static ResolvedRange ResolveIntrinsicRange(
                in IntrinsicKey key, DynamicBuffer<Stat> stats,
                ref BlobHashMap<IntrinsicKey, EssenceConfig.IntrinsicData> configs)
            {
                if (!configs.TryGetValue(key, out var data))
                    return default;

                var min = data.Ref.Min;
                var max = data.Ref.Max;
                var hasStatBounds = false;

                if (stats.IsCreated)
                {
                    var statMap = stats.AsMap();
                    if (!data.Ref.MinStatKey.Value.IsNull() && statMap.TryGetValue(data.Ref.MinStatKey, out var minStat))
                    {
                        min = (int)math.floor(minStat.Value);
                        hasStatBounds = true;
                    }

                    if (!data.Ref.MaxStatKey.Value.IsNull() && statMap.TryGetValue(data.Ref.MaxStatKey, out var maxStat))
                    {
                        max = (int)math.floor(maxStat.Value);
                        hasStatBounds = true;
                    }
                }

                return new ResolvedRange
                {
                    Min = min,
                    Max = max,
                    Range = max > min ? max - min : 0,
                    HasStatBounds = hasStatBounds
                };
            }

            private static void AppendTrendDelta(ref FixedString128Bytes label, BLId key,
                DynamicBuffer<StatTrendSample> trends)
            {
                if (!trends.IsCreated || trends.Length < 2) return;

                float last = 0f, prev = 0f;
                var found = 0;
                for (var i = trends.Length - 1; i >= 0 && found < 2; i--)
                {
                    if (trends[i].Key != key) continue;
                    if (found == 0) last = trends[i].Value;
                    else prev = trends[i].Value;
                    found++;
                }

                if (found < 2) return;
                var delta = last - prev;
                if (math.abs(delta) <= 0.001f) return;

                label.Append(' ');
                label.Append('(');
                if (delta > 0) label.Append('+');
                label.Append(delta);
                label.Append(')');
            }

            private static void FormatModifier(ref FixedString128Bytes str, StatModifier mod)
            {
                switch (mod.ModifyType)
                {
                    case StatModifyType.Added:
                        if (mod.Value >= 0) str.Append('+');
                        str.Append(mod.Value);
                        break;
                    case StatModifyType.Additive:
                        if (mod.ValueFloat >= 0) str.Append('+');
                        str.Append(mod.ValueFloat * 100f);
                        str.Append('%');
                        str.Append(' ');
                        str.Append('A');
                        str.Append('d');
                        str.Append('d');
                        break;
                    case StatModifyType.Multiplicative:
                        str.Append('x');
                        str.Append(1f + mod.ValueFloat);
                        str.Append(' ');
                        str.Append('M');
                        str.Append('u');
                        str.Append('l');
                        break;
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

            private static bool IsFiltered(FixedString32Bytes name, in FixedString32Bytes filter)
            {
                return !filter.IsEmpty && name.IndexOf(filter) == -1;
            }
        }
    }
}
#endif