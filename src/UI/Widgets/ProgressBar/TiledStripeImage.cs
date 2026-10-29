using UnityEngine;
using UnityEngine.UI;

namespace PrettyConvenientMenu.UI.Widgets.ProgressBar
{
    internal sealed class TiledStripeImage : RawImage
    {
        private const float TileSize = 16f;
        private static Texture2D? stripedTexture;

        public static Texture2D GetOrCreateStripedTexture()
        {
            if (stripedTexture != null)
            {
                return stripedTexture;
            }

            const int size = 16;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };

            // White stripes with transparent white gaps allow perfect tinting to any color (Orange, Cyan, etc.)
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 transparentWhite = new Color32(255, 255, 255, 0);

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isStripe = ((x + y) % 8) < 4;
                    pixels[y * size + x] = isStripe ? white : transparentWhite;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            stripedTexture = tex;
            return stripedTexture;
        }

        public static Texture2D GetOrCreateStripedOrangeTexture() => GetOrCreateStripedTexture();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            Rect r = GetPixelAdjustedRect();
            vh.Clear();

            float uMax = r.width > 0f ? r.width / TileSize : 1f;
            float vMax = r.height > 0f ? r.height / TileSize : 1f;

            UIVertex v = UIVertex.simpleVert;
            v.color = color;

            v.position = new Vector3(r.xMin, r.yMin, 0f);
            v.uv0 = new Vector2(0f, 0f);
            UIVertex v0 = v;

            v.position = new Vector3(r.xMin, r.yMax, 0f);
            v.uv0 = new Vector2(0f, vMax);
            UIVertex v1 = v;

            v.position = new Vector3(r.xMax, r.yMax, 0f);
            v.uv0 = new Vector2(uMax, vMax);
            UIVertex v2 = v;

            v.position = new Vector3(r.xMax, r.yMin, 0f);
            v.uv0 = new Vector2(uMax, 0f);
            UIVertex v3 = v;

            vh.AddUIVertexQuad(new UIVertex[] { v0, v1, v2, v3 });
        }
    }
}
