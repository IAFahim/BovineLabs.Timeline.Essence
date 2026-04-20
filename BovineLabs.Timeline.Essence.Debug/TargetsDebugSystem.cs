#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Quill;
using BovineLabs.Reaction.Data.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Timeline.EntityLinks.Debug
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct TargetsDebugSystem : ISystem
    {
        private UnsafeComponentLookup<LocalToWorld> ltwLookup;
        private UnsafeComponentLookup<TargetsCustom> targetsCustomLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            ltwLookup = state.GetUnsafeComponentLookup<LocalToWorld>(true);
            targetsCustomLookup = state.GetUnsafeComponentLookup<TargetsCustom>(true);
            state.RequireForUpdate<DrawSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ltwLookup.Update(ref state);
            targetsCustomLookup.Update(ref state);

            var drawer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();

            state.Dependency = new DrawTargetsJob
            {
                Drawer = drawer,
                LtwLookup = ltwLookup,
                TargetsCustomLookup = targetsCustomLookup
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        private partial struct DrawTargetsJob : IJobEntity
        {
            public Drawer Drawer;
            [ReadOnly] public UnsafeComponentLookup<LocalToWorld> LtwLookup;
            [ReadOnly] public UnsafeComponentLookup<TargetsCustom> TargetsCustomLookup;

            private static readonly Color ColorOwner = new(0.2f, 0.8f, 1.0f);
            private static readonly Color ColorSource = new(1.0f, 0.6f, 0.1f);
            private static readonly Color ColorTarget = new(1.0f, 0.2f, 0.4f);
            private static readonly Color ColorCustom0 = new(0.4f, 1.0f, 0.4f);
            private static readonly Color ColorCustom1 = new(0.8f, 0.3f, 1.0f);

            private void Execute(Entity entity, in LocalToWorld ltw, in Targets targets)
            {
                var nullCount = 0;

                DrawTether(entity, ltw.Position, targets.Owner, "Owner", ColorOwner, 0, ref nullCount);
                DrawTether(entity, ltw.Position, targets.Source, "Source", ColorSource, 1, ref nullCount);
                DrawTether(entity, ltw.Position, targets.Target, "Target", ColorTarget, 2, ref nullCount);

                if (TargetsCustomLookup.TryGetComponent(entity, out var custom))
                {
                    DrawTether(entity, ltw.Position, custom.Target0, "Custom0", ColorCustom0, 3, ref nullCount);
                    DrawTether(entity, ltw.Position, custom.Target1, "Custom1", ColorCustom1, 4, ref nullCount);
                }
            }

            private void DrawTether(Entity self, float3 selfPos, Entity target, FixedString32Bytes label, Color color,
                int index, ref int nullCount)
            {
                if (target == Entity.Null)
                {
                    var dimColor = color;
                    dimColor.a = 0.4f;
                    var nullPos = selfPos + new float3(0, 0.8f + nullCount * 0.25f, 0);
                    Drawer.Text32(nullPos, $"[No {label}]", dimColor, 10f);
                    nullCount++;
                    return;
                }

                if (!LtwLookup.TryGetComponent(target, out var targetLtw))
                {
                    var errPos = selfPos + new float3(0, 0.8f + nullCount * 0.25f, 0);
                    Drawer.Text32(errPos, $"[{label} has no Transform]", Color.red, 10f);
                    nullCount++;
                    return;
                }

                var targetPos = targetLtw.Position;

                if (self == target || math.all(selfPos == targetPos))
                {
                    DrawSelfLoop(selfPos, label, color, index);
                    return;
                }

                DrawCurvedTether(selfPos, targetPos, label, color, index);
            }

            private void DrawCurvedTether(float3 start, float3 end, FixedString32Bytes label, Color color, int index)
            {
                var distance = math.distance(start, end);
                var mid = (start + end) * 0.5f;

                mid.y += distance * 0.2f + index * 0.1f;

                const int segments = 16;
                var lines = new NativeList<float3>(segments * 2, Allocator.Temp);
                var prev = start;

                for (var i = 1; i <= segments; i++)
                {
                    var t = i / (float)segments;
                    var current = math.lerp(math.lerp(start, mid, t), math.lerp(mid, end, t), t);

                    lines.Add(prev);
                    lines.Add(current);
                    prev = current;
                }

                Drawer.Lines(lines.AsArray(), color);

                var dir = math.normalize(end - lines[lines.Length - 4]);
                Drawer.Arrow(end - dir * 0.1f, dir * 0.25f, color);

                Drawer.Text32(mid + new float3(0, 0.2f, 0), label, color, 11f);
                lines.Dispose();
            }

            private void DrawSelfLoop(float3 pos, FixedString32Bytes label, Color color, int index)
            {
                var height = 1.0f + index * 0.3f;
                var spread = 0.5f + index * 0.1f;

                var p0 = pos;
                var p1 = pos + new float3(spread, height, 0);
                var p2 = pos + new float3(-spread, height, 0);
                var p3 = pos;

                const int segments = 16;
                var lines = new NativeList<float3>(segments * 2, Allocator.Temp);
                var prev = p0;

                for (var i = 1; i <= segments; i++)
                {
                    var t = i / (float)segments;
                    var u = 1 - t;

                    var current = u * u * u * p0 +
                                  3 * u * u * t * p1 +
                                  3 * u * t * t * p2 +
                                  t * t * t * p3;

                    lines.Add(prev);
                    lines.Add(current);
                    prev = current;
                }

                Drawer.Lines(lines.AsArray(), color);

                var dir = math.normalize(p3 - lines[lines.Length - 4]);
                Drawer.Arrow(pos - dir * 0.05f, dir * 0.2f, color);

                var topPos = pos + new float3(0, height + 0.1f, 0);
                Drawer.Text32(topPos, label, color, 10f);
                lines.Dispose();
            }
        }
    }
}
#endif