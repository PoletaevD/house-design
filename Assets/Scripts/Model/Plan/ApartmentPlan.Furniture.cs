using System.Collections.Generic;
using UnityEngine;

namespace HouseDesign
{
    internal sealed partial class ApartmentPlan
    {
        private const float WallClearance = .075f;

        private readonly List<PlacedItem> _items = new();

        internal IReadOnlyList<PlacedItem> Items => _items;

        internal Room CanPlace(CatalogEntry entry, Vector2 position, float angle, PlacedItem ignored = null)
        {
            var room = FindRoom(position);

            if (room == null || entry.Height > WallHeight)
            {
                return null;
            }

            var footprint = new FurnitureFootprint(position, entry.Width, entry.Depth, angle);

            if (!footprint.Fits(room.Bounds, WallClearance))
            {
                return null;
            }

            foreach (var item in _items)
            {
                if (item != ignored && item.Room == room && footprint.Intersects(Footprint(item)))
                {
                    return null;
                }
            }

            foreach (var opening in _openings)
            {
                if (!opening.IsDoor || opening.RoomId != room.Id && opening.NeighborId != room.Id)
                {
                    continue;
                }

                var wall = WallFor(opening);

                if (wall.HasValue && BlocksDoor(footprint, wall.Value, opening))
                {
                    return null;
                }
            }

            return room;
        }

        internal PlacedItem AddItem(CatalogEntry entry, Vector2 position, float angle)
        {
            var room = CanPlace(entry, position, angle);

            if (room == null)
            {
                return null;
            }

            var item = new PlacedItem(entry, room, position, angle);
            _items.Add(item);

            return item;
        }

        internal void RemoveItem(PlacedItem item)
        {
            _items.Remove(item);
        }

        private FurnitureFootprint Footprint(PlacedItem item)
        {
            return new FurnitureFootprint(item.Position, item.Entry.Width, item.Entry.Depth, item.Angle);
        }

        private bool BlocksDoor(FurnitureFootprint footprint, PlanWall wall, Opening opening)
        {
            var swing = opening.SwingDirection(wall);
            var hinge = wall.Point(opening.HingePosition(wall));
            var across = wall.Horizontal ? Vector2.right : Vector2.up;

            if (opening.HingeAtEnd)
            {
                across = -across;
            }

            var approach = new FurnitureFootprint(wall.Point(opening.Center(wall)) - swing * .25f, opening.Width, .5f, wall.Horizontal ? 0f : 90f);

            return footprint.Intersects(approach) || footprint.IntersectsSwing(hinge, across, swing, opening.Width);
        }
    }
}
