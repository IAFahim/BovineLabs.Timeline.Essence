#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Quill;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Essence.Debug
{
    public readonly struct View
    {
        public readonly float3 Anchor;
        public readonly float3 Right;
        public readonly float3 Up;
        public readonly float Unit;

        private static readonly float3 WorldUp = new(0f, 1f, 0f);
        private const float MinUnit = 0.005f;

        public View(float3 anchor, float3 right, float3 up, float unit)
        {
            Anchor = anchor;
            Right = right;
            Up = up;
            Unit = unit;
        }

        public float3 At(float x, float y) => Anchor + Right * (x * Unit) + Up * (y * Unit);

        public float Size(float px) => px * Unit * Layout.SizeK;

        public View Shift(float dx, float dy) =>
            new(Anchor + Right * (dx * Unit) + Up * (dy * Unit), Right, Up, Unit);

        public View NudgeWorld(float3 delta) =>
            new(Anchor + delta, Right, Up, Unit);

        public static View Facing(in CameraCulling cam, float3 world, float scale)
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
                    return new View(world, right, up, math.max(MinUnit, dist * scale));
                }
            }

            return new View(world, new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), scale);
        }

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

                    // Use fixedScale directly for pure world-space size, ignoring 'dist'
                    return new View(world, right, up, fixedScale);
                }
            }

            return new View(world, new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), fixedScale);
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

    public struct Pen
    {
        private readonly float leading;
        public float Y;

        public Pen(float startY, float headerDrop, float leading)
        {
            Y = startY - headerDrop;
            this.leading = leading;
        }

        public float Take()
        {
            var y = Y;
            Y -= leading;
            return y;
        }
    }

    public static class Ink
    {
        public static readonly Color Frame  = new(1f, 1f, 1f, 0.12f);
        public static readonly Color Rule   = new(1f, 1f, 1f, 0.20f);
        public static readonly Color Track  = new(1f, 1f, 1f, 0.13f);
        public static readonly Color Label  = new(0.72f, 0.77f, 0.84f, 0.90f);
        public static readonly Color Value  = new(0.97f, 0.98f, 1f, 1f);
        public static readonly Color Muted  = new(0.55f, 0.58f, 0.65f, 0.65f);
        public static readonly Color Shadow = new(0f, 0f, 0f, 0.72f);
        public static readonly Color Live   = new(0.36f, 0.92f, 0.55f, 1f);
        public static readonly Color Idle   = new(0.62f, 0.34f, 0.36f, 0.85f);

        public static readonly Color RampCalm = new(0.34f, 0.78f, 0.92f, 1f);
        public static readonly Color RampWarm = new(0.98f, 0.78f, 0.30f, 1f);
        public static readonly Color RampHot  = new(0.96f, 0.32f, 0.34f, 1f);

        public static Color Toward(float t)
        {
            t = math.saturate(t);
            var a = new float3(RampCalm.r, RampCalm.g, RampCalm.b);
            var b = new float3(RampWarm.r, RampWarm.g, RampWarm.b);
            var c = new float3(RampHot.r, RampHot.g, RampHot.b);
            var m = t < 0.5f ? math.lerp(a, b, t * 2f) : math.lerp(b, c, (t - 0.5f) * 2f);
            return new Color(m.x, m.y, m.z, 1f);
        }

        public static Color Dim(Color c, float alpha) =>
            new(c.r, c.g, c.b, c.a * math.saturate(alpha));

        public static Color Lift(Color c, float brightness)
        {
            var k = math.saturate(brightness);
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }
    }

    public static class Layout
    {
        public const float Title  = 14f;
        public const float Body   = 12f;
        public const float Micro  = 10f;
        public const float SizeK  = 5f;

        public const float Leading    = 1.15f;
        public const float Header     = 1.70f;
        public const float TitleRule  = 0.65f;
        public const float Pad        = 0.55f;

        public const float HaloX      = 0.05f;
        public const float HaloY      = 0.05f;
        public const float StrokeStep = 0.055f;
        public const int   StrokeLayers = 2;

        public const float LabelX   = -2.8f;
        public const float GaugeX0  =  0.2f;
        public const float GaugeX1  =  3.2f;
        public const float ValueX   =  3.9f;
        public const float HalfCard =  5.0f;
    }

    public static class Format
    {
        public static void Compact(ref FixedString128Bytes s, double value)
        {
            if (!math.isfinite((float)value)) { s.Append('-'); s.Append('-'); return; }
            if (value < 0) { s.Append('-'); value = -value; }

            if (value < 1_000) Mantissa(ref s, value);
            else if (value < 1_000_000)    { Mantissa(ref s, value / 1_000d);       s.Append('k'); }
            else if (value < 1_000_000_000){ Mantissa(ref s, value / 1_000_000d);   s.Append('M'); }
            else                           { Mantissa(ref s, value / 1_000_000_000d);s.Append('B'); }
        }

        private static void Mantissa(ref FixedString128Bytes s, double value)
        {
            var whole = (int)value;
            var frac  = (int)math.round((value - whole) * 10);
            if (frac >= 10) { whole++; frac = 0; }
            s.Append(whole);
            if (frac != 0) { s.Append('.'); s.Append(frac); }
        }
    }

    public static class Glyph
    {
        public static void Seg(Drawer d, in View v, float x0, float y0, float x1, float y1, Color c)
            => d.Line(v.At(x0, y0), v.At(x1, y1), c);

        public static void Stroke(Drawer d, in View v, float x0, float y0, float x1, float y1, Color c,
            int layers = Layout.StrokeLayers)
        {
            var dx = x1 - x0; var dy = y1 - y0;
            var len = math.sqrt(dx * dx + dy * dy);
            if (len < 1e-6f) return;
            var px = -dy / len * Layout.StrokeStep;
            var py =  dx / len * Layout.StrokeStep;
            for (var i = 0; i < layers; i++)
            {
                var t = i - (layers - 1) * 0.5f;
                d.Line(v.At(x0 + px * t, y0 + py * t), v.At(x1 + px * t, y1 + py * t), c);
            }
        }

        public static void Text(Drawer d, in View v, float x, float y, in FixedString128Bytes s, Color c, float px)
        {
            d.Text128(v.At(x + Layout.HaloX, y - Layout.HaloY), s, Ink.Shadow, v.Size(px));
            d.Text128(v.At(x, y), s, c, v.Size(px));
        }

        public static void Label(Drawer d, in View v, float x, float y, in FixedString32Bytes s, Color c,
            float px = Layout.Body)
        {
            var wide = new FixedString128Bytes();
            wide.Append(s);
            d.Text128(v.At(x + Layout.HaloX, y - Layout.HaloY), wide, Ink.Shadow, v.Size(px));
            d.Text128(v.At(x, y), wide, c, v.Size(px));
        }

        public static void Title(Drawer d, in View v, in FixedString32Bytes text, float halfWidth, Color accent)
        {
            var wide = new FixedString128Bytes();
            wide.Append(text);
            d.Text128(v.At(Layout.HaloX, -Layout.HaloY), wide, Ink.Shadow, v.Size(Layout.Title));
            d.Text128(v.At(0f, 0f), wide, accent, v.Size(Layout.Title));
            Stroke(d, v, -halfWidth, -Layout.TitleRule, halfWidth, -Layout.TitleRule, Ink.Rule);
        }

        public static void Frame(Drawer d, in View v, float xL, float xR, float yTop, float yBot,
            Color edge, Color accent)
        {
            Seg(d, v, xL, yTop, xR, yTop, edge);
            Seg(d, v, xR, yTop, xR, yBot, edge);
            Seg(d, v, xR, yBot, xL, yBot, edge);
            Seg(d, v, xL, yBot, xL, yTop, edge);
            Stroke(d, v, xL, yTop, xL, yBot, accent, 3);
        }

        public static void Rule(Drawer d, in View v, float x0, float x1, float y, Color c)
            => Stroke(d, v, x0, y, x1, y, c);

        public static void Gauge(Drawer d, in View v, float x0, float x1, float y, float fill)
        {
            var t  = math.saturate(fill);
            var xe = x0 + (x1 - x0) * t;
            Stroke(d, v, x0, y, x1, y, Ink.Track, 2);
            if (t > 1e-4f) Stroke(d, v, x0, y, xe, y, Ink.Toward(t), 3);
            Seg(d, v, xe, y - 0.15f, xe, y + 0.15f, Ink.Value);
            Seg(d, v, x0, y - 0.10f, x0, y + 0.10f, Ink.Muted);
            Seg(d, v, x1, y - 0.10f, x1, y + 0.10f, Ink.Muted);
        }

        public static void Delta(Drawer d, in View v, float x, float y, float sign, Color c)
        {
            if (sign == 0f) return;
            var s = math.sign(sign);
            const float w = 0.20f; const float h = 0.28f;
            float ax = x,         ay = y + s * h * 0.5f;
            float lx = x - w,     ly = y - s * h * 0.5f;
            float rx = x + w,     ry = y - s * h * 0.5f;
            Seg(d, v, lx, ly, ax, ay, c);
            Seg(d, v, ax, ay, rx, ry, c);
        }

        public static void Pulse(Drawer d, in View v, float x, float y, float r, Color c)
        {
            Seg(d, v, x,     y + r, x + r, y,     c);
            Seg(d, v, x + r, y,     x,     y - r, c);
            Seg(d, v, x,     y - r, x - r, y,     c);
            Seg(d, v, x - r, y,     x,     y + r, c);
        }

        public static unsafe void Spark(
            Drawer d, in View v,
            float x0, float x1, float y0, float height,
            float* values, int count, Color c)
        {
            if (count < 2) return;
            var min = float.MaxValue; var max = float.MinValue;
            for (var i = 0; i < count; i++) { min = math.min(min, values[i]); max = math.max(max, values[i]); }
            var span = math.max(1e-5f, max - min);

            var pts   = stackalloc float3[count];
            var pairs = (count - 1) * 2;
            var segs  = stackalloc float3[pairs];

            for (var i = 0; i < count; i++)
            {
                var t = (float)i / (count - 1);
                pts[i] = v.At(x0 + (x1 - x0) * t, y0 + height * ((values[i] - min) / span));
            }

            for (var i = 0; i < count - 1; i++)
            {
                segs[i * 2]     = pts[i];
                segs[i * 2 + 1] = pts[i + 1];
            }

            Stroke(d, v, x0, y0, x1, y0, Ink.Track);

            var lines = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float3>(segs, pairs, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref lines, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            d.Lines(lines, c);
        }
    }
}
#endif