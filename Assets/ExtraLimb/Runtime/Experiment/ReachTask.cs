using System.Collections.Generic;
using System.Globalization;
using ExtraLimb.Body;
using ExtraLimb.Core;
using ExtraLimb.Limb;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// リーチング課題（Step 5-1、計画書の1.5）。体の前の円周上に並べたターゲット（TaskLayout）を1つずつ点灯し、触れると別のターゲットを点灯する。
    /// <list type="bullet">
    /// <item>前半（追加肢だけ）：ターゲットは追加肢の指先にだけ反応する。本来の手で触れても数えない（回数は記録する）。</item>
    /// <item>後半（本来の腕も使う）：追加肢と本来の両手のどれで触れても数え、どれで触れたかを記録する。</item>
    /// <item>点灯する順は、参加者ID・条件・何回目か・区間から決まる乱数（StableRandom）。同じ位置は続けない。</item>
    /// <item>円は、最初に実験者が Re-center したとき（Space・パネル）に、そのときの胸の位置と向き・身長・肩幅から置く。
    /// 生やす位置や入力部位を変えても動かさない。休憩で座り直したら、パネルの「円を置き直す」で置き直す。身長・肩幅を変えたときも置き直す。</item>
    /// <item>記録：ターゲット1個に1行を reach.csv に書く（触れた・時間切れ・中止）。円の位置は events.csv の reach_ring（置いたときと、記録を始め直したとき）。</item>
    /// </list>
    /// ConditionRunner が IConditionTask として前半・後半の始めと終わりを知らせる（練習でも同じ）。
    /// </summary>
    [DefaultExecutionOrder(10025)]
    public class ReachTask : MonoBehaviour, IConditionTask
    {
        public const string ReachFileName = "reach.csv";

        /// <summary>reach.csv の列。position は 1 始まり（1＝上、参加者から見て時計回り）。時刻は events.csv の time と同じ。</summary>
        public static readonly string[] ReachColumns =
        {
            "participant", "order", "code", "attempt", "segment", "target_number", "position", "position_name", "result", "effector",
            "lit_time", "touch_time", "reach_time", "hand_touches_ignored", "target_x", "target_y", "target_z",
            "attach_point", "input_site", "mapping",
        };

        [Tooltip("ターゲットの円の大きさと位置")]
        [SerializeField] TaskLayout layout;
        [Tooltip("空なら同じ GameObject かシーンから探す")]
        [SerializeField] LimbSession session;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] ConditionRunner runner;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] ExperimentRecorder recorder;
        [Tooltip("空ならシーンから探す。円を置いたら仮のボタンと立方体を隠す（hidePracticeBoardWhenPlaced）")]
        [SerializeField] PracticeButtonBoard practiceBoard;
        [Tooltip("課題をしていない間も、ターゲットの位置を薄く見せる")]
        [SerializeField] bool showIdleTargets = true;
        [SerializeField] bool hidePracticeBoardWhenPlaced = true;
        [SerializeField] float hitFlashSeconds = 0.25f;
        [SerializeField] Color idleColor = new Color(0.42f, 0.45f, 0.5f);
        [SerializeField] Color litColor = new Color(1f, 0.82f, 0.12f);
        [SerializeField] Color hitColor = new Color(0.3f, 1f, 0.45f);
        [Tooltip("当たる範囲（効果器の球）を半透明で見せる。参加者（HMD）にも見える。切り替えは ShowProbes")]
        [SerializeField] bool showProbes = true;

        // 当たる範囲の色（シーンに保存しない。保存すると、コードで色を変えても開いているシーンの古い値が使われるため）
        // 桃色：アバターの水色と、ターゲットの灰・黄・緑と見分けられる色。追加肢も本来の手も同じ指先の球なので同じ色
        static readonly Color ProbeColor = new Color(1f, 0.36f, 0.78f, 0.9f);
        // 球の中の濃さ（縁に対する比）。小さいので濃く
        const float ProbeCenterAlpha = 0.75f;

        readonly ReachJudge judge = new ReachJudge();
        readonly List<EffectorProbe> probes = new List<EffectorProbe>();
        readonly CsvRow row = new CsvRow();
        readonly List<Transform> spheres = new List<Transform>();
        readonly List<Material> materials = new List<Material>();
        ReachRing ring;
        bool placed;
        Transform root;
        LineRenderer outline;
        Material outlineMaterial;
        Transform probeRoot;
        readonly List<Transform> probeSpheres = new List<Transform>();
        Material probeMaterial;
        BodyFrameProvider body;
        bool placeRequested;
        string reportedDirectory;

        int segment;
        bool naturalHandsCount;
        int lit = -1;
        int hits, ignoredForTarget, ignoredInSegment, targetNumber;
        double litAt = double.NaN;
        float litAtReal;
        StableRandom sequence;
        int flashIndex = -1;
        float flashUntil;
        string message = "";

        /// <summary>円を置いたか。</summary>
        public bool IsPlaced => placed;

        public ReachRing Ring => ring;

        public TaskLayout Layout => layout;

        /// <summary>今の区間（0＝していない、1＝前半、2＝後半）。</summary>
        public int Segment => segment;

        public bool IsRunning => segment != 0;

        /// <summary>今の区間で数えた到達数。</summary>
        public int Hits => hits;

        /// <summary>今の区間で、本来の手で触れたが数えなかった回数（前半）。</summary>
        public int IgnoredInSegment => ignoredInSegment;

        /// <summary>点灯しているターゲット（0 始まり、なければ -1）。</summary>
        public int LitIndex => lit;

        /// <summary>今のターゲットを点灯してからの秒。</summary>
        public float LitSeconds => lit >= 0 ? Time.realtimeSinceStartup - litAtReal : 0f;

        /// <summary>直前に触れた効果器と、点灯から触れるまでの秒。</summary>
        public EffectorKind LastEffector { get; private set; }
        public float LastReachSeconds { get; private set; }

        /// <summary>最後のメッセージ（パネルに出す）。</summary>
        public string Message => message;

        /// <summary>
        /// 当たる範囲を見せるか（2026-11-13）。円を置いている間、追加肢と本来の手の指先の球（半径 probeRadius）を、
        /// 判定と同じ位置と大きさで半透明に描く。前半（追加肢だけ）は本来の手の球を出さない。
        /// </summary>
        public bool ShowProbes
        {
            get => showProbes;
            set
            {
                showProbes = value;
                UpdateProbeVisuals();
            }
        }

        /// <summary>課題をしていない間もターゲットの位置を見せるか。</summary>
        public bool ShowIdleTargets
        {
            get => showIdleTargets;
            set
            {
                showIdleTargets = value;
                UpdateVisibility();
            }
        }

        void OnEnable()
        {
            if (session == null) session = GetComponent<LimbSession>();
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            if (runner == null) runner = FindFirstObjectByType<ConditionRunner>();
            if (recorder == null) recorder = FindFirstObjectByType<ExperimentRecorder>();
            if (practiceBoard == null) practiceBoard = FindFirstObjectByType<PracticeButtonBoard>();
            if (session != null) session.EventRaised += OnSessionEvent;
            HookBody();
            if (layout == null) Debug.LogWarning("[ExtraLimb] リーチング課題：TaskLayout が設定されていません", this);
        }

        void OnDisable()
        {
            if (session != null) session.EventRaised -= OnSessionEvent;
            if (body != null) body.CalibrationChanged -= OnBodyChanged;
            body = null;
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
            foreach (var material in materials) Destroy(material);
            if (outlineMaterial != null) Destroy(outlineMaterial);
            if (probeRoot != null) Destroy(probeRoot.gameObject);
            if (probeMaterial != null) Destroy(probeMaterial);
        }

        void OnSessionEvent(SessionEvent e)
        {
            // 最初に実験者が Re-center したとき（校正のとき）に円を置く。体を解いた後（LateUpdate）に置く
            if (!placed && e.type == SessionEventTypes.RecenterRequest && e.origin != ChangeOrigins.System) placeRequested = true;
        }

        // 身長・肩幅を変えたら、アバターの大きさが変わった後の体で置き直す
        void OnBodyChanged()
        {
            if (placed) placeRequested = true;
        }

        // ---- 円を置く ----

        /// <summary>今の胸の位置と向き・身長・肩幅で、円を置く（置き直す）。課題の途中は置き直さない。</summary>
        public bool Place()
        {
            if (IsRunning) return Fail("課題中は円を置き直せません");
            if (layout == null) return Fail("円の設定（ReachLayout）がありません");
            if (!TryGetBody(out var chest)) return Fail("アバターの準備ができていないため、円を置けません");
            var calibration = body.Calibration;
            ring = layout.Compute(chest, calibration.Height, calibration.ShoulderWidth);
            placed = true;
            BuildVisuals();
            ReportRing("place");
            if (hidePracticeBoardWhenPlaced && practiceBoard != null && practiceBoard.Visible) practiceBoard.Visible = false;
            Say("");
            return true;
        }

        void HookBody()
        {
            if (body != null || session == null || session.Body == null) return;
            body = session.Body;
            body.CalibrationChanged += OnBodyChanged;
        }

        // 円の位置をイベントに残す。置いたとき（place）と、円を置いた後に新しい記録を始めたとき（current）
        void ReportRing(string key)
        {
            reportedDirectory = recorder != null && recorder.IsRecording ? recorder.CurrentDirectory : null;
            if (session == null) return;
            float yaw = Mathf.Atan2(ring.forward.x, ring.forward.z) * Mathf.Rad2Deg;
            string text = $"中心（{Format(ring.center)}）、前の向き {yaw.ToString("F1", CultureInfo.InvariantCulture)}°、半径 {ring.radius * 100f:F1} cm、" +
                          $"ターゲット {ring.Count} 個（半径 {layout.targetRadius * 100f:F1} cm）";
            session.Report(SessionEventTypes.ReachRing, "session", key, "", text,
                key == "place" ? $"リーチング課題の円を置きました：{text}" : $"リーチング課題の円（記録の開始の前に置いたもの）：{text}");
        }

        bool TryGetBody(out Pose chest)
        {
            chest = default;
            HookBody();
            return body != null && body.IsReady && body.TryGetFrame(HumanBodyBones.UpperChest, out chest);
        }

        /// <summary>
        /// 追加肢（動かしているもののどれか）が届くターゲットの数（今の生やす位置で。届く距離＝腕の長さの95%＋手首から指先まで＋ターゲットの半径）。
        /// 円を置いていないか、追加肢の準備ができていなければ -1。unreachable に届かないターゲットの名前を入れる。
        /// </summary>
        public int CountReachable(out string unreachable)
        {
            unreachable = "";
            if (!placed || layout == null || session == null) return -1;
            var limbs = new List<ExtraLimbRig>();
            foreach (var channel in session.Channels)
            {
                var limb = channel.Limb;
                if (channel.Active && limb != null && limb.IsReady && limb.Reach > 1e-3f) limbs.Add(limb);
            }
            if (limbs.Count == 0) return -1;
            int count = 0;
            var names = new List<string>();
            for (int k = 0; k < ring.Count; k++)
            {
                bool reachable = false;
                foreach (var limb in limbs)
                {
                    float tip = (limb.ContactPoint - limb.Hand.position).magnitude;
                    float reach = limb.Reach * 0.95f + tip + layout.targetRadius;
                    if (Vector3.Distance(limb.Frame.position, ring.targets[k]) <= reach) reachable = true;
                }
                if (reachable) count++;
                else names.Add(ReachRing.PositionName(k, ring.Count));
            }
            unreachable = string.Join("・", names);
            return count;
        }

        // ---- 課題（ConditionRunner から） ----

        public void BeginSegment(int segmentNumber, bool countNaturalHands)
        {
            if (!placed && !Place())
            {
                Debug.LogWarning($"[ExtraLimb] リーチング課題：円を置けないため、区間 {segmentNumber} を始められません（{message}）", this);
                return;
            }
            segment = segmentNumber;
            naturalHandsCount = countNaturalHands;
            hits = ignoredInSegment = targetNumber = 0;
            lit = -1;
            LastEffector = EffectorKind.None;
            sequence = new StableRandom(SeedText(segmentNumber));
            LightNext();
            UpdateVisibility();
        }

        public int EndSegment(int segmentNumber)
        {
            if (segment == 0) return 0;
            if (lit >= 0) WriteRow("timeout", EffectorKind.None, double.NaN);
            int count = hits;
            Stop();
            return count;
        }

        public void CancelSegment()
        {
            if (segment == 0) return;
            if (lit >= 0) WriteRow("aborted", EffectorKind.None, double.NaN);
            Stop();
        }

        void Stop()
        {
            segment = 0;
            lit = -1;
            UpdateVisibility();
        }

        void LightNext()
        {
            lit = sequence.NextOther(ring.Count, lit);
            targetNumber++;
            litAt = Elapsed();
            litAtReal = Time.realtimeSinceStartup;
            ignoredForTarget = 0;
            CollectProbes();
            judge.Reset(ring.targets[lit], layout.targetRadius, probes);
        }

        void LateUpdate()
        {
            if (placeRequested && !IsRunning)
            {
                placeRequested = false;
                Place();
            }
            if (placed && recorder != null && recorder.IsRecording && recorder.CurrentDirectory != reportedDirectory) ReportRing("current");
            // 追加肢と本来の手を解いた後（LimbSession・VRIK の後）に判定する
            if (segment != 0 && placed && lit >= 0)
            {
                CollectProbes();
                var effector = judge.Update(ring.targets[lit], layout.targetRadius, probes, naturalHandsCount, out int ignored);
                ignoredForTarget += ignored;
                ignoredInSegment += ignored;
                if (effector != EffectorKind.None)
                {
                    double touchedAt = Elapsed();
                    LastEffector = effector;
                    LastReachSeconds = Time.realtimeSinceStartup - litAtReal;
                    WriteRow("hit", effector, touchedAt);
                    hits++;
                    flashIndex = lit;
                    flashUntil = Time.realtimeSinceStartup + hitFlashSeconds;
                    LightNext();
                }
            }
            UpdateColors();
            UpdateProbeVisuals();
        }

        /// <summary>
        /// 効果器の球の中心：見た目の指先から、指の付け根の向きへ半径だけ戻った点。球の先端が指先にそろうので、
        /// 指先がターゲットの表面に触れたときに触れたとする（2026-11-13。追加肢は、それまで指先より約4cm先の骨の端を中心にしていた）。
        /// </summary>
        public static Vector3 FingertipProbeCenter(Vector3 fingertip, Vector3 fingerDirection, float radius) =>
            fingertip - fingerDirection.normalized * radius;

        /// <summary>
        /// 本来の手の効果器の球の中心：中指の第3関節（middleDistal）の子を見た目の指先とし、第3関節から指先への向きで FingertipProbeCenter と同じにする。
        /// 指の骨がなければ手首（2026-11-13。それまでは手のひらの点に半径 4 cm）。
        /// </summary>
        public static Vector3 HandProbeCenter(Transform hand, Transform middleDistal, float radius)
        {
            if (middleDistal == null || middleDistal.childCount == 0) return hand.position;
            var tip = middleDistal.GetChild(0).position;
            return FingertipProbeCenter(tip, tip - middleDistal.position, radius);
        }

        // 効果器：追加肢の指先（右を先に）、本来の手の指先。どちらも見た目の中指の先を先端とする同じ大きさの球
        void CollectProbes()
        {
            probes.Clear();
            if (session != null)
            {
                foreach (var channel in session.Channels)
                {
                    var limb = channel.Limb;
                    if (!channel.Active || limb == null || !limb.IsReady || !limb.Visible) continue;
                    var kind = limb.Side == LimbSide.Left ? EffectorKind.ExtraLeft : EffectorKind.ExtraRight;
                    probes.Add(new EffectorProbe(kind, true, FingertipProbeCenter(limb.ContactPoint, limb.ContactDirection, layout.probeRadius),
                        layout.probeRadius));
                }
            }
            HookBody();
            var animator = body != null ? body.Animator : null;
            if (animator == null) return;
            AddHand(animator, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleDistal, EffectorKind.HandLeft);
            AddHand(animator, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleDistal, EffectorKind.HandRight);
        }

        void AddHand(Animator animator, HumanBodyBones handBone, HumanBodyBones middleDistalBone, EffectorKind kind)
        {
            var hand = animator.GetBoneTransform(handBone);
            if (hand == null) return;
            var center = HandProbeCenter(hand, animator.GetBoneTransform(middleDistalBone), layout.probeRadius);
            probes.Add(new EffectorProbe(kind, true, center, layout.probeRadius));
        }

        string SeedText(int segmentNumber)
        {
            var spec = runner != null && !runner.IsPractice ? runner.Current : null;
            string participant = runner?.Plan?.participantId ?? "";
            string order = spec != null ? (spec.order + 1).ToString(CultureInfo.InvariantCulture) : "practice";
            int attempt = spec != null ? runner.AttemptsOf(runner.CurrentIndex) : runner != null ? runner.PracticeCount : 0;
            return $"{participant}|{order}|{attempt}|{segmentNumber}";
        }

        // ---- 記録 ----

        void WriteRow(string result, EffectorKind effector, double touchedAt)
        {
            if (recorder == null || !recorder.IsRecording || lit < 0) return;
            var spec = runner != null && !runner.IsPractice ? runner.Current : null;
            var config = session != null && session.Primary != null ? session.Primary.Config : null;
            int attempt = spec != null ? runner.AttemptsOf(runner.CurrentIndex) : runner != null ? runner.PracticeCount : 0;
            var target = ring.targets[lit];
            row.Clear()
                .Add(runner?.Plan?.participantId ?? "")
                .Add(spec != null ? (spec.order + 1).ToString(CultureInfo.InvariantCulture) : "practice")
                .Add(spec?.Code ?? "")
                .Add(attempt)
                .Add(segment)
                .Add(targetNumber)
                .Add(lit + 1)
                .Add(ReachRing.PositionName(lit, ring.Count))
                .Add(result)
                .Add(EffectorKinds.Key(effector))
                .Add(litAt, 3)
                .Add(touchedAt, 3)
                .Add(double.IsNaN(touchedAt) || double.IsNaN(litAt) ? double.NaN : touchedAt - litAt, 3)
                .Add(ignoredForTarget)
                .Add(target.x, 4).Add(target.y, 4).Add(target.z, 4)
                .Add(spec != null ? spec.attachPointId : config?.attachPointId ?? "")
                .Add(spec != null ? spec.inputSite.ToString() : config != null ? config.inputSite.ToString() : "")
                .Add(spec != null ? spec.mappingPresetId : config?.mappingPresetId ?? "");
            recorder.WriteTableRow(ReachFileName, ReachColumns, row);
        }

        double Elapsed() => recorder != null && recorder.IsRecording ? recorder.ElapsedSeconds : double.NaN;

        // ---- 見た目 ----

        void BuildVisuals()
        {
            if (root == null)
            {
                var go = new GameObject("ReachTargets");
                root = go.transform;
            }
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            while (spheres.Count < ring.Count)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"ReachTarget_{spheres.Count + 1}";
                Destroy(sphere.GetComponent<Collider>());
                sphere.transform.SetParent(root, false);
                var renderer = sphere.GetComponent<Renderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var material = new Material(shader);
                renderer.sharedMaterial = material;
                materials.Add(material);
                spheres.Add(sphere.transform);
            }
            for (int k = 0; k < spheres.Count; k++)
            {
                bool used = k < ring.Count;
                spheres[k].gameObject.SetActive(used);
                if (!used) continue;
                spheres[k].position = ring.targets[k];
                spheres[k].localScale = Vector3.one * (layout.targetRadius * 2f);
            }
            if (outline == null)
            {
                var go = new GameObject("ReachRingOutline");
                go.transform.SetParent(root, false);
                outline = go.AddComponent<LineRenderer>();
                outline.loop = true;
                outline.useWorldSpace = true;
                outline.widthMultiplier = 0.002f;
                outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outline.receiveShadows = false;
                outlineMaterial = new Material(shader);
                SetColor(outlineMaterial, idleColor * 0.8f);
                outline.sharedMaterial = outlineMaterial;
            }
            const int segments = 72;
            outline.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2f * Mathf.PI / segments;
                outline.SetPosition(i, ring.center + ring.radius * (Mathf.Cos(angle) * ring.right + Mathf.Sin(angle) * ring.up));
            }
            UpdateVisibility();
            UpdateColors();
        }

        void UpdateVisibility()
        {
            if (root != null) root.gameObject.SetActive(placed && (showIdleTargets || IsRunning));
        }

        void UpdateColors()
        {
            if (!placed) return;
            bool flashing = Time.realtimeSinceStartup < flashUntil;
            for (int k = 0; k < spheres.Count && k < materials.Count; k++)
            {
                var color = flashing && k == flashIndex ? hitColor : k == lit && IsRunning ? litColor : idleColor;
                SetColor(materials[k], color);
            }
        }

        // 当たる範囲：判定に使う効果器の球を、同じ位置と大きさで描く。指先の球は半分が指の中にあるので、手や指に隠れないように手前に描く
        void UpdateProbeVisuals()
        {
            bool show = showProbes && placed && layout != null;
            if (!show)
            {
                if (probeRoot != null) probeRoot.gameObject.SetActive(false);
                return;
            }
            CollectProbes();
            if (probeRoot == null)
            {
                probeRoot = new GameObject("ReachProbes").transform;
                probeMaterial = CreateOverlayMaterial(ProbeColor, ProbeCenterAlpha);
            }
            probeRoot.gameObject.SetActive(true);
            while (probeSpheres.Count < probes.Count)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"ReachProbe_{probeSpheres.Count + 1}";
                Destroy(sphere.GetComponent<Collider>());
                sphere.transform.SetParent(probeRoot, false);
                var renderer = sphere.GetComponent<Renderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                probeSpheres.Add(sphere.transform);
            }
            for (int i = 0; i < probeSpheres.Count; i++)
            {
                bool used = i < probes.Count;
                var probe = used ? probes[i] : default;
                bool hand = used && (probe.kind == EffectorKind.HandLeft || probe.kind == EffectorKind.HandRight);
                // 前半（追加肢だけ）は本来の手では数えないので、手の球は出さない
                bool visible = used && !(hand && segment == 1);
                probeSpheres[i].gameObject.SetActive(visible);
                if (!visible) continue;
                probeSpheres[i].position = probe.point;
                probeSpheres[i].localScale = Vector3.one * (probe.radius * 2f);
                probeSpheres[i].GetComponent<Renderer>().sharedMaterial = probeMaterial;
            }
        }

        // 手前に描く半透明（Shaders/Resources/ExtraLimbOverlayUnlit）。見つからなければ URP の Unlit を半透明にする（指の中は隠れる）
        static Material CreateOverlayMaterial(Color color, float centerAlpha)
        {
            var overlay = Shader.Find("ExtraLimb/OverlayUnlit");
            if (overlay != null)
            {
                var onTop = new Material(overlay) { name = "ReachProbe" };
                SetColor(onTop, color);
                onTop.SetFloat("_CenterAlpha", Mathf.Clamp01(centerAlpha));
                return onTop;
            }
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = "ReachProbe" };
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            SetColor(material, color);
            return material;
        }

        static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
        }

        void Say(string text) => message = text ?? "";

        bool Fail(string text)
        {
            Say(text);
            return false;
        }

        static string Format(Vector3 v) => $"{v.x.ToString("F3", CultureInfo.InvariantCulture)}, {v.y.ToString("F3", CultureInfo.InvariantCulture)}, {v.z.ToString("F3", CultureInfo.InvariantCulture)}";
    }
}
