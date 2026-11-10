using System.Collections.Generic;
using ExtraLimb.Limb;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 動作確認用の仮のボタン（Step 2-3）。Re-centerのたびと、Attach Point を付け替えたときに、追加肢の手の前にボタンを並べ直す。
    /// 黄色のボタンが押す対象で、追加肢の指先が触れると「押した」になり、緑に光って次の対象が黄色になる。
    /// 押した時間はコンソールに出す。Playgroundのタスク（Step 5）ができたら使わない。
    /// 把持の確認用に、ボタンの下に小さな台と立方体も置く（Step 3-4）。立方体は追加肢（右トリガー）でも本来の手（グリップ）でも掴め、
    /// 放すと落ちる。「立方体を戻す」かRe-centerで台の上に戻る。
    /// </summary>
    [DefaultExecutionOrder(10020)]
    public class PracticeButtonBoard : MonoBehaviour
    {
        [Tooltip("空ならシーンから探す")]
        [SerializeField] LimbSession session;

        [Tooltip("ボタンを置く場所：手首を基準位置からこれだけ動かしたときの指先のあたり（根元の向き、x は外向きが正、m）")]
        [SerializeField] Vector3[] wristOffsets =
        {
            new Vector3(-0.15f, 0f, 0.05f),
            new Vector3(0f, 0.13f, 0.05f),
            new Vector3(0.15f, 0f, 0.05f),
            new Vector3(0f, 0f, 0.16f),
        };

        [SerializeField] float buttonSize = 0.06f;

        [Header("把持の確認用の立方体")]
        [Tooltip("立方体を置く場所：手首を基準位置からこれだけ動かしたときの、手のひらのあたり（根元の向き、x は外向きが正、m）")]
        [SerializeField] Vector3[] cubeWristOffsets =
        {
            new Vector3(-0.08f, -0.12f, 0.10f),
            new Vector3(0.10f, -0.12f, 0.10f),
        };
        [SerializeField] float cubeSize = 0.05f;
        [Tooltip("指先がボタンの中心からこの距離（m）に入ったら押したとする")]
        [SerializeField] float pressRadius = 0.05f;
        [SerializeField] bool showGui = true;

        static readonly Color IdleColor = new Color(0.55f, 0.55f, 0.6f);
        static readonly Color TargetColor = new Color(1f, 0.85f, 0.2f);
        static readonly Color TouchColor = new Color(0.7f, 0.85f, 1f);
        static readonly Color PressedColor = new Color(0.3f, 1f, 0.4f);
        const float FlashSeconds = 0.4f;

        class Button
        {
            public Transform transform;
            public Material material;
            public string label;
        }

        readonly List<Button> buttons = new List<Button>();
        readonly List<Graspable> cubes = new List<Graspable>();
        readonly List<Transform> pedestals = new List<Transform>();
        readonly List<Vector3> cubeHomes = new List<Vector3>();
        int grabs;
        Transform contactMarker;
        int targetIndex = -1;
        float targetShownAt, flashUntil;
        int flashIndex = -1;
        int hits;
        float lastSeconds, totalSeconds;
        readonly System.Random random = new System.Random();

        void OnEnable()
        {
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            if (session == null)
            {
                Debug.LogError("[ExtraLimb] LimbSession がシーンにないため、仮のボタンを出せません", this);
                return;
            }
            session.Recentered += Layout;
            session.MountChanged += Layout;
            session.LimbSolved += Check;
        }

        void OnDisable()
        {
            if (session == null) return;
            session.Recentered -= Layout;
            session.MountChanged -= Layout;
            session.LimbSolved -= Check;
        }

        /// <summary>今の追加肢の位置に合わせて、ボタンを並べ直す（Re-centerと付け替えの直後に呼ばれる）。</summary>
        public void Layout()
        {
            var limb = session.Limb;
            if (limb == null || !limb.IsReady) return;

            // 腕を解いた直後なので、手首から指先までのずれ（根元の座標）を測れる
            var tip = limb.WorldToFrame(limb.ContactPoint) - limb.WorldToFrame(limb.Hand.position);
            var basePosition = session.Pipeline.Law.BasePosition;
            var workspace = session.Pipeline.Workspace;
            float maxTipReach = limb.Reach + tip.magnitude;

            for (int i = 0; i < wristOffsets.Length; i++)
            {
                var offset = limb.Mirror(wristOffsets[i]);
                // 手首がそこへ届くように、届く範囲の少し内側に収める
                var wrist = basePosition + offset;
                if (workspace != null)
                {
                    wrist = workspace.Clamp(wrist);
                    var fromCenter = wrist - workspace.Center;
                    float limit = workspace.MaxRadius * 0.97f;
                    if (fromCenter.magnitude > limit) wrist = workspace.Center + fromCenter.normalized * limit;
                }
                var local = wrist + tip;
                var button = i < buttons.Count ? buttons[i] : CreateButton(i);
                button.label = Label(offset);
                button.transform.position = limb.FrameToWorld(local);
                if (local.magnitude > maxTipReach * 0.98f)
                    Debug.LogWarning($"[ExtraLimb] 仮のボタン「{button.label}」は追加肢が届かない位置かもしれません（根元から {local.magnitude * 100f:F0} cm）");
            }
            ChooseNextTarget();
            LayoutCubes(limb, tip, basePosition, workspace);
            Debug.Log($"[ExtraLimb] 仮のボタンを{wristOffsets.Length}個並べました。黄色のボタンを追加肢の指先で押してください");
        }

        // 立方体の置き場所：手首がそこへ届くとき、手のひら（手首と指先の中間）が来るところ。台の上に載せる
        void LayoutCubes(ExtraLimbRig limb, Vector3 tip, Vector3 basePosition, ExtraLimb.Core.WorkspaceLimiter workspace)
        {
            cubeHomes.Clear();
            for (int i = 0; i < cubeWristOffsets.Length; i++)
            {
                var wrist = basePosition + limb.Mirror(cubeWristOffsets[i]);
                if (workspace != null)
                {
                    wrist = workspace.Clamp(wrist);
                    var fromCenter = wrist - workspace.Center;
                    float limit = workspace.MaxRadius * 0.95f;
                    if (fromCenter.magnitude > limit) wrist = workspace.Center + fromCenter.normalized * limit;
                }
                var home = limb.FrameToWorld(wrist + tip * 0.5f);
                cubeHomes.Add(home);
                if (i >= cubes.Count) CreateCube(i);
                pedestals[i].position = home + Vector3.down * (cubeSize * 0.5f + 0.006f);
            }
            ResetCubes();
        }

        /// <summary>掴まれていない立方体を台の上に戻す。</summary>
        public void ResetCubes()
        {
            for (int i = 0; i < cubes.Count && i < cubeHomes.Count; i++)
            {
                var cube = cubes[i];
                if (cube == null || cube.IsHeld) continue;
                cube.transform.SetPositionAndRotation(cubeHomes[i], Quaternion.identity);
                var body = cube.Body;
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
        }

        void CreateCube(int index)
        {
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = $"PracticeCube_Pedestal_{index}";
            pedestal.transform.SetParent(transform, false);
            pedestal.transform.localScale = new Vector3(cubeSize * 1.8f, 0.012f, cubeSize * 1.8f);
            var pedestalMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            SetColor(pedestalMaterial, new Color(0.35f, 0.35f, 0.4f));
            pedestal.GetComponent<Renderer>().material = pedestalMaterial;
            pedestals.Add(pedestal.transform);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"PracticeCube_{index}";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * cubeSize;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            SetColor(material, index % 2 == 0 ? new Color(0.95f, 0.45f, 0.3f) : new Color(0.35f, 0.6f, 1f));
            go.GetComponent<Renderer>().material = material;
            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.1f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var graspable = go.AddComponent<Graspable>();
            graspable.Grabbed += _ => grabs++;
            cubes.Add(graspable);
        }

        static string Label(Vector3 offset)
        {
            var a = new Vector3(Mathf.Abs(offset.x), Mathf.Abs(offset.y), Mathf.Abs(offset.z));
            if (a.x >= a.y && a.x >= a.z) return offset.x > 0f ? "右" : "左";
            if (a.y >= a.z) return offset.y > 0f ? "上" : "下";
            return offset.z > 0f ? "前" : "後";
        }

        void ChooseNextTarget()
        {
            if (buttons.Count == 0) return;
            int next = random.Next(buttons.Count);
            if (buttons.Count > 1 && next == targetIndex) next = (next + 1 + random.Next(buttons.Count - 1)) % buttons.Count;
            targetIndex = next;
            targetShownAt = Time.time;
        }

        void Check()
        {
            var limb = session.Limb;
            if (limb == null || !limb.IsReady || buttons.Count == 0) return;
            var contact = limb.ContactPoint;
            if (contactMarker == null) contactMarker = CreateSphere("PracticeButton_Contact", 0.015f, Color.white).transform;
            contactMarker.position = contact;

            for (int i = 0; i < buttons.Count; i++)
            {
                bool touching = Vector3.Distance(contact, buttons[i].transform.position) <= pressRadius;
                if (touching && i == targetIndex && session.IsInputValid)
                {
                    lastSeconds = Time.time - targetShownAt;
                    totalSeconds += lastSeconds;
                    hits++;
                    flashIndex = i;
                    flashUntil = Time.time + FlashSeconds;
                    Debug.Log($"[ExtraLimb] 仮のボタン「{buttons[i].label}」を押しました：{lastSeconds:F2} 秒（{hits}回目）");
                    ChooseNextTarget();
                }

                Color color;
                if (i == flashIndex && Time.time < flashUntil) color = PressedColor;
                else if (i == targetIndex) color = TargetColor;
                else if (touching) color = TouchColor;
                else color = IdleColor;
                SetColor(buttons[i].material, color);
            }
        }

        Button CreateButton(int index)
        {
            var go = CreateSphere($"PracticeButton_{index}", buttonSize, IdleColor);
            var button = new Button { transform = go.transform, material = go.GetComponent<Renderer>().material };
            buttons.Add(button);
            return button;
        }

        GameObject CreateSphere(string objectName, float size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = objectName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            go.GetComponent<Renderer>().material = material;
            SetColor(material, color);
            return go;
        }

        static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
        }

        void OnGUI()
        {
            if (!showGui || session == null) return;
            const float width = 420f;
            GUILayout.BeginArea(new Rect(Screen.width - width - 10f, LimbSession.PanelHeight + 20f, width, 66f), GUI.skin.box);
            string target = targetIndex >= 0 && targetIndex < buttons.Count ? buttons[targetIndex].label : "—";
            GUILayout.Label($"仮のボタン：押した数 {hits}　直前 {lastSeconds:F2} 秒　対象「{target}」　立方体を掴んだ数 {grabs}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-centerして並べ直す")) session.RequestRecenter();
            if (GUILayout.Button("立方体を戻す")) ResetCubes();
            if (GUILayout.Button("数を0に戻す"))
            {
                hits = grabs = 0;
                totalSeconds = lastSeconds = 0f;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
