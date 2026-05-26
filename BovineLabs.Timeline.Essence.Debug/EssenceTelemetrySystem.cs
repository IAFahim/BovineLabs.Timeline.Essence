#if UNITY_EDITOR || BL_DEBUG
using System.Diagnostics.CodeAnalysis;
using BovineLabs.Core;
using BovineLabs.Core.Collections;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Essence.Data;
using BovineLabs.Quill;
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
        [ConfigVar("essence-telemetry.force-draw", false, "Force-enable the Essence telemetry drawer.")]
        internal static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<K01>();

        [ConfigVar("essence-telemetry.scale", 0.04f, "Fixed world-space scale for the UI.")]
        internal static readonly SharedStatic<float> Scale = SharedStatic<float>.GetOrCreate<K02>();

        [ConfigVar("essence-telemetry.stat-offset", 0f, 1.3f, 0f, 0f, "World anchor offset for the stats panel.")]
        internal static readonly SharedStatic<Vector4> StatOffset = SharedStatic<Vector4>.GetOrCreate<K03>();

        [ConfigVar("essence-telemetry.intrinsic-offset", 0f, 1.3f, 0f, 0f, "World anchor offset for the intrinsics panel.")]
        internal static readonly SharedStatic<Vector4> IntrinsicOffset = SharedStatic<Vector4>.GetOrCreate<K04>();

        [ConfigVar("essence-telemetry.stat-color", 0.42f, 0.68f, 0.98f, 1f, "Accent for stats.")]
        internal static readonly SharedStatic<Color> StatColor = SharedStatic<Color>.GetOrCreate<K05>();

        [ConfigVar("essence-telemetry.intrinsic-color", 0.74f, 0.56f, 1f, 1f, "Accent for intrinsics.")]
        internal static readonly SharedStatic<Color> IntrinsicColor = SharedStatic<Color>.GetOrCreate<K06>();

        [ConfigVar("essence-telemetry.stat-filter", "", "Filter stats by name prefix.")]
        internal static readonly SharedStatic<FixedString32Bytes> StatFilter =
            SharedStatic<FixedString32Bytes>.GetOrCreate<K07>();

        [ConfigVar("essence-telemetry.intrinsic-filter", "", "Filter intrinsics by name prefix.")]
        internal static readonly SharedStatic<FixedString32Bytes> IntrinsicFilter =
            SharedStatic<FixedString32Bytes>.GetOrCreate<K08>();

        private struct K01 { } private struct K02 { } private struct K03 { } private struct K04 { }
        private struct K05 { } private struct K06 { } private struct K07 { } private struct K08 { }
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

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<DrawSystem.Singleton>()) return;
            ref var drawSystem = ref SystemAPI.GetSingletonRW<DrawSystem.Singleton>().ValueRW;

            Drawer drawer;
            if (!EssenceTelemetryConfig.Enabled.Data)
            {
                drawer = drawSystem.CreateDrawer<EssenceTelemetrySystem>();
                if (!drawer.IsEnabled) return;
            }
            else
            {
                drawer = drawSystem.CreateDrawer();
            }

            state.Dependency = new RenderJob
            {
                Renderer             = drawer,
                Camera               = drawSystem.CameraCulling,
                Scale                = EssenceTelemetryConfig.Scale.Data,
                StatWorldOffset      = ((float4)EssenceTelemetryConfig.StatOffset.Data).xyz,
                IntrinsicWorldOffset = ((float4)EssenceTelemetryConfig.IntrinsicOffset.Data).xyz,
                StatAccent           = EssenceTelemetryConfig.StatColor.Data,
                StatFilter           = EssenceTelemetryConfig.StatFilter.Data,
                IntrinsicAccent      = EssenceTelemetryConfig.IntrinsicColor.Data,
                IntrinsicFilter      = EssenceTelemetryConfig.IntrinsicFilter.Data,
                PanelSpacing         = TelemetryConfig.PanelSpacing.Data,
                UseLogFill           = TelemetryConfig.LogFill.Data,
                TransformHandle      = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                StatHandle           = SystemAPI.GetBufferTypeHandle<Stat>(true),
                IntrinsicHandle      = SystemAPI.GetBufferTypeHandle<Intrinsic>(true),
                TrendHandle          = SystemAPI.GetBufferTypeHandle<StatTrendSample>(true),
                StatDefaultsHandle   = SystemAPI.GetComponentTypeHandle<StatDefaults>(true),
                StatModifiersHandle  = SystemAPI.GetBufferTypeHandle<StatModifiers>(true),
                DebugNames           = SystemAPI.GetSingleton<EssenceDebugNames>(),
                Config               = SystemAPI.GetSingleton<EssenceConfig>(),
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
            public Color IntrinsicAccent;
            public FixedString32Bytes IntrinsicFilter;
            public bool UseLogFill;

            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public BufferTypeHandle<Stat>            StatHandle;
            [ReadOnly] public BufferTypeHandle<Intrinsic>       IntrinsicHandle;
            [ReadOnly] public BufferTypeHandle<StatTrendSample> TrendHandle;
            [ReadOnly] public ComponentTypeHandle<StatDefaults> StatDefaultsHandle;
            [ReadOnly] public BufferTypeHandle<StatModifiers>   StatModifiersHandle;
            [ReadOnly] public EssenceDebugNames                 DebugNames;
            [ReadOnly] public EssenceConfig                     Config;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
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
                    var head = transforms[index].Position;

                    if (hasStats)
                    {
                        var statView = View.WorldFacing(Camera, head, Scale)
                            .NudgeWorld(StatWorldOffset);
                        if (hasBoth) statView = statView.Shift(-PanelSpacing * 0.5f, 0f);

                        EmitStatPanel(statView, index, chunk,
                            statBuffers[index],
                            hasTrends ? trendBuffers[index] : default);
                    }

                    if (hasIntrinsics)
                    {
                        var intrView = View.WorldFacing(Camera, head, Scale)
                            .NudgeWorld(IntrinsicWorldOffset);
                        if (hasBoth) intrView = intrView.Shift(PanelSpacing * 0.5f, 0f);

                        EmitIntrinsicPanel(intrView, index, chunk,
                            intrinsicBuffers[index],
                            hasStats ? statBuffers[index] : default);
                    }
                }
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
                title.Append("STATS");
                Glyph.TitleRow(Renderer, v, y, title, StatAccent);
                y = Glyph.AdvanceLine(y);
                y = Glyph.AdvanceGroup(y);

                foreach (var stat in stats.AsMap())
                {
                    var name = ResolveName(ref names, stat.Key.Value);
                    if (IsFiltered(name, StatFilter)) continue;

                    var fill = ComputeStatFill(stat.Key.Value, stat.Value.Value,
                        defaultsArr, entityIndex, hasDefaults, UseLogFill);

                    var label = new FixedString128Bytes();
                    label.Append(name);
                    label.Append(": ");
                    label.Append(stat.Value.Value);
                    AppendTrendDelta(ref label, stat.Key.Value, trends);

                    Glyph.BarRow(Renderer, v, 0f, y, label, fill, StatAccent, fontSize);
                    y = Glyph.AdvanceLine(y);

                    if (hasDefaults)
                        y = EmitStatDefaults(v, y, stat.Key.Value, defaultsArr[entityIndex], fontSize);

                    if (hasModifiers)
                        y = EmitStatModifiers(v, y, stat.Key.Value, modifiersAcc[entityIndex], fontSize);

                    y = Glyph.AdvanceGroup(y);
                }
            }

            private float EmitStatDefaults(in View v, float y, ushort statKey,
                StatDefaults defaults, float fontSize)
            {
                ref var baseArray = ref defaults.Value.Value.Default;
                for (var i = 0; i < baseArray.Length; i++)
                {
                    if (baseArray[i].Type.Value != statKey) continue;

                    var detail = new FixedString128Bytes();
                    detail.Append("Base: ");
                    FormatModifier(ref detail, baseArray[i]);
                    Glyph.DetailRow(Renderer, v, y, detail, fontSize);
                    y = Glyph.AdvanceLine(y);
                }
                return y;
            }

            private float EmitStatModifiers(in View v, float y, ushort statKey,
                DynamicBuffer<StatModifiers> mods, float fontSize)
            {
                for (var i = 0; i < mods.Length; i++)
                {
                    if (mods[i].Value.Type.Value != statKey) continue;

                    var detail = new FixedString128Bytes();
                    detail.Append("Mod: ");
                    FormatModifier(ref detail, mods[i].Value);
                    detail.Append(" Src:");
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
                title.Append("INTRINSICS");
                Glyph.TitleRow(Renderer, v, y, title, IntrinsicAccent);
                y = Glyph.AdvanceLine(y);
                y = Glyph.AdvanceGroup(y);

                foreach (var intrinsic in intrinsics.AsMap())
                {
                    var name = ResolveName(ref names, intrinsic.Key.Value);
                    if (IsFiltered(name, IntrinsicFilter)) continue;

                    var resolved = ResolveIntrinsicRange(intrinsic.Key, stats, ref configs);
                    var fill = resolved.Range > 0
                        ? (UseLogFill
                            ? Glyph.LogFill(intrinsic.Value, resolved.Min, resolved.Max)
                            : Glyph.LinearFill(intrinsic.Value, resolved.Min, resolved.Max))
                        : 0.5f;

                    var label = new FixedString128Bytes();
                    label.Append(name);
                    label.Append(": ");
                    label.Append(intrinsic.Value);

                    if (resolved.Range > 0)
                    {
                        label.Append(" [");
                        label.Append(resolved.Min);
                        label.Append("..");
                        label.Append(resolved.Max);
                        label.Append("]");
                    }

                    Glyph.BarRow(Renderer, v, 0f, y, label, fill, IntrinsicAccent, fontSize);
                    y = Glyph.AdvanceLine(y);

                    if (resolved.HasStatBounds)
                    {
                        var detail = new FixedString128Bytes();
                        detail.Append("Min: ");
                        detail.Append(resolved.Min);
                        detail.Append(" | Max: ");
                        detail.Append(resolved.Max);
                        detail.Append(" (stat-driven)");
                        Glyph.DetailRow(Renderer, v, y, detail, fontSize);
                        y = Glyph.AdvanceLine(y);
                    }

                    y = Glyph.AdvanceGroup(y);
                }
            }

            private static float ComputeStatFill(ushort key, float value,
                NativeArray<StatDefaults> defaultsArr, int entityIndex, bool hasDefaults, bool useLog)
            {
                var max = 100f;

                if (hasDefaults && defaultsArr.IsCreated)
                {
                    ref var arr = ref defaultsArr[entityIndex].Value.Value.Default;
                    for (var i = 0; i < arr.Length; i++)
                        if (arr[i].Type.Value == key && arr[i].ModifyType == StatModifyType.Added)
                        {
                            max = math.max(1f, (float)arr[i].Value);
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
                    if (data.Ref.MinStatKey.Value != 0 && statMap.TryGetValue(data.Ref.MinStatKey, out var minStat))
                    {
                        min = (int)math.floor(minStat.Value);
                        hasStatBounds = true;
                    }
                    if (data.Ref.MaxStatKey.Value != 0 && statMap.TryGetValue(data.Ref.MaxStatKey, out var maxStat))
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
                    HasStatBounds = hasStatBounds,
                };
            }

            private static void AppendTrendDelta(ref FixedString128Bytes label, ushort key,
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

                label.Append(delta > 0 ? " (+" : " (");
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
                        str.Append("% Add");
                        break;
                    case StatModifyType.Multiplicative:
                        str.Append('x');
                        str.Append(1f + mod.ValueFloat);
                        str.Append(" Mul");
                        break;
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

            private static bool IsFiltered(FixedString32Bytes name, in FixedString32Bytes filter) =>
                !filter.IsEmpty && name.IndexOf(filter) == -1;
        }
    }
}
#endif