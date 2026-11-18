using System;
using System.Collections.Generic;
using UnityEngine;

namespace HouseDesign
{
    [Serializable]
    internal sealed partial class ApartmentProject
    {
        // Unity uses the serialized field names as keys in .atelier.json.
        [SerializeField]
        private int version;

        [SerializeField]
        private float wallHeight;

        [SerializeField]
        private Lighting lighting;

        [SerializeField]
        private List<RoomData> rooms;

        [SerializeField]
        private List<OpeningData> openings;

        [SerializeField]
        private List<ItemData> items;

        [Serializable]
        internal sealed class Lighting
        {
#pragma warning disable IDE0044 // Unity requires writable fields for deserialization.
            [SerializeField]
            private float intensity;

            [SerializeField]
            private float warmth;

            [SerializeField]
            private float angle;

            [SerializeField]
            private bool cutaway;
#pragma warning restore IDE0044

            internal float Intensity => intensity;
            internal float Warmth => warmth;
            internal float Angle => angle;
            internal bool Cutaway => cutaway;

            internal Lighting(float intensity, float warmth, float angle, bool cutaway)
            {
                this.intensity = intensity;
                this.warmth = warmth;
                this.angle = angle;
                this.cutaway = cutaway;
            }
        }

        [Serializable]
        private sealed class RoomData
        {
            public int id;
            public string name;
            public Rect bounds;
            public int floor;
            public int walls;
            public int[] wallFinishes;
        }

        [Serializable]
        private sealed class OpeningData
        {
            public int room;
            public int neighbor;
            public WallSide side;
            public float offset;
            public float width;
            public float height;
            public float sill;
            public bool opensIntoNeighbor;
            public bool hingeAtEnd;
        }

        [Serializable]
        private sealed class ItemData
        {
            public string kind;
            public int room;
            public Vector2 position;
            public float angle;
        }
    }
}
