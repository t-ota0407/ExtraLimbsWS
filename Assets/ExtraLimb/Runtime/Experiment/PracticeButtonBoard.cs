using System.Collections.Generic;
using ExtraLimb.Limb;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 動作確認用の仮のボタン（Step 2-3）。Re-centerのたびと、Attach Point を付け替えたときに、追加肢の手の前にボタンを並べ直す。
    /// 黄色のボタンが押す対象で、追加肢の指先が触れると「押した」になり、緑に光って次の対象が黄色になる。
    /// 押した時間はコンソールに出す。Playgroundのタスク（Step 5）ができたら使わない。
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
            Debug.Log($"[ExtraLimb] 仮のボタンを{wristOffsets.Length}個並べました。黄色のボタンを追加肢の指先で押してください");
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
            GUILayout.BeginArea(new Rect(Screen.width - width - 10f, LimbSession.PanelHeight + 20f, width, 78f), GUI.skin.box);
            string target = targetIndex >= 0 && targetIndex < buttons.Count ? buttons[targetIndex].label : "—";
            GUILayout.Label($"仮のボタン：押した数 {hits}　直前 {lastSeconds:F2} 秒　平均 {(hits > 0 ? totalSeconds / hits : 0f):F2} 秒　対象「{target}」");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-centerして並べ直す")) session.RequestRecenter();
            if (GUILayout.Button("数を0に戻す"))
            {
                hits = 0;
                totalSeconds = lastSeconds = 0f;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
