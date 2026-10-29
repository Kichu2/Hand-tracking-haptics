using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace HapticsTutorial
{
    public readonly struct ArucoDetection
    {
        public readonly int Id;
        public readonly int Rotation;
        public readonly Vector2[] Corners;
        public readonly float Area;

        public ArucoDetection(int id, int rotation, Vector2[] corners, float area)
        {
            Id = id;
            Rotation = rotation;
            Corners = corners;
            Area = area;
        }
    }

    /// <summary>
    /// Lightweight DICT_4X4_50 detector for Quest camera frames. Good lighting and large markers matter.
    /// </summary>
    public static class ArucoMarkerDetector
    {
        const int TargetWidth = 320;

        static byte[] _gray;
        static byte[] _binary;
        static int[] _labels;
        static readonly List<ArucoDetection> Results = new List<ArucoDetection>(8);
        static readonly int[,] Bits = new int[4, 4];

        public static List<ArucoDetection> Detect(NativeArray<Color32> colors, int width, int height)
        {
            Results.Clear();
            if (!colors.IsCreated || width < 16 || height < 16)
                return Results;

            var scale = Mathf.Max(1, width / TargetWidth);
            var dw = Mathf.Max(16, width / scale);
            var dh = Mathf.Max(16, height / scale);
            EnsureBuffers(dw * dh);

            DownscaleGray(colors, width, height, dw, dh, scale);
            var threshold = Otsu(_gray, dw * dh);
            for (var i = 0; i < dw * dh; i++)
                _binary[i] = (byte)(_gray[i] < threshold ? 1 : 0);

            var componentCount = Label(_binary, dw, dh);
            for (var label = 1; label <= componentCount; label++)
                TryComponent(colors, width, height, dw, dh, scale, label);

            return Results;
        }

        static void TryComponent(NativeArray<Color32> colors, int srcW, int srcH, int dw, int dh, int scale, int label)
        {
            var points = new List<Vector2>(64);
            var area = 0;
            for (var y = 1; y < dh - 1; y++)
            {
                var row = y * dw;
                for (var x = 1; x < dw - 1; x++)
                {
                    if (_labels[row + x] != label)
                        continue;
                    area++;
                    if (IsBorder(dw, dh, x, y, label))
                        points.Add(new Vector2(x, y));
                }
            }

            if (area < 80 || area > dw * dh / 3 || points.Count < 8)
                return;

            var hull = ConvexHull(points);
            if (hull.Count < 4)
                return;

            var quad = ApproximateQuad(hull);
            if (quad == null)
                return;

            OrderCorners(quad);
            for (var i = 0; i < 4; i++)
                quad[i] *= scale;

            if (!ReadBits(colors, srcW, srcH, quad))
                return;

            if (!ArucoDict4x4.TryIdentify(Bits, out var id, out var rotation))
                return;

            CycleCorners(quad, (4 - rotation) & 3);
            Results.Add(new ArucoDetection(id, rotation, quad, area * scale * scale));
        }

        static bool ReadBits(NativeArray<Color32> colors, int width, int height, Vector2[] corners)
        {
            const int modules = 6;
            var samples = new float[modules, modules];
            for (var row = 0; row < modules; row++)
            {
                var v = (row + 0.5f) / modules;
                for (var col = 0; col < modules; col++)
                {
                    var u = (col + 0.5f) / modules;
                    var p = BilinearQuad(corners, u, v);
                    samples[row, col] = SampleLuma(colors, width, height, p);
                }
            }

            float border = 0f, inner = 0f;
            var borderCount = 0;
            var innerCount = 0;
            for (var row = 0; row < modules; row++)
            {
                for (var col = 0; col < modules; col++)
                {
                    var isBorder = row == 0 || col == 0 || row == modules - 1 || col == modules - 1;
                    if (isBorder)
                    {
                        border += samples[row, col];
                        borderCount++;
                    }
                    else
                    {
                        inner += samples[row, col];
                        innerCount++;
                    }
                }
            }

            border /= borderCount;
            inner /= innerCount;
            var split = (border + inner) * 0.5f;
            if (border > inner - 4f)
                return false;

            for (var row = 0; row < 4; row++)
            {
                for (var col = 0; col < 4; col++)
                {
                    var luma = samples[row + 1, col + 1];
                    Bits[row, col] = luma < split ? 1 : 0;
                }
            }

            return true;
        }

        static Vector2 BilinearQuad(Vector2[] c, float u, float v)
        {
            var top = Vector2.Lerp(c[0], c[1], u);
            var bottom = Vector2.Lerp(c[3], c[2], u);
            return Vector2.Lerp(top, bottom, v);
        }

        static float SampleLuma(NativeArray<Color32> colors, int width, int height, Vector2 p)
        {
            var x = Mathf.Clamp(p.x, 0f, width - 1.001f);
            var y = Mathf.Clamp(p.y, 0f, height - 1.001f);
            var x0 = (int)x;
            var y0 = (int)y;
            var x1 = Mathf.Min(x0 + 1, width - 1);
            var y1 = Mathf.Min(y0 + 1, height - 1);
            var tx = x - x0;
            var ty = y - y0;
            var a = Luma(colors[y0 * width + x0]);
            var b = Luma(colors[y0 * width + x1]);
            var c = Luma(colors[y1 * width + x0]);
            var d = Luma(colors[y1 * width + x1]);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static int Luma(Color32 p) => (p.r * 77 + p.g * 150 + p.b * 29) >> 8;

        static void DownscaleGray(NativeArray<Color32> colors, int width, int height, int dw, int dh, int scale)
        {
            for (var y = 0; y < dh; y++)
            {
                var srcY = Mathf.Min(y * scale, height - 1);
                for (var x = 0; x < dw; x++)
                {
                    var srcX = Mathf.Min(x * scale, width - 1);
                    _gray[y * dw + x] = (byte)Luma(colors[srcY * width + srcX]);
                }
            }
        }

        static byte Otsu(byte[] gray, int n)
        {
            var hist = new int[256];
            for (var i = 0; i < n; i++)
                hist[gray[i]]++;

            float sum = 0;
            for (var i = 0; i < 256; i++)
                sum += i * hist[i];

            float sumB = 0;
            var wB = 0;
            float best = 0;
            var threshold = 128;
            for (var i = 0; i < 256; i++)
            {
                wB += hist[i];
                if (wB == 0)
                    continue;
                var wF = n - wB;
                if (wF == 0)
                    break;
                sumB += i * hist[i];
                var mB = sumB / wB;
                var mF = (sum - sumB) / wF;
                var between = wB * (float)wF * (mB - mF) * (mB - mF);
                if (between <= best)
                    continue;
                best = between;
                threshold = i;
            }

            return (byte)threshold;
        }

        static int Label(byte[] binary, int w, int h)
        {
            var parent = new int[w * h + 2];
            var next = 1;
            System.Array.Clear(_labels, 0, w * h);

            int Find(int x)
            {
                while (parent[x] != x)
                    x = parent[x] = parent[parent[x]];
                return x;
            }

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    if (binary[i] == 0)
                        continue;

                    var left = x > 0 ? _labels[i - 1] : 0;
                    var up = y > 0 ? _labels[i - w] : 0;
                    if (left == 0 && up == 0)
                    {
                        parent[next] = next;
                        _labels[i] = next++;
                    }
                    else if (left == 0)
                    {
                        _labels[i] = up;
                    }
                    else if (up == 0)
                    {
                        _labels[i] = left;
                    }
                    else
                    {
                        var a = Find(left);
                        var b = Find(up);
                        if (a != b)
                            parent[a] = b;
                        _labels[i] = b;
                    }
                }
            }

            var remap = new int[next];
            var count = 0;
            for (var i = 1; i < next; i++)
            {
                var root = Find(i);
                if (remap[root] == 0)
                    remap[root] = ++count;
                remap[i] = remap[root];
            }

            for (var i = 0; i < w * h; i++)
            {
                if (_labels[i] != 0)
                    _labels[i] = remap[_labels[i]];
            }

            return count;
        }

        static bool IsBorder(int w, int h, int x, int y, int label)
        {
            return _labels[y * w + x - 1] != label ||
                   _labels[y * w + x + 1] != label ||
                   _labels[(y - 1) * w + x] != label ||
                   _labels[(y + 1) * w + x] != label;
        }

        static List<Vector2> ConvexHull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var lower = new List<Vector2>();
            var upper = new List<Vector2>();
            foreach (var p in pts)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }

            for (var i = pts.Count - 1; i >= 0; i--)
            {
                var p = pts[i];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static Vector2[] ApproximateQuad(List<Vector2> hull)
        {
            if (hull.Count == 4)
                return hull.ToArray();

            for (var eps = 1.5f; eps < 24f; eps += 1.5f)
            {
                var simplified = DouglasPeucker(hull, eps);
                if (simplified.Count == 4)
                    return simplified.ToArray();
            }

            return null;
        }

        static List<Vector2> DouglasPeucker(List<Vector2> pts, float epsilon)
        {
            if (pts.Count < 3)
                return new List<Vector2>(pts);

            var first = pts[0];
            var last = pts[pts.Count - 1];
            var max = -1f;
            var index = 0;
            for (var i = 1; i < pts.Count - 1; i++)
            {
                var d = PerpDist(pts[i], first, last);
                if (d <= max)
                    continue;
                max = d;
                index = i;
            }

            if (max < epsilon)
                return new List<Vector2> { first, last };

            var left = DouglasPeucker(pts.GetRange(0, index + 1), epsilon);
            var right = DouglasPeucker(pts.GetRange(index, pts.Count - index), epsilon);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        static float PerpDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var n = (b - a).magnitude;
            if (n < 1e-4f)
                return Vector2.Distance(p, a);
            return Mathf.Abs((p.x - a.x) * (b.y - a.y) - (p.y - a.y) * (b.x - a.x)) / n;
        }

        static void OrderCorners(Vector2[] c)
        {
            var center = (c[0] + c[1] + c[2] + c[3]) * 0.25f;
            System.Array.Sort(c, (a, b) =>
            {
                var aa = Mathf.Atan2(a.y - center.y, a.x - center.x);
                var bb = Mathf.Atan2(b.y - center.y, b.x - center.x);
                return bb.CompareTo(aa);
            });

            var start = 0;
            var best = float.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var score = c[i].y - c[i].x * 0.15f;
                if (score <= best)
                    continue;
                best = score;
                start = i;
            }

            if (start == 0)
                return;

            var rotated = new Vector2[4];
            for (var i = 0; i < 4; i++)
                rotated[i] = c[(start + i) % 4];
            for (var i = 0; i < 4; i++)
                c[i] = rotated[i];
        }

        static void CycleCorners(Vector2[] c, int rotation)
        {
            if (rotation == 0)
                return;
            var rotated = new Vector2[4];
            for (var i = 0; i < 4; i++)
                rotated[i] = c[(i + rotation) % 4];
            for (var i = 0; i < 4; i++)
                c[i] = rotated[i];
        }

        static void EnsureBuffers(int n)
        {
            if (_gray != null && _gray.Length >= n)
                return;
            _gray = new byte[n];
            _binary = new byte[n];
            _labels = new int[n];
        }
    }
}
