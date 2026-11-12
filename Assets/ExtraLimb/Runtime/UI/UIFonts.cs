using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ExtraLimb.UI
{
    /// <summary>
    /// UI Toolkit で日本語を出すためのフォント。UI Toolkit の既定のフォントには日本語の字形がないので、
    /// Windows に入っている日本語のフォントから、字を必要なときに作るフォント（DynamicOS）を作って使う。
    /// フォントのファイルはリポジトリに入れない（実験用PCは Windows なので、ここに挙げたどれかが入っている）。
    /// <para>
    /// 太字（USS の -unity-font-style: bold）には、同じファミリーの Semibold（なければ Bold）を使う。
    /// Yu Gothic UI は数字の幅がそろっているので、残り時間などが動いても字がぶれない。
    /// </para>
    /// </summary>
    public static class UIFonts
    {
        /// <summary>試す順。英数字が詰まりすぎず、UI 向けに作られたものから。</summary>
        public static readonly string[] JapaneseFamilies = { "Yu Gothic UI", "BIZ UDPGothic", "Meiryo UI", "Meiryo", "BIZ UDGothic", "MS Gothic" };

        /// <summary>太字に使う字の太さ（試す順）。</summary>
        static readonly string[] BoldStyles = { "Semibold", "SemiBold", "Bold" };

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
                    var asset = Create(family, "Regular");
                    if (asset == null) continue;
                    if (asset.HasCharacters("追加肢記録", out _, false, true))
                    {
                        asset.name = $"{family} (OS)";
                        AttachBold(asset, family);
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

        static FontAsset Create(string family, string style)
        {
            try
            {
                return FontAsset.CreateFontAsset(family, style);
            }
            catch (System.Exception)
            {
                return null; // そのフォントが入っていない
            }
        }

        // 太字（700）と Semibold（600）の字に、同じファミリーの太い字を割り当てる（なければ字を太らせて描く）
        static void AttachBold(FontAsset regular, string family)
        {
            var table = regular.fontWeightTable;
            if (table == null || table.Length < 8) return;
            foreach (var style in BoldStyles)
            {
                var bold = Create(family, style);
                if (bold == null) continue;
                bold.name = $"{family} {style} (OS)";
                table[6].regularTypeface = bold;
                table[7].regularTypeface = bold;
                return;
            }
        }
    }
}
