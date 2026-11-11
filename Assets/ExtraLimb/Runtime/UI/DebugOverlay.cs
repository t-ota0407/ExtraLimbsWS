using System.Collections.Generic;
using ExtraLimb.Experiment;
using ExtraLimb.Limb;
using UnityEngine;

namespace ExtraLimb.UI
{
    /// <summary>
    /// モニタだけの可視化（Step 4-2b、計画書の Part 2-4「モニタだけの可視化」）。動かしている追加肢ごとに描く。
    /// <list type="bullet">
    /// <item>Re-center時の基準点（手首の基準位置 p_b）：白い小さな球。</item>
    /// <item>変位ベクトル：基準点から IK ターゲットへの線（右は水色、左は桃色）。IK ターゲットの目印（LimbSession の球）もモニタにだけ描く。</item>
    /// <item>届く範囲：根元を中心とする3つの円（最大半径）。手先が範囲の端にあると赤くなる。</item>
    /// <item>トリガーの押し込み量：手先から上へ伸びる棒（長さが押し込み量。掴む操作中は緑）。</item>
    /// </list>
    /// 入力の6つの値は、実験者パネルの「入力の6つの値」に出す。すべて DisplayLayers.MonitorOnly に置くので、HMD には映らない
    /// （SpectatorCamera の「参加者にも可視化を見せる」をオンにしたときだけ映る）。
    /// </summary>
    [DefaultExecutionOrder(10065)]
    public class DebugOverlay : MonoBehaviour
    {
        [Tooltip("空ならシーンから探す")]
        [SerializeField] LimbSession session;
        [SerializeField] bool show = true;
        [SerializeField] float lineWidth = 0.005f;
        [SerializeField] float baseSize = 0.025f;
        [Tooltip("トリガーを押し切ったときの棒の長さ（m）")]
        [SerializeField] float triggerBarLength = 0.12f;
        [SerializeField] int circleSegments = 64;

        static readonly Color[] LimbColors = { new Color(0.3f, 0.9f, 1f), new Color(1f, 0.45f, 0.85f) };
        static readonly Color WorkspaceColor = new Color(0.55f, 0.6f, 0.68f);
        static readonly Color EdgeColor = new Color(1f, 0.35f, 0.3f);
        static readonly Color TriggerColor = new Color(1f, 0.85f, 0.2f);
        static readonly Color GraspColor = new Color(0.3f, 1f, 0.4f);

        class LimbViz
        {
            public LimbChannel channel;
            public GameObject root;
            public Transform basePoint;
            public LineRenderer vector;
            public LineRenderer[] circles;
            public LineRenderer trigger;
            public Material workspaceMaterial, triggerMaterial;
        }

        readonly List<LimbViz> vizzes = new List<LimbViz>();
        Transform container;

        /// <summary>可視化を出すか。</summary>
        public bool Show
        {
            get => show;
            set
            {
                show = value;
                if (container != null) container.gameObject.SetActive(show);
            }
        }

        void OnEnable()
        {
            if (session == null) session = FindFirstObjectByType<LimbSession>();
        }

        void LateUpdate()
        {
            if (session == null) return;
            if (container == null)
            {
                container = new GameObject("ExtraLimb_MonitorOverlay").transform;
                container.SetParent(transform, false);
                container.gameObject.layer = DisplayLayers.MonitorOnly;
                container.gameObject.SetActive(show);
            }
            if (!show) return;

            foreach (var channel in session.Channels)
                if (vizzes.Find(v => v.channel == channel) == null) vizzes.Add(Create(channel, vizzes.Count));

            var grasp = session.Grasp;
            foreach (var viz in vizzes) UpdateViz(viz, grasp);
        }

        void UpdateViz(LimbViz viz, GraspRig grasp)
        {
            var channel = viz.channel;
            var limb = channel.Limb;
            bool visible = channel.Active && limb != null && limb.IsReady && limb.Visible;
            viz.root.SetActive(visible);
            if (!visible) return;

            var pipeline = channel.Pipeline;
            var basePoint = limb.FrameToWorld(pipeline.BasePosition);
            var target = limb.FrameToWorld(pipeline.Target);
            viz.basePoint.position = basePoint;
            viz.vector.SetPosition(0, basePoint);
            viz.vector.SetPosition(1, target);

            // 届く範囲：根元の座標の3つの面の円
            var frame = limb.Frame;
            var workspace = pipeline.Workspace;
            var center = limb.FrameToWorld(workspace != null ? workspace.Center : Vector3.zero);
            float radius = workspace != null ? workspace.MaxRadius : limb.Reach;
            var axes = new[]
            {
                (frame.rotation * Vector3.right, frame.rotation * Vector3.up),
                (frame.rotation * Vector3.up, frame.rotation * Vector3.forward),
                (frame.rotation * Vector3.forward, frame.rotation * Vector3.right),
            };
            for (int c = 0; c < viz.circles.Length; c++)
            {
                var (a, b) = axes[c];
                var line = viz.circles[c];
                for (int i = 0; i < circleSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / circleSegments;
                    line.SetPosition(i, center + (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * radius);
                }
            }
            SetColor(viz.workspaceMaterial, pipeline.AtWorkspaceBoundary ? EdgeColor : WorkspaceColor);

            // トリガーの押し込み量：手先から上へ
            var grasper = grasp != null ? grasp.GrasperFor(channel) : null;
            float value = grasp != null ? Mathf.Clamp01(grasp.TriggerValue(channel)) : 0f;
            var hand = limb.ContactPoint;
            viz.trigger.gameObject.SetActive(value > 0.01f || (grasper != null && grasper.Pressed));
            viz.trigger.SetPosition(0, hand);
            viz.trigger.SetPosition(1, hand + Vector3.up * (triggerBarLength * Mathf.Max(value, 0.02f)));
            SetColor(viz.triggerMaterial, grasper != null && grasper.Pressed ? GraspColor : TriggerColor);
        }

        LimbViz Create(LimbChannel channel, int index)
        {
            var color = LimbColors[index % LimbColors.Length];
            var viz = new LimbViz { channel = channel };
            viz.root = new GameObject($"Overlay_{channel.Subject}");
            viz.root.transform.SetParent(container, false);

            var basePoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            basePoint.name = "BasePoint";
            Destroy(basePoint.GetComponent<Collider>());
            basePoint.transform.SetParent(viz.root.transform, false);
            basePoint.transform.localScale = Vector3.one * baseSize;
            basePoint.GetComponent<Renderer>().material = CreateMaterial(Color.white);
            viz.basePoint = basePoint.transform;

            viz.vector = CreateLine(viz.root.transform, "Displacement", 2, false, CreateMaterial(color), lineWidth * 1.4f);
            viz.workspaceMaterial = CreateMaterial(WorkspaceColor);
            viz.circles = new LineRenderer[3];
            for (int i = 0; i < 3; i++) viz.circles[i] = CreateLine(viz.root.transform, $"Workspace_{i}", circleSegments, true, viz.workspaceMaterial, lineWidth * 0.6f);
            viz.triggerMaterial = CreateMaterial(TriggerColor);
            viz.trigger = CreateLine(viz.root.transform, "Trigger", 2, false, viz.triggerMaterial, lineWidth * 3f);

            DisplayLayers.SetLayerRecursively(viz.root, DisplayLayers.MonitorOnly);
            return viz;
        }

        static LineRenderer CreateLine(Transform parent, string name, int points, bool loop, Material material, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points;
            line.loop = loop;
            line.widthMultiplier = width;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        static Material CreateMaterial(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            SetColor(material, color);
            return material;
        }

        static void SetColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
        }
    }
}
