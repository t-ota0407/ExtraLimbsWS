using UnityEngine;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// TrackerMonitor（IMGUI）の見た目。実験者パネル（UI Toolkit）にそろえる：同じ字（Yu Gothic UI）、同じ色、角の丸い暗い箱。
    /// Unity の既定の灰色の箱は使わない。
    /// </summary>
    sealed class MonitorStyles
    {
        public const string DangerHex = "#E8665C";
        public const string CautionHex = "#E0B15C";

        static readonly string[] Families = { "Yu Gothic UI", "BIZ UDPGothic", "Meiryo UI", "Meiryo", "MS Gothic" };

        static readonly Color32 Text = new Color32(236, 234, 229, 255);
        static readonly Color32 Text2 = new Color32(163, 167, 174, 255);
        static readonly Color32 Panel = new Color32(20, 22, 26, 228);
        static readonly Color32 Control = new Color32(44, 48, 55, 255);
        static readonly Color32 ControlHover = new Color32(54, 59, 67, 255);
        static readonly Color32 ControlActive = new Color32(36, 39, 45, 255);

        static MonitorStyles instance;

        public readonly GUIStyle box, title, label, muted, button;

        public static MonitorStyles Get()
        {
            // 再生を止めるとテクスチャが消えることがあるので、そのときは作り直す
            if (instance == null || instance.box.normal.background == null) instance = new MonitorStyles();
            return instance;
        }

        MonitorStyles()
        {
            Font font = null;
            try
            {
                font = Font.CreateDynamicFontFromOSFont(Families, 13);
            }
            catch (System.Exception)
            {
                // 既定の字を使う
            }

            box = new GUIStyle
            {
                normal = { background = Rounded(Panel, 10) },
                border = new RectOffset(10, 10, 10, 10),
                padding = new RectOffset(16, 16, 12, 14),
            };
            label = new GUIStyle
            {
                font = font,
                fontSize = 13,
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Text },
                padding = new RectOffset(0, 0, 3, 3),
                margin = new RectOffset(0, 6, 0, 0),
            };
            muted = new GUIStyle(label) { normal = { textColor = Text2 } };
            title = new GUIStyle(label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false };
            button = new GUIStyle
            {
                font = font,
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = Rounded(Control, 6), textColor = Text },
                hover = { background = Rounded(ControlHover, 6), textColor = Text },
                active = { background = Rounded(ControlActive, 6), textColor = Text },
                border = new RectOffset(6, 6, 6, 6),
                padding = new RectOffset(12, 12, 6, 6),
                margin = new RectOffset(0, 6, 3, 3),
                fixedHeight = 28f,
            };
        }

        // 角の丸い四角（9 分割で伸ばす）
        static Texture2D Rounded(Color32 color, int radius)
        {
            int size = radius * 2 + 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Mathf.Clamp(px, radius, size - radius);
                    float cy = Mathf.Clamp(py, radius, size - radius);
                    float distance = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float coverage = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color32(color.r, color.g, color.b, (byte)(color.a * coverage));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
