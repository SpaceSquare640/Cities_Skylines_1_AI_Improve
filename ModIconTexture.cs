using UnityEngine;

namespace AIImprove
{
    // "我覺得需要處理一下快速 UI 沒有icon的問題" (2026-08-16): UnifiedUiIntegration.TryRegister
    // used to pass `null` for UUIAPI.Register's spritefile parameter - dnSpy against the player's
    // own UnifiedUILib.dll confirmed that overload's spritefile is a path to a PNG *on disk*
    // (ButtonBase.GetOrCreateAtlas -> TextureUtil.GetTextureFromFile when embeded=false), which
    // this project has never shipped, so the toolbar button rendered with no icon at all.
    //
    // Rather than start bundling a PNG (and the whole publish/download-path plumbing that would
    // need), this builds a small icon entirely at runtime - same "no external art assets" approach
    // already used for SolidColorSprite.cs - and hands it to UUIAPI's Texture2D overload instead
    // (ExternalButton.SetIcon(Texture2D), confirmed via dnSpy: adds the texture straight into
    // UUI's own shared atlas by name, no file path involved at all).
    //
    // A blocky pixel "A" (for AI_Improve) on the same accent-blue used by the settings page header,
    // rather than a stylized icon - simple enough to hand-author as a bitmap literal without any
    // drawing tooling, and still clearly distinguishable from every other mod's toolbar icon.
    internal static class ModIconTexture
    {
        private static Texture2D cached;

        // 5 columns x 7 rows, top to bottom. 'X' = foreground pixel.
        private static readonly string[] GlyphA =
        {
            ".XXX.",
            "X...X",
            "X...X",
            "XXXXX",
            "X...X",
            "X...X",
            "X...X",
        };

        public static Texture2D Get()
        {
            if (cached == null)
            {
                cached = Build();
            }

            return cached;
        }

        private static Texture2D Build()
        {
            const int cell = 4;
            const int size = 32;
            int glyphWidth = GlyphA[0].Length * cell;
            int glyphHeight = GlyphA.Length * cell;
            int marginX = (size - glyphWidth) / 2;
            int marginY = (size - glyphHeight) / 2;

            Color32 background = new Color32(58, 121, 187, 255); // matches SettingsPageUI.AccentColor
            Color32 foreground = Color.white;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "AIImproveToolbarIcon";
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = background;
            }

            for (int row = 0; row < GlyphA.Length; row++)
            {
                string line = GlyphA[row];
                for (int col = 0; col < line.Length; col++)
                {
                    if (line[col] != 'X')
                    {
                        continue;
                    }

                    // Texture2D pixel rows run bottom-to-top; the glyph literal above reads
                    // top-to-bottom, so row 0 (the glyph's top) needs to land near the top of the
                    // texture, i.e. a high y value.
                    int destY = size - marginY - (row * cell) - cell;
                    int destX = marginX + col * cell;

                    for (int dy = 0; dy < cell; dy++)
                    {
                        for (int dx = 0; dx < cell; dx++)
                        {
                            int x = destX + dx;
                            int y = destY + dy;
                            if (x >= 0 && x < size && y >= 0 && y < size)
                            {
                                pixels[y * size + x] = foreground;
                            }
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
