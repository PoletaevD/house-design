using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

namespace HouseDesign
{
    [DisallowMultipleComponent]
    public sealed partial class ApartmentStudio : MonoBehaviour
    {
#pragma warning disable IDE0044 // Unity assigns this field from the scene.
        [SerializeField]
        private Material _litMaterial = null;
#pragma warning restore IDE0044

        private readonly ApartmentPlan _plan = new();
        private StudioMaterials _materials;
        private StudioGeometry _geometry;
        private ApartmentStage _stage;
        private FurnitureCatalog _catalog;

        private PanelSettings _panelSettings;
        private VisualElement _root;
        private VisualElement _viewport;
        private Button _toolsToggle;
        private Button _detailsToggle;
        private VisualElement _tools;
        private VisualElement _frameTools;
        private VisualElement _rooms;
        private VisualElement _inspectorTabs;
        private ScrollView _inspector;
        private PlanCanvas _canvas;
        private ModelSelectionCanvas _modelSelection;
        private Label _selectionLabel;
        private Label _status;
        private Label _metrics;
        private Label _viewTitle;
        private Label _viewHelp;
        private Label _placementHint;
        private Button _planButton;
        private Button _modelButton;

        private Tween _modeTween;
        private Tween _statusTween;

        private Room _room;
        private Opening _opening;
        private PlacedItem _selectedItem;
        private PlanTool _tool;
        private StudioTab _tab;
        private CatalogCategory _category;
        private bool _in3D;
        private bool _narrowWorkspace;
        private WorkspacePanel _openPanel;
        private bool _projectFramePending;

        private bool _cutaway = true;
        private float _light = 1.3f;
        private float _warmth = .28f;
        private float _sunAngle = -36f;

        private enum PlanTool
        {
            Select,
            Room,
            Door,
            Window,
            Measure,
            Paint
        }

        private enum StudioTab
        {
            Catalog,
            Finishes,
            Light
        }

        private enum WorkspacePanel
        {
            None,
            Tools,
            Details
        }

        private void Awake()
        {
            _materials = new StudioMaterials(_litMaterial);
            _geometry = new StudioGeometry();
            _catalog = new FurnitureCatalog(_materials);
            _stage = new ApartmentStage(transform, _plan, _materials, _geometry, _catalog);

            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("ApartmentTheme");
            _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panelSettings.referenceResolution = new Vector2Int(1280, 720);
            _panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panelSettings.match = 1f;

            var ui = new GameObject("Studio workspace");
            ui.transform.SetParent(transform, false);
            ui.SetActive(false);
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            ui.SetActive(true);

            _root = document.rootVisualElement;
            _root.styleSheets.Add(Resources.Load<StyleSheet>("ApartmentStudio"));
            _root.AddToClassList("studio");

            BuildWorkspace();
            BindInput();
            LoadDemo();
        }

        private void OnDestroy()
        {
            ReleasePoster();
            _modeTween?.Kill();
            _statusTween?.Kill();

            if (_modelSelection != null)
            {
                _stage.ViewChanged -= _modelSelection.RefreshProjection;
            }

            _stage?.Dispose();
            _geometry?.Dispose();
            _materials?.Dispose();

            if (_panelSettings != null)
            {
                Destroy(_panelSettings);
            }
        }

