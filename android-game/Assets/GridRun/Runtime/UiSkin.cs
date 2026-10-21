using UnityEngine;
using UnityEngine.UI;

namespace GridRun
{
    // One small, generated nine-slice shape keeps the menus consistent without texture assets.
    public static class UiSkin
    {
        private static Sprite rounded;

        public static void Round(Image image)
        {
            if (rounded == null) rounded = MakeRounded();
            image.sprite = rounded;
            image.type = Image.Type.Sliced;
        }

        private static Sprite MakeRounded()
        {
            const int size = 32;
            const float radius = 8f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Grid Run rounded UI",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + .5f - Mathf.Clamp(x + .5f, radius, size - radius);
                    float dy = y + .5f - Mathf.Clamp(y + .5f, radius, size - radius);
                    float alpha = Mathf.Clamp01(radius + .5f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(9, 9, 9, 9));
        }
    }
}

