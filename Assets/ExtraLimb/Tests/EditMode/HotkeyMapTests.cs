using System.Collections.Generic;
using System.Linq;
using ExtraLimb.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>実験者のホットキーのアセット（Step 4-2）。</summary>
    public class HotkeyMapTests
    {
        static InputActionMap LoadMap()
        {
            var asset = Resources.Load<InputActionAsset>(HotkeyMap.ResourcePath);
            Assert.That(asset, Is.Not.Null, $"Resources/{HotkeyMap.ResourcePath} がない");
            var map = asset.FindActionMap(HotkeyMap.MapName);
            Assert.That(map, Is.Not.Null, $"「{HotkeyMap.MapName}」の組がない");
            return map;
        }

        // 1つのアクションの割り当て（組み合わせは「修飾キー+キー」）
        static List<string> Keys(InputAction action)
        {
            var keys = new List<string>();
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.isPartOfComposite) continue;
                if (!binding.isComposite)
                {
                    keys.Add(binding.path.ToLowerInvariant());
                    continue;
                }
                var parts = new List<string>();
                for (int j = i + 1; j < bindings.Count && bindings[j].isPartOfComposite; j++) parts.Add(bindings[j].path.ToLowerInvariant());
                keys.Add(string.Join("+", parts));
            }
            return keys;
        }

        [Test]
        public void EveryDescribedAction_HasABinding()
        {
            var map = LoadMap();
            foreach (var (name, _) in HotkeyMap.Descriptions)
            {
                var action = map.FindAction(name);
                Assert.That(action, Is.Not.Null, name);
                Assert.That(Keys(action), Is.Not.Empty, name);
            }
        }

        [Test]
        public void EveryAction_IsDescribed()
        {
            var described = HotkeyMap.Descriptions.Select(d => d.action).ToList();
            foreach (var action in LoadMap().actions) Assert.That(described, Does.Contain(action.name));
        }

        [Test]
        public void Keys_DoNotOverlap()
        {
            var seen = new Dictionary<string, string>();
            foreach (var action in LoadMap().actions)
            foreach (var key in Keys(action))
            {
                Assert.That(seen.ContainsKey(key), Is.False, $"{key} が {action.name} と {(seen.TryGetValue(key, out var other) ? other : "")} で重なっている");
                seen[key] = action.name;
            }
        }

        [Test]
        public void Keys_DoNotUseVirtualTrackerKeys()
        {
            // DesktopDev の仮想トラッカー（MockTrackerRig）が使うキー
            var mockKeys = new[] { "w", "s", "a", "d", "q", "e", "z", "x", "u", "o", "tab", "y", "c" }.Select(k => $"<keyboard>/{k}").ToList();
            foreach (var action in LoadMap().actions)
            foreach (var key in Keys(action))
                Assert.That(mockKeys, Does.Not.Contain(key), $"{action.name} が仮想トラッカーのキー {key} を使っている");
        }

        [Test]
        public void InputSites_UseCtrlWithDigits()
        {
            var map = LoadMap();
            for (int i = 1; i <= 4; i++)
                Assert.That(Keys(map.FindAction($"InputSite{i}")), Does.Contain($"<keyboard>/ctrl+<keyboard>/{i}"));
        }
    }
}