        private void BuildWorkspace()
        {
            var header = Element(_root, "header");
            var brand = Element(header, "brand");
            Text(brand, "A", "brand-mark");
            var brandText = Element(brand, "brand-text");
            Text(brandText, "ATELIER", "brand-name");
            Text(brandText, "Студия интерьера", "brand-subtitle");

            var modes = Element(header, "mode-switch");
            _planButton = Action(modes, "2D    Чертёж", () => ChangeMode(false), "mode-button");
            _modelButton = Action(modes, "3D    Интерьер", () => ChangeMode(true), "mode-button");

            var projects = Element(header, "projects");
            Action(projects, "Импорт", ImportProject, "top-action");
            Action(projects, "Экспорт", ExportProject, "top-action");
            Action(projects, "Постер", ExportPoster, "top-action");
            Action(projects, "Пример", () => RequestReset(true), "top-action");
            Action(projects, "Новый план", () => RequestReset(false), "top-action");

            var panelControls = Element(_root, "panel-controls");
            _toolsToggle = Action(panelControls, "Инструменты и комнаты", () => ToggleWorkspacePanel(WorkspacePanel.Tools), "top-action");
            _detailsToggle = Action(panelControls, "Параметры", () => ToggleWorkspacePanel(WorkspacePanel.Details), "top-action");

            var body = Element(_root, "body");
            var sidebar = Element(body, "sidebar");
            var sidebarScroll = new ScrollView
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden
            };
            sidebarScroll.AddToClassList("sidebar-scroll");
            sidebar.Add(sidebarScroll);
            var sidebarContent = Element(sidebarScroll.contentContainer, "sidebar-content");
            Text(sidebarContent, "ПРОСТРАНСТВО", "sidebar-heading");
            _tools = Element(sidebarContent, "tool-list");
            Text(sidebarContent, "КОМНАТЫ", "sidebar-heading");
            _rooms = Element(sidebarContent, "room-list");

            _viewport = Element(body, "viewport");
            _viewport.focusable = true;
            _canvas = new PlanCanvas(_plan);
            _canvas.AddToClassList("plan-canvas");
            _viewport.Add(_canvas);
            _modelSelection = new ModelSelectionCanvas(_stage);
            _modelSelection.AddToClassList("selection-canvas");
            _viewport.Add(_modelSelection);
            _stage.ViewChanged += _modelSelection.RefreshProjection;
            var viewportHeading = Element(_viewport, "viewport-heading");
            viewportHeading.pickingMode = PickingMode.Ignore;
            _viewTitle = Text(viewportHeading, "План квартиры", "viewport-title");
            _viewHelp = Text(viewportHeading, "", "viewport-help");
            viewportHeading.RegisterCallback<GeometryChangedEvent>(_ => _canvas.KeepLabelsClear(_canvas.WorldToLocal(viewportHeading.worldBound)));
            var viewportFooter = Element(_viewport, "viewport-footer");
            viewportFooter.pickingMode = PickingMode.Ignore;
            _frameTools = Element(viewportFooter, "viewport-actions");
            Action(_frameTools, "−", () => ZoomCenter(4f), "viewport-button").tooltip = "Уменьшить масштаб";
            Action(_frameTools, "+", () => ZoomCenter(-4f), "viewport-button").tooltip = "Увеличить масштаб";
            Action(_frameTools, "В кадр", Fit, "viewport-button");
            _selectionLabel = Text(viewportFooter, "", "model-selection-label");
            _selectionLabel.RegisterCallback<GeometryChangedEvent>(_ => SizeModelSelectionLabel());
            _placementHint = Text(_viewport, "", "placement-hint");
            _placementHint.style.display = DisplayStyle.None;
            Element(_viewport, "viewport-focus").pickingMode = PickingMode.Ignore;

            var details = Element(body, "details");
            _inspectorTabs = Element(details, "inspector-tabs");
            _inspector = new ScrollView
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden
            };
            _inspector.AddToClassList("inspector");
            _inspector.verticalScroller.AddToClassList("workspace-scrollbar");
            details.Add(_inspector);

