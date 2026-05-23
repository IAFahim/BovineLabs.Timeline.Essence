#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Essence.Data;
using BovineLabs.Quill;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Essence.Debug
{
    [InternalBufferCapacity(0)]
    public struct StatTrendSample : IBufferElementData
    {
        public ushort Key;
        public float Value;
        public double Timestamp;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct StatTrendSystem : ISystem
    {
        private const double SampleInterval = 0.1;
        private const double RetentionWindow = 4.0;

        private double nextSample;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate(SystemAPI.QueryBuilder().WithAll<Stat>().Build());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!EssenceTelemetryConfig.Enabled.Data && !SystemAPI.HasSingleton<DrawSystem.Singleton>()) return;

            var time = SystemAPI.Time.ElapsedTime;
            if (time < this.nextSample) return;
            this.nextSample = time + SampleInterval;

            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            foreach (var (_, entity) in SystemAPI.Query<DynamicBuffer<Stat>>().WithNone<StatTrendSample>().WithEntityAccess())
            {
                ecb.AddBuffer<StatTrendSample>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            foreach (var (stats, trend) in SystemAPI.Query<DynamicBuffer<Stat>, DynamicBuffer<StatTrendSample>>())
            {
                while (trend.Length > 0 && time - trend[0].Timestamp > RetentionWindow)
                {
                    trend.RemoveAt(0);
                }

                foreach (var stat in stats.AsMap())
                {
                    trend.Add(new StatTrendSample
                    {
                        Key = stat.Key.Value,
                        Value = stat.Value.Value,
                        Timestamp = time,
                    });
                }
            }
        }
    }
}
#endif