using UnityEngine;

namespace ExtraLimb.Body
{
    /// <summary>
    /// 追加肢を生やす位置（Attach Point）1か所の定義。右の肢の値で書き、左の肢では x を反転して使う。
    /// 位置と向きはすべて「体の向き」（x=外、y=上、z=前、m）で、モデルの寸法で書く（参加者の体格には BodyCalibration で合わせる）。
    /// 付け根は骨の子にせず、毎フレーム親の骨の位置から置く。親の骨が拡縮されても（一人称で頭を隠すなど）追加肢は縮まない。
    /// </summary>
    [CreateAssetMenu(menuName = "ExtraLimb/Attach Point", fileName = "AttachPoint")]
    public class AttachPointDefinition : ScriptableObject
    {
        [Tooltip("設定やログで使うID（例：shoulder_top）")]
        public string id = "shoulder_top";

        [Tooltip("パネルに出す名前")]
        public string displayName = "肩の上";

        [Header("付け根")]
        [Tooltip("付け根が一緒に動く骨")]
        public HumanBodyBones parentBone = HumanBodyBones.UpperChest;

        [Tooltip("上腕の付け根の位置。親の骨の位置から、体の向きで（x は外向きが正、m）")]
        public Vector3 jointOffset = new Vector3(0.149f, 0.183f, -0.003f);

        [Header("手先のターゲットを表す座標")]
        [Tooltip("根元の座標の向きを合わせる骨。どの位置でも同じにすると、入力の向きと手先の動く向きの関係がそろう")]
        public HumanBodyBones frameBone = HumanBodyBones.UpperChest;

        [Tooltip("手首の既定位置（制御則の基準位置 p_b）。上腕の付け根から、体の向きで（x は外向きが正、m）")]
        public Vector3 restHandPosition = new Vector3(0.12f, -0.06f, 0.30f);

        [Tooltip("肘を向けたい点。上腕の付け根から、体の向きで（x は外向きが正、m）")]
        public Vector3 bendHint = new Vector3(0.35f, -0.10f, -0.05f);

        [Header("出典")]
        [TextArea]
        public string note = "";
    }
}