            var statusBar = Element(_root, "statusbar");
            _status = Text(statusBar, "", "status-text");
            _metrics = Text(statusBar, "", "metrics");
            _viewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateViewport());
            _root.RegisterCallback<GeometryChangedEvent>(onWorkspaceGeometryChanged);

            void onWorkspaceGeometryChanged(GeometryChangedEvent evt)
            {
                var width = evt.newRect.width;
                var narrow = width < 760f;

                if (narrow != _narrowWorkspace)
                {
                    _narrowWorkspace = narrow;
                    var scrollbar = narrow ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
                    _inspector.verticalScrollerVisibility = scrollbar;

                    if (narrow)
                    {
                        sidebar.BringToFront();
                    }
                    else
                    {
                        sidebar.SendToBack();
                    }
                }

                _root.EnableInClassList("narrow-workspace", _narrowWorkspace);

                if (!_narrowWorkspace)
                {
                    _openPanel = WorkspacePanel.None;
                }

                RefreshWorkspacePanels();
                UpdateViewport();
            }
        }

        private void ToggleWorkspacePanel(WorkspacePanel panel)
        {
            _openPanel = _openPanel == panel ? WorkspacePanel.None : panel;
            RefreshWorkspacePanels();
        }

        private void CloseWorkspacePanel()
        {
            if (_openPanel != WorkspacePanel.None)
            {
                ToggleWorkspacePanel(_openPanel);
            }
        }

        private void RefreshWorkspacePanels()
        {
            var toolsOpen = _openPanel == WorkspacePanel.Tools;
            var detailsOpen = _openPanel == WorkspacePanel.Details;
            _root.EnableInClassList("show-tools", toolsOpen);
            _root.EnableInClassList("show-details", detailsOpen);
            _toolsToggle.EnableInClassList("active-panel", toolsOpen);
            _detailsToggle.EnableInClassList("active-panel", detailsOpen);
            _toolsToggle.text = toolsOpen ? "Скрыть инструменты" : "Инструменты и комнаты";
            _detailsToggle.text = detailsOpen ? "Скрыть панель" : _stage.Walking ? "Освещение" : _in3D ? "Каталог и настройки" : "Параметры";
        }

        private void ChangeMode(bool model)
        {
            if (_in3D == model)
            {
                return;
            }

            if (model && _plan.Rooms.Count == 0)
            {
                Status("Добавьте хотя бы одну комнату, чтобы открыть интерьер.", true);
                return;
            }

            CancelInteraction();

            if (model || _stage.Walking)
            {
                _stage.EndWalk(!model);
            }

            _modeTween?.Kill();
            _in3D = model;
            _tool = PlanTool.Select;
            _opening = null;
            _selectedItem = null;
            _viewport.EnableInClassList("model-view", model);
            _canvas.pickingMode = model ? PickingMode.Ignore : PickingMode.Position;
            _canvas.style.display = DisplayStyle.Flex;
            _planButton.EnableInClassList("active-mode", !model);
            _modelButton.EnableInClassList("active-mode", model);

            if (model)
            {
                UpdateViewport();
                _stage.Show(_canvas.ToPlan(_canvas.layout.size * .5f), _canvas.WorldDistance(_canvas.layout.height / 2f));
                _modeTween = Fade(_canvas, 0f, .47f).OnComplete(() => _canvas.style.display = DisplayStyle.None);
            }
            else
            {
                _canvas.Refresh();
                _modeTween = Fade(_canvas, 1f, .32f).OnComplete(_stage.Hide);
            }

            RefreshWorkspace();
            CloseWorkspacePanel();
            Status(model ? "Перетащите объект из каталога на пол." : "Выберите инструмент или перетащите комнату на чертеже.");
        }

        private Tween Fade(VisualElement element, float opacity, float duration)
        {
            var value = element.style.opacity.keyword == StyleKeyword.Undefined ? element.style.opacity.value : element.resolvedStyle.opacity;

            return DOTween.To(() => value, setOpacity, opacity, duration).SetEase(Ease.InOutCubic);

            void setOpacity(float next)
            {
                value = next;
                element.style.opacity = next;
            }
        }

        private void UpdateViewport()
        {
            _stage.SetViewport(_viewport.worldBound, _root.worldBound);
            ApplyProjectFrame();
        }

        private void Fit()
        {
            if (_stage.ChangingWalkView)
            {
                return;
            }

            if (_in3D)
            {
                _stage.Fit();
            }
            else
            {
                _canvas.Fit();
            }
        }

        private void FrameProjectViews()
        {
            _stage.ForgetWalkPosition();
            _projectFramePending = true;
            ApplyProjectFrame();

            if (_projectFramePending)
            {
                _viewport.schedule.Execute(ApplyProjectFrame);
            }
        }

        private void ApplyProjectFrame()
        {
            var size = _viewport.layout.size;

            if (!_projectFramePending || !(size.x > 1f && size.y > 1f && _root.worldBound.width > 1f && _root.worldBound.height > 1f))
            {
                return;
            }

            _projectFramePending = false;
            _canvas.Fit(size);
            _stage.EndWalk();
            _stage.SetViewport(_viewport.worldBound, _root.worldBound);
            _stage.Fit();
        }

        private void ZoomCenter(float delta)
        {
            if (_stage.ChangingWalkView)
            {
                return;
            }

            if (_in3D)
            {
                _stage.Zoom(delta);
            }
            else
            {
                _canvas.Zoom(_canvas.layout.size * .5f, delta);
            }
        }

        private void RefreshWorkspace()
        {
            _planButton.EnableInClassList("active-mode", !_in3D);
            _modelButton.EnableInClassList("active-mode", _in3D);
            _viewTitle.text = _stage.Walking ? "Прогулка по квартире" : _in3D ? "Интерьер квартиры" : "План квартиры";
            _viewHelp.text = _stage.Walking ? "WASD или стрелки — идти    ·    ПКМ — осмотреться    ·    Esc — выход" : _in3D ? "ПКМ — орбита    ·    СКМ — сдвиг    ·    Колесо — масштаб" : "Сетка 100 мм    ·    СКМ — сдвиг    ·    Колесо — масштаб";
            _frameTools.style.display = _stage.Walking ? DisplayStyle.None : DisplayStyle.Flex;
            RefreshWorkspacePanels();
            _canvas.SelectedRoom = _opening == null && _tool != PlanTool.Measure ? _room : null;
            _canvas.SelectedOpening = _opening;
            _canvas.Refresh();
            RefreshModelSelection();
            RefreshTools();
            RefreshRooms();
            RefreshInspector();
            var area = 0f;

            foreach (var room in _plan.Rooms)
            {
                area += room.Bounds.width * room.Bounds.height;
            }

            _metrics.text = $"Комнат {_plan.Rooms.Count}    /    {area:0.0} м²    /    {_plan.Items.Count} предм.";
        }

        private void RefreshModelSelection()
        {
            var show = _in3D && _placement == null && !_stage.Walking && _tool != PlanTool.Paint;
            var item = show ? _selectedItem : null;
            var opening = show ? _opening : null;
            var room = show && item == null && opening == null ? _room : null;
            _modelSelection.SetSelection(room, item, opening);
            _selectionLabel.style.display = item != null || room != null || opening != null ? DisplayStyle.Flex : DisplayStyle.None;
            _selectionLabel.EnableInClassList("item-selected", item != null);
            _selectionLabel.text = opening != null ? (opening.IsDoor ? "Дверной проём" : "Окно") : item != null ? $"Предмет · {item.Entry.Name}" : room != null ? $"Комната · {room.Name}" : "";
            SizeModelSelectionLabel();
        }

        private void SizeModelSelectionLabel()
        {
            if (_selectionLabel.text.Length == 0)
            {
                return;
            }

            var size = _selectionLabel.MeasureTextSize(_selectionLabel.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            var style = _selectionLabel.resolvedStyle;
            var width = size.x + style.paddingLeft + style.paddingRight + style.borderLeftWidth + style.borderRightWidth;

            if (width > 0f && float.IsFinite(width))
            {
                _selectionLabel.style.width = Mathf.Ceil(width) + 2f;
            }
        }

        private void RefreshTools()
        {
            _tools.Clear();

            if (_stage.Walking)
            {
                Action(_tools, "Выйти из прогулки", ToggleWalk, "tool-button").AddToClassList("walk-exit");
                return;
            }

            Tool("↖", _in3D ? "Выбор объекта" : "Выбор и сдвиг", PlanTool.Select);

            if (!_in3D)
            {
                Tool("□", "Контур комнаты", PlanTool.Room);
                Tool("∟", "Дверные проёмы", PlanTool.Door);
                Tool("▤", "Окно", PlanTool.Window);
                Tool("↔", "Рулетка", PlanTool.Measure);
            }
            else
            {
                Action(_tools, "⇥    Прогулка", ToggleWalk, "tool-button");
                Tool("▧", "Малярная кисть", PlanTool.Paint);
                Action(_tools, _cutaway ? "▱    Срез стен вкл." : "▱    Полные стены", toggleCutaway, "tool-button");

                void toggleCutaway()
                {
                    _cutaway = !_cutaway;
                    _stage.SetCutaway(_cutaway);
                    RefreshTools();
                }
            }
        }

        private void Tool(string symbol, string name, PlanTool tool)
        {
            var button = Action(_tools, $"{symbol}  {name}", () => SetTool(tool), "tool-button");
            button.EnableInClassList("active-tool", _tool == tool);
        }

        private void SetTool(PlanTool tool)
        {
            CancelInteraction();
            _tool = tool;
            _opening = null;
            _selectedItem = null;

            if (tool == PlanTool.Paint)
            {
                _tab = StudioTab.Finishes;
            }

            RefreshWorkspace();
            CloseWorkspacePanel();
            _viewport.Focus();
            var hint = tool switch
            {
                PlanTool.Room => "Проведите от угла к углу. Края примагнитятся к сетке и соседним стенам.",
                PlanTool.Door => "Щёлкните по общей стене для новой двери или выберите существующую в списке справа.",
                PlanTool.Window => "Щёлкните по внешней стене. Размер и высота доступны в свойствах.",
                PlanTool.Measure => "Щёлкните в двух точках на плане, чтобы измерить расстояние.",
                PlanTool.Paint => "Выберите материал справа и щёлкните по стене в 3D.",
                _ => _in3D ? "Щёлкните по комнате или предмету." : "Перетащите комнату или потяните за любой из четырёх углов."
            };
            Status(hint);
        }

        private void RefreshRooms()
        {
            _rooms.Clear();

            if (_plan.Rooms.Count == 0)
            {
                Text(_rooms, "Комнат пока нет", "empty-rooms");
            }

            foreach (var room in _plan.Rooms)
            {
                var button = Action(_rooms, $"{room.Name}\n{room.Bounds.width * room.Bounds.height:0.0} м²", () => SelectRoom(room), "room-button");
                button.EnableInClassList("active-room", room == _room);
            }
        }

        private void SelectRoom(Room room)
        {
            if (_stage.Walking)
            {
                return;
            }

            _room = room;
            _opening = null;
            _selectedItem = null;

            if (!_in3D && room != null)
            {
                _tool = PlanTool.Select;
            }

            RefreshWorkspace();

            if (_openPanel == WorkspacePanel.Tools)
            {
                CloseWorkspacePanel();
                _viewport.Focus();
            }
        }

        private void Status(string message, bool error = false)
        {
            _statusTween?.Kill();
            _status.text = message;
            _status.EnableInClassList("status-error", error);
            _status.style.opacity = .72f;
            _statusTween = Fade(_status, 1f, .22f);
        }

        private void RequestReset(bool demo)
        {
            if (_plan.Rooms.Count == 0)
            {
                ResetWorkspace(demo);
                return;
            }

            var overlay = Element(_root, "modal-shade");
            var dialog = Element(overlay, "modal");
            Text(dialog, demo ? "Открыть пример?" : "Начать новый план?", "panel-title");
            Text(dialog, "Текущая планировка и расстановка будут удалены.", "panel-caption");
            var actions = Element(dialog, "field-row");
            Action(actions, "Отмена", () => overlay.RemoveFromHierarchy(), "secondary-button");
            Action(actions, demo ? "Открыть" : "Создать", confirmReset, "primary-button");
            overlay.style.opacity = 0f;
            Fade(overlay, 1f, .18f);

            void confirmReset()
            {
                overlay.RemoveFromHierarchy();
                ResetWorkspace(demo);
            }
        }

        private void ResetWorkspace(bool demo)
        {
            if (_in3D)
            {
                ChangeMode(false);
            }

            _plan.Reset();
            _opening = null;
            _selectedItem = null;
            _room = null;
            _tool = PlanTool.Room;

            if (demo)
            {
                LoadDemo();
                return;
            }

            RefreshWorkspace();
            FrameProjectViews();
            Status("Нарисуйте первую комнату на сетке.");
        }

        private void LoadDemo()
        {
            var living = _plan.AddRoom("Гостиная", new Rect(0f, 0f, 4.8f, 4.3f));
            var bedroom = _plan.AddRoom("Спальня", new Rect(4.8f, 0f, 4.5f, 3.3f));
            var hall = _plan.AddRoom("Прихожая", new Rect(4.8f, 3.3f, 4.5f, 2.15f));
            var kitchen = _plan.AddRoom("Кухня", new Rect(0f, 4.3f, 4.8f, 3.55f));
            var bath = _plan.AddRoom("Ванная", new Rect(4.8f, 5.45f, 4.5f, 2.4f));
            bedroom.Floor = 1;
            bedroom.Walls = 1;
            hall.Floor = 2;
            kitchen.Floor = 2;
            kitchen.Walls = 3;
            bath.Floor = 3;
            bath.Walls = 2;
            AddDemoOpening(new Vector2(4.8f, 3.8f), true);
            AddDemoOpening(new Vector2(6.45f, 3.3f), true);
            AddDemoOpening(new Vector2(4.8f, 4.88f), true);
            AddDemoOpening(new Vector2(6.5f, 5.45f), true);
            AddDemoOpening(new Vector2(2.3f, 0f), false, 2.3f);
            AddDemoOpening(new Vector2(6.5f, 0f), false, 1.75f);
            AddDemoOpening(new Vector2(0f, 5.8f), false);
            AddDemoOpening(new Vector2(8.15f, 7.85f), false, .8f);
            var sofa = new Vector2(2.44f, .82f);
            var television = new Vector2(2.44f, 3.95f);
            var armchair = new Vector2(1.02f, 2.55f);
            AddDemoItem(FurnitureKind.Sofa, sofa, television - sofa);
            AddDemoItem(FurnitureKind.Armchair, armchair, television - armchair);
            AddDemoItem(FurnitureKind.Television, television, sofa - television);
            AddDemoItem(FurnitureKind.Plant, new Vector2(.55f, .65f), Vector2.one);
            AddDemoItem(FurnitureKind.Bed, new Vector2(6.30f, 1.23f), Vector2.up);
            AddDemoItem(FurnitureKind.Wardrobe, new Vector2(8.90f, 1.24f), Vector2.left);
            AddDemoItem(FurnitureKind.Lamp, new Vector2(5.13f, .47f), Vector2.up);
            AddDemoItem(FurnitureKind.Table, new Vector2(2.06f, 5.85f), Vector2.up);
            AddDemoItem(FurnitureKind.Refrigerator, new Vector2(.69f, 7.39f), Vector2.down);
            AddDemoItem(FurnitureKind.KitchenSet, new Vector2(2.78f, 7.44f), Vector2.down);
            AddDemoItem(FurnitureKind.Sink, new Vector2(6.03f, 7.50f), Vector2.down);
            AddDemoItem(FurnitureKind.Washer, new Vector2(7.18f, 7.43f), Vector2.down);
            AddDemoItem(FurnitureKind.Bathtub, new Vector2(8.81f, 6.65f), Vector2.left);
            _room = living;
            _tool = PlanTool.Select;
            RefreshWorkspace();
            FrameProjectViews();
            Status("Измените пример или создайте свою планировку.");
        }

        private void AddDemoOpening(Vector2 point, bool door, float width = 0f)
        {
            var wall = _plan.FindWall(point, .05f, door);

            if (wall.HasValue)
            {
                var opening = _plan.PreviewOpening(wall.Value, point, door);

                if (width > 0f)
                {
                    opening.Width = width;
                }

                _plan.AddOpening(wall.Value, opening);
            }
        }

        private void AddDemoItem(FurnitureKind kind, Vector2 point, Vector2 facing)
        {
            foreach (var entry in _catalog.Entries)
            {
                if (entry.Kind == kind)
                {
                    // Catalog model fronts face local -Z, which is positive Y on the plan.
                    var angle = Mathf.Atan2(-facing.x, facing.y) * Mathf.Rad2Deg;
                    _plan.AddItem(entry, point, angle);

                    return;
                }
            }
        }

        private VisualElement Element(VisualElement parent, string cssClass)
        {
            var element = new VisualElement();
            element.AddToClassList(cssClass);
            parent.Add(element);

            return element;
        }

        private Label Text(VisualElement parent, string value, string cssClass)
        {
            var label = new Label(value)
            {
                pickingMode = PickingMode.Ignore
            };
            label.AddToClassList(cssClass);
            parent.Add(label);

            return label;
        }

        private Button Action(VisualElement parent, string title, Action handler, string cssClass)
        {
            var button = new Button(handler)
            {
                text = title
            };
            button.AddToClassList(cssClass);
            parent.Add(button);

            return button;
        }
    }
}
