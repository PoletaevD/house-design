using UnityEngine;

namespace HouseDesign
{
    internal sealed class PlacedItem
    {
        internal CatalogEntry Entry
        {
            get;
        }
        internal Room Room
        {
            get;
            set;
        }
        internal Vector2 Position
        {
            get;
            set;
        }
        internal float Angle
        {
            get;
            set;
        }

        internal PlacedItem(CatalogEntry entry, Room room, Vector2 position, float angle)
        {
            Entry = entry;
            Room = room;
            Position = position;
            Angle = angle;
        }
    }
}
