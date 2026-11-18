using UnityEngine;
using UnityEngine.UIElements;

namespace HouseDesign
{
    internal sealed class ModelSelectionCanvas : VisualElement
    {
        private static readonly Color s_Shadow = new Color32(45, 34, 39, 225);
        private static readonly Color s_RoomColor = new Color32(255, 186, 117, 255);
        private static readonly Color s_ItemColor = new Color32(152, 251, 230, 255);
        private static readonly Vector2[] s_Directions =
        {
            Vector2.left,
            Vector2.right,
            Vector2.down,
            Vector2.up
        };
        private readonly (Vector2 From, Vector2 To, float Gap)[] _passages = new (Vector2, Vector2, float)[4];
        private readonly Label[] _passageLabels = new Label[4];

        private readonly ApartmentStage _stage;
        private Room _room;
        private PlacedItem _item;
        private Opening _opening;
        private bool _showPassages;

        internal ModelSelectionCanvas(ApartmentStage stage)
        {
            _stage = stage;
            pickingMode = PickingMode.Ignore;

            for (var index = 0; index < _passageLabels.Length; index++)
            {
                var label = new Label
                {
                    pickingMode = PickingMode.Ignore
                };
                label.AddToClassList("measurement-value");
                label.style.display = DisplayStyle.None;
                _passageLabels[index] = label;
                Add(label);
            }

            RegisterCallback<GeometryChangedEvent>(_ => RefreshProjection());
            generateVisualContent += Draw;
        }

        internal void SetSelection(Room room, PlacedItem item, Opening opening)
        {
            _room = room;
            _item = item;
            _opening = opening;
            RefreshProjection();
        }

        internal void ShowPassages(Room room, CatalogEntry entry, Vector2 position, float angle)
        {
            _showPassages = room != null;

            if (_showPassages)
            {
                var footprint = new FurnitureFootprint(position, entry.Width, entry.Depth, angle);
                var bounds = room.Bounds;
                var inset = ApartmentStage.WallThickness / 2f;

                for (var index = 0; index < s_Directions.Length; index++)
                {
                    var from = footprint.EdgeToward(s_Directions[index]);
                    var to = index switch
                    {
                        0 => new Vector2(bounds.xMin + inset, from.y),
                        1 => new Vector2(bounds.xMax - inset, from.y),
                        2 => new Vector2(from.x, bounds.yMin + inset),
                        _ => new Vector2(from.x, bounds.yMax - inset)
                    };
                    _passages[index] = (from, to, Vector2.Dot(to - from, s_Directions[index]));
                }
            }

            RefreshProjection();
        }

        internal void RefreshProjection()
        {
            style.display = _showPassages || _room != null || _item != null || _opening != null ? DisplayStyle.Flex : DisplayStyle.None;

            for (var index = 0; index < _passages.Length; index++)
            {
                var label = _passageLabels[index];
                label.style.display = _showPassages ? DisplayStyle.Flex : DisplayStyle.None;

                if (_showPassages)
                {
                    var (from, to, gap) = _passages[index];
                    var middle = ToLocal(_stage.ProjectFloor((from + to) / 2f));
                    var marker = ToLocal(_stage.ProjectFloor(from));
                    middle += (middle - marker).normalized * 20f;
                    label.text = $"{Mathf.Max(0f, gap):0.00} м";
                    label.style.color = GapColor(gap);
                    label.style.left = Mathf.Clamp(middle.x - 38f, 4f, Mathf.Max(4f, layout.width - 80f));
                    label.style.top = Mathf.Clamp(middle.y - 12f, 4f, Mathf.Max(4f, layout.height - 26f));
                }
            }

            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            if (_showPassages)
            {
                DrawPassages(context.painter2D);
            }
            else if (_item != null && _stage.TryProjectItem(_item, out var bounds))
            {
                DrawBrackets(context.painter2D, bounds, s_ItemColor);
            }
            else if (_opening != null && _stage.TryProjectOpening(_opening, out bounds))
            {
                DrawBrackets(context.painter2D, bounds, s_RoomColor);
            }
            else if (_room != null)
            {
                DrawRoom(context.painter2D, _room.Bounds);
            }
        }

