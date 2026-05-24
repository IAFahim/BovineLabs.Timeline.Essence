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

        [ConfigVar("essencetelemetry.scale", 0.05f, "Fixed world-space scale multiplier for the cards.")]
        internal static readonly SharedStatic<float> Scale = SharedStatic<float>.GetOrCreate<ScaleType>();

        [ConfigVar("essencetelemetry.stat-offset", 0f, 1.3f, 0f, 0f, "World anchor offset for the stats card.")]
        internal static readonly SharedStatic<Vector4> StatOffset = SharedStatic<Vector4>.GetOrCreate<StatOffsetType>();

        [ConfigVar("essencetelemetry.stat-color", 0.42f, 0.68f, 0.98f, 1f, "Accent for the stats card.")]
        internal static readonly SharedStatic<Color> StatColor = SharedStatic<Color>.GetOrCreate<StatColorType>();

        [ConfigVar("essencetelemetry.stat-filter", "", "Filter stats by name prefix.")]
        internal static readonly SharedStatic<FixedString32Bytes> StatFilter =
            SharedStatic<FixedString32Bytes>.GetOrCreate<StatFilterType>();

        [ConfigVar("essencetelemetry.intrinsic-offset", 0f, 1.3f, 0f, 0f, "World anchor offset for the intrinsics card.")]
        internal static readonly SharedStatic<Vector4> IntrinsicOffset =
            SharedStatic<Vector4>.GetOrCreate<IntrinsicOffsetType>();

        [ConfigVar("essencetelemetry.intrinsic-color", 0.74f, 0.56f, 1f, 1f, "Accent for the intrinsics card.")]
        internal static readonly SharedStatic<Color> IntrinsicColor =
            SharedStatic<Color>.GetOrCreate<IntrinsicColorType>();

        [ConfigVar("essencetelemetry.intrinsic-filter", "", "Filter intrinsics by name prefix.")]
        internal static readonly SharedStatic<FixedString32Bytes> IntrinsicFilter =
            SharedStatic<FixedString32Bytes>.GetOrCreate<IntrinsicFilterType>();

        private struct EnabledType { }
        private struct ScaleType { }
        private struct StatOffsetType { }
        private struct StatColorType { }
        private struct StatFilterType { }
        private struct IntrinsicOffsetType { }
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

            var statOff = (float4)EssenceTelemetryConfig.StatOffset.Data;
            var intrOff = (float4)EssenceTelemetryConfig.IntrinsicOffset.Data;

            state.Dependency = new RenderTelemetryJob
            {
                Renderer         = drawer,
                Camera           = drawSystem.CameraCulling,
                Scale            = EssenceTelemetryConfig.Scale.Data,
                TransformHandle  = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                StatHandle       = SystemAPI.GetBufferTypeHandle<Stat>(true),
                IntrinsicHandle  = SystemAPI.GetBufferTypeHandle<Intrinsic>(true),
                TrendHandle      = SystemAPI.GetBufferTypeHandle<StatTrendSample>(true),
                DebugNames       = SystemAPI.GetSingleton<EssenceDebugNames>(),
                Config           = SystemAPI.GetSingleton<EssenceConfig>(),
                StatWorldLift    = statOff.y,
                StatAccent       = EssenceTelemetryConfig.StatColor.Data,
                StatFilter       = EssenceTelemetryConfig.StatFilter.Data,
                IntrinsicWorldLift = intrOff.y,
                IntrinsicAccent  = EssenceTelemetryConfig.IntrinsicColor.Data,
                IntrinsicFilter  = EssenceTelemetryConfig.IntrinsicFilter.Data,
            }.Schedule(telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderTelemetryJob : IJobChunk
        {
            private const int  SparkCap = 32;
            private const float CardBias = 5.8f;

            public Drawer Renderer;
            public CameraCulling Camera;
            public float Scale;

            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public BufferTypeHandle<Stat>            StatHandle;
            [ReadOnly] public BufferTypeHandle<Intrinsic>       IntrinsicHandle;
            [ReadOnly] public BufferTypeHandle<StatTrendSample> TrendHandle;
            [ReadOnly] public EssenceDebugNames                 DebugNames;
            [ReadOnly] public EssenceConfig                     Config;

            public float StatWorldLift;
            public Color StatAccent;
            public FixedString32Bytes StatFilter;

            public float IntrinsicWorldLift;
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
                    var head     = transforms[index].Position;
                    var baseView = View.WorldFacing(Camera, head, Scale);

                    if (hasStats)
                    {
                        var statView = baseView
                            .NudgeWorld(new float3(0f, StatWorldLift, 0f))
                            .Shift(-CardBias, 0f);
                        RenderStats(statView, stats[index],
                            hasTrends ? trends[index] : default);
                    }

                    if (hasIntrinsics)
                    {
                        var intrView = baseView
                            .NudgeWorld(new float3(0f, IntrinsicWorldLift, 0f))
                            .Shift(+CardBias, 0f);
                        RenderIntrinsics(intrView, intrinsics[index],
                            hasStats ? stats[index] : default);
                    }
                }
            }

            private void RenderStats(
                in View v, DynamicBuffer<Stat> buffer, DynamicBuffer<StatTrendSample> trend)
            {
                ref var names  = ref DebugNames.Value.Value.StatNames;
                var pen        = new Pen(0f, Layout.Header, Layout.Leading);
                var hasTrend   = trend.IsCreated && trend.Length > 0;

                Glyph.Title(Renderer, v, "STATS", Layout.HalfCard, StatAccent);

                foreach (var stat in buffer.AsMap())
                {
                    var name = Resolve(ref names, stat.Key.Value);
                    if (Hidden(name, StatFilter)) continue;

                    var y = pen.Take();

                    Glyph.Label(Renderer, v, Layout.LabelX, y, name, Ink.Label);

                    var compact = new FixedString128Bytes();
                    Format.Compact(ref compact, stat.Value.Value);
                    Glyph.Text(Renderer, v, Layout.ValueX, y, compact, Ink.Value, Layout.Body);

                    if (hasTrend) RenderSpark(v, y, stat.Key.Value, trend);
                }

                var frameBot = pen.Y - Layout.Pad;
                Glyph.Frame(Renderer, v,
                    -Layout.HalfCard, Layout.HalfCard,
                    Layout.Pad, frameBot,
                    Ink.Frame, StatAccent);
            }

            private unsafe void RenderSpark(
                in View v, float rowY, ushort key, DynamicBuffer<StatTrendSample> trend)
            {
                var samples = stackalloc float[SparkCap];
                var count   = 0;
                for (var i = 0; i < trend.Length && count < SparkCap; i++)
                    if (trend[i].Key == key) samples[count++] = trend[i].Value;

                if (count < 2) return;

                var dir = samples[count - 1] - samples[count - 2];
                Glyph.Delta(Renderer, v, Layout.GaugeX0 - 0.5f, rowY + 0.2f, dir, dir >= 0f ? Ink.RampCalm : Ink.RampWarm);
                Glyph.Spark(Renderer, v,
                    Layout.GaugeX0, Layout.GaugeX1,
                    rowY - 0.35f, 0.65f,
                    samples, count, StatAccent);
            }

            private void RenderIntrinsics(
                in View v, DynamicBuffer<Intrinsic> buffer, DynamicBuffer<Stat> stats)
            {
                ref var names   = ref DebugNames.Value.Value.IntrinsicNames;
                ref var configs = ref Config.Value.Value.IntrinsicDatas;
                var pen         = new Pen(0f, Layout.Header, Layout.Leading);

                Glyph.Title(Renderer, v, "INTRINSICS", Layout.HalfCard, IntrinsicAccent);

                foreach (var intrinsic in buffer.AsMap())
                {
                    var name = Resolve(ref names, intrinsic.Key.Value);
                    if (Hidden(name, IntrinsicFilter)) continue;

                    var y     = pen.Take();
                    var value = (float)intrinsic.Value;

                    Glyph.Label(Renderer, v, Layout.LabelX, y, name, Ink.Label);

                    if (configs.TryGetValue(intrinsic.Key, out var data))
                    {
                        var min = data.Ref.Min;
                        var max = data.Ref.Max;

                        if (stats.IsCreated)
                        {
                            var statMap = stats.AsMap();
                            if (data.Ref.MinStatKey != 0 &&
                                statMap.TryGetValue(data.Ref.MinStatKey, out var minStat))
                            {
                                min = (int)math.floor(minStat.Value);
                            }

                            if (data.Ref.MaxStatKey != 0 &&
                                statMap.TryGetValue(data.Ref.MaxStatKey, out var maxStat))
                            {
                                ;
                                max = (int)math.floor(maxStat.Value);
                            }
                        }

                        if (max > min)
                            Glyph.Gauge(Renderer, v,
                                Layout.GaugeX0, Layout.GaugeX1,
                                y - 0.25f,
                                (value - min) / (max - min));
                    }

                    var compact = new FixedString128Bytes();
                    Format.Compact(ref compact, value);
                    Glyph.Text(Renderer, v, Layout.ValueX, y, compact, Ink.Value, Layout.Body);
                }

                var frameBot = pen.Y - Layout.Pad;
                Glyph.Frame(Renderer, v,
                    -Layout.HalfCard, Layout.HalfCard,
                    Layout.Pad, frameBot,
                    Ink.Frame, IntrinsicAccent);
            }

            private static FixedString32Bytes Resolve(
                ref BlobHashMap<ushort, FixedString32Bytes> names, ushort key)
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