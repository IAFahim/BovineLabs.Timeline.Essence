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
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1611:Element parameters should be documented", Justification = "Using see cref")]
    public static class EssenceTelemetryConfig
    {
        [ConfigVar("essencetelemetry.force-draw", false, "Enable the Essence telemetry drawer.")]
        internal static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<EnabledType>();

        [ConfigVar("essencetelemetry.stat-offset", -0.95f, 2.0f, 0f, 0f, "Offset for stats (Left).")]
        internal static readonly SharedStatic<Vector4> StatOffset = SharedStatic<Vector4>.GetOrCreate<StatOffsetType>();

        [ConfigVar("essencetelemetry.stat-color", 0.42f, 0.68f, 0.98f, 1f, "Accent for the stats card.")]
        internal static readonly SharedStatic<Color> StatColor = SharedStatic<Color>.GetOrCreate<StatColorType>();

        [ConfigVar("essencetelemetry.stat-filter", "", "Filter stats by name.")]
        internal static readonly SharedStatic<FixedString32Bytes> StatFilter = SharedStatic<FixedString32Bytes>.GetOrCreate<StatFilterType>();

        [ConfigVar("essencetelemetry.intrinsic-offset", 0.95f, 2.0f, 0f, 0f, "Offset for intrinsics (Right).")]
        internal static readonly SharedStatic<Vector4> IntrinsicOffset = SharedStatic<Vector4>.GetOrCreate<IntrinsicOffsetType>();

        [ConfigVar("essencetelemetry.intrinsic-color", 0.74f, 0.56f, 1f, 1f, "Accent for the intrinsics card.")]
        internal static readonly SharedStatic<Color> IntrinsicColor = SharedStatic<Color>.GetOrCreate<IntrinsicColorType>();

        [ConfigVar("essencetelemetry.intrinsic-filter", "", "Filter intrinsics by name.")]
        internal static readonly SharedStatic<FixedString32Bytes> IntrinsicFilter = SharedStatic<FixedString32Bytes>.GetOrCreate<IntrinsicFilterType>();

        private struct EnabledType { }
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
            this.telemetryQuery = SystemAPI.QueryBuilder()
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
                Renderer = drawer,
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                StatHandle = SystemAPI.GetBufferTypeHandle<Stat>(true),
                IntrinsicHandle = SystemAPI.GetBufferTypeHandle<Intrinsic>(true),
                TrendHandle = SystemAPI.GetBufferTypeHandle<StatTrendSample>(true),
                DebugNames = SystemAPI.GetSingleton<EssenceDebugNames>(),
                Config = SystemAPI.GetSingleton<EssenceConfig>(),

                StatOffset = ((float4)EssenceTelemetryConfig.StatOffset.Data).xyz,
                StatAccent = EssenceTelemetryConfig.StatColor.Data,
                StatFilter = EssenceTelemetryConfig.StatFilter.Data,

                IntrinsicOffset = ((float4)EssenceTelemetryConfig.IntrinsicOffset.Data).xyz,
                IntrinsicAccent = EssenceTelemetryConfig.IntrinsicColor.Data,
                IntrinsicFilter = EssenceTelemetryConfig.IntrinsicFilter.Data,
            }.Schedule(this.telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderTelemetryJob : IJobChunk
        {
            private const int SparkCapacity = 32;

            public Drawer Renderer;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public BufferTypeHandle<Stat> StatHandle;
            [ReadOnly] public BufferTypeHandle<Intrinsic> IntrinsicHandle;
            [ReadOnly] public BufferTypeHandle<StatTrendSample> TrendHandle;
            [ReadOnly] public EssenceDebugNames DebugNames;
            [ReadOnly] public EssenceConfig Config;

            public float3 StatOffset;
            public Color StatAccent;
            public FixedString32Bytes StatFilter;

            public float3 IntrinsicOffset;
            public Color IntrinsicAccent;
            public FixedString32Bytes IntrinsicFilter;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref this.TransformHandle);
                var stats = chunk.GetBufferAccessor(ref this.StatHandle);
                var intrinsics = chunk.GetBufferAccessor(ref this.IntrinsicHandle);
                var trends = chunk.GetBufferAccessor(ref this.TrendHandle);

                var hasStats = stats.Length > 0;
                var hasIntrinsics = intrinsics.Length > 0;
                var hasTrends = trends.Length > 0;

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var origin = transforms[index].Position;

                    if (hasStats)
                    {
                        this.RenderStats(origin + this.StatOffset, stats[index], hasTrends ? trends[index] : default);
                    }

                    if (hasIntrinsics)
                    {
                        this.RenderIntrinsics(origin + this.IntrinsicOffset, intrinsics[index], hasStats ? stats[index] : default);
                    }
                }
            }

            private void RenderStats(float3 top, DynamicBuffer<Stat> buffer, DynamicBuffer<StatTrendSample> trend)
            {
                ref var names = ref this.DebugNames.Value.Value.StatNames;
                var pen = new Pen(top, Layout.Header, Layout.Row);
                var hasTrend = trend.IsCreated && trend.Length > 0;

                Glyph.Title(this.Renderer, top, "STATS", this.StatAccent);

                foreach (var stat in buffer.AsMap())
                {
                    var name = Resolve(ref names, stat.Key.Value);
                    if (Hidden(name, this.StatFilter)) continue;

                    var row = pen.Take();
                    Glyph.Label(this.Renderer, row, name, Ink.Label);

                    var value = new FixedString128Bytes();
                    Format.Compact(ref value, stat.Value.Value);
                    Glyph.Readout(this.Renderer, new float3(row.x + Layout.ValueColumn, row.y, row.z), value, Ink.Value, Layout.Body);

                    if (hasTrend)
                    {
                        this.RenderSpark(row, stat.Key.Value, trend);
                    }
                }

                Glyph.Frame(this.Renderer, top, Layout.CardWidth, top.y - pen.Cursor.y + Layout.Pad, Ink.Frame, this.StatAccent);
            }

            private unsafe void RenderSpark(float3 row, ushort key, DynamicBuffer<StatTrendSample> trend)
            {
                var samples = stackalloc float[SparkCapacity];
                var count = 0;
                for (var i = 0; i < trend.Length && count < SparkCapacity; i++)
                {
                    if (trend[i].Key == key) samples[count++] = trend[i].Value;
                }

                if (count < 2) return;

                var anchor = new float3(row.x + Layout.GaugeColumn, row.y, row.z);
                Glyph.Spark(this.Renderer, anchor, Layout.SparkWidth, Layout.SparkHeight, samples, count, this.StatAccent);

                var direction = samples[count - 1] - samples[count - 2];
                var trendColor = direction >= 0f ? Ink.RampCalm : Ink.RampWarm;
                Glyph.Delta(this.Renderer, new float3(anchor.x + Layout.SparkWidth + 0.04f, row.y + 0.02f, row.z), direction, trendColor);
            }

            private void RenderIntrinsics(float3 top, DynamicBuffer<Intrinsic> buffer, DynamicBuffer<Stat> stats)
            {
                ref var names = ref this.DebugNames.Value.Value.IntrinsicNames;
                ref var configs = ref this.Config.Value.Value.IntrinsicDatas;
                var pen = new Pen(top, Layout.Header, Layout.Row);

                Glyph.Title(this.Renderer, top, "INTRINSICS", this.IntrinsicAccent);

                foreach (var intrinsic in buffer.AsMap())
                {
                    var name = Resolve(ref names, intrinsic.Key.Value);
                    if (Hidden(name, this.IntrinsicFilter)) continue;

                    var row = pen.Take();
                    Glyph.Label(this.Renderer, row, name, Ink.Label);

                    var value = (float)intrinsic.Value;
                    var readout = new FixedString128Bytes();
                    Format.Compact(ref readout, value);

                    if (configs.TryGetValue(intrinsic.Key, out var data))
                    {
                        float min = data.Ref.Min;
                        float max = data.Ref.Max;

                        if (stats.IsCreated)
                        {
                            var statMap = stats.AsMap();
                            if (data.Ref.MinStatKey != 0 && statMap.TryGetValue(data.Ref.MinStatKey, out var minStat))
                            {
                                min = math.floor(minStat.Value);
                            }

                            if (data.Ref.MaxStatKey != 0 && statMap.TryGetValue(data.Ref.MaxStatKey, out var maxStat))
                            {
                                max = math.floor(maxStat.Value);
                            }
                        }

                        if (max > min)
                        {
                            var fill = (value - min) / (max - min);
                            Glyph.Gauge(this.Renderer, new float3(row.x + Layout.GaugeColumn, row.y + 0.03f, row.z), Layout.BarWidth, fill);
                        }
                    }

                    Glyph.Readout(this.Renderer, new float3(row.x + Layout.ValueColumn, row.y, row.z), readout, Ink.Value, Layout.Body);
                }

                Glyph.Frame(this.Renderer, top, Layout.CardWidth, top.y - pen.Cursor.y + Layout.Pad, Ink.Frame, this.IntrinsicAccent);
            }

            private static FixedString32Bytes Resolve(ref BlobHashMap<ushort, FixedString32Bytes> names, ushort key)
            {
                if (names.TryGetValue(key, out var named)) return named.Ref;
                var fallback = new FixedString32Bytes();
                fallback.Append(key);
                return fallback;
            }

            private static bool Hidden(FixedString32Bytes name, in FixedString32Bytes filter)
            {
                return !filter.IsEmpty && name.IndexOf(filter) == -1;
            }
        }
    }
}
#endif