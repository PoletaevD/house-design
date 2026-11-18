using UnityEngine;

namespace HouseDesign
{
    internal sealed class Opening
    {
        internal int RoomId
        {
            get;
        }
        internal int NeighborId
        {
            get;
        }
        internal WallSide Side
        {
            get;
        }
        internal float Offset
        {
            get;
            set;
        }
        internal float Width
        {
            get;
            set;
        }
        internal float Height
        {
            get;
            set;
        }
        internal float Sill
        {
            get;
            set;
        }
        internal bool OpensIntoNeighbor
        {
            get;
            set;
        }

        internal bool HingeAtEnd
        {
            get;
            set;
        }

        internal bool IsDoor => NeighborId != 0;

        internal Opening(PlanWall wall, float center, bool isDoor)
        {
            RoomId = wall.Room.Id;
            NeighborId = wall.Neighbor?.Id ?? 0;
            Side = wall.Side;
            Offset = center - Origin(wall);
            Width = isDoor ? .9f : 1.4f;
            Height = isDoor ? 2.05f : 1.25f;
            Sill = isDoor ? 0f : .85f;
        }

        internal bool Matches(PlanWall wall)
        {
            return wall.Room.Id == RoomId && (wall.Neighbor?.Id ?? 0) == NeighborId && wall.Side == Side;
        }

        internal float Center(PlanWall wall)
        {
            return Origin(wall) + Offset;
        }

        internal Vector2 SwingDirection(PlanWall wall)
        {
            return OpensIntoNeighbor ? -wall.Inward : wall.Inward;
        }

        internal float HingePosition(PlanWall wall, float inset = 0f)
        {
            var direction = HingeAtEnd ? 1f : -1f;

            return Center(wall) + direction * (Width / 2f - inset);
        }

        private float Origin(PlanWall wall)
        {
            return wall.Horizontal ? wall.Room.Bounds.xMin : wall.Room.Bounds.yMin;
        }
    }

    internal enum OpeningError
    {
        None,
        Size,
        Height,
        Wall,
        Neighbor,
        Furniture
    }
}
