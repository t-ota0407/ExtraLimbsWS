using System;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>制御方式。実験では位置制御だけを使う（計画書の決定事項#20）。速度制御・ハイブリッドはコードとテストだけ残す。</summary>
    public enum ControlModeKind
    {
        /// <summary>位置制御（0次）：変位に比例して手先が動く。</summary>
        Position = 0,

        /// <summary>速度制御（1次）：変位に比例した速さで手先が動き続ける。</summary>
        Rate = 1,

        /// <summary>ハイブリッド：半径の内側は位置制御、外側は基準位置を動かす。</summary>
        Hybrid = 2,
    }

    /// <summary>掴む・離すの判定方法。</summary>
    public enum GraspTriggerKind
    {
        None = 0,

        /// <summary>入力トラッカーのひねり角で判定する。</summary>
        Rotation = 1,

        /// <summary>実験者のキー入力で判定する（WoZ）。</summary>
        Key = 2,

        /// <summary>ひねり角とキーのどちらでもよい。</summary>
        Any = 3,
    }

    /// <summary>把持トリガーの設定（Step 3-4で使う）。</summary>
    [Serializable]
    public class GraspTriggerConfig
    {
        public GraspTriggerKind kind = GraspTriggerKind.Key;

        /// <summary>ひねり角を測る、入力トラッカーの局所軸。</summary>
        public SignedAxis twistAxis = SignedAxis.PosZ;

        /// <summary>この角度（度）を超えたら掴む。</summary>
        public float onAngleDeg = 25f;

        /// <summary>この角度（度）を下回ったら離す。onAngleDeg より小さくして、閾値付近のちらつきを防ぐ。</summary>
        public float offAngleDeg = 15f;
    }

    /// <summary>変位に掛けるOne Euro Filterの設定。</summary>
    [Serializable]
    public class PoseFilterSettings
    {
        public bool enabled = true;

        /// <summary>止まっているときの遮断周波数（Hz）。小さいほど揺れが減るが遅れる。</summary>
        public float minCutoff = 1.5f;

        /// <summary>速さに応じて遮断周波数を上げる係数。大きいほど速い動きの遅れが減る。</summary>
        public float beta = 5f;

        /// <summary>速さの推定に使う遮断周波数（Hz）。</summary>
        public float derivativeCutoff = 1f;
    }

    /// <summary>
    /// 追加肢1本の、実行中の設定。これが唯一の正で、変更は LimbSession.Apply() を通す（Step 4-1）。
    /// JSONにそのまま書き出せる。ゲインや不感帯の初期値は仮で、パイロット（Step 6-1）で決める。
    /// </summary>
    [Serializable]
    public class LimbRuntimeConfig
    {
        /// <summary>生やす位置（Attach Point）のID。</summary>
        public string attachPointId = "shoulder_top";

        /// <summary>入力に使う部位。トラッカーの割り当て（TrackerBindingProfile）からトラッカーを探す。</summary>
        public BodySite inputSite = BodySite.RightAnkle;

        /// <summary>入力に使うソースのID（シリアル番号など）。空なら inputSite から探す。</summary>
        public string inputSourceId = "";

        /// <summary>変位の基準にするソースのID。空なら部屋（入力と同じ空間の固定の基準）。腰トラッカーを使うときはそのシリアル番号。</summary>
        public string referenceSourceId = "";

        /// <summary>制御方式。実験では Position だけを使う（パネルには出さない）。</summary>
        public ControlModeKind mode = ControlModeKind.Position;

        /// <summary>位置ゲイン G_p（無次元）。変位1cmで手先が何cm動くか。</summary>
        public float positionGain = 2f;

        /// <summary>速度ゲイン G_v（1/s）。変位1cmあたり、手先が毎秒何cm動くか。速度制御用（実験では使わない）。</summary>
        public float velocityGain = 3f;

        /// <summary>速度制御の不感帯の半径 d（cm）。</summary>
        public float deadzoneCm = 2f;

        /// <summary>ハイブリッド制御の切替半径 r（cm）。</summary>
        public float hybridRadiusCm = 8f;

        /// <summary>マッピングの型（MappingPreset）のID。空なら入力の部位の既定の型を使う。</summary>
        public string mappingPresetId = "";

        /// <summary>マッピングの中身。型を選ぶとその値が入り、軸ごとの反転はここに反映する。</summary>
        public AxisMapping axisMapping = AxisMapping.Identity;

        public PoseFilterSettings filter = new PoseFilterSettings();

        public GraspTriggerConfig grasp = new GraspTriggerConfig();

        /// <summary>不感帯の半径（m）。</summary>
        public float DeadzoneMeters => deadzoneCm * 0.01f;

        /// <summary>ハイブリッド制御の切替半径（m）。</summary>
        public float HybridRadiusMeters => hybridRadiusCm * 0.01f;

        public string ToJson(bool prettyPrint = false) => JsonUtility.ToJson(this, prettyPrint);

        /// <summary>JSONから読む。JSONにない項目は初期値のまま。</summary>
        public static LimbRuntimeConfig FromJson(string json)
        {
            var config = new LimbRuntimeConfig();
            if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, config);
            return config;
        }

        /// <summary>中のクラスまで含めた複製。</summary>
        public LimbRuntimeConfig Clone() => FromJson(ToJson());
    }
}
