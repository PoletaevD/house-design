using System;
using System.Collections.Generic;
using UnityEngine;

namespace HouseDesign
{
    internal sealed class FurnitureCatalog
    {
        private readonly Material _wood;
        private readonly Material _oak;
        private readonly Material _fabric;
        private readonly Material _lightFabric;
        private readonly Material _clay;
        private readonly Material _metal;
        private readonly Material _white;
        private readonly Material _screen;
        private readonly Material _leaves;
        private readonly Material _ceramic;
        private readonly Material _basin;
        private readonly Material _chrome;

        internal IReadOnlyList<CatalogEntry> Entries
        {
            get;
        }

        internal FurnitureCatalog(StudioMaterials materials)
        {
            Entries = new[]
            {
                new CatalogEntry(FurnitureKind.Sofa, CatalogCategory.Furniture, "Диван Como", 2.25f, .92f, .86f),
                new CatalogEntry(FurnitureKind.Armchair, CatalogCategory.Furniture, "Кресло Alba", .83f, .80f, .88f),
                new CatalogEntry(FurnitureKind.Table, CatalogCategory.Furniture, "Стол Porto", 1.58f, .85f, .76f),
                new CatalogEntry(FurnitureKind.Bed, CatalogCategory.Furniture, "Кровать Nido", 1.78f, 2.15f, 1.03f),
                new CatalogEntry(FurnitureKind.Wardrobe, CatalogCategory.Furniture, "Шкаф Linea", 1.54f, .58f, 2.12f),
                new CatalogEntry(FurnitureKind.Desk, CatalogCategory.Furniture, "Письменный стол", 1.30f, .62f, .76f),
                new CatalogEntry(FurnitureKind.KitchenSet, CatalogCategory.Furniture, "Кухонный гарнитур", 2.70f, .64f, 2.06f),
                new CatalogEntry(FurnitureKind.Refrigerator, CatalogCategory.Appliances, "Холодильник", .72f, .70f, 1.94f),
                new CatalogEntry(FurnitureKind.Washer, CatalogCategory.Appliances, "Стиральная машина", .62f, .65f, .87f),
                new CatalogEntry(FurnitureKind.Television, CatalogCategory.Appliances, "ТВ с тумбой", 1.35f, .42f, 1.23f),
                new CatalogEntry(FurnitureKind.Plant, CatalogCategory.Decor, "Фикус в кашпо", .58f, .58f, 1.24f),
                new CatalogEntry(FurnitureKind.Lamp, CatalogCategory.Decor, "Торшер Arco", .46f, .46f, 1.57f),
                new CatalogEntry(FurnitureKind.Bathtub, CatalogCategory.Plumbing, "Ванна", 1.75f, .80f, .66f),
                new CatalogEntry(FurnitureKind.Sink, CatalogCategory.Plumbing, "Раковина с тумбой", .72f, .52f, 1.02f)
            };

            _wood = materials.ColorMaterial("#665044", .25f);
            _oak = materials.ColorMaterial("#B38D63", .23f);
            _fabric = materials.ColorMaterial("#8D5748", .09f);
            _lightFabric = materials.ColorMaterial("#E8DCC7", .09f);
            _clay = materials.ColorMaterial("#B78057", .16f);
            _metal = materials.ColorMaterial("#403A3B", .46f, .35f);
            _white = materials.ColorMaterial("#EEE6DA", .37f);
            _screen = materials.ColorMaterial("#3B4C5C", .55f, .24f);
            _leaves = materials.ColorMaterial("#657453", .25f);
            _ceramic = materials.ColorMaterial("#FAF4E9", .67f);
            _basin = materials.ColorMaterial("#CAD8D6", .64f);
            _chrome = materials.ColorMaterial("#9B9D9F", .70f, .48f);
        }

