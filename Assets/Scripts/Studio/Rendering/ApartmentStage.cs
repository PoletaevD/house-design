using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HouseDesign
{
    internal sealed partial class ApartmentStage
    {
        private const int PreviewLayer = 30;
        private const int ExteriorLayer = 28;
        internal const float WallThickness = .13f;
        private const float DefaultOrbitDistance = 29f;
        private const float OrbitFramingHeight = .82f;

        private readonly ApartmentPlan _plan;
        private readonly StudioMaterials _materials;
        private readonly StudioGeometry _geometry;
        private readonly FurnitureCatalog _catalog;
        private readonly Transform _root;
        private readonly Camera _camera;
        private readonly Light _sun;
        private readonly Light _fill;
        private readonly GameObject _windowDaylight;
        private readonly Material _edge;
        private readonly Material _ceiling;
        private readonly Material _frame;
        private readonly Material _glass;
        private readonly Material _door;
        private readonly Material _valid;
        private readonly Material _invalid;
        private readonly Color _overviewBackground;

        private readonly Dictionary<Collider, PlacedItem> _itemHits = new();
        private readonly Dictionary<Collider, PlanWall> _wallHits = new();
        private readonly Dictionary<Collider, Opening> _openingHits = new();
        private readonly Dictionary<Opening, BoxCollider[]> _openingVisuals = new();
        private readonly Dictionary<PlacedItem, Transform> _itemVisuals = new();
        private readonly List<(Light Source, Vector3 Inward, float Area)> _daylight = new();
        private readonly Dictionary<CatalogEntry, RenderTexture> _thumbnails = new();
        private readonly List<(Renderer[] Walls, Collider[] Hits, Transform Low, Vector2 Inward)> _cutWalls = new();

        private Transform _structure;
        private Transform _ceilings;
        private Transform _items;
        private Transform _ghost;
        private Transform _ghostModel;
        private Renderer[] _ghostRenderers;
        private bool _ghostValid;

        private Tween _riseTween;
        private Tween _cameraTween;
        private Tween _zoomTween;
        private Tween _lightTween;

        private Vector3 _target;
        private float _orbitDistance = DefaultOrbitDistance;
        private float _fitSize = 4f;
        private float _orbitAspect = 1f;
        private float _yaw = -24f;
        private float _pitch = 54f;
        private bool _cutaway = true;

        private float _lightAmount = 1.3f;
        private float _lightWarmth = .28f;
        private float _lightAngle = -36f;

        internal event Action ViewChanged;

        internal ApartmentStage(Transform owner, ApartmentPlan plan, StudioMaterials materials, StudioGeometry geometry, FurnitureCatalog catalog)
        {
            _plan = plan;
            _materials = materials;
            _geometry = geometry;
            _catalog = catalog;

            _root = Child(owner, "Apartment model");
            _root.gameObject.SetActive(false);

            _camera = Child(owner, "Apartment camera").gameObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _overviewBackground = _materials.Hex("#D9D1C5");
            _camera.backgroundColor = _overviewBackground;
            _camera.cullingMask &= ~((1 << PreviewLayer) | (1 << ExteriorLayer));
            _camera.nearClipPlane = .1f;
            _camera.farClipPlane = 70f;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.GetUniversalAdditionalCameraData().requiresColorOption = CameraOverrideOption.Off;

            _sun = CreateLight(owner, "Sun", LightShadows.Soft);
            _fill = CreateLight(owner, "Fill", LightShadows.None);
            _windowDaylight = Resources.Load<GameObject>("WindowDaylight");
            _fill.transform.rotation = Quaternion.Euler(61f, 135f, 0f);

            _edge = materials.ColorMaterial("#AAA098", .24f);
            _ceiling = materials.ColorMaterial("#F4EEE5", .12f);
            _frame = materials.ColorMaterial("#524843", .35f);
            _glass = Resources.Load<Material>("ApartmentGlass");
            _door = materials.ColorMaterial("#9C7861", .22f);
            _valid = materials.ColorMaterial("#60B8AE", .28f);
            _invalid = materials.ColorMaterial("#D56169", .28f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.skybox = null;
            RenderSettings.reflectionIntensity = 0f;
            ApplyLighting(_lightAmount, _lightWarmth, _lightAngle);
        }

        internal void SetViewport(Rect viewport, Rect panel)
        {
            if (panel.width > 0f && panel.height > 0f && viewport.width > 1f && viewport.height > 1f)
            {
                var aspect = viewport.width / viewport.height;

                if (!Mathf.Approximately(_camera.aspect, aspect) && _root.gameObject.activeSelf && !Walking && !ChangingWalkView && _cameraTween?.IsActive() != true)
                {
                    UpdateOrbitAspect(aspect);
                }

                _camera.rect = new Rect((viewport.xMin - panel.xMin) / panel.width, (panel.yMax - viewport.yMax) / panel.height, viewport.width / panel.width, viewport.height / panel.height);
                _camera.aspect = aspect;
                NotifyViewChanged();
            }
        }

        internal void Show(Vector2 planCenter, float planSize)
        {
            _root.gameObject.SetActive(true);
            Rebuild();
            _yaw = -24f;
            _pitch = 54f;
            Fit();

            var endTarget = _target;
            var endSize = _camera.orthographicSize;
            var startTarget = World(planCenter);
            var startSize = Mathf.Max(1f, planSize);
            var progress = 0f;
            _cameraTween?.Kill();
            setView(0f);
            _cameraTween = DOTween.To(() => progress, setView, 1f, .86f).SetEase(Ease.InOutCubic);

            void setView(float value)
            {
                progress = value;
                _pitch = Mathf.Lerp(89.9f, 54f, value);
                _yaw = Mathf.Lerp(0f, -24f, value);
                _target = Vector3.Lerp(startTarget, endTarget, value);
                _camera.orthographicSize = Mathf.Lerp(startSize, endSize, value);
                ApplyCamera();
            }

            _riseTween?.Kill();
            _structure.localScale = new Vector3(1f, .025f, 1f);
            _items.localScale = _structure.localScale;
            _riseTween = DOTween.To(() => _structure.localScale.y, raiseStructure, 1f, .72f).SetEase(Ease.OutCubic);

            void raiseStructure(float value)
            {
                var scale = new Vector3(1f, value, 1f);
                _structure.localScale = scale;
                _items.localScale = scale;
                NotifyViewChanged();
            }
        }

        internal void Hide()
        {
            StopModeTweens();
            EndGhost();
            _root.gameObject.SetActive(false);
        }

        internal void Rebuild()
        {
            _riseTween?.Kill(true);
            DestroyChild(_structure);
            DestroyChild(_items);
            DestroyChild(_exterior);
            _exterior = null;
            _structure = Child(_root, "Floors and walls");
            _ceilings = Child(_structure, "Ceilings");
            _items = Child(_root, "Placed objects");
            _cutWalls.Clear();
            _wallHits.Clear();
            _openingHits.Clear();
            _openingVisuals.Clear();
            _itemHits.Clear();
            _itemVisuals.Clear();
            _daylight.Clear();

            var bounds = _plan.Bounds;
            _geometry.Box(_structure, World(bounds.center, -.255f), new Vector3(bounds.width + 28f, .18f, bounds.height + 28f), _edge);

            foreach (var room in _plan.Rooms)
            {
                var size = room.Bounds.size;
                var finish = _materials.Floors[room.Floor];
                var tiles = finish.TilesPerMeter;
                var textureTransform = new Vector4(size.x * tiles, size.y * tiles, room.Bounds.xMin * tiles, -room.Bounds.yMax * tiles);

                _geometry.Box(_structure, World(room.Bounds.center, -.085f), new Vector3(size.x, .17f, size.y), finish.Material, textureTransform);

                const float CeilingThickness = .1f;
                var ceilingCenter = World(room.Bounds.center, _plan.WallHeight + CeilingThickness / 2f);
                var ceiling = _geometry.Box(_ceilings, ceilingCenter, new Vector3(size.x, CeilingThickness, size.y), _ceiling);
                ceiling.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            foreach (var wall in _plan.Walls)
            {
                BuildWall(wall);
            }

            foreach (var item in _plan.Items)
            {
                AddItem(item, false);
            }

            UpdateCutaway();
            UpdateDaylight();
            UpdateCeilingVisibility();

            if (Walking)
            {
                ShowExterior(true);
            }
        }

        internal void Fit()
        {
            _cameraTween?.Kill();
            _zoomTween?.Kill();
            var bounds = _plan.Bounds;
            _target = World(bounds.center, _plan.Rooms.Count == 0 ? 0f : _plan.WallHeight / 2f);
            _orbitDistance = Mathf.Max(DefaultOrbitDistance, bounds.size.magnitude / 2f + _plan.WallHeight + 8f);
            _camera.farClipPlane = Mathf.Max(70f, _orbitDistance + bounds.size.magnitude / 2f + 15f);
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _fitSize = OrthographicFit(bounds, rotation, _camera.aspect, OrbitFramingHeight);
            _orbitAspect = _camera.aspect;
            _camera.orthographicSize = _fitSize;
            ApplyCamera();
        }

        internal void Orbit(Vector2 delta)
        {
            if (Walking)
            {
                LookAround(delta);
                return;
            }

            _cameraTween?.Kill();
            _yaw += delta.x * .32f;
            _pitch = Mathf.Clamp(_pitch - delta.y * .27f, 28f, 82f);
            ApplyCamera();
        }

        internal void Pan(Vector2 delta, float viewportHeight)
        {
            _cameraTween?.Kill();
            var units = _camera.orthographicSize * 2f / Mathf.Max(1f, viewportHeight);
            var right = _camera.transform.right;
            var forward = Vector3.Cross(right, Vector3.up);
            _target += (-right * delta.x + forward * delta.y / Mathf.Max(.45f, Mathf.Sin(_pitch * Mathf.Deg2Rad))) * units;
            ApplyCamera();
        }

        internal void Zoom(float delta)
        {
            _cameraTween?.Kill();
            _zoomTween?.Kill();
            var size = Mathf.Clamp(_camera.orthographicSize * Mathf.Exp(delta * .042f), 1f, Mathf.Max(46f, _fitSize * 3f));
            _zoomTween = _camera.DOOrthoSize(size, .2f).SetEase(Ease.OutCubic).OnUpdate(NotifyViewChanged);
        }

        internal void SetCutaway(bool value)
        {
            _cutaway = value;
            UpdateCutaway();
        }

        internal bool TryGetFloorPoint(Vector2 viewportPoint, out Vector2 point)
        {
            var ray = _camera.ViewportPointToRay(new Vector3(viewportPoint.x, 1f - viewportPoint.y));
            var floor = new Plane(Vector3.up, Vector3.zero);

            if (floor.Raycast(ray, out var distance))
            {
                var hit = ray.GetPoint(distance);
                point = new Vector2(hit.x, -hit.z);

                return true;
            }

            point = default;

            return false;
        }

        internal PlacedItem PickItem(Vector2 viewportPoint)
        {
            var ray = _camera.ViewportPointToRay(new Vector3(viewportPoint.x, 1f - viewportPoint.y));

            return Physics.Raycast(ray, out var hit, _camera.farClipPlane) && _itemHits.TryGetValue(hit.collider, out var item) ? item : null;
        }

        internal Opening PickOpening(Vector2 viewportPoint)
        {
            var ray = _camera.ViewportPointToRay(new Vector3(viewportPoint.x, 1f - viewportPoint.y));

            return Physics.Raycast(ray, out var hit, _camera.farClipPlane) && _openingHits.TryGetValue(hit.collider, out var opening) ? opening : null;
        }

        internal PlanWall? PickWall(Vector2 viewportPoint)
        {
            var ray = _camera.ViewportPointToRay(new Vector3(viewportPoint.x, 1f - viewportPoint.y));

            return Physics.Raycast(ray, out var hit, _camera.farClipPlane) && _wallHits.TryGetValue(hit.collider, out var wall) ? wall : null;
        }

        internal Vector2 ProjectFloor(Vector2 point)
        {
            return Project(World(point));
        }

        internal bool TryProjectItem(PlacedItem item, out Rect bounds)
        {
            bounds = default;

            if (!_itemVisuals.TryGetValue(item, out var holder))
            {
                return false;
            }

            bounds = ProjectBox(holder, Vector3.up * (item.Entry.Height / 2f), new Vector3(item.Entry.Width, item.Entry.Height, item.Entry.Depth));

            return true;
        }

        internal bool TryProjectOpening(Opening opening, out Rect bounds)
        {
            bounds = default;

            if (!_openingVisuals.TryGetValue(opening, out var colliders) || !colliders[0].enabled)
            {
                return false;
            }

            for (var index = 0; index < colliders.Length; index++)
            {
                var part = colliders[index];
                var projected = ProjectBox(part.transform, part.center, part.size);

                if (index == 0)
                {
                    bounds = projected;
                }
                else
                {
                    var min = Vector2.Min(bounds.min, projected.min);
                    bounds = new Rect(min, Vector2.Max(bounds.max, projected.max) - min);
                }
            }

            return true;
        }

        private Rect ProjectBox(Transform holder, Vector3 center, Vector3 size)
        {
            var half = size / 2f;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (var index = 0; index < 8; index++)
            {
                var corner = new Vector3((index & 1) == 0 ? -half.x : half.x, (index & 2) == 0 ? -half.y : half.y, (index & 4) == 0 ? -half.z : half.z);
                var point = Project(holder.TransformPoint(center + corner));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return new Rect(min, max - min);
        }

        internal void AddItem(PlacedItem item, bool animate)
        {
            var holder = Child(_items, item.Entry.Name);
            var model = _catalog.CreateModel(_geometry, item.Entry, holder);
            _itemVisuals.Add(item, holder);
            var collider = holder.gameObject.AddComponent<BoxCollider>();
            collider.center = Vector3.up * (item.Entry.Height / 2f);
            collider.size = new Vector3(item.Entry.Width, item.Entry.Height, item.Entry.Depth);
            _itemHits.Add(collider, item);
            MoveItem(item, false);

            if (animate)
            {
                var height = model.localPosition + Vector3.up * .32f;
                model.localPosition = height;
                model.DOLocalMoveY(0f, .40f).SetEase(Ease.OutBounce).SetLink(model.gameObject);
                var scale = model.localScale;
                model.localScale = scale * .89f;
                model.DOScale(scale, .34f).SetEase(Ease.OutBack).SetLink(model.gameObject);
            }
        }

        internal void MoveItem(PlacedItem item, bool animate)
        {
            var holder = _itemVisuals[item];
            holder.localPosition = World(item.Position, .012f);
            holder.DOKill();

            if (animate)
            {
                holder.DORotate(new Vector3(0f, item.Angle, 0f), .18f).SetEase(Ease.OutCubic).SetLink(holder.gameObject).OnUpdate(NotifyViewChanged);
            }
            else
            {
                holder.rotation = Quaternion.Euler(0f, item.Angle, 0f);
            }

            Physics.SyncTransforms();
        }

        internal void RemoveItem(PlacedItem item)
        {
            var holder = _itemVisuals[item];
            _itemHits.Remove(holder.GetComponent<Collider>());
            _itemVisuals.Remove(item);
            DestroyChild(holder);
        }

        internal void StartGhost(CatalogEntry entry)
        {
            EndGhost();
            _ghost = Child(_root, "Placement preview");
            _ghostModel = _catalog.CreateModel(_geometry, entry, _ghost, _invalid);
            _geometry.Box(_ghost, new Vector3(0f, .022f, 0f), new Vector3(entry.Width + .035f, .018f, entry.Depth + .035f), _invalid);
            _ghostRenderers = _ghost.GetComponentsInChildren<Renderer>();

            foreach (var renderer in _ghostRenderers)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            _ghostValid = false;
            _ghost.gameObject.SetActive(false);
        }

        internal void UpdateGhost(bool visible, Vector2 point, float angle, bool valid)
        {
            if (_ghost == null)
            {
                return;
            }

            _ghost.gameObject.SetActive(visible);

            if (!visible)
            {
                return;
            }

            _ghost.SetPositionAndRotation(World(point, .014f), Quaternion.Euler(0f, angle, 0f));

            if (_ghostValid != valid)
            {
                _ghostValid = valid;

                foreach (var renderer in _ghostRenderers)
                {
                    renderer.sharedMaterial = valid ? _valid : _invalid;
                }

                _ghostModel.DOKill();
                _ghostModel.localPosition = Vector3.up * .045f;
                _ghostModel.DOLocalMoveY(0f, .15f).SetLink(_ghostModel.gameObject);
            }
        }

        internal void EndGhost()
        {
            DestroyChild(_ghost);
            _ghost = null;
        }

        internal void SetLighting(float amount, float warmth, float angle)
        {
            _lightTween?.Kill();
            var initial = new Vector3(_lightAmount, _lightWarmth, _lightAngle);
            var target = new Vector3(amount, warmth, angle);
            _lightTween = DOTween.To(() => initial, updateLighting, target, .32f).SetEase(Ease.OutCubic);

            void updateLighting(Vector3 value)
            {
                initial = value;
                ApplyLighting(value.x, value.y, value.z);
            }
        }

        internal RenderTexture Thumbnail(CatalogEntry entry)
        {
            if (_thumbnails.TryGetValue(entry, out var cached))
            {
                return cached;
            }

            var preview = Child(_root.parent, "Catalog thumbnail");
            preview.gameObject.layer = PreviewLayer;
            preview.position = new Vector3(100f, 0f, 100f);
            _catalog.CreateModel(_geometry, entry, preview);
            var target = preview.position + Vector3.up * entry.Height * .45f;
            var camera = Child(preview, "Preview camera").gameObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = _materials.Hex("#DDD2C2");
            camera.cullingMask = 1 << PreviewLayer;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(entry.Height, new Vector2(entry.Width, entry.Depth).magnitude) * .63f;
            camera.transform.position = target + new Vector3(3f, 2.5f, -4f);
            camera.transform.LookAt(target);
            camera.allowHDR = false;
            var texture = new RenderTexture(192, 128, 16, RenderTextureFormat.ARGB32)
            {
                name = entry.Name,
                antiAliasing = 1
            };

            var request = new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = texture
            };
            RenderPipeline.SubmitRenderRequest(camera, request);
            _thumbnails.Add(entry, texture);
            DestroyChild(preview);

            return texture;
        }

        internal void Dispose()
        {
            StopModeTweens();
            _walkFlight?.Kill();
            _lightTween?.Kill();

            foreach (var texture in _thumbnails.Values)
            {
                texture.Release();
                Object.Destroy(texture);
            }
        }

        private void BuildWall(PlanWall wall)
        {
            var group = Child(_structure, "Wall");
            var openings = new List<Opening>();

            foreach (var opening in _plan.Openings)
            {
                if (_plan.BelongsTo(opening, wall))
                {
                    openings.Add(opening);
                }
            }

            openings.Sort((first, second) => first.Center(wall).CompareTo(second.Center(wall)));
            var cursor = wall.Start;

            foreach (var opening in openings)
            {
                var center = opening.Center(wall);
                var start = center - opening.Width / 2f;
                var end = center + opening.Width / 2f;
                WallPiece(group, wall, cursor, start, 0f, _plan.WallHeight);
                WallPiece(group, wall, start, end, 0f, opening.Sill);
                WallPiece(group, wall, start, end, opening.Sill + opening.Height, _plan.WallHeight);
                OpeningFrame(group, wall, opening, start, end);
                cursor = end;
            }

            WallPiece(group, wall, cursor, wall.End, 0f, _plan.WallHeight);

            if (wall.Neighbor == null)
            {
                var low = Child(_structure, "Cut wall");
                WallPiece(low, wall, wall.Start, wall.End, 0f, .13f);
                low.gameObject.SetActive(false);
                _cutWalls.Add((group.GetComponentsInChildren<Renderer>(), group.GetComponentsInChildren<Collider>(), low, wall.Inward));
            }
        }

        private void WallPiece(Transform parent, PlanWall wall, float start, float end, float bottom, float top)
        {
            if (end - start < .003f || top - bottom < .003f)
            {
                return;
            }

            var half = WallThickness / 2f;
            var center = wall.Point((start + end) / 2f);
            var position = World(center, (bottom + top) / 2f);
            var inward = World(wall.Inward);
            var length = end - start;
            var height = top - bottom;
            var facing = Quaternion.Euler(0f, wall.Horizontal ? 0f : 90f, 0f);
            var size = new Vector3(length + .008f, height, half);
            var inside = _materials.Walls[wall.Room.WallFinish(wall.Side)];
            var outside = wall.Neighbor == null ? inside : _materials.Walls[wall.Neighbor.WallFinish(wall.OppositeSide)];
            TexturedWallHalf(parent, position + inward * (half / 2f), facing, size, start, bottom, inside);
            TexturedWallHalf(parent, position - inward * (half / 2f), facing, size, start, bottom, outside);

            if (bottom < .005f && top > .24f)
            {
                var skirting = _geometry.Box(parent, World(center, .038f) + inward * (half + .009f), new Vector3(length, .073f, .022f), _edge);
                skirting.rotation = facing;

                if (wall.Neighbor != null)
                {
                    var reverse = _geometry.Box(parent, World(center, .038f) - inward * (half + .009f), new Vector3(length, .073f, .022f), _edge);
                    reverse.rotation = facing;
                }
            }

            AddWallCollider(parent, wall, position, facing, new Vector3(length, height, WallThickness));
        }

        private void AddWallCollider(Transform parent, PlanWall wall, Vector3 position, Quaternion facing, Vector3 size)
        {
            _wallHits.Add(AddCollider(parent, position, facing, size), wall);
        }

        private BoxCollider AddCollider(Transform parent, Vector3 position, Quaternion facing, Vector3 size)
        {
            var colliderHost = Child(parent, "Picking collider");
            colliderHost.SetPositionAndRotation(position, facing);
            var collider = colliderHost.gameObject.AddComponent<BoxCollider>();
            collider.size = size;

            return collider;
        }

        private void TexturedWallHalf(Transform parent, Vector3 position, Quaternion rotation, Vector3 size, float offset, float height, Finish finish)
        {
            var tiles = finish.TilesPerMeter;
            var part = _geometry.Box(parent, position, size, finish.Material, new Vector4(size.x * tiles, size.y * tiles, offset * tiles, height * tiles));
            part.rotation = rotation;
        }

        private void OpeningFrame(Transform parent, PlanWall wall, Opening opening, float start, float end)
        {
            var top = opening.Sill + opening.Height;
            var rotation = Quaternion.Euler(0f, wall.Horizontal ? 0f : 90f, 0f);
            var center = (start + end) / 2f;
            var midY = (opening.Sill + top) / 2f;
            var jambSize = new Vector3(.042f, opening.Height, .15f);
            var headerSize = new Vector3(opening.Width, .046f, .15f);
            FramePart(parent, wall, start + .021f, midY, jambSize, rotation, _frame);
            FramePart(parent, wall, end - .021f, midY, jambSize, rotation, _frame);
            FramePart(parent, wall, center, top - .023f, headerSize, rotation, _frame);

            if (opening.IsDoor)
            {
                var hinge = wall.Point(opening.HingePosition(wall, .042f));
                var swing = opening.SwingDirection(wall);
                var depth = opening.Width - .085f;
                var leafAngle = Quaternion.Euler(0f, wall.Horizontal ? 90f : 0f, 0f);
                var leafCenter = hinge + swing * (depth / 2f - .02f);
                var leafPosition = World(leafCenter, opening.Height / 2f);
                var leafSize = new Vector3(depth, opening.Height - .095f, .046f);
                var door = _geometry.RoundedBox(parent, leafPosition, leafSize, _door);
                door.rotation = leafAngle;
                var leaf = openingCollider(leafPosition, leafAngle, leafSize);
                leaf.gameObject.layer = DoorLeafLayer;
                var doorParts = new[]
                {
                    openingCollider(World(wall.Point(start + .021f), midY), rotation, jambSize),
                    openingCollider(World(wall.Point(end - .021f), midY), rotation, jambSize),
                    openingCollider(World(wall.Point(center), top - .023f), rotation, headerSize),
                    leaf
                };
                _openingVisuals.Add(opening, doorParts);
                var handle = leafCenter + swing * (depth / 2f - .095f);
                var grip = _geometry.RoundedBox(parent, World(handle, Mathf.Min(1.03f, opening.Height / 2f)), new Vector3(.105f, .034f, .112f), _frame);
                grip.rotation = leafAngle;

                return;
            }

            FramePart(parent, wall, center, opening.Sill + .025f, new Vector3(opening.Width + .08f, .05f, .19f), rotation, _frame);
            var paneSize = new Vector3(opening.Width - .06f, opening.Height - .07f, .022f);
            var pane = _geometry.Box(parent, World(wall.Point(center), midY), paneSize, _glass);
            pane.rotation = rotation;
            pane.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            FramePart(parent, wall, center, midY, new Vector3(.036f, opening.Height, .067f), rotation, _frame);
            var windowParts = new[]
            {
                openingCollider(World(wall.Point(center), midY), rotation, new Vector3(opening.Width, opening.Height, .15f))
            };
            _openingVisuals.Add(opening, windowParts);

            var inward = World(wall.Inward);
            var light = Object.Instantiate(_windowDaylight, parent).GetComponent<Light>();
            light.transform.position = World(wall.Point(center), midY) + inward * .13f;
            var roomDepth = wall.Horizontal ? wall.Room.Bounds.height : wall.Room.Bounds.width;
            light.range = Mathf.Clamp(roomDepth + 1f, 3f, 7f);
            _daylight.Add((light, inward, paneSize.x * paneSize.y));

            BoxCollider openingCollider(Vector3 position, Quaternion facing, Vector3 size)
            {
                var hit = AddCollider(parent, position, facing, size);
                _openingHits.Add(hit, opening);

                return hit;
            }
        }

        private void FramePart(Transform parent, PlanWall wall, float x, float y, Vector3 size, Quaternion rotation, Material material)
        {
            var part = _geometry.Box(parent, World(wall.Point(x), y), size, material);
            part.rotation = rotation;
        }

        private Vector2 Project(Vector3 point)
        {
            var screen = _camera.WorldToViewportPoint(point);

            return new Vector2(screen.x, 1f - screen.y);
        }

        private void ApplyCamera()
        {
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _camera.transform.SetPositionAndRotation(_target - rotation * Vector3.forward * _orbitDistance, rotation);
            UpdateCutaway();
            NotifyViewChanged();
        }

        private void NotifyViewChanged()
        {
            ViewChanged?.Invoke();
        }

        private float OrthographicFit(Rect bounds, Quaternion rotation, float aspect, float framingHeight = 1f)
        {
            var half = new Vector3(bounds.width + WallThickness, _plan.WallHeight, bounds.height + WallThickness) / 2f;
            var width = extentAlong(rotation * Vector3.right);
            var height = extentAlong(rotation * Vector3.up);

            return Mathf.Max(2.1f, Mathf.Max(width / Mathf.Max(.05f, aspect), height / framingHeight) * 1.15f);

            float extentAlong(Vector3 axis)
            {
                return Mathf.Abs(axis.x) * half.x + Mathf.Abs(axis.y) * half.y + Mathf.Abs(axis.z) * half.z;
            }
        }

        private void UpdateOrbitAspect(float aspect)
        {
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var bounds = _plan.Bounds;
            var previousFit = OrthographicFit(bounds, rotation, _orbitAspect, OrbitFramingHeight);
            _fitSize = OrthographicFit(bounds, rotation, aspect, OrbitFramingHeight);
            _zoomTween?.Kill();
            _camera.orthographicSize *= _fitSize / previousFit;
            _orbitAspect = aspect;
        }

        private void UpdateCutaway()
        {
            ApplyCutaway(_camera.transform.forward, _cutaway && !Walking && _pitch < 82f);
        }

        private void ApplyCutaway(Vector3 fromCamera, bool enabled)
        {
            var planDirection = new Vector2(fromCamera.x, -fromCamera.z).normalized;

            foreach (var (walls, hits, low, inward) in _cutWalls)
            {
                var cut = enabled && Vector2.Dot(planDirection, inward) > .24f;

                if (cut == low.gameObject.activeSelf)
                {
                    continue;
                }

                foreach (var wall in walls)
                {
                    if (wall.sharedMaterial == _glass)
                    {
                        wall.enabled = !cut;
                    }
                    else
                    {
                        wall.shadowCastingMode = cut ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                    }
                }

                foreach (var hit in hits)
                {
                    hit.enabled = !cut;
                }

                low.gameObject.SetActive(cut);
            }
        }

        private Light CreateLight(Transform parent, string name, LightShadows shadows)
        {
            var light = Child(parent, name).gameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = shadows;

            return light;
        }

        private void ApplyLighting(float amount, float warmth, float angle)
        {
            _lightAmount = amount;
            _lightWarmth = warmth;
            _lightAngle = angle;
            _sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(56f, 30f, warmth), angle, 0f);
            _sun.color = Color.Lerp(_materials.Hex("#DAE8FF"), _materials.Hex("#FFC17F"), warmth);
            _sun.intensity = amount;
            _fill.color = Color.Lerp(_materials.Hex("#E7E3DC"), _materials.Hex("#BBADB8"), warmth);
            _fill.intensity = .25f + amount * .16f;
            RenderSettings.ambientLight = Color.Lerp(_materials.Hex("#777A81"), _materials.Hex("#675A60"), warmth);
            UpdateDaylight();
        }

        private void UpdateDaylight()
        {
            var sun = _sun.transform.forward;
            var horizontal = new Vector3(sun.x, 0f, sun.z).normalized;

            foreach (var (light, inward, area) in _daylight)
            {
                var incidence = Mathf.Max(0f, Vector3.Dot(horizontal, inward));
                var direction = inward + horizontal * (incidence * .45f) + Vector3.down * Mathf.Lerp(.95f, .48f, _lightWarmth);
                light.transform.rotation = Quaternion.LookRotation(direction);
                light.color = _sun.color;
                light.intensity = _lightAmount * Mathf.Min(area, 5f) * (.35f + incidence * 1.25f);
            }
        }

        private void StopModeTweens()
        {
            _riseTween?.Kill(true);
            _cameraTween?.Kill();
            _zoomTween?.Kill();
        }

        private Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);

            return child;
        }

        private void DestroyChild(Transform child)
        {
            if (child != null)
            {
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
        }

        private Vector3 World(Vector2 point, float elevation = 0f)
        {
            return new Vector3(point.x, elevation, -point.y);
        }
    }
}
