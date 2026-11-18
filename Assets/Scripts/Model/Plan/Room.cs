using System;
using UnityEngine;

namespace HouseDesign
{
    internal sealed class Room
    {
        private int[] _wallFinishes;

        internal int Id
        {
            get;
        }
        internal string Name
        {
            get;
            set;
        }
        internal Rect Bounds
        {
            get;
            set;
        }
        internal int Floor
        {
            get;
            set;
        }
        internal int Walls
        {
            get;
            set;
        }

        internal Room(int id, string name, Rect bounds)
        {
            Id = id;
            Name = name;
            Bounds = bounds;
        }

        internal int WallFinish(WallSide side)
        {
            var finish = _wallFinishes?[(int)side] ?? -1;

            return finish < 0 ? Walls : finish;
        }

        internal void PaintWall(WallSide side, int finish)
        {
            if (finish == Walls && _wallFinishes == null)
            {
                return;
            }

            _wallFinishes ??= new[]
            {
                -1,
                -1,
                -1,
                -1
            };

            _wallFinishes[(int)side] = finish == Walls ? -1 : finish;

            if (Array.TrueForAll(_wallFinishes, value => value < 0))
            {
                _wallFinishes = null;
            }
        }

        internal void PaintWalls(int finish)
        {
            Walls = finish;
            _wallFinishes = null;
        }

        internal int[] CopyWallFinishes()
        {
            return _wallFinishes?.Clone() as int[];
        }
    }
}
