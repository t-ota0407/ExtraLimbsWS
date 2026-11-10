using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Body
{
    /// <summary>
    /// 使える Attach Point の一覧（並び順が F1, F2, … のキーに対応）と、今の体での付け根の姿勢の計算。
    /// 確認用に、全ての付け根の位置に目印（小さな球）を出せる。
    /// 実行順は VRIK（9998）の後。
    /// </summary>
    [DefaultExecutionOrder(9999)]
    public class AttachPointRegistry : MonoBehaviour
    {
        [Tooltip("空なら同じ GameObject から探す")]
        [SerializeField] BodyFrameProvider body;
        [SerializeField] List<AttachPointDefinition> points = new List<AttachPointDefinition>();

        [Header("確認用の目印")]
        [SerializeField] bool showMarkers;
        [SerializeField] float markerSize = 0.03f;

        readonly List<Transform> markers = new List<Transform>();

        public BodyFrameProvider Body => body;
        public IReadOnlyList<AttachPointDefinition> Points => points;
        public int Count => points.Count;

        /// <summary>付け根の目印を出すか。</summary>
        public bool ShowMarkers
        {
            get => showMarkers;
            set => showMarkers = value;
        }

        void Awake() => Initialize();

        /// <summary>体のフレームを用意する。何度呼んでもよい（ほかの部品の Awake から先に呼ばれることがある）。</summary>
        public bool Initialize()
        {
            if (body == null) body = GetComponent<BodyFrameProvider>();
            return body != null && body.Initialize();
        }

        /// <summary>テストから一覧を与えるときに使う。</summary>
        internal void Configure(BodyFrameProvider provider, List<AttachPointDefinition> definitions)
        {
            body = provider;
            points = definitions;
        }

        void OnValidate()
        {
            var ids = new HashSet<string>();
            foreach (var point in points)
            {
                if (point == null) continue;
                if (!ids.Add(point.id)) Debug.LogWarning($"[ExtraLimb] Attach Point のIDが重複しています：{point.id}", this);
            }
        }

        public AttachPointDefinition Get(int index) => index >= 0 && index < points.Count ? points[index] : null;

        public int IndexOf(string id)
        {
            for (int i = 0; i < points.Count; i++)
                if (points[i] != null && points[i].id == id) return i;
            return -1;
        }

        public bool TryGet(string id, out AttachPointDefinition definition)
        {
            definition = Get(IndexOf(id));
            return definition != null;
        }

        /// <summary>
        /// 今の体での付け根の姿勢。mirror が true なら左の肢（x を反転）。
        /// 手首の既定位置と肘の点は、アバターの拡縮に合わせて大きさだけ変える（x の向きの反転は ExtraLimbRig が行う）。
        /// </summary>
        public bool TryComputeMount(AttachPointDefinition definition, bool mirror, out MountPose pose)
        {
            pose = default;
            if (definition == null || body == null || !body.IsReady) return false;
            if (!body.TryGetFrame(definition.parentBone, out var parent) || !body.TryGetFrame(definition.frameBone, out var frame)) return false;

            var offset = definition.jointOffset;
            if (mirror) offset.x = -offset.x;
            offset = body.Calibration.ScaleOffset(offset);
            float scale = body.Calibration.AvatarScale;
            pose = new MountPose(parent.position + parent.rotation * offset, frame.rotation, definition.restHandPosition * scale, definition.bendHint * scale);
            return true;
        }

        void LateUpdate()
        {
            for (int i = 0; i < markers.Count; i++) markers[i].gameObject.SetActive(showMarkers);
            if (!showMarkers || body == null || !body.IsReady) return;
            for (int i = 0; i < points.Count; i++)
            {
                if (i >= markers.Count) markers.Add(CreateMarker(i));
                bool valid = TryComputeMount(points[i], false, out var pose);
                markers[i].gameObject.SetActive(valid);
                if (valid) markers[i].position = pose.joint;
            }
        }

        Transform CreateMarker(int index)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"AttachPointMarker_{(points[index] != null ? points[index].id : index.ToString())}";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * markerSize;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            var color = Color.HSVToRGB(index / (float)Mathf.Max(points.Count, 1), 0.7f, 1f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
            go.GetComponent<Renderer>().material = material;
            return go.transform;
        }

        void OnDestroy()
        {
            foreach (var marker in markers)
                if (marker != null) Destroy(marker.gameObject);
        }
    }
}
