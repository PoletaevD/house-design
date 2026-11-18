using System;
using System.Collections.Generic;
using UnityEngine;

namespace HouseDesign
{
    internal sealed partial class ApartmentPlan
    {
        private const float Grid = .1f;
        internal const float MinimumRoomSize = 1.2f;
        internal const float MaximumRoomSize = 16f;
        private const float Epsilon = .001f;
        private const float DimensionEpsilon = .00001f;

        private readonly List<Room> _rooms = new();
        private readonly List<PlanWall> _walls = new();
        private int _nextRoomId = 1;

        internal IReadOnlyList<Room> Rooms => _rooms;
        internal IReadOnlyList<PlanWall> Walls => _walls;

        internal float WallHeight
        {
            get;
            private set;
        }

        internal Rect Bounds
        {
            get
            {
                if (_rooms.Count == 0)
                {
                    return new Rect(-4f, -3f, 8f, 6f);
                }

                var bounds = _rooms[0].Bounds;

                foreach (var room in _rooms)
                {
                    bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, room.Bounds.xMin), Mathf.Min(bounds.yMin, room.Bounds.yMin), Mathf.Max(bounds.xMax, room.Bounds.xMax), Mathf.Max(bounds.yMax, room.Bounds.yMax));
                }

                return bounds;
            }
        }

        internal ApartmentPlan()
        {
            WallHeight = 2.7f;
        }

        internal Room FindRoom(Vector2 point)
        {
            foreach (var room in _rooms)
            {
                if (room.Bounds.Contains(point))
                {
                    return room;
                }
            }

            return null;
        }

        internal bool CanFitRoom(Rect bounds, Room ignored = null)
        {
            if (bounds.width < MinimumRoomSize - Epsilon || bounds.height < MinimumRoomSize - Epsilon || bounds.width > MaximumRoomSize + Epsilon || bounds.height > MaximumRoomSize + Epsilon)
            {
                return false;
            }

            foreach (var room in _rooms)
            {
                if (room == ignored)
                {
                    continue;
                }

                var other = room.Bounds;

                if (Mathf.Min(bounds.xMax, other.xMax) - Mathf.Max(bounds.xMin, other.xMin) > Epsilon && Mathf.Min(bounds.yMax, other.yMax) - Mathf.Max(bounds.yMin, other.yMin) > Epsilon)
                {
                    return false;
                }
            }

            return true;
        }

        internal Room AddRoom(string name, Rect bounds, int? restoredId = null)
        {
            if (!CanFitRoom(bounds))
            {
                return null;
            }

            var room = new Room(restoredId ?? _nextRoomId, name, bounds);
            _rooms.Add(room);
            RebuildWalls();

            if (OpeningsFit())
            {
                _nextRoomId = Math.Max(_nextRoomId, room.Id + 1);
                return room;
            }

            _rooms.Remove(room);
            RebuildWalls();

            return null;
        }

        internal bool EditRoom(Room room, Rect bounds, bool resize = false)
        {
            if (!CanFitRoom(bounds, room))
            {
                return false;
            }

            var previous = room.Bounds;
            var movement = bounds.position - previous.position;
            var movedItems = new List<(PlacedItem Item, Vector2 Position)>();
            var movedOpenings = new List<(Opening Opening, float Offset)>();

            if (resize)
            {
                foreach (var opening in _openings)
                {
                    if (opening.RoomId == room.Id)
                    {
                        movedOpenings.Add((opening, opening.Offset));
                        opening.Offset -= opening.Side is WallSide.North or WallSide.South ? movement.x : movement.y;
                    }
                }
            }
            else
            {
                foreach (var item in _items)
                {
                    if (item.Room == room)
                    {
                        movedItems.Add((item, item.Position));
                        item.Position += movement;
                    }
                }
            }

            room.Bounds = bounds;
            RebuildWalls();

            var valid = OpeningsFit();

            foreach (var item in _items)
            {
                valid &= CanPlace(item.Entry, item.Position, item.Angle, item) == item.Room;
            }

            if (valid)
            {
                return true;
            }

            room.Bounds = previous;

            foreach (var (item, position) in movedItems)
            {
                item.Position = position;
            }

            foreach (var (opening, offset) in movedOpenings)
            {
                opening.Offset = offset;
            }

            RebuildWalls();

            return false;
        }

        internal void RemoveRoom(Room room)
        {
            _openings.RemoveAll(opening => opening.RoomId == room.Id || opening.NeighborId == room.Id);
            _items.RemoveAll(item => item.Room == room);
            _rooms.Remove(room);
            RebuildWalls();
        }

        internal void Reset()
        {
            _rooms.Clear();
            _openings.Clear();
            _items.Clear();
            _walls.Clear();
            _nextRoomId = 1;
            WallHeight = 2.7f;
        }

        internal void ReplaceWith(ApartmentPlan source)
        {
            Reset();
            _rooms.AddRange(source._rooms);
            _walls.AddRange(source._walls);
            _openings.AddRange(source._openings);
            _items.AddRange(source._items);
            _nextRoomId = source._nextRoomId;
            WallHeight = source.WallHeight;
        }

        internal bool SetWallHeight(float height)
        {
            foreach (var opening in _openings)
            {
                if (opening.Height + opening.Sill > height + DimensionEpsilon)
                {
                    return false;
                }
            }

            foreach (var item in _items)
            {
                if (item.Entry.Height > height)
                {
                    return false;
                }
            }

            WallHeight = height;

            return true;
        }

        internal float SnapCoordinate(float value, bool horizontal, float tolerance, Room ignored = null, float size = 0f)
        {
            var snapped = Mathf.Round(value / Grid) * Grid;
            var nearest = tolerance;

            foreach (var room in _rooms)
            {
                if (room == ignored)
                {
                    continue;
                }

                var bounds = room.Bounds;
                var minimum = horizontal ? bounds.xMin : bounds.yMin;
                var maximum = horizontal ? bounds.xMax : bounds.yMax;
                SnapEdge(value, minimum, ref nearest, ref snapped);
                SnapEdge(value, maximum, ref nearest, ref snapped);

                if (size > Epsilon)
                {
                    SnapEdge(value, minimum - size, ref nearest, ref snapped);
                    SnapEdge(value, maximum - size, ref nearest, ref snapped);
                }
            }

            return snapped;
        }

        internal Rect ResizeBounds(Room room, Rect start, Vector2Int corner, Vector2 target, float tolerance)
        {
            var handle = new Vector2(corner.x < 0 ? start.xMin : start.xMax, corner.y < 0 ? start.yMin : start.yMax);
            var anchor = new Vector2(corner.x < 0 ? start.xMax : start.xMin, corner.y < 0 ? start.yMax : start.yMin);
            var x = Mathf.Abs(target.x - handle.x) <= Epsilon ? handle.x : SnapCoordinate(target.x, true, tolerance, room);
            var y = Mathf.Abs(target.y - handle.y) <= Epsilon ? handle.y : SnapCoordinate(target.y, false, tolerance, room);
            var size = new Vector2(Mathf.Max(0f, (x - anchor.x) * corner.x), Mathf.Max(0f, (y - anchor.y) * corner.y));
            var end = anchor + Vector2.Scale(size, corner);

            return new Rect(Vector2.Min(anchor, end), size);
        }

        internal PlanWall? FindWall(Vector2 point, float tolerance, bool? interior = null)
        {
            PlanWall? result = null;

            foreach (var wall in _walls)
            {
                if (interior.HasValue && interior.Value != (wall.Neighbor != null))
                {
                    continue;
                }

                var closest = wall.Point(Mathf.Clamp(wall.Along(point), wall.Start, wall.End));
                var distance = Vector2.Distance(closest, point);

                if (distance < tolerance)
                {
                    tolerance = distance;
                    result = wall;
                }
            }

            return result;
        }

        private void SnapEdge(float value, float edge, ref float nearest, ref float snapped)
        {
            var distance = Mathf.Abs(value - edge);

            if (distance < nearest)
            {
                nearest = distance;
                snapped = edge;
            }
        }

        private void RebuildWalls()
        {
            _walls.Clear();

            foreach (var room in _rooms)
            {
                BuildSide(room, WallSide.North, room.Bounds.yMin, room.Bounds.xMin, room.Bounds.xMax);
                BuildSide(room, WallSide.East, room.Bounds.xMax, room.Bounds.yMin, room.Bounds.yMax);
                BuildSide(room, WallSide.South, room.Bounds.yMax, room.Bounds.xMin, room.Bounds.xMax);
                BuildSide(room, WallSide.West, room.Bounds.xMin, room.Bounds.yMin, room.Bounds.yMax);
            }
        }

        private void BuildSide(Room room, WallSide side, float line, float start, float end)
        {
            var neighbors = new List<(Room Room, float Start, float End)>();

            foreach (var other in _rooms)
            {
                if (other == room)
                {
                    continue;
                }

                var bounds = other.Bounds;
                var opposite = side switch
                {
                    WallSide.North => bounds.yMax,
                    WallSide.East => bounds.xMin,
                    WallSide.South => bounds.yMin,
                    _ => bounds.xMax
                };

                if (Mathf.Abs(opposite - line) > Epsilon)
                {
                    continue;
                }

                var horizontal = side is WallSide.North or WallSide.South;
                var overlapStart = Mathf.Max(start, horizontal ? bounds.xMin : bounds.yMin);
                var overlapEnd = Mathf.Min(end, horizontal ? bounds.xMax : bounds.yMax);

                if (overlapEnd - overlapStart > Epsilon)
                {
                    neighbors.Add((other, overlapStart, overlapEnd));
                }
            }

            neighbors.Sort((first, second) => first.Start.CompareTo(second.Start));
            var cursor = start;

            foreach (var neighbor in neighbors)
            {
                AddWall(room, null, side, cursor, neighbor.Start, line);

                if (room.Id < neighbor.Room.Id)
                {
                    AddWall(room, neighbor.Room, side, neighbor.Start, neighbor.End, line);
                }

                cursor = neighbor.End;
            }

            AddWall(room, null, side, cursor, end, line);
        }

        private void AddWall(Room room, Room neighbor, WallSide side, float start, float end, float line)
        {
            if (end - start > Epsilon)
            {
                _walls.Add(new PlanWall(room, neighbor, side, start, end, line));
            }
        }
    }
}
