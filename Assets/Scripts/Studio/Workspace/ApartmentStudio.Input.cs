using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace HouseDesign
{
    public sealed partial class ApartmentStudio
    {
        private Gesture _gesture;
        private int _pointerId = -1;
        private Vector2 _press;
        private Vector2 _lastPointer;

        private Vector2 _roomOrigin;
        private Rect _roomStart;
        private Vector2Int _resizeCorner;
        private Room _draggedRoom;

        private PlanWall _openingDragWall;
        private int _openingDragEdge;

        private CatalogEntry _placement;
        private PlacedItem _movingItem;
        private Vector2 _placementPoint;
        private float _placementAngle;
        private Room _placementRoom;

        private enum Gesture
        {
            None,
            DrawRoom,
            MoveRoom,
            ResizeRoom,
            DragOpening,
            Pan,
            Orbit,
            Catalog,
            Place
        }

        private void BindInput()
        {
            _viewport.RegisterCallback<PointerDownEvent>(OnViewportDown);
            _root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _root.RegisterCallback<PointerCaptureOutEvent>(OnCaptureLost);
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _viewport.RegisterCallback<WheelEvent>(OnWheel);
            _viewport.RegisterCallback<NavigationMoveEvent>(onNavigationMove);

            void onNavigationMove(NavigationMoveEvent evt)
            {
                if (_stage.Walking)
                {
                    evt.StopImmediatePropagation();
                }
            }
        }

        private void OnViewportDown(PointerDownEvent evt)
        {
            if (evt.target != _viewport && evt.target != _canvas)
            {
                return;
            }

            _viewport.Focus();

            if (_stage.ChangingWalkView)
            {
                return;
            }

            var pointer = (Vector2)evt.position;
            var local = _viewport.WorldToLocal(pointer);

            if (_stage.Walking)
            {
                if (evt.button == 1 || evt.pointerType == UnityEngine.UIElements.PointerType.touch)
                {
                    Capture(Gesture.Orbit, evt.pointerId, pointer);
                }

                return;
            }

            if (evt.button == 1 && _in3D || evt.button == 2)
            {
                Capture(evt.button == 1 ? Gesture.Orbit : Gesture.Pan, evt.pointerId, pointer);
            }
            else if (evt.button == 0)
            {
                if (_in3D)
                {
                    ModelPress(local, evt.pointerId, pointer);
                }
                else
                {
                    PlanPress(local, evt.pointerId, pointer);
                }
            }
        }

        private void ModelPress(Vector2 local, int pointerId, Vector2 pointer)
        {
            if (_placement != null)
            {
                Capture(Gesture.Place, pointerId, pointer);
                UpdatePlacement(pointer);

                return;
            }

            var point = ViewportPoint(local);
            Physics.SyncTransforms();

            if (_tool == PlanTool.Paint)
            {
                PaintSurface(point);
                return;
            }

            var item = _stage.PickItem(point);

            if (item != null)
            {
                if (_selectedItem != item || _tab != StudioTab.Catalog)
                {
                    SelectItem(item);
                }

                return;
            }

            var opening = _stage.PickOpening(point);

            if (opening != null && _plan.WallFor(opening) is PlanWall wall)
            {
                SelectOpening(opening, wall);
            }
            else
            {
                var hit = _stage.PickWall(point);
                var room = _stage.TryGetFloorPoint(point, out var floor) ? _plan.FindRoom(floor) : null;
                SelectRoom(room ?? hit?.Room);
            }
        }

        private void PlanPress(Vector2 local, int pointerId, Vector2 pointer)
        {
            var point = _canvas.ToPlan(local);

            if (_tool == PlanTool.Measure)
            {
                MeasurePress(point);
                return;
            }

            if (_tool == PlanTool.Room)
            {
                _roomOrigin = Snap(point);
                Capture(Gesture.DrawRoom, pointerId, pointer);
                UpdateRoomPreview(point);

                return;
            }

            if (_opening != null && _plan.WallFor(_opening) is PlanWall selectedWall)
            {
                var edge = _canvas.OpeningHandle(local, selectedWall, _opening);

                if (edge != 0)
                {
                    StartOpeningDrag(selectedWall, edge, pointerId, pointer);
                    return;
                }
            }

            bool? isDoor = _tool switch
            {
                PlanTool.Door => true,
                PlanTool.Window => false,
                _ => null
            };

            if (_canvas.TryPickOpening(local, isDoor, out var opening, out var wall))
            {
                SelectOpening(opening, wall);
                StartOpeningDrag(wall, 0, pointerId, pointer);

                return;
            }

            if (_tool == PlanTool.Select && _opening == null && _canvas.TryGetResizeHandle(local, _room, out _resizeCorner, out var handle))
            {
                _draggedRoom = _room;
                _roomStart = _room.Bounds;
                _roomOrigin = point - handle;
                Capture(Gesture.ResizeRoom, pointerId, pointer);

                return;
            }

            if (isDoor.HasValue)
            {
                HandleOpening(_plan.FindWall(point, _canvas.WorldDistance(15f), isDoor.Value), point, isDoor.Value);
                return;
            }

            var room = _plan.FindRoom(point);
            SelectRoom(room);

            if (room != null)
            {
                _draggedRoom = room;
                _roomStart = room.Bounds;
                _roomOrigin = point - room.Bounds.position;
                Capture(Gesture.MoveRoom, pointerId, pointer);
            }
        }

        private void HandleOpening(PlanWall? wall, Vector2 point, bool door)
        {
            if (!wall.HasValue)
            {
                Status(door ? "Выберите стык двух комнат." : "Выберите наружный участок стены.", true);
                return;
            }

            var value = wall.Value;
            var existing = _plan.FindOpening(value, value.Along(point));

            if (existing != null)
            {
                SelectOpening(existing, value);
                return;
            }

            var opening = _plan.PreviewOpening(value, point, door);

            if (!_plan.AddOpening(value, opening))
            {
                Status("Здесь не хватает места для проёма. Проверьте длину стены и соседние объекты.", true);
                return;
            }

            RebuildStage();
            SelectOpening(opening, value);
            _canvas.HoverWall = null;
            _canvas.OpeningPreview = null;
            _canvas.MarkDirtyRepaint();
            Status(door ? "Дверной проём добавлен." : "Окно добавлено.");
        }

        private void SelectOpening(Opening opening, PlanWall wall)
        {
            _room = wall.Room;
            _opening = opening;
            _selectedItem = null;
            RefreshWorkspace();
            _viewport.Focus();
            Status(_in3D ? "Размеры и настройки проёма доступны справа." : "Перетащите проём вдоль стены. Потяните за край, чтобы изменить ширину.");
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            var pointer = (Vector2)evt.position;
            var delta = pointer - _lastPointer;
            var local = _viewport.WorldToLocal(pointer);

            if (_gesture != Gesture.None && evt.pointerId != _pointerId)
            {
                return;
            }

            switch (_gesture)
            {
                case Gesture.DragOpening:
                    if ((pointer - _press).sqrMagnitude > 9f || _canvas.OpeningDragPreview != null)
                    {
                        UpdateOpeningDrag(local);
                    }

                    break;
                case Gesture.DrawRoom:
                case Gesture.MoveRoom:
                case Gesture.ResizeRoom:
                    if ((pointer - _press).sqrMagnitude > 9f || _canvas.RoomPreview.HasValue)
                    {
                        UpdateRoomPreview(_canvas.ToPlan(local));
                    }

                    break;
                case Gesture.Orbit:
                    _stage.Orbit(delta);
                    break;
                case Gesture.Pan:
                    if (_in3D)
                    {
                        _stage.Pan(delta, _viewport.layout.height);
                    }
                    else
                    {
                        _canvas.Pan(delta);
                    }

                    break;
                default:
                    if (!_in3D && _tool == PlanTool.Measure && !_measureComplete && _canvas.MeasureStart.HasValue && _viewport.ContainsPoint(local))
                    {
                        _canvas.ShowMeasurement(_canvas.MeasureStart, MeasurePoint(_canvas.ToPlan(local)));
                    }
                    else if (_placement != null)
                    {
                        var keyboard = Keyboard.current;

                        if (keyboard != null && keyboard.altKey.isPressed)
                        {
                            _placementAngle = Mathf.Repeat(_placementAngle + delta.x * 1.1f, 360f);
                        }

                        UpdatePlacement(pointer);
                    }
                    else if (!_in3D && _tool is PlanTool.Door or PlanTool.Window)
                    {
                        UpdateOpeningPreview(local);
                    }

                    break;
            }

            _lastPointer = pointer;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_gesture == Gesture.None || _pointerId != evt.pointerId)
            {
                return;
            }

            var completed = _gesture;
            var pointer = (Vector2)evt.position;
            var moved = (pointer - _press).sqrMagnitude > 9f;

            if (moved && completed is Gesture.DrawRoom or Gesture.MoveRoom or Gesture.ResizeRoom)
            {
                UpdateRoomPreview(_canvas.ToPlan(_viewport.WorldToLocal(pointer)));
            }

            if (moved && completed == Gesture.DragOpening)
            {
                UpdateOpeningDrag(_viewport.WorldToLocal(pointer));
            }

            ReleaseCapture();

            if (completed is Gesture.DrawRoom or Gesture.MoveRoom or Gesture.ResizeRoom)
            {
                CommitRoom(completed, moved);
            }
            else if (completed == Gesture.DragOpening)
            {
                CommitOpeningDrag(moved);
            }
            else if (completed is Gesture.Catalog or Gesture.Place)
            {
                if (completed == Gesture.Catalog && !moved)
                {
                    return;
                }

                UpdatePlacement(pointer);

                if (_viewport.worldBound.Contains(pointer))
                {
                    DropItem();
                }
                else if (completed == Gesture.Catalog && (pointer - _press).sqrMagnitude > 36f)
                {
                    CancelPlacement();
                }
            }
        }

        private void OnCaptureLost(PointerCaptureOutEvent evt)
        {
            if (_pointerId == evt.pointerId)
            {
                _pointerId = -1;
                CancelInteraction();
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            if (_stage.Walking || _stage.ChangingWalkView)
            {
                evt.StopPropagation();
                return;
            }

            var point = new Vector2(evt.mousePosition.x, evt.mousePosition.y);

            if (_in3D && _placement != null)
            {
                RotatePreview(evt.delta.y > 0f ? 15f : -15f);
            }
            else if (_in3D)
            {
                _stage.Zoom(evt.delta.y);
            }
            else
            {
                _canvas.Zoom(_canvas.WorldToLocal(point), evt.delta.y);
            }

            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            var target = evt.target as VisualElement;

            if (target is TextField || target?.GetFirstAncestorOfType<TextField>() != null)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Escape)
            {
                if (_openPanel != WorkspacePanel.None)
                {
                    var toggle = _openPanel == WorkspacePanel.Tools ? _toolsToggle : _detailsToggle;
                    CloseWorkspacePanel();
                    toggle.Focus();
                }
                else if (_stage.Walking)
                {
                    ToggleWalk();
                }
                else
                {
                    SetTool(PlanTool.Select);
                }

                evt.StopPropagation();

                return;
            }

            if (_stage.Walking)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Delete && _opening != null)
            {
                DeleteOpening(_opening);
                evt.StopPropagation();

                return;
            }

            if (!_in3D)
            {
                return;
            }

            if (evt.keyCode is KeyCode.Q or KeyCode.E)
            {
                var delta = (evt.keyCode == KeyCode.Q ? -1f : 1f) * (evt.shiftKey ? 45f : 15f);

                if (_placement != null)
                {
                    RotatePreview(delta);
                }
                else if (_selectedItem != null)
                {
                    TurnSelected(delta);
                }

                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Delete && _selectedItem != null && _placement == null)
            {
                DeleteItem(_selectedItem);
                evt.StopPropagation();
            }
        }

        private void Capture(Gesture gesture, int pointerId, Vector2 pointer)
        {
            ReleaseCapture();
            _gesture = gesture;
            _pointerId = pointerId;
            _press = pointer;
            _lastPointer = pointer;
            _root.CapturePointer(pointerId);
        }

        private void ReleaseCapture()
        {
            var pointerId = _pointerId;
            _pointerId = -1;
            _gesture = Gesture.None;

            if (pointerId >= 0 && _root.HasPointerCapture(pointerId))
            {
                _root.ReleasePointer(pointerId);
            }
        }

        private void UpdateRoomPreview(Vector2 point)
        {
            Rect bounds;

            if (_gesture == Gesture.DrawRoom)
            {
                var end = Snap(point);
                var start = Vector2.Min(_roomOrigin, end);
                var stop = Vector2.Max(_roomOrigin, end);
                bounds = new Rect(start, stop - start);
            }
            else if (_gesture == Gesture.MoveRoom)
            {
                var origin = point - _roomOrigin;
                var x = _plan.SnapCoordinate(origin.x, true, _canvas.WorldDistance(10f), _draggedRoom, _roomStart.width);
                var y = _plan.SnapCoordinate(origin.y, false, _canvas.WorldDistance(10f), _draggedRoom, _roomStart.height);
                bounds = new Rect(new Vector2(x, y), _roomStart.size);
            }
            else
            {
                bounds = _plan.ResizeBounds(_draggedRoom, _roomStart, _resizeCorner, point - _roomOrigin, _canvas.WorldDistance(10f));
            }

            _canvas.PreviewRoom = _draggedRoom;
            _canvas.RoomPreview = bounds;
            _canvas.PreviewValid = _plan.CanFitRoom(bounds, _draggedRoom);
            _canvas.Refresh();
        }

        private Vector2 Snap(Vector2 point, Room ignored = null)
        {
            var tolerance = _canvas.WorldDistance(10f);

            return new Vector2(_plan.SnapCoordinate(point.x, true, tolerance, ignored), _plan.SnapCoordinate(point.y, false, tolerance, ignored));
        }

        private void CommitRoom(Gesture gesture, bool moved)
        {
            var preview = _canvas.RoomPreview;
            _canvas.RoomPreview = null;
            _canvas.PreviewRoom = null;

            if (moved && preview.HasValue)
            {
                var valid = _canvas.PreviewValid;

                if (valid && gesture == Gesture.DrawRoom)
                {
                    var room = _plan.AddRoom($"Комната {_plan.Rooms.Count + 1}", preview.Value);
                    valid = room != null;

                    if (valid)
                    {
                        _room = room;
                        _opening = null;
                        _tool = PlanTool.Select;
                    }
                }
                else if (valid)
                {
                    valid = _plan.EditRoom(_draggedRoom, preview.Value, gesture == Gesture.ResizeRoom);
                }

                Status(valid ? "Планировка обновлена." : "Проверьте размеры комнаты, стыки, проёмы и мебель.", !valid);
            }

            _draggedRoom = null;
            RefreshWorkspace();
        }

        private void UpdateOpeningPreview(Vector2 local)
        {
            var point = _canvas.ToPlan(local);
            var door = _tool == PlanTool.Door;
            var hasOpening = _canvas.TryPickOpening(local, door, out _, out _);
            var wall = !hasOpening && _viewport.ContainsPoint(local) ? _plan.FindWall(point, _canvas.WorldDistance(15f), door) : null;
            _canvas.HoverWall = wall;
            _canvas.OpeningPreview = null;

            if (wall.HasValue && _plan.FindOpening(wall.Value, wall.Value.Along(point)) == null)
            {
                var opening = _plan.PreviewOpening(wall.Value, point, door);
                _canvas.OpeningPreview = opening;
                _canvas.PreviewValid = _plan.CanAddOpening(wall.Value, opening);
            }

            _canvas.MarkDirtyRepaint();
        }

        private void StartOpeningDrag(PlanWall wall, int edge, int pointerId, Vector2 pointer)
        {
            _openingDragWall = wall;
            _openingDragEdge = edge;
            _canvas.HoverWall = null;
            _canvas.OpeningPreview = null;
            _canvas.MarkDirtyRepaint();
            Capture(Gesture.DragOpening, pointerId, pointer);
        }

        private void UpdateOpeningDrag(Vector2 local)
        {
            var wall = _openingDragWall;
            var side = _openingDragEdge;
            var center = _opening.Center(wall);
            var width = _opening.Width;
            var delta = wall.Along(_canvas.ToPlan(local)) - wall.Along(_canvas.ToPlan(_viewport.WorldToLocal(_press)));
            delta = Mathf.Round(delta * 20f) / 20f;
            var span = _plan.OpeningSpan(wall, _opening);

            if (side == 0)
            {
                center = Mathf.Clamp(center + delta, span.Start + width / 2f, span.End - width / 2f);
            }
            else
            {
                var anchor = center - side * width / 2f;
                var maximum = side < 0 ? anchor - span.Start : span.End - anchor;
                width = Mathf.Clamp(width + delta * side, _plan.MinimumOpeningWidth(_opening), maximum);
                center = anchor + side * width / 2f;
            }

            var preview = _plan.OpeningAt(wall, _opening, center, width);
            _canvas.OpeningDragPreview = preview;
            _canvas.PreviewValid = _plan.CanAddOpening(wall, preview, _opening);
            _canvas.Refresh();
        }

        private void CommitOpeningDrag(bool moved)
        {
            var preview = _canvas.OpeningDragPreview;
            _canvas.OpeningDragPreview = null;

            if (moved && preview != null)
            {
                var result = _plan.UpdateOpening(_opening, preview.Offset, preview.Width, preview.Height, preview.Sill);
                ReportOpeningEdit(result);
            }

            RefreshWorkspace();
        }

        private void StartPlacement(CatalogEntry entry, PlacedItem moving)
        {
            if (_stage.Walking)
            {
                return;
            }

            CancelInteraction();
            _tool = PlanTool.Select;
            RefreshTools();
            _placement = entry;
            _movingItem = moving;
            RefreshModelSelection();
            _placementAngle = moving?.Angle ?? 0f;
            _stage.StartGhost(entry);
            _placementHint.style.display = DisplayStyle.Flex;
            _placementHint.text = $"{entry.Name}   ·   Q / E 15°   ·   Alt + мышь — свободный угол";
            Status("Линии показывают расстояние до стен. Красный — менее 60 см.");
            CloseWorkspacePanel();
            _viewport.Focus();
        }

        private void UpdatePlacement(Vector2 panelPoint)
        {
            if (_placement == null)
            {
                return;
            }

            var local = _viewport.WorldToLocal(panelPoint);
            _placementRoom = null;
            var visible = _viewport.ContainsPoint(local) && _stage.TryGetFloorPoint(ViewportPoint(local), out _placementPoint);

            if (visible)
            {
                _placementPoint.x = Mathf.Round(_placementPoint.x * 20f) / 20f;
                _placementPoint.y = Mathf.Round(_placementPoint.y * 20f) / 20f;
                _placementRoom = _plan.CanPlace(_placement, _placementPoint, _placementAngle, _movingItem);
            }

            var valid = _placementRoom != null;
            _stage.UpdateGhost(visible, _placementPoint, _placementAngle, valid);
            _modelSelection.ShowPassages(visible ? _plan.FindRoom(_placementPoint) : null, _placement, _placementPoint, _placementAngle);
            _placementHint.EnableInClassList("placement-invalid", visible && !valid);
            var verdict = !visible ? "Наведите на пол" : valid ? "Можно поставить" : "Место занято";
            _placementHint.text = $"{verdict}  ·  {_placementAngle:0}°   /   Q, E — шаг   ·   Alt + мышь — свободный угол";
        }

        private void RotatePreview(float delta)
        {
            _placementAngle = Mathf.Repeat(_placementAngle + delta, 360f);
            UpdatePlacement(_lastPointer);
        }

        private void DropItem()
        {
            if (_placementRoom == null)
            {
                Status("Здесь предмет упирается в стену, проём или другой объект.", true);
                return;
            }

            var item = _movingItem;

            if (item == null)
            {
                item = _plan.AddItem(_placement, _placementPoint, _placementAngle);

                if (item == null)
                {
                    return;
                }

                _stage.AddItem(item, true);
            }
            else
            {
                item.Position = _placementPoint;
                item.Angle = _placementAngle;
                item.Room = _placementRoom;
                _stage.MoveItem(item, true);
            }

            CancelPlacement();
            SelectItem(item);
            Status($"На плане · {item.Entry.Name}");
        }

        private void SelectItem(PlacedItem item)
        {
            _room = item.Room;
            _selectedItem = item;
            _opening = null;
            _tab = StudioTab.Catalog;
            RefreshWorkspace();
        }

        private void TurnSelected(float delta)
        {
            RotateItem(_selectedItem, _selectedItem.Angle + delta);
            var angle = Mathf.Repeat(_selectedItem.Angle, 360f);
            _rotationReadout.Track?.SetValueWithoutNotify(angle);

            if (_rotationReadout.Input != null)
            {
                SetNumericText(_rotationReadout.Input, angle);
            }
        }

        private void RotateItem(PlacedItem item, float angle)
        {
            angle = Mathf.Repeat(angle, 360f);

            if (_plan.CanPlace(item.Entry, item.Position, angle, item) == null)
            {
                Status("Для такого поворота рядом не хватает места.", true);
                return;
            }

            item.Angle = angle;
            _stage.MoveItem(item, true);
        }

        private void DeleteItem(PlacedItem item)
        {
            CancelPlacement();
            _stage.RemoveItem(item);
            _plan.RemoveItem(item);
            _selectedItem = null;
            RefreshWorkspace();
            Status("Предмет удалён.");
        }

        private void CancelPlacement()
        {
            _placement = null;
            _movingItem = null;
            _placementRoom = null;
            _stage.EndGhost();
            _modelSelection.ShowPassages(null, null, default, 0f);
            RefreshModelSelection();
            _placementHint.style.display = DisplayStyle.None;
            _placementHint.EnableInClassList("placement-invalid", false);
        }

        private void CancelInteraction()
        {
            ReleaseCapture();
            CancelPlacement();
            _draggedRoom = null;
            _canvas.RoomPreview = null;
            _canvas.PreviewRoom = null;
            _canvas.HoverWall = null;
            _canvas.OpeningPreview = null;
            _canvas.OpeningDragPreview = null;
            _measureComplete = false;
            _canvas.ShowMeasurement(null, null);
            _canvas.Refresh();
        }

        private Vector2 ViewportPoint(Vector2 local)
        {
            return new Vector2(local.x / _viewport.layout.width, local.y / _viewport.layout.height);
        }
    }
}
