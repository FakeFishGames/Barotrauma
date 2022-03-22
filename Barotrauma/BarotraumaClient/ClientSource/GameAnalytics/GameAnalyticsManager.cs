using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    static partial class GameAnalyticsManager
    {
        static partial void CreateConsentPrompt()
        {
            if (consentTextAvailable)
            {
                var background = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker");
                var frame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.7f), background.RectTransform, Anchor.Center) { MinSize = new Point(800, 0), MaxSize = new Point(1500, int.MaxValue) });

                var content = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), frame.RectTransform, Anchor.Center))
                {
                    Stretch = true,
                    AbsoluteSpacing = GUI.IntScale(15)
                };

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), TextManager.Get("statisticsconsentheader"), font: GUI.SubHeadingFont, textColor: Color.White);
                var mainText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), TextManager.Get("statisticsconsenttext"), wrap: true, parseRichText: true);

                foreach (var data in mainText.RichTextData)
                {
                    mainText.ClickableAreas.Add(new GUITextBlock.ClickableArea()
                    {
                        Data = data,
                        OnClick = (GUITextBlock component, GUITextBlock.ClickableArea area) =>
                        {
                            GameMain.Instance.ShowOpenUrlInWebBrowserPrompt("https://gameanalytics.com/privacy/");
                        }
                    });
                }

                string privacyPolicyText = File.ReadAllText("daedalic_privacypolicy.txt");
                var privacyPolicyBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform) { MaxSize = new Point(int.MaxValue, GUI.IntScale(200)) });
                var privacyPolicy = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), privacyPolicyBox.Content.RectTransform), privacyPolicyText, wrap: true)
                {
                    CanBeFocused = false
                };
                privacyPolicy.RectTransform.MinSize = new Point(0, (int)privacyPolicy.TextSize.Y);

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), TextManager.Get("statisticsconsentstatement"), wrap: true);

                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), isHorizontal: true);

                void buttonContainerSpacing(float width)
                    => new GUIFrame(new RectTransform(new Vector2(width, 1.0f), buttonContainer.RectTransform), style: null);

                buttonContainerSpacing(0.1f);
                var yesBtn = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonContainer.RectTransform), TextManager.Get("Yes"));
                yesBtn.OnClicked += (btn, userdata) =>
                {
                    GUIMessageBox.MessageBoxes.Remove(background);
                    SetConsentInternal(Consent.Yes);
                    return true;
                };
                yesBtn.Enabled = false;

                IEnumerable<CoroutineStatus> enableAfterTime(WaitForSeconds time, params GUIComponent[] components)
                {
                    yield return time;
                    foreach (var c in components)
                    {
                        c.Enabled = true;
                    }
                    yield return CoroutineStatus.Success;
                }
                
                buttonContainerSpacing(0.2f);

                var noBtn = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonContainer.RectTransform), TextManager.Get("No"));
                noBtn.OnClicked += (btn, userdata) =>
                {
                    GUIMessageBox.MessageBoxes.Remove(background);
                    SetConsent(Consent.No);
                    return true;
                };
                noBtn.Enabled = false;

                CoroutineManager.StartCoroutine(enableAfterTime(new WaitForSeconds(0.3f), yesBtn, noBtn));

                buttonContainerSpacing(0.1f);

                buttonContainer.RectTransform.MinSize = new Point(0, yesBtn.RectTransform.MinSize.Y);
                buttonContainer.RectTransform.MaxSize = new Point(int.MaxValue, yesBtn.RectTransform.MinSize.Y);

                content.Recalculate();
                foreach (var child in content.Children)
                {
                    if (child is GUITextBlock textBlock)
                    {
                        textBlock.TextScale = MathHelper.Min(1.0f, 1.0f / GameSettings.TextScale);
                        textBlock.RectTransform.MinSize = new Point(0, (int)textBlock.TextSize.Y);
                        textBlock.RectTransform.MaxSize = new Point(int.MaxValue, (int)textBlock.TextSize.Y);
                    }
                }

                int contentHeight = content.Children.Sum(c => c.RectTransform.MaxSize.Y + content.AbsoluteSpacing);
                frame.RectTransform.MinSize = new Point(frame.RectTransform.MinSize.X, (int)(contentHeight / content.RectTransform.RelativeSize.Y));
                frame.RectTransform.MaxSize = new Point(frame.RectTransform.MaxSize.X, (int)(contentHeight / content.RectTransform.RelativeSize.Y));

                GUIMessageBox.MessageBoxes.Add(background);
            }
            else
            {
                //user statistics disabled by default if the prompt cannot be shown in the user's language
                SetConsent(Consent.Unknown);
            }
        }
    }
}