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
        [ConfigVar("essencetelemetry.force-draw", false, "Enable the Essence telemetry drawer.")]
        internal static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<EnabledType>();

        [ConfigVar("essencetelemetry.full-details", true, "Show full details including modifiers.")]
        internal static readonly SharedStatic<bool> FullDetails = SharedStatic<bool>.GetOrCreate<FullDetailsType>();

        [ConfigVar("essencetelemetry.scale", 0.04f, "Fixed world-space scale for the UI.")]
        internal static readonly SharedStatic<float> Scale = SharedStatic<float>.GetOrCreate<ScaleType>();

        [ConfigVar("essencetelemetry.stat-offset", 0f, 1.3f, 0f, 0f, "World anchor offset for the stats panel.")]
        internal static readonly SharedStatic<Vector4> StatOffset = SharedStatic<Vector4>.GetOrCreate<StatOffsetType>();

        [ConfigVar("essencetelemetry.intrinsic-offset", 0f, 1.3f, 0f, 0f, "World anchor offset for the intrinsics panel.")]
        internal static readonly SharedStatic<Vector4> IntrinsicOffset = SharedStatic<Vector4>.GetOrCreate<IntrinsicOffsetType>();

        [ConfigVar("essencetelemetry.stat-color", 0.42f, 0.68f, 0.98f, 1f, "Accent for stats.")]
        internal static readonly SharedStatic<Color> StatColor = SharedStatic<Color>.GetOrCreate<StatColorType>();

        [ConfigVar("essencetelemetry.stat-filter", "", "Filter stats by name prefix.")]
        internal static readonly SharedStatic<FixedString32Bytes> StatFilter =
            SharedStatic<FixedString32Bytes>.GetOrCreate<StatFilterType>();

        [ConfigVar("essencetelemetry.intrinsic-color", 0.74f, 0.56f, 1f, 1f, "Accent for intrinsics.")]
        internal static readonly SharedStatic<Color> IntrinsicColor =
            SharedStatic<Color>.GetOrCreate<IntrinsicColorType>();

        [ConfigVar("essencetelemetry.intrinsic-filter", "", "Filter intrinsics by name prefix.")]
        internal static readonly SharedStatic<FixedString32Bytes> IntrinsicFilter =
            SharedStatic<FixedString32Bytes>.GetOrCreate<IntrinsicFilterType>();

        private struct EnabledType { }
        private struct FullDetailsType { }
        private struct ScaleType { }
        private struct StatOffsetType { }
        private struct IntrinsicOffsetType { }
        private struct StatColorType { }
        private struct StatFilterType { }
        private struct IntrinsicColorType { }
        private struct IntrinsicFilterType { }
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

            state.Dependency = new RenderTelemetryJob
            {
                Renderer            = drawer,
                Camera              = drawSystem.CameraCulling,
                Scale               = EssenceTelemetryConfig.Scale.Data,
                TransformHandle     = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                StatHandle          = SystemAPI.GetBufferTypeHandle<Stat>(true),
                IntrinsicHandle     = SystemAPI.GetBufferTypeHandle<Intrinsic>(true),
                TrendHandle         = SystemAPI.GetBufferTypeHandle<StatTrendSample>(true),
                StatDefaultsHandle  = SystemAPI.GetComponentTypeHandle<StatDefaults>(true),
                StatModifiersHandle = SystemAPI.GetBufferTypeHandle<StatModifiers>(true),
                DebugNames          = SystemAPI.GetSingleton<EssenceDebugNames>(),
                Config              = SystemAPI.GetSingleton<EssenceConfig>(),
                FullDetails         = EssenceTelemetryConfig.FullDetails.Data,
                LodDistance         = TelemetryLayoutConfig.LodDistance.Data,
                StatWorldOffset     = ((float4)EssenceTelemetryConfig.StatOffset.Data).xyz,
                IntrinsicWorldOffset = ((float4)EssenceTelemetryConfig.IntrinsicOffset.Data).xyz,
                StatAccent          = EssenceTelemetryConfig.StatColor.Data,
                StatFilter          = EssenceTelemetryConfig.StatFilter.Data,
                IntrinsicAccent     = EssenceTelemetryConfig.IntrinsicColor.Data,
                IntrinsicFilter     = EssenceTelemetryConfig.IntrinsicFilter.Data,
            }.Schedule(telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderTelemetryJob : IJobChunk
        {
            public Drawer Renderer;
            public CameraCulling Camera;
            public float Scale;

            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public BufferTypeHandle<Stat>            StatHandle;
            [ReadOnly] public BufferTypeHandle<Intrinsic>       IntrinsicHandle;
            [ReadOnly] public BufferTypeHandle<StatTrendSample> TrendHandle;
            [ReadOnly] public ComponentTypeHandle<StatDefaults> StatDefaultsHandle;
            [ReadOnly] public BufferTypeHandle<StatModifiers>   StatModifiersHandle;

            [ReadOnly] public EssenceDebugNames                 DebugNames;
            [ReadOnly] public EssenceConfig                     Config;

            public bool FullDetails;
            public float LodDistance;
            public float3 StatWorldOffset;
            public float3 IntrinsicWorldOffset;
            public Color StatAccent;
            public FixedString32Bytes StatFilter;
            public Color IntrinsicAccent;
            public FixedString32Bytes IntrinsicFilter;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms  = chunk.GetNativeArray(ref TransformHandle);
                var stats       = chunk.GetBufferAccessor(ref StatHandle);
                var intrinsics  = chunk.GetBufferAccessor(ref IntrinsicHandle);
                var trends      = chunk.GetBufferAccessor(ref TrendHandle);

                var hasStats      = stats.Length > 0;
                var hasIntrinsics = intrinsics.Length > 0;
                var hasTrends     = trends.Length > 0;

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var head = transforms[index].Position;
                    var baseView = View.WorldFacing(Camera, head, Scale).NudgeWorld(StatWorldOffset);
                    var panelSpacing = TelemetryLayoutConfig.PanelSpacing.Data;
                    
                    bool showDetails = FullDetails && baseView.Distance < LodDistance;

                    if (hasStats)
                    {
                        var statView = hasIntrinsics ? baseView.Shift(-panelSpacing * 0.5f, 0f) : baseView;
                        RenderStats(statView, index, chunk, stats[index], hasTrends ? trends[index] : default, showDetails);
                    }

                    if (hasIntrinsics)
                    {
                        var intrView = View.WorldFacing(Camera, head, Scale).NudgeWorld(IntrinsicWorldOffset);
                        intrView = hasStats ? intrView.Shift(panelSpacing * 0.5f, 0f) : intrView;
                        RenderIntrinsics(intrView, intrinsics[index], hasStats ? stats[index] : default, showDetails);
                    }
                }
            }

            private void RenderStats(in View v, int entityIndex, in ArchetypeChunk chunk,
                DynamicBuffer<Stat> buffer, DynamicBuffer<StatTrendSample> trend, bool showDetails)
            {
                var y = 0f;
                var titleSize = TelemetryLayoutConfig.TitleSize.Data;
                var fontSize = TelemetryLayoutConfig.FontSize.Data;
                var lineHeight = TelemetryLayoutConfig.LineHeight.Data;
                var groupSpacing = TelemetryLayoutConfig.GroupSpacing.Data;
                var indent = TelemetryLayoutConfig.Indent.Data;

                Glyph.Text(Renderer, v, 0f, y, "[ STATS ]", StatAccent, titleSize);
                y -= lineHeight * (1f + groupSpacing);

                ref var names = ref DebugNames.Value.Value.StatNames;
                var hasDefaults = chunk.Has(ref StatDefaultsHandle);
                var hasModifiers = chunk.Has(ref StatModifiersHandle);

                var defaultsAcc = hasDefaults ? chunk.GetNativeArray(ref StatDefaultsHandle) : default;
                var modifiersAcc = hasModifiers ? chunk.GetBufferAccessor(ref StatModifiersHandle) : default;

                foreach (var stat in buffer.AsMap())
                {
                    var name = Resolve(ref names, stat.Key.Value);
                    if (Hidden(name, StatFilter)) continue;

                    var text = new FixedString128Bytes();
                    text.Append("[");
                    text.Append(stat.Value.Value);

                    var delta = GetTrendDelta(stat.Key.Value, trend);
                    if (math.abs(delta) > 0.001f)
                    {
                        text.Append(delta > 0 ? " (+" : " (");
                        text.Append(delta);
                        text.Append(")");
                    }
                    text.Append("] ");
                    text.Append(name);

                    Glyph.Text(Renderer, v, 0f, y, text, Ink.Value, fontSize);
                    y -= lineHeight;

                    if (showDetails)
                    {
                        if (hasDefaults)
                        {
                            var defs = defaultsAcc[entityIndex];
                            ref var baseArray = ref defs.Value.Value.Default;
                            for (int i = 0; i < baseArray.Length; i++)
                            {
                                if (baseArray[i].Type.Value == stat.Key.Value)
                                {
                                    var detail = new FixedString128Bytes();
                                    detail.Append("-> Base: ");
                                    FormatModifier(ref detail, baseArray[i]);
                                    Glyph.Text(Renderer, v, indent, y, detail, Ink.Muted, fontSize);
                                    y -= lineHeight;
                                }
                            }
                        }

                        if (hasModifiers)
                        {
                            var mods = modifiersAcc[entityIndex];
                            for (int i = 0; i < mods.Length; i++)
                            {
                                if (mods[i].Value.Type.Value == stat.Key.Value)
                                {
                                    var detail = new FixedString128Bytes();
                                    detail.Append("-> Mod: ");
                                    FormatModifier(ref detail, mods[i].Value);
                                    detail.Append(" (Src: ");
                                    detail.Append(mods[i].SourceEntity.Index);
                                    detail.Append(")");
                                    Glyph.Text(Renderer, v, indent, y, detail, Ink.Muted, fontSize);
                                    y -= lineHeight;
                                }
                            }
                        }
                    }

                    y -= lineHeight * groupSpacing;
                }
            }

            private void FormatModifier(ref FixedString128Bytes str, StatModifier mod)
            {
                switch (mod.ModifyType)
                {
                    case StatModifyType.Added:
                        str.Append(mod.Value >= 0 ? "+" : "");
                        str.Append(mod.Value);
                        break;
                    case StatModifyType.Additive:
                        str.Append(mod.ValueFloat >= 0 ? "+" : "");
                        str.Append(mod.ValueFloat * 100f);
                        str.Append("% (Add)");
                        break;
                    case StatModifyType.Multiplicative:
                        str.Append("x");
                        str.Append(1f + mod.ValueFloat);
                        str.Append(" (Mul)");
                        break;
                }
            }

            private float GetTrendDelta(ushort key, DynamicBuffer<StatTrendSample> trend)
            {
                if (!trend.IsCreated || trend.Length < 2) return 0f;
                float last = 0f; float prev = 0f;
                int count = 0;
                for (int i = trend.Length - 1; i >= 0; i--)
                {
                    if (trend[i].Key == key)
                    {
                        if (count == 0) last = trend[i].Value;
                        else if (count == 1) { prev = trend[i].Value; break; }
                        count++;
                    }
                }
                return count >= 2 ? (last - prev) : 0f;
            }

            private void RenderIntrinsics(in View v, DynamicBuffer<Intrinsic> buffer, DynamicBuffer<Stat> stats, bool showDetails)
            {
                var y = 0f;
                var titleSize = TelemetryLayoutConfig.TitleSize.Data;
                var fontSize = TelemetryLayoutConfig.FontSize.Data;
                var lineHeight = TelemetryLayoutConfig.LineHeight.Data;
                var groupSpacing = TelemetryLayoutConfig.GroupSpacing.Data;
                var indent = TelemetryLayoutConfig.Indent.Data;

                Glyph.Text(Renderer, v, 0f, y, "[ INTRINSICS ]", IntrinsicAccent, titleSize);
                y -= lineHeight * (1f + groupSpacing);

                ref var names   = ref DebugNames.Value.Value.IntrinsicNames;
                ref var configs = ref Config.Value.Value.IntrinsicDatas;

                foreach (var intrinsic in buffer.AsMap())
                {
                    var name = Resolve(ref names, intrinsic.Key.Value);
                    if (Hidden(name, IntrinsicFilter)) continue;

                    var text = new FixedString128Bytes();

                    int min = 0;
                    int max = 0;

                    if (configs.TryGetValue(intrinsic.Key, out var data))
                    {
                        min = data.Ref.Min;
                        max = data.Ref.Max;

                        if (stats.IsCreated)
                        {
                            var statMap = stats.AsMap();
                            if (data.Ref.MinStatKey.Value != 0 && statMap.TryGetValue(data.Ref.MinStatKey, out var minStat))
                                min = (int)math.floor(minStat.Value);
                            if (data.Ref.MaxStatKey.Value != 0 && statMap.TryGetValue(data.Ref.MaxStatKey, out var maxStat))
                                max = (int)math.floor(maxStat.Value);
                        }

                        if (max > min)
                        {
                            float pct = math.saturate((float)(intrinsic.Value - min) / (max - min));
                            Glyph.AppendProgressBar(ref text, pct);
                        }
                    }

                    text.Append(name);
                    text.Append(": ");
                    text.Append(intrinsic.Value);

                    Glyph.Text(Renderer, v, 0f, y, text, IntrinsicAccent, fontSize);
                    y -= lineHeight;

                    if (showDetails && (max > min || min != 0))
                    {
                        var detail = new FixedString128Bytes();
                        detail.Append("-> Min: ");
                        detail.Append(min);
                        detail.Append(" | Max: ");
                        detail.Append(max);

                        Glyph.Text(Renderer, v, indent, y, detail, Ink.Muted, fontSize);
                        y -= lineHeight;
                    }

                    y -= lineHeight * groupSpacing;
                }
            }

            private static FixedString32Bytes Resolve(ref BlobHashMap<ushort, FixedString32Bytes> names, ushort key)
            {
                if (names.TryGetValue(key, out var named)) return named.Ref;
                var fallback = new FixedString32Bytes();
                fallback.Append(key);
                return fallback;
            }

            private static bool Hidden(FixedString32Bytes name, in FixedString32Bytes filter) =>
                !filter.IsEmpty && name.IndexOf(filter) == -1;
        }
    }
}
#endif