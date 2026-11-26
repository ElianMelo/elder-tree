using UnityEngine.TextCore.Text;

namespace ElderTree.UI
{
    /// <summary>The serif used for headings across the HUD and menus (the first one installed), created once per run.</summary>
    public static class UIFonts
    {
        static FontAsset serif;
        static bool tried;

        public static FontAsset Serif
        {
            get
            {
                if (tried && serif != null) return serif;
                tried = true;
                foreach (var fam in new[] { "Cinzel", "Trajan Pro", "Palatino Linotype", "Book Antiqua", "Georgia", "Cambria", "Times New Roman" })
                {
                    try
                    {
                        serif = FontAsset.CreateFontAsset(fam, "Regular", 90);
                        if (serif != null) return serif;
                    }
                    catch { /* not installed on this OS */ }
                }
                return null;
            }
        }
    }
}
