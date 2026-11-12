using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// キーボードで動かす仮想トラッカー一式（腰＋各部位）を TrackerRegistry に登録する。HMDやトラッカーなしで開発するために使う。
    /// 変位の基準に使う「部屋」（ID "room:Virtual"）も登録する。体全体の向きを変える（U/O）と、部屋基準では変位が出る。
    /// 実験時のホットキー（Space、P/V/H、G/T、F1〜F5 など）とぶつからないキーを使う。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class MockTrackerRig : MonoBehaviour, ITrackerProviderStatus
    {
        [SerializeField] TrackerRegistryHost registryHost;

        [Tooltip("仮想の腰の位置（ワールド座標、m）")]
        [SerializeField] Vector3 waistPosition = new Vector3(0f, 0.95f, 0f);

        [Tooltip("腰以外の仮想トラッカー。位置と向きは腰から見た値")]
        [SerializeField] List<MockTrackerSpec> trackers = new List<MockTrackerSpec>
        {
            new MockTrackerSpec(BodySite.RightAnkle, new Vector3(0.15f, -0.85f, 0.25f)),
            new MockTrackerSpec(BodySite.RightKnee, new Vector3(0.15f, -0.45f, 0.1f)),
            new MockTrackerSpec(BodySite.RightShoulder, new Vector3(0.18f, 0.45f, 0f)),
            new MockTrackerSpec(BodySite.RightElbow, new Vector3(0.22f, 0.2f, 0.08f)),
        };

        [Tooltip("部位を動かす速さ（m/s）。左Shiftを押している間は1/4")]
        [SerializeField] float moveSpeed = 0.2f;

        [Tooltip("部位をひねる速さ（度/s）")]
        [SerializeField] float twistSpeed = 60f;

        [Tooltip("体全体の向きを変える速さ（度/s）")]
        [SerializeField] float turnSpeed = 45f;

        [SerializeField] bool showHelp = true;

        TrackerRegistry registry;
        FixedPoseSource room;
        MockBody body;
        int selected;

        public MockBody Body => body;
        public string ProviderName => "仮想トラッカー";
        public string StatusText => body == null ? "停止中" : $"{body.Trackers.Count + 1}台（キーボードで操作）";
        public bool NeedsAttention => false;

        void OnEnable()
        {
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            if (registryHost == null)
            {
                Debug.LogError("[ExtraLimb] TrackerRegistryHost がシーンにないため、仮想トラッカーを登録できません");
                enabled = false;
                return;
            }
            registry = registryHost.Registry;
            body = new MockBody(new Pose(waistPosition, Quaternion.identity), trackers);
            room = FixedPoseSource.Room(PoseSpace.Virtual);
            registry.Register(room);
            registry.Register(body.Waist);
            foreach (var tracker in body.Trackers) registry.Register(tracker);
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, body.Trackers.Count - 1));
        }

        void OnDisable()
        {
            if (registry == null || body == null) return;
            registry.Unregister(body.Waist.Id);
            foreach (var tracker in body.Trackers) registry.Unregister(tracker.Id);
            if (room != null) registry.Unregister(room.Id);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || body == null || body.Trackers.Count == 0) return;
            if (keyboard.ctrlKey.isPressed || keyboard.altKey.isPressed) return;

            float dt = Time.unscaledDeltaTime;
            float scale = keyboard.leftShiftKey.isPressed ? 0.25f : 1f;

            if (keyboard.tabKey.wasPressedThisFrame) selected = (selected + 1) % body.Trackers.Count;
            if (keyboard.cKey.wasPressedThisFrame) body.Reset();
            if (keyboard.yKey.wasPressedThisFrame)
            {
                var tracker = body.Trackers[selected];
                tracker.IsTracking = !tracker.IsTracking;
            }

            var move = Vector3.zero;
            if (keyboard.wKey.isPressed) move.z += 1f;
            if (keyboard.sKey.isPressed) move.z -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed) move.x -= 1f;
            if (keyboard.eKey.isPressed) move.y += 1f;
            if (keyboard.qKey.isPressed) move.y -= 1f;
            if (move != Vector3.zero) body.Move(selected, move * (moveSpeed * scale * dt));

            float twist = 0f;
            if (keyboard.zKey.isPressed) twist += 1f;
            if (keyboard.xKey.isPressed) twist -= 1f;
            if (twist != 0f) body.Twist(selected, twist * twistSpeed * scale * dt);

            float turn = 0f;
            if (keyboard.oKey.isPressed) turn += 1f;
            if (keyboard.uKey.isPressed) turn -= 1f;
            if (turn != 0f) body.Turn(turn * turnSpeed * scale * dt);
        }

        void OnGUI()
        {
            if (!showHelp || body == null || body.Trackers.Count == 0) return;
            const float width = 430f, height = 150f;
            GUILayout.BeginArea(new Rect(10f, Screen.height - height - 10f, width, height), GUI.skin.box);
            var tracker = body.Trackers[selected];
            GUILayout.Label($"仮想トラッカーの操作　選択中：{BodySiteNames.Japanese(tracker.Site)}（{(tracker.IsTracking ? "追跡中" : "追跡なし")}）");
            GUILayout.Label("Tab：選ぶ部位を切替　W/S：前後　A/D：左右　Q/E：上下");
            GUILayout.Label("Z/X：ひねる（局所Z軸まわり）　Y：追跡切れをまねる");
            GUILayout.Label($"U/O：体全体の向きを変える（今 {body.BodyYaw:F0}°）　C：初期位置に戻す");
            GUILayout.Label("左Shift：ゆっくり動かす");
            GUILayout.EndArea();
        }
    }
}
