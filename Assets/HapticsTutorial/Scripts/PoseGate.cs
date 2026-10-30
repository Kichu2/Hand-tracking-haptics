using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// Holds back a sudden jump that only one marker supports until the next measurement agrees
    /// with it, so a single misread frame does not yank the virtual cube. Poses seen through two
    /// or more markers, or after the cube was out of view, are accepted straight away.
    /// </summary>
    public class PoseGate
    {
        const float JumpDistance = 0.04f;
        const float JumpAngle = 25f;
        const float AgreeDistance = 0.02f;
        const float AgreeAngle = 12f;
        const float StaleSeconds = 0.5f;

        Pose _accepted;
        bool _hasAccepted;
        float _acceptedTime;
        Pose _pending;
        bool _hasPending;

        public bool Accept(Pose pose, int markerCount, float time)
        {
            var trusted = !_hasAccepted || markerCount >= 2 || time - _acceptedTime > StaleSeconds ||
                          !Differs(pose, _accepted, JumpDistance, JumpAngle);
            if (!trusted && !(_hasPending && !Differs(pose, _pending, AgreeDistance, AgreeAngle)))
            {
                _pending = pose;
                _hasPending = true;
                return false;
            }

            _accepted = pose;
            _hasAccepted = true;
            _acceptedTime = time;
            _hasPending = false;
            return true;
        }

        static bool Differs(Pose a, Pose b, float distance, float angle) =>
            Vector3.Distance(a.position, b.position) > distance || Quaternion.Angle(a.rotation, b.rotation) > angle;
    }
}
