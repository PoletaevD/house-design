using System.Collections.Generic;
using UnityEngine;

namespace HouseDesign
{
    internal sealed partial class ApartmentPlan
    {
        private const float OpeningMargin = .12f;

        private readonly List<Opening> _openings = new();

        internal IReadOnlyList<Opening> Openings => _openings;

        internal Opening FindOpening(PlanWall wall, float position)
        {
            return _openings.Find(opening => BelongsTo(opening, wall) && Mathf.Abs(opening.Center(wall) - position) <= opening.Width / 2f);
        }

        internal bool BelongsTo(Opening opening, PlanWall wall)
        {
            if (!opening.Matches(wall))
            {
                return false;
            }

            var center = opening.Center(wall);

            return center - opening.Width / 2f >= wall.Start + OpeningMargin - Epsilon && center + opening.Width / 2f <= wall.End - OpeningMargin + Epsilon;
        }

        internal PlanWall? WallFor(Opening opening)
        {
            foreach (var wall in _walls)
            {
                if (BelongsTo(opening, wall))
                {
                    return wall;
                }
            }

            return null;
        }

        internal Opening PreviewOpening(PlanWall wall, Vector2 point, bool isDoor)
        {
            var opening = new Opening(wall, wall.Along(point), isDoor);
            opening.Width = Mathf.Min(opening.Width, wall.Length - OpeningMargin * 2f);
            opening.Height = Mathf.Min(opening.Height, WallHeight - opening.Sill);
            var half = opening.Width / 2f + OpeningMargin;
            var center = Mathf.Clamp(Mathf.Round(wall.Along(point) / Grid) * Grid, wall.Start + half, wall.End - half);
            opening.Offset += center - wall.Along(point);

            return opening;
        }

        internal bool CanAddOpening(PlanWall wall, Opening opening, Opening ignored = null)
        {
            return CheckOpening(wall, opening, ignored) == OpeningError.None;
        }

        private OpeningError CheckOpening(PlanWall wall, Opening opening, Opening ignored)
        {
            if (!(opening.Width >= MinimumOpeningWidth(opening) - DimensionEpsilon && opening.Height >= (opening.IsDoor ? 1.8f : .3f) - DimensionEpsilon))
            {
                return OpeningError.Size;
            }

            if (!(opening.Sill >= 0f && (!opening.IsDoor || opening.Sill == 0f) && opening.Height + opening.Sill <= WallHeight + DimensionEpsilon))
            {
                return OpeningError.Height;
            }

            if (!BelongsTo(opening, wall))
            {
                return OpeningError.Wall;
            }

            var center = opening.Center(wall);

            foreach (var other in _openings)
            {
                if (other != ignored && BelongsTo(other, wall) && Mathf.Abs(center - other.Center(wall)) < (opening.Width + other.Width) / 2f + OpeningMargin - Epsilon)
                {
                    return OpeningError.Neighbor;
                }
            }

            if (opening.IsDoor)
            {
                foreach (var item in _items)
                {
                    if ((item.Room == wall.Room || item.Room == wall.Neighbor) && BlocksDoor(Footprint(item), wall, opening))
                    {
                        return OpeningError.Furniture;
                    }
                }
            }

            return OpeningError.None;
        }

        internal (float Start, float End) OpeningSpan(PlanWall wall, Opening opening)
        {
            var start = wall.Start + OpeningMargin;
            var end = wall.End - OpeningMargin;
            var center = opening.Center(wall);

            foreach (var other in _openings)
            {
                if (other == opening || !BelongsTo(other, wall))
                {
                    continue;
                }

                var neighbor = other.Center(wall);

                if (neighbor < center)
                {
                    start = Mathf.Max(start, neighbor + other.Width / 2f + OpeningMargin);
                }
                else
                {
                    end = Mathf.Min(end, neighbor - other.Width / 2f - OpeningMargin);
                }
            }

            return (start, end);
        }

        internal float MinimumOpeningWidth(Opening opening)
        {
            return opening.IsDoor ? .6f : .35f;
        }

        internal Opening OpeningAt(PlanWall wall, Opening opening, float center, float width)
        {
            return new Opening(wall, center, opening.IsDoor)
            {
                Width = width,
                Height = opening.Height,
                Sill = opening.Sill,
                HingeAtEnd = opening.HingeAtEnd,
                OpensIntoNeighbor = opening.OpensIntoNeighbor
            };
        }

        internal bool AddOpening(PlanWall wall, Opening opening)
        {
            if (!CanAddOpening(wall, opening))
            {
                return false;
            }

            _openings.Add(opening);

            return true;
        }

        internal OpeningError UpdateOpening(Opening opening, float offset, float width, float height, float sill)
        {
            var oldWall = WallFor(opening);

            if (!oldWall.HasValue)
            {
                return OpeningError.Wall;
            }

            var previous = (opening.Offset, opening.Width, opening.Height, opening.Sill);
            (opening.Offset, opening.Width, opening.Height, opening.Sill) = (offset, width, height, sill);
            var result = CheckOpening(WallFor(opening) ?? oldWall.Value, opening, opening);

            if (result != OpeningError.None)
            {
                (opening.Offset, opening.Width, opening.Height, opening.Sill) = previous;
            }

            return result;
        }

        internal void RemoveOpening(Opening opening)
        {
            _openings.Remove(opening);
        }

        internal bool SetDoorSwing(Opening opening, bool opensIntoNeighbor, bool hingeAtEnd)
        {
            var wall = WallFor(opening);

            if (!opening.IsDoor || !wall.HasValue)
            {
                return false;
            }

            var previous = (opening.OpensIntoNeighbor, opening.HingeAtEnd);
            opening.OpensIntoNeighbor = opensIntoNeighbor;
            opening.HingeAtEnd = hingeAtEnd;

            if (!CanAddOpening(wall.Value, opening, opening))
            {
                (opening.OpensIntoNeighbor, opening.HingeAtEnd) = previous;
                return false;
            }

            return true;
        }

        private bool OpeningsFit()
        {
            foreach (var opening in _openings)
            {
                var wall = WallFor(opening);

                if (!wall.HasValue || !CanAddOpening(wall.Value, opening, opening))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
