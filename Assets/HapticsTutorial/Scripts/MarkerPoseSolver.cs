using System.Collections.Generic;
using Meta.XR;
using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>Marker pose in a rigid body's frame, or false if that marker is not part of it.</summary>
    public delegate bool MarkerLayout(int markerId, out Pose markerInBody);

    /// <summary>
    /// Camera-space PnP for square markers. A solved marker frame has right/up as printed and
    /// forward pointing into the marker (Unity is left-handed).
    /// </summary>
    public static class MarkerPoseSolver
    {
        /// <summary>
        /// Single marker pose from its four corners, refined by least squares. Pass the camera pose
        /// captured with the image when the image was read back asynchronously.
        /// </summary>
        public static bool TryMarkerPose(
            ArucoDetection detection,
            PassthroughCameraAccess camera,
            float markerSize,
            out Pose markerWorld,
            Pose? cameraPose = null)
        {
            markerWorld = default;
            if (camera == null || !camera.IsPlaying || detection.Corners == null || detection.Corners.Length != 4)
                return false;

            var camPose = cameraPose ?? camera.GetCameraPose();
            var invCam = Quaternion.Inverse(camPose.rotation);
            var model = new List<Vector3>(4);
            var image = new List<Vector2>(4);
            for (var k = 0; k < 4; k++)
            {
                if (!TryNormalized(camera, camPose, invCam, detection.Corners[k], out var point))
                    return false;
                image.Add(point);
                model.Add(MarkerCorner(k, markerSize));
            }

            if (!SolveHomographyPose(model.ToArray(), image.ToArray(), out var local))
                return false;
            Refine(model, image, ref local);
            markerWorld = new Pose(
                camPose.position + camPose.rotation * local.position,
                camPose.rotation * local.rotation);
            return true;
        }

        /// <summary>
        /// Rigid body pose from every visible marker in the layout at once. Each single-marker pose,
        /// its mirror-ambiguous twin, and the previous body pose seed a Levenberg-Marquardt fit of all
        /// corners' reprojection error. A marker seen at an angle fits two poses almost equally well,
        /// so among near-ties the one closest to the previous pose wins; otherwise the best fit does.
        /// </summary>
        public static bool TryBodyPose(
            IReadOnlyList<ArucoDetection> detections,
            IReadOnlyList<Pose> markerWorldPoses,
            PassthroughCameraAccess camera,
            float markerSize,
            MarkerLayout layout,
            out Pose bodyWorld,
            Pose? cameraPose = null,
            Pose? previousBody = null)
        {
            bodyWorld = default;
            if (camera == null || !camera.IsPlaying || detections == null || detections.Count == 0)
                return false;

            var camPose = cameraPose ?? camera.GetCameraPose();
            var invCam = Quaternion.Inverse(camPose.rotation);
            var model = new List<Vector3>(detections.Count * 4);
            var image = new List<Vector2>(detections.Count * 4);
            foreach (var detection in detections)
            {
                if (detection.Corners == null || detection.Corners.Length != 4 ||
                    !layout(detection.Id, out var markerInBody))
                    continue;
                for (var k = 0; k < 4; k++)
                {
                    if (!TryNormalized(camera, camPose, invCam, detection.Corners[k], out var point))
                        return false;
                    image.Add(point);
                    model.Add(markerInBody.position + markerInBody.rotation * MarkerCorner(k, markerSize));
                }
            }

            if (model.Count < 4)
                return false;

            var seeds = new List<Pose>(detections.Count * 2 + 1);
            for (var i = 0; i < detections.Count; i++)
            {
                if (!layout(detections[i].Id, out var markerInBody))
                    continue;
                var marker = markerWorldPoses[i];
                seeds.Add(BodyFromMarker(marker, markerInBody));
                seeds.Add(BodyFromMarker(MirrorAmbiguity(marker, camPose.position), markerInBody));
            }

            if (previousBody.HasValue)
                seeds.Add(previousBody.Value);

            var poses = new List<Pose>(seeds.Count);
            var errors = new List<double>(seeds.Count);
            var bestError = double.MaxValue;
            foreach (var seedWorld in seeds)
            {
                var local = new Pose(
                    invCam * (seedWorld.position - camPose.position),
                    invCam * seedWorld.rotation);
                var error = Refine(model, image, ref local);
                poses.Add(local);
                errors.Add(error);
                bestError = System.Math.Min(bestError, error);
            }

            if (bestError == double.MaxValue)
                return false;

            // Near-ties: within twice the best error plus a ~half-pixel noise floor per corner.
            var tie = bestError * 2.0 + 1e-6 * model.Count;
            var best = default(Pose);
            var bestScore = double.MaxValue;
            for (var i = 0; i < poses.Count; i++)
            {
                if (errors[i] > tie)
                    continue;
                double score = errors[i];
                if (previousBody.HasValue)
                {
                    var rotation = camPose.rotation * poses[i].rotation;
                    var position = camPose.position + camPose.rotation * poses[i].position;
                    score = Quaternion.Angle(rotation, previousBody.Value.rotation) +
                            1000.0 * Vector3.Distance(position, previousBody.Value.position);
                }

                if (score >= bestScore)
                    continue;
                bestScore = score;
                best = poses[i];
            }

            bodyWorld = new Pose(
                camPose.position + camPose.rotation * best.position,
                camPose.rotation * best.rotation);
            return true;
        }

        // body = marker * inverse(markerInBody)
        static Pose BodyFromMarker(Pose marker, Pose markerInBody)
        {
            var inv = Quaternion.Inverse(markerInBody.rotation);
            return new Pose(marker.position + marker.rotation * (inv * -markerInBody.position), marker.rotation * inv);
        }

        // The other pose a square seen at an angle fits almost as well: its normal mirrored
        // across the line of sight, same centre.
        static Pose MirrorAmbiguity(Pose marker, Vector3 cameraPosition)
        {
            var view = (marker.position - cameraPosition).normalized;
            var normal = marker.rotation * Vector3.back;
            var mirrored = 2f * Vector3.Dot(normal, view) * view - normal;
            return new Pose(marker.position, Quaternion.FromToRotation(normal, mirrored) * marker.rotation);
        }

        // Detector corners are pixel centres with y up, like viewport space.
        static bool TryNormalized(PassthroughCameraAccess camera, Pose camPose, Quaternion invCam, Vector2 corner, out Vector2 point)
        {
            var resolution = camera.CurrentResolution;
            var viewport = new Vector2((corner.x + 0.5f) / resolution.x, (corner.y + 0.5f) / resolution.y);
            var local = invCam * camera.ViewportPointToRay(viewport, camPose).direction;
            point = default;
            if (local.z < 1e-5f)
                return false;
            point = new Vector2(local.x / local.z, local.y / local.z);
            return true;
        }

        static Vector3 MarkerCorner(int corner, float markerSize)
        {
            var h = markerSize * 0.5f;
            return corner switch
            {
                0 => new Vector3(-h, h, 0f),
                1 => new Vector3(h, h, 0f),
                2 => new Vector3(h, -h, 0f),
                _ => new Vector3(-h, -h, 0f)
            };
        }

        // Levenberg-Marquardt on (rotation vector, translation) of the body in camera space.
        // Returns the summed squared reprojection error in normalized image units.
        static double Refine(List<Vector3> model, List<Vector2> image, ref Pose pose)
        {
            const int iterations = 15;
            const float eps = 1e-4f;
            var n = model.Count * 2;
            var r = new double[n];
            var rTry = new double[n];
            var jac = new double[n, 6];
            var lambda = 1e-3;
            var error = Residuals(model, image, pose, r);

            for (var iter = 0; iter < iterations; iter++)
            {
                for (var j = 0; j < 6; j++)
                {
                    Residuals(model, image, Perturb(pose, j, eps), rTry);
                    for (var i = 0; i < n; i++)
                        jac[i, j] = (rTry[i] - r[i]) / eps;
                }

                var jtj = new double[6, 6];
                var jtr = new double[6];
                for (var a = 0; a < 6; a++)
                {
                    for (var i = 0; i < n; i++)
                        jtr[a] += jac[i, a] * r[i];
                    for (var b = a; b < 6; b++)
                    {
                        var sum = 0.0;
                        for (var i = 0; i < n; i++)
                            sum += jac[i, a] * jac[i, b];
                        jtj[a, b] = sum;
                        jtj[b, a] = sum;
                    }
                }

                var improved = false;
                for (var attempt = 0; attempt < 6 && !improved; attempt++)
                {
                    var m = new double[6, 6];
                    for (var a = 0; a < 6; a++)
                    {
                        for (var b = 0; b < 6; b++)
                            m[a, b] = jtj[a, b];
                        m[a, a] += lambda * (jtj[a, a] + 1e-9);
                    }

                    if (!SolveSmall(m, jtr, out var step))
                        break;

                    var candidate = Apply(pose, step);
                    var candidateError = Residuals(model, image, candidate, rTry);
                    if (candidateError < error)
                    {
                        improved = true;
                        pose = candidate;
                        error = candidateError;
                        System.Array.Copy(rTry, r, n);
                        lambda = System.Math.Max(lambda / 3.0, 1e-7);
                    }
                    else
                    {
                        lambda *= 4.0;
                    }
                }

                if (!improved)
                    break;
            }

            return error;
        }

        static double Residuals(List<Vector3> model, List<Vector2> image, Pose pose, double[] r)
        {
            var error = 0.0;
            for (var i = 0; i < model.Count; i++)
            {
                var p = pose.position + pose.rotation * model[i];
                if (p.z < 1e-3f)
                {
                    r[2 * i] = 1.0;
                    r[2 * i + 1] = 1.0;
                }
                else
                {
                    r[2 * i] = p.x / p.z - image[i].x;
                    r[2 * i + 1] = p.y / p.z - image[i].y;
                }

                error += r[2 * i] * r[2 * i] + r[2 * i + 1] * r[2 * i + 1];
            }

            return error;
        }

        static Pose Perturb(Pose pose, int index, float eps)
        {
            var step = new double[6];
            step[index] = -eps;
            return Apply(pose, step);
        }

        // Applies minus 'step': the first three entries are a rotation vector (radians), the rest translation.
        static Pose Apply(Pose pose, double[] step)
        {
            var w = new Vector3((float)-step[0], (float)-step[1], (float)-step[2]);
            var angle = w.magnitude;
            var rotation = angle > 1e-9f
                ? Quaternion.AngleAxis(angle * Mathf.Rad2Deg, w / angle) * pose.rotation
                : pose.rotation;
            var position = pose.position - new Vector3((float)step[3], (float)step[4], (float)step[5]);
            return new Pose(position, Quaternion.Normalize(rotation));
        }

        static bool SolveSmall(double[,] a, double[] b, out double[] x)
        {
            var n = b.Length;
            x = new double[n];
            var m = new double[n, n + 1];
            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < n; j++)
                    m[i, j] = a[i, j];
                m[i, n] = b[i];
            }

            for (var col = 0; col < n; col++)
            {
                var pivot = col;
                for (var row = col + 1; row < n; row++)
                {
                    if (System.Math.Abs(m[row, col]) > System.Math.Abs(m[pivot, col]))
                        pivot = row;
                }

                if (System.Math.Abs(m[pivot, col]) < 1e-15)
                    return false;
                for (var j = col; j <= n; j++)
                    (m[col, j], m[pivot, j]) = (m[pivot, j], m[col, j]);
                for (var row = 0; row < n; row++)
                {
                    if (row == col)
                        continue;
                    var f = m[row, col] / m[col, col];
                    for (var j = col; j <= n; j++)
                        m[row, j] -= f * m[col, j];
                }
            }

            for (var i = 0; i < n; i++)
                x[i] = m[i, n] / m[i, i];
            return true;
        }

        static bool SolveHomographyPose(Vector3[] obj, Vector2[] img, out Pose pose)
        {
            pose = default;
            var a = new float[8, 8];
            var b = new float[8];
            for (var i = 0; i < 4; i++)
            {
                var x = obj[i].x;
                var y = obj[i].y;
                var u = img[i].x;
                var v = img[i].y;
                var row0 = i * 2;
                var row1 = row0 + 1;
                a[row0, 0] = x;
                a[row0, 1] = y;
                a[row0, 2] = 1f;
                a[row0, 6] = -u * x;
                a[row0, 7] = -u * y;
                b[row0] = u;
                a[row1, 3] = x;
                a[row1, 4] = y;
                a[row1, 5] = 1f;
                a[row1, 6] = -v * x;
                a[row1, 7] = -v * y;
                b[row1] = v;
            }

            if (!SolveLinear(a, b, out var h))
                return false;

            var r1 = new Vector3(h[0], h[3], h[6]);
            var r2 = new Vector3(h[1], h[4], h[7]);
            var t = new Vector3(h[2], h[5], 1f);
            var n = (r1.magnitude + r2.magnitude) * 0.5f;
            if (n < 1e-6f)
                return false;
            r1 /= n;
            r2 /= n;
            t /= n;
            var r3 = Vector3.Cross(r1, r2);
            r1.Normalize();
            r3.Normalize();
            r2 = Vector3.Cross(r3, r1);
            if (Vector3.Dot(Vector3.Cross(r1, r2), r3) < 0f)
                r3 = -r3;
            if (t.z < 0f)
            {
                r1 = -r1;
                r2 = -r2;
                t = -t;
            }

            var rot = Quaternion.LookRotation(r3, r2);
            pose = new Pose(t, rot);
            return t.magnitude > 0.05f && t.magnitude < 3f;
        }

        static bool SolveLinear(float[,] a, float[] b, out float[] x)
        {
            const int n = 8;
            x = new float[n];
            var m = new float[n, n + 1];
            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < n; j++)
                    m[i, j] = a[i, j];
                m[i, n] = b[i];
            }

            for (var col = 0; col < n; col++)
            {
                var pivot = col;
                var best = Mathf.Abs(m[col, col]);
                for (var row = col + 1; row < n; row++)
                {
                    var v = Mathf.Abs(m[row, col]);
                    if (v <= best)
                        continue;
                    best = v;
                    pivot = row;
                }

                if (best < 1e-8f)
                    return false;

                if (pivot != col)
                {
                    for (var j = col; j <= n; j++)
                    {
                        (m[col, j], m[pivot, j]) = (m[pivot, j], m[col, j]);
                    }
                }

                var diag = m[col, col];
                for (var j = col; j <= n; j++)
                    m[col, j] /= diag;

                for (var row = 0; row < n; row++)
                {
                    if (row == col)
                        continue;
                    var f = m[row, col];
                    for (var j = col; j <= n; j++)
                        m[row, j] -= f * m[col, j];
                }
            }

            for (var i = 0; i < n; i++)
                x[i] = m[i, n];
            return true;
        }
    }
}
