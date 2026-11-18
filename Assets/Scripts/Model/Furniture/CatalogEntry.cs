namespace HouseDesign
{
    internal enum FurnitureKind
    {
        Sofa,
        Armchair,
        Table,
        Bed,
        Wardrobe,
        Desk,
        Refrigerator,
        Washer,
        Television,
        Plant,
        Lamp,
        Bathtub,
        Sink,
        KitchenSet
    }

    internal enum CatalogCategory
    {
        Furniture,
        Appliances,
        Decor,
        Plumbing
    }

    internal sealed class CatalogEntry
    {
        internal FurnitureKind Kind
        {
            get;
        }
        internal CatalogCategory Category
        {
            get;
        }
        internal string Name
        {
            get;
        }
        internal float Width
        {
            get;
        }
        internal float Depth
        {
            get;
        }
        internal float Height
        {
            get;
        }

        internal CatalogEntry(FurnitureKind kind, CatalogCategory category, string name, float width, float depth, float height)
        {
            Kind = kind;
            Category = category;
            Name = name;
            Width = width;
            Depth = depth;
            Height = height;
        }
    }
}
