#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Quill;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Essence.Debug
{
    public static class Ink
    {
        public static readonly Color Frame = new(1f, 1f, 1f, 0.10f);
        public static readonly Color Rule = new(1f, 1f, 1f, 0.20f);
        public static readonly Color Track = new(1f, 1f, 1f, 0.11f);
        public static readonly Color Label = new(0.72f, 0.77f, 0.84f, 0.85f);
        public static readonly Color Value = new(0.97f, 0.98f, 1f, 1f);
        public static readonly Color Muted = new(0.55f, 0.58f, 0.65f, 0.65f);
        public static readonly Color Shadow = new(0f, 0f, 0f, 0.6f);

        public static readonly Color Live = new(0.36f, 0.92f, 0.55f, 1f);
        public static readonly Color Idle = new(0.62f, 0.34f, 0.36f, 0.85f);

        public static readonly Color RampCalm = new(0.34f, 0.78f, 0.92f, 1f);
        public static readonly Color RampWarm = new(0.98f, 0.78f, 0.30f, 1f);
        public static readonly Color RampHot = new(0.96f, 0.32f, 0.34f, 1f);

        public static Color Toward(float t)
        {
            t = math.saturate(t);
            float3 a = new(RampCalm.r, RampCalm.g, RampCalm.b);
            float3 b = new(RampWarm.r, RampWarm.g, RampWarm.b);
            float3 c = new(RampHot.r, RampHot.g, RampHot.b);
            float3 m = t < 0.5f ? math.lerp(a, b, t * 2f) : math.lerp(b, c, (t - 0.5f) * 2f);
            return new Color(m.x, m.y, m.z, 1f);
        }

        public static Color Dim(Color c, float intensity) => new(c.r, c.g, c.b, c.a * math.saturate(intensity));

        public static Color Lift(Color c, float intensity)
        {
            float k = math.saturate(intensity);
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }
    }

    public static class Layout
    {
        public const float Title = 12f;
        public const float Body = 11f;
        public const float Micro = 9f;

        public const float Row = 0.155f;
        public const float Header = 0.24f;
        public const float Pad = 0.07f;

        public const float LabelWidth = 0.4f;
        public const float GaugeColumn = 0.4f;
        public const float BarWidth = 0.3f;
        public const float ValueColumn = 0.74f;
        public const float CardWidth = 0.98f;

        public const float SparkWidth = 0.28f;
        public const float SparkHeight = 0.09f;

        public const float Halo = 0.012f;
        public const float StrokeStep = 0.014f;
        public const int StrokeLayers = 2;
    }

    public struct Pen
    {
        public float3 Cursor;
        public readonly float3 Top;
        private readonly float leading;

        public Pen(float3 top, float headerDrop, float leading)
        {
            this.Top = top;
            this.leading = leading;
            this.Cursor = new float3(top.x, top.y - headerDrop, top.z);
        }

        public float3 Take()
        {
            var here = this.Cursor;
            this.Cursor.y -= this.leading;
            return here;
        }
    }

    public static class Format
    {
        public static void Compact(ref FixedString128Bytes s, double value)
        {
            if (!math.isfinite((float)value))
            {
                s.Append('-');
                s.Append('-');
                return;
            }

            if (value < 0)
            {
                s.Append('-');
                value = -value;
            }

            if (value < 1_000) Mantissa(ref s, value);
            else if (value < 1_000_000) { Mantissa(ref s, value / 1_000); s.Append('k'); }
            else if (value < 1_000_000_000) { Mantissa(ref s, value / 1_000_000); s.Append('M'); }
            else { Mantissa(ref s, value / 1_000_000_000); s.Append('B'); }
        }

        private static void Mantissa(ref FixedString128Bytes s, double value)
        {
            var whole = (int)value;
            var frac = (int)math.round((value - whole) * 10);
            if (frac >= 10)
            {
                whole += 1;
                frac = 0;
            }

            s.Append(whole);
            if (frac != 0)
            {
                s.Append('.');
                s.Append(frac);
            }
        }
    }

    public static class Glyph
    {
        public static void Title(Drawer d, float3 at, in FixedString32Bytes text, Color accent)
        {
            d.Text32(at, text, accent, Layout.Title);
            var a = new float3(at.x, at.y - 0.085f, at.z);
            var b = new float3(at.x + Layout.CardWidth - Layout.Pad, a.y, at.z);
            d.Line(a, b, Ink.Rule);
        }

        public static void Readout(Drawer d, float3 at, in FixedString128Bytes text, Color color, float size)
        {
            var shadow = new float3(at.x + Layout.Halo, at.y - Layout.Halo, at.z);
            d.Text128(shadow, text, Ink.Shadow, size);
            d.Text128(at, text, color, size);
        }

        public static void Label(Drawer d, float3 at, in FixedString32Bytes text, Color color)
        {
            d.Text32(at, text, color, Layout.Body);
        }

        public static void Stroke(Drawer d, float3 a, float3 b, Color color, int layers, float step)
        {
            for (var i = 0; i < layers; i++)
            {
                var off = (i - (layers - 1) * 0.5f) * step;
                d.Line(new float3(a.x, a.y + off, a.z), new float3(b.x, b.y + off, b.z), color);
            }
        }

        public static void Notch(Drawer d, float3 at, float height, Color color)
        {
            d.Line(new float3(at.x, at.y + height * 0.5f, at.z), new float3(at.x, at.y - height * 0.5f, at.z), color);
        }

        public static void Gauge(Drawer d, float3 left, float width, float fill)
        {
            var t = math.saturate(fill);
            var right = new float3(left.x + width, left.y, left.z);
            var end = new float3(left.x + width * t, left.y, left.z);

            Stroke(d, left, right, Ink.Track, Layout.StrokeLayers, Layout.StrokeStep);
            Stroke(d, left, end, Ink.Toward(t), Layout.StrokeLayers + 1, Layout.StrokeStep);
            Notch(d, end, 0.05f, Ink.Value);
            Notch(d, left, 0.035f, Ink.Muted);
            Notch(d, right, 0.035f, Ink.Muted);
        }

        public static void Frame(Drawer d, float3 top, float width, float height, Color edge, Color accent)
        {
            var tl = new float3(top.x - Layout.Pad, top.y + Layout.Pad, top.z);
            var tr = new float3(tl.x + width, tl.y, top.z);
            var bl = new float3(tl.x, tl.y - height, top.z);
            var br = new float3(tr.x, bl.y, top.z);

            d.Line(tl, tr, edge);
            d.Line(tr, br, edge);
            d.Line(br, bl, edge);
            d.Line(bl, tl, edge);
            Stroke(d, tl, bl, accent, 3, 0.012f);
        }

        public static void Delta(Drawer d, float3 at, float sign, Color color)
        {
            if (sign == 0f) return;
            var s = math.sign(sign);
            const float w = 0.03f;
            const float h = 0.05f;
            var apex = new float3(at.x, at.y + s * h * 0.5f, at.z);
            var l = new float3(at.x - w, at.y - s * h * 0.5f, at.z);
            var r = new float3(at.x + w, at.y - s * h * 0.5f, at.z);
            d.Line(l, apex, color);
            d.Line(apex, r, color);
        }

        public static void Pulse(Drawer d, float3 center, float radius, Color color)
        {
            var n = new float3(center.x, center.y + radius, center.z);
            var s = new float3(center.x, center.y - radius, center.z);
            var e = new float3(center.x + radius, center.y, center.z);
            var w = new float3(center.x - radius, center.y, center.z);
            d.Line(n, e, color);
            d.Line(e, s, color);
            d.Line(s, w, color);
            d.Line(w, n, color);
        }

        public static unsafe void Strip(Drawer d, float3* ordered, int count, Color color)
        {
            if (count < 2) return;
            var pairs = (count - 1) * 2;
            var data = stackalloc float3[pairs];
            var k = 0;
            for (var i = 1; i < count; i++)
            {
                data[k++] = ordered[i - 1];
                data[k++] = ordered[i];
            }

            var lines = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float3>(data, pairs, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref lines, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            d.Lines(lines, color);
        }

        public static unsafe void Spark(Drawer d, float3 left, float width, float height, float* values, int count, Color color)
        {
            if (count < 2) return;

            var min = float.MaxValue;
            var max = float.MinValue;
            for (var i = 0; i < count; i++)
            {
                min = math.min(min, values[i]);
                max = math.max(max, values[i]);
            }

            var span = math.max(1e-5f, max - min);
            var points = stackalloc float3[count];
            for (var i = 0; i < count; i++)
            {
                var x = left.x + width * (i / (float)(count - 1));
                var y = left.y + height * ((values[i] - min) / span);
                points[i] = new float3(x, y, left.z);
            }

            d.Line(left, new float3(left.x + width, left.y, left.z), Ink.Track);
            Strip(d, points, count, color);
        }
    }
}
#endif
