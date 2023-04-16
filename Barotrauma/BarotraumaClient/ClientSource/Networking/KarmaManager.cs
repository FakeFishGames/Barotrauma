using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    partial class KarmaManager : ISerializableEntity
    {
        public void CreateSettingsFrame(GUIComponent parent)
        {
            if (TextManager.ContainsTag("Karma.ResetKarmaBetweenRounds"))
            {
                CreateLabeledTickBox(parent, "ResetKarmaBetweenRounds");
            }

            CreateLabeledSlider(parent, 0.0f, 40.0f, 1.0f, nameof(KickBanThreshold));
            if (TextManager.ContainsTag("Karma.KicksBeforeBan"))
            {
                CreateLabeledNumberInput(parent, 0, 10, nameof(KicksBeforeBan));
            }
            CreateLabeledSlider(parent, 0.0f, 50.0f, 1.0f, nameof(HerpesThreshold));

            CreateLabeledSlider(parent, 0.0f, 0.5f, 0.01f, nameof(KarmaDecay));
            CreateLabeledSlider(parent, 50.0f, 100.0f, 1.0f, nameof(KarmaDecayThreshold));
            CreateLabeledSlider(parent, 0.0f, 0.5f, 0.01f, nameof(KarmaIncrease));
            CreateLabeledSlider(parent, 0.0f, 50.0f, 1.0f, nameof(KarmaIncreaseThreshold));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.12f), parent.RectTransform), TextManager.Get("Karma.PositiveActions"), 
                textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont)
            {
                CanBeFocused = false
            };

            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.01f, nameof(StructureRepairKarmaIncrease));
            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.01f, nameof(HealFriendlyKarmaIncrease));
            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.01f, nameof(DamageEnemyKarmaIncrease));
            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.01f, nameof(ItemRepairKarmaIncrease));
            CreateLabeledSlider(parent, 0.0f, 10.0f, 0.05f, nameof(ExtinguishFireKarmaIncrease));
            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.01f, nameof(BallastFloraKarmaIncrease));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.12f), parent.RectTransform), TextManager.Get("Karma.NegativeActions"), 
                textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont)
            {
                CanBeFocused = false
            };

            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.01f, nameof(StructureDamageKarmaDecrease));
            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.01f, nameof(DamageFriendlyKarmaDecrease));
            //hide these for now if a localized text is not available
            if (TextManager.ContainsTag("Karma." + nameof(StunFriendlyKarmaDecrease)))
            {
                CreateLabeledSlider(parent, 0.0f, 1.0f, 0.01f, nameof(StunFriendlyKarmaDecrease));
            }
            if (TextManager.ContainsTag("Karma." + nameof(StunFriendlyKarmaDecreaseThreshold)))
            {
                CreateLabeledSlider(parent, 0.0f, 10.0f, 1.0f, nameof(StunFriendlyKarmaDecreaseThreshold));
            }
            CreateLabeledSlider(parent, 0.0f, 100.0f, 1.0f, nameof(ReactorMeltdownKarmaDecrease));
            CreateLabeledSlider(parent, 0.0f, 10.0f, 0.05f, nameof(ReactorOverheatKarmaDecrease));
            CreateLabeledNumberInput(parent, 0, 20, nameof(AllowedWireDisconnectionsPerMinute));
            CreateLabeledSlider(parent, 0.0f, 20.0f, 0.5f, nameof(WireDisconnectionKarmaDecrease));
            CreateLabeledSlider(parent, 0.0f, 30.0f, 1.0f, nameof(SpamFilterKarmaDecrease));

            //hide these for now if a localized text is not available
            if (TextManager.ContainsTag("Karma." + nameof(DangerousItemStealKarmaDecrease)))
            {
                CreateLabeledSlider(parent, 0.0f, 30.0f, 1.0f, nameof(DangerousItemStealKarmaDecrease));
            }
            if (TextManager.ContainsTag("Karma." + nameof(DangerousItemStealBots)))
            {
                CreateLabeledTickBox(parent, nameof(DangerousItemStealBots));
            }
            CreateLabeledSlider(parent, 0.0f, 30.0f, 0.5f, nameof(DangerousItemContainKarmaDecrease));
            CreateLabeledTickBox(parent, nameof(IsDangerousItemContainKarmaDecreaseIncremental));
            CreateLabeledSlider(parent, 0.0f, 100.0f, 1.0f, nameof(MaxDangerousItemContainKarmaDecrease));
        }

        private void CreateLabeledSlider(GUIComponent parent, float min, float max, float step, string propertyName)
        {
            var container = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f,
                ToolTip = TextManager.Get("Karma." + propertyName + "ToolTip")
            };

            LocalizedString labelText = TextManager.Get("Karma." + propertyName);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), container.RectTransform),
                labelText, textAlignment: Alignment.CenterLeft, font: GUIStyle.SmallFont)
            {
                ToolTip = TextManager.Get("Karma." + propertyName + "ToolTip")
            };

            var slider = new GUIScrollBar(new RectTransform(new Vector2(0.3f, 1.0f), container.RectTransform), barSize: 0.1f, style: "GUISlider")
            {
                Step = step <= 0.0f ? 0.0f : step / (max - min),
                Range = new Vector2(min, max),
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    string formattedValueStr = step >= 1.0f ?
                        ((int)scrollBar.BarScrollValue).ToString() :
                        scrollBar.BarScrollValue.Format(decimalCount: step <= 0.1f ? 2 : 1);
                    label.Text = TextManager.AddPunctuation(':', labelText, formattedValueStr);
                    return true;
                }
            };
            container.RectTransform.MinSize = new Point(0, container.RectTransform.Children.Max(c => c.MinSize.Y));
            GameMain.NetworkMember.ServerSettings.AssignGUIComponent(propertyName, slider);
            slider.OnMoved(slider, slider.BarScroll);
        }

        private void CreateLabeledNumberInput(GUIComponent parent, int min, int max, string propertyName)
        {
            var container = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f,
                ToolTip = TextManager.Get("Karma." + propertyName + "ToolTip")
            };

            LocalizedString labelText = TextManager.Get("Karma." + propertyName);
            new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), container.RectTransform), labelText, textAlignment: Alignment.CenterLeft, font: GUIStyle.SmallFont)
            {
                ToolTip = TextManager.Get("Karma." + propertyName + "ToolTip")
            };

            var numInput = new GUINumberInput(new RectTransform(new Vector2(0.3f, 1.0f), container.RectTransform), NumberType.Int)
            {
                MinValueInt = min,
                MaxValueInt = max
            };

            container.RectTransform.MinSize = new Point(0, container.RectTransform.Children.Max(c => c.MinSize.Y));
            GameMain.NetworkMember.ServerSettings.AssignGUIComponent(propertyName, numInput);
        }

        private void CreateLabeledTickBox(GUIComponent parent, string propertyName)
        {
            var tickBox = new GUITickBox(new RectTransform(new Vector2(0.3f, 0.1f), parent.RectTransform), TextManager.Get("Karma." + propertyName))
            {
                ToolTip = TextManager.Get("Karma." + propertyName + "ToolTip").Fallback("")
            };
            GameMain.NetworkMember.ServerSettings.AssignGUIComponent(propertyName, tickBox);
        }
    }
}
