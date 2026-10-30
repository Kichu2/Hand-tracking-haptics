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
    /// Corners are in pixels with y up (row 0 at the bottom), matching Unity viewport space.
    /// </summary>
    public static class ArucoMarkerDetector
    {
        const int TargetWidth = 320;

        static byte[] _gray;
        static byte[] _binary;
        static int[] _labels;
        static readonly List<ArucoDetection> Results = new List<ArucoDetection>(8);
        static readonly int[,] Bits = new int[4, 4];
        static readonly int[,] Transposed = new int[4, 4];

        // A mirrored image only decodes as a transposed marker, so a frame with mirrored
        // matches and no normal ones means rows arrive top-down: flip and remember.
        static bool _topDown;
        static int _mirrorVotes;

        /// <summary>True when the camera buffer's first row is the top of the image.</summary>
        public static bool ImageTopDown => _topDown;

        public static List<ArucoDetection> Detect(NativeArray<Color32> colors, int width, int height)
        {
            DetectOnce(colors, width, height);
            if (Results.Count == 0 && _mirrorVotes > 0)
            {
                _topDown = !_topDown;
                Debug.Log($"[Haptics Tutorial] Markers looked mirrored; reading camera rows {(_topDown ? "top-down" : "bottom-up")}.");
                DetectOnce(colors, width, height);
            }

            return Results;
        }

        static void DetectOnce(NativeArray<Color32> colors, int width, int height)
        {
            Results.Clear();
            _mirrorVotes = 0;
            if (!colors.IsCreated || width < 16 || height < 16)
                return;

            var scale = Mathf.Max(1, width / TargetWidth);
            var dw = Mathf.Max(16, width / scale);
            var dh = Mathf.Max(16, height / scale);
            EnsureBuffers(dw * dh);

            DownscaleGray(colors, width, height, dw, dh, scale);
            var threshold = Otsu(_gray, dw * dh);
            for (var i = 0; i < dw * dh; i++)
                _binary[i] = (byte)(_gray[i] <= threshold ? 1 : 0); // Otsu's threshold is the last dark level

            var componentCount = Label(_binary, dw, dh);
            for (var label = 1; label <= componentCount; label++)
                TryComponent(colors, width, height, dw, dh, scale, label);
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

            RefineCorners(colors, srcW, srcH, quad);
            if (!ReadBits(colors, srcW, srcH, quad))
                return;

            if (!ArucoDict4x4.TryIdentify(Bits, out var id, out var rotation))
            {
                for (var r = 0; r < 4; r++)
                {
                    for (var col = 0; col < 4; col++)
                        Transposed[r, col] = Bits[col, r];
                }

                if (ArucoDict4x4.TryIdentify(Transposed, out _, out _))
                    _mirrorVotes++;
                return;
            }

            CycleCorners(quad, (4 - rotation) & 3);
            Results.Add(new ArucoDetection(id, rotation, quad, area * scale * scale));
        }

        // Samples a 8x8 module grid: the white quiet zone ring, the black border ring, and the 4x4 bits.
        static bool ReadBits(NativeArray<Color32> colors, int width, int height, Vector2[] corners)
        {
            const int modules = 6;
            const int grid = modules + 2;
            var samples = new float[grid, grid];
            if (!SquareToQuad(corners, out var h))
                return false;
            for (var row = 0; row < grid; row++)
            {
                for (var col = 0; col < grid; col++)
                {
                    // Average a small patch around the module centre to resist blur and noise.
                    var sum = 0f;
                    for (var sy = -1; sy <= 1; sy++)
                    {
                        var v = (row - 1 + 0.5f + sy * 0.2f) / modules;
                        for (var sx = -1; sx <= 1; sx++)
                        {
                            var u = (col - 1 + 0.5f + sx * 0.2f) / modules;
                            sum += SampleLuma(colors, width, height, ApplyHomography(h, u, v));
                        }
                    }

                    samples[row, col] = sum / 9f;
                }
            }

            float quiet = 0f, border = 0f;
            int quietCount = 0, borderCount = 0;
            for (var row = 0; row < grid; row++)
            {
                for (var col = 0; col < grid; col++)
                {
                    var ring = Mathf.Min(Mathf.Min(row, col), Mathf.Min(grid - 1 - row, grid - 1 - col));
                    if (ring == 0)
                    {
                        quiet += samples[row, col];
                        quietCount++;
                    }
                    else if (ring == 1)
                    {
                        border += samples[row, col];
                        borderCount++;
                    }
                }
            }

            quiet /= quietCount;
            border /= borderCount;
            if (quiet - border < 25f)
                return false;

            var split = (quiet + border) * 0.5f;
            for (var row = 0; row < grid; row++)
            {
                for (var col = 0; col < grid; col++)
                {
                    var ring = Mathf.Min(Mathf.Min(row, col), Mathf.Min(grid - 1 - row, grid - 1 - col));
                    if (ring == 1 && samples[row, col] > split)
                        return false;
                }
            }

            for (var row = 0; row < 4; row++)
            {
                for (var col = 0; col < 4; col++)
                    Bits[row, col] = samples[row + 2, col + 2] < split ? 1 : 0;
            }

            return true;
        }

        // Snaps each quad edge to the black-border/white-margin transition at full resolution,
        // fits a line per edge, and intersects neighbouring lines for sub-pixel corners.
        static void RefineCorners(NativeArray<Color32> colors, int width, int height, Vector2[] c)
        {
            const int samplesPerEdge = 12;
            var center = (c[0] + c[1] + c[2] + c[3]) * 0.25f;
            var lineP = new Vector2[4];
            var lineD = new Vector2[4];
            var xs = new Vector2[samplesPerEdge];

            for (var e = 0; e < 4; e++)
            {
                var a = c[e];
                var b = c[(e + 1) % 4];
                var edge = b - a;
                var length = edge.magnitude;
                if (length < 8f)
                    return;
                var dir = edge / length;
                var normal = new Vector2(-dir.y, dir.x);
                if (Vector2.Dot(normal, (a + b) * 0.5f - center) < 0f)
                    normal = -normal;

                // Less than half a module, so the search only crosses the outer edge.
                var range = Mathf.Max(2f, length / 6f * 0.45f);
                var found = 0;
                for (var s = 0; s < samplesPerEdge; s++)
                {
                    var t = 0.15f + 0.7f * s / (samplesPerEdge - 1);
                    var p = a + edge * t;
                    if (TryFindEdge(colors, width, height, p, normal, range, out var hit))
                        xs[found++] = hit;
                }

                if (found < samplesPerEdge / 2)
                    return;

                var mean = Vector2.zero;
                for (var i = 0; i < found; i++)
                    mean += xs[i];
                mean /= found;
                float sxx = 0f, sxy = 0f, syy = 0f;
                for (var i = 0; i < found; i++)
                {
                    var d = xs[i] - mean;
                    sxx += d.x * d.x;
                    sxy += d.x * d.y;
                    syy += d.y * d.y;
                }

                var angle = 0.5f * Mathf.Atan2(2f * sxy, sxx - syy);
                lineP[e] = mean;
                lineD[e] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            var refined = new Vector2[4];
            for (var i = 0; i < 4; i++)
            {
                var prev = (i + 3) % 4;
                if (!Intersect(lineP[prev], lineD[prev], lineP[i], lineD[i], out refined[i]))
                    return;
                var limit = Mathf.Max(3f, Vector2.Distance(c[i], c[(i + 1) % 4]) / 6f);
                if (Vector2.Distance(refined[i], c[i]) > limit)
                    return;
            }

            for (var i = 0; i < 4; i++)
                c[i] = refined[i];
        }

        // Walks from inside to outside along the normal and returns the mid-luma crossing.
        static bool TryFindEdge(NativeArray<Color32> colors, int width, int height, Vector2 p, Vector2 normal, float range, out Vector2 hit)
        {
            hit = p;
            const float step = 0.5f;
            var count = Mathf.Max(3, (int)(range * 2f / step) + 1);
            var lo = float.MaxValue;
            var hi = float.MinValue;
            var values = new float[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = SampleLuma(colors, width, height, p + normal * (-range + i * step));
                lo = Mathf.Min(lo, values[i]);
                hi = Mathf.Max(hi, values[i]);
            }

            if (hi - lo < 25f)
                return false;

            var mid = (lo + hi) * 0.5f;
            for (var i = 1; i < count; i++)
            {
                if (values[i - 1] >= mid || values[i] < mid)
                    continue;
                var f = (mid - values[i - 1]) / (values[i] - values[i - 1]);
                hit = p + normal * (-range + (i - 1 + f) * step);
                return true;
            }

            return false;
        }

        static bool Intersect(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2, out Vector2 point)
        {
            point = default;
            var denom = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(denom) < 1e-4f)
                return false;
            var diff = p2 - p1;
            var t = (diff.x * d2.y - diff.y * d2.x) / denom;
            point = p1 + d1 * t;
            return true;
        }

        // Projective map from the unit square (c0 at 0,0, c1 at 1,0, c2 at 1,1, c3 at 0,1) to the quad,
        // so module centres stay correct under perspective. Heckbert's closed form.
        static bool SquareToQuad(Vector2[] c, out float[] h)
        {
            h = null;
            var sx = c[0].x - c[1].x + c[2].x - c[3].x;
            var sy = c[0].y - c[1].y + c[2].y - c[3].y;
            float g, k;
            if (Mathf.Abs(sx) < 1e-4f && Mathf.Abs(sy) < 1e-4f)
            {
                g = 0f;
                k = 0f;
            }
            else
            {
                var dx1 = c[1].x - c[2].x;
                var dx2 = c[3].x - c[2].x;
                var dy1 = c[1].y - c[2].y;
                var dy2 = c[3].y - c[2].y;
                var det = dx1 * dy2 - dx2 * dy1;
                if (Mathf.Abs(det) < 1e-6f)
                    return false;
                g = (sx * dy2 - dx2 * sy) / det;
                k = (dx1 * sy - sx * dy1) / det;
            }

            h = new[]
            {
                c[1].x - c[0].x + g * c[1].x, c[3].x - c[0].x + k * c[3].x, c[0].x,
                c[1].y - c[0].y + g * c[1].y, c[3].y - c[0].y + k * c[3].y, c[0].y,
                g, k
            };
            return true;
        }

        static Vector2 ApplyHomography(float[] h, float u, float v)
        {
            var w = h[6] * u + h[7] * v + 1f;
            return new Vector2((h[0] * u + h[1] * v + h[2]) / w, (h[3] * u + h[4] * v + h[5]) / w);
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
            var a = Luma(colors[Row(y0, height) * width + x0]);
            var b = Luma(colors[Row(y0, height) * width + x1]);
            var c = Luma(colors[Row(y1, height) * width + x0]);
            var d = Luma(colors[Row(y1, height) * width + x1]);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static int Luma(Color32 p) => (p.r * 77 + p.g * 150 + p.b * 29) >> 8;

        static int Row(int y, int height) => _topDown ? height - 1 - y : y;

        static void DownscaleGray(NativeArray<Color32> colors, int width, int height, int dw, int dh, int scale)
        {
            for (var y = 0; y < dh; y++)
            {
                var srcY = Mathf.Min(y * scale, height - 1);
                for (var x = 0; x < dw; x++)
                {
                    var srcX = Mathf.Min(x * scale, width - 1);
                    _gray[y * dw + x] = (byte)Luma(colors[Row(srcY, height) * width + srcX]);
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

        // Corners of a closed convex hull: the two farthest-apart hull points are opposite
        // corners, and the farthest point from that diagonal on each side gives the other two.
        static Vector2[] ApproximateQuad(List<Vector2> hull)
        {
            var n = hull.Count;
            int a = 0, b = 0;
            var best = -1f;
            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    var d = (hull[i] - hull[j]).sqrMagnitude;
                    if (d <= best)
                        continue;
                    best = d;
                    a = i;
                    b = j;
                }
            }

            var c = FarthestFromLine(hull, a, b);
            var d2 = FarthestFromLine(hull, b, a);
            if (c < 0 || d2 < 0)
                return null;

            var quad = new[] { hull[a], hull[c], hull[b], hull[d2] };

            // Reject blobs that are not close to a quadrilateral.
            var perimeter = 0f;
            for (var i = 0; i < 4; i++)
            {
                var side = Vector2.Distance(quad[i], quad[(i + 1) % 4]);
                if (side < 6f)
                    return null;
                perimeter += side;
            }

            var tolerance = Mathf.Max(1.5f, perimeter * 0.02f);
            foreach (var p in hull)
            {
                var nearest = float.MaxValue;
                for (var i = 0; i < 4; i++)
                    nearest = Mathf.Min(nearest, SegmentDist(p, quad[i], quad[(i + 1) % 4]));
                if (nearest > tolerance)
                    return null;
            }

            return quad;
        }

        // Walks the hull from index 'from' to 'to' and returns the point farthest from that line.
        static int FarthestFromLine(List<Vector2> hull, int from, int to)
        {
            var n = hull.Count;
            var index = -1;
            var max = 1f;
            for (var i = (from + 1) % n; i != to; i = (i + 1) % n)
            {
                var d = PerpDist(hull[i], hull[from], hull[to]);
                if (d <= max)
                    continue;
                max = d;
                index = i;
            }

            return index;
        }

        static float SegmentDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
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