        private void DrawPassages(Painter2D painter)
        {
            foreach (var (from, to, gap) in _passages)
            {
                var start = ToLocal(_stage.ProjectFloor(from));
                var end = ToLocal(_stage.ProjectFloor(to));
                var direction = (end - start).normalized;
                var perpendicular = new Vector2(-direction.y, direction.x) * 4f;
                painter.BeginPath();
                painter.MoveTo(start);
                painter.LineTo(end);
                painter.MoveTo(end - perpendicular);
                painter.LineTo(end + perpendicular);
                Stroke(painter, GapColor(gap));
            }
        }

        private Color GapColor(float gap)
        {
            return gap < .60f ? new Color32(255, 165, 155, 255) : gap < .85f ? s_RoomColor : s_ItemColor;
        }

        private void DrawRoom(Painter2D painter, Rect room)
        {
            const float Inset = .07f;
            var left = room.xMin + Inset;
            var right = room.xMax - Inset;
            var top = room.yMin + Inset;
            var bottom = room.yMax - Inset;
            painter.BeginPath();
            painter.MoveTo(ToLocal(_stage.ProjectFloor(new Vector2(left, top))));
            painter.LineTo(ToLocal(_stage.ProjectFloor(new Vector2(right, top))));
            painter.LineTo(ToLocal(_stage.ProjectFloor(new Vector2(right, bottom))));
            painter.LineTo(ToLocal(_stage.ProjectFloor(new Vector2(left, bottom))));
            painter.ClosePath();
            painter.fillColor = new Color(s_RoomColor.r, s_RoomColor.g, s_RoomColor.b, .16f);
            painter.Fill();
            Stroke(painter, s_RoomColor);
        }

        private void DrawBrackets(Painter2D painter, Rect projected, Color color)
        {
            var min = ToLocal(projected.min) - Vector2.one * 6f;
            var max = ToLocal(projected.max) + Vector2.one * 6f;
            var length = Mathf.Clamp(Mathf.Min(max.x - min.x, max.y - min.y) * .26f, 8f, 21f);
            Bracket(painter, min, new Vector2(1f, 1f), length, color);
            Bracket(painter, new Vector2(max.x, min.y), new Vector2(-1f, 1f), length, color);
            Bracket(painter, max, new Vector2(-1f, -1f), length, color);
            Bracket(painter, new Vector2(min.x, max.y), new Vector2(1f, -1f), length, color);
        }

        private void Stroke(Painter2D painter, Color color)
        {
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.lineWidth = 6f;
            painter.strokeColor = s_Shadow;
            painter.Stroke();
            painter.lineWidth = 3f;
            painter.strokeColor = color;
            painter.Stroke();
        }

        private void Bracket(Painter2D painter, Vector2 corner, Vector2 direction, float length, Color color)
        {
            corner = new Vector2(Mathf.Round(corner.x), Mathf.Round(corner.y));
            length = Mathf.Round(length);
            FillBracket(painter, corner, direction, length, 5f, s_Shadow);
            FillBracket(painter, corner + direction, direction, length - 2f, 3f, color);
        }

        private void FillBracket(Painter2D painter, Vector2 corner, Vector2 direction, float length, float width, Color color)
        {
            var horizontal = Vector2.right * direction.x;
            var vertical = Vector2.up * direction.y;
            painter.BeginPath();
            painter.MoveTo(corner);
            painter.LineTo(corner + horizontal * length);
            painter.LineTo(corner + horizontal * length + vertical * width);
            painter.LineTo(corner + direction * width);
            painter.LineTo(corner + horizontal * width + vertical * length);
            painter.LineTo(corner + vertical * length);
            painter.ClosePath();
            painter.fillColor = color;
            painter.Fill();
        }

        private Vector2 ToLocal(Vector2 point)
        {
            return Vector2.Scale(point, layout.size);
        }
    }
}
