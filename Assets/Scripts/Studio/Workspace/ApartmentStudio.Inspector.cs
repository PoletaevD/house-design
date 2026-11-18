using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HouseDesign
{
    public sealed partial class ApartmentStudio
    {
        private VisualElement _panel;
        private (Slider Track, TextField Input) _rotationReadout;

        private void RefreshInspector()
        {
            _inspectorTabs.Clear();
            _inspector.Clear();
            _inspector.EnableInClassList("plan-inspector", !_in3D);
            _rotationReadout = default;

            if (_stage.Walking)
            {
                Text(_inspectorTabs, "ОСВЕЩЕНИЕ", "inspector-caption");
                _panel = Element(_inspector.contentContainer, "panel-content");
                LightInspector();

                return;
            }

            if (_in3D)
            {
                AddTab("Каталог", StudioTab.Catalog);
                AddTab("Отделка", StudioTab.Finishes);
                AddTab("Свет", StudioTab.Light);
            }
            else
            {
                Text(_inspectorTabs, "ПАРАМЕТРЫ ПЛАНА", "inspector-caption");
            }

            _panel = Element(_inspector.contentContainer, "panel-content");

            if (_opening != null)
            {
                OpeningInspector();
            }
            else if (!_in3D)
            {
                if (_tool == PlanTool.Door)
                {
                    DoorList();
                }
                else if (_tool == PlanTool.Measure)
                {
                    Text(_panel, "Рулетка", "panel-title");
                    Text(_panel, "Щёлкните в двух точках на плане. Рулетка притягивается к стенам и краям проёмов.", "panel-caption");
                    Action(_panel, "Очистить замер", () => SetTool(PlanTool.Measure), "secondary-button");
                }
                else
                {
                    PlanInspector();
                }
            }
            else
            {
                switch (_tab)
                {
                    case StudioTab.Finishes:
                        FinishInspector();
                        break;
                    case StudioTab.Light:
                        LightInspector();
                        break;
                    default:
                        CatalogInspector();
                        break;
                }
            }
        }

        private void AddTab(string label, StudioTab tab)
        {
            Button button = null;
            button = Action(_inspectorTabs, label, selectTab, "tab");
            button.EnableInClassList("active-tab", _tab == tab && _opening == null);

            void selectTab()
            {
                var keepFocus = _root.focusController?.focusedElement == button;
                CancelInteraction();
                _opening = null;

                if (_tool == PlanTool.Paint && tab != StudioTab.Finishes)
                {
                    _tool = PlanTool.Select;
                    RefreshTools();
                }

                RefreshModelSelection();
                _tab = tab;
                _inspector.scrollOffset = Vector2.zero;
                RefreshInspector();

                if (keepFocus)
                {
                    _inspectorTabs.Q<Button>(className: "active-tab")?.Focus();
                }
            }
        }

        private void PlanInspector()
        {
            var room = _room;

            if (room == null)
            {
                Text(_panel, "Планировка", "panel-title");
                Text(_panel, "Нарисуйте комнату или выберите её на плане, чтобы задать точные размеры.", "panel-caption");
                Action(_panel, "Нарисовать комнату", () => SetTool(PlanTool.Room), "primary-button");
                WallHeightField();

                return;
            }

            var bounds = room.Bounds;
            Text(_panel, room.Name, "panel-title");
            Text(_panel, $"{bounds.width * bounds.height:0.0} м² · Размеры в миллиметрах", "panel-caption");
            Text(_panel, "На чертеже потяните за любой из четырёх углов.", "section-caption");
            var name = new TextField("Название")
            {
                value = room.Name,
                maxLength = 35
            };
            name.AddToClassList("studio-field");
            _panel.Add(name);
            BindValidation(_panel, name, value => string.IsNullOrWhiteSpace(value) || value.Trim().Length > 35 ? "Введите название комнаты длиной до 35 символов." : null);
            var size = Element(_panel, "field-row");
            var width = Number(size, "Ширина", bounds.width, ApartmentPlan.MinimumRoomSize, ApartmentPlan.MaximumRoomSize);
            var depth = Number(size, "Глубина", bounds.height, ApartmentPlan.MinimumRoomSize, ApartmentPlan.MaximumRoomSize);
            var origin = Element(_panel, "field-row");
            var x = Number(origin, "Позиция X", bounds.xMin, -100f, 100f);
            var y = Number(origin, "Позиция Y", bounds.yMin, -100f, 100f);
            var editError = FieldGroupError(_panel, width, depth, x, y);
            var apply = Action(_panel, "Применить размеры", applyRoomBounds, "primary-button");
            RequireFields(apply, name, width, depth, x, y);
            Action(_panel, "Удалить комнату", () => ConfirmRoomRemoval(room), "danger-button");
            WallHeightField();

            void applyRoomBounds()
            {
                if (!ValidField(name) || !TryMeters(width, out var w) || !TryMeters(depth, out var d) || !TryMeters(x, out var px) || !TryMeters(y, out var py))
                {
                    Status("Введите размеры и координаты в миллиметрах.", true);
                    return;
                }

                if (!_plan.EditRoom(room, new Rect(px, py, w, d)))
                {
                    ShowFieldMessage(editError, "Комната пересекает соседнюю или в новых границах не помещаются проёмы либо мебель.");
                    return;
                }

                room.Name = name.value.Trim();
                RefreshWorkspace();
                Status("Параметры комнаты обновлены.");
            }
        }

        private void ConfirmRoomRemoval(Room room)
        {
            var overlay = Element(_root, "modal-shade");
            var dialog = Element(overlay, "modal");
            Text(dialog, "Удалить комнату?", "panel-title");
            Text(dialog, "Связанные проёмы и предметы в комнате тоже будут удалены.", "panel-caption");
            var buttons = Element(dialog, "field-row");
            Action(buttons, "Отмена", () => overlay.RemoveFromHierarchy(), "secondary-button");
            Action(buttons, "Удалить", removeRoom, "primary-button");

            void removeRoom()
            {
                overlay.RemoveFromHierarchy();
                _plan.RemoveRoom(room);
                SelectRoom(null);
                Status("Комната удалена.");
            }
        }

        private void OpeningInspector()
        {
            var opening = _opening;
            Text(_panel, opening.IsDoor ? "Дверной проём" : "Окно", "panel-title");

            if (opening.IsDoor)
            {
                var swing = Text(_panel, DoorSwingLabel(opening), "panel-caption");
                Action(_panel, "Развернуть открывание", () => ChangeDoorSwing(opening, !opening.OpensIntoNeighbor, opening.HingeAtEnd, swing), "secondary-button");
                Action(_panel, "Переставить петли", () => ChangeDoorSwing(opening, opening.OpensIntoNeighbor, !opening.HingeAtEnd, swing), "secondary-button");
            }

            var remove = Action(_panel, opening.IsDoor ? "Удалить дверной проём · Delete" : "Удалить окно · Delete", () => DeleteOpening(opening), "danger-button");
            remove.AddToClassList("opening-delete");

            if (opening.IsDoor && !_in3D)
            {
                Action(_panel, "К списку дверей", () => SetTool(PlanTool.Door), "secondary-button");
            }

            Text(_panel, "Размеры в миллиметрах. Смещение измеряется от угла комнаты до центра проёма.", "panel-caption");

            if (!_in3D)
            {
                Text(_panel, "Перетащите проём вдоль стены или потяните за маркер, чтобы изменить ширину.", "section-caption");
            }

            var wall = _plan.WallFor(opening).Value;
            var span = _plan.OpeningSpan(wall, opening);
            Text(_panel, $"Доступная ширина участка — {Mathf.RoundToInt((span.End - span.Start) * 1000f)} мм. Высота стены — {Mathf.RoundToInt(_plan.WallHeight * 1000f)} мм.", "section-caption");
            var size = Element(_panel, "field-row");
            var wallLength = wall.Horizontal ? wall.Room.Bounds.width : wall.Room.Bounds.height;
            var width = Number(size, "Ширина", opening.Width, _plan.MinimumOpeningWidth(opening), wallLength);
            var height = Number(size, "Высота", opening.Height, opening.IsDoor ? 1.8f : .3f, _plan.WallHeight);
            var location = Element(_panel, "field-row");
            var offset = Number(location, "Смещение", opening.Offset, 0f, wallLength);
            var sill = opening.IsDoor ? null : Number(location, "Подоконник", opening.Sill, 0f, _plan.WallHeight - .3f);
            var editError = FieldGroupError(_panel, width, height, offset, sill);
            var apply = Action(_panel, "Применить параметры", applyOpening, "primary-button");
            RequireFields(apply, width, height, offset, sill);

            WallHeightField();

            void applyOpening()
            {
                var elevation = 0f;

                if (!TryMeters(width, out var w) || !TryMeters(height, out var h) || !TryMeters(offset, out var position) || sill != null && (!TryMeters(sill, out elevation) || elevation < 0f))
                {
                    Status("Введите размеры в миллиметрах.", true);
                    return;
                }

                var repositioned = false;

                if (Mathf.Abs(position - opening.Offset) <= .00051f)
                {
                    position = opening.Offset;
                    var available = span.End - span.Start;

                    if (w > opening.Width && w <= available + .0001f)
                    {
                        var center = opening.Center(wall);
                        var half = Mathf.Min(w, available) / 2f;
                        var corrected = Mathf.Clamp(center, span.Start + half, span.End - half);
                        position += corrected - center;
                        repositioned = Mathf.Abs(corrected - center) > .0005f;
                    }
                }

                var result = _plan.UpdateOpening(opening, position, w, h, elevation);

                if (result == OpeningError.None)
                {
                    RebuildStage();
                    RefreshWorkspace();
                }

                ReportOpeningEdit(result, repositioned, editError);
            }
        }

        private void ReportOpeningEdit(OpeningError result, bool repositioned = false, Label fieldError = null)
        {
            var message = result switch
            {
                OpeningError.Size => _opening.IsDoor ? "Минимальный размер двери — 600 × 1800 мм." : "Минимальный размер окна — 350 × 300 мм.",
                OpeningError.Height => "Верх проёма должен помещаться по высоте стены. Для окна учитывается подоконник.",
                OpeningError.Wall => "Такой проём выходит за доступный участок стены.",
                OpeningError.Neighbor => "Между проёмами нужно оставить 120 мм стены.",
                OpeningError.Furniture => "Мебель задевает дверь при открывании или перекрывает проход.",
                _ => repositioned ? "Размеры обновлены. Центр проёма сдвинут, чтобы он поместился на стене." : "Параметры проёма обновлены."
            };

            if (fieldError != null && result != OpeningError.None)
            {
                ShowFieldMessage(fieldError, message);
            }
            else
            {
                Status(message, result != OpeningError.None);
            }
        }

        private void DoorList()
        {
            Text(_panel, "Дверные проёмы", "panel-title");
            Text(_panel, "Щёлкните по общей стене, чтобы добавить дверь. Выберите существующий проём на плане или в списке.", "panel-caption");

            foreach (var opening in _plan.Openings)
            {
                var wall = _plan.WallFor(opening);

                if (opening.IsDoor && wall.HasValue)
                {
                    var value = wall.Value;
                    Action(_panel, $"{value.Room.Name} ↔ {value.Neighbor.Name}", () => SelectOpening(opening, value), "secondary-button");
                }
            }
        }

        private string DoorSwingLabel(Opening opening)
        {
            var wall = _plan.WallFor(opening).Value;
            var target = opening.OpensIntoNeighbor ? wall.Neighbor : wall.Room;

            return $"{wall.Room.Name} ↔ {wall.Neighbor.Name}\nОткрывается в «{target.Name}»";
        }

        private void ChangeDoorSwing(Opening opening, bool opensIntoNeighbor, bool hingeAtEnd, Label label)
        {
            if (!_plan.SetDoorSwing(opening, opensIntoNeighbor, hingeAtEnd))
            {
                Status("Мебель перекрывает открывание с этой стороны.", true);
                return;
            }

            label.text = DoorSwingLabel(opening);
            RebuildStage();
            _canvas.MarkDirtyRepaint();
            Status("Открывание двери обновлено.");
        }

        private void DeleteOpening(Opening opening)
        {
            _plan.RemoveOpening(opening);
            _opening = null;
            _canvas.HoverWall = null;
            _canvas.OpeningPreview = null;
            RebuildStage();
            RefreshWorkspace();
            _viewport.Focus();
            Status(opening.IsDoor ? "Дверной проём удалён. Стена восстановлена." : "Окно удалено. Стена восстановлена.");
        }

        private void WallHeightField()
        {
            Text(_panel, "Высота всех стен", "section-title");
            var controls = Element(_panel, "field-row");
            controls.AddToClassList("height-controls");
            var height = Number(controls, "Высота, мм", _plan.WallHeight, 2.1f, 5f);
            var editError = FieldGroupError(_panel, height);
            var apply = Action(controls, "Применить", applyWallHeight, "secondary-button");
            RequireFields(apply, height);

            void applyWallHeight()
            {
                if (!TryMeters(height, out var value) || value < 2.1f || value > 5f || !_plan.SetWallHeight(value))
                {
                    ShowFieldMessage(editError, "При такой высоте проёмы или мебель выходят за пределы стены.");
                    return;
                }

                RebuildStage();
                RefreshInspector();
                Status($"Высота стен — {Mathf.RoundToInt(value * 1000f)} мм.");
            }
        }

        private void CatalogInspector()
        {
            if (_selectedItem != null)
            {
                ItemInspector(_selectedItem);
            }

            Text(_panel, "Каталог", "panel-title");
            Text(_panel, "Перетащите предмет на пол или нажмите на карточку и выберите место.", "panel-caption");
            var categories = Element(_panel, "categories");
            Category(categories, "Мебель", CatalogCategory.Furniture);
            Category(categories, "Техника", CatalogCategory.Appliances);
            Category(categories, "Декор", CatalogCategory.Decor);
            Category(categories, "Сантехника", CatalogCategory.Plumbing);
            var cards = Element(_panel, "catalog-grid");

            foreach (var entry in _catalog.Entries)
            {
                if (entry.Category == _category)
                {
                    CatalogCard(cards, entry);
                }
            }
        }

        private void Category(VisualElement parent, string label, CatalogCategory category)
        {
            Button button = null;
            button = Action(parent, label, selectCategory, "category");
            button.EnableInClassList("active-category", _category == category);

            void selectCategory()
            {
                var keepFocus = _root.focusController?.focusedElement == button;
                CancelInteraction();
                _category = category;
                RefreshInspector();

                if (keepFocus)
                {
                    _panel.Q<Button>(className: "active-category")?.Focus();
                }
            }
        }

        private void CatalogCard(VisualElement parent, CatalogEntry entry)
        {
            var card = Element(parent, "catalog-card");
            card.focusable = true;
            var thumbnail = Element(card, "thumbnail");
            thumbnail.pickingMode = PickingMode.Ignore;
            thumbnail.style.backgroundImage = Background.FromRenderTexture(_stage.Thumbnail(entry));
            Text(card, entry.Name, "catalog-name");
            Text(card, $"{entry.Width * 100f:0} × {entry.Depth * 100f:0} см", "catalog-size");
            card.RegisterCallback<PointerDownEvent>(onPointerDown);
            card.RegisterCallback<KeyDownEvent>(onKeyDown);

            void onPointerDown(PointerDownEvent evt)
            {
                if (evt.button == 0)
                {
                    StartPlacement(entry, null);
                    Capture(Gesture.Catalog, evt.pointerId, evt.position);
                    evt.StopPropagation();
                }
            }

            void onKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode is KeyCode.Return or KeyCode.Space)
                {
                    StartPlacement(entry, null);
                    evt.StopPropagation();
                }
            }
        }

        private void ItemInspector(PlacedItem item)
        {
            var itemPanel = Element(_panel, "item-inspector");
            Text(itemPanel, item.Entry.Name, "section-title");
            _rotationReadout = AddSlider(itemPanel, "Угол, °", 0f, 360f, Mathf.Repeat(item.Angle, 360f), rotateItem);

            var snap = Element(itemPanel, "field-row");
            Action(snap, "−15°", () => TurnSelected(-15f), "secondary-button");
            Action(snap, "+15°", () => TurnSelected(15f), "secondary-button");

            var actions = Element(itemPanel, "field-row");
            Action(actions, "Переместить", () => StartPlacement(item.Entry, item), "secondary-button");
            Action(actions, "Удалить", () => DeleteItem(item), "danger-button");

            float rotateItem(float value)
            {
                RotateItem(item, value);
                var accepted = Mathf.Repeat(item.Angle, 360f);

                return value == 360f && accepted == 0f ? 360f : accepted;
            }
        }

        private void FinishInspector()
        {
            Text(_panel, "Материалы", "panel-title");

            if (_tool == PlanTool.Paint)
            {
                Text(_panel, "Выберите покрытие, затем щёлкните по видимой стороне стены.", "panel-caption");
                var options = Element(_panel, "field-row");
                paintMode("Одна стена", false);
                paintMode("Вся комната", true);
                FinishGroup(_materials.Walls, _brushFinish, index => _brushFinish = index, false);

                return;

                void paintMode(string caption, bool wholeRoom)
                {
                    var button = Action(options, caption, selectPaintMode, "secondary-button");
                    button.EnableInClassList("active-choice", _paintRoom == wholeRoom);

                    void selectPaintMode()
                    {
                        _paintRoom = wholeRoom;
                        RefreshInspector();
                    }
                }
            }

            Action(_panel, "Малярная кисть", () => SetTool(PlanTool.Paint), "secondary-button");
            var room = _room;
            Text(_panel, room == null ? "Выберите комнату в списке или на плане." : $"Комната · {room.Name}", "panel-caption");

            if (room != null)
            {
                Text(_panel, "Пол", "section-title");
                FinishGroup(_materials.Floors, room.Floor, index => room.Floor = index);
                Text(_panel, "Стены", "section-title");
                FinishGroup(_materials.Walls, room.Walls, room.PaintWalls);
                Action(_panel, "Применить ко всем комнатам", applyToAllRooms, "secondary-button");

                void applyToAllRooms()
                {
                    foreach (var other in _plan.Rooms)
                    {
                        other.Floor = room.Floor;
                        other.PaintWalls(room.Walls);
                    }

                    RebuildStage();
                    Status("Материалы применены во всех комнатах.");
                }
            }

            WallHeightField();
        }

        private void FinishGroup(IReadOnlyList<Finish> finishes, int selected, Action<int> apply, bool updateModel = true)
        {
            var group = Element(_panel, "finish-group");

            for (var index = 0; index < finishes.Count; index++)
            {
                var choice = index;
                var finish = finishes[index];
                var button = Action(group, finish.Name, selectFinish, "finish-button");
                button.EnableInClassList("active-finish", choice == selected);
                var tile = Element(button, "finish-chip");
                tile.pickingMode = PickingMode.Ignore;
                tile.style.backgroundColor = finish.Color;
                tile.style.backgroundImage = new StyleBackground(finish.Texture);

                void selectFinish()
                {
                    apply(choice);

                    if (updateModel)
                    {
                        RebuildStage();
                    }

                    RefreshInspector();
                    Status($"Материал · {finish.Name}");
                }
            }
        }

        private void LightInspector()
        {
            Text(_panel, "Освещение", "panel-title");
            var presets = Element(_panel, "field-row");
            Action(presets, "День", () => LightingPreset(1.55f, .16f, -42f), "secondary-button");
            Action(presets, "Закат", () => LightingPreset(1.17f, .85f, 51f), "secondary-button");
            Action(presets, "Облачно", () => LightingPreset(.65f, .33f, -14f), "secondary-button");
            LightSlider("Интенсивность", .10f, 2.5f, _light, value => _light = value);
            LightSlider("Теплота", 0f, 1f, _warmth, value => _warmth = value);
            LightSlider("Направление", -180f, 180f, _sunAngle, value => _sunAngle = value);

        }

        private void LightingPreset(float light, float warmth, float angle)
        {
            _light = light;
            _warmth = warmth;
            _sunAngle = angle;
            _stage.SetLighting(_light, _warmth, _sunAngle);
            RefreshInspector();
        }

        private void LightSlider(string name, float min, float max, float value, Action<float> apply)
        {
            AddSlider(_panel, name, min, max, value, applyLighting);

            float applyLighting(float next)
            {
                apply(next);
                _stage.SetLighting(_light, _warmth, _sunAngle);

                return next;
            }
        }

        private (Slider Track, TextField Input) AddSlider(VisualElement parent, string name, float min, float max, float value, Func<float, float> apply)
        {
            var group = Element(parent, "slider-field");
            Text(group, name, "slider-label");
            var controls = Element(group, "slider-controls");
            var slider = new Slider(min, max)
            {
                value = value
            };
            slider.AddToClassList("studio-slider");
            controls.Add(slider);
            var input = new TextField
            {
                value = FormatNumber(value),
                maxLength = 10
            };
            input.AddToClassList("slider-value");
            controls.Add(input);
            BindNumber(group, input, min, max, false);
            var syncedText = input.value;

            slider.RegisterValueChangedCallback(evt => setValue(evt.newValue));
            input.RegisterCallback<FocusOutEvent>(_ => commitInput());
            input.RegisterCallback<KeyDownEvent>(onSliderKeyDown);

            return (slider, input);

            void setValue(float requested)
            {
                var accepted = float.IsFinite(requested) ? apply(Mathf.Clamp(requested, min, max)) : slider.value;
                slider.SetValueWithoutNotify(accepted);
                SetNumericText(input, accepted);
                syncedText = input.value;
            }

            void commitInput()
            {
                if (input.value != syncedText && ValidField(input) && TryNumber(input.value, out var next))
                {
                    setValue(next);
                }
            }

            void onSliderKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    commitInput();
                    evt.StopPropagation();
                }
            }
        }

        private void RebuildStage()
        {
            if (_in3D)
            {
                _stage.Rebuild();
                RefreshModelSelection();
            }
        }
    }
}
