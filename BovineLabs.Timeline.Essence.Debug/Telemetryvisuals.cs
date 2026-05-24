#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core.ConfigVars;
using BovineLabs.Quill;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Essence.Debug
{
    [Configurable]
    public static class TelemetryVisualConfig
    {
        [ConfigVar("telemetry.visual-overlay", true, "Draw visual overlays alongside telemetry text.")]
        internal static readonly SharedStatic<bool> VisualOverlay = SharedStatic<bool>.GetOrCreate<VOType>();

        [ConfigVar("telemetry.visual-dots", true, "Draw per-line fill indicator dots (requires FullDetails range).")]
        internal static readonly SharedStatic<bool> ShowDots = SharedStatic<bool>.GetOrCreate<SDType>();

        [ConfigVar("telemetry.visual-lod", 80f, "Max distance at which the health beacon is drawn.")]
        internal static readonly SharedStatic<float> BeaconLod = SharedStatic<float>.GetOrCreate<BLType>();

        [ConfigVar("telemetry.cond-bits", 16, "Number of condition bits shown in the ring.")]
        internal static readonly SharedStatic<int> CondBits = SharedStatic<int>.GetOrCreate<CBType>();

        [ConfigVar("telemetry.arc-gap", 0.07f, "Gap between condition ring arc segments (radians).")]
        internal static readonly SharedStatic<float> ArcGap = SharedStatic<float>.GetOrCreate<AGType>();

        [ConfigVar("telemetry.ripple-max-r", 2.0f, "Event ripple maximum expansion radius (world units).")]
        internal static readonly SharedStatic<float> RippleMaxR = SharedStatic<float>.GetOrCreate<RMType>();

        [ConfigVar("telemetry.ripple-life", 1.4f, "Event ripple lifetime in seconds.")]
        internal static readonly SharedStatic<float> RippleLife = SharedStatic<float>.GetOrCreate<RLType>();

        [ConfigVar("telemetry.ripple-offset-x", 0f, "Horizontal glyph-space offset for ripple anchors (0 = centre-aligned with text). Negative = left.")]
        internal static readonly SharedStatic<float> RippleOffsetX = SharedStatic<float>.GetOrCreate<ROXType>();

        private struct VOType { }  private struct SDType { }  private struct BLType { }
        private struct CBType { }  private struct AGType { }
        private struct RMType { }  private struct RLType { }  private struct ROXType { }
    }

    public static class VisualGlyph
    {
        private const float FullArc = math.PI * 2f - 0.001f;

        public static float3 Normal(in View v) => math.cross(v.Right, v.Up);

        public static Color HealthGradient(float pct)
        {
            pct = math.saturate(pct);
            float r, g;
            if (pct <= 0.5f) { r = 0.92f; g = pct * 1.60f; }
            else             { r = (1f - pct) * 1.72f; g = 0.80f; }
            return new Color(r, g, 0.07f, 1f);
        }

        public static Color KeyColor(ushort key, float s = 0.70f, float vv = 0.88f) =>
            HsvToRgb(key * 137.508f % 360f / 360f, s, vv);

        public static void Beacon(Drawer d, in View v, float glyphX, float glyphY,
            float glyphRadius, Color color)
        {
            var center  = v.At(glyphX, glyphY);
            var worldR  = glyphRadius * v.Unit;
            var normal  = Normal(v);
            d.Circle(center, normal * worldR, color);
        }

        public static void BeaconPulse(Drawer d, in View v, float glyphX, float glyphY,
            float glyphRadius, float time, Color color)
        {
            var p      = math.sin(time * 3.8f) * 0.5f + 0.5f;
            var center = v.At(glyphX, glyphY);
            var worldR = glyphRadius * v.Unit * (1.16f + p * 0.30f);
            var a      = (0.25f + p * 0.60f) * color.a;
            d.Circle(center, Normal(v) * worldR, new Color(color.r, color.g, color.b, a));
        }

        public static void StatusDot(Drawer d, in View v, float glyphX, float glyphY, float fill)
        {
            var size = TelemetryLayoutConfig.LineHeight.Data * 0.14f * v.Unit;
            d.Point(v.At(glyphX, glyphY), size, HealthGradient(fill));
        }

        public static void ConditionRing(Drawer d, in View v,
            float glyphX, float glyphY, float glyphRadius,
            uint mask, int bits, Color setColor, Color clearColor)
        {
            var center  = v.At(glyphX, glyphY);
            var worldR  = glyphRadius * v.Unit;
            var normal  = Normal(v);
            var up      = v.Up;
            var n       = math.min(bits, 32);
            var gap     = TelemetryVisualConfig.ArcGap.Data;

            for (var i = 0; i < n; i++)
            {
                var slot  = math.PI * 2f / n;
                var start = i * slot + gap * 0.5f;
                var sweep = slot - gap;
                if (sweep < 0.005f) continue;
                var arm   = math.mul(quaternion.AxisAngle(normal, start), up) * worldR;
                var isSet = (mask & (1u << i)) != 0;
                if (!isSet)
                {
                    d.Arc(center, normal, arm, sweep, clearColor);
                }
                else
                {
                    d.Arc(center, normal, arm, sweep, clearColor);
                    d.Arc(center, normal, arm, sweep, setColor);
                }
            }
        }

        public static void Ripple(Drawer d, in View v, float glyphX, float glyphY,
            float age, float life, float maxWorldR, Color color)
        {
            var t = math.saturate(age / life);
            if (t >= 0.98f) return;
            var worldR = maxWorldR * t;
            if (worldR < 0.01f) return;
            var a = (1f - t) * (1f - t) * color.a;
            if (a < 0.015f) return;
            var center = v.At(glyphX, glyphY);
            d.Circle(center, Normal(v) * worldR, new Color(color.r, color.g, color.b, a));
        }

        private static Color HsvToRgb(float h, float s, float vv)
        {
            var c  = vv * s;
            var hp = h * 6f;
            var x  = c * (1f - math.abs(hp % 2f - 1f));
            var m  = vv - c;
            var ii = (int)hp % 6;
            float r, g, b;
            if      (ii == 0) { r = c; g = x; b = 0; }
            else if (ii == 1) { r = x; g = c; b = 0; }
            else if (ii == 2) { r = 0; g = c; b = x; }
            else if (ii == 3) { r = 0; g = x; b = c; }
            else if (ii == 4) { r = x; g = 0; b = c; }
            else              { r = c; g = 0; b = x; }
            return new Color(r + m, g + m, b + m, 1f);
        }
    }
}
#endif