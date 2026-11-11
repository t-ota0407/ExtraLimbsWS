using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ExtraLimb.UI
{
    /// <summary>
    /// UI Toolkit で日本語を出すためのフォント（Step 4-2）。UI Toolkit の既定のフォントには日本語の字形がないので、
    /// Windows に入っている日本語のフォントから、字を必要なときに作るフォント（DynamicOS）を作って使う。
    /// フォントのファイルはリポジトリに入れない（実験用PCは Windows なので、ここに挙げたどれかが入っている）。
    /// </summary>
    public static class UIFonts
    {
        /// <summary>試す順。見やすさを優先して UD フォントから。</summary>
        public static readonly string[] JapaneseFamilies = { "BIZ UDGothic", "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic" };

        static FontAsset japanese;
        static bool searched;

        /// <summary>日本語のフォント（見つからなければ null）。</summary>
        public static FontAsset Japanese
        {
            get
            {
                if (searched) return japanese;
                searched = true;
                foreach (var family in JapaneseFamilies)
                {
                    FontAsset asset = null;
                    try
                    {
                        asset = FontAsset.CreateFontAsset(family, "Regular");
                    }
                    catch (System.Exception)
                    {
                        // そのフォントが入っていない
                    }
                    if (asset == null) continue;
                    if (asset.HasCharacters("追加肢記録", out _, false, true))
                    {
                        asset.name = $"{family} (OS)";
                        japanese = asset;
                        Debug.Log($"[ExtraLimb] 実験者パネルの字：{family}");
                        break;
                    }
                    Object.Destroy(asset);
                }
                if (japanese == null) Debug.LogWarning("[ExtraLimb] 日本語のフォントが見つかりません。実験者パネルの日本語が出ないことがあります");
                return japanese;
            }
        }

        /// <summary>要素（とその子）の字を日本語のフォントにする。</summary>
        public static void ApplyJapanese(VisualElement element)
        {
            var font = Japanese;
            if (element != null && font != null) element.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
        }
    }
}
