#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Timeline.Core.Debug;
using Unity.Entities;

namespace BovineLabs.Essence.Debug
{
    /// <summary>
    /// Shared gate for the telemetry recorder systems (history / trend). A recorder should only do work when the
    /// matching telemetry panel would actually render this frame — otherwise it burns time (and grows buffers)
    /// while nothing is drawn. This reuses the exact mechanism <see cref="TimelineDebugUtility.TryGetDrawer{TSystem}(ref SystemState, bool, out Drawer)"/>
    /// uses to decide a panel is active, without emitting any draw commands.
    /// </summary>
    internal static class TelemetryGate
    {
        /// <summary>
        /// True when the panel drawn by <typeparamref name="TSystem"/> would render this frame — either because it is
        /// force-enabled via its ConfigVar (<paramref name="forceFlag"/>), or because its Quill drawer is toggled on in
        /// the viewer. The created drawer is discarded (never written to), so this is a pure "is-active" query.
        /// </summary>
        public static bool IsActive<TSystem>(ref SystemState state, bool forceFlag)
            where TSystem : unmanaged, ISystem
        {
            return TimelineDebugUtility.TryGetDrawer<TSystem>(ref state, forceFlag, out _);
        }
    }
}
#endif
