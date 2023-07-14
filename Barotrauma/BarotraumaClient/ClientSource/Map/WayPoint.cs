﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class WayPoint : MapEntity
    {
        private static Dictionary<string, Sprite> iconSprites;
        private const int WaypointSize = 12, SpawnPointSize = 32;

        public override bool IsVisible(Rectangle worldView)
        {
            return Screen.Selected == GameMain.SubEditorScreen || GameMain.DebugDraw;
        }

        public override bool SelectableInEditor
        {
            get { return !IsHidden(); }
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!editing && (!GameMain.DebugDraw || Screen.Selected.Cam.Zoom < 0.1f)) { return; }
            if (IsHidden()) { return; }

            Vector2 drawPos = Position;
            if (Submarine != null) { drawPos += Submarine.DrawPosition; }
            drawPos.Y = -drawPos.Y;

            Draw(spriteBatch, drawPos);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 drawPos)
        {
            Color clr = CurrentHull == null ? Color.DodgerBlue : GUIStyle.Green;
            if (spawnType != SpawnType.Path) { clr = Color.Gray; }
            if (!IsTraversable)
            {
                clr = Color.Black;
            }
            if (IsHighlighted || IsHighlighted) { clr = Color.Lerp(clr, Color.White, 0.8f); }

            int iconSize = spawnType == SpawnType.Path ? WaypointSize : SpawnPointSize;
            if (ConnectedDoor != null || Ladders != null || Stairs != null || SpawnType != SpawnType.Path)
            {
                iconSize = (int)(iconSize * 1.5f);
            }

            if (IsSelected || IsHighlighted)
            {
                int glowSize = (int)(iconSize * 1.5f);
                GUIStyle.UIGlowCircular.Draw(spriteBatch,
                    new Rectangle((int)(drawPos.X - glowSize / 2), (int)(drawPos.Y - glowSize / 2), glowSize, glowSize),
                    Color.White);
            }

            Sprite sprite = iconSprites[SpawnType.ToString()];
            if (spawnType == SpawnType.Human && AssignedJob?.Icon != null)
            {
                sprite = iconSprites["Path"];
            }
            else if (ConnectedDoor != null)
            {
                sprite = iconSprites["Door"];
                if (ConnectedDoor.IsHorizontal && Ladders == null)
                {
                    clr = Color.Yellow;
                }
            }
            else if (Ladders != null)
            {
                sprite = iconSprites["Ladder"];
            }
            sprite.Draw(spriteBatch, drawPos, clr, scale: iconSize / (float)sprite.SourceRect.Width, depth: 0.001f);
            sprite.RelativeOrigin = Vector2.One * 0.5f;
            if (spawnType == SpawnType.Human && AssignedJob?.Icon != null)
            {
                AssignedJob.Icon.Draw(spriteBatch, drawPos, AssignedJob.UIColor, scale: iconSize / (float)AssignedJob.Icon.SourceRect.Width * 0.8f, depth: 0.0f);
            }

            foreach (MapEntity e in linkedTo)
            {
                GUI.DrawLine(spriteBatch,
                    drawPos,
                    new Vector2(e.DrawPosition.X, -e.DrawPosition.Y),
                    (IsTraversable ? GUIStyle.Green : Color.Gray) * 0.7f, width: 5, depth: 0.002f);
            }
            if (ConnectedGap != null)
            {
                GUI.DrawLine(spriteBatch,
                    drawPos,
                    new Vector2(ConnectedGap.DrawPosition.X, -ConnectedGap.DrawPosition.Y),
                    GUIStyle.Green * 0.5f, width: 1);
            }
            if (Ladders != null)
            {
                GUI.DrawLine(spriteBatch,
                    drawPos,
                    new Vector2(Ladders.Item.DrawPosition.X, -Ladders.Item.DrawPosition.Y),
                    GUIStyle.Green * 0.5f, width: 1);
            }

            var color = Color.WhiteSmoke;
            if (spawnType == SpawnType.Path)
            {
                if (linkedTo.Count < 2)
                {
                    if (linkedTo.Count == 0)
                    {
                        color = Color.Red;
                    }
                    else
                    {
                        if (CurrentHull == null)
                        {
                            color = Ladders == null ? Color.Red : Color.Yellow;
                        }
                        else
                        {
                            color = Color.Yellow;
                        }
                    }
                }
            }
            else if (spawnType == SpawnType.ExitPoint && ExitPointSize != Point.Zero)
            {
                GUI.DrawRectangle(spriteBatch, drawPos - ExitPointSize.ToVector2() / 2, ExitPointSize.ToVector2(), Color.Cyan, thickness: 5);
            }

            GUIStyle.SmallFont.DrawString(spriteBatch,
                ID.ToString(),
                new Vector2(DrawPosition.X - 10, -DrawPosition.Y - 30),
                color);
            if (Tunnel?.Type != null)
            {
                GUIStyle.SmallFont.DrawString(spriteBatch,
                Tunnel.Type.ToString(),
                new Vector2(DrawPosition.X - 10, -DrawPosition.Y - 45),
                color);
            }
        }

        public override bool IsMouseOn(Vector2 position)
        {
            if (IsHidden()) { return false; }
            float dist = Vector2.DistanceSquared(position, WorldPosition);
            float radius = (SpawnType == SpawnType.Path ? WaypointSize : SpawnPointSize) * 0.6f;
            return dist < radius * radius;
        }

        private bool IsHidden()
        {
            if (!SubEditorScreen.IsLayerVisible(this)) { return false; }
            if (spawnType == SpawnType.Path)
            {
                return (!GameMain.DebugDraw && !ShowWayPoints);
            }
            else
            {
                return (!GameMain.DebugDraw && !ShowSpawnPoints);
            }
        }

        public override void UpdateEditing(Camera cam, float deltaTime)
        {
            if (editingHUD == null || editingHUD.UserData != this)
            {
                editingHUD = CreateEditingHUD();
            }

            if (IsSelected && PlayerInput.PrimaryMouseButtonClicked() && GUI.MouseOn == null)
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

                if (PlayerInput.KeyDown(Keys.Space))
                {
                    foreach (MapEntity e in HighlightedEntities)
                    {
                        if (e is not WayPoint || e == this) { continue; }

                        if (linkedTo.Contains(e))
                        {
                            linkedTo.Remove(e);
                            e.linkedTo.Remove(this);
                        }
                        else
                        {
                            linkedTo.Add(e);
                            e.linkedTo.Add(this);
                        }
                    }
                }
                else
                {
                    FindHull();
                    // Update gaps, ladders, and stairs
                    UpdateLinkedEntity(position, Gap.GapList, gap => ConnectedGap = gap, gap =>
                    {
                        if (ConnectedGap == gap)
                        {
                            ConnectedGap = null;
                        }
                    });
                    UpdateLinkedEntity(position, Item.ItemList, i =>
                    {
                        var ladder = i?.GetComponent<Ladder>();
                        if (ladder != null)
                        {
                            Ladders = ladder;
                        }
                    }, i =>
                    {
                        var ladder = i?.GetComponent<Ladder>();
                        if (ladder != null)
                        {
                            if (Ladders == ladder)
                            {
                                Ladders = null;
                            }
                        }
                    }, inflate: 5);
                    FindStairs();
                    // TODO: Cannot check the rectangle, since the rectangle is not rotated -> Need to use the collider.
                    //var stairList = mapEntityList.Where(me => me is Structure s && s.StairDirection != Direction.None).Select(me => me as Structure);
                    //UpdateLinkedEntity(position, stairList, s =>
                    //{
                    //    Stairs = s;
                    //}, s =>
                    //{
                    //    if (Stairs == s)
                    //    {
                    //        Stairs = null;
                    //    }
                    //});
                }
            }
        }

        private void UpdateLinkedEntity<T>(Vector2 worldPos, IEnumerable<T> list, Action<T> match, Action<T> noMatch, int inflate = 0) where T : MapEntity
        {
            foreach (var entity in list)
            {
                var rect = entity.WorldRect;
                rect.Inflate(inflate, inflate);
                if (Submarine.RectContains(rect, worldPos))
                {
                    match(entity);
                }
                else
                {
                    noMatch(entity);
                }
            }
        }

        private bool ChangeSpawnType(GUIButton button, object obj)
        {
            var prevSpawnType = spawnType;
            GUITextBlock spawnTypeText = button.Parent.GetChildByUserData("spawntypetext") as GUITextBlock;
            var values = (SpawnType[])Enum.GetValues(typeof(SpawnType));
            int currIndex = values.IndexOf(spawnType);
            currIndex += (int)button.UserData;
            int firstIndex = 1;
            int lastIndex = values.Length - 1;
            if (currIndex > lastIndex)
            {
                currIndex = firstIndex;
            }
            if (currIndex < firstIndex)
            {
                currIndex = lastIndex;
            }
            spawnType = values[currIndex];
            spawnTypeText.Text = spawnType.ToString();
            if (spawnType == SpawnType.ExitPoint || prevSpawnType == SpawnType.ExitPoint) { CreateEditingHUD(); } 
            return true;
        }

        private GUIComponent CreateEditingHUD()
        {
            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.15f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) })
            {
                UserData = this
            };

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), editingHUD.RectTransform, Anchor.Center))
            {
                Stretch = true,
                AbsoluteSpacing = (int)(GUI.Scale * 5)
            };

            if (spawnType == SpawnType.Path)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), TextManager.Get("Waypoint"), font: GUIStyle.LargeFont);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), TextManager.Get("LinkWaypoint"));
            }
            else
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), TextManager.Get("Spawnpoint"), font: GUIStyle.LargeFont);
                
                var spawnTypeContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), spawnTypeContainer.RectTransform), TextManager.Get("SpawnType"));

                var button = new GUIButton(new RectTransform(Vector2.One, spawnTypeContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIMinusButton")
                {
                    UserData = -1,
                    OnClicked = ChangeSpawnType
                };
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), spawnTypeContainer.RectTransform), spawnType.ToString(), textAlignment: Alignment.Center)
                {
                    UserData = "spawntypetext"
                };
                button = new GUIButton(new RectTransform(Vector2.One, spawnTypeContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIPlusButton")
                {
                    UserData = 1,
                    OnClicked = ChangeSpawnType
                };

                var descText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform),
                    TextManager.Get("IDCardDescription"), font: GUIStyle.SmallFont)
                {
                    ToolTip = TextManager.Get("IDCardDescriptionTooltip")
                };
                GUITextBox propertyBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), descText.RectTransform, Anchor.CenterRight), IdCardDesc)
                {
                    MaxTextLength = 150,
                    ToolTip = TextManager.Get("IDCardDescriptionTooltip")
                };
                propertyBox.OnTextChanged += (textBox, text) =>
                {
                    IdCardDesc = text;
                    return true;
                };
                propertyBox.OnEnterPressed += (textBox, text) =>
                {
                    IdCardDesc = text;
                    textBox.Flash(GUIStyle.Green);
                    return true;
                };
                propertyBox.OnDeselected += (textBox, keys) =>
                {
                    IdCardDesc = textBox.Text;
                    textBox.Flash(GUIStyle.Green);
                };

                var idCardTagsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform),
                    TextManager.Get("IDCardTags"), font: GUIStyle.SmallFont)
                {
                    ToolTip = TextManager.Get("IDCardTagsTooltip")
                };
                propertyBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), idCardTagsText.RectTransform, Anchor.CenterRight), string.Join(", ", idCardTags))
                {
                    MaxTextLength = 60,
                    ToolTip = TextManager.Get("IDCardTagsTooltip")
                };
                propertyBox.OnTextChanged += (textBox, text) =>
                {
                    IdCardTags = text.Split(',');
                    return true;
                };
                propertyBox.OnEnterPressed += (textBox, text) =>
                {
                    textBox.Text = string.Join(",", IdCardTags);
                    textBox.Flash(GUIStyle.Green);
                    return true;
                };
                propertyBox.OnDeselected += (textBox, keys) =>
                {
                    textBox.Text = string.Join(",", IdCardTags);
                    textBox.Flash(GUIStyle.Green);
                };

                var jobsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform),
                    TextManager.Get("SpawnpointJobs"), font: GUIStyle.SmallFont)
                {
                    ToolTip = TextManager.Get("SpawnpointJobsTooltip")
                };
                var jobDropDown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1.0f), jobsText.RectTransform, Anchor.CenterRight))
                {
                    ToolTip = TextManager.Get("SpawnpointJobsTooltip"),
                    OnSelected = (selected, userdata) =>
                    {
                        AssignedJob = userdata as JobPrefab;
                        return true;
                    }
                };
                jobDropDown.AddItem(TextManager.Get("Any"), null);
                foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
                {
                    jobDropDown.AddItem(jobPrefab.Name, jobPrefab);
                }
                jobDropDown.SelectItem(AssignedJob);

                var tagsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform),
                    TextManager.Get("spawnpointtags"), font: GUIStyle.SmallFont);
                propertyBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), tagsText.RectTransform, Anchor.CenterRight), string.Join(", ", tags))
                {
                    MaxTextLength = 60,
                    ToolTip = TextManager.Get("spawnpointtagstooltip")
                };
                propertyBox.OnTextChanged += (textBox, text) =>
                {
                    tags = text.Split(',').ToIdentifiers().ToHashSet();
                    return true;
                };
                propertyBox.OnEnterPressed += (textBox, text) =>
                {
                    textBox.Text = string.Join(",", tags);
                    textBox.Flash(GUIStyle.Green);
                    return true;
                };
                propertyBox.OnDeselected += (textBox, keys) =>
                {
                    textBox.Text = string.Join(",", tags);
                    textBox.Flash(GUIStyle.Green);
                };

                if (SpawnType == SpawnType.ExitPoint)
                {
                    var sizeField = GUI.CreatePointField(ExitPointSize, GUI.IntScale(20), TextManager.Get("dimensions"), paddedFrame.RectTransform);
                    GUINumberInput xField = null, yField = null;
                    foreach (GUIComponent child in sizeField.GetAllChildren())
                    {
                        if (yField == null)
                        {
                            yField = child as GUINumberInput;
                        }
                        else
                        {
                            xField = child as GUINumberInput;
                            if (xField != null) { break; }
                        }
                    }
                    xField.MinValueInt = 0;
                    xField.OnValueChanged = (numberInput) => { ExitPointSize = new Point(numberInput.IntValue, ExitPointSize.Y); };
                    yField.MinValueInt = 0;
                    yField.OnValueChanged = (numberInput) => { ExitPointSize = new Point(ExitPointSize.X, numberInput.IntValue); };
                }
            }

            editingHUD.RectTransform.Resize(new Point(
                editingHUD.Rect.Width,
                (int)(paddedFrame.Children.Sum(c => c.Rect.Height + paddedFrame.AbsoluteSpacing) / paddedFrame.RectTransform.RelativeSize.Y)));

            PositionEditingHUD();

            return editingHUD;
        }
    }
}
