using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class SubmarineInfo : IDisposable
    {
        public Sprite PreviewImage;

        partial void InitProjectSpecific()
        {
            string previewImageData = SubmarineElement.GetAttributeString("previewimage", "");
            if (!string.IsNullOrEmpty(previewImageData))
            {
                try
                {
                    using (MemoryStream mem = new MemoryStream(Convert.FromBase64String(previewImageData)))
                    {
                        var texture = TextureLoader.FromStream(mem, path: FilePath);
                        if (texture == null) { throw new Exception("PreviewImage texture returned null"); }
                        PreviewImage = new Sprite(texture, null, null);
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Loading the preview image of the submarine \"" + Name + "\" failed. The file may be corrupted.", e);
                    GameAnalyticsManager.AddErrorEventOnce("Submarine..ctor:PreviewImageLoadingFailed", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Loading the preview image of the submarine \"" + Name + "\" failed. The file may be corrupted.");
                    PreviewImage = null;
                }
            }
        }


        public void CreatePreviewWindow(GUIComponent parent)
        {
            var content = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform), style: null);

            if (PreviewImage == null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform), TextManager.Get(SavedSubmarines.Contains(this) ? "SubPreviewImageNotFound" : "SubNotDownloaded"));
            }
            else
            {
                var submarinePreviewBackground = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform), style: null) { Color = Color.Black };
                new GUIImage(new RectTransform(new Vector2(0.98f), submarinePreviewBackground.RectTransform, Anchor.Center), PreviewImage, scaleToFit: true);
                new GUIFrame(new RectTransform(Vector2.One, submarinePreviewBackground.RectTransform), "InnerGlow", color: Color.Black);
            }
            var descriptionBox = new GUIListBox(new RectTransform(new Vector2(1, 0.5f), content.RectTransform, Anchor.BottomCenter))
            {
                UserData = "descriptionbox",
                ScrollBarVisible = true,
                Spacing = 5
            };

            //space
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.03f), descriptionBox.Content.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1, 0), descriptionBox.Content.RectTransform), TextManager.Get("submarine.name." + Name, true) ?? Name, font: GUI.LargeFont, wrap: true) { ForceUpperCase = true, CanBeFocused = false };

            float leftPanelWidth = 0.6f;
            float rightPanelWidth = 0.4f / leftPanelWidth;

            ScalableFont font = descriptionBox.Rect.Width < 350 ? GUI.SmallFont : GUI.Font;

            Vector2 realWorldDimensions = Dimensions * Physics.DisplayToRealWorldRatio;
            if (realWorldDimensions != Vector2.Zero)
            {
                string dimensionsStr = TextManager.GetWithVariables("DimensionsFormat", new string[2] { "[width]", "[height]" }, new string[2] { ((int)realWorldDimensions.X).ToString(), ((int)realWorldDimensions.Y).ToString() });

                var dimensionsText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                    TextManager.Get("Dimensions"), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), dimensionsText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                    dimensionsStr, textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                dimensionsText.RectTransform.MinSize = new Point(0, dimensionsText.Children.First().Rect.Height);
            }

            if (RecommendedCrewSizeMax > 0)
            {
                var crewSizeText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                    TextManager.Get("RecommendedCrewSize"), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), crewSizeText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                    RecommendedCrewSizeMin + " - " + RecommendedCrewSizeMax, textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                crewSizeText.RectTransform.MinSize = new Point(0, crewSizeText.Children.First().Rect.Height);
            }

            if (!string.IsNullOrEmpty(RecommendedCrewExperience))
            {
                var crewExperienceText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                    TextManager.Get("RecommendedCrewExperience"), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), crewExperienceText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                    TextManager.Get(RecommendedCrewExperience), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                crewExperienceText.RectTransform.MinSize = new Point(0, crewExperienceText.Children.First().Rect.Height);
            }

            if (RequiredContentPackages.Any())
            {
                var contentPackagesText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                    TextManager.Get("RequiredContentPackages"), textAlignment: Alignment.TopLeft, font: font)
                { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), contentPackagesText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                    string.Join(", ", RequiredContentPackages), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                contentPackagesText.RectTransform.MinSize = new Point(0, contentPackagesText.Children.First().Rect.Height);
            }

            // show what game version the submarine was created on
            if (!IsVanillaSubmarine() && GameVersion != null)
            {
                var versionText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                        TextManager.Get("serverlistversion"), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), versionText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                        GameVersion.ToString(), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };

                versionText.RectTransform.MinSize = new Point(0, versionText.Children.First().Rect.Height);
            }

            GUITextBlock.AutoScaleAndNormalize(descriptionBox.Content.Children.Where(c => c is GUITextBlock).Cast<GUITextBlock>());

            //space
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), descriptionBox.Content.RectTransform), style: null);

            if (!string.IsNullOrEmpty(Description))
            {
                new GUITextBlock(new RectTransform(new Vector2(1, 0), descriptionBox.Content.RectTransform),
                    TextManager.Get("SaveSubDialogDescription", fallBackTag: "WorkshopItemDescription"), font: GUI.Font, wrap: true)
                { CanBeFocused = false, ForceUpperCase = true };
            }

            new GUITextBlock(new RectTransform(new Vector2(1, 0), descriptionBox.Content.RectTransform), Description, font: font, wrap: true)
            {
                CanBeFocused = false
            };
        }
    }
}
