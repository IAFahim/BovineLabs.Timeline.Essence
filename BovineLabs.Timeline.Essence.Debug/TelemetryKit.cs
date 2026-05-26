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
    public static class TelemetryConfig
    {
        [ConfigVar("telemetry.font-size", 12f, "Font size for standard text.")]
        internal static readonly SharedStatic<float> FontSize = SharedStatic<float>.GetOrCreate<K01>();

        [ConfigVar("telemetry.title-size", 14f, "Font size for panel titles.")]
        internal static readonly SharedStatic<float> TitleSize = SharedStatic<float>.GetOrCreate<K02>();

        [ConfigVar("telemetry.line-height", 1.2f, "Vertical space between lines.")]
        internal static readonly SharedStatic<float> LineHeight = SharedStatic<float>.GetOrCreate<K03>();

        [ConfigVar("telemetry.group-spacing", 0.6f, "Extra vertical multiplier between groups.")]
        internal static readonly SharedStatic<float> GroupSpacing = SharedStatic<float>.GetOrCreate<K04>();

        [ConfigVar("telemetry.indent", 0.8f, "Horizontal indent for detail rows.")]
        internal static readonly SharedStatic<float> Indent = SharedStatic<float>.GetOrCreate<K05>();

        [ConfigVar("telemetry.panel-spacing", 6.0f, "Horizontal gap between side-by-side panels.")]
        internal static readonly SharedStatic<float> PanelSpacing = SharedStatic<float>.GetOrCreate<K06>();

        [ConfigVar("telemetry.shadow-offset", 0.04f, "Drop shadow offset.")]
        internal static readonly SharedStatic<float> ShadowOffset = SharedStatic<float>.GetOrCreate<K07>();

        [ConfigVar("telemetry.scale-k", 5f, "Font-to-world multiplier.")]
        internal static readonly SharedStatic<float> ScaleK = SharedStatic<float>.GetOrCreate<K08>();

        [ConfigVar("telemetry.row-width", 12f, "Full width of a bar row in glyph units.")]
        internal static readonly SharedStatic<float> RowWidth = SharedStatic<float>.GetOrCreate<K09>();

        [ConfigVar("telemetry.bar-bg-alpha", 0.12f, "Background alpha for unfilled bar region.")]
        internal static readonly SharedStatic<float> BarBgAlpha = SharedStatic<float>.GetOrCreate<K10>();

        [ConfigVar("telemetry.bar-fill-alpha", 0.35f, "Fill alpha for the value portion of the bar.")]
        internal static readonly SharedStatic<float> BarFillAlpha = SharedStatic<float>.GetOrCreate<K11>();

        [ConfigVar("telemetry.text-nudge", 0.01f, "Forward nudge for text to prevent z-fighting with bars.")]
        internal static readonly SharedStatic<float> TextNudge = SharedStatic<float>.GetOrCreate<K12>();

        [ConfigVar("telemetry.log-fill", false, "Use logarithmic scale for bar fill when ranges are very large.")]
        internal static readonly SharedStatic<bool> LogFill = SharedStatic<bool>.GetOrCreate<K13>();

        private struct K01 { } private struct K02 { } private struct K03 { } private struct K04 { }
        private struct K05 { } private struct K06 { } private struct K07 { } private struct K08 { }
        private struct K09 { } private struct K10 { } private struct K11 { } private struct K12 { }
        private struct K13 { }
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

        public float Size(float px) => px * Unit * TelemetryConfig.ScaleK.Data;

        public float3 Normal => math.cross(Right, Up);

        public View NudgeWorld(float3 delta) =>
            new(Anchor + delta, Right, Up, Unit, Distance);

        public View NudgeForward(float epsilon) =>
            new(Anchor + math.normalizesafe(Normal) * epsilon, Right, Up, Unit, Distance);

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

        public static Color WithAlpha(Color c, float a) =>
            new(c.r, c.g, c.b, a);
    }

    public static class Glyph
    {
        public static void Text(Drawer d, in View v, float cx, float y,
            in FixedString128Bytes s, Color c, float px)
        {
            var textView = v.NudgeForward(TelemetryConfig.TextNudge.Data);
            var h = TelemetryConfig.ShadowOffset.Data;
            d.Text128(textView.At(cx + h, y - h), s, Ink.Shadow, textView.Size(px));
            d.Text128(textView.At(cx, y), s, c, textView.Size(px));
        }

        public static void Bar(Drawer d, in View v, float cx, float y,
            float fill, Color accent)
        {
            var halfWidth = TelemetryConfig.RowWidth.Data * 0.5f;
            var halfHeight = TelemetryConfig.LineHeight.Data * 0.4f;
            var bgColor = Ink.WithAlpha(accent, TelemetryConfig.BarBgAlpha.Data);
            var fillColor = Ink.WithAlpha(accent, TelemetryConfig.BarFillAlpha.Data);
            var clampedFill = math.saturate(fill);

            var left = cx - halfWidth;
            var right = cx + halfWidth;
            var bottom = y - halfHeight;
            var top = y + halfHeight;

            var bl = v.At(left, bottom);
            var br = v.At(right, bottom);
            var tr = v.At(right, top);
            var tl = v.At(left, top);
            d.SolidQuad(bl, br, tr, tl, bgColor);

            if (clampedFill > 0.001f)
            {
                var fillRight = left + TelemetryConfig.RowWidth.Data * clampedFill;
                var fr = v.At(fillRight, bottom);
                var ftr = v.At(fillRight, top);
                d.SolidQuad(bl, fr, ftr, tl, fillColor);
            }
        }

        public static float LogFill(float value, float min, float max)
        {
            if (max <= min) return 0f;
            var range = max - min;
            var offset = value - min;
            return math.saturate(math.log(1f + offset) / math.log(1f + range));
        }

        public static float LinearFill(float value, float min, float max)
        {
            if (max <= min) return 0f;
            return math.saturate((value - min) / (max - min));
        }

        public static void BarRow(Drawer d, in View v, float cx, float y,
            in FixedString128Bytes label, float fill, Color accent, float fontSize)
        {
            Bar(d, v, cx, y, fill, accent);
            Text(d, v, cx, y, label, Ink.Value, fontSize);
        }

        public static void DetailRow(Drawer d, in View v, float y,
            in FixedString128Bytes detail, float fontSize)
        {
            Text(d, v, 0f, y, detail, Ink.Muted, fontSize);
        }

        public static void TitleRow(Drawer d, in View v, float y,
            in FixedString128Bytes title, Color accent)
        {
            Text(d, v, 0f, y, title, accent, TelemetryConfig.TitleSize.Data);
        }

        public static float AdvanceLine(float y) =>
            y - TelemetryConfig.LineHeight.Data;

        public static float AdvanceGroup(float y) =>
            y - TelemetryConfig.LineHeight.Data * TelemetryConfig.GroupSpacing.Data;
    }
}
#endif