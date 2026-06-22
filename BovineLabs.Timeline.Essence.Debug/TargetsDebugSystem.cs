#if UNITY_EDITOR || BL_DEBUG
using System.Diagnostics.CodeAnalysis;
using BovineLabs.Core;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Quill;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Core.Debug;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Essence.Debug
{
    [Configurable]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1611:Element parameters should be documented",
        Justification = "Using see cref")]
    public static class TargetsDebugSystemConfig
    {
        private const string DrawForced = "targets.draw-enabled";
        private const string DrawGlobalDescEnabled = "Enable the Targets debug drawer in the editor.";

        [ConfigVar(DrawForced, false, DrawGlobalDescEnabled)]
        public static readonly SharedStatic<bool> Enabled =
            SharedStatic<bool>.GetOrCreate<Tags.Enabled>();

        private struct Tags
        {
            public struct Enabled
            {
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct TargetsDebugSystem : ISystem
    {
        private UnsafeComponentLookup<LocalToWorld> _ltwLookup;
        private UnsafeComponentLookup<LocalTransform> _localTransformLookup;
        private UnsafeComponentLookup<Parent> _parentLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _ltwLookup = state.GetUnsafeComponentLookup<LocalToWorld>(true);
            _localTransformLookup = state.GetUnsafeComponentLookup<LocalTransform>(true);
            _parentLookup = state.GetUnsafeComponentLookup<Parent>(true);
            state.RequireForUpdate<DrawSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            _ltwLookup.Update(ref state);
            _localTransformLookup.Update(ref state);
            _parentLookup.Update(ref state);

            if (!TimelineDebugUtility.TryGetDrawer<TargetsDebugSystem>(ref state, TargetsDebugSystemConfig.Enabled.Data,
                    out var drawer, out var viewer, out var hasViewer))
                return;

            var names = new NativeHashMap<Entity, FixedString64Bytes>(64, Allocator.TempJob);
            foreach (var (targetsRO, _) in SystemAPI.Query<RefRO<Targets>>().WithEntityAccess())
            {
                var t = targetsRO.ValueRO;
                AddName(ref state, ref names, t.Owner);
                AddName(ref state, ref names, t.Source);
                AddName(ref state, ref names, t.Target);
                AddName(ref state, ref names, t.Custom);
            }

            state.Dependency = new DrawTargetsJob
            {
                Drawer = drawer,
                Viewer = viewer,
                HasViewer = hasViewer,
                LtwLookup = _ltwLookup,
                LocalTransformLookup = _localTransformLookup,
                ParentLookup = _parentLookup,
                Names = names
            }.Schedule(state.Dependency);

            names.Dispose(state.Dependency);
        }

        private static void AddName(ref SystemState state, ref NativeHashMap<Entity, FixedString64Bytes> names,
            Entity e)
        {
            if (e == Entity.Null || names.ContainsKey(e))
                return;

            state.EntityManager.GetName(e, out var name);
            names.Add(e, name);
        }

        [BurstCompile]
        private partial struct DrawTargetsJob : IJobEntity
        {
            public Drawer Drawer;
            public float3 Viewer;
            public bool HasViewer;
            [ReadOnly] public UnsafeComponentLookup<LocalToWorld> LtwLookup;
            [ReadOnly] public UnsafeComponentLookup<LocalTransform> LocalTransformLookup;
            [ReadOnly] public UnsafeComponentLookup<Parent> ParentLookup;
            [ReadOnly] public NativeHashMap<Entity, FixedString64Bytes> Names;

            private float3 GetAntiJitterPosition(Entity e, float3 fallback)
            {
                if (LocalTransformLookup.HasComponent(e) && !ParentLookup.HasComponent(e))
                    return LocalTransformLookup[e].Position;
                return fallback;
            }

            private static readonly Color ColorOwner = TimelineDebugColors.OwnerLink;
            private static readonly Color ColorSource = TimelineDebugColors.SourceLink;
            private static readonly Color ColorTarget = TimelineDebugColors.TargetLink;
            private static readonly Color ColorCustom = TimelineDebugColors.CustomLink;

            private void Execute(Entity entity, in LocalToWorld ltw, in Targets targets)
            {
                var nullCount = 0;
                var selfPos = GetAntiJitterPosition(entity, ltw.Position);
                var tier = TimelineDebugTier.Resolve(selfPos, Viewer, HasViewer);

                var fsOwner = new FixedString32Bytes();
                fsOwner.Append('O');
                fsOwner.Append('w');
                fsOwner.Append('r');
                DrawTether(entity, selfPos, targets.Owner, fsOwner, ColorOwner, 0,
                    ref nullCount, tier);
                var fsSource = new FixedString32Bytes();
                fsSource.Append('S');
                fsSource.Append('r');
                fsSource.Append('c');
                DrawTether(entity, selfPos, targets.Source, fsSource, ColorSource,
                    1, ref nullCount, tier);
                var fsTarget = new FixedString32Bytes();
                fsTarget.Append('T');
                fsTarget.Append('g');
                fsTarget.Append('t');
                DrawTether(entity, selfPos, targets.Target, fsTarget, ColorTarget,
                    2, ref nullCount, tier);
                var fsCustom = new FixedString32Bytes();
                fsCustom.Append('C');
                fsCustom.Append('t');
                fsCustom.Append('m');
                DrawTether(entity, selfPos, targets.Custom, fsCustom, ColorCustom,
                    3, ref nullCount, tier);
            }

            private void DrawTether(Entity self, float3 selfPos, Entity target, FixedString32Bytes label, Color color,
                int index, ref int nullCount, DebugTier tier)
            {
                if (target == Entity.Null)
                {
                    if (tier != DebugTier.Close)
                        return;

                    var dimColor = color;
                    dimColor.a = 0.4f;
                    var nullPos = selfPos + new float3(0, 0.8f + nullCount * 0.25f, 0);
                    var msg = new FixedString32Bytes();
                    msg.Append('[');
                    msg.Append('N');
                    msg.Append('o');
                    msg.Append(' ');
                    msg.Append(label);
                    msg.Append(']');
                    Drawer.Text32(nullPos, msg, dimColor, 10f);
                    nullCount++;
                    return;
                }

                if (!LtwLookup.TryGetComponent(target, out var targetLtw))
                {
                    var errPos = selfPos + new float3(0, 0.8f + nullCount * 0.25f, 0);
                    var msg = new FixedString32Bytes();
                    msg.Append('[');
                    msg.Append(label);
                    msg.Append(' ');
                    msg.Append('n');
                    msg.Append('o');
                    msg.Append(' ');
                    msg.Append('T');
                    msg.Append('r');
                    msg.Append('a');
                    msg.Append('n');
                    msg.Append('s');
                    msg.Append('f');
                    msg.Append('o');
                    msg.Append('r');
                    msg.Append('m');
                    msg.Append(']');
                    Drawer.Text32(errPos, msg, TimelineDebugColors.Error, 10f);
                    nullCount++;
                    return;
                }

                var targetPos = GetAntiJitterPosition(target, targetLtw.Position);

                var display = new FixedString128Bytes();
                display.Append(label);
                if (Names.TryGetValue(target, out var name))
                {
                    display.Append("-");
                    display.Append(name);
                }

                if (self == target || math.all(selfPos == targetPos))
                {
                    DrawSelfLoop(selfPos, display, color, index, tier, label);
                    return;
                }

                DrawCurvedTether(selfPos, targetPos, display, color, index, tier, label);
            }

            private unsafe void DrawCurvedTether(float3 start, float3 end, FixedString128Bytes label, Color color,
                int index, DebugTier tier, FixedString32Bytes slot)
            {
                var distance = math.distance(start, end);
                var mid = (start + end) * 0.5f;

                mid.y += distance * 0.2f + index * 0.1f;

                const int segments = 16;
                const int points = segments * 2;
                var linesData = stackalloc float3[points];
                var lines = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float3>(linesData, points,
                    Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref lines, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

                var lineLength = 0;
                var prev = start;

                for (var i = 1; i <= segments; i++)
                {
                    var t = i / (float)segments;
                    var current = math.lerp(math.lerp(start, mid, t), math.lerp(mid, end, t), t);

                    lines[lineLength++] = prev;
                    lines[lineLength++] = current;
                    prev = current;
                }

                Drawer.Lines(lines.GetSubArray(0, lineLength), color);

                var dir = math.normalize(end - lines[lineLength - 4]);
                Drawer.Arrow(end - dir * 0.1f, dir * 0.25f, color);

                if (tier == DebugTier.Mid)
                    Drawer.Text32(mid + new float3(0, 0.2f, 0), slot, color, 11f);

                if (tier == DebugTier.Close)
                {
                    Drawer.Text128(mid + new float3(0, 0.2f, 0), label, color, 11f);
                    var readout = new FixedString128Bytes();
                    readout.Append(distance);
                    readout.Append((FixedString32Bytes)"m");
                    Drawer.Text128(mid + new float3(0, -0.1f, 0), readout, TimelineDebugColors.Label, 10f);
                }
            }

            private unsafe void DrawSelfLoop(float3 pos, FixedString128Bytes label, Color color, int index,
                DebugTier tier, FixedString32Bytes slot)
            {
                var height = 1.0f + index * 0.3f;
                var spread = 0.5f + index * 0.1f;

                var p0 = pos;
                var p1 = pos + new float3(spread, height, 0);
                var p2 = pos + new float3(-spread, height, 0);
                var p3 = pos;

                const int segments = 16;
                const int points = segments * 2;
                var linesData = stackalloc float3[points];
                var lines = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float3>(linesData, points,
                    Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref lines, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

                var lineLength = 0;
                var prev = p0;

                for (var i = 1; i <= segments; i++)
                {
                    var t = i / (float)segments;
                    var u = 1 - t;

                    var current = u * u * u * p0 +
                                  3 * u * u * t * p1 +
                                  3 * u * t * t * p2 +
                                  t * t * t * p3;

                    lines[lineLength++] = prev;
                    lines[lineLength++] = current;
                    prev = current;
                }

                Drawer.Lines(lines.GetSubArray(0, lineLength), color);

                var dir = math.normalize(p3 - lines[lineLength - 4]);
                Drawer.Arrow(pos - dir * 0.05f, dir * 0.2f, color);

                var topPos = pos + new float3(0, height + 0.1f, 0);

                if (tier == DebugTier.Mid)
                    Drawer.Text32(topPos, slot, color, 10f);
                else if (tier == DebugTier.Close)
                    Drawer.Text128(topPos, label, color, 10f);
            }
        }
    }
}
#endif