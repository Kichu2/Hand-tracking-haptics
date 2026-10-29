using Meta.XR;
using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// Camera-space PnP for a square marker, then cube pose from a known face id.
    /// Face IDs: 0 +Z, 1 -Z, 2 +X, 3 -X, 4 +Y, 5 -Y.
    /// </summary>
    public static class MarkerPoseSolver
    {
        static readonly Quaternion[] FaceRotation =
        {
            Quaternion.LookRotation(Vector3.forward, Vector3.up),
            Quaternion.LookRotation(Vector3.back, Vector3.up),
            Quaternion.LookRotation(Vector3.right, Vector3.up),
            Quaternion.LookRotation(Vector3.left, Vector3.up),
            Quaternion.LookRotation(Vector3.up, Vector3.back),
            Quaternion.LookRotation(Vector3.down, Vector3.forward)
        };

        public static bool TryCubePose(
            ArucoDetection detection,
            PassthroughCameraAccess camera,
            float markerSize,
            float cubeSize,
            out Pose worldPose)
        {
            worldPose = default;
            if (camera == null || !camera.IsPlaying || detection.Corners == null || detection.Corners.Length != 4)
                return false;

            var half = markerSize * 0.5f;
            var objectPoints = new Vector3[]
            {
                new Vector3(-half, half, 0f),
                new Vector3(half, half, 0f),
                new Vector3(half, -half, 0f),
                new Vector3(-half, -half, 0f)
            };

            var image = new Vector2[4];
            var resolution = camera.CurrentResolution;
            var camPose = camera.GetCameraPose();
            for (var i = 0; i < 4; i++)
            {
                var pixelY = resolution.y - detection.Corners[i].y;
                var viewport = new Vector2(
                    detection.Corners[i].x / resolution.x,
                    pixelY / resolution.y);
                var ray = camera.ViewportPointToRay(viewport, camPose);
                var local = Quaternion.Inverse(camPose.rotation) * ray.direction;
                if (Mathf.Abs(local.z) < 1e-5f)
                    return false;
                image[i] = new Vector2(local.x / local.z, local.y / local.z);
            }

            if (!SolveHomographyPose(objectPoints, image, out var markerLocal))
                return false;

            var markerWorld = new Pose(
                camPose.position + camPose.rotation * markerLocal.position,
                camPose.rotation * markerLocal.rotation);

            worldPose = MarkerToCube(markerWorld, detection.Id, cubeSize);
            return true;
        }

        static Pose MarkerToCube(Pose markerWorld, int faceId, float cubeSize)
        {
            var half = cubeSize * 0.5f;
            faceId = Mathf.Clamp(faceId, 0, FaceRotation.Length - 1);
            var faceLocal = new Pose(Vector3.forward * half, FaceRotation[faceId]);
            var inversePos = Quaternion.Inverse(faceLocal.rotation) * -faceLocal.position;
            var inverseRot = Quaternion.Inverse(faceLocal.rotation);
            return new Pose(
                markerWorld.position + markerWorld.rotation * inversePos,
                markerWorld.rotation * inverseRot);
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
                var r0 = i * 2;
                var r1 = r0 + 1;
                a[r0, 0] = x;
                a[r0, 1] = y;
                a[r0, 2] = 1f;
                a[r0, 6] = -u * x;
                a[r0, 7] = -u * y;
                b[r0] = u;
                a[r1, 3] = x;
                a[r1, 4] = y;
                a[r1, 5] = 1f;
                a[r1, 6] = -v * x;
                a[r1, 7] = -v * y;
                b[r1] = v;
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
