using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HouseDesign
{
    internal sealed partial class ApartmentStage
    {
        private const float EyeHeight = 1.62f;
        private const float BodyRadius = .17f;
        private const float WalkFov = 64f;
        private const float WalkSpeed = 2.1f;
        private const int DoorLeafLayer = 29;
        private const int WalkMask = Physics.DefaultRaycastLayers & ~(1 << DoorLeafLayer);
        private float _walkYaw;
        private float _walkPitch;
        private Vector3 _walkDestination;
        private bool _hasWalkPosition;
        private float _walkProjection;
        private Tween _walkFlight;
        private Transform _exterior;
        private Material _gardenGround;
        private Material _gardenPaving;
        private Material _treeTrunk;
        private Material _treeLight;
        private Material _treeShade;
        private Material _neighborWall;
        private Material _neighborTrim;

        internal bool Walking
        {
            get;
            private set;
        }
        internal bool ChangingWalkView => _walkFlight != null && _walkFlight.IsActive();

        internal bool BeginWalk(Room preferred)
        {
            StopModeTweens();
            Walking = true;
            UpdateCutaway();
            Physics.SyncTransforms();

            if (!TryWalkSpawn(preferred, out var spawn))
            {
                Walking = false;
                UpdateCutaway();

                return false;
            }

            if (!_hasWalkPosition)
            {
                _walkYaw = _yaw;
                _walkPitch = 4f;
            }

            _walkDestination = World(spawn, EyeHeight);
            _hasWalkPosition = true;
            FlyWalk(_walkDestination, Quaternion.Euler(_walkPitch, _walkYaw, 0f), true);

            return true;
        }

        internal void ForgetWalkPosition()
        {
            _hasWalkPosition = false;
        }

        internal void EndWalk(bool animate = false)
        {
            if (!Walking && !ChangingWalkView)
            {
                return;
            }

            Walking = false;
            UpdateOrbitAspect(_camera.aspect);

            if (animate)
            {
                var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                FlyWalk(_target - rotation * Vector3.forward * _orbitDistance, rotation, false);
            }
            else
            {
                _walkFlight?.Kill();
                _walkFlight = null;
                FinishWalkFlight(false);
                ApplyCamera();
            }
        }

        internal void Walk(Vector2 direction, float deltaTime)
        {
            if (!Walking || deltaTime <= 0f)
            {
                return;
            }

            var input = Vector2.ClampMagnitude(direction, 1f);
            var look = Quaternion.Euler(_walkPitch, _walkYaw, 0f);
            var flying = ChangingWalkView;

            if (!flying)
            {
                _camera.transform.rotation = Quaternion.Slerp(_camera.transform.rotation, look, 1f - Mathf.Exp(-18f * deltaTime));
            }

            if (input != Vector2.zero)
            {
                var heading = Quaternion.Euler(0f, flying ? _walkYaw : _camera.transform.eulerAngles.y, 0f);
                var step = heading * new Vector3(input.x, 0f, input.y) * (WalkSpeed * deltaTime);
                _walkDestination = SlideWalk(_walkDestination, step);
            }

            if (!flying)
            {
                _camera.transform.position = _walkDestination;
            }
        }

        private Vector3 SlideWalk(Vector3 position, Vector3 step)
        {
            for (var iteration = 0; iteration < 2 && step.sqrMagnitude > .00000001f; iteration++)
            {
                var distance = step.magnitude;
                var direction = step / distance;
                var bottom = new Vector3(position.x, .23f, position.z);
                var top = new Vector3(position.x, 1.44f, position.z);
                var blocked = Physics.CapsuleCast(bottom, top, BodyRadius, direction, out var hit, distance, WalkMask, QueryTriggerInteraction.Ignore);
                var travel = blocked ? Mathf.Max(0f, hit.distance - .006f) : distance;
                var destination = position + direction * travel;

                if (_plan.FindRoom(new Vector2(destination.x, -destination.z)) == null)
                {
                    break;
                }

                position = destination;

                if (!blocked)
                {
                    break;
                }

                var normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                step = Vector3.ProjectOnPlane(direction * (distance - travel), normal);
            }

            return position;
        }

        private bool TryWalkSpawn(Room preferred, out Vector2 spawn)
        {
            if (_hasWalkPosition)
            {
                var saved = new Vector2(_walkDestination.x, -_walkDestination.z);

                if (_plan.FindRoom(saved) is Room room)
                {
                    if (CanStandAt(saved))
                    {
                        spawn = saved;
                        return true;
                    }

                    if (TryRoomSpawn(room.Bounds, saved, out spawn))
                    {
                        return true;
                    }
                }
            }

            if (preferred != null && TryRoomSpawn(preferred.Bounds, preferred.Bounds.center, out spawn))
            {
                return true;
            }

            foreach (var room in _plan.Rooms)
            {
                if (room != preferred && TryRoomSpawn(room.Bounds, room.Bounds.center, out spawn))
                {
                    return true;
                }
            }

            spawn = default;

            return false;
        }

        private bool TryRoomSpawn(Rect bounds, Vector2 near, out Vector2 spawn)
        {
            var nearest = float.PositiveInfinity;
            spawn = default;

            for (var y = bounds.yMin + .24f; y <= bounds.yMax - .24f; y += .25f)
            {
                for (var x = bounds.xMin + .24f; x <= bounds.xMax - .24f; x += .25f)
                {
                    var point = new Vector2(x, y);
                    var distance = (point - near).sqrMagnitude;

                    if (distance < nearest && CanStandAt(point))
                    {
                        nearest = distance;
                        spawn = point;
                    }
                }
            }

            return float.IsFinite(nearest);
        }

        private bool CanStandAt(Vector2 point)
        {
            return !Physics.CheckCapsule(World(point, .23f), World(point, 1.44f), BodyRadius, WalkMask, QueryTriggerInteraction.Ignore);
        }

        private void LookAround(Vector2 delta)
        {
            _walkYaw += delta.x * .23f;
            _walkPitch = Mathf.Clamp(_walkPitch + delta.y * .23f, -78f, 78f);
        }

        private void FlyWalk(Vector3 destination, Quaternion rotation, bool entering)
        {
            _walkFlight?.Kill();
            var start = _camera.transform.position;
            var facing = _camera.transform.rotation;
            var firstProjection = _walkProjection;
            var lastProjection = entering ? 1f : 0f;
            var progress = 0f;
            _camera.orthographic = false;
            _camera.nearClipPlane = .05f;
            _walkFlight = DOTween.To(() => progress, fly, 1f, 1.1f).SetEase(Ease.InOutSine).SetUpdate(UpdateType.Late, true).OnComplete(() => FinishWalkFlight(entering));
            fly(0f);

            void fly(float value)
            {
                progress = value;
                var lift = Mathf.Sin(value * Mathf.PI) * 1.5f;
                var target = entering ? _walkDestination : destination;
                _camera.transform.SetPositionAndRotation(Vector3.Lerp(start, target, value) + Vector3.up * lift, Quaternion.Slerp(facing, rotation, value));
                _walkProjection = Mathf.Lerp(firstProjection, lastProjection, value);
                UpdateCeilingVisibility();
                ShowExterior(_walkProjection > .8f);
                BlendView(_walkProjection);
                NotifyViewChanged();
            }
        }

        private void BlendView(float perspectiveWeight)
        {
            var size = _camera.orthographicSize;
            var aspect = _camera.aspect;
            var near = _camera.nearClipPlane;
            var far = _camera.farClipPlane;
            var ortho = Matrix4x4.Ortho(-size * aspect, size * aspect, -size, size, near, far);
            var matchingFov = Mathf.Atan(size / _orbitDistance) * 2f * Mathf.Rad2Deg;
            var perspective = Matrix4x4.Perspective(Mathf.Lerp(matchingFov, WalkFov, perspectiveWeight), aspect, near, far);
            var referenceDepth = Mathf.Lerp(_orbitDistance, 2f, perspectiveWeight);

            for (var index = 0; index < 16; index++)
            {
                ortho[index] = Mathf.Lerp(ortho[index], perspective[index] / referenceDepth, perspectiveWeight);
            }

            _camera.projectionMatrix = ortho;
        }

        private void FinishWalkFlight(bool firstPerson)
        {
            _walkFlight = null;
            _walkProjection = firstPerson ? 1f : 0f;
            _camera.orthographic = !firstPerson;
            _camera.fieldOfView = WalkFov;
            _camera.nearClipPlane = firstPerson ? .05f : .1f;
            _camera.ResetProjectionMatrix();
            UpdateCeilingVisibility();
            ShowExterior(firstPerson);
            UpdateCutaway();
            NotifyViewChanged();
        }

        private void UpdateCeilingVisibility()
        {
            if (_ceilings != null)
            {
                _ceilings.gameObject.SetActive(Walking && _camera.transform.position.y < _plan.WallHeight - _camera.nearClipPlane);
            }
        }

        private void ShowExterior(bool visible)
        {
            var mask = 1 << ExteriorLayer;

            if (visible == ((_camera.cullingMask & mask) != 0) && (!visible || _exterior != null))
            {
                return;
            }

            if (visible && _exterior == null)
            {
                BuildExterior();
            }

            _exterior?.gameObject.SetActive(visible);
            _camera.cullingMask = visible ? _camera.cullingMask | mask : _camera.cullingMask & ~mask;
            _camera.backgroundColor = visible ? _materials.Hex("#BBD0D7") : _overviewBackground;
        }

        private void BuildExterior()
        {
            _gardenGround ??= _materials.ColorMaterial("#858B70", .12f);
            _gardenPaving ??= _materials.ColorMaterial("#C8BEAD", .15f);
            _treeTrunk ??= _materials.ColorMaterial("#776050", .12f);
            _treeLight ??= _materials.ColorMaterial("#819578", .13f);
            _treeShade ??= _materials.ColorMaterial("#626F59", .13f);
            _neighborWall ??= _materials.ColorMaterial("#BCABA0", .2f);
            _neighborTrim ??= _materials.ColorMaterial("#8C8580", .22f);
            _exterior = Child(_root, "Window surroundings");
            var bounds = _plan.Bounds;
            const float Ground = -.155f;
            addPart(_geometry.Box(_exterior, World(bounds.center, Ground - .05f), new Vector3(bounds.width + 151f, .1f, bounds.height + 151f), _gardenGround));

            for (var side = 0; side < 4; side++)
            {
                var horizontal = (side & 1) == 0;
                var tangent = horizontal ? Vector3.right : Vector3.forward;
                var outward = Vector3.Cross(tangent, Vector3.up) * (side < 2 ? 1f : -1f);
                var span = horizontal ? bounds.width : bounds.height;
                var depth = horizontal ? bounds.height : bounds.width;
                var edge = World(bounds.center, Ground) + outward * (depth / 2f);
                addPart(_geometry.Box(_exterior, edge + outward * 4.3f, size(span + 12f, .07f, 1.8f), _gardenPaving));

                for (var index = 0; index < 4; index++)
                {
                    var offset = ((index + .4f) / 4f - .5f) * (span + 16f);
                    var tree = edge + tangent * offset + outward * (5.8f + (index % 3) * 1.7f);
                    var height = 2.7f + ((index + side) % 3) * .55f;
                    addPart(_geometry.Cylinder(_exterior, tree + Vector3.up * (height * .31f), new Vector3(.22f, height * .62f, .22f), _treeTrunk));
                    addPart(_geometry.Sphere(_exterior, tree + Vector3.up * (height * .72f), new Vector3(2.3f, height * .74f, 2.15f), index % 2 == 0 ? _treeLight : _treeShade));
                    var shrub = tree - outward * 2.8f + tangent * 1.4f + Vector3.up * .25f;
                    addPart(_geometry.Sphere(_exterior, shrub, new Vector3(1.7f, .8f, .95f), _treeLight));
                }

                for (var index = 0; index < 3; index++)
                {
                    var offset = (index - 1f) * Mathf.Max(8.5f, span * .36f);
                    var building = edge + tangent * offset + outward * (19f + (index % 2) * 6f);
                    var height = 4.1f + ((side + index) % 3) * 1.3f;
                    const float BuildingWidth = 6.6f;
                    addPart(_geometry.Box(_exterior, building + Vector3.up * (height / 2f), size(BuildingWidth, height, 5.3f), _neighborWall));
                    addPart(_geometry.Box(_exterior, building + Vector3.up * height, size(BuildingWidth + .28f, .28f, 5.6f), _neighborTrim));

                    for (var row = 0; row < 2; row++)
                    {
                        var facade = building - outward * 2.655f + Vector3.up * (height * (.37f + row * .36f));
                        addPart(_geometry.Box(_exterior, facade, size(BuildingWidth * .73f, .55f, .04f), _neighborTrim));
                    }
                }

                Vector3 size(float width, float height, float distance)
                {
                    return horizontal ? new Vector3(width, height, distance) : new Vector3(distance, height, width);
                }
            }

            void addPart(Transform part)
            {
                part.gameObject.layer = ExteriorLayer;
                part.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        internal bool TryPickWallSurface(Vector2 viewportPoint, out Room room, out WallSide side)
        {
            var ray = _camera.ViewportPointToRay(new Vector3(viewportPoint.x, 1f - viewportPoint.y));
            room = null;
            side = default;

            if (!Physics.Raycast(ray, out var hit, _camera.farClipPlane) || !_wallHits.TryGetValue(hit.collider, out var wall))
            {
                return false;
            }

            var facing = Vector3.Dot(hit.normal, World(wall.Inward));

            if (Mathf.Abs(facing) < .1f)
            {
                facing = Vector3.Dot(-ray.direction, World(wall.Inward));
            }

            var reverse = wall.Neighbor != null && facing < 0f;
            room = reverse ? wall.Neighbor : wall.Room;
            side = reverse ? wall.OppositeSide : wall.Side;

            return true;
        }

        internal void RenderOverview(RenderTexture destination, bool rebuild)
        {
            var previous = _root.gameObject.activeSelf;
            var ghostVisible = _ghost != null && _ghost.gameObject.activeSelf;
            var overview = Child(_root.parent, "Poster camera");

            try
            {
                _root.gameObject.SetActive(true);

                if (ghostVisible)
                {
                    _ghost.gameObject.SetActive(false);
                }

                if (rebuild)
                {
                    Rebuild();
                }

                _riseTween?.Kill(true);
                var camera = overview.gameObject.AddComponent<Camera>();
                camera.CopyFrom(_camera);
                camera.cullingMask &= ~(1 << ExteriorLayer);
                camera.backgroundColor = _overviewBackground;
                camera.GetUniversalAdditionalCameraData().requiresColorOption = CameraOverrideOption.Off;
                camera.enabled = false;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.orthographic = true;
                camera.aspect = (float)destination.width / destination.height;
                var bounds = _plan.Bounds;
                var rotation = Quaternion.Euler(56f, -30f, 0f);
                var center = World(bounds.center, _plan.WallHeight / 2f);
                var distance = Mathf.Max(DefaultOrbitDistance, bounds.size.magnitude / 2f + _plan.WallHeight + 8f);
                camera.farClipPlane = Mathf.Max(70f, distance + bounds.size.magnitude / 2f + 15f);
                camera.orthographicSize = OrthographicFit(bounds, rotation, camera.aspect);
                camera.ResetProjectionMatrix();
                camera.transform.SetPositionAndRotation(center - rotation * Vector3.forward * distance, rotation);
                _ceilings.gameObject.SetActive(false);
                ApplyCutaway(camera.transform.forward, true);
                var request = new UniversalRenderPipeline.SingleCameraRequest
                {
                    destination = destination
                };
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            finally
            {
                UpdateCeilingVisibility();
                UpdateCutaway();
                _root.gameObject.SetActive(previous);

                if (ghostVisible)
                {
                    _ghost.gameObject.SetActive(true);
                }

                DestroyChild(overview);
            }
        }
    }
}
