#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core.ConfigVars;
using BovineLabs.Quill;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Essence.Debug
{
    [Configurable]
    public static class TelemetryLayoutConfig
    {
        [ConfigVar("telemetry.font-size", 12f, "Font size for standard text.")]
        internal static readonly SharedStatic<float> FontSize = SharedStatic<float>.GetOrCreate<FontSizeType>();

        [ConfigVar("telemetry.title-size", 14f, "Font size for panel titles.")]
        internal static readonly SharedStatic<float> TitleSize = SharedStatic<float>.GetOrCreate<TitleSizeType>();

        [ConfigVar("telemetry.line-height", 1.2f, "Vertical space between lines.")]
        internal static readonly SharedStatic<float> LineHeight = SharedStatic<float>.GetOrCreate<LineHeightType>();

        [ConfigVar("telemetry.group-spacing", 0.6f, "Extra vertical space multiplier between distinct items.")]
        internal static readonly SharedStatic<float> GroupSpacing = SharedStatic<float>.GetOrCreate<GroupSpacingType>();

        [ConfigVar("telemetry.indent", 0.8f, "Horizontal indent for details.")]
        internal static readonly SharedStatic<float> Indent = SharedStatic<float>.GetOrCreate<IndentType>();

        [ConfigVar("telemetry.panel-spacing", 6.0f, "Horizontal space between left and right panels.")]
        internal static readonly SharedStatic<float> PanelSpacing = SharedStatic<float>.GetOrCreate<PanelSpacingType>();

        [ConfigVar("telemetry.shadow-offset", 0.04f, "Drop shadow offset.")]
        internal static readonly SharedStatic<float> ShadowOffset = SharedStatic<float>.GetOrCreate<ShadowOffsetType>();

        [ConfigVar("telemetry.scale-k", 5f, "Internal scale multiplier for font size to world units.")]
        internal static readonly SharedStatic<float> ScaleK = SharedStatic<float>.GetOrCreate<ScaleKType>();

        [ConfigVar("telemetry.lod-distance", 30f, "Distance beyond which full details are hidden to reduce clutter.")]
        internal static readonly SharedStatic<float> LodDistance = SharedStatic<float>.GetOrCreate<LodDistanceType>();

        private struct FontSizeType { }
        private struct TitleSizeType { }
        private struct LineHeightType { }
        private struct GroupSpacingType { }
        private struct IndentType { }
        private struct PanelSpacingType { }
        private struct ShadowOffsetType { }
        private struct ScaleKType { }
        private struct LodDistanceType { }
    }

    public readonly struct View
    {
        public readonly float3 Anchor;
        public readonly float3 Right;
        public readonly float3 Up;
        public readonly float Unit;
        public readonly float Distance;

        private static readonly float3 WorldUp = new(0f, 1f, 0f);

        public View(float3 anchor, float3 right, float3 up, float unit, float distance)
        {
            Anchor = anchor;
            Right = right;
            Up = up;
            Unit = unit;
            Distance = distance;
        }

        public float3 At(float x, float y) => Anchor + Right * (x * Unit) + Up * (y * Unit);

        public float Size(float px) => px * Unit * TelemetryLayoutConfig.ScaleK.Data;

        public View NudgeWorld(float3 delta) =>
            new(Anchor + delta, Right, Up, Unit, Distance);

        public View Shift(float dx, float dy) =>
            new(Anchor + Right * (dx * Unit) + Up * (dy * Unit), Right, Up, Unit, Distance);

        public static View WorldFacing(in CameraCulling cam, float3 world, float fixedScale)
        {
            if (!cam.IsDefault && EyeFromPlanes(cam, out var eye))
            {
                var toEye = eye - world;
                var dist = math.length(toEye);
                if (dist > 1e-4f)
                {
                    var normal = toEye / dist;
                    var right = SafeRight(normal);
                    var up = math.cross(normal, right);
                    return new View(world, right, up, fixedScale, dist);
                }
            }

            return new View(world, new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), fixedScale, 0f);
        }

        private static bool EyeFromPlanes(in CameraCulling cam, out float3 eye)
        {
            var n1 = cam.Left.xyz;  var d1 = cam.Left.w;
            var n2 = cam.Right.xyz; var d2 = cam.Right.w;
            var n3 = cam.Top.xyz;   var d3 = cam.Top.w;
            var c23 = math.cross(n2, n3);
            var denom = math.dot(n1, c23);
            if (math.abs(denom) < 1e-6f) { eye = default; return false; }
            eye = (-d1 * c23 - d2 * math.cross(n3, n1) - d3 * math.cross(n1, n2)) / denom;
            return true;
        }

        private static float3 SafeRight(float3 normal)
        {
            var r = math.cross(WorldUp, normal);
            var len = math.length(r);
            return len > 1e-4f ? r / len : new float3(1f, 0f, 0f);
        }
    }

    public static class Ink
    {
        public static readonly Color Label  = new(0.72f, 0.77f, 0.84f, 0.90f);
        public static readonly Color Value  = new(0.97f, 0.98f, 1f, 1f);
        public static readonly Color Shadow = new(0f, 0f, 0f, 0.85f);
        public static readonly Color Live   = new(0.36f, 0.92f, 0.55f, 1f);
        public static readonly Color Idle   = new(0.62f, 0.34f, 0.36f, 0.85f);
        public static readonly Color Muted  = new(0.55f, 0.58f, 0.65f, 0.90f);

        public static Color Dim(Color c, float alpha) =>
            new(c.r, c.g, c.b, c.a * math.saturate(alpha));
    }

    public static class Glyph
    {
        public static void Text(Drawer d, in View v, float x, float y, in FixedString128Bytes s, Color c, float px)
        {
            var h = TelemetryLayoutConfig.ShadowOffset.Data;
            d.Text128(v.At(x + h, y - h), s, Ink.Shadow, v.Size(px));
            d.Text128(v.At(x, y), s, c, v.Size(px));
        }

        public static void AppendProgressBar(ref FixedString128Bytes text, float pct, int length = 10)
        {
            text.Append('[');
            int fill = (int)math.clamp(math.round(pct * length), 0, length);
            for (int i = 0; i < fill; i++) text.Append('=');
            for (int i = fill; i < length; i++) text.Append('-');
            text.Append(']');
            text.Append(' ');
        }
    }
}
#endif