        internal Transform CreateModel(StudioGeometry geometry, CatalogEntry entry, Transform parent, Material overrideMaterial = null)
        {
            var root = new GameObject(entry.Name).transform;
            root.SetParent(parent, false);
            root.gameObject.layer = parent.gameObject.layer;

            void box(Material material, float x, float y, float z, float width, float height, float depth)
            {
                geometry.RoundedBox(root, new Vector3(x, y, z), new Vector3(width, height, depth), overrideMaterial ?? material);
            }

            void round(Material material, float x, float y, float z, float width, float height, float depth)
            {
                geometry.Cylinder(root, new Vector3(x, y, z), new Vector3(width, height, depth), overrideMaterial ?? material);
            }

            void legs(float height, float width, float x, float z, Material material)
            {
                box(material, -x, height / 2f, -z, width, height, width);
                box(material, x, height / 2f, -z, width, height, width);
                box(material, -x, height / 2f, z, width, height, width);
                box(material, x, height / 2f, z, width, height, width);
            }

            void faucet(float x, float y, float z, float height)
            {
                round(_chrome, x, y + height / 2f, z, .033f, height, .085f);
                box(_chrome, x, y + height, z - .07f, .038f, .032f, .19f);
            }

            switch (entry.Kind)
            {
                case FurnitureKind.Sofa:
                    legs(.12f, .055f, .42f, .38f, _wood);
                    box(_fabric, 0f, .26f, 0f, 1f, .31f, .97f);
                    box(_fabric, 0f, .65f, .39f, .97f, .64f, .20f);
                    box(_fabric, -.445f, .45f, -.02f, .11f, .42f, .94f);
                    box(_fabric, .445f, .45f, -.02f, .11f, .42f, .94f);

                    for (var index = -1; index <= 1; index++)
                    {
                        box(_fabric, .258f * index, .48f, -.105f, .25f, .16f, .66f);
                        box(_fabric, .258f * index, .72f, .274f, .25f, .48f, .21f);
                    }

                    box(_lightFabric, -.30f, .65f, .12f, .15f, .20f, .16f);
                    box(_clay, .32f, .66f, .12f, .14f, .22f, .15f);
                    break;
                case FurnitureKind.Armchair:
                    legs(.23f, .065f, .34f, .33f, _wood);
                    box(_lightFabric, 0f, .34f, -.04f, .90f, .23f, .92f);
                    box(_lightFabric, 0f, .68f, .36f, .86f, .64f, .25f);
                    box(_oak, -.44f, .53f, -.035f, .10f, .09f, .89f);
                    box(_oak, .44f, .53f, -.035f, .10f, .09f, .89f);
                    box(_lightFabric, 0f, .46f, -.08f, .76f, .17f, .78f);
                    break;
                case FurnitureKind.Table:
                    legs(.91f, .08f, .39f, .34f, _wood);
                    box(_oak, 0f, .94f, 0f, 1f, .12f, 1f);
                    box(_wood, 0f, .83f, 0f, .85f, .12f, .75f);
                    break;
                case FurnitureKind.Bed:
                    legs(.12f, .065f, .37f, .37f, _wood);
                    box(_wood, 0f, .21f, -.02f, 1f, .24f, .95f);
                    box(_lightFabric, 0f, .44f, .46f, 1f, .88f, .08f);
                    box(_lightFabric, 0f, .40f, -.035f, .97f, .23f, .93f);
                    box(_fabric, 0f, .53f, -.20f, .98f, .10f, .58f);
                    box(_clay, 0f, .59f, -.30f, .99f, .055f, .20f);
                    box(_lightFabric, -.24f, .56f, .30f, .40f, .13f, .19f);
                    box(_lightFabric, .24f, .56f, .30f, .40f, .13f, .19f);
                    break;
                case FurnitureKind.Wardrobe:
                    legs(.045f, .075f, .40f, .35f, _metal);
                    box(_wood, 0f, .52f, .025f, 1f, .96f, .90f);

                    for (var index = -1; index <= 1; index++)
                    {
                        box(_oak, .315f * index, .52f, -.453f, .309f, .92f, .09f);
                        box(_metal, .315f * index + .085f, .55f, -.495f, .018f, .18f, .025f);
                    }

                    break;
                case FurnitureKind.Desk:
                    legs(.91f, .07f, .42f, .35f, _metal);
                    box(_oak, 0f, .94f, 0f, 1f, .12f, 1f);
                    box(_wood, .27f, .76f, -.035f, .41f, .25f, .83f);
                    box(_metal, .27f, .76f, -.46f, .13f, .04f, .033f);
                    break;
                case FurnitureKind.Refrigerator:
                    box(_white, 0f, .50f, .035f, .96f, 1f, .93f);
                    box(_white, 0f, .66f, -.435f, 1f, .66f, .13f);
                    box(_white, 0f, .15f, -.435f, 1f, .33f, .13f);
                    box(_metal, -.34f, .59f, -.495f, .065f, .24f, .018f);
                    box(_metal, -.34f, .22f, -.495f, .065f, .13f, .018f);
                    break;
                case FurnitureKind.Washer:
                    box(_white, 0f, .50f, .025f, 1f, 1f, .95f);
                    var ring = geometry.Cylinder(root, new Vector3(0f, .43f, -.447f), new Vector3(.70f, .099f, .52f), overrideMaterial ?? _metal);
                    ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    var glass = geometry.Cylinder(root, new Vector3(0f, .43f, -.486f), new Vector3(.57f, .028f, .42f), overrideMaterial ?? _screen);
                    glass.localRotation = ring.localRotation;
                    box(_screen, -.25f, .86f, -.46f, .29f, .085f, .057f);
                    box(_metal, .30f, .86f, -.46f, .12f, .095f, .072f);
                    break;
                case FurnitureKind.Television:
                    legs(.11f, .05f, .42f, .36f, _metal);
                    box(_oak, 0f, .22f, 0f, 1f, .26f, 1f);
                    box(_wood, .26f, .22f, -.49f, .43f, .20f, .023f);
                    box(_wood, -.26f, .22f, -.49f, .43f, .20f, .023f);
                    box(_metal, 0f, .435f, .08f, .27f, .18f, .18f);
                    box(_metal, 0f, .73f, .14f, .94f, .54f, .13f);
                    box(_screen, 0f, .73f, .063f, .90f, .495f, .04f);
                    break;
                case FurnitureKind.Plant:
                    round(_clay, 0f, .14f, 0f, .59f, .28f, .59f);
                    round(_clay, 0f, .26f, 0f, .69f, .065f, .69f);
                    round(_wood, 0f, .29f, 0f, .55f, .026f, .55f);
                    round(_wood, 0f, .54f, 0f, .065f, .54f, .065f);

                    for (var index = 0; index < 11; index++)
                    {
                        var angle = index * 137.5f;
                        var direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                        var spread = .19f + .075f * Mathf.Sin(index * 1.3f);
                        var position = direction * spread + Vector3.up * (.49f + index * .043f);
                        var leaf = geometry.Sphere(root, position, new Vector3(.26f, .075f, .47f), overrideMaterial ?? _leaves);
                        leaf.localRotation = Quaternion.Euler(-18f + index * 2f, angle, 0f);
                    }

                    break;
                case FurnitureKind.Lamp:
                    round(_metal, 0f, .026f, 0f, .85f, .052f, .85f);
                    round(_metal, 0f, .48f, 0f, .085f, .91f, .085f);
                    round(_lightFabric, 0f, .885f, 0f, 1f, .23f, 1f);
                    round(_clay, 0f, .78f, 0f, .92f, .025f, .92f);
                    break;
                case FurnitureKind.Bathtub:
                    box(_ceramic, 0f, .16f, 0f, .98f, .30f, .96f);
                    box(_basin, 0f, .32f, 0f, .82f, .05f, .75f);
                    box(_ceramic, 0f, .47f, -.43f, 1f, .46f, .14f);
                    box(_ceramic, 0f, .47f, .43f, 1f, .46f, .14f);
                    box(_ceramic, -.44f, .47f, 0f, .12f, .46f, .80f);
                    box(_ceramic, .43f, .47f, 0f, .14f, .46f, .80f);
                    box(_ceramic, .39f, .70f, .31f, .18f, .06f, .35f);
                    faucet(.39f, .73f, .33f, .22f);
                    round(_chrome, .30f, .35f, 0f, .024f, .011f, .055f);
                    break;
                case FurnitureKind.Sink:
                    legs(.095f, .075f, .37f, .34f, _metal);
                    box(_oak, 0f, .405f, .025f, .93f, .63f, .88f);

                    for (var index = -1; index <= 1; index += 2)
                    {
                        box(_wood, index * .23f, .40f, -.445f, .45f, .59f, .09f);
                        box(_chrome, index * .08f, .53f, -.495f, .07f, .025f, .025f);
                        box(_ceramic, index * .425f, .775f, -.025f, .15f, .12f, .95f);
                    }

                    box(_ceramic, 0f, .775f, -.43f, .82f, .12f, .14f);
                    box(_ceramic, 0f, .775f, .34f, .82f, .12f, .32f);
                    box(_basin, 0f, .728f, -.055f, .73f, .031f, .66f);
                    round(_chrome, 0f, .75f, -.06f, .065f, .014f, .092f);
                    faucet(0f, .83f, .32f, .14f);
                    break;
                case FurnitureKind.KitchenSet:
                    box(_wood, 0f, .025f, .05f, .96f, .05f, .84f);
                    box(_oak, 0f, .23f, .045f, 1f, .36f, .89f);

                    for (var index = 0; index < 4; index++)
                    {
                        var x = -.375f + index * .25f;
                        box(index == 2 ? _screen : _oak, x, .23f, -.445f, .244f, .34f, .09f);
                        box(_metal, x, .35f, -.49f, .11f, .013f, .022f);
                    }

                    box(_screen, 0f, .425f, 0f, 1f, .031f, 1f);
                    box(_chrome, -.327f, .446f, -.045f, .25f, .012f, .69f);
                    box(_basin, -.327f, .453f, -.083f, .178f, .009f, .45f);
                    faucet(-.327f, .452f, .25f, .075f);
                    box(_metal, .125f, .447f, -.02f, .20f, .015f, .78f);

                    for (var index = -1; index <= 1; index += 2)
                    {
                        round(_screen, .125f - .046f, .457f, index * .185f, .055f, .009f, .22f);
                        round(_screen, .125f + .046f, .457f, index * .185f, .055f, .009f, .22f);
                    }

                    for (var index = 0; index < 3; index++)
                    {
                        var x = -.348f + index * .248f;
                        box(_wood, x, .85f, .27f, .242f, .30f, .45f);
                        box(_white, x, .85f, .031f, .235f, .285f, .047f);
                        box(_metal, x + .080f, .755f, .003f, .013f, .043f, .021f);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry));
            }

            root.localScale = new Vector3(entry.Width, entry.Height, entry.Depth);

            return root;
        }
    }
}
