using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HouseDesign
{
    internal sealed class StudioMaterials
    {
        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int s_Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int s_Metallic = Shader.PropertyToID("_Metallic");

        private readonly Material _template;
        private readonly List<Object> _ownedAssets = new();

        internal IReadOnlyList<Finish> Floors
        {
            get;
        }
        internal IReadOnlyList<Finish> Walls
        {
            get;
        }

        internal StudioMaterials(Material template)
        {
            _template = template;
            Floors = new[]
            {
                CreateFinish("Натуральный дуб", "#AD805B", "#C7A37D", Pattern.Wood, .30f),
                CreateFinish("Орех", "#654539", "#916B56", Pattern.Wood, .25f),
                CreateFinish("Светлый терраццо", "#C6B6A3", "#D6C8B8", Pattern.Terrazzo, .33f),
                CreateFinish("Керамика", "#6C7A82", "#89999D", Pattern.Tile, .42f)
            };
            Walls = new[]
            {
                CreateFinish("Мел", "#DED4C3", "#ECE5DA", Pattern.Plaster, .15f),
                CreateFinish("Лён", "#B29C85", "#CCB8A1", Pattern.Plaster, .15f),
                CreateFinish("Сланец", "#65757E", "#83939B", Pattern.Plaster, .19f),
                CreateFinish("Глиняный кирпич", "#A46654", "#C6896F", Pattern.Brick, .20f)
            };
        }

        internal Material ColorMaterial(string hex, float smoothness, float metallic = 0f)
        {
            var material = new Material(_template);
            material.SetColor(s_BaseColor, Hex(hex));
            material.SetFloat(s_Smoothness, smoothness);
            material.SetFloat(s_Metallic, metallic);
            _ownedAssets.Add(material);

            return material;
        }

        internal Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            return color;
        }

        internal void Dispose()
        {
            foreach (var asset in _ownedAssets)
            {
                Object.Destroy(asset);
            }
        }

        private Finish CreateFinish(string name, string lowHex, string highHex, Pattern pattern, float smoothness)
        {
            const int Size = 128;
            var low = Hex(lowHex);
            var high = Hex(highHex);
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            var pixels = new Color32[Size * Size];

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    pixels[y * Size + x] = Sample(pattern, low, high, x, y, Size);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            _ownedAssets.Add(texture);

            var material = ColorMaterial("#FFFFFF", smoothness);
            material.SetTexture(s_BaseMap, texture);

            return new Finish(name, high, texture, material, pattern == Pattern.Plaster ? .62f : 1.1f);
        }

        private Color Sample(Pattern pattern, Color low, Color high, int x, int y, int size)
        {
            var u = (float)x / size;
            var v = (float)y / size;
            var noise = Hash(x, y);
            var softNoise = Mathf.PerlinNoise(u * 8f, v * 8f);

            switch (pattern)
            {
                case Pattern.Wood:
                    var plank = y / 16;
                    var seam = (x + plank * 43) % size;

                    if (y % 16 == 0 || seam == 0)
                    {
                        return low * .80f;
                    }

                    var grain = Mathf.Sin(v * 220f + Mathf.Sin(u * Mathf.PI * 2f) * 2f) * .12f;

                    return Color.Lerp(low, high, .30f + Hash(plank, 2) * .5f + grain + noise * .10f);
                case Pattern.Tile:
                    if (x % 64 <= 1 || y % 64 <= 1)
                    {
                        return low * .58f;
                    }

                    return Color.Lerp(low, high, .24f + Hash(x / 64, y / 64) * .48f + softNoise * .18f);
                case Pattern.Brick:
                    var row = y / 16;
                    var shift = (x + row % 2 * 16) % size;

                    if (y % 16 <= 1 || shift % 32 <= 1)
                    {
                        return Color.Lerp(high, Color.white, .42f);
                    }

                    return Color.Lerp(low, high, Hash(shift / 32, row) * .68f + softNoise * .24f);
                case Pattern.Terrazzo:
                    var cellX = x / 8;
                    var cellY = y / 8;
                    var dx = x % 8 - Hash(cellX, cellY + 20) * 5f - 1f;
                    var dy = y % 8 - Hash(cellX + 20, cellY) * 5f - 1f;

                    if (dx * dx + dy * dy < 1f + Hash(cellX, cellY) * 7f)
                    {
                        return Color.Lerp(low * .61f, high * 1.1f, Hash(cellY, cellX));
                    }

                    return Color.Lerp(low, high, .62f + softNoise * .22f);
                default:
                    return Color.Lerp(low, high, .49f + (noise - .5f) * .28f + softNoise * .19f);
            }
        }

        private float Hash(int x, int y)
        {
            return Mathf.Repeat(Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.54f, 1f);
        }

        private enum Pattern
        {
            Wood,
            Terrazzo,
            Tile,
            Plaster,
            Brick
        }
    }

    internal sealed class Finish
    {
        internal string Name
        {
            get;
        }
        internal Color Color
        {
            get;
        }
        internal Texture2D Texture
        {
            get;
        }
        internal Material Material
        {
            get;
        }
        internal float TilesPerMeter
        {
            get;
        }

        internal Finish(string name, Color color, Texture2D texture, Material material, float tilesPerMeter)
        {
            Name = name;
            Color = color;
            Texture = texture;
            Material = material;
            TilesPerMeter = tilesPerMeter;
        }
    }
}
