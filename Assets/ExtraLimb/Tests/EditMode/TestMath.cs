using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>テストで使う、ベクトルと回転の比較と乱数の姿勢。</summary>
    static class TestMath
    {
        public const float PositionTolerance = 1e-4f; // 0.1 mm
        public const float AngleToleranceDeg = 0.05f;

        public static void AreClose(Vector3 expected, Vector3 actual, float tolerance = PositionTolerance, string message = null)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(tolerance),
                $"{message} expected {expected.ToString("F5")} but was {actual.ToString("F5")}");
        }

        public static void AreClose(Quaternion expected, Quaternion actual, float toleranceDeg = AngleToleranceDeg, string message = null)
        {
            Assert.That(AngleBetween(expected, actual), Is.LessThan(toleranceDeg),
                $"{message} expected {expected.eulerAngles.ToString("F2")} but was {actual.eulerAngles.ToString("F2")}");
        }

        /// <summary>2つの回転の差の角度（度）。q と -q を同じとみなす。</summary>
        public static float AngleBetween(Quaternion a, Quaternion b)
        {
            float dot = Mathf.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
            return 2f * Mathf.Acos(Mathf.Min(1f, dot)) * Mathf.Rad2Deg;
        }

        public static Quaternion RandomRotation(System.Random random)
        {
            var q = new Quaternion(
                (float)(random.NextDouble() * 2 - 1),
                (float)(random.NextDouble() * 2 - 1),
                (float)(random.NextDouble() * 2 - 1),
                (float)(random.NextDouble() * 2 - 1));
            return Quaternion.Normalize(q);
        }

        public static Vector3 RandomPosition(System.Random random, float range)
        {
            return new Vector3(
                (float)(random.NextDouble() * 2 - 1) * range,
                (float)(random.NextDouble() * 2 - 1) * range,
                (float)(random.NextDouble() * 2 - 1) * range);
        }

        /// <summary>姿勢全体に、ワールドでの回転と平行移動をかける（体ごと向きを変える・歩くことに相当）。</summary>
        public static Pose TransformWorld(Pose pose, Quaternion worldRotation, Vector3 worldTranslation)
        {
            return new Pose(worldRotation * pose.position + worldTranslation, worldRotation * pose.rotation);
        }
    }
}
