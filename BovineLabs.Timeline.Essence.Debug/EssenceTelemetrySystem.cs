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
    public static class EssenceTelemetrySystemConfig
    {
        private const string DrawForced = "essencetelemetrysystem.force-draw";
        [ConfigVar(DrawForced, false, "Enable the Essence telemetry drawer in the editor.")]
        internal static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<EssenceTelemetrySystemForced>();
        private struct EssenceTelemetrySystemForced { }
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
            if (!EssenceTelemetrySystemConfig.Enabled.Data)
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
                Config = SystemAPI.GetSingleton<EssenceConfig>()
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

            private static readonly Color StatTint = new(0.35f, 0.65f, 0.96f, 1f);
            private static readonly Color IntrinsicTint = new(0.7f, 0.53f, 1f, 1f);
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
                    // Offset slightly to the right so it doesn't overlap with Reaction
                    var cursor = origin + new float3(0.6f, 2.0f, 0f);

                    this.Renderer.Line(origin, cursor, HeaderTint);
                    this.Renderer.Text32(cursor, "[ ESSENCE ]", HeaderTint, 12f);
                    cursor.y -= 0.2f;

                    if (hasStats) this.RenderStats(ref cursor, statsAccessor[index]);
                    if (hasIntrinsics) this.RenderIntrinsics(ref cursor, intrinsicsAccessor[index], hasStats ? statsAccessor[index] : default);
                }
            }

            private void RenderStats(ref float3 cursor, DynamicBuffer<Stat> buffer)
            {
                ref var names = ref this.DebugNames.Value.Value.StatNames;
                foreach (var kvp in buffer.AsMap())
                {
                    var format = new FixedString128Bytes();
                    if (names.TryGetValue(kvp.Key.Value, out var namePtr)) format.Append(namePtr.Ref);
                    else format.Append(kvp.Key.Value);
                        
                    this.PadRight(ref format);
                    
                    format.Append(kvp.Value.Value);
                    format.Append(" (A:");
                    format.Append(kvp.Value.Added);
                    format.Append(" M:");
                    format.Append(kvp.Value.Multi);
                    format.Append(")");

                    this.Renderer.Text128(cursor, format, StatTint, 11f);
                    cursor.y -= 0.15f;
                }
            }

            private void RenderIntrinsics(ref float3 cursor, DynamicBuffer<Intrinsic> buffer, DynamicBuffer<Stat> stats)
            {
                ref var names = ref this.DebugNames.Value.Value.IntrinsicNames;
                ref var configDatas = ref this.Config.Value.Value.IntrinsicDatas;

                foreach (var kvp in buffer.AsMap())
                {
                    var format = new FixedString128Bytes();
                    if (names.TryGetValue(kvp.Key.Value, out var namePtr)) format.Append(namePtr.Ref);
                    else format.Append(kvp.Key.Value);
                        
                    this.PadRight(ref format);
                    format.Append(kvp.Value);

                    // Show linear bounds if configured
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

                    this.Renderer.Text128(cursor, format, IntrinsicTint, 11f);
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