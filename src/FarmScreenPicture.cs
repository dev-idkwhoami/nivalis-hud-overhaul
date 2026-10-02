using UnityEngine;

namespace NivalisMods.HudOverhaul;

internal static class FarmScreenPicture
{
    // One shared generated picture per produce icon and screen aspect; original assets stay untouched.
    private static readonly Dictionary<(int Icon, int Height), Material> Materials = new();
    internal static Material Get(Sprite? sprite, float aspect)
    {
        if (!float.IsFinite(aspect) || aspect <= 0) throw new InvalidOperationException("Invalid display aspect ratio.");
        var width = 512;
        var height = Mathf.Clamp(Mathf.RoundToInt(width / aspect), 64, 1024);
        var key = (sprite?.GetInstanceID() ?? 0, height);
        if (Materials.TryGetValue(key, out var cached)) return cached;
        var shader = Shader.Find("Unlit/Texture");
        if (shader == null) throw new InvalidOperationException("Unlit/Texture shader unavailable.");
        if (sprite == null)
        {
            var blank = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            blank.SetPixels32(new[] { new Color32(7, 10, 10, 255) });
            blank.Apply(false, true);
            var blankMaterial = new Material(shader) { mainTexture = blank, name = "HUD farm screen blank" };
            Materials.Add(key, blankMaterial);
            return blankMaterial;
        }
        if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None)
            throw new InvalidOperationException("Rotated atlas sprite needs an explicit crop transform.");
        var rect = sprite.textureRect;
        var sourceWidth = Mathf.RoundToInt(rect.width);
        var sourceHeight = Mathf.RoundToInt(rect.height);
        var temporary = RenderTexture.GetTemporary(sourceWidth, sourceHeight, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D? readback = null, picture = null;
        Material? material = null;
        try
        {
            var texture = sprite.texture;
            Graphics.Blit(texture, temporary, new Vector2(rect.width / texture.width, rect.height / texture.height),
                new Vector2(rect.x / texture.width, rect.y / texture.height));
            RenderTexture.active = temporary;
            readback = new Texture2D(sourceWidth, sourceHeight, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, sourceWidth, sourceHeight), 0, 0);
            readback.Apply();
            var source = readback.GetPixels32();
            var pixels = new Color32[width * height];
            var background = new Color32(7, 10, 10, 255);
            Array.Fill(pixels, background);
            var fit = Mathf.Min(width * 0.8f / sourceWidth, height * 0.8f / sourceHeight);
            var drawWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * fit));
            var drawHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * fit));
            var left = (width - drawWidth) / 2;
            var bottom = (height - drawHeight) / 2;
            for (var y = 0; y < drawHeight; y++)
                for (var x = 0; x < drawWidth; x++)
                {
                    var color = source[(y * sourceHeight / drawHeight) * sourceWidth + x * sourceWidth / drawWidth];
                    var alpha = color.a;
                    pixels[(bottom + y) * width + left + x] = new Color32(
                        (byte)((color.r * alpha + background.r * (255 - alpha)) / 255),
                        (byte)((color.g * alpha + background.g * (255 - alpha)) / 255),
                        (byte)((color.b * alpha + background.b * (255 - alpha)) / 255), 255);
                }
            picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            picture.name = "HUD farm screen produce";
            picture.SetPixels32(pixels);
            picture.Apply(false, true);
            material = new Material(shader);
            material.name = "HUD farm screen opaque picture";
            material.mainTexture = picture;
            // Unity's Unlit/Texture pass uses default backface culling and depth
            // testing. The picture is on the actual screen mesh, not a UI overlay.
            Materials.Add(key, material);
            return material;
        }
        catch
        {
            if (picture != null) UnityEngine.Object.Destroy(picture);
            if (material != null) UnityEngine.Object.Destroy(material);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            if (readback != null) UnityEngine.Object.Destroy(readback);
        }
    }
}
