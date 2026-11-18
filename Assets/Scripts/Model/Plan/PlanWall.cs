using UnityEngine;

namespace HouseDesign
{
    internal enum WallSide
    {
        North,
        East,
        South,
        West
    }

    internal readonly struct PlanWall
    {
        internal Room Room
        {
            get;
        }
        internal Room Neighbor
        {
            get;
        }
        internal WallSide Side
        {
            get;
        }
        internal float Start
        {
            get;
        }
        internal float End
        {
            get;
        }
        private float Line
        {
            get;
        }
        internal bool Horizontal => Side is WallSide.North or WallSide.South;
        internal float Length => End - Start;
        internal WallSide OppositeSide => (WallSide)(((int)Side + 2) % 4);
        internal Vector2 Inward => Side switch
        {
            WallSide.North => Vector2.up,
            WallSide.East => Vector2.left,
            WallSide.South => Vector2.down,
            _ => Vector2.right
        };

        internal PlanWall(Room room, Room neighbor, WallSide side, float start, float end, float line)
        {
            Room = room;
            Neighbor = neighbor;
            Side = side;
            Start = start;
            End = end;
            Line = line;
        }

        internal Vector2 Point(float distance)
        {
            return Horizontal ? new Vector2(distance, Line) : new Vector2(Line, distance);
        }

        internal float Along(Vector2 point)
        {
            return Horizontal ? point.x : point.y;
        }
    }
}
