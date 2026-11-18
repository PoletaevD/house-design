using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace HouseDesign
{
    public sealed partial class ApartmentStudio
    {
        private const int PosterWidth = 1600;
        private const int PosterHeight = 1000;
        private const string PosterFileName = "Atelier-Poster.png";
        private PanelSettings _posterPanel;
        private RenderTexture _posterTarget;
        private RenderTexture _posterOverview;
        private GameObject _posterDocument;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int AtelierDownloadPng(string fileName, byte[] png, int size);
#endif

        private void ExportPoster()
        {
            if (_plan.Rooms.Count == 0)
            {
                Status("Добавьте хотя бы одну комнату для постера.", true);
                return;
            }

            if (_posterTarget != null)
            {
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            StartPoster(null);
#elif UNITY_EDITOR
            var path = UnityEditor.EditorUtility.SaveFilePanel("Сохранить постер квартиры", "", Path.GetFileNameWithoutExtension(PosterFileName), "png");

            if (!string.IsNullOrEmpty(path))
            {
                StartPoster(path);
            }
#else
            ProjectPathDialog("Сохранить постер квартиры", StartPoster, PosterFileName);
#endif
        }

        private void StartPoster(string path)
        {
            try
            {
                _posterTarget = new RenderTexture(PosterWidth, PosterHeight, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Apartment poster"
                };

                var overview = new RenderTextureDescriptor(740, 716, RenderTextureFormat.ARGB32, 24)
                {
                    msaaSamples = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.msaaSampleCount
                };
                overview.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(overview);
                _posterOverview = new RenderTexture(overview)
                {
                    name = "Poster 3D view"
                };
                _stage.RenderOverview(_posterOverview, !_in3D);

                _posterPanel = ScriptableObject.CreateInstance<PanelSettings>();
                _posterPanel.themeStyleSheet = _panelSettings.themeStyleSheet;
                _posterPanel.targetTexture = _posterTarget;
                _posterPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
                _posterPanel.SetScreenToPanelSpaceFunction(_ => new Vector2(-10000f, -10000f));

                _posterDocument = new GameObject("Poster layout");
                _posterDocument.transform.SetParent(transform, false);
                _posterDocument.SetActive(false);
                var document = _posterDocument.AddComponent<UIDocument>();
                document.panelSettings = _posterPanel;
                _posterDocument.SetActive(true);

                var root = document.rootVisualElement;
                var plan = ComposePoster(root);
                StartCoroutine(WritePoster(path, root, plan));
            }
            catch (Exception error)
            {
                ReleasePoster();
                Status("Не удалось собрать постер.", true);
                Debug.LogException(error);
            }
        }

        private PlanCanvas ComposePoster(VisualElement root)
        {
            root.pickingMode = PickingMode.Ignore;
            root.styleSheets.Add(Resources.Load<StyleSheet>("ApartmentStudio"));
            root.AddToClassList("poster-page");

            var header = Element(root, "poster-header");
            var title = Element(header, "poster-heading");
            Text(title, "ATELIER", "poster-brand");
            Text(title, "План квартиры", "poster-title");
            var area = 0f;

            foreach (var room in _plan.Rooms)
            {
                area += room.Bounds.width * room.Bounds.height;
            }

            Text(header, $"{area:0.0} м²   ·   Комнат: {_plan.Rooms.Count}", "poster-summary");
            var views = Element(root, "poster-views");

            var plan = new PlanCanvas(_plan, true)
            {
                pickingMode = PickingMode.Ignore
            };
            plan.AddToClassList("plan-canvas");
            addView("2D · Чертёж", plan);
            var model = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };
            model.AddToClassList("poster-model");
            model.style.backgroundImage = Background.FromRenderTexture(_posterOverview);
            addView("3D · Интерьер", model);

            return plan;

            void addView(string label, VisualElement view)
            {
                var card = Element(views, "poster-card");
                card.pickingMode = PickingMode.Ignore;
                Text(card, label, "poster-view-title");
                var window = Element(card, "poster-window");
                window.pickingMode = PickingMode.Ignore;
                window.Add(view);
            }
        }

        private IEnumerator WritePoster(string path, VisualElement root, PlanCanvas plan)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            var captions = plan.LayoutPosterCaptions();

            if (captions.Count > 0)
            {
                var legend = Element(root, "poster-legend");
                Text(legend, "Комнаты на чертеже", "poster-legend-title");
                var entries = Element(legend, "poster-legend-entries");

                foreach (var caption in captions)
                {
                    Text(entries, caption, "poster-legend-entry").enableRichText = false;
                }

                yield return null;
                yield return new WaitForEndOfFrame();
                var legendHeight = legend.layout.height;

                if (!(legendHeight > 0f))
                {
                    Status("Не удалось разместить список комнат на постере.", true);
                    ReleasePoster();
                    yield break;
                }

                if (legendHeight > SystemInfo.maxTextureSize - PosterHeight)
                {
                    Status("Список комнат превышает максимальный размер постера на этом устройстве.", true);
                    ReleasePoster();
                    yield break;
                }

                var height = PosterHeight + Mathf.CeilToInt(legendHeight);
                var original = _posterTarget;
                _posterTarget = new RenderTexture(PosterWidth, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = original.name
                };
                _posterPanel.targetTexture = _posterTarget;
                Destroy(original);
            }

            yield return null;
            yield return new WaitForEndOfFrame();

            var previous = RenderTexture.active;
            Texture2D image = null;

            try
            {
                var height = _posterTarget.height;
                image = new Texture2D(PosterWidth, height, TextureFormat.RGB24, false);
                RenderTexture.active = _posterTarget;
                image.ReadPixels(new Rect(0, 0, PosterWidth, height), 0, 0);
                var png = image.EncodeToPNG();
#if UNITY_WEBGL && !UNITY_EDITOR
                var downloaded = AtelierDownloadPng(PosterFileName, png, png.Length) != 0;
                Status(downloaded ? "Скачивание постера началось." : "Браузеру не удалось скачать постер.", !downloaded);
#else
                File.WriteAllBytes(path, png);
                Status("Постер PNG сохранён.");
#endif
            }
            catch (Exception error)
            {
                Status("Не удалось сохранить постер.", true);
                Debug.LogException(error);
            }
            finally
            {
                RenderTexture.active = previous;
                Destroy(image);
                ReleasePoster();
            }
        }

        private void ReleasePoster()
        {
            if (_posterDocument != null)
            {
                _posterDocument.SetActive(false);
                Destroy(_posterDocument);
            }

            Destroy(_posterPanel);
            Destroy(_posterTarget);
            Destroy(_posterOverview);
            _posterTarget = null;
            _posterDocument = null;
            _posterPanel = null;
            _posterOverview = null;
        }
    }
}
