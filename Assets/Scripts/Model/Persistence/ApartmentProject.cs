using System;
using System.Collections.Generic;
using UnityEngine;

namespace HouseDesign
{
    internal sealed partial class ApartmentProject
    {
        private const int FormatVersion = 2;
        internal const int MaximumFileBytes = 8 * 1024 * 1024;

        internal static string Write(ApartmentPlan plan, Lighting lighting)
        {
            var project = new ApartmentProject
            {
                version = FormatVersion,
                wallHeight = plan.WallHeight,
                lighting = lighting,
                rooms = new List<RoomData>(plan.Rooms.Count),
                openings = new List<OpeningData>(plan.Openings.Count),
                items = new List<ItemData>(plan.Items.Count)
            };

            foreach (var room in plan.Rooms)
            {
                var roomData = new RoomData
                {
                    id = room.Id,
                    name = room.Name,
                    bounds = room.Bounds,
                    floor = room.Floor,
                    walls = room.Walls,
                    wallFinishes = room.CopyWallFinishes()
                };
                project.rooms.Add(roomData);
            }

            foreach (var opening in plan.Openings)
            {
                var openingData = new OpeningData
                {
                    room = opening.RoomId,
                    neighbor = opening.NeighborId,
                    side = opening.Side,
                    offset = opening.Offset,
                    width = opening.Width,
                    height = opening.Height,
                    sill = opening.Sill,
                    opensIntoNeighbor = opening.OpensIntoNeighbor,
                    hingeAtEnd = opening.HingeAtEnd
                };
                project.openings.Add(openingData);
            }

            foreach (var item in plan.Items)
            {
                var itemData = new ItemData
                {
                    kind = item.Entry.Kind.ToString(),
                    room = item.Room.Id,
                    position = item.Position,
                    angle = Mathf.Repeat(item.Angle, 360f)
                };
                project.items.Add(itemData);
            }

            return JsonUtility.ToJson(project, true);
        }

        internal static bool TryRead(string json, IReadOnlyList<CatalogEntry> entries, int floorFinishCount, int wallFinishCount, out ApartmentPlan plan, out Lighting lighting, out string error)
        {
            plan = null;
            lighting = null;
            error = "Не удалось прочитать файл проекта Atelier.";

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            ApartmentProject project;
            try
            {
                project = JsonUtility.FromJson<ApartmentProject>(json.TrimStart('\uFEFF'));
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (project == null || project.version == 0)
            {
                return false;
            }

            if (project.version is not (1 or FormatVersion))
            {
                error = "Эта версия файла проекта пока не поддерживается.";
                return false;
            }

            if (project.rooms == null || project.openings == null || project.items == null || project.lighting == null)
            {
                return false;
            }

            var lightingInRange = project.lighting.Intensity is >= .10f and <= 2.5f && project.lighting.Warmth is >= 0f and <= 1f && project.lighting.Angle is >= -180f and <= 180f;
            if (project.wallHeight is not (>= 2.1f and <= 5f) || !lightingInRange)
            {
                return false;
            }

            var draft = new ApartmentPlan();
            draft.SetWallHeight(project.wallHeight);
            var roomIds = new HashSet<int>();

            foreach (var data in project.rooms)
            {
                error = "В файле есть комната с неверными параметрами или пересекающимися границами.";

                if (data == null || data.id <= 0 || data.id >= int.MaxValue - 1 || !roomIds.Add(data.id) || string.IsNullOrWhiteSpace(data.name))
                {
                    return false;
                }

                var finishesInRange = data.floor >= 0 && data.floor < floorFinishCount && data.walls >= 0 && data.walls < wallFinishCount;
                var bounds = data.bounds;
                var finiteBounds = Finite(bounds.position) && Finite(bounds.size) && Finite(bounds.max);
                if (!finishesInRange || !finiteBounds || bounds.xMax <= bounds.xMin || bounds.yMax <= bounds.yMin)
                {
                    return false;
                }

                var room = draft.AddRoom(data.name, bounds, data.id);
                if (room == null)
                {
                    return false;
                }

                room.Floor = data.floor;
                room.Walls = data.walls;

                if (data.wallFinishes != null && data.wallFinishes.Length != 0)
                {
                    if (data.wallFinishes.Length != 4)
                    {
                        return false;
                    }

                    for (var index = 0; index < data.wallFinishes.Length; index++)
                    {
                        var finish = data.wallFinishes[index];

                        if (finish < -1 || finish >= wallFinishCount)
                        {
                            return false;
                        }

                        if (finish >= 0)
                        {
                            room.PaintWall((WallSide)index, finish);
                        }
                    }
                }
            }

            foreach (var data in project.openings)
            {
                error = "В файле есть проём, который не помещается на указанной стене.";

                if (data == null || !RestoreOpening(draft, data))
                {
                    return false;
                }
            }

            foreach (var data in project.items)
            {
                error = "В файле есть неизвестный предмет или предмет, который не помещается в указанной комнате.";

                if (data == null || !Finite(data.position) || !float.IsFinite(data.angle))
                {
                    return false;
                }

                var entry = FindEntry(entries, data.kind);
                var item = entry != null ? draft.AddItem(entry, data.position, Mathf.Repeat(data.angle, 360f)) : null;

                if (item == null || item.Room.Id != data.room)
                {
                    return false;
                }

            }

            plan = draft;
            lighting = project.lighting;
            error = null;

            return true;
        }

        private static bool RestoreOpening(ApartmentPlan plan, OpeningData data)
        {
            foreach (var wall in plan.Walls)
            {
                if (wall.Room.Id != data.room || (wall.Neighbor?.Id ?? 0) != data.neighbor || wall.Side != data.side)
                {
                    continue;
                }

                var opening = new Opening(wall, wall.Start, data.neighbor != 0)
                {
                    Offset = data.offset,
                    Width = data.width,
                    Height = data.height,
                    Sill = data.sill,
                    OpensIntoNeighbor = data.opensIntoNeighbor,
                    HingeAtEnd = data.hingeAtEnd
                };

                if (plan.BelongsTo(opening, wall))
                {
                    return plan.AddOpening(wall, opening);
                }
            }

            return false;
        }

        private static CatalogEntry FindEntry(IReadOnlyList<CatalogEntry> entries, string kind)
        {
            foreach (var entry in entries)
            {
                if (entry.Kind.ToString() == kind)
                {
                    return entry;
                }
            }

            return null;
        }

        private static bool Finite(Vector2 point)
        {
            return float.IsFinite(point.x) && float.IsFinite(point.y);
        }
    }
}
