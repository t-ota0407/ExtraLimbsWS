using ExtraLimb.Body;
using ExtraLimb.Core;
using ExtraLimb.Limb;
using ExtraLimb.Tracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 把持の入力と手（Step 3-4、計画書の決定事項#6・#23）。
    /// <list type="bullet">
    /// <item>追加肢：右コントローラーの人差し指トリガーを押している間だけ掴む（LimbRuntimeConfig.grasp）。実験者の G キーは予備。</item>
    /// <item>本来の手：左右のコントローラーのグリップで掴む（フェーズDで使う）。</item>
    /// </list>
    /// 掴んだ瞬間に、そのコントローラーを短く振動させる。追加肢の掴む点（手のひらのあたり）に小さな球を出し、
    /// 掴める物が近いと黄色、掴んでいると緑にする。シーンになければ LimbSession が起動時に追加する。
    /// </summary>
    [DefaultExecutionOrder(10016)]
    public class GraspRig : MonoBehaviour
    {
        [Tooltip("空なら同じ GameObject かシーンから探す")]
        [SerializeField] LimbSession session;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] SelfAvatarRig avatar;

        [Header("掴める範囲（m）")]
        [SerializeField] float limbRadius = 0.08f;
        [SerializeField] float handRadius = 0.06f;

        [Header("本来の手（グリップ）")]
        [SerializeField] bool naturalHands = true;
        [SerializeField] float gripPressThreshold = 0.6f;
        [SerializeField] float gripReleaseThreshold = 0.4f;

        [Header("振動")]
        [SerializeField] float hapticAmplitude = 0.6f;
        [SerializeField] float hapticSeconds = 0.08f;

        [Header("表示")]
        [SerializeField] bool showIndicator = true;
        [SerializeField] float indicatorSize = 0.025f;

        static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color HoverColor = new Color(1f, 0.85f, 0.2f);
        static readonly Color HeldColor = new Color(0.3f, 1f, 0.4f);

        EndEffectorGrasper limbGrasper, leftHand, rightHand;
        readonly HysteresisSwitch limbTrigger = new HysteresisSwitch();
        readonly HysteresisSwitch leftGrip = new HysteresisSwitch(), rightGrip = new HysteresisSwitch();
        Transform leftBone, rightBone, leftPalm, rightPalm;
        Transform indicator;
        Material indicatorMaterial;

        public EndEffectorGrasper LimbGrasper => limbGrasper;
        public EndEffectorGrasper LeftHand => leftHand;
        public EndEffectorGrasper RightHand => rightHand;

        /// <summary>追加肢を掴むトリガーの押し込み量（0〜1）。</summary>
        public float TriggerValue { get; private set; }

        /// <summary>G キーで掴んでいるか。</summary>
        public bool KeyPressed { get; private set; }

        public IControllerInput Controllers => ControllerInput.Provider;

        GraspTriggerConfig Config => session != null && session.Config.grasp != null ? session.Config.grasp : DefaultConfig;
        static readonly GraspTriggerConfig DefaultConfig = new GraspTriggerConfig();

        void Start()
        {
            if (session == null) session = GetComponent<LimbSession>();
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            if (avatar == null) avatar = FindFirstObjectByType<SelfAvatarRig>();

            limbGrasper = CreateGrasper("ExtraLimb_Grasp", "extra_limb", limbRadius, LimbPose);
            limbGrasper.Grabbed += target => OnGrabbed(limbGrasper, target, Config.hand);
            limbGrasper.Released += target => Debug.Log($"[ExtraLimb] 追加肢が「{target.name}」を放しました");

            var animator = avatar != null ? avatar.GetComponent<Animator>() : null;
            if (naturalHands && animator != null && animator.isHuman)
            {
                leftBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                rightBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
                leftPalm = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
                rightPalm = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                if (leftBone != null)
                {
                    leftHand = CreateGrasper("Hand_Left_Grasp", "hand_left", handRadius, () => HandPose(leftBone, leftPalm));
                    leftHand.Grabbed += target => OnGrabbed(leftHand, target, ControllerHand.Left);
                }
                if (rightBone != null)
                {
                    rightHand = CreateGrasper("Hand_Right_Grasp", "hand_right", handRadius, () => HandPose(rightBone, rightPalm));
                    rightHand.Grabbed += target => OnGrabbed(rightHand, target, ControllerHand.Right);
                }
            }

            if (showIndicator)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "ExtraLimb_GraspIndicator";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * indicatorSize;
                indicatorMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
                go.GetComponent<Renderer>().material = indicatorMaterial;
                indicator = go.transform;
            }
            Debug.Log($"[ExtraLimb] 把持：追加肢は{(Config.hand == ControllerHand.Left ? "左" : "右")}トリガー（予備 G キー）、本来の手は{(leftHand != null || rightHand != null ? "グリップ" : "なし")}。コントローラー入力：{Controllers.Name}");
        }

        EndEffectorGrasper CreateGrasper(string objectName, string effector, float radius, System.Func<Pose> pose)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var grasper = go.AddComponent<EndEffectorGrasper>();
            grasper.Radius = radius;
            grasper.EffectorName = effector;
            grasper.PoseSource = pose;
            return grasper;
        }

        void Update()
        {
            var input = Controllers;
            input.Tick(Time.unscaledDeltaTime);
            var config = Config;

            limbTrigger.On = config.pressThreshold;
            limbTrigger.Off = Mathf.Min(config.releaseThreshold, config.pressThreshold - 0.05f);
            TriggerValue = input.GetTrigger(config.hand);
            bool controller = config.UsesController && limbTrigger.Update(TriggerValue);
            var keyboard = Keyboard.current;
            KeyPressed = config.UsesKey && keyboard != null && keyboard.gKey.isPressed && !keyboard.ctrlKey.isPressed && !keyboard.altKey.isPressed;
            if (limbGrasper != null) limbGrasper.Pressed = controller || KeyPressed;

            leftGrip.On = rightGrip.On = gripPressThreshold;
            leftGrip.Off = rightGrip.Off = gripReleaseThreshold;
            if (leftHand != null) leftHand.Pressed = leftGrip.Update(input.GetGrip(ControllerHand.Left));
            if (rightHand != null) rightHand.Pressed = rightGrip.Update(input.GetGrip(ControllerHand.Right));
        }

        // 掴む点の表示（手を動かした後に置く）
        void LateUpdate()
        {
            if (indicator == null || limbGrasper == null) return;
            bool visible = session != null && session.Limb != null && session.Limb.IsReady && session.Limb.Visible;
            indicator.gameObject.SetActive(visible);
            if (!visible) return;
            indicator.position = limbGrasper.transform.position;
            var color = limbGrasper.Held != null ? HeldColor : limbGrasper.Hovered != null ? HoverColor : IdleColor;
            if (indicatorMaterial.HasProperty("_BaseColor")) indicatorMaterial.SetColor("_BaseColor", color);
            indicatorMaterial.color = color;
        }

        void OnGrabbed(EndEffectorGrasper grasper, Graspable target, ControllerHand hand)
        {
            Controllers.Vibrate(hand, hapticAmplitude, hapticSeconds);
            Debug.Log($"[ExtraLimb] {Describe(grasper)}が「{target.name}」を掴みました");
        }

        /// <summary>手の表示名。</summary>
        public string Describe(EndEffectorGrasper grasper)
        {
            if (grasper == limbGrasper) return "追加肢";
            if (grasper == leftHand) return "左手";
            if (grasper == rightHand) return "右手";
            return grasper != null ? grasper.EffectorName : "なし";
        }

        // 追加肢の掴む点：手首と指先の中間（手のひらのあたり）
        Pose LimbPose()
        {
            var limb = session != null ? session.Limb : null;
            if (limb == null || !limb.IsReady) return new Pose(transform.position, transform.rotation);
            return new Pose(Vector3.Lerp(limb.Hand.position, limb.ContactPoint, 0.5f), limb.Hand.rotation);
        }

        // 本来の手の掴む点：手首と中指の付け根の間
        static Pose HandPose(Transform hand, Transform palm)
        {
            var position = palm != null ? Vector3.Lerp(hand.position, palm.position, 0.7f) : hand.position;
            return new Pose(position, hand.rotation);
        }
    }
}
