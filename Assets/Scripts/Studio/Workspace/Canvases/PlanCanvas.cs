using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HouseDesign
{
    internal sealed class PlanCanvas : VisualElement
    {
        private static readonly Vector2Int[] s_RoomCorners =
        {
            new(-1, -1),
            new(1, -1),
            new(-1, 1),
            new(1, 1)
        };

        private readonly ApartmentPlan _plan;
        private readonly VisualElement _labels;
        private readonly bool _poster;
        private readonly Label _measureLabel;
        private readonly List<(Room Room, VisualElement Badge, Label Name, Label Area)> _posterCaptions = new();
        private readonly List<(Vector2 Center, Vector2 Marker)> _posterLeaders = new();
        private Vector2 _origin;
        private Rect _headingBounds;
        private float _scale = 65f;
        private float _minimumZoom = 8f;
        private bool _fitted;
        private bool _stayFramed;
        private Vector2 _framedSize;

        internal Room SelectedRoom
        {
            get;
            set;
        }
        internal Opening SelectedOpening
        {
            get;
            set;
        }
        internal Rect? RoomPreview
        {
            get;
            set;
        }
        internal Room PreviewRoom
        {
            get;
            set;
        }
        internal PlanWall? HoverWall
        {
            get;
            set;
        }
        internal Opening OpeningPreview
        {
            get;
            set;
        }
        internal Opening OpeningDragPreview
        {
            get;
            set;
        }
        internal bool PreviewValid
        {
            get;
            set;
        }
        internal Vector2? MeasureStart
        {
            get;
            private set;
        }
        private Vector2? MeasureEnd
        {
            get;
            set;
        }

        internal PlanCanvas(ApartmentPlan plan, bool poster = false)
        {
            _plan = plan;
            _poster = poster;

            _labels = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };
            _labels.AddToClassList("drawing-labels");
            Add(_labels);

            if (poster)
            {
                foreach (var room in _plan.Rooms)
                {
                    var badge = RoomCaption(room.Name, room.Bounds, out var name, out var area);
                    badge.style.visibility = Visibility.Hidden;
                    _labels.Add(badge);
                    _posterCaptions.Add((room, badge, name, area));
                }
            }
            else
            {
                _measureLabel = new Label
                {
                    pickingMode = PickingMode.Ignore
                };
                _measureLabel.AddToClassList("measurement-value");
                _measureLabel.style.display = DisplayStyle.None;
                Add(_measureLabel);
            }

            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(fitOnFirstLayout);

            void fitOnFirstLayout(GeometryChangedEvent _)
            {
                if (!_fitted || _stayFramed)
                {
                    Fit();
                }
                else
                {
                    Refresh();
                }
            }
        }

        internal Vector2 ToPlan(Vector2 local)
        {
            return (local - _origin) / _scale;
        }

        internal float WorldDistance(float pixels)
        {
            return pixels / _scale;
        }

        internal void Fit()
        {
            Fit(layout.size);
        }

        internal void Fit(Vector2 canvasSize)
        {
            if (!(canvasSize.x > 1f && canvasSize.y > 1f))
            {
                return;
            }

            var bounds = _plan.Bounds;
            var top = _poster ? 36f : Mathf.Max(110f, _headingBounds.yMax + 41f);
            var bottom = _poster ? 36f : 63f;
            var padding = new Vector2(_poster ? 84f : Mathf.Clamp(canvasSize.x * .16f, 42f, 140f), top + bottom);
            var space = Vector2.Max(Vector2.one, canvasSize - padding);
            var scale = Mathf.Min(space.x / Mathf.Max(5f, bounds.width), space.y / Mathf.Max(4f, bounds.height));
            _scale = _poster ? scale : Mathf.Min(scale, 116f);
            _minimumZoom = Mathf.Min(8f, _scale * .5f);
            _origin = canvasSize * .5f - bounds.center * _scale + new Vector2(0f, (top - bottom) * .5f);
            _fitted = true;
            _stayFramed = true;
            _framedSize = canvasSize;
            Refresh();
        }

        internal void KeepLabelsClear(Rect headingBounds)
        {
            var heightChanged = !Mathf.Approximately(_headingBounds.yMax, headingBounds.yMax);
            _headingBounds = headingBounds;

            if (heightChanged && _stayFramed)
            {
                Fit(_framedSize);
            }
            else
            {
                Refresh();
            }
        }

        internal void ShowMeasurement(Vector2? first, Vector2? last)
        {
            MeasureStart = first;
            MeasureEnd = last;
            RefreshMeasurement();
            MarkDirtyRepaint();
        }

        private void RefreshMeasurement()
        {
            if (_measureLabel == null)
            {
                return;
            }

            _measureLabel.style.display = MeasureStart.HasValue && MeasureEnd.HasValue ? DisplayStyle.Flex : DisplayStyle.None;

            if (MeasureStart is Vector2 start && MeasureEnd is Vector2 end)
            {
                var center = ToScreen((start + end) * .5f);
                _measureLabel.text = $"{Vector2.Distance(start, end):0.00} м";
                var left = Mathf.Clamp(center.x - 38f, 4f, Mathf.Max(4f, layout.width - 80f));
                var top = Mathf.Clamp(center.y - 29f, 4f, Mathf.Max(4f, layout.height - 24f));

                if (_headingBounds.Overlaps(new Rect(left, top, 76f, 23f)))
                {
                    top = _headingBounds.yMax + 4f;
                }

                _measureLabel.style.left = left;
                _measureLabel.style.top = top;
            }
        }

        internal void Pan(Vector2 delta)
        {
            _stayFramed = false;
            _origin += delta;
            Refresh();
        }

        internal void Zoom(Vector2 anchor, float delta)
        {
            _stayFramed = false;
            var world = ToPlan(anchor);
            _scale = Mathf.Clamp(_scale * Mathf.Exp(-delta * .046f), _minimumZoom, 240f);
            _origin = anchor - world * _scale;
            Refresh();
        }

        internal bool TryGetResizeHandle(Vector2 point, Room room, out Vector2Int corner, out Vector2 handle)
        {
            corner = default;
            handle = default;

            if (room == null)
            {
                return false;
            }

            var nearest = 11f;

            foreach (var candidate in s_RoomCorners)
            {
                var position = CornerPoint(room.Bounds, candidate);
                var distance = Vector2.Distance(point, ToScreen(position));

                if (distance <= nearest)
                {
                    nearest = distance;
                    corner = candidate;
                    handle = position;
                }
            }

            return corner.x != 0;
        }

        internal bool TryPickOpening(Vector2 point, bool? isDoor, out Opening selected, out PlanWall selectedWall)
        {
            selected = null;
            selectedWall = default;
            var nearest = 11f;

            foreach (var opening in _plan.Openings)
            {
                if (isDoor.HasValue && isDoor.Value != opening.IsDoor)
                {
                    continue;
                }

                var wall = _plan.WallFor(opening);

                if (!wall.HasValue)
                {
                    continue;
                }

                var value = wall.Value;
                var along = opening.HingePosition(value);
                var hinge = ToScreen(value.Point(along));
                var latch = ToScreen(value.Point(2f * opening.Center(value) - along));
                var distance = DistanceToSegment(point, hinge, latch);

                if (opening.IsDoor)
                {
                    var swing = opening.SwingDirection(value);
                    var radius = opening.Width * _scale;
                    distance = Mathf.Min(distance, DistanceToSegment(point, hinge, hinge + swing * radius));
                    var relative = point - hinge;

                    if (Vector2.Dot(relative, swing) >= 0f && Vector2.Dot(relative, latch - hinge) >= 0f)
                    {
                        distance = Mathf.Min(distance, relative.sqrMagnitude <= radius * radius ? 0f : relative.magnitude - radius);
                    }
                }

                if (distance <= nearest)
                {
                    nearest = distance;
                    selected = opening;
                    selectedWall = value;
                }
            }

            return selected != null;
        }

        internal int OpeningHandle(Vector2 point, PlanWall wall, Opening opening)
        {
            var center = opening.Center(wall);
            var first = (point - ToScreen(wall.Point(center - opening.Width / 2f))).sqrMagnitude;
            var last = (point - ToScreen(wall.Point(center + opening.Width / 2f))).sqrMagnitude;

            return Mathf.Min(first, last) > 100f ? 0 : first <= last ? -1 : 1;
        }

        internal void Refresh()
        {
            MarkDirtyRepaint();
            RefreshMeasurement();

            if (_poster)
            {
                return;
            }

            _labels.Clear();

            if (!_fitted)
            {
                return;
            }

            foreach (var room in _plan.Rooms)
            {
                var bounds = room == PreviewRoom && RoomPreview.HasValue ? RoomPreview.Value : room.Bounds;

                if (bounds.width * _scale < 60f || bounds.height * _scale < 47f)
                {
                    continue;
                }

                var screen = ToScreen(bounds.center);
                var width = Mathf.Min(Mathf.Clamp(room.Name.Length * 10f + 22f, 80f, 196f), bounds.width * _scale - 10f);
                var badge = new Rect(screen.x - width / 2f, screen.y - 21f, width, 42f);

                if (!ShowLabel(badge))
                {
                    continue;
                }

                var caption = RoomCaption(room.Name, bounds, out _, out _);
                PlaceLabel(caption, badge);
            }

            if (RoomPreview.HasValue || SelectedRoom != null)
            {
                var rect = RoomPreview ?? SelectedRoom.Bounds;
                AddLabel($"{rect.width:0.00} м", ToScreen(new Vector2(rect.center.x, rect.yMin)) - new Vector2(0f, 18f), "dimension-label", 64f);
                AddLabel($"{rect.height:0.00} м", ToScreen(new Vector2(rect.xMax, rect.center.y)) + new Vector2(37f, 0f), "dimension-label", 64f);
            }

            var selectedWall = SelectedOpening != null ? _plan.WallFor(SelectedOpening) : null;

            if (selectedWall.HasValue)
            {
                var wall = selectedWall.Value;
                var opening = OpeningDragPreview ?? SelectedOpening;
                var caption = ToScreen(wall.Point(opening.Center(wall))) - wall.Inward * (wall.Horizontal ? 17f : 35f);
                AddLabel($"{Mathf.RoundToInt(opening.Width * 1000f)} мм", caption, "dimension-label", 64f);
            }
        }

        private VisualElement RoomCaption(string name, Rect bounds, out Label title, out Label area)
        {
            var badge = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };
            badge.AddToClassList("room-caption");
            title = new Label(name)
            {
                pickingMode = PickingMode.Ignore,
                enableRichText = false
            };
            title.AddToClassList("room-label");
            area = new Label($"{bounds.width * bounds.height:0.0} м²")
            {
                pickingMode = PickingMode.Ignore
            };
            area.AddToClassList("area-label");
            badge.Add(title);
            badge.Add(area);

            return badge;
        }

        internal List<string> LayoutPosterCaptions()
        {
            const float Inset = 8f;
            const float HorizontalPadding = 16f;
            var occupied = new List<Rect>();
            var overflow = new List<(Room Room, string Area)>();
            var legend = new List<string>();

            foreach (var (room, badge, name, area) in _posterCaptions)
            {
                var size = room.Bounds.size * _scale;
                var maxWidth = size.x - Inset * 2f - HorizontalPadding;
                var maxHeight = size.y - Inset * 2f;

                if (maxWidth >= 40f && maxHeight >= 42f)
                {
                    var fullName = name.MeasureTextSize(name.text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);
                    var areaSize = area.MeasureTextSize(area.text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);
                    var textWidth = Mathf.Min(maxWidth, Mathf.Ceil(Mathf.Max(fullName.x, areaSize.x)) + 4f);
                    var wrappedName = name.MeasureTextSize(name.text, textWidth, MeasureMode.Exactly, 0f, MeasureMode.Undefined);
                    var height = Mathf.Max(42f, Mathf.Ceil(wrappedName.y + areaSize.y) + 10f);

                    if (height <= maxHeight && areaSize.x <= textWidth)
                    {
                        var badgeSize = new Vector2(textWidth + HorizontalPadding, height);
                        var rect = new Rect(ToScreen(room.Bounds.center) - badgeSize / 2f, badgeSize);
                        badge.style.height = height;
                        badge.style.visibility = Visibility.Visible;
                        PlaceLabel(badge, rect);
                        occupied.Add(rect);
                        continue;
                    }
                }

                badge.RemoveFromHierarchy();
                overflow.Add((room, area.text));
            }

            foreach (var (room, area) in overflow)
            {
                var number = (legend.Count + 1).ToString();
                var center = ToScreen(room.Bounds.center);

                if (TryPlacePosterMarker(center, number.Length, occupied, out var marker))
                {
                    var label = new Label(number)
                    {
                        pickingMode = PickingMode.Ignore
                    };
                    label.AddToClassList("poster-marker");
                    PlaceLabel(label, marker);
                    occupied.Add(marker);

                    if (!marker.Contains(center))
                    {
                        _posterLeaders.Add((center, marker.center));
                    }

                    legend.Add($"{number}. {room.Name} · {area}");
                }
                else
                {
                    var point = room.Bounds.center;
                    legend.Add($"{number}. {room.Name} · {area} (X {point.x:0.0}, Y {point.y:0.0} м)");
                }
            }

            MarkDirtyRepaint();

            return legend;
        }

        private bool TryPlacePosterMarker(Vector2 center, int digits, List<Rect> occupied, out Rect result)
        {
            var size = new Vector2(Mathf.Max(24f, digits * 9f + 10f), 24f);
            var limits = new Rect(4f, 4f, layout.width - 8f, layout.height - 8f);

            for (var radius = 0; radius <= 72; radius += 12)
            {
                var steps = radius == 0 ? 1 : 8;

                for (var step = 0; step < steps; step++)
                {
                    var angle = step * Mathf.PI / 4f;
                    var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    var rect = new Rect(center + offset - size / 2f, size);
                    var padded = new Rect(rect.position - Vector2.one * 2f, rect.size + Vector2.one * 4f);

                    if (limits.Contains(padded.min) && limits.Contains(padded.max) && !occupied.Exists(other => other.Overlaps(padded)))
                    {
                        result = rect;
                        return true;
                    }
                }
            }

            result = default;

            return false;
        }

        private void AddLabel(string text, Vector2 position, string cssClass, float width)
        {
            var labelBounds = new Rect(position.x - width / 2f, position.y - 9f, width, 18f);

            if (!ShowLabel(labelBounds))
            {
                return;
            }

            var label = new Label(text)
            {
                pickingMode = PickingMode.Ignore
            };
            label.AddToClassList(cssClass);
            PlaceLabel(label, labelBounds);
        }

        private bool ShowLabel(Rect bounds)
        {
            return new Rect(-25f, -25f, layout.width + 50f, layout.height + 50f).Contains(bounds.center) && !_headingBounds.Overlaps(bounds);
        }

        private void PlaceLabel(VisualElement label, Rect bounds)
        {
            label.style.left = bounds.x;
            label.style.top = bounds.y;
            label.style.width = bounds.width;

            if (label.parent != _labels)
            {
                _labels.Add(label);
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            if (!_fitted)
            {
                return;
            }

            var painter = context.painter2D;

            if (!_poster)
            {
                DrawGrid(painter);
            }

            foreach (var room in _plan.Rooms)
            {
                var tint = room == SelectedRoom ? new Color32(61, 80, 94, 255) : new Color32(44, 56, 68, 255);
                FillRect(painter, ToScreen(room.Bounds.min), ToScreen(room.Bounds.max), tint);
            }

            foreach (var item in _plan.Items)
            {
                DrawFurniture(painter, item);
            }

            foreach (var wall in _plan.Walls)
            {
                DrawWall(painter, wall);
            }

            foreach (var (center, marker) in _posterLeaders)
            {
                Line(painter, center, marker, new Color32(232, 185, 140, 255), 1f);
            }

            if (HoverWall.HasValue)
            {
                var wall = HoverWall.Value;
                Line(painter, ToScreen(wall.Point(wall.Start)), ToScreen(wall.Point(wall.End)), new Color32(223, 157, 114, 190), 2f);

                if (OpeningPreview != null)
                {
                    DrawOpening(painter, wall, OpeningPreview, PreviewValid ? new Color32(153, 217, 192, 255) : new Color32(241, 120, 137, 255));
                }
            }

            if (RoomPreview.HasValue)
            {
                DrawRoomOutline(painter, RoomPreview.Value, PreviewValid ? new Color32(237, 173, 121, 255) : new Color32(241, 120, 137, 255), true);
            }
            else if (SelectedRoom != null)
            {
                DrawRoomOutline(painter, SelectedRoom.Bounds, new Color32(227, 159, 110, 255), false);
            }

            if (MeasureStart is Vector2 first)
            {
                var color = new Color32(154, 239, 217, 255);
                var start = ToScreen(first);
                var end = ToScreen(MeasureEnd ?? first);

                if ((end - start).sqrMagnitude > .1f)
                {
                    Line(painter, start, end, color, 2f);
                }

                FillRect(painter, start - Vector2.one * 3f, start + Vector2.one * 3f, color);
                FillRect(painter, end - Vector2.one * 3f, end + Vector2.one * 3f, color);
            }
        }

        private void DrawGrid(Painter2D painter)
        {
            var extent = layout.size;
            var step = _scale * .1f;

            if (step >= 8f)
            {
                DrawGridLines(painter, extent, step, new Color32(48, 58, 69, 180));
            }

            step = _scale * .5f;

            if (step >= 9f)
            {
                DrawGridLines(painter, extent, step, new Color32(54, 67, 80, 215));
            }

            var major = _scale >= 9f ? _scale : _scale * Mathf.Pow(10f, Mathf.Ceil(Mathf.Log10(9f / _scale)));
            DrawGridLines(painter, extent, major, new Color32(66, 80, 95, 220));
        }

        private void DrawGridLines(Painter2D painter, Vector2 extent, float step, Color color)
        {
            painter.strokeColor = color;
            painter.lineWidth = 1f;
            painter.BeginPath();

            for (var x = Mathf.Repeat(_origin.x, step); x < extent.x; x += step)
            {
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, extent.y));
            }

            for (var y = Mathf.Repeat(_origin.y, step); y < extent.y; y += step)
            {
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(extent.x, y));
            }

            painter.Stroke();
        }

        private void DrawWall(Painter2D painter, PlanWall wall)
        {
            var wallColor = wall.Neighbor == null ? new Color32(177, 189, 191, 255) : new Color32(138, 157, 167, 255);
            var cursor = wall.Start;
            var openings = new List<Opening>();

            foreach (var opening in _plan.Openings)
            {
                if (_plan.BelongsTo(opening, wall))
                {
                    openings.Add(opening);
                }
            }

            openings.Sort((first, second) => first.Center(wall).CompareTo(second.Center(wall)));

            foreach (var opening in openings)
            {
                var selected = opening == SelectedOpening;
                var visible = selected ? OpeningDragPreview ?? opening : opening;
                var start = visible.Center(wall) - visible.Width / 2f;
                Line(painter, ToScreen(wall.Point(cursor)), ToScreen(wall.Point(start)), wallColor, 3f);
                var color = selected ? new Color32(241, 169, 111, 255) : new Color32(143, 198, 215, 255);

                if (selected && OpeningDragPreview != null && !PreviewValid)
                {
                    color = new Color32(241, 120, 137, 255);
                }

                DrawOpening(painter, wall, visible, color, selected);
                cursor = start + visible.Width;

                if (selected)
                {
                    var radius = Vector2.one * 4.5f;
                    var first = ToScreen(wall.Point(start));
                    var last = ToScreen(wall.Point(cursor));
                    FillRect(painter, first - radius, first + radius, color);
                    FillRect(painter, last - radius, last + radius, color);
                }
            }

            Line(painter, ToScreen(wall.Point(cursor)), ToScreen(wall.Point(wall.End)), wallColor, 3f);
        }

        private void DrawOpening(Painter2D painter, PlanWall wall, Opening opening, Color color, bool selected = false)
        {
            var center = opening.Center(wall);
            var along = opening.HingePosition(wall);
            var hinge = ToScreen(wall.Point(along));
            var edge = ToScreen(wall.Point(2f * center - along));
            var inward = opening.SwingDirection(wall);

            if (!opening.IsDoor)
            {
                Line(painter, hinge + inward * 2.5f, edge + inward * 2.5f, color, 1.5f);
                Line(painter, hinge - inward * 2.5f, edge - inward * 2.5f, color, 1.5f);

                return;
            }

            var radius = opening.Width * _scale;
            var extended = hinge + inward * radius;

            if (selected)
            {
                var threshold = new Color(color.r, color.g, color.b, .28f);
                Line(painter, hinge, edge, threshold, 5f);
            }

            Line(painter, hinge, extended, color, selected ? 3f : 1.7f);
            painter.strokeColor = color;
            painter.lineWidth = selected ? 2f : 1f;
            painter.BeginPath();
            painter.MoveTo(edge);
            var startDirection = (edge - hinge).normalized;

            for (var step = 1; step <= 12; step++)
            {
                var angle = step * Mathf.PI / 24f;
                painter.LineTo(hinge + (startDirection * Mathf.Cos(angle) + inward * Mathf.Sin(angle)) * radius);
            }

            painter.Stroke();
        }

        private void DrawFurniture(Painter2D painter, PlacedItem item)
        {
            var radians = item.Angle * Mathf.Deg2Rad;
            var right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (item.Entry.Width * _scale / 2f);
            var forward = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * (item.Entry.Depth * _scale / 2f);
            var center = ToScreen(item.Position);
            painter.fillColor = new Color32(115, 139, 151, 65);
            painter.strokeColor = new Color32(126, 147, 158, 126);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(center - right - forward);
            painter.LineTo(center + right - forward);
            painter.LineTo(center + right + forward);
            painter.LineTo(center - right + forward);
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }

        private void DrawRoomOutline(Painter2D painter, Rect room, Color color, bool isPreview)
        {
            var from = ToScreen(room.min);
            var to = ToScreen(room.max);

            if (isPreview)
            {
                FillRect(painter, from, to, new Color(color.r, color.g, color.b, .17f));
            }

            painter.strokeColor = color;
            painter.lineWidth = 2f;
            RectPath(painter, from, to);
            painter.Stroke();

            if (!isPreview)
            {
                var radius = new Vector2(4.5f, 4.5f);

                foreach (var corner in s_RoomCorners)
                {
                    var center = ToScreen(CornerPoint(room, corner));
                    FillRect(painter, center - radius, center + radius, color);
                    FillRect(painter, center - Vector2.one * 2f, center + Vector2.one * 2f, new Color32(42, 36, 42, 255));
                }
            }
        }

        private Vector2 CornerPoint(Rect bounds, Vector2Int corner)
        {
            return new Vector2(corner.x < 0 ? bounds.xMin : bounds.xMax, corner.y < 0 ? bounds.yMin : bounds.yMax);
        }

        private float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            var line = end - start;
            var t = Mathf.Clamp01(Vector2.Dot(point - start, line) / Mathf.Max(line.sqrMagnitude, .001f));

            return Vector2.Distance(point, start + line * t);
        }

        private void FillRect(Painter2D painter, Vector2 start, Vector2 end, Color color)
        {
            painter.fillColor = color;
            RectPath(painter, start, end);
            painter.Fill();
        }

        private void RectPath(Painter2D painter, Vector2 start, Vector2 end)
        {
            painter.BeginPath();
            painter.MoveTo(start);
            painter.LineTo(new Vector2(end.x, start.y));
            painter.LineTo(end);
            painter.LineTo(new Vector2(start.x, end.y));
            painter.ClosePath();
        }

        private void Line(Painter2D painter, Vector2 start, Vector2 end, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(start);
            painter.LineTo(end);
            painter.Stroke();
        }

        private Vector2 ToScreen(Vector2 point)
        {
            return _origin + point * _scale;
        }
    }
}
