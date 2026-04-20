#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Essence.Data;
using BovineLabs.Quill;
using BovineLabs.Reaction.Data.Conditions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Essence.Debug
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct EssenceTelemetrySystem : ISystem
    {
        private EntityQuery telemetryQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            telemetryQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalToWorld>()
                .WithAny<Stat, Intrinsic, ConditionEvent>()
                .Build();

            state.RequireForUpdate<DrawSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var renderer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();

            state.Dependency = new RenderTelemetryJob
            {
                Renderer = renderer,
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                StatHandle = SystemAPI.GetBufferTypeHandle<Stat>(true),
                IntrinsicHandle = SystemAPI.GetBufferTypeHandle<Intrinsic>(true),
                EventHandle = SystemAPI.GetBufferTypeHandle<ConditionEvent>(true)
            }.Schedule(telemetryQuery, state.Dependency);
        }

        [BurstCompile]
        private struct RenderTelemetryJob : IJobChunk
        {
            public Drawer Renderer;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public BufferTypeHandle<Stat> StatHandle;
            [ReadOnly] public BufferTypeHandle<Intrinsic> IntrinsicHandle;
            [ReadOnly] public BufferTypeHandle<ConditionEvent> EventHandle;

            private static readonly Color StatTint = new(0.2f, 0.9f, 0.4f, 1f);
            private static readonly Color IntrinsicTint = new(0.1f, 0.6f, 0.9f, 1f);
            private static readonly Color EventTint = new(0.9f, 0.4f, 0.2f, 1f);
            private static readonly Color HeaderTint = new(1f, 1f, 1f, 0.2f);

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var statsAccessor = chunk.GetBufferAccessor(ref StatHandle);
                var intrinsicsAccessor = chunk.GetBufferAccessor(ref IntrinsicHandle);
                var eventsAccessor = chunk.GetBufferAccessor(ref EventHandle);

                var hasStats = statsAccessor.Length > 0;
                var hasIntrinsics = intrinsicsAccessor.Length > 0;
                var hasEvents = eventsAccessor.Length > 0;

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var index))
                {
                    var origin = transforms[index].Position;
                    var cursor = origin + new float3(0f, 1.5f, 0f);

                    Renderer.Line(origin, cursor, HeaderTint);
                    Renderer.Point(origin, 0.05f, HeaderTint);

                    if (hasStats) RenderStats(ref cursor, statsAccessor[index]);
                    if (hasIntrinsics) RenderIntrinsics(ref cursor, intrinsicsAccessor[index]);
                    if (hasEvents) RenderEvents(ref cursor, eventsAccessor[index]);
                }
            }

            private void RenderStats(ref float3 cursor, DynamicBuffer<Stat> buffer)
            {
                foreach (var kvp in buffer.AsMap())
                {
                    var format = new FixedString64Bytes();
                    format.Append("[STA] ");
                    format.Append(kvp.Key.Value);
                    format.Append(" : ");
                    format.Append(kvp.Value.Value);

                    Renderer.Text64(cursor, format, StatTint, 11f);
                    cursor.y += 0.15f;
                }
            }

            private void RenderIntrinsics(ref float3 cursor, DynamicBuffer<Intrinsic> buffer)
            {
                foreach (var kvp in buffer.AsMap())
                {
                    var format = new FixedString64Bytes();
                    format.Append("[INT] ");
                    format.Append(kvp.Key.Value);
                    format.Append(" : ");
                    format.Append(kvp.Value);

                    Renderer.Text64(cursor, format, IntrinsicTint, 11f);
                    cursor.y += 0.15f;
                }
            }

            private void RenderEvents(ref float3 cursor, DynamicBuffer<ConditionEvent> buffer)
            {
                foreach (var kvp in buffer.AsMap())
                {
                    var format = new FixedString64Bytes();
                    format.Append("[EVT] ");
                    format.Append(kvp.Key.Value);
                    format.Append(" : ");
                    format.Append(kvp.Value);

                    Renderer.Text64(cursor, format, EventTint, 11f);
                    cursor.y += 0.15f;
                }
            }
        }
    }
}
#endif