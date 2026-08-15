using ColossalFramework.UI;
using UnityEngine;

namespace AIImprove
{
    // Backs the header banner and the pill-style toggle switches added for the Content Manager
    // page redesign (2026-08-15, "完全還原" per user's explicit choice for how closely to match
    // ACME/Advanced Stop Selection's UI style). Those mods' custom banners/switches are backed by
    // their own bundled texture files, which this project doesn't have and shouldn't copy - a
    // single 1x1 white pixel packed into its own UITextureAtlas, tinted per-component via the
    // standard UIComponent.color property, reproduces the same "solid colored rectangle" building
    // block without needing any external art asset. Same UITextureAtlas construction shape used by
    // UnifiedUILib's own TextureUtil.CreateTextureAtlas (confirmed via dnSpy on the installed
    // UnifiedUILib.dll) - ScriptableObject.CreateInstance<UITextureAtlas>(), a Material cloned from
    // the default UI atlas material, one SpriteInfo covering the full 0..1 UV region.
    internal static class SolidColorSprite
    {
        public const string SpriteName = "AIImproveSolid";

        private static UITextureAtlas atlas;

        public static UITextureAtlas Atlas
        {
            get
            {
                if (atlas == null)
                {
                    atlas = Build();
                }

                return atlas;
            }
        }

        private static UITextureAtlas Build()
        {
            Texture2D texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32 white = new Color32(255, 255, 255, 255);
            Color32[] pixels = new Color32[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = white;
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            UITextureAtlas newAtlas = ScriptableObject.CreateInstance<UITextureAtlas>();
            Material material = Object.Instantiate(UIView.GetAView().defaultAtlas.material);
            material.mainTexture = texture;
            newAtlas.material = material;
            newAtlas.name = "AIImproveSolidAtlas";

            newAtlas.AddSprite(new UITextureAtlas.SpriteInfo
            {
                name = SpriteName,
                texture = texture,
                region = new Rect(0f, 0f, 1f, 1f),
            });

            return newAtlas;
        }
    }
}
