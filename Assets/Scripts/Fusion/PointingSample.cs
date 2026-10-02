using UnityEngine;

namespace MMI.Fusion
{
    /// <summary>One pointing hit, independent of the ray/gesture system that produced it.</summary>
    public readonly struct PointingSample
    {
        public readonly GameObject HitObject;
        public readonly Vector3 HitPoint;

        public PointingSample(GameObject hitObject, Vector3 hitPoint)
        {
            HitObject = hitObject;
            HitPoint = hitPoint;
        }
    }
}
