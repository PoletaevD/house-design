using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace HouseDesign
{
    public sealed partial class ApartmentStudio
    {
        private const string ProjectFileName = "Atelier.atelier.json";

#if UNITY_WEBGL && !UNITY_EDITOR
        private delegate void ImportCallback(int result, IntPtr buffer, int length);

        private static ApartmentStudio s_ImportReceiver;

        [DllImport("__Internal")]
        private static extern int AtelierDownloadProject(string fileName, string json);

        [DllImport("__Internal")]
        private static extern int AtelierPickProject(int maxBytes, ImportCallback callback);

        [AOT.MonoPInvokeCallback(typeof(ImportCallback))]
        private static void ReceiveProject(int result, IntPtr buffer, int length)
        {
            var studio = s_ImportReceiver;
            s_ImportReceiver = null;

            if (studio == null || result == 0)
            {
                return;
            }

            if (result == 1 && length >= 0 && length <= ApartmentProject.MaximumFileBytes)
            {
                var bytes = new byte[length];

                if (length > 0)
                {
                    Marshal.Copy(buffer, bytes, 0, length);
                }

                studio.ReadProject(Encoding.UTF8.GetString(bytes));
            }
            else
            {
                studio.Status(result == 2 ? "Файл проекта превышает 8 МБ." : "Браузеру не удалось прочитать файл проекта.", true);
            }
        }
#endif

        private void ExportProject()
        {
            var json = ApartmentProject.Write(_plan, new ApartmentProject.Lighting(_light, _warmth, _sunAngle, _cutaway));

            if (Encoding.UTF8.GetByteCount(json) > ApartmentProject.MaximumFileBytes)
            {
                Status("Файл проекта превышает 8 МБ.", true);
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            var downloaded = AtelierDownloadProject(ProjectFileName, json) != 0;
            Status(downloaded ? "Скачивание проекта началось." : "Браузеру не удалось скачать проект.", !downloaded);
#elif UNITY_EDITOR
            var path = UnityEditor.EditorUtility.SaveFilePanel("Экспорт проекта Atelier", "", Path.GetFileNameWithoutExtension(ProjectFileName), "json");

            if (!string.IsNullOrEmpty(path))
            {
                SaveProjectFile(path, json);
            }
#else
            ProjectPathDialog("Экспорт проекта", path => SaveProjectFile(path, json));
#endif
        }

        private void ImportProject()
        {
            CancelInteraction();
#if UNITY_WEBGL && !UNITY_EDITOR
            if (s_ImportReceiver is not null)
            {
                return;
            }

            s_ImportReceiver = this;

            if (AtelierPickProject(ApartmentProject.MaximumFileBytes, ReceiveProject) == 0)
            {
                s_ImportReceiver = null;
                Status("Браузеру не удалось открыть окно выбора файла.", true);
            }
#elif UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFilePanel("Импорт проекта Atelier", "", "json");

            if (!string.IsNullOrEmpty(path))
            {
                OpenProjectFile(path);
            }
#else
            ProjectPathDialog("Импорт проекта", OpenProjectFile, mustExist: true);
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void SaveProjectFile(string path, string json)
        {
            try
            {
                File.WriteAllText(path, json, new UTF8Encoding(false));
                Status("Проект сохранён в файл.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Status("Не удалось сохранить проект по указанному пути.", true);
            }
        }

        private void OpenProjectFile(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);

                if (stream.Length > ApartmentProject.MaximumFileBytes)
                {
                    Status("Файл проекта превышает 8 МБ.", true);
                    return;
                }

                using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
                ReadProject(reader.ReadToEnd());
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Status("Не удалось прочитать файл проекта.", true);
            }
        }

#if !UNITY_EDITOR
        private void ProjectPathDialog(string title, Action<string> action, string fileName = ProjectFileName, bool mustExist = false)
        {
            var overlay = Element(_root, "modal-shade");
            var dialog = Element(overlay, "modal");
            Text(dialog, title, "panel-title");
            var path = new TextField("Путь к файлу")
            {
                value = Path.Combine(Application.persistentDataPath, fileName)
            };
            path.AddToClassList("studio-field");
            dialog.Add(path);
            BindValidation(dialog, path, validatePath, allowEdit: value => value.IndexOfAny(Path.GetInvalidPathChars()) < 0);
            var buttons = Element(dialog, "field-row");
            Action(buttons, "Отмена", () => overlay.RemoveFromHierarchy(), "secondary-button");
            var proceed = Action(buttons, "Продолжить", submitPath, "primary-button");
            RequireFields(proceed, path);

            string validatePath(string value)
            {
                try
                {
                    var fullPath = Path.GetFullPath(value.Trim());

                    if (mustExist)
                    {
                        return File.Exists(fullPath) ? null : "По указанному пути нет файла.";
                    }

                    return Path.GetFileName(fullPath).Length > 0 && Directory.Exists(Path.GetDirectoryName(fullPath)) && !Directory.Exists(fullPath) ? null : "Введите имя файла и выберите существующую папку.";
                }
                catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
                {
                    return "Введите действительный путь к файлу.";
                }
            }

            void submitPath()
            {
                if (!ValidField(path))
                {
                    return;
                }

                overlay.RemoveFromHierarchy();
                action(path.value.Trim());
            }
        }
#endif
#endif

        private void ReadProject(string json)
        {
            if (!ApartmentProject.TryRead(json, _catalog.Entries, _materials.Floors.Count, _materials.Walls.Count, out var plan, out var lighting, out var error))
            {
                Status(error, true);
                return;
            }

            if (_plan.Rooms.Count == 0)
            {
                ApplyProject(plan, lighting);
                return;
            }

            var overlay = Element(_root, "modal-shade");
            var dialog = Element(overlay, "modal");
            Text(dialog, "Открыть проект?", "panel-title");
            Text(dialog, $"Комнат в файле — {plan.Rooms.Count}. Открытие файла заменит текущий план.", "panel-caption");
            var actions = Element(dialog, "field-row");
            Action(actions, "Отмена", () => overlay.RemoveFromHierarchy(), "secondary-button");
            Action(actions, "Открыть", confirmOpen, "primary-button");

            void confirmOpen()
            {
                overlay.RemoveFromHierarchy();
                ApplyProject(plan, lighting);
            }
        }

        private void ApplyProject(ApartmentPlan plan, ApartmentProject.Lighting lighting)
        {
            CancelInteraction();
            _stage.EndWalk();

            if (_in3D && plan.Rooms.Count == 0)
            {
                ChangeMode(false);
            }

            _plan.ReplaceWith(plan);
            _room = null;
            _opening = null;
            _selectedItem = null;
            _tool = _plan.Rooms.Count > 0 ? PlanTool.Select : PlanTool.Room;

            _cutaway = lighting.Cutaway;
            _light = lighting.Intensity;
            _warmth = lighting.Warmth;
            _sunAngle = lighting.Angle;
            _stage.SetCutaway(_cutaway);
            _stage.SetLighting(_light, _warmth, _sunAngle);

            RebuildStage();
            RefreshWorkspace();
            FrameProjectViews();
            Status("Проект открыт.");
        }
    }
}
