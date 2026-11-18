using System;
using UnityEngine;

namespace HouseDesign
{
    internal readonly struct FurnitureFootprint
    {
        private readonly Vector2 _center;
        private readonly Vector2 _right;
        private readonly Vector2 _forward;
        private readonly Vector2 _halfSize;

        internal FurnitureFootprint(Vector2 center, float width, float depth, float angle)
        {
            var radians = angle * Mathf.Deg2Rad;
            _center = center;
            _right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            _forward = new Vector2(-_right.y, _right.x);
            _halfSize = new Vector2(width, depth) / 2f;
        }

        internal Vector2 EdgeToward(Vector2 direction)
        {
            return _center + _right * (_halfSize.x * side(_right)) + _forward * (_halfSize.y * side(_forward));

            float side(Vector2 axis)
            {
                var alignment = Vector2.Dot(axis, direction);

                return Mathf.Abs(alignment) < .0001f ? 0f : Mathf.Sign(alignment);
            }
        }

        internal bool Fits(Rect bounds, float margin)
        {
            var x = Radius(Vector2.right) + margin;
            var y = Radius(Vector2.up) + margin;

            return _center.x - x >= bounds.xMin && _center.x + x <= bounds.xMax && _center.y - y >= bounds.yMin && _center.y + y <= bounds.yMax;
        }

        internal bool Intersects(FurnitureFootprint other)
        {
            var delta = other._center - _center;

            return Overlaps(other, delta, _right) && Overlaps(other, delta, _forward) && Overlaps(other, delta, other._right) && Overlaps(other, delta, other._forward);
        }

        internal bool IntersectsSwing(Vector2 hinge, Vector2 across, Vector2 swing, float radius)
        {
            // Clip the footprint to the swing quadrant before measuring its distance from the hinge.
            Span<Vector2> corners = stackalloc Vector2[8];
            Span<Vector2> clipped = stackalloc Vector2[8];
            var right = _right * _halfSize.x;
            var forward = _forward * _halfSize.y;
            var relative = _center - hinge;
            corners[0] = relative - right - forward;
            corners[1] = relative + right - forward;
            corners[2] = relative + right + forward;
            corners[3] = relative - right + forward;
            var count = Clip(corners[..4], clipped, across);
            count = Clip(clipped[..count], corners, swing);

            if (count == 0)
            {
                return false;
            }

            if (Mathf.Abs(Vector2.Dot(relative, _right)) <= _halfSize.x && Mathf.Abs(Vector2.Dot(relative, _forward)) <= _halfSize.y)
            {
                return true;
            }

            var limit = (radius - .001f) * (radius - .001f);
            var previous = corners[count - 1];

            foreach (var point in corners[..count])
            {
                var edge = point - previous;
                var closest = previous + edge * Mathf.Clamp01(-Vector2.Dot(previous, edge) / Mathf.Max(edge.sqrMagnitude, 1e-12f));

                if (closest.sqrMagnitude < limit)
                {
                    return true;
                }

                previous = point;
            }

            return false;
        }

        private int Clip(ReadOnlySpan<Vector2> points, Span<Vector2> output, Vector2 normal)
        {
            if (points.IsEmpty)
            {
                return 0;
            }

            var count = 0;
            var previous = points[^1];
            var previousDistance = Vector2.Dot(previous, normal);

            foreach (var point in points)
            {
                var distance = Vector2.Dot(point, normal);

                if ((previousDistance < 0f) != (distance < 0f))
                {
                    output[count++] = Vector2.LerpUnclamped(previous, point, previousDistance / (previousDistance - distance));
                }

                if (distance >= 0f)
                {
                    output[count++] = point;
                }

                previous = point;
                previousDistance = distance;
            }

            return count;
        }

        private bool Overlaps(FurnitureFootprint other, Vector2 delta, Vector2 axis)
        {
            return Mathf.Abs(Vector2.Dot(delta, axis)) < Radius(axis) + other.Radius(axis) - .001f;
        }

        private float Radius(Vector2 axis)
        {
            return Mathf.Abs(Vector2.Dot(_right, axis)) * _halfSize.x + Mathf.Abs(Vector2.Dot(_forward, axis)) * _halfSize.y;
        }
    }
}
