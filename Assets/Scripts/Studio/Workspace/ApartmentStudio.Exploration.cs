using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace HouseDesign
{
    public sealed partial class ApartmentStudio
    {
        private int _brushFinish;
        private bool _paintRoom;
        private bool _measureComplete;

        private void ToggleWalk()
        {
            CancelInteraction();

            if (_stage.Walking)
            {
                _stage.EndWalk(true);
                Status("Открыт обзор квартиры.");
            }
            else if (!_stage.BeginWalk(_room))
            {
                Status("В квартире не нашлось свободного места для начала прогулки.", true);
                return;
            }
            else
            {
                _tool = PlanTool.Select;
                _opening = null;
                _selectedItem = null;
                Status("Идите через дверные проёмы. Стены и мебель ограничивают движение.");
            }

            RefreshWorkspace();
            CloseWorkspacePanel();
            _viewport.Focus();
        }

        private void LateUpdate()
        {
            if (!_stage.Walking)
            {
                return;
            }

            var direction = Vector2.zero;
            var focused = _root.focusController?.focusedElement as VisualElement;
            var typing = focused is TextField || focused?.GetFirstAncestorOfType<TextField>() != null;

            if (Application.isFocused && !typing && _root.Q(className: "modal-shade") == null && Keyboard.current is Keyboard keyboard)
            {
                var right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f;
                var left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f;
                var forward = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f;
                var backward = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f;

                direction = new Vector2(right - left, forward - backward);
            }

            var delta = Time.timeScale > 0f ? Time.smoothDeltaTime / Time.timeScale : 0f;
            _stage.Walk(direction, Mathf.Min(delta > 0f ? delta : Time.unscaledDeltaTime, .05f));
        }

        private void MeasurePress(Vector2 point)
        {
            var snapped = MeasurePoint(point);

            if (_measureComplete || !_canvas.MeasureStart.HasValue)
            {
                _measureComplete = false;
                _canvas.ShowMeasurement(snapped, null);
                Status("Укажите вторую точку.");

                return;
            }

            _measureComplete = true;
            var length = Vector2.Distance(_canvas.MeasureStart.Value, snapped);
            _canvas.ShowMeasurement(_canvas.MeasureStart, snapped);
            Status($"Расстояние {length:0.00} м. Щёлкните на плане, чтобы начать новый замер.");
        }

        private Vector2 MeasurePoint(Vector2 point)
        {
            var snapped = new Vector2(Mathf.Round(point.x * 20f), Mathf.Round(point.y * 20f)) / 20f;
            var tolerance = _canvas.WorldDistance(8f);
            var wall = _plan.FindWall(point, tolerance);

            if (wall is not PlanWall value)
            {
                return snapped;
            }

            var along = value.Along(point);
            var closest = Mathf.Clamp(value.Along(snapped), value.Start, value.End);
            consider(value.Start);
            consider(value.End);

            foreach (var opening in _plan.Openings)
            {
                if (_plan.BelongsTo(opening, value))
                {
                    consider(opening.Center(value) - opening.Width / 2f);
                    consider(opening.Center(value) + opening.Width / 2f);
                }
            }

            return value.Point(closest);

            void consider(float edge)
            {
                var distance = Mathf.Abs(along - edge);

                if (distance < tolerance)
                {
                    tolerance = distance;
                    closest = edge;
                }
            }
        }

        private void PaintSurface(Vector2 viewportPoint)
        {
            if (!_stage.TryPickWallSurface(viewportPoint, out var room, out var side))
            {
                Status("Щёлкните по видимой поверхности стены.", true);
                return;
            }

            if (_paintRoom)
            {
                room.PaintWalls(_brushFinish);
            }
            else
            {
                room.PaintWall(side, _brushFinish);
            }

            _room = room;
            RebuildStage();
            RefreshRooms();
            Status($"{room.Name} · {_materials.Walls[_brushFinish].Name}{(_paintRoom ? " · все стены" : "")}");
        }

    }
}
