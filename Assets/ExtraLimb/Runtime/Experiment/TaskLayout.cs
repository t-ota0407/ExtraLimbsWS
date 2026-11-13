using System;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// リーチング課題のターゲットの円（Step 5-1、計画書の1.5・決定事項#25）。
    /// 円は体の前に、体の正面を向けて置く。中心と半径は、校正したときの胸の位置と向き・身長・肩幅からの比で決めるので、
    /// 体格が違っても難易度がそろう。半径は身長からの比（本来の腕も追加肢も、長さは身長に比例するため）。生やす位置や入力部位を変えても動かさない（全条件で同じ円）。
    /// 値はパイロットで決める（今は仮の値）。アセットを変えたら、パネルの「円を置き直す」で反映する。
    /// </summary>
    [CreateAssetMenu(menuName = "ExtraLimb/リーチング課題の円", fileName = "ReachLayout")]
    public class TaskLayout : ScriptableObject
    {
        [Header("円の中心（胸から）")]
        [Tooltip("体の前への距離（身長に対する比）")]
        public float forwardRatio = 0.17f;
        [Tooltip("胸から上への高さ（身長に対する比。負なら下）。2026-11-13 に 0.09 から 0.08 へ（触れる点を見た目の指先に直して、脇腹から真上に届かなくなったため）")]
        public float upRatio = 0.08f;
        [Tooltip("体の正中から右への距離（肩幅に対する比。負なら左）。追加肢は左右2本なので正中（0）に置く（2026-11-13。右の1本だけのときは 0.3）")]
        public float rightRatio = 0f;

        [Header("円とターゲット")]
        [Tooltip("円の半径（身長に対する比）。0.26 で身長 180 cm のとき直径およそ 94 cm（2026-11-13 に 0.28 から。どの生やす位置からも、左右どちらかの追加肢の指先が8か所すべてに届くように）")]
        public float radiusRatio = 0.26f;
        [Tooltip("ターゲットの数（円周上に等間隔。1つ目が真上で、参加者から見て時計回り）")]
        [Min(2)] public int targetCount = 8;
        [Tooltip("ターゲット（球）の半径（m）")]
        public float targetRadius = 0.03f;

        [Header("触れたとする距離")]
        [Tooltip("追加肢の指先の太さの半分（m）。見た目の指先からこの距離だけ指の付け根の向きに戻った点を中心に、この半径の球が、ターゲットの球に重なったら触れたとする（球の先端が指先）")]
        public float extraLimbProbeRadius = 0.01f;
        [Tooltip("本来の手の大きさ（m）。手のひらの点がターゲットの表面からこの距離に入ったら触れたとする")]
        public float handProbeRadius = 0.04f;

        /// <summary>胸の姿勢（体の向きにそろえたフレーム）と身長・肩幅（m）から、円を作る。</summary>
        public ReachRing Compute(Pose chest, float height, float shoulderWidth) =>
            ReachRing.Create(chest, height, shoulderWidth, forwardRatio, upRatio, rightRatio, radiusRatio, targetCount);
    }

    /// <summary>置いたターゲットの円（ワールド座標）。</summary>
    [Serializable]
    public struct ReachRing
    {
        public Vector3 center;

        /// <summary>参加者から見た右（水平）。</summary>
        public Vector3 right;

        /// <summary>上（鉛直）。</summary>
        public Vector3 up;

        /// <summary>参加者の前（水平）。円の面はこれに垂直。</summary>
        public Vector3 forward;

        public float radius;
        public Vector3[] targets;

        public int Count => targets?.Length ?? 0;

        /// <summary>
        /// 円を作る。前と右は胸の向きを水平にしたもの、上は鉛直。中心＝胸＋前×(身長×forwardRatio)＋上×(身長×upRatio)＋右×(肩幅×rightRatio)、
        /// 半径＝身長×radiusRatio。ターゲット k（0 始まり）は真上から参加者に見て時計回りに 360°/count ずつ。
        /// </summary>
        public static ReachRing Create(Pose chest, float height, float shoulderWidth, float forwardRatio, float upRatio, float rightRatio, float radiusRatio, int count)
        {
            var up = Vector3.up;
            var forward = Vector3.ProjectOnPlane(chest.rotation * Vector3.forward, up);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(up, forward).normalized;
            var ring = new ReachRing
            {
                center = chest.position + forward * (height * forwardRatio) + up * (height * upRatio) + right * (shoulderWidth * rightRatio),
                right = right,
                up = up,
                forward = forward,
                radius = Mathf.Max(0f, height * radiusRatio),
                targets = new Vector3[Mathf.Max(0, count)],
            };
            for (int k = 0; k < ring.targets.Length; k++)
            {
                float angle = Mathf.PI * 0.5f - k * 2f * Mathf.PI / ring.targets.Length;
                ring.targets[k] = ring.center + ring.radius * (Mathf.Cos(angle) * right + Mathf.Sin(angle) * up);
            }
            return ring;
        }

        /// <summary>ターゲット k の位置の名前（上・右上・右 … 8個のとき。それ以外は番号）。</summary>
        public static string PositionName(int k, int count)
        {
            if (count == 8 && k >= 0 && k < 8) return new[] { "上", "右上", "右", "右下", "下", "左下", "左", "左上" }[k];
            return (k + 1).ToString();
        }
    }
}
