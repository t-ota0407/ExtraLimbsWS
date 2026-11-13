using System;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>向き付きの軸。把持トリガーで「入力トラッカーのどの局所軸のひねりを見るか」などに使う。</summary>
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
    /// 入力の部位の動きの種類（体の軸で測る）。マッピングの型で、手先の各軸にどれを使うかを選ぶ。
    /// 並進は Re-center 時からの移動（m）、回転は Re-center 時からの傾き・ひねり（rad）。
    /// 回転の向きは、トラッカーの付け方によらず体の軸で決まる。
    /// </summary>
    public enum InputChannel
    {
        /// <summary>使わない。</summary>
        None = 0,

        /// <summary>右へ動かす。</summary>
        MoveRight = 1,

        /// <summary>上へ動かす。</summary>
        MoveUp = 2,

        /// <summary>前へ動かす。</summary>
        MoveForward = 3,

        /// <summary>前へ傾ける（上の端が前へ倒れる向き。体の右の軸まわり）。</summary>
        TiltForward = 4,

        /// <summary>右へ傾ける（上の端が右へ倒れる向き。体の前の軸まわり）。</summary>
        TiltRight = 5,

        /// <summary>右へひねる（上から見て、前の向きが右へ回る向き。体の上の軸まわり）。</summary>
        TwistRight = 6,
    }

    public static class InputChannelInfo
    {
        /// <summary>並ぶ順（表示用）。</summary>
        public static readonly InputChannel[] All =
        {
            InputChannel.MoveRight, InputChannel.MoveUp, InputChannel.MoveForward,
            InputChannel.TiltForward, InputChannel.TiltRight, InputChannel.TwistRight,
        };

        public static bool IsRotation(this InputChannel channel) => channel >= InputChannel.TiltForward;

        /// <summary>
        /// 体を左右に鏡に写したとき（右と左を入れ替える）に、値の向きが逆になるか。
        /// 右へ動かす・右へ傾ける・右へひねるは逆になり、上・前へ動かす・前へ傾けるは変わらない。
        /// </summary>
        public static bool FlipsWhenMirrored(this InputChannel channel) =>
            channel == InputChannel.MoveRight || channel == InputChannel.TiltRight || channel == InputChannel.TwistRight;

        /// <summary>動かし方の言葉。sign が負なら逆向きの言葉（例：右へ動かす → 左へ動かす）。</summary>
        public static string Describe(InputChannel channel, float sign = 1f)
        {
            bool positive = sign >= 0f;
            switch (channel)
            {
                case InputChannel.MoveRight: return positive ? "右へ動かす" : "左へ動かす";
                case InputChannel.MoveUp: return positive ? "上へ動かす" : "下へ動かす";
                case InputChannel.MoveForward: return positive ? "前へ動かす" : "後ろへ動かす";
                case InputChannel.TiltForward: return positive ? "前へ傾ける" : "後ろへ傾ける";
                case InputChannel.TiltRight: return positive ? "右へ傾ける" : "左へ傾ける";
                case InputChannel.TwistRight: return positive ? "右へひねる" : "左へひねる";
                default: return "使わない";
            }
        }
    }

    /// <summary>
    /// 入力の6つの値：体の軸で表した並進（m）と回転（回転ベクトル、rad）。
    /// 回転ベクトルの各成分は、体の右・上・前の軸まわりの回転角（Unity の左手系で、その軸の正の向きに対する回転）。
    /// </summary>
    public readonly struct InputChannels
    {
        /// <summary>並進（m）。x=右、y=上、z=前。</summary>
        public readonly Vector3 translation;

        /// <summary>回転ベクトル（軸×角度、rad）。体の軸で表す。</summary>
        public readonly Vector3 rotation;

        public InputChannels(Vector3 translation, Vector3 rotation)
        {
            this.translation = translation;
            this.rotation = rotation;
        }

        public static InputChannels Zero => new InputChannels(Vector3.zero, Vector3.zero);

        /// <summary>そのチャネルの値。並進は m、回転は rad。</summary>
        public float Raw(InputChannel channel)
        {
            switch (channel)
            {
                case InputChannel.MoveRight: return translation.x;
                case InputChannel.MoveUp: return translation.y;
                case InputChannel.MoveForward: return translation.z;
                // 右の軸まわりの正の回転で、上の向きが前へ倒れる
                case InputChannel.TiltForward: return rotation.x;
                // 前の軸まわりの正の回転で、上の向きは左へ倒れる
                case InputChannel.TiltRight: return -rotation.z;
                // 上の軸まわりの正の回転で、前の向きが右へ回る
                case InputChannel.TwistRight: return rotation.y;
                default: return 0f;
            }
        }

        /// <summary>そのチャネルの値（m）。回転はてこの長さを掛けて長さに直す。</summary>
        public float Value(InputChannel channel, float leverLength) => channel.IsRotation() ? Raw(channel) * leverLength : Raw(channel);

        /// <summary>回転の回転ベクトル（軸×角度、rad）。角度は −π〜π にそろえる。</summary>
        public static Vector3 RotationVector(Quaternion q)
        {
            q = Quaternion.Normalize(q);
            if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            var v = new Vector3(q.x, q.y, q.z);
            float s = v.magnitude;
            if (s < 1e-7f) return v * 2f;
            float angle = 2f * Mathf.Atan2(s, q.w);
            return v * (angle / s);
        }
    }

    /// <summary>手先の1つの軸に使う入力のチャネルと、倍率（符号つき。−1 で反転、0 で使わない）。</summary>
    [Serializable]
    public struct ChannelAssignment
    {
        public InputChannel channel;
        public float scale;

        public ChannelAssignment(InputChannel channel, float scale = 1f)
        {
            this.channel = channel;
            this.scale = scale;
        }

        /// <summary>この軸を動かすか。</summary>
        public bool IsUsed => channel != InputChannel.None && scale != 0f;

        public override string ToString()
        {
            if (!IsUsed) return "None";
            string magnitude = Mathf.Abs(scale) != 1f ? $"×{Mathf.Abs(scale):0.##}" : "";
            return $"{(scale < 0f ? "-" : "")}{channel}{magnitude}";
        }
    }

    /// <summary>
    /// マッピングの型（計画書の決定事項#21）。手先の3軸（x=右、y=上、z=前）それぞれに、入力の6つの値のうち1つと倍率を割り当てる。
    /// 回転はてこの長さ（m）を掛けて長さに直すので、並進と同じ位置ゲインを使える（例：0.3 m なら 10° で約5 cm）。
    /// 出力 u は体の軸で表した長さ（m）で、位置制御で p_t = p_b + G_p u になる。
    /// </summary>
    [Serializable]
    public struct AxisMapping
    {
        /// <summary>回転を長さに直すときの、てこの長さの既定値（m）。</summary>
        public const float DefaultLeverLength = 0.3f;

        /// <summary>手先の右（x）に使う入力。</summary>
        public ChannelAssignment right;

        /// <summary>手先の上（y）に使う入力。</summary>
        public ChannelAssignment up;

        /// <summary>手先の前（z）に使う入力。</summary>
        public ChannelAssignment forward;

        /// <summary>回転を長さに直すてこの長さ（m）。</summary>
        public float leverLength;

        public AxisMapping(ChannelAssignment right, ChannelAssignment up, ChannelAssignment forward, float leverLength = DefaultLeverLength)
        {
            this.right = right;
            this.up = up;
            this.forward = forward;
            this.leverLength = leverLength;
        }

        /// <summary>部位を動かした向きに手先が動く（並進そのまま）。</summary>
        public static AxisMapping Identity => new AxisMapping(
            new ChannelAssignment(InputChannel.MoveRight),
            new ChannelAssignment(InputChannel.MoveUp),
            new ChannelAssignment(InputChannel.MoveForward));

        /// <summary>手先の軸（0=右、1=上、2=前）の割り当て。</summary>
        public ChannelAssignment this[int axis]
        {
            get
            {
                switch (axis)
                {
                    case 0: return right;
                    case 1: return up;
                    case 2: return forward;
                    default: throw new ArgumentOutOfRangeException(nameof(axis));
                }
            }
            set
            {
                switch (axis)
                {
                    case 0: right = value; break;
                    case 1: up = value; break;
                    case 2: forward = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(axis));
                }
            }
        }

        /// <summary>使っている軸が、それぞれ別の入力を使っているか（同じ動きから2つの軸を取っていないか）。</summary>
        public bool HasDistinctChannels
        {
            get
            {
                for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    if (this[i].IsUsed && this[j].IsUsed && this[i].channel == this[j].channel) return false;
                return true;
            }
        }

        /// <summary>回転の入力を使っているか。</summary>
        public bool UsesRotation
        {
            get
            {
                for (int i = 0; i < 3; i++)
                    if (this[i].IsUsed && this[i].channel.IsRotation()) return true;
                return false;
            }
        }

        /// <summary>そのチャネルを、手先のどれかの軸に使っているか。</summary>
        public bool Uses(InputChannel channel)
        {
            for (int i = 0; i < 3; i++)
                if (this[i].IsUsed && this[i].channel == channel) return true;
            return false;
        }

        /// <summary>入力の6つの値から u（m、体の軸）を作る。</summary>
        public Vector3 Apply(in InputChannels input)
        {
            return new Vector3(Component(right, input), Component(up, input), Component(forward, input));
        }

        /// <summary>並進だけの入力から u を作る（回転は0とみなす）。</summary>
        public Vector3 Apply(Vector3 translation) => Apply(new InputChannels(translation, Vector3.zero));

        /// <summary>手先の軸（0=右、1=上、2=前）を反転したもの。</summary>
        public AxisMapping WithInverted(int axis)
        {
            var copy = this;
            var assignment = copy[axis];
            assignment.scale = -assignment.scale;
            copy[axis] = assignment;
            return copy;
        }

        /// <summary>
        /// 左右を鏡に写した型（右の部位の型を、左の同じ部位で左の追加肢を動かすときに使う）。
        /// 手先の右（x）と、入力の「右へ動かす・右へ傾ける・右へひねる」は鏡に写すと向きが逆になる。
        /// 片方だけが逆になる組み合わせ（例：手先の右 ← 前へ傾ける）は倍率の符号を変え、両方とも逆・両方とも変わらない組み合わせはそのまま。
        /// そのため「動かした向き＝手先の向き」の型（並進そのまま、上体のひねり→右 など）は、鏡に写しても同じ型になる。
        /// </summary>
        public AxisMapping Mirrored()
        {
            var copy = this;
            for (int axis = 0; axis < 3; axis++)
            {
                var assignment = copy[axis];
                if (!assignment.IsUsed) continue;
                bool handFlips = axis == 0;
                if (handFlips != assignment.channel.FlipsWhenMirrored()) assignment.scale = -assignment.scale;
                copy[axis] = assignment;
            }
            return copy;
        }

        /// <summary>手先の軸が、入力の向きと逆向きに動くか（倍率が負か）。</summary>
        public bool IsInverted(int axis) => this[axis].scale < 0f;

        /// <summary>手先の軸を動かす動かし方の言葉（例：「右へひねる」）。</summary>
        public string DescribeAxis(int axis)
        {
            var assignment = this[axis];
            return assignment.IsUsed ? InputChannelInfo.Describe(assignment.channel, assignment.scale) : "使わない";
        }

        float Component(ChannelAssignment assignment, in InputChannels input)
        {
            return assignment.IsUsed ? assignment.scale * input.Value(assignment.channel, leverLength) : 0f;
        }

        public override string ToString() => $"右←{right}, 上←{up}, 前←{forward}, てこ={leverLength:0.###} m";
    }
}
