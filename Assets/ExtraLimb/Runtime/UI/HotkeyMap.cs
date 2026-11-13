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
    /// 実験モードでは、条件で決まる設定を変えるキー（生やす位置・入力の部位・型・ゲイン・2本目・左右の選択）は効かない。
    /// デモモードでは、Enter で今の設定のまま練習（リーチング課題）を始める。
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
            ("NextCondition", "次の条件へ"),
            ("PrevCondition", "前の条件へ"),
            ("StartTrial", "課題を開始"),
            ("AbortTrial", "課題を中止"),
            ("RedoCondition", "今の条件をやり直し"),
            ("Rating", "口頭評価を入力（上の項目から順に）"),
            ("Recenter", "Re-center"),
            ("RecordForward", "前の向きを記録"),
            ("AttachPoint1", "生やす位置：肩の上"),
            ("AttachPoint2", "生やす位置：脇腹"),
            ("AttachPoint3", "生やす位置：頭部"),
            ("InputSite1", "入力の部位：右足首"),
            ("InputSite2", "入力の部位：右膝"),
            ("InputSite3", "入力の部位：右肩"),
            ("InputSite4", "入力の部位：右肘"),
            ("NextMapping", "次のマッピングの型"),
            ("GainUp", "ゲイン ＋0.5"),
            ("GainDown", "ゲイン −0.5"),
            ("ToggleTwoLimbs", "2本目の追加肢の表示・非表示"),
            ("SelectRight", "右の追加肢を選択"),
            ("SelectLeft", "左の追加肢を選択"),
            ("Grasp", "掴む（予備。押している間）"),
            ("ToggleRecording", "記録の開始・停止"),
            ("Marker", "マーカーを追加"),
            ("SaveConfig", "今の設定を保存"),
            ("ToggleMirror", "鏡の表示・非表示"),
            ("NextView", "モニタの視点を切り替え"),
            ("TogglePanel", "パネルの表示・非表示"),
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
        [Tooltip("空ならシーンから探す")]
        [SerializeField] SpectatorCamera spectator;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] ConditionRunner runner;

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
            if (spectator == null) spectator = FindFirstObjectByType<SpectatorCamera>();
            if (runner == null) runner = FindFirstObjectByType<ConditionRunner>();

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
                Bind($"AttachPoint{i + 1}", Unlocked(_ => session?.SelectAttachPoint(index)));
            }
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                Bind($"InputSite{i + 1}", Unlocked(_ =>
                {
                    if (session != null && index < session.InputSites.Count) session.SelectInputSite(session.InputSites[index]);
                }));
            }
            Bind("NextMapping", Unlocked(_ => session?.CycleMappingPreset()));
            Bind("GainUp", Unlocked(_ => ChangeGain(0.5f)));
            Bind("GainDown", Unlocked(_ => ChangeGain(-0.5f)));
            Bind("ToggleTwoLimbs", Unlocked(_ => session?.SetTwoLimbs(!session.TwoLimbs)));
            Bind("SelectRight", Unlocked(_ => session?.SelectSide(LimbSide.Right)));
            Bind("SelectLeft", Unlocked(_ => session?.SelectSide(LimbSide.Left)));
            Bind("ToggleRecording", origin =>
            {
                if (recorder == null) return;
                if (recorder.IsRecording) recorder.StopRecording(origin);
                else recorder.StartRecording(origin);
            });
            Bind("Marker", origin => recorder?.InsertMarker(origin));
            Bind("SaveConfig", origin => recorder?.SaveCurrentConfig(origin));
            Bind("ToggleMirror", _ => ToggleMirror(mirror, session));
            Bind("NextView", _ => spectator?.NextView());
            Bind("TogglePanel", _ =>
            {
                if (panel != null) panel.Visible = !panel.Visible;
            });
            Bind("NextCondition", _ => runner?.Next());
            Bind("PrevCondition", _ => runner?.Previous());
            Bind("StartTrial", _ =>
            {
                if (runner == null) return;
                if (runner.IsExperimentMode) runner.StartTrial();
                else runner.StartPractice();
            });
            Bind("AbortTrial", _ => runner?.Abort());
            Bind("RedoCondition", _ => runner?.Redo());
            BindRating();
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

        // 実験モードでは、条件で決まる設定を変えるキーを効かせない（押したら知らせる）
        Action<string> Unlocked(Action<string> run) => origin =>
        {
            if (runner != null && runner.ConditionSettingsLocked)
            {
                runner.NotifyLocked();
                return;
            }
            run(origin);
        };

        // 押したキーの名前を出どころにして呼ぶ
        void Bind(string name, Action<string> run)
        {
            var action = map.FindAction(name);
            if (action == null)
            {
                Debug.LogWarning($"[ExtraLimb] ホットキーのアセットに「{name}」がありません", this);
                return;
            }
            // 割り当てが複数（Enter とテンキーの Enter など）なら、押したキーの名前を出どころにする
            bool alternatives = action.bindings.Count > 1 && !action.bindings[0].isComposite;
            Action<InputAction.CallbackContext> handler = context =>
            {
                if (IsTyping) return;
                string key = alternatives && context.control != null ? context.control.displayName : action.GetBindingDisplayString();
                string origin = ChangeOrigins.Key(key);
                using (session != null ? session.WithOrigin(origin) : default)
                    run(origin);
            };
            action.performed += handler;
            handlers.Add((action, handler));
        }

        // 口頭評価：押した数字（1〜7、テンキーも）を、評価の入力中の項目に入れる。
        // Ctrl+1〜4（入力の部位）を押したときにも数字の割り当てが反応するので、修飾キーを押している間は無視する
        void BindRating()
        {
            var action = map.FindAction("Rating");
            if (action == null)
            {
                Debug.LogWarning("[ExtraLimb] ホットキーのアセットに「Rating」がありません", this);
                return;
            }
            Action<InputAction.CallbackContext> handler = context =>
            {
                if (IsTyping || runner == null || ModifierPressed()) return;
                int value = RatingValue(context.control);
                if (value < 1) return;
                using (session != null ? session.WithOrigin(ChangeOrigins.Key(context.control.displayName)) : default)
                    runner.EnterRating(value);
            };
            action.performed += handler;
            handlers.Add((action, handler));
        }

        /// <summary>押したキーの数字（1〜9。数字のキーでなければ 0）。</summary>
        public static int RatingValue(InputControl control)
        {
            if (control == null) return 0;
            string name = control.name;
            char last = name.Length > 0 ? name[name.Length - 1] : ' ';
            return last >= '1' && last <= '9' ? last - '0' : 0;
        }

        static bool ModifierPressed()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.altKey.isPressed || keyboard.shiftKey.isPressed);
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
