using System.Collections.Generic;
using ExtraLimb.Body;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 仮想の鏡。鏡の面で反転した空間に、アバターの見た目だけの複製（鏡像）を置き、毎フレーム骨の姿勢を写す。
    /// <list type="bullet">
    /// <item>反射カメラを使わないので、HMD の左右の目で正しい奥行きに見える。</item>
    /// <item>鏡像の頭は隠さない（一人称で頭の骨を縮めていても、鏡には頭が映る）。</item>
    /// <item>映るのはアバター（追加肢を含む）だけ。鏡の向こうに壁と床だけの部屋を作り、鏡の枠の外からは鏡像が見えないようにする。</item>
    /// </list>
    /// この GameObject の位置と向きが鏡を表す：原点は鏡の下の床の高さ（左右の中央）、前（z）は鏡を見る人の側。
    /// 実行順は、体と追加肢を解き終えた後（LimbSession、仮のボタンより後）。表示は実験者パネルとホットキー（既定は M）で切り替える。
    /// </summary>
    [DefaultExecutionOrder(10100)]
    public class AvatarMirror : MonoBehaviour
    {
        [Tooltip("映すアバターの根元（SelfAvatar）。空ならシーンの SelfAvatarRig")]
        [SerializeField] Transform source;
        [Tooltip("鏡像に使う、見た目だけのモデル。映すアバターと同じ FBX にする（骨の階層で対応を取る）")]
        [SerializeField] GameObject visualModel;

        [Header("鏡と部屋（m）")]
        [SerializeField] Vector2 mirrorSize = new Vector2(1.2f, 2.0f);
        [Tooltip("鏡の下端の高さ")]
        [SerializeField] float mirrorBottom = 0.1f;
        [Tooltip("鏡のある壁の幅と高さ")]
        [SerializeField] Vector2 wallSize = new Vector2(4f, 2.6f);
        [Tooltip("鏡の向こうの部屋の奥行き。鏡から離れる距離の上限になる")]
        [SerializeField] float roomDepth = 4f;
        [SerializeField] bool buildRoom = true;
        [SerializeField] Color wallColor = new Color(0.78f, 0.78f, 0.80f);
        [SerializeField] Color frameColor = new Color(0.15f, 0.15f, 0.17f);
        [Tooltip("部屋の床の材質。空なら壁の色。シーンの床と同じにすると、鏡の中の床がつながって見える")]
        [SerializeField] Material floorMaterial;

        [Tooltip("鏡を出すか。実験者パネルとホットキー（HotkeyMap、既定は M）で切り替える")]
        [SerializeField] bool visible = true;

        const float WallThickness = 0.04f;
        const float FrameWidth = 0.04f;

        readonly List<(Transform source, Transform target, bool keepScale)> bones = new List<(Transform, Transform, bool)>();
        Transform mirrorSpace, image, room;
        bool built;

        /// <summary>鏡像の根元。</summary>
        public Transform Image => image;
        public bool IsBuilt => built;

        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
                if (mirrorSpace != null) mirrorSpace.gameObject.SetActive(value);
                if (room != null) room.gameObject.SetActive(value);
            }
        }

        void Start() => Build();

        /// <summary>テストから映す対象とモデルを与えるときに使う。</summary>
        internal void Configure(Transform sourceRoot, GameObject model, bool withRoom)
        {
            source = sourceRoot;
            visualModel = model;
            buildRoom = withRoom;
        }

        /// <summary>鏡像と部屋を作る。2回目以降は何もしない。</summary>
        internal bool Build()
        {
            if (built) return true;
            if (source == null)
            {
                var avatar = FindFirstObjectByType<SelfAvatarRig>();
                if (avatar != null) source = avatar.transform;
            }
            if (source == null || visualModel == null)
            {
                Debug.LogWarning("[ExtraLimb] 鏡：映すアバターか鏡像のモデルが指定されていません", this);
                return false;
            }

            mirrorSpace = new GameObject("MirrorSpace").transform;
            mirrorSpace.SetParent(transform, false);
            mirrorSpace.localScale = new Vector3(1f, 1f, -1f); // 鏡の面（z=0）で反転

            image = Instantiate(visualModel, mirrorSpace).transform;
            image.name = "MirrorImage";
            var animator = image.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
            foreach (var renderer in image.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.updateWhenOffscreen = true;

            MapBones();
            if (buildRoom) BuildRoom();
            built = true;
            Visible = visible;
            Sync();
            return true;
        }

        // 映すアバターと鏡像の骨を、根元からの階層の名前で対応させる
        void MapBones()
        {
            var targets = new Dictionary<string, Transform>();
            foreach (var t in image.GetComponentsInChildren<Transform>(true))
                if (t != image) targets[PathOf(image, t)] = t;

            var sourceAnimator = source.GetComponent<Animator>();
            var head = sourceAnimator != null && sourceAnimator.isHuman ? sourceAnimator.GetBoneTransform(HumanBodyBones.Head) : null;
            bones.Clear();
            foreach (var t in source.GetComponentsInChildren<Transform>(true))
            {
                if (t == source || !targets.TryGetValue(PathOf(source, t), out var target)) continue;
                bones.Add((t, target, t == head));
            }
        }

        static string PathOf(Transform root, Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>今のアバターの姿勢を鏡像に写す。</summary>
        internal void Sync()
        {
            if (!built || source == null) return;
            image.localPosition = transform.InverseTransformPoint(source.position);
            image.localRotation = Quaternion.Inverse(transform.rotation) * source.rotation;
            image.localScale = source.lossyScale;
            foreach (var (from, to, keepScale) in bones)
            {
                to.localPosition = from.localPosition;
                to.localRotation = from.localRotation;
                // 頭は一人称のために縮めてあるので、鏡像では元の大きさのまま
                if (!keepScale) to.localScale = from.localScale;
            }
        }

        void LateUpdate()
        {
            if (built && visible) Sync();
        }

        // ---- 部屋（鏡のある壁と、鏡の向こうの床・壁・天井） ----

        void BuildRoom()
        {
            room = new GameObject("MirrorRoom").transform;
            room.SetParent(transform, false);

            float w = mirrorSize.x, h = mirrorSize.y, bottom = mirrorBottom;
            float W = Mathf.Max(wallSize.x, w + 0.2f), H = Mathf.Max(wallSize.y, bottom + h + 0.1f), D = roomDepth;
            float z = -WallThickness * 0.5f;

            // 鏡のある壁（鏡の部分だけ穴が開いている）
            float side = (W - w) * 0.5f;
            Box("Wall_Left", new Vector3(-(w + side) * 0.5f, H * 0.5f, z), new Vector3(side, H, WallThickness), wallColor);
            Box("Wall_Right", new Vector3((w + side) * 0.5f, H * 0.5f, z), new Vector3(side, H, WallThickness), wallColor);
            if (bottom > 0f) Box("Wall_Bottom", new Vector3(0f, bottom * 0.5f, z), new Vector3(w, bottom, WallThickness), wallColor);
            float top = H - (bottom + h);
            if (top > 0f) Box("Wall_Top", new Vector3(0f, bottom + h + top * 0.5f, z), new Vector3(w, top, WallThickness), wallColor);

            // 鏡の枠
            float fz = FrameWidth * 0.5f;
            Box("Frame_Left", new Vector3(-(w + FrameWidth) * 0.5f, bottom + h * 0.5f, fz), new Vector3(FrameWidth, h + 2f * FrameWidth, FrameWidth), frameColor);
            Box("Frame_Right", new Vector3((w + FrameWidth) * 0.5f, bottom + h * 0.5f, fz), new Vector3(FrameWidth, h + 2f * FrameWidth, FrameWidth), frameColor);
            Box("Frame_Bottom", new Vector3(0f, bottom - FrameWidth * 0.5f, fz), new Vector3(w, FrameWidth, FrameWidth), frameColor);
            Box("Frame_Top", new Vector3(0f, bottom + h + FrameWidth * 0.5f, fz), new Vector3(w, FrameWidth, FrameWidth), frameColor);

            // 鏡の向こうの部屋（床はシーンの床とつながるように、ほんの少し上げる）
            var floor = Box("Room_Floor", new Vector3(0f, -0.003f, -D * 0.5f), new Vector3(W, 0.01f, D), wallColor);
            if (floorMaterial != null) floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            Box("Room_Back", new Vector3(0f, H * 0.5f, -D - WallThickness * 0.5f), new Vector3(W, H, WallThickness), wallColor);
            Box("Room_Left", new Vector3(-W * 0.5f - WallThickness * 0.5f, H * 0.5f, -D * 0.5f), new Vector3(WallThickness, H, D), wallColor);
            Box("Room_Right", new Vector3(W * 0.5f + WallThickness * 0.5f, H * 0.5f, -D * 0.5f), new Vector3(WallThickness, H, D), wallColor);
            Box("Room_Ceiling", new Vector3(0f, H + WallThickness * 0.5f, -D * 0.5f), new Vector3(W, WallThickness, D), wallColor);
        }

        GameObject Box(string objectName, Vector3 localPosition, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objectName;
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(room, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            var renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
            renderer.sharedMaterial = material;
            return go;
        }

        static void DestroySafe(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
