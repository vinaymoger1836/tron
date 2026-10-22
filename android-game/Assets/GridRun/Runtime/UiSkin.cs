using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GridRun
{
    // Shared Grid interface style: dark glass frames with chamfered corners, a thin luminous
    // edge, and a soft inner glow. All sprites are generated, so no texture assets are needed.
    public static class UiSkin
    {
        public static readonly Color Text = new Color(.86f, .97f, 1f);
        public static readonly Color Accent = new Color(.35f, .9f, 1f);
        public static readonly Color Muted = new Color(.55f, .72f, .8f);

        private static Sprite rounded;
        private static readonly Dictionary<string, Sprite> frames = new Dictionary<string, Sprite>();
        private static Sprite scrim;

        public static void Round(Image image)
        {
            if (rounded == null) rounded = MakeRounded();
            image.sprite = rounded;
            image.type = Image.Type.Sliced;
        }

        // Interactive control. Primary marks the main action of a screen.
        public static void Button(Button button, Image image, bool primary = false)
        {
            image.sprite = primary
                ? Frame("primary", new Color(.03f, .2f, .27f, .94f), new Color(.45f, .95f, 1f), 3)
                : Frame("button", new Color(.012f, .06f, .085f, .9f), new Color(.2f, .72f, .88f), 2);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(.84f, .88f, .92f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.62f, .92f, 1f);
            colors.disabledColor = new Color(.45f, .45f, .45f, .55f);
            colors.fadeDuration = .08f;
            button.colors = colors;
        }

        public static void Panel(Image image)
        {
            image.sprite = Frame("panel", new Color(.004f, .018f, .03f, .97f), new Color(.14f, .55f, .7f), 2);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }

        public static void Field(Image image)
        {
            image.sprite = Frame("field", new Color(.01f, .045f, .07f, .98f), new Color(.16f, .5f, .62f), 2);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }

        // Left-to-right darkening behind menu text over the live 3D scene.
        public static void Scrim(Image image)
        {
            if (scrim == null)
            {
                const int width = 256;
                var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
                {
                    name = "Grid Run scrim", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                };
                for (int x = 0; x < width; x++)
                {
                    float t = x / (width - 1f);
                    float alpha = Mathf.Lerp(.9f, 0f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.35f, 1f, t)));
                    texture.SetPixel(x, 0, new Color(.002f, .01f, .02f, alpha));
                }
                texture.Apply(false, true);
                scrim = Sprite.Create(texture, new Rect(0, 0, width, 1), new Vector2(.5f, .5f));
            }
            image.sprite = scrim;
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private static Sprite Frame(string key, Color fill, Color edge, float edgeWidth)
        {
            if (frames.TryGetValue(key, out var cached)) return cached;
            const int size = 64;
            const float chamfer = 12f, border = 18f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Grid Run frame " + key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            float diagonal = 1f / Mathf.Sqrt(2);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + .5f, py = y + .5f, qx = size - px, qy = size - py;
                    // Signed distance inside a square with 45-degree cut corners.
                    float d = Mathf.Min(Mathf.Min(px, py), Mathf.Min(qx, qy));
                    d = Mathf.Min(d, Mathf.Min((px + py - chamfer) * diagonal, (qx + py - chamfer) * diagonal));
                    d = Mathf.Min(d, Mathf.Min((px + qy - chamfer) * diagonal, (qx + qy - chamfer) * diagonal));
                    float coverage = Mathf.Clamp01(d + .5f);
                    float line = Mathf.Clamp01(edgeWidth + .5f - d);
                    float glow = d > edgeWidth ? Mathf.Exp(-(d - edgeWidth) / 3.5f) * .32f : 0;
                    Color color = Color.Lerp(fill, edge, line) + edge * glow * (1 - line);
                    color.a = coverage * Mathf.Clamp01(Mathf.Lerp(fill.a, 1, line) + glow);
                    pixels[y * size + x] = color;
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            frames[key] = sprite;
            return sprite;
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
