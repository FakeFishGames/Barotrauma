using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    class EditMapScreen : Screen
    {
        private Camera cam;

        public GUIComponent topPanel, leftPanel;

        private GUIComponent[] GUItabs;
        private int selectedTab;

        private GUIFrame loadFrame;

        private GUIFrame saveFrame;

        private GUITextBox nameBox;

        const int PreviouslyUsedCount = 10;
        private GUIListBox previouslyUsedList;

        //a Character used for picking up and manipulating items
        private Character dummyCharacter;
        
        private bool characterMode;

        private bool wiringMode;
        private GUIFrame wiringToolPanel;

        private Tutorials.EditorTutorial tutorial;

        public Camera Cam
        {
            get { return cam; }
        }

        public int SelectedTab
        {
            get { return selectedTab; }
        }

        public string GetSubName()
        {
            return (Submarine.MainSub == null) ? "" : Submarine.MainSub.Name;
        }

        private string GetItemCount()
        {
            return "Items: " +Item.ItemList.Count;
        }

        private string GetStructureCount()
        {
            return "Structures: " + (MapEntity.mapEntityList.Count - Item.ItemList.Count);
        }

        private string GetPhysicsBodyCount()
        {
            return "Physics bodies: " + GameMain.World.BodyList.Count;
        }

        public bool CharacterMode
        {
            get { return characterMode; }
        }

        public bool WiringMode
        {
            get { return wiringMode; }
        }


        public EditMapScreen()
        {
            cam = new Camera(); 
            //cam.Translate(new Vector2(-10.0f, 50.0f));

            selectedTab = -1;

            topPanel = new GUIFrame(new Rectangle(0, 0, 0, 31), GUI.Style);
            topPanel.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);

            var button = new GUIButton(new Rectangle(0, 0, 70, 20), "Open...", GUI.Style, topPanel);
            button.OnClicked = CreateLoadScreen;

            button = new GUIButton(new Rectangle(80,0,70,20), "Save", GUI.Style, topPanel);
            button.OnClicked = (GUIButton btn, object data) =>
            {
                CreateSaveScreen();

                return true;
            };

            var nameLabel = new GUITextBlock(new Rectangle(170, -4, 150, 20), "", GUI.Style, topPanel, GUI.LargeFont);
            nameLabel.TextGetter = GetSubName;

            var linkedSubBox = new GUIDropDown(new Rectangle(750,0,200,20), "Add submarine", GUI.Style, topPanel);
            linkedSubBox.ToolTip = 
                "Places another submarine into the current submarine file. "+
                "Can be used for adding things such as smaller vessels, "+
                "escape pods or detachable sections into the main submarine.";

            foreach (Submarine sub in Submarine.SavedSubmarines)
            {
                linkedSubBox.AddItem(sub.Name, sub);
            }
            linkedSubBox.OnSelected += SelectLinkedSub;
            
            leftPanel = new GUIFrame(new Rectangle(0, 30, 150, GameMain.GraphicsHeight-30), GUI.Style);
            leftPanel.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            
            GUITextBlock itemCount = new GUITextBlock(new Rectangle(0, 30, 0, 20), "", GUI.Style, leftPanel);
            itemCount.TextGetter = GetItemCount;

            GUITextBlock structureCount = new GUITextBlock(new Rectangle(0, 50, 0, 20), "", GUI.Style, leftPanel);
            structureCount.TextGetter = GetStructureCount;

            GUItabs = new GUIComponent[Enum.GetValues(typeof(MapEntityCategory)).Length];                       

            int width = 400, height = 400;
            int y = 90;
            int i = 0;
            foreach (MapEntityCategory category in Enum.GetValues(typeof(MapEntityCategory)))
            {
                var catButton = new GUIButton(new Rectangle(0, y, 0, 20), category.ToString(), Alignment.Left, GUI.Style, leftPanel);
                catButton.UserData = i;
                catButton.OnClicked = SelectTab;
                y+=25;

                GUItabs[i] = new GUIFrame(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), GUI.Style);
                GUItabs[i].Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

                new GUITextBlock(new Rectangle(-200, 0, 100, 15), "Filter", GUI.Style, Alignment.TopRight, Alignment.TopRight, GUItabs[i], false, GUI.SmallFont);

                GUITextBox searchBox = new GUITextBox(new Rectangle(-20, 0, 180, 15), Alignment.TopRight, GUI.Style, GUItabs[i]);
                searchBox.Font = GUI.SmallFont;
                searchBox.OnTextChanged = FilterMessages;
                GUIComponent.KeyboardDispatcher.Subscriber = searchBox;

                var clearButton = new GUIButton(new Rectangle(0, 0, 15, 15), "x", Alignment.TopRight, GUI.Style, GUItabs[i]);
                clearButton.OnClicked = ClearFilter;
                clearButton.UserData = searchBox;

                GUIListBox itemList = new GUIListBox(new Rectangle(0, 20, 0, 0), Color.White * 0.7f, GUI.Style, GUItabs[i]);
                itemList.OnSelected = SelectPrefab;
                itemList.CheckSelected = MapEntityPrefab.GetSelected;

                foreach (MapEntityPrefab ep in MapEntityPrefab.list)
                {
                    if (!ep.Category.HasFlag(category)) continue;

                    Color color = ((itemList.CountChildren % 2) == 0) ? Color.Transparent : Color.White * 0.1f;

                    GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 50), Color.Transparent, null, itemList);
                    frame.UserData = ep;
                    frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                    frame.Color = color;
                    frame.HoverColor = Color.Gold * 0.2f;
                    frame.SelectedColor = Color.Gold * 0.5f;

                    GUITextBlock textBlock = new GUITextBlock(
                        new Rectangle(40, 0, 0, 25),
                        ep.Name,
                        Color.Transparent, Color.White,
                        Alignment.Left, Alignment.Left,
                        null, frame);
                    textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                    if (!string.IsNullOrWhiteSpace(ep.Description))
                    {
                        textBlock.ToolTip = ep.Description;
                    }

                    if (ep.sprite != null)
                    {
                        GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), ep.sprite, Alignment.Left, frame);
                        img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
                        img.Color = ep.SpriteColor;
                    }
                }

                itemList.children.Sort((i1, i2) => (i1.UserData as MapEntityPrefab).Name.CompareTo((i2.UserData as MapEntityPrefab).Name));

                i++;
            }

            y+=50;
            button = new GUIButton(new Rectangle(0, y, 0, 20), "Character mode", Alignment.Left, GUI.Style, leftPanel);
            button.ToolTip = "Allows you to pick up and use items. Useful for things such as placing items inside closets, turning devices on/off and doing the wiring.";
            button.OnClicked = ToggleCharacterMode;

            y += 35;
            button = new GUIButton(new Rectangle(0, y, 0, 20), "Wiring mode", Alignment.Left, GUI.Style, leftPanel);
            //button.ToolTip = "Allows you to pick up and use items. Useful for things such as placing items inside closets, turning devices on/off and doing the wiring.";
            button.OnClicked = ToggleWiringMode;
            
            y+=50;
            button = new GUIButton(new Rectangle(0, y, 0, 20), "Generate waypoints", Alignment.Left, GUI.Style, leftPanel);
            button.ToolTip = "AI controlled crew members require waypoints to navigate around the sub.";
            button.OnClicked = GenerateWaypoints;
            
            y+=50;

            new GUITextBlock(new Rectangle(0, y, 0, 20), "Show:", GUI.Style, leftPanel);
            
            var tickBox = new GUITickBox(new Rectangle(0,y+20,20,20), "Waypoints", Alignment.TopLeft, leftPanel);
            tickBox.OnSelected = (GUITickBox obj) => { WayPoint.ShowWayPoints = !WayPoint.ShowWayPoints; return true; };
            tickBox.Selected = true;
            tickBox = new GUITickBox(new Rectangle(0, y + 45, 20, 20), "Spawnpoints", Alignment.TopLeft, leftPanel);
            tickBox.OnSelected = (GUITickBox obj) => { WayPoint.ShowSpawnPoints = !WayPoint.ShowSpawnPoints; return true; };
            tickBox.Selected = true;
            tickBox = new GUITickBox(new Rectangle(0, y + 70, 20, 20), "Links", Alignment.TopLeft, leftPanel);
            tickBox.OnSelected = (GUITickBox obj) => { Item.ShowLinks = !Item.ShowLinks; return true; };
            tickBox.Selected = true;
            tickBox = new GUITickBox(new Rectangle(0, y + 95, 20, 20), "Hulls", Alignment.TopLeft, leftPanel);
            tickBox.OnSelected = (GUITickBox obj) => { Hull.ShowHulls = !Hull.ShowHulls; return true; };
            tickBox.Selected = true;
            tickBox = new GUITickBox(new Rectangle(0, y + 120, 20, 20), "Gaps", Alignment.TopLeft, leftPanel);
            tickBox.OnSelected = (GUITickBox obj) => { Gap.ShowGaps = !Gap.ShowGaps; return true; };
            tickBox.Selected = true;

            y+=150;

            if (y < GameMain.GraphicsHeight - 100)
            {
                new GUITextBlock(new Rectangle(0, y, 0, 15), "Previously used:", GUI.Style, leftPanel);

                previouslyUsedList = new GUIListBox(new Rectangle(0, y + 15, 0, Math.Min(GameMain.GraphicsHeight - y - 40, 150)), GUI.Style, leftPanel);
                previouslyUsedList.OnSelected = SelectPrefab;
            }

        }

        public void StartTutorial()
        {
            tutorial = new Tutorials.EditorTutorial("EditorTutorial");

            CoroutineManager.StartCoroutine(tutorial.UpdateState());
        }

        public override void Select()
        {
            base.Select();
            
            GUIComponent.MouseOn = null;
            characterMode = false;

            if (Submarine.MainSub != null)
            {
                cam.Position = Submarine.MainSub.Position + Submarine.MainSub.HiddenSubPosition;
                //nameBox.Text = Submarine.MainSub.Name;
                //descriptionBox.Text = ToolBox.LimitString(Submarine.MainSub.Description, 15);
            }
            else
            {
                cam.Position = Submarine.HiddenSubStartPosition;
                //if (nameBox != null) nameBox.Text = "";
                //descriptionBox.Text = "";

                Submarine.MainSub = new Submarine(Path.Combine(Submarine.SavePath, "Unnamed.sub"), "", false);
            }

            //nameBox.Deselect();

            cam.UpdateTransform();
        }

        public override void Deselect()
        {
            base.Deselect();

            GUIComponent.MouseOn = null;

            MapEntityPrefab.Selected = null;

            MapEntity.DeselectAll();

            if (characterMode) ToggleCharacterMode();            

            if (wiringMode) ToggleWiringMode();

            if (dummyCharacter != null)
            {
                dummyCharacter.Remove();
                dummyCharacter = null;
                GameMain.World.ProcessChanges();
            }
        }

        private void CreateDummyCharacter()
        {
            if (dummyCharacter != null) dummyCharacter.Remove();

            dummyCharacter = Character.Create(Character.HumanConfigFile, Vector2.Zero);

            for (int i = 0; i<dummyCharacter.Inventory.SlotPositions.Length; i++)
            {
                dummyCharacter.Inventory.SlotPositions[i].X += false ? -1000 : leftPanel.Rect.Width+10;
            }

            Character.Controlled = dummyCharacter;
            GameMain.World.ProcessChanges();
        }

        private bool SaveSub(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                GUI.AddMessage("Name your submarine before saving it", Color.Red, 3.0f);

                nameBox.Flash();
                return false;
            }

            if (nameBox.Text.Contains("../"))
            {
                DebugConsole.ThrowError("Illegal symbols in filename (../)");
                nameBox.Flash();
                return false;
            }

            string savePath = nameBox.Text + ".sub";

            if (Submarine.MainSub != null)
            {
                savePath = Path.Combine(Path.GetDirectoryName(Submarine.MainSub.FilePath), savePath);
            }
            else
            {
                savePath = Path.Combine(Submarine.SavePath, savePath);
            }

            Submarine.SaveCurrent(savePath);
            Submarine.MainSub.CheckForErrors();

            GUI.AddMessage("Submarine saved to " + Submarine.MainSub.FilePath, Color.Green, 3.0f);
            
            return false;
        }

        private void CreateSaveScreen()
        {
            int width = 400, height = 400;

            int y = 0;

            saveFrame = new GUIFrame(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), GUI.Style, null);
            saveFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            new GUITextBlock(new Rectangle(0,0,200,30), "Save submarine", GUI.Style, saveFrame, GUI.LargeFont);

            y += 30;

            new GUITextBlock(new Rectangle(0,y,150,20), "Name:", GUI.Style, saveFrame);
            y += 20;

            nameBox = new GUITextBox(new Rectangle(5, y, 250, 20), GUI.Style, saveFrame);
            nameBox.OnEnterPressed = ChangeSubName;
            nameBox.Text = GetSubName();

            y += 30;
            
            new GUITextBlock(new Rectangle(0, y, 150, 20), "Description:", GUI.Style, saveFrame);
            y += 20;

            var descriptionBox = new GUITextBox(new Rectangle(5, y, 0, 100), null, null, Alignment.TopLeft,
                Alignment.TopLeft, GUI.Style, saveFrame);
            descriptionBox.Wrap = true;
            descriptionBox.Text = Submarine.MainSub == null ? "" : Submarine.MainSub.Description;
            descriptionBox.OnEnterPressed = ChangeSubDescription;

            y += descriptionBox.Rect.Height + 15;
            new GUITextBlock(new Rectangle(0, y, 150, 20), "Settings:", GUI.Style, saveFrame);

            y += 20;

            int tagX = 10, tagY = 0;
            foreach (SubmarineTag tag in Enum.GetValues(typeof(SubmarineTag)))
            {
                FieldInfo fi = typeof(SubmarineTag).GetField(tag.ToString());
                DescriptionAttribute[] attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                string tagStr = attributes.Length > 0 ? attributes[0].Description : "";

                var tagTickBox = new GUITickBox(new Rectangle(tagX, y+ tagY, 20, 20), tagStr, Alignment.TopLeft, saveFrame);
                tagTickBox.Selected = Submarine.MainSub == null ? false : Submarine.MainSub.HasTag(tag);
                tagTickBox.UserData = tag;

                tagTickBox.OnSelected = (GUITickBox tickBox) =>
                    {
                        if (Submarine.MainSub == null) return false;

                        if (tickBox.Selected)
                        {
                            Submarine.MainSub.AddTag((SubmarineTag)tickBox.UserData);
                        }
                        else
                        {
                            Submarine.MainSub.RemoveTag((SubmarineTag)tickBox.UserData);
                        }

                        return true;
                    };

                tagY += 25;
                if (tagY > 100)
                {
                    tagY = 0;
                    tagX += 200;
                }
            }
            
            var saveButton = new GUIButton(new Rectangle(-90, 0, 80, 20), "Save", Alignment.Right | Alignment.Bottom, GUI.Style, saveFrame);
            saveButton.OnClicked = SaveSub;

            var cancelButton = new GUIButton(new Rectangle(0, 0, 80, 20), "Cancel", Alignment.Right | Alignment.Bottom, GUI.Style, saveFrame);
            cancelButton.OnClicked = (GUIButton btn, object userdata) =>
            {
                saveFrame = null;
                return true;
            };

        }

        private bool CreateLoadScreen(GUIButton button, object obj)
        {
            Submarine.Preload();

            int width = 300, height = 400;
            loadFrame = new GUIFrame(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), GUI.Style, null);
            loadFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            var subList = new GUIListBox(new Rectangle(0, 0, 0, height - 50), Color.White, GUI.Style, loadFrame);
            subList.OnSelected = (GUIComponent selected, object userData) =>
                {
                    var deleteBtn = loadFrame.FindChild("delete") as GUIButton;
                    if (deleteBtn != null) deleteBtn.Enabled = true;

                    return true;
                };

            foreach (Submarine sub in Submarine.SavedSubmarines)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    sub.Name,
                    GUI.Style,
                    Alignment.Left, Alignment.Left, subList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = sub;
                textBlock.ToolTip = sub.FilePath;
            }

            var deleteButton = new GUIButton(new Rectangle(0, 0, 70, 20), "Delete", Alignment.BottomLeft, GUI.Style, loadFrame);
            deleteButton.Enabled = false;
            deleteButton.UserData = "delete";
            deleteButton.OnClicked = (GUIButton btn, object userdata) =>
            {
                var subListBox = loadFrame.GetChild<GUIListBox>();

                if (subList.Selected!=null)
                {
                    Submarine sub = subList.Selected.UserData as Submarine;
                    try
                    {
                        System.IO.File.Delete(sub.FilePath);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Couldn't delete file ''"+sub.FilePath+"''!", e);
                    }
                }

                deleteButton.Enabled = false;

                CreateLoadScreen(null, null);

                return true;
            };

            var loadButton = new GUIButton(new Rectangle(-90, 0, 80, 20), "Load", Alignment.Right | Alignment.Bottom, GUI.Style, loadFrame);
            loadButton.OnClicked = LoadSub;

            var cancelButton = new GUIButton(new Rectangle(0, 0, 80, 20), "Cancel", Alignment.Right | Alignment.Bottom, GUI.Style, loadFrame);
            cancelButton.OnClicked = (GUIButton btn, object userdata) =>
                {
                    loadFrame = null;
                    return true;
                };

            return true;
        }

        private bool LoadSub(GUIButton button, object obj)
        {
            GUIListBox subList = loadFrame.GetChild<GUIListBox>();

            if (subList.Selected == null) return false;

            Submarine selectedSub = subList.Selected.UserData as Submarine;

            if (selectedSub == null) return false;

            Submarine.MainSub = selectedSub;
            selectedSub.Load(true);

            //nameBox.Text = selectedSub.Name;
            //descriptionBox.Text = ToolBox.LimitString(selectedSub.Description,15);

            loadFrame = null;

            return true;
        }

        private bool SelectTab(GUIButton button, object obj)
        {
            selectedTab = (int)obj;

            ClearFilter(GUItabs[selectedTab].GetChild<GUIButton>(), null);

            GUIComponent.KeyboardDispatcher.Subscriber = GUItabs[selectedTab].GetChild<GUITextBox>();            

            return true;
        }

        private bool FilterMessages(GUITextBox textBox, string text)
        {
            if (selectedTab == -1)
            {
                GUIComponent.KeyboardDispatcher.Subscriber = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                GUItabs[selectedTab].GetChild<GUIListBox>().children.ForEach(c => c.Visible = true);
                return true;
            }

            text = text.ToLower();

            foreach (GUIComponent child in GUItabs[selectedTab].GetChild<GUIListBox>().children)
            {
                var textBlock = child.GetChild<GUITextBlock>();
                child.Visible = textBlock.Text.ToLower().Contains(text);
            }

            GUItabs[selectedTab].GetChild<GUIListBox>().BarScroll = 0.0f;

            return true;
        }

        public bool ClearFilter(GUIComponent button, object obj)
        {
            FilterMessages(null, "");

            var searchBox = button.UserData as GUITextBox;
            if (searchBox != null) searchBox.Text = "";

            return true;
        }

        public void ToggleCharacterMode()
        {
            ToggleCharacterMode(null,null);
        }

        private bool ToggleCharacterMode(GUIButton button, object obj)
        {
            selectedTab = -1;

            characterMode = !characterMode;         
            //button.Color = (characterMode) ? Color.Gold : Color.White;
            
                wiringMode = false;

            if (characterMode)
            {
                CreateDummyCharacter();
            }
            else if (dummyCharacter != null)
            {
                RemoveDummyCharacter();
            }

            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                me.IsHighlighted = false;
                me.IsSelected = false;
            }
            
            return true;
        }

        private void ToggleWiringMode()
        {
            ToggleWiringMode(null, null);
        }
        
        private bool ToggleWiringMode(GUIButton button, object obj)
        {
            wiringMode = !wiringMode;

            characterMode = false;


            if (wiringMode)
            {
                CreateDummyCharacter();

                var screwdriverPrefab = ItemPrefab.list.Find(ip => ip.Name == "Screwdriver") as ItemPrefab;

                var item = new Item(screwdriverPrefab, Vector2.Zero, null);

                dummyCharacter.Inventory.TryPutItem(item, new List<InvSlotType>() {InvSlotType.RightHand}, false);

                wiringToolPanel = CreateWiringPanel();
            }
            else
            {
                RemoveDummyCharacter();
            }
            
            return true;
        }

        private void RemoveDummyCharacter()
        {
            if (dummyCharacter == null) return;
            
            foreach (Item item in dummyCharacter.Inventory.Items)
            {
                if (item == null) continue;

                item.Remove();
            }

            dummyCharacter.Remove();
            dummyCharacter = null;
            
        }

        private GUIFrame CreateWiringPanel()
        {
            GUIFrame frame = new GUIFrame(new Rectangle(0,0,50,300), null, Alignment.Right | Alignment.CenterY, GUI.Style);
            frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);

            GUIListBox listBox = new GUIListBox(Rectangle.Empty, GUI.Style, frame);
            listBox.OnSelected = SelectWire;

            foreach (MapEntityPrefab ep in MapEntityPrefab.list)
            {
                var itemPrefab = ep as ItemPrefab;
                if (itemPrefab == null || itemPrefab.Name == null || !itemPrefab.Name.Contains("Wire")) continue;

                GUIFrame imgFrame = new GUIFrame(new Rectangle(0, 0, (int)itemPrefab.sprite.size.X, (int)itemPrefab.sprite.size.Y), null, listBox);
                imgFrame.UserData = itemPrefab;
                imgFrame.HoverColor = Color.White * 0.5f;
                imgFrame.SelectedColor = Color.Gold * 0.7f;

                var img = new GUIImage(new Rectangle(0, 0, (int)itemPrefab.sprite.size.X, (int)itemPrefab.sprite.size.Y), itemPrefab.sprite, Alignment.TopLeft, imgFrame);
                img.Color = ep.SpriteColor;
            }

            return frame;
        }

        private bool SelectLinkedSub(GUIComponent selected, object userData)
        {
            var submarine = selected.UserData as Submarine;
            if (submarine == null) return false;

            var prefab = new LinkedSubmarinePrefab(submarine);

            MapEntityPrefab.SelectPrefab(prefab);

            return true;
        }

        private bool SelectWire(GUIComponent component, object userData)
        {
            if (dummyCharacter == null) return false;

            //if the same type of wire has already been selected, deselect it and return
            Item existingWire = dummyCharacter.SelectedItems.FirstOrDefault(i => i != null && i.Prefab == userData as ItemPrefab);
            if (existingWire != null)
            {
                existingWire.Drop();
                existingWire.Remove();
                return false;
            }

            var wire = new Item(userData as ItemPrefab, Vector2.Zero, null);

            int slotIndex = dummyCharacter.Inventory.FindLimbSlot(InvSlotType.LeftHand);

            //if there's some other type of wire in the inventory, remove it
            existingWire = dummyCharacter.Inventory.Items[slotIndex];
            if (existingWire != null && existingWire.Prefab != userData as ItemPrefab)
            {
                existingWire.Drop();
                existingWire.Remove();
            }

            dummyCharacter.Inventory.TryPutItem(wire, slotIndex, false, false);

            return true;
           
        }

        private bool ChangeSubName(GUITextBox textBox, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                textBox.Flash(Color.Red);
                return false;
            }

            if (Submarine.MainSub != null) Submarine.MainSub.Name = text;
            textBox.Deselect();

            textBox.Text = text;

            textBox.Flash(Color.Green);

            return true;
        }

        private bool ChangeSubDescription(GUITextBox textBox, string text)
        {
            if (Submarine.MainSub != null)
            {
                Submarine.MainSub.Description = text;
            }
            else
            {
                textBox.UserData = text;
            }

            // textBox.Rect = new Rectangle(textBox.Rect.Location, new Point(textBox.Rect.Width, 20));
            
            textBox.Text = ToolBox.LimitString(text, 15);

            textBox.Flash(Color.Green);
            textBox.Deselect();

            return true;
        }
        
        private bool SelectPrefab(GUIComponent component, object obj)
        {
            AddPreviouslyUsed(obj as MapEntityPrefab);

            MapEntityPrefab.SelectPrefab(obj);
            selectedTab = -1;
            GUIComponent.MouseOn = null;
            return false;
        }

        private bool GenerateWaypoints(GUIButton button, object obj)
        {
            if (Submarine.MainSub == null) return false;

            WayPoint.GenerateSubWaypoints(Submarine.MainSub);
            return true;
        }

        private void AddPreviouslyUsed(MapEntityPrefab mapEntityPrefab)
        {
            if (previouslyUsedList == null || mapEntityPrefab == null) return;

            previouslyUsedList.Deselect();

            if (previouslyUsedList.CountChildren == PreviouslyUsedCount)
            {
                previouslyUsedList.RemoveChild(previouslyUsedList.children.Last());
            }

            var existing = previouslyUsedList.FindChild(mapEntityPrefab);
            if (existing != null) previouslyUsedList.RemoveChild(existing);

            string name = ToolBox.LimitString(mapEntityPrefab.Name,15);

            var textBlock = new GUITextBlock(new Rectangle(0,0,0,15), name, GUI.Style, previouslyUsedList);
            textBlock.UserData = mapEntityPrefab;

            previouslyUsedList.RemoveChild(textBlock);
            previouslyUsedList.children.Insert(0, textBlock);
        }


        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        public override void Update(double deltaTime)
        {
            if (tutorial != null) tutorial.Update((float)deltaTime);

            if (GUIComponent.MouseOn == null)
            {
                //if (nameBox.Selected && PlayerInput.LeftButtonClicked())
                //{
                //    ChangeSubName(nameBox, nameBox.Text);
                //}

                cam.MoveCamera((float)deltaTime);
                //cam.Zoom = MathHelper.Clamp(cam.Zoom + (PlayerInput.ScrollWheelSpeed / 1000.0f)*cam.Zoom, 0.1f, 2.0f);
            }

            if (characterMode || wiringMode)
            {
                if (dummyCharacter == null || Entity.FindEntityByID(dummyCharacter.ID) != dummyCharacter)
                {
                    ToggleCharacterMode(null, null);
                }
                else
                {
                    if (dummyCharacter.SelectedConstruction==null)
                    {
                        Vector2 mouseSimPos = FarseerPhysics.ConvertUnits.ToSimUnits(dummyCharacter.CursorPosition);
                        foreach (Limb limb in dummyCharacter.AnimController.Limbs)
                        {
                            limb.body.SetTransform(mouseSimPos, 0.0f);
                        }
                    }

                    dummyCharacter.ControlLocalPlayer((float)deltaTime, cam, false);
                    dummyCharacter.Control((float)deltaTime, cam);

                    dummyCharacter.Submarine = Submarine.MainSub;

                    cam.TargetPos = Vector2.Zero;

                }
            }
            else
            {

                MapEntity.UpdateSelecting(cam);
            }

            GUIComponent.MouseOn = null;

            leftPanel.Update((float)deltaTime);
            topPanel.Update((float)deltaTime);

            if (wiringMode)
            {
                if (!dummyCharacter.SelectedItems.Any(it => it != null && it.HasTag("Wire")))
                {
                    wiringToolPanel.GetChild<GUIListBox>().Deselect();
                }
                wiringToolPanel.Update((float)deltaTime);
            }

            if (loadFrame!=null)
            {
                loadFrame.Update((float)deltaTime);
                if (PlayerInput.RightButtonClicked()) loadFrame = null;
            }
            else if (saveFrame != null)
            {
                saveFrame.Update((float)deltaTime);
            }
            else if (selectedTab > -1)
            {
                GUItabs[selectedTab].Update((float)deltaTime);
                if (PlayerInput.RightButtonClicked()) selectedTab = -1;
            }
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform();

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                null, null, null, null,
                cam.Transform);

            graphics.Clear(new Color(0.051f, 0.149f, 0.271f, 1.0f));
            if (GameMain.DebugDraw)
            {
                GUI.DrawLine(spriteBatch, new Vector2(0.0f, -cam.WorldView.Y), new Vector2(0.0f, -(cam.WorldView.Y - cam.WorldView.Height)), Color.White*0.5f, 1.0f, (int)(2.0f/cam.Zoom));
                GUI.DrawLine(spriteBatch, new Vector2(cam.WorldView.X, -Submarine.MainSub.HiddenSubPosition.Y), new Vector2(cam.WorldView.Right, -Submarine.MainSub.HiddenSubPosition.Y), Color.White * 0.5f, 1.0f, (int)(2.0f / cam.Zoom));
            }
           
            Submarine.Draw(spriteBatch, true);

            if (!characterMode && !wiringMode)
            {
                if (MapEntityPrefab.Selected != null) MapEntityPrefab.Selected.UpdatePlacing(spriteBatch, cam);

                MapEntity.DrawSelecting(spriteBatch, cam);
            }


            spriteBatch.End();

            //-------------------- HUD -----------------------------

            spriteBatch.Begin();

            leftPanel.Draw(spriteBatch);
            topPanel.Draw(spriteBatch);

            //EntityPrefab.DrawList(spriteBatch, new Vector2(20,50));
            
            if ((characterMode || wiringMode) && dummyCharacter != null)                     
            {
                foreach (MapEntity me in MapEntity.mapEntityList)
                {
                    me.IsHighlighted = false;
                }

                dummyCharacter.AnimController.FindHull(dummyCharacter.CursorWorldPosition, false); 

                foreach (Item item in dummyCharacter.SelectedItems)
                {
                    if (item == null) continue;
                    item.SetTransform(dummyCharacter.SimPosition, 0.0f);

                    item.Update(cam, (float)deltaTime);
                }

                if (dummyCharacter.SelectedConstruction != null)
                {
                    dummyCharacter.SelectedConstruction.DrawHUD(spriteBatch, dummyCharacter);

                    if (PlayerInput.KeyHit(InputType.Select) && dummyCharacter.ClosestItem != dummyCharacter.SelectedConstruction) dummyCharacter.SelectedConstruction = null;
                }

                dummyCharacter.DrawHUD(spriteBatch, cam);
                
                if (wiringMode) wiringToolPanel.Draw(spriteBatch);
            }
            else
            {
                if (loadFrame!=null)
                {
                    loadFrame.Draw(spriteBatch);
                }
                else if (saveFrame != null)
                {
                    saveFrame.Draw(spriteBatch);
                }
                else if (selectedTab > -1)
                {
                    GUItabs[selectedTab].Draw(spriteBatch);
                }

                MapEntity.Edit(spriteBatch, cam);
            }

            if (tutorial != null) tutorial.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, cam);

            if (!PlayerInput.LeftButtonHeld()) Inventory.draggingItem = null;
                                              
            spriteBatch.End();
        }
    }
}
