using System;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>向き付きの軸。軸の割り当てで「入力のどの軸を、どちら向きで使うか」を表す。</summary>
    public enum SignedAxis
    {
        PosX = 0,
        NegX = 1,
        PosY = 2,
        NegY = 3,
        PosZ = 4,
        NegZ = 5,
    }

    public static class SignedAxisExtensions
    {
        /// <summary>軸の番号（x=0, y=1, z=2）。</summary>
        public static int Index(this SignedAxis axis) => (int)axis / 2;

        /// <summary>向き（+1 または -1）。</summary>
        public static float Sign(this SignedAxis axis) => (int)axis % 2 == 0 ? 1f : -1f;

        /// <summary>その軸の単位ベクトル。</summary>
        public static Vector3 ToVector(this SignedAxis axis)
        {
            var v = Vector3.zero;
            v[axis.Index()] = axis.Sign();
            return v;
        }
    }

    /// <summary>
    /// 変位の軸の割り当て（計画書の M）。腰トラッカーの座標で測った変位を、アバターの胴体の座標の変位 u に写す。
    /// 出力の各軸（x=右、y=上、z=前）に、入力のどの軸をどちら向きで使うかと、軸ごとの倍率を持つ。
    /// 倍率を0にした軸は使わない（例：上下の動きを無視する）。
    /// </summary>
    [Serializable]
    public struct AxisMapping
    {
        /// <summary>出力のx（右）に使う入力の軸。</summary>
        public SignedAxis x;

        /// <summary>出力のy（上）に使う入力の軸。</summary>
        public SignedAxis y;

        /// <summary>出力のz（前）に使う入力の軸。</summary>
        public SignedAxis z;

        /// <summary>出力の軸ごとの倍率。</summary>
        public Vector3 scale;

        public AxisMapping(SignedAxis x, SignedAxis y, SignedAxis z, Vector3 scale)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.scale = scale;
        }

        public AxisMapping(SignedAxis x, SignedAxis y, SignedAxis z)
            : this(x, y, z, Vector3.one)
        {
        }

        /// <summary>そのまま写す割り当て。</summary>
        public static AxisMapping Identity => new AxisMapping(SignedAxis.PosX, SignedAxis.PosY, SignedAxis.PosZ);

        /// <summary>
        /// 3つの出力軸が、入力の別々の軸を使っているか（符号付きの置換になっているか）。
        /// false のときは同じ入力の軸が2つの出力に写るので、設定の誤りとして扱う。
        /// </summary>
        public bool IsPermutation => x.Index() != y.Index() && y.Index() != z.Index() && x.Index() != z.Index();

        /// <summary>入力の変位を出力の座標に写す。</summary>
        public Vector3 Apply(Vector3 v)
        {
            return new Vector3(
                x.Sign() * v[x.Index()] * scale.x,
                y.Sign() * v[y.Index()] * scale.y,
                z.Sign() * v[z.Index()] * scale.z);
        }

        public override string ToString() => $"x←{x}, y←{y}, z←{z}, scale={scale}";
    }
}
