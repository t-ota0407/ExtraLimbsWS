using System;
using System.Collections.Generic;
using ExtraLimb.Experiment;
using ExtraLimb.Limb;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtraLimb.UI
{
    /// <summary>
    /// 実験者のホットキー（Step 4-2、計画書の「パネルの項目とホットキー」）。割り当ては Input System のアクションのアセット
    /// （Resources/ExtraLimbUI/ExperimenterHotkeys.inputactions）にまとめてあり、アセットを書き換えればキーを変えられる。
    /// キーを押すと LimbSession・ExperimentRecorder などの公開のメソッドを呼び、変更の出どころを key:キー名 として記録に残す。
    /// 実験者パネルの文字の欄に入力している間は、ホットキーを無視する。
    /// G（予備の把持）は押している間だけ GraspRig.KeyPressed を立てる（GraspRig より前に動くよう、実行順を 10005 にしてある）。
    /// </summary>
    [DefaultExecutionOrder(10005)]
    public class HotkeyMap : MonoBehaviour
    {
        /// <summary>アセットを Resources から読むときのパス。</summary>
        public const string ResourcePath = "ExtraLimbUI/ExperimenterHotkeys";

        /// <summary>アクションの組の名前。</summary>
        public const string MapName = "Experimenter";

        /// <summary>アクションの名前と説明（パネルのホットキー一覧の順）。</summary>
        public static readonly (string action, string description)[] Descriptions =
        {
            ("Recenter", "Re-center（動かしている追加肢すべて）"),
            ("RecordForward", "前を記録（設定している追加肢）"),
            ("AttachPoint1", "生やす位置 1（肩の上）"),
            ("AttachPoint2", "生やす位置 2（脇腹）"),
            ("AttachPoint3", "生やす位置 3（頭部）"),
            ("InputSite1", "入力の部位 1（右足首）"),
            ("InputSite2", "入力の部位 2（右膝）"),
            ("InputSite3", "入力の部位 3（右肩）"),
            ("InputSite4", "入力の部位 4（右肘）"),
            ("NextMapping", "次のマッピングの型"),
            ("GainUp", "ゲインを 0.5 上げる"),
            ("GainDown", "ゲインを 0.5 下げる"),
            ("ToggleTwoLimbs", "2本目を出す・隠す"),
            ("SelectRight", "右の追加肢を設定する"),
            ("SelectLeft", "左の追加肢を設定する"),
            ("Grasp", "掴む（予備。押している間）"),
            ("ToggleRecording", "記録を止める・始める"),
            ("Marker", "マーカーを入れる"),
            ("SaveConfig", "今の設定を JSON に保存"),
            ("ToggleMirror", "鏡を出す・隠す"),
            ("TogglePanel", "実験者パネルを出す・隠す"),
        };

        [Tooltip("ホットキーのアセット。空なら Resources の ResourcePath から読む")]
        [SerializeField] InputActionAsset actions;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] LimbSession session;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] ExperimentRecorder recorder;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] AvatarMirror mirror;
        [Tooltip("空なら同じ GameObject かシーンから探す")]
        [SerializeField] ExperimenterPanel panel;

        InputActionAsset runtimeActions;
        InputActionMap map;
        InputAction graspAction;
        readonly List<(InputAction action, Action<InputAction.CallbackContext> handler)> handlers =
            new List<(InputAction, Action<InputAction.CallbackContext>)>();

        /// <summary>ホットキー一覧の1行。</summary>
        public readonly struct Entry
        {
            public readonly string action;
            public readonly string keys;
            public readonly string description;

            public Entry(string action, string keys, string description)
            {
                this.action = action;
                this.keys = keys;
                this.description = description;
            }
        }

        /// <summary>使っているアクションの組（なければ null）。</summary>
        public InputActionMap Map => map;

        /// <summary>今の割り当て（Descriptions の順）。</summary>
        public List<Entry> Entries()
        {
            var list = new List<Entry>();
            if (map == null) return list;
            foreach (var (name, description) in Descriptions)
            {
                var action = map.FindAction(name);
                if (action != null) list.Add(new Entry(name, action.GetBindingDisplayString(), description));
            }
            return list;
        }

        void OnEnable()
        {
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            if (recorder == null) recorder = FindFirstObjectByType<ExperimentRecorder>();
            if (mirror == null) mirror = FindFirstObjectByType<AvatarMirror>();
            if (panel == null) panel = GetComponent<ExperimenterPanel>();
            if (panel == null) panel = FindFirstObjectByType<ExperimenterPanel>();

            var source = actions != null ? actions : Resources.Load<InputActionAsset>(ResourcePath);
            if (source == null)
            {
                Debug.LogError($"[ExtraLimb] ホットキーのアセットが見つかりません（Resources/{ResourcePath}）", this);
                return;
            }
            // アセットそのものは書き換えないよう、複製を使う
            runtimeActions = Instantiate(source);
            map = runtimeActions.FindActionMap(MapName);
            if (map == null)
            {
                Debug.LogError($"[ExtraLimb] ホットキーのアセットに「{MapName}」の組がありません", this);
                return;
            }

            Bind("Recenter", _ => session?.RequestRecenter());
            Bind("RecordForward", _ => session?.RequestRecordForward());
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                Bind($"AttachPoint{i + 1}", _ => session?.SelectAttachPoint(index));
            }
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                Bind($"InputSite{i + 1}", _ =>
                {
                    if (session != null && index < session.InputSites.Count) session.SelectInputSite(session.InputSites[index]);
                });
            }
            Bind("NextMapping", _ => session?.CycleMappingPreset());
            Bind("GainUp", _ => ChangeGain(0.5f));
            Bind("GainDown", _ => ChangeGain(-0.5f));
            Bind("ToggleTwoLimbs", _ => session?.SetTwoLimbs(!session.TwoLimbs));
            Bind("SelectRight", _ => session?.SelectSide(LimbSide.Right));
            Bind("SelectLeft", _ => session?.SelectSide(LimbSide.Left));
            Bind("ToggleRecording", origin =>
            {
                if (recorder == null) return;
                if (recorder.IsRecording) recorder.StopRecording(origin);
                else recorder.StartRecording(origin);
            });
            Bind("Marker", origin => recorder?.InsertMarker(origin));
            Bind("SaveConfig", origin => recorder?.SaveCurrentConfig(origin));
            Bind("ToggleMirror", _ => ToggleMirror(mirror, session));
            Bind("TogglePanel", _ =>
            {
                if (panel != null) panel.Visible = !panel.Visible;
            });
            graspAction = map.FindAction("Grasp");
            map.Enable();
        }

        void OnDisable()
        {
            foreach (var (action, handler) in handlers) action.performed -= handler;
            handlers.Clear();
            if (session != null && session.Grasp != null) session.Grasp.KeyPressed = false;
            map?.Disable();
            map = null;
            graspAction = null;
            if (runtimeActions != null) Destroy(runtimeActions);
            runtimeActions = null;
        }

        void Update()
        {
            var grasp = session != null ? session.Grasp : null;
            if (grasp != null) grasp.KeyPressed = graspAction != null && graspAction.IsPressed() && !IsTyping;
        }

        bool IsTyping => panel != null && panel.IsTypingText;

        // 押したキーの名前を出どころにして呼ぶ
        void Bind(string name, Action<string> run)
        {
            var action = map.FindAction(name);
            if (action == null)
            {
                Debug.LogWarning($"[ExtraLimb] ホットキーのアセットに「{name}」がありません", this);
                return;
            }
            Action<InputAction.CallbackContext> handler = _ =>
            {
                if (IsTyping) return;
                string origin = ChangeOrigins.Key(action.GetBindingDisplayString());
                using (session != null ? session.WithOrigin(origin) : default)
                    run(origin);
            };
            action.performed += handler;
            handlers.Add((action, handler));
        }

        void ChangeGain(float delta)
        {
            var channel = session != null ? session.Selected : null;
            if (channel != null) session.SetPositionGain(channel.Config.positionGain + delta);
        }

        /// <summary>鏡を出す・隠す。記録にも残す。</summary>
        public static void ToggleMirror(AvatarMirror mirror, LimbSession session)
        {
            if (mirror == null) return;
            mirror.Visible = !mirror.Visible;
            session?.Report(SessionEventTypes.Mirror, "session", "visible", (!mirror.Visible) ? "true" : "false", mirror.Visible ? "true" : "false",
                mirror.Visible ? "鏡を出しました" : "鏡を隠しました");
        }
    }
}
