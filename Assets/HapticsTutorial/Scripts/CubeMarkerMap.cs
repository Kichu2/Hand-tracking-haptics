using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// Learns where each ArUco marker sits on the cube, so markers can go on any face, any way up.
    /// Whenever two markers are seen together their relative pose is sampled; a robust average of
    /// those samples gives the layout. The cube's centre, axes and edge length come from the face
    /// planes, assuming each marker is roughly centred on its face. Poses are stored in the frame
    /// of the first marker seen (the reference).
    /// </summary>
    public class CubeMarkerMap
    {
        /// <summary>Markers per cube. Ids passed in are local: marker id minus the cube's first id.</summary>
        public const int MaxMarkers = 6;

        const int MaxSamples = 30;
        const int InliersToLock = 12;
        const float InlierDistance = 0.012f;
        const float MismatchDistance = 0.02f;
        const float MismatchAngle = 12f;
        const int MismatchesToRelearn = 15;

        class Entry
        {
            public readonly List<Pose> Samples = new List<Pose>(MaxSamples);
            public Pose Pose;
            public int Inliers;
            public bool Locked;
            public int Mismatches;
        }

        readonly Entry[] _entries = new Entry[MaxMarkers];
        readonly float _markerSize;
        int _reference = -1;

        public CubeMarkerMap(float markerSize)
        {
            _markerSize = markerSize;
            CubeSize = DefaultCubeSize;
            CubeInMap = new Pose(Vector3.zero, Quaternion.identity);
        }

        /// <summary>Edge length assumed until two faces have been linked.</summary>
        public float DefaultCubeSize => _markerSize * 1.6f;

        public float CubeSize { get; private set; }

        /// <summary>Cube centre and axes in the reference marker's frame.</summary>
        public Pose CubeInMap { get; private set; }

        /// <summary>Set when the layout changes; clear it after saving.</summary>
        public bool Dirty { get; set; }

        public int KnownCount
        {
            get
            {
                var count = 0;
                foreach (var entry in _entries)
                {
                    if (entry != null && entry.Inliers > 0)
                        count++;
                }

                return count;
            }
        }

        public int LockedCount
        {
            get
            {
                var count = 0;
                foreach (var entry in _entries)
                {
                    if (entry != null && entry.Locked)
                        count++;
                }

                return count;
            }
        }

        /// <summary>Marker pose in the cube's frame (origin at the cube centre).</summary>
        public bool TryMarkerInCube(int id, out Pose markerInCube)
        {
            markerInCube = default;
            if (id < 0 || id >= MaxMarkers || _entries[id] == null || _entries[id].Inliers == 0)
                return false;
            markerInCube = Multiply(Inverse(CubeInMap), _entries[id].Pose);
            return true;
        }

        /// <summary>Feed single-marker world poses seen in the same camera frame.</summary>
        public void Observe(IReadOnlyList<int> ids, IReadOnlyList<Pose> worldPoses)
        {
            if (ids.Count == 0)
                return;

            if (_reference < 0)
            {
                _reference = ids[0];
                var entry = new Entry { Pose = new Pose(Vector3.zero, Quaternion.identity), Inliers = InliersToLock, Locked = true };
                _entries[_reference] = entry;
                Dirty = true;
                Rebuild();
            }

            // Anchor on the best-known visible marker; prefer the reference to avoid chaining error.
            var anchor = -1;
            for (var i = 0; i < ids.Count; i++)
            {
                var entry = _entries[ids[i]];
                if (entry == null || entry.Inliers < 3)
                    continue;
                if (anchor < 0 || ids[i] == _reference ||
                    (ids[anchor] != _reference && entry.Inliers > _entries[ids[anchor]].Inliers))
                    anchor = i;
            }

            if (anchor < 0)
                return;

            var anchorId = ids[anchor];
            var mapFromWorld = Multiply(_entries[anchorId].Pose, Inverse(worldPoses[anchor]));
            var changed = false;
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (i == anchor || id == _reference)
                    continue;
                var sample = Multiply(mapFromWorld, worldPoses[i]);
                changed |= AddSample(id, sample, anchorId == _reference);
            }

            if (changed)
            {
                Dirty = true;
                Rebuild();
            }
        }

        bool AddSample(int id, Pose sample, bool fromReference)
        {
            var entry = _entries[id];
            if (entry == null)
            {
                entry = new Entry();
                _entries[id] = entry;
            }

            if (entry.Locked)
            {
                // A marker that was moved stops agreeing with the reference; relearn it.
                if (!fromReference)
                    return false;
                var far = Vector3.Distance(sample.position, entry.Pose.position) > MismatchDistance ||
                          Quaternion.Angle(sample.rotation, entry.Pose.rotation) > MismatchAngle;
                entry.Mismatches = far ? entry.Mismatches + 1 : 0;
                if (entry.Mismatches < MismatchesToRelearn)
                    return false;
                Debug.Log($"[Haptics Tutorial] Marker {id} moved on the cube; relearning its position.");
                entry.Samples.Clear();
                entry.Locked = false;
                entry.Mismatches = 0;
            }

            if (entry.Samples.Count == MaxSamples)
                entry.Samples.RemoveAt(0);
            entry.Samples.Add(sample);
            UpdateEstimate(entry);
            if (entry.Inliers >= InliersToLock)
            {
                entry.Locked = true;
                Debug.Log($"[Haptics Tutorial] Learned marker {id} on the cube ({KnownCount} markers known).");
            }

            return true;
        }

        // Component-wise median position, then the mean of samples near it. Single-marker
        // poses occasionally flip, and this ignores those.
        static void UpdateEstimate(Entry entry)
        {
            var n = entry.Samples.Count;
            var xs = new float[n];
            var ys = new float[n];
            var zs = new float[n];
            for (var i = 0; i < n; i++)
            {
                xs[i] = entry.Samples[i].position.x;
                ys[i] = entry.Samples[i].position.y;
                zs[i] = entry.Samples[i].position.z;
            }

            System.Array.Sort(xs);
            System.Array.Sort(ys);
            System.Array.Sort(zs);
            var median = new Vector3(xs[n / 2], ys[n / 2], zs[n / 2]);

            var position = Vector3.zero;
            var rotation = Vector4.zero;
            var first = default(Quaternion);
            var inliers = 0;
            foreach (var sample in entry.Samples)
            {
                if (Vector3.Distance(sample.position, median) > InlierDistance)
                    continue;
                var q = sample.rotation;
                if (inliers == 0)
                    first = q;
                else if (Dot(first, q) < 0f)
                    q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                position += sample.position;
                rotation += new Vector4(q.x, q.y, q.z, q.w);
                inliers++;
            }

            entry.Inliers = inliers;
            if (inliers == 0)
                return;
            rotation /= rotation.magnitude;
            entry.Pose = new Pose(position / inliers, new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w));
        }

        // Each centred face marker satisfies centre = p + n * size / 2, with n the inward normal
        // (a solved marker's forward points into it). Least squares over all known faces, with a
        // weak prior on size so a single face still gives a sensible cube.
        void Rebuild()
        {
            var points = new List<Vector3>();
            var normals = new List<Vector3>();
            var upHint = Vector3.up;
            for (var id = 0; id < MaxMarkers; id++)
            {
                var entry = _entries[id];
                if (entry == null || entry.Inliers < 3)
                    continue;
                points.Add(entry.Pose.position);
                normals.Add(entry.Pose.rotation * Vector3.forward);
                if (id == _reference)
                    upHint = entry.Pose.rotation * Vector3.up;
            }

            if (points.Count == 0)
                return;

            var meanP = Vector3.zero;
            var meanN = Vector3.zero;
            for (var i = 0; i < points.Count; i++)
            {
                meanP += points[i];
                meanN += normals[i];
            }

            meanP /= points.Count;
            meanN /= points.Count;

            const float prior = 0.02f;
            var numerator = prior * DefaultCubeSize;
            var denominator = prior;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i] - meanP;
                var b = (normals[i] - meanN) * 0.5f;
                numerator -= Vector3.Dot(a, b);
                denominator += Vector3.Dot(b, b);
            }

            var size = Mathf.Clamp(numerator / denominator, _markerSize * 1.05f, 0.5f);
            var centre = meanP + meanN * (size * 0.5f);

            // Cube axes: the reference face normal, then the most perpendicular other face normal.
            var forward = normals[0];
            for (var id = 0; id < MaxMarkers; id++)
            {
                if (id == _reference && _entries[id] != null)
                    forward = _entries[id].Pose.rotation * Vector3.forward;
            }

            var bestDot = 0.3f;
            foreach (var n in normals)
            {
                var d = Mathf.Abs(Vector3.Dot(n, forward));
                if (d >= bestDot)
                    continue;
                bestDot = d;
                upHint = n;
            }

            var up = (upHint - forward * Vector3.Dot(upHint, forward)).normalized;
            CubeSize = size;
            CubeInMap = new Pose(centre, Quaternion.LookRotation(forward, up));
        }

        /// <summary>
        /// Starts from a known layout (marker poses in the cube's frame, by local id) so tracking
        /// works from the first frame. Markers that disagree with it are still relearned.
        /// </summary>
        public void UseLayout(Pose[] markerInCube)
        {
            System.Array.Clear(_entries, 0, MaxMarkers);
            _reference = -1;
            for (var id = 0; id < MaxMarkers && id < markerInCube.Length; id++)
            {
                _entries[id] = new Entry { Pose = markerInCube[id], Inliers = InliersToLock, Locked = true };
                if (_reference < 0)
                    _reference = id;
            }

            Rebuild();
        }

        /// <summary>
        /// Marker poses for docs/markers/rubiks_cube_net_print.pdf folded onto a cube: the cross
        /// net's centre square is the front, with top above it, left/right beside it, and bottom
        /// then back below it. Local ids 0–5 = top, left, front, right, bottom, back.
        /// A marker's forward is its inward normal (see MarkerPoseSolver).
        /// </summary>
        public static Pose[] CrossNetLayout(float edge)
        {
            var h = edge * 0.5f;
            return new[]
            {
                new Pose(new Vector3(0f, h, 0f), Quaternion.LookRotation(Vector3.down, Vector3.forward)),
                new Pose(new Vector3(-h, 0f, 0f), Quaternion.LookRotation(Vector3.right, Vector3.up)),
                new Pose(new Vector3(0f, 0f, -h), Quaternion.LookRotation(Vector3.forward, Vector3.up)),
                new Pose(new Vector3(h, 0f, 0f), Quaternion.LookRotation(Vector3.left, Vector3.up)),
                new Pose(new Vector3(0f, -h, 0f), Quaternion.LookRotation(Vector3.up, Vector3.back)),
                new Pose(new Vector3(0f, 0f, h), Quaternion.LookRotation(Vector3.back, Vector3.down))
            };
        }

        public string Serialize()
        {
            var sb = new StringBuilder("v1;").Append(_reference);
            for (var id = 0; id < MaxMarkers; id++)
            {
                var entry = _entries[id];
                if (entry == null || !entry.Locked)
                    continue;
                var p = entry.Pose.position;
                var q = entry.Pose.rotation;
                sb.Append(';').Append(id);
                foreach (var v in new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w })
                    sb.Append(',').Append(v.ToString("R", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        public bool Deserialize(string data)
        {
            if (string.IsNullOrEmpty(data))
                return false;
            var parts = data.Split(';');
            if (parts.Length < 3 || parts[0] != "v1" || !int.TryParse(parts[1], out var reference) ||
                reference < 0 || reference >= MaxMarkers)
                return false;

            var entries = new Entry[MaxMarkers];
            for (var i = 2; i < parts.Length; i++)
            {
                var f = parts[i].Split(',');
                if (f.Length != 8 || !int.TryParse(f[0], out var id) || id < 0 || id >= MaxMarkers)
                    return false;
                var v = new float[7];
                for (var k = 0; k < 7; k++)
                {
                    if (!float.TryParse(f[k + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k]))
                        return false;
                }

                entries[id] = new Entry
                {
                    Pose = new Pose(new Vector3(v[0], v[1], v[2]), new Quaternion(v[3], v[4], v[5], v[6])),
                    Inliers = InliersToLock,
                    Locked = true
                };
            }

            if (entries[reference] == null)
                return false;
            System.Array.Copy(entries, _entries, MaxMarkers);
            _reference = reference;
            Rebuild();
            return true;
        }

        static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        static Pose Multiply(Pose a, Pose b) =>
            new Pose(a.position + a.rotation * b.position, a.rotation * b.rotation);

        static Pose Inverse(Pose p)
        {
            var inv = Quaternion.Inverse(p.rotation);
            return new Pose(inv * -p.position, inv);
        }
    }
}
