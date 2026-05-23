// BovineLabs.Essence.Debug/EssenceTelemetrySystem.cs
#if UNITY_EDITOR || BL_DEBUG
using System.Diagnostics.CodeAnalysis;
using BovineLabs.Core;
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
    public static class ReactionTelemetrySystem
    {
        [ConfigVar("essencetelemetry.force-draw", false, "Enable the Essence telemetry drawer.")]
        internal static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<EnabledType>();
        
        // --- Stat Config ---
        [ConfigVar("essencetelemetry.stat-offset", -0.8f, 2.0f, 0f, 0f, "Offset for stats (Left).")]
        internal static readonly SharedStatic<Vector4> StatOffset = SharedStatic<Vector4>.GetOrCreate<StatOffsetType>();

        [ConfigVar("essencetelemetry.stat-color", 0.35f, 0.65f, 0.96f, 1f, "Color for stats.")]
        internal static readonly SharedStatic<Color> StatColor = SharedStatic<Color>.GetOrCreate<StatColorType>();

        [ConfigVar("essencetelemetry.stat-filter", "", "Filter stats by name.")]
        internal static readonly SharedStatic<FixedString32Bytes> StatFilter = SharedStatic<FixedString32Bytes>.GetOrCreate<StatFilterType>();

        // --- Intrinsic Config ---
        [ConfigVar("essencetelemetry.intrinsic-offset", 0.8f, 2.0f, 0f, 0f, "Offset for intrinsics (Right).")]
        internal static readonly SharedStatic<Vector4> IntrinsicOffset = SharedStatic<Vector4>.GetOrCreate<IntrinsicOffsetType>();

        [ConfigVar("essencetelemetry.intrinsic-color", 0.7f, 0.53f, 1f, 1f, "Color for intrinsics.")]
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
            if (!ReactionTelemetrySystem.Enabled.Data)
            {
                drawer = drawSystem.CreateDrawer<EssenceTelemetrySystem>();
                if (!drawer.IsEnabled) return;
            }
            else drawer = drawSystem.CreateDrawer();

            state.Dependency = new RenderTelemetryJob
            {
                Renderer = drawer,
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                StatHandle = SystemAPI.GetBufferTypeHandle<Stat>(true),
                IntrinsicHandle = SystemAPI.GetBufferTypeHandle<Intrinsic>(true),
                DebugNames = SystemAPI.GetSingleton<EssenceDebugNames>(),
                Config = SystemAPI.GetSingleton<EssenceConfig>(),
                
                // ConfigVars
                StatOffset = ((float4)ReactionTelemetrySystem.StatOffset.Data).xyz,
                StatColor = ReactionTelemetrySystem.StatColor.Data,
                StatFilter = ReactionTelemetrySystem.StatFilter.Data,
                
                IntrinsicOffset = ((float4)ReactionTelemetrySystem.IntrinsicOffset.Data).xyz,
                IntrinsicColor = ReactionTelemetrySystem.IntrinsicColor.Data,
                IntrinsicFilter = ReactionTelemetrySystem.IntrinsicFilter.Data
            }.Schedule(this.telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderTelemetryJob : IJobChunk
        {
            public Drawer Renderer;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public BufferTypeHandle<Stat> StatHandle;
            [ReadOnly] public BufferTypeHandle<Intrinsic> IntrinsicHandle;
            [ReadOnly] public EssenceDebugNames DebugNames;
            [ReadOnly] public EssenceConfig Config;

            public float3 StatOffset;
            public Color StatColor;
            public FixedString32Bytes StatFilter;

            public float3 IntrinsicOffset;
            public Color IntrinsicColor;
            public FixedString32Bytes IntrinsicFilter;

            private static readonly Color HeaderTint = new(1f, 1f, 1f, 0.3f);
            private const int TargetNameLength = 18; 

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref this.TransformHandle);
                var statsAccessor = chunk.GetBufferAccessor(ref this.StatHandle);
                var intrinsicsAccessor = chunk.GetBufferAccessor(ref this.IntrinsicHandle);

                var hasStats = statsAccessor.Length > 0;
                var hasIntrinsics = intrinsicsAccessor.Length > 0;

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var origin = transforms[index].Position;

                    if (hasStats)
                    {
                        var statCursor = origin + this.StatOffset;
                        this.Renderer.Line(origin, statCursor, HeaderTint);
                        this.Renderer.Text32(statCursor, "[ STATS ]", HeaderTint, 12f);
                        statCursor.y -= 0.2f;
                        this.RenderStats(ref statCursor, statsAccessor[index]);
                    }
                    
                    if (hasIntrinsics)
                    {
                        var intCursor = origin + this.IntrinsicOffset;
                        this.Renderer.Line(origin, intCursor, HeaderTint);
                        this.Renderer.Text32(intCursor, "[ INTRINSICS ]", HeaderTint, 12f);
                        intCursor.y -= 0.2f;
                        this.RenderIntrinsics(ref intCursor, intrinsicsAccessor[index], hasStats ? statsAccessor[index] : default);
                    }
                }
            }

            private void RenderStats(ref float3 cursor, DynamicBuffer<Stat> buffer)
            {
                ref var names = ref this.DebugNames.Value.Value.StatNames;
                bool hasFilter = !this.StatFilter.IsEmpty;

                foreach (var kvp in buffer.AsMap())
                {
                    var name = new FixedString32Bytes();
                    if (names.TryGetValue(kvp.Key.Value, out var namePtr)) name = namePtr.Ref;
                    else name.Append(kvp.Key.Value);

                    if (hasFilter && name.IndexOf(this.StatFilter) == -1) continue;

                    var format = new FixedString128Bytes();
                    format.Append(name);
                    this.PadRight(ref format);
                    
                    format.Append(kvp.Value.Value);
                    format.Append(" (A:");
                    format.Append(kvp.Value.Added);
                    format.Append(" M:");
                    format.Append(kvp.Value.Multi);
                    format.Append(")");

                    this.Renderer.Text128(cursor, format, this.StatColor, 11f);
                    cursor.y -= 0.15f;
                }
            }

            private void RenderIntrinsics(ref float3 cursor, DynamicBuffer<Intrinsic> buffer, DynamicBuffer<Stat> stats)
            {
                ref var names = ref this.DebugNames.Value.Value.IntrinsicNames;
                ref var configDatas = ref this.Config.Value.Value.IntrinsicDatas;
                bool hasFilter = !this.IntrinsicFilter.IsEmpty;

                foreach (var kvp in buffer.AsMap())
                {
                    var name = new FixedString32Bytes();
                    if (names.TryGetValue(kvp.Key.Value, out var namePtr)) name = namePtr.Ref;
                    else name.Append(kvp.Key.Value);

                    if (hasFilter && name.IndexOf(this.IntrinsicFilter) == -1) continue;

                    var format = new FixedString128Bytes();
                    format.Append(name);
                    this.PadRight(ref format);
                    format.Append(kvp.Value);

                    if (configDatas.TryGetValue(kvp.Key, out var cData))
                    {
                        int min = cData.Ref.Min;
                        int max = cData.Ref.Max;

                        if (stats.IsCreated)
                        {
                            var statMap = stats.AsMap();
                            if (cData.Ref.MinStatKey != 0 && statMap.TryGetValue(cData.Ref.MinStatKey, out var minStat))
                                min = (int)math.floor(minStat.Value);
                            if (cData.Ref.MaxStatKey != 0 && statMap.TryGetValue(cData.Ref.MaxStatKey, out var maxStat))
                                max = (int)math.floor(maxStat.Value);
                        }

                        format.Append(" [");
                        format.Append(min);
                        format.Append(" -> ");
                        format.Append(max);
                        format.Append("]");
                    }

                    this.Renderer.Text128(cursor, format, this.IntrinsicColor, 11f);
                    cursor.y -= 0.15f;
                }
            }

            private void PadRight(ref FixedString128Bytes str)
            {
                int spacesNeeded = math.max(1, TargetNameLength - str.Length);
                for (int i = 0; i < spacesNeeded; i++) str.Append(' ');
            }
        }
    }
}
#endif