using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;

namespace Barotrauma
{
    class InGameInfoCharacter
    {
        //This is to make it so the hosts original characters are, essentially, also treated as clients for filtering purposes.
        public Boolean IsHostCharacter;
        public Client client;
        public Character character;
        public Boolean Removed = false;
        public float RemovalTimer = 3f;
        //public CharacterInfo characterinfo;
    }

    class StatusWidget : GUIComponent
    {
        Character character;

        GUITextBlock healthlabel;
        GUITextBlock bleedlabel;
        GUITextBlock oxygenlabel;
        GUITextBlock pressurelabel;
        GUITextBlock stunlabel;
        GUITextBlock husklabel;

        public StatusWidget(Rectangle rect, Alignment alignment, Character character, GUIComponent parent = null)
            : base(null)
        {
            this.rect = rect;

            this.alignment = alignment;

            this.character = character;

            color = new Color(15,15,15,125);

            int barheight = rect.Y;

            healthlabel = new GUITextBlock(new Rectangle(rect.X, barheight, 55, 15), "Health", null, Alignment.Center, Alignment.Center, this, false);
            healthlabel.TextColor = Color.Black;
            healthlabel.TextScale = 0.75f;
            healthlabel.Visible = true;

            bleedlabel = new GUITextBlock(new Rectangle(rect.X + 55, barheight, 45, 15), "Bleed", null, Alignment.Center, Alignment.Center, this, false);
            bleedlabel.TextColor = Color.Black;
            bleedlabel.TextScale = 0.75f;
            bleedlabel.Visible = false;

            barheight += 15;

            oxygenlabel = new GUITextBlock(new Rectangle(rect.X, barheight, 55, 15), "Oxygen", null, Alignment.Center, Alignment.Center, this, false);
            oxygenlabel.TextColor = Color.Black;
            oxygenlabel.TextScale = 0.75f;
            oxygenlabel.Visible = false;

            pressurelabel = new GUITextBlock(new Rectangle(rect.X + 55, barheight, 45, 15), "Pressure", null, Alignment.Center, Alignment.Center, this, false);
            pressurelabel.TextColor = Color.Black;
            pressurelabel.TextScale = 0.75f;
            pressurelabel.Visible = false;

            barheight += 15;

            stunlabel = new GUITextBlock(new Rectangle(rect.X, barheight, 55, 15), "Stun", null, Alignment.Center, Alignment.Center, this, false);
            stunlabel.TextColor = Color.Black;
            stunlabel.TextScale = 0.75f;
            stunlabel.Visible = true;

            husklabel = new GUITextBlock(new Rectangle(rect.X + 55, barheight, 45, 15), "Husk", null, Alignment.Center, Alignment.Center, this, false);
            husklabel.TextColor = Color.Black;
            husklabel.TextScale = 0.75f;
            husklabel.Visible = false;


            if (parent != null) parent.AddChild(this);
            this.parent = parent;
        }

        public override void Update(float deltaTime)
        {
            if (!Visible) return;
            base.Update(deltaTime);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            base.Draw(spriteBatch);

            Color currColor = color;
            //if (state == ComponentState.Hover) currColor = hoverColor;
            //if (state == ComponentState.Selected) currColor = selectedColor;
            if (state == ComponentState.Hover) Parent.State = ComponentState.Hover;
            if (state == ComponentState.Selected) Parent.State = ComponentState.Selected;


            Color outLineColour = Color.Gray;

            //Negative Colours
            Color NegativeLow = new Color(145, 145, 145, 160);
            Color NegativeHigh = new Color(25, 25, 25, 220);

            //Health Colours
            Color HealthPositiveHigh = new Color(0, 255, 0, 15);
            Color HealthPositiveLow = new Color(255, 0, 0, 60);
            //Oxygen Colours
            Color OxygenPositiveHigh = new Color(0, 255, 255, 15);
            Color OxygenPositiveLow = new Color(0, 0, 200, 60);
            //Stun Colours
            Color StunPositiveHigh = new Color(235, 135, 45, 100);
            Color StunPositiveLow = new Color(204, 119, 34, 30);
            //Bleeding Colours
            Color BleedPositiveHigh = new Color(255, 50, 50, 100);
            Color BleedPositiveLow = new Color(150, 50, 50, 15);
            //Pressure Colours
            Color PressurePositiveHigh = new Color(255, 255, 0, 100);
            Color PressurePositiveLow = new Color(125, 125, 0, 15);

            //Husk Colours
            Color HuskPositiveHigh = new Color(255, 100, 255, 150);
            Color HuskPositiveLow = new Color(125, 30, 125, 15);

            float pressureFactor = (character.AnimController.CurrentHull == null) ?
            100.0f : Math.Min(character.AnimController.CurrentHull.LethalPressure, 100.0f);
            if (character.PressureProtection > 0.0f && (character.WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthOutsideHull || (character.WorldPosition.Y > GameMain.NilMod.PlayerCrushDepthInHull && character.CurrentHull != null))) pressureFactor = 0.0f;

            //GUI.DrawRectangle(spriteBatch, rect, currColor * (currColor.A / 255.0f), true);
            //GUI.DrawRectangle(spriteBatch, new Vector2(rect.X, rect.Y), new Vector2(80.0f, 10.0f), Color.Green, false, 0f);

            int barheight = rect.Y;

            if (!character.NeedsAir) barheight = barheight + 15;

            if (!character.IsDead)
            {
                Parent.Color = Color.Transparent;
                healthlabel.Rect = new Rectangle(rect.X, barheight, (character.Bleeding >= 0.1f ? 55 : 100), 15);
                healthlabel.TextScale = 0.75f;
                healthlabel.Text = "Health";

                if (character.Health >= 0f)
                {
                    GUI.DrawProgressBar(spriteBatch, new Vector2(rect.X, -barheight), new Vector2((character.Bleeding >= 0.1f ? 55.0f : 100.0f), 15.0f), character.Health / character.MaxHealth, Color.Lerp(HealthPositiveLow, HealthPositiveHigh, character.Health / character.MaxHealth), outLineColour, 0.5f, 0f, "Left");
                }
                //Health has gone below 0
                else
                {
                    GUI.DrawProgressBar(spriteBatch, new Vector2(rect.X, -barheight), new Vector2((character.Bleeding >= 0.1f ? 55.0f : 100.0f), 15.0f), -(character.Health / character.MaxHealth), Color.Lerp(NegativeLow, NegativeHigh, -(character.Health / character.MaxHealth)), outLineColour, 0.5f, 0f, "Right");
                }

                if (character.Bleeding >= 0.1f)
                {
                    GUI.DrawProgressBar(spriteBatch, new Vector2(rect.X + 55, -barheight), new Vector2(45.0f, 15.0f), character.Bleeding / 5f, Color.Lerp(BleedPositiveLow, BleedPositiveHigh, character.Bleeding / 5f), outLineColour, 0.5f, 0f, "Right");
                    bleedlabel.Rect = new Rectangle(rect.X + 55, barheight, 45, 15);
                    bleedlabel.Visible = true;
                }
                else
                {
                    bleedlabel.Visible = false;
                }

                barheight += 15;

                if (character.NeedsAir)
                {
                    Boolean showpressure = false;
                    if (pressureFactor / 100f >= 0.3f) showpressure = true;
                    //Oxygen Bar
                    if (character.Oxygen >= 0f)
                    {
                        GUI.DrawProgressBar(spriteBatch, new Vector2(rect.X, -barheight), new Vector2((showpressure ? 55 : 100), 15.0f), character.Oxygen / 100f, Color.Lerp(OxygenPositiveLow, OxygenPositiveHigh, character.Oxygen / 100f), outLineColour, 0.5f, 0f, "Left");
                    }
                    //Oxygen has gone below 0
                    else if (character.Oxygen < 0f)
                    {
                        GUI.DrawProgressBar(spriteBatch, new Vector2(rect.X, -barheight), new Vector2((showpressure ? 55f : 100f), 15.0f), -(character.Oxygen / 100f), Color.Lerp(NegativeLow, NegativeHigh, -(character.Oxygen / 100f)), outLineColour, 0.5f, 0f, "Right");
                    }
                    oxygenlabel.Rect = new Rectangle(rect.X, barheight, (showpressure ? 55 : 100), 15);
                    oxygenlabel.Visible = true;

                    //Pressure Bar
                    if (showpressure)
                    {
                        GUI.DrawProgressBar(spriteBatch, new Vector2(rect.X + 55, -barheight), new Vector2(45.0f, 15.0f), pressureFactor / 100f, Color.Lerp(PressurePositiveLow, PressurePositiveHigh, pressureFactor / 100f), outLineColour, 0.5f, 0f, "Right");
                        pressurelabel.Rect = new Rectangle(rect.X + 55, barheight, 45, 15);
                        pressurelabel.Visible = true;
                    }
                    else
                    {
                        pressurelabel.Visible = false;
                    }
                    barheight += 15;
                }
                else
                {
                    oxygenlabel.Visible = false;
                    pressurelabel.Visible = false;
                }

                stunlabel.Visible = true;

                if (character.huskInfection == null)
                {
                    //Stun bar
                    GUI.DrawProgressBar(spriteBatch, new Vector2(rect.X, -barheight), new Vector2(100.0f, 15.0f), character.Stun / 60f, Color.Lerp(StunPositiveLow, StunPositiveHigh, character.Stun / 60f), outLineColour, 0.5f, 0f, "Left");
                    stunlabel.Rect = new Rectangle(rect.X, barheight, 100, 15);
                    husklabel.Visible = false;
                }
                else
                {
                    //Stun bar
                    GUI.DrawProgressBar(spriteBatch, new Vector2(rect.X, -barheight), new Vector2(55.0f, 15.0f), character.Stun / 60f, Color.Lerp(StunPositiveLow, StunPositiveHigh, character.Stun / 60f), outLineColour, 0.5f, 0f, "Left");
                    stunlabel.Rect = new Rectangle(rect.X, barheight, 55, 15);

                    //Husk bar
                    GUI.DrawProgressBar(spriteBatch, new Vector2(rect.X + 55, -barheight), new Vector2(45.0f, 15.0f), character.HuskInfectionState, Color.Lerp(HuskPositiveLow, HuskPositiveHigh, character.HuskInfectionState), outLineColour, 0.5f, 0f, "Right");
                    husklabel.Rect = new Rectangle(rect.X + 55, barheight, 45, 15);
                    husklabel.Visible = true;
                }
            }
            else
            {
                Parent.Color = new Color(0, 0, 0, 150);

                if (!character.NeedsAir)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(rect.X, rect.Y + 15), new Vector2(rect.Width, rect.Height - 15), new Color(150, 5, 5, 15), true, 0f, 1);
                    healthlabel.Rect = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
                }
                else
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height), new Color(150, 5, 5, 15), true, 0f, 1);
                    healthlabel.Rect = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
                }
                
                healthlabel.TextScale = 1.4f;
                healthlabel.Text = "DECEASED.";

                bleedlabel.Visible = false;
                oxygenlabel.Visible = false;
                pressurelabel.Visible = false;
                stunlabel.Visible = false;
                husklabel.Visible = false;
            }

            DrawChildren(spriteBatch);
        }
    }


    class InGameInfo
    {
        private static Texture2D CommandIcons;
        private static Texture2D NoCommandIcon;

        public float LowestRemoveTimer;
        public int TotalRemovesleft;

        private GUIFrame ingameInfoFrame;
        private GUITextBlock ingameInfoFilterText;
        private GUITextBlock timerwarning;
        private GUIListBox clientguilist;

        private Sprite Controlsprites;

        private List<InGameInfoCharacter> characterlist;

        private List<InGameInfoCharacter> filteredcharacterlist;

        int currentfilter = 0;
        float IngameInfoScroll;
        int LastCharacterCount;

        public InGameInfo()
        {
            Initialize();
        }

        public void Initialize()
        {
            //if (CommandIcons == null) CommandIcons = TextureLoader.FromFile("Content/UI/NilMod/inventoryIcons.png");
            //if (NoCommandIcon == null) NoCommandIcon = TextureLoader.FromFile("Content/UI/NilMod/NoCommandIcon.png");
            if (NoCommandIcon == null) NoCommandIcon = TextureLoader.FromFile("Content/UI/uiButton.png");

            characterlist = new List<InGameInfoCharacter>();
            filteredcharacterlist = new List<InGameInfoCharacter>();

            ingameInfoFilterText = null;
            timerwarning = null;
            clientguilist = null;
            ingameInfoFrame = null;

            //InGameInfoClient Host = new InGameInfoClient();
            //clientlist.Add(Host);


            currentfilter = 0;
        }

        public void AddClient(Client newclient)
        {
            InGameInfoCharacter newingameinfoclient = new InGameInfoCharacter();
            newingameinfoclient.client = newclient;
            characterlist.Add(newingameinfoclient);
            UpdateGameInfoGUIList();
        }

        public void RemoveClient(Client removedclient)
        {
            InGameInfoCharacter inGameInfoClienttoremove = characterlist.Find(c => c.client == removedclient);
            if (inGameInfoClienttoremove != null)
            {
                if (inGameInfoClienttoremove.character != null)
                {
                    //We need to keep the character itself.
                    inGameInfoClienttoremove.client = null;
                }
                else
                {
                    //This is not a client, safe to completely remove.
                    RemoveEntry(inGameInfoClienttoremove);
                }
                
                UpdateGameInfoGUIList();
            }
        }

        public void AddNoneClientCharacter(Character newcharacter, Boolean IsHost = false)
        {
            InGameInfoCharacter newingameinfocharacter = new InGameInfoCharacter();
            newingameinfocharacter.character = newcharacter;
            newingameinfocharacter.IsHostCharacter = IsHost;
            characterlist.Add(newingameinfocharacter);
            UpdateGameInfoGUIList();
        }

        //Setting of a clients character or respawning characters need their entry (And thus the GUI) Updated.
        public void UpdateClientCharacter(Client clienttoupdate, Character newcharacter, Boolean UpdateGUIList = false)
        {
            InGameInfoCharacter inGameInfoClienttoremove = characterlist.Find(c => c.client == clienttoupdate);
            if (inGameInfoClienttoremove != null)
            {
                inGameInfoClienttoremove.character = newcharacter;

                //Don't spam updates for looped respawns
                if (UpdateGUIList) UpdateGameInfoGUIList();
            }
        }

        public void RemoveCharacter(Character character)
        {
            InGameInfoCharacter inGameInfoCharactertoremove = characterlist.Find(c => c.character == character);
            if (inGameInfoCharactertoremove != null)
            {
                if(inGameInfoCharactertoremove.client != null)
                {
                    //We need to keep the client itself.
                    inGameInfoCharactertoremove.character = null;
                }
                else
                {
                    //This is not a client, safe to completely remove.
                    RemoveEntry(inGameInfoCharactertoremove);
                }
                UpdateGameInfoGUIList();
            }
        }

        public void RemoveEntry(InGameInfoCharacter removed)
        {
            if(GameMain.Server.GameStarted)
            {
                removed.Removed = true;
                removed.RemovalTimer = 5f;
            }
            else
            {
                characterlist.Remove(removed);
            }
        }

        //Remove None-Client characters and clients character references (For end of round purposes)
        public void ResetGUIListData()
        {
            List<InGameInfoCharacter> noneClients = characterlist.FindAll(c => c.client == null);

            for (int i = 0; i < noneClients.Count; i++)
            {
                characterlist.Remove(noneClients[i]);
            }

            List<InGameInfoCharacter> clients = characterlist.FindAll(c => c.client != null);

            //Remove Clients character entries
            for (int i = 0; i < clients.Count; i++)
            {
                clients[i].character = null;
            }
        }

        public void AddToGUIUpdateList()
        {
            if (ingameInfoFrame != null) ingameInfoFrame.AddToGUIUpdateList();
        }

        public void Update(float deltaTime)
        {
            if (characterlist != null && characterlist.Count > 0)
            {
                Boolean UpdateGUIList = false;
                float LowestTimer = 100f;
                for (int i = characterlist.Count - 1; i >= 0; i--)
                {
                    if (characterlist[i].Removed)
                    {
                        characterlist[i].RemovalTimer -= deltaTime;
                        if (characterlist[i].RemovalTimer <= 0f)
                        {
                            characterlist.RemoveAt(i);
                        }
                    }
                }

                if(UpdateGUIList) UpdateGameInfoGUIList();

                if (timerwarning != null)
                {
                    if (LowestTimer <= 3f)
                    {
                        timerwarning.Visible = true;
                        timerwarning.Text = "Removal in: " + Math.Round(LowestTimer, 1) + "s";
                    }
                    else
                    {
                        timerwarning.Visible = false;
                        timerwarning.Text = "";
                    }
                }
            }
            else
            {
                if (timerwarning != null) timerwarning.Visible = false;
            }

            if (ingameInfoFrame != null)
            {
                ingameInfoFrame.Update(deltaTime);
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (ingameInfoFrame != null) ingameInfoFrame.Draw(spriteBatch);
        }

        public bool ToggleGameInfoFrame(GUIButton button, object obj)
        {
            if (ingameInfoFrame == null)
            {
                CreateGameInfoFrame();
            }
            else
            {
                ingameInfoFilterText = null;
                timerwarning = null;
                clientguilist = null;
                ingameInfoFrame = null;
            }

            return true;
        }



        public void CreateGameInfoFrame()
        {
            int width = 200, height = 600;


            ingameInfoFrame = new GUIFrame(
                Rectangle.Empty, new Color(0,0,0,0), "", null);
            ingameInfoFrame.CanBeFocused = false;

            var innerFrame = new GUIFrame(
                new Rectangle(-70, 50, width, height), new Color(255, 255, 255, 100), "", ingameInfoFrame);

            innerFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            var LeftButton = new GUIButton(new Rectangle(20, -30, 85, 20), "<-", "", innerFrame);
            LeftButton.UserData = -1;
            LeftButton.OnClicked += (btn, userData) =>
            {
                ChangeFilter(Convert.ToInt32(userData));
                UpdateGameInfoGUIList();
                return true;
            };

                var RightButton = new GUIButton(new Rectangle(105, -30, 85, 20), "->", "", innerFrame);
            RightButton.UserData = +1;
            RightButton.OnClicked += (btn, userData) =>
            {
                ChangeFilter(Convert.ToInt32(userData));
                UpdateGameInfoGUIList();
                return true;
            };

            timerwarning = new GUITextBlock(new Rectangle(25, 18, 150, 10), "", new Color(0, 0, 0, 0), new Color(200, 200, 10, 255), Alignment.Left, Alignment.Center,null, innerFrame, false);
            timerwarning.TextScale = 0.78f;

            ingameInfoFilterText = new GUITextBlock(new Rectangle(25, 0, 150, 20), "Filter: None", new Color(0,0,0,0),new Color(255, 255, 255, 255), Alignment.Left, Alignment.Center, "", innerFrame,false);

            clientguilist = new GUIListBox(new Rectangle(30, 30, 150, 500), new Color(15, 15, 15, 180), "", innerFrame);
            clientguilist.OutlineColor = new Color(0, 0, 0, 0);
            clientguilist.HoverColor = new Color(255, 255, 255, 20);
            clientguilist.SelectedColor = new Color(15, 15, 15, 20);
            clientguilist.OnSelected += (btn, userData) =>
            {
                clientguilist.Deselect();
                return true;
            };
            UpdateGameInfoGUIList();
        }

        public void UpdateGameInfoGUIList()
        {
            //Only update if its actually running and open (IE. ingame, etc) - it will do the necessary update on creation anyways
            if (ingameInfoFrame != null)
            {
                ChangeFilter(0);
                if (filteredcharacterlist.Count() > 0 && LastCharacterCount > 0)
                {
                    int scrolldifference = LastCharacterCount - filteredcharacterlist.Count();
                    float scrollchangepercent = LastCharacterCount / filteredcharacterlist.Count();
                    float newscroll = ((clientguilist.BarScroll * LastCharacterCount * 150)) / (150 * filteredcharacterlist.Count());
                    //Removed items
                    if (scrolldifference > 0)
                    {
                        newscroll = MathHelper.Clamp((clientguilist.BarScroll + ((scrolldifference * clientguilist.BarScroll) / filteredcharacterlist.Count())), 0f, 1f);
                    }
                    //Added Items
                    else if (scrolldifference < 0)
                    {
                        //TODO - Plead for mercy with somebody who actually understands how to math scrollbars
                        //This is the best "Scrollbar smoothing" A dummy like Nilanth could manage without another 50 hours tweaking it.
                        if (clientguilist.BarScroll < 0.002f)
                        {
                            newscroll = 0f;
                        }
                        else if (clientguilist.BarScroll < 0.1f)
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (-0.04f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                        else if (clientguilist.BarScroll < 0.2f)
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (-0.03f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                        else if (clientguilist.BarScroll < 0.3f)
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (-0.02f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                        else if (clientguilist.BarScroll < 0.4f)
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (-0.01f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                        else if (clientguilist.BarScroll < 0.5f)
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (0.01f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                        else if (clientguilist.BarScroll < 0.6f)
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (0.015f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                        else if (clientguilist.BarScroll < 0.7f)
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (0.15f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                        else if (clientguilist.BarScroll < 0.8f)
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (0.2f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                        else if (clientguilist.BarScroll < 0.9f)
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (0.22f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                        else
                        {
                            newscroll = MathHelper.Clamp((clientguilist.BarScroll - ((((-scrolldifference * clientguilist.BarScroll)) / (filteredcharacterlist.Count() - -scrolldifference)) + (0.25f / filteredcharacterlist.Count()))), 0f, 1f);
                        }
                    }
                    //Same item count
                    else
                    {
                        newscroll = IngameInfoScroll;
                    }

                    IngameInfoScroll = newscroll;
                }
                clientguilist.children = new List<GUIComponent>();

                for (int i = 0; i < filteredcharacterlist.Count(); i++)
                {
                    GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 160, 150), Color.Transparent, "ListBoxElement", clientguilist);
                    frame.UserData = filteredcharacterlist[i];
                    frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                    frame.Color = new Color(0, 0, 0, 0);
                    frame.SelectedColor = new Color(0, 0, 0, 50);
                    //frame.CanBeFocused = false;

                    if (!filteredcharacterlist[i].Removed)
                    {
                        int TextHeight = -10;

                        //Clients name (Not their characters name)
                        if (filteredcharacterlist[i].client != null || filteredcharacterlist[i].IsHostCharacter)
                        {
                            GUITextBlock textBlockclientname = new GUITextBlock(
                                new Rectangle(22, TextHeight, 100, 15),
                                "",
                                null, null,
                                Alignment.TopLeft, Alignment.TopLeft,
                                "", frame);
                            textBlockclientname.TextScale = 0.8f;
                            //textBlockclientname.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                            TextHeight += 10;

                            if (filteredcharacterlist[i].client != null)
                            {
                                textBlockclientname.Text = ToolBox.LimitString("CL: " + filteredcharacterlist[i].client.Name, GUI.Font, frame.Rect.Width - 10);
                            }
                            else
                            {
                                textBlockclientname.Text = "Host Character";
                            }
                        }

                        GUITextBlock textBlockcharactername = new GUITextBlock(
                                new Rectangle(22, TextHeight, 100, 15),
                                "",
                                null, null,
                                Alignment.TopLeft, Alignment.TopLeft,
                                "", frame);
                        textBlockcharactername.TextScale = 0.8f;
                        //textBlockcharactername.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                        TextHeight += 10;

                        if (filteredcharacterlist[i].character != null && !filteredcharacterlist[i].character.IsDead)
                        {
                            textBlockcharactername.Text = ToolBox.LimitString("Chr: " + filteredcharacterlist[i].character.Name, GUI.Font, frame.Rect.Width - 10);
                        }
                        else if (filteredcharacterlist[i].client != null && (filteredcharacterlist[i].client.NeedsMidRoundSync || !filteredcharacterlist[i].client.InGame))
                        {
                            textBlockcharactername.Text = "Chr: Lobby";
                        }
                        else if (filteredcharacterlist[i].client != null && filteredcharacterlist[i].client.InGame && filteredcharacterlist[i].client.SpectateOnly)
                        {
                            textBlockcharactername.Text = "Chr: Spectator";
                        }
                        else if (filteredcharacterlist[i].character != null && !filteredcharacterlist[i].character.IsDead)
                        {
                            textBlockcharactername.Text = "Chr: Corpse";
                        }
                        else if (filteredcharacterlist[i].client != null && filteredcharacterlist[i].client.InGame)
                        {
                            textBlockcharactername.Text = "Chr: Ghost";
                        }

                        if (filteredcharacterlist[i].character != null && filteredcharacterlist[i].character.SpeciesName.ToLowerInvariant() == "human")
                        {
                            GUITextBlock textBlockjob = new GUITextBlock(
                                new Rectangle(22, TextHeight, 100, 20),
                                "",
                                null, null,
                                Alignment.TopLeft, Alignment.TopLeft,
                                "", frame);
                            textBlockjob.TextScale = 0.8f;
                            //textBlockjob.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                            TextHeight += 10;

                            if (filteredcharacterlist[i].character.Info != null)
                            {
                                textBlockjob.Text = ToolBox.LimitString("JB: " + filteredcharacterlist[i].character.Info.Job.Name, GUI.Font, frame.Rect.Width - 10);
                            }
                        }


                        GUITextBlock textBlockteam = new GUITextBlock(
                                new Rectangle(22, TextHeight, 100, 20),
                                "",
                                null, null,
                                Alignment.TopLeft, Alignment.TopLeft,
                                "", frame);
                        textBlockteam.TextScale = 0.8f;
                        //textBlockteam.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                        TextHeight += 10;

                        if (filteredcharacterlist[i].character != null)
                        {
                            if (filteredcharacterlist[i].character.TeamID == 0)
                            {
                                if (Character.Controlled == filteredcharacterlist[i].character)
                                {
                                    textBlockteam.Text = "T: Host Controlled";
                                }
                                else if (filteredcharacterlist[i].client != null)
                                {
                                    textBlockteam.Text = "T: Neutral";
                                }
                                else if (filteredcharacterlist[i].character.AIController is HumanAIController && filteredcharacterlist[i].client == null && Character.Controlled != filteredcharacterlist[i].character)
                                {
                                    textBlockteam.Text = "T: AI Human";
                                }
                                else
                                {
                                    textBlockteam.Text = "T: Fish";
                                }
                            }
                            else if (filteredcharacterlist[i].character.TeamID == 1)
                            {
                                textBlockteam.Text = "T: Coalition";
                            }
                            else if (filteredcharacterlist[i].character.TeamID == 2)
                            {
                                textBlockteam.Text = "T: Renegades";
                            }
                        }
                        else if (filteredcharacterlist[i].client.TeamID == 0)
                        {
                            textBlockteam.Text = "T: Neutral";
                        }
                        else if (filteredcharacterlist[i].client.TeamID == 1)
                        {
                            textBlockteam.Text = "T: Coalition";
                        }
                        else if (filteredcharacterlist[i].client.TeamID == 2)
                        {
                            textBlockteam.Text = "T: Renegades";
                        }








                        /*

                        //Client job and team if applicable.
                        //If they have a character with a valid info use this first
                        if (filteredcharacterlist[i].character != null && filteredcharacterlist[i].character.Info != null && filteredcharacterlist[i].character.Info.Job != null)
                        {
                            textBlockjob.Text = ToolBox.LimitString(filteredcharacterlist[i].character.Info.Job.Name, GUI.Font, frame.Rect.Width - 20);

                            switch (filteredcharacterlist[i].character.TeamID)
                            {
                                case 0:
                                    textBlockjob.Text = ToolBox.LimitString(textBlockjob.Text + " (Creature)", GUI.Font, frame.Rect.Width - 20);
                                    break;
                                case 1:
                                    textBlockjob.Text = ToolBox.LimitString(textBlockjob.Text + " (Coalition)", GUI.Font, frame.Rect.Width - 20);
                                    break;
                                case 2:
                                    textBlockjob.Text = ToolBox.LimitString(textBlockjob.Text + " (Renegade)", GUI.Font, frame.Rect.Width - 20);
                                    break;
                                default:
                                    textBlockjob.Text = ToolBox.LimitString(textBlockjob.Text + " (Team NA)", GUI.Font, frame.Rect.Width - 20);
                                    break;
                            }
                        }
                        //If they do not have a character with valid info, use the clients info if it exists
                        else if (filteredcharacterlist[i].client != null && filteredcharacterlist[i].client.CharacterInfo != null)
                        {
                            if (filteredcharacterlist[i].client.NeedsMidRoundSync || !filteredcharacterlist[i].client.InGame)
                            {
                                textBlockjob.Text = ToolBox.LimitString("Not In Game", GUI.Font, frame.Rect.Width - 20);
                            }
                            else if (filteredcharacterlist[i].character != null && filteredcharacterlist[i].character.SpeciesName.ToLowerInvariant() == "human" && filteredcharacterlist[i].character.Info == null)
                            {
                                textBlockjob.Text = ToolBox.LimitString("Unemployed", GUI.Font, frame.Rect.Width - 20);
                            }
                            else if(filteredcharacterlist[i].character != null && filteredcharacterlist[i].character.SpeciesName.ToLowerInvariant() == "human" && filteredcharacterlist[i].character.Info == null)
                            {

                            }
                            else
                            {
                                textBlockjob.Text = ToolBox.LimitString("Fish", GUI.Font, frame.Rect.Width - 20);
                            }

                            switch (filteredcharacterlist[i].client.CharacterInfo.TeamID)
                            {
                                case 0:
                                    textBlockjob.Text = ToolBox.LimitString(textBlockjob.Text + " (Creature)", GUI.Font, frame.Rect.Width - 20);
                                    break;
                                case 1:
                                    textBlockjob.Text = ToolBox.LimitString(textBlockjob.Text + " (Coalition)", GUI.Font, frame.Rect.Width - 20);
                                    break;
                                case 2:
                                    textBlockjob.Text = ToolBox.LimitString(textBlockjob.Text + " (Renegade)", GUI.Font, frame.Rect.Width - 20);
                                    break;
                                default:
                                    textBlockjob.Text = ToolBox.LimitString(textBlockjob.Text + " (Team NA)", GUI.Font, frame.Rect.Width - 20);
                                    break;
                            }
                        }
                        //If they don't even have a character info classify them as a fish ><>
                        else if(filteredcharacterlist[i].character != null)
                        {
                            textBlockjob.Text = ToolBox.LimitString("Fish (Creature)", GUI.Font, frame.Rect.Width - 20);
                        }

                        */

                        GUIImageButton GUIImageCharsprite = null;

                        if (filteredcharacterlist[i].character != null && filteredcharacterlist[i].character.AnimController != null && filteredcharacterlist[i].character.AnimController.Limbs != null)
                        {
                            Limb CharspriteLimb = filteredcharacterlist[i].character.AnimController.Limbs.ToList().Find(l => l.type == LimbType.Head);
                            if (CharspriteLimb != null)
                            {
                                //Sprite Charsprite = new Sprite(CharspriteLimb.sprite.Texture,new Rectangle(0,0,25,25),new Vector2(0,0),0f);
                                //Charsprite.size = new Vector2(25, 25);
                                //Charsprite.size = CharspriteLimb.sprite.size;
                                GUIImageCharsprite = new GUIImageButton(new Rectangle(0, 0, 25, 80), CharspriteLimb.sprite, Alignment.Left, frame);
                                float rescalesize = (CharspriteLimb.sprite.size.X * 1.5f);
                                if (rescalesize < (CharspriteLimb.sprite.size.Y / 1.5f)) rescalesize = (CharspriteLimb.sprite.size.Y / 1.3f);
                                float newscale = 1f;

                                //Colour Definition
                                GUIImageCharsprite.Color = new Color(255, 255, 255, 255);
                                GUIImageCharsprite.HoverColor = new Color(200, 200, 25, 255);
                                GUIImageCharsprite.SelectedColor = new Color(100, 100, 100, 255);

                                GUIImageCharsprite.UserData = frame.UserData;
                                GUIImageCharsprite.OnClicked += (btn, userData) =>
                                {
                                    InGameInfoCharacter thischar = (InGameInfoCharacter)frame.UserData;

                                //Only spy if not already controlling
                                if (Character.Controlled != thischar.character)
                                    {
                                        Character.Spied = thischar.character;
                                        GameMain.GameScreen.Cam.Zoom = 0.8f;
                                        GameMain.GameScreen.Cam.Position = Character.Spied.WorldPosition;
                                        GameMain.GameScreen.Cam.UpdateTransform(true);
                                    }
                                    return true;
                                };

                                GUIImageCharsprite.OnDoubleClicked += (btn, userData) =>
                                {
                                    InGameInfoCharacter thischar = (InGameInfoCharacter)frame.UserData;

                                //Do not take control of client characters or remote players
                                if (thischar.client == null && !thischar.character.IsRemotePlayer)
                                    {
                                    //Remove the spy effect if setting control
                                    Character.Spied = null;
                                        Character.Controlled = thischar.character;
                                    //GameMain.GameScreen.Cam.Zoom = 0.8f;
                                    GameMain.GameScreen.Cam.Position = Character.Controlled.WorldPosition;
                                        GameMain.GameScreen.Cam.UpdateTransform(true);
                                    }
                                    return true;
                                };

                                while (rescalesize > 125f)
                                {
                                    newscale = newscale / 2f;
                                    //rescalesize -= 50f;
                                    rescalesize = rescalesize / 2f;
                                }

                                while (rescalesize > 60f)
                                {
                                    newscale = newscale / 1.25f;
                                    //rescalesize -= 50f;
                                    rescalesize = rescalesize / 1.25f;
                                }

                                GUIImageCharsprite.Scale = newscale;
                                GUIImageCharsprite.Rotation = 0f;
                            }
                            else
                            {
                                //TODO - add code for No creature image found (HEADLESS creatures? DEFINATELY DESERVES SOMETHING THERE like decapitation or question mark)
                                GUIImageCharsprite = null;
                            }

                            StatusWidget playerstatus = new StatusWidget(new Rectangle(25, 40, 100, 46), Alignment.Left, filteredcharacterlist[i].character, frame);
                        }
                    }
                    else
                    {
                        GUITextBlock removallabel = new GUITextBlock(new Rectangle(25, 40, 100, 46), "Character no longer exists.", null, Alignment.Left, Alignment.Center, frame, false);
                        removallabel.TextColor = Color.Black;
                        removallabel.TextScale = 0.75f;
                        removallabel.Visible = true;
                        removallabel.Color = new Color(150, 90, 5, 10);
                        removallabel.HoverColor = new Color(150, 90, 5, 10);
                        removallabel.OutlineColor = new Color(150, 90, 5, 10);
                        removallabel.SelectedColor = new Color(150, 90, 5, 10);
                        //GUI.DrawRectangle(spriteBatch, new Vector2(frame.Rect.X, frame.Rect.Y), new Vector2(frame.Rect.Width, frame.Rect.Height), new Color(150, 90, 5, 10), true, 0f, 1);
                        removallabel.Rect = new Rectangle(frame.Rect.X, frame.Rect.Y, frame.Rect.Width, frame.Rect.Height);

                        removallabel.TextScale = 1.4f;
                    }
                }

                LastCharacterCount = filteredcharacterlist.Count;
                clientguilist.BarScroll = IngameInfoScroll;
            }
        }

        public void ChangeFilter(int filterincrement)
        {
            currentfilter = currentfilter + filterincrement;
            //Page Cycling
            if (currentfilter < 0) currentfilter = 5;
            if (currentfilter > 5) currentfilter = 0;

            filteredcharacterlist = new List<InGameInfoCharacter>();
            //Server Filters
            if (GameMain.Server != null)
            {
                switch (currentfilter)
                {
                    case 0:     //0 - All Clients
                        ingameInfoFilterText.Text = "Filter: All Clients";
                        //Include the hosts original spawns and respawns, but only if their actually alive or controlled.
                        filteredcharacterlist = characterlist.FindAll(cl => (!cl.Removed && (cl.client != null || (cl.character != null && (cl.IsHostCharacter && !cl.character.IsDead))) | (cl.character != null && cl.character == Character.Controlled)));
                        break;
                    case 1:     //1 - Coalition Clients
                        ingameInfoFilterText.Text = "Filter: Coalition Clients";
                        filteredcharacterlist = characterlist.FindAll(cl => (!cl.Removed && (cl.client != null || cl.IsHostCharacter) | (cl.character != null && cl.character == Character.Controlled)) && cl.character != null && cl.character.TeamID == 1);
                        if (filteredcharacterlist.Count == 0) ChangeFilter(filterincrement);
                        break;
                    case 2:     //2 - Renegade Clients
                        ingameInfoFilterText.Text = "Filter: Renegade Clients";
                        filteredcharacterlist = characterlist.FindAll(cl => (!cl.Removed && (cl.client != null || cl.IsHostCharacter) | (cl.character != null && cl.character == Character.Controlled)) && cl.character != null && cl.character.TeamID == 2);
                        if (filteredcharacterlist.Count == 0) ChangeFilter(filterincrement);
                        break;
                    case 3:     //3 - Creature Clients
                        ingameInfoFilterText.Text = "Filter: Creature Clients";
                        filteredcharacterlist = characterlist.FindAll(cl => (!cl.Removed && (cl.client != null || cl.IsHostCharacter) | (cl.character != null && cl.character == Character.Controlled)) && cl.character != null && cl.character.TeamID == 0);
                        if (filteredcharacterlist.Count == 0) ChangeFilter(filterincrement);
                        break;
                    case 4:     //4 - Creature AI
                        ingameInfoFilterText.Text = "Filter: Creature AI";
                        filteredcharacterlist = characterlist.FindAll(cl => (!cl.Removed && cl.client == null || !cl.IsHostCharacter) && (cl.character != null && cl.character != Character.Controlled) && (cl.character != null && cl.character.TeamID == 0));
                        if (filteredcharacterlist.Count == 0) ChangeFilter(filterincrement);
                        break;
                    case 5:     //4 - Human AI
                        ingameInfoFilterText.Text = "Filter: Human AI";
                        filteredcharacterlist = characterlist.FindAll(cl => (!cl.Removed && cl.client == null || !cl.IsHostCharacter) && cl.character != Character.Controlled && (cl.character != null && cl.character.AIController is HumanAIController));
                        if (filteredcharacterlist.Count == 0) ChangeFilter(filterincrement);
                        break;
                    case 6:
                        ingameInfoFilterText.Text = "Filter: Player Corpses";
                        filteredcharacterlist = characterlist.FindAll(cl => !cl.Removed && cl.character != null && (cl.character.IsRemotePlayer || cl.IsHostCharacter) && cl.character.IsDead);
                        if (filteredcharacterlist.Count == 0) ChangeFilter(filterincrement);
                        break;
                    case 7:
                        ingameInfoFilterText.Text = "Filter: AI Corpses";
                        filteredcharacterlist = characterlist.FindAll(cl => cl.character != null && !cl.character.IsRemotePlayer && cl.character.IsDead);
                        if (filteredcharacterlist.Count == 0) ChangeFilter(filterincrement);
                        break;
                    default:
                        ingameInfoFilterText.Text = "Filter: ERROR.";
                        break;
                }
            }
            //Client Filters
            else if (GameMain.Client != null)
            {
                switch (currentfilter)
                {
                    case 0:     //0 - All Clients
                        ingameInfoFilterText.Text = "Filter: Humans";
                        //Include the hosts original spawns and respawns, but only if their actually alive or controlled.
                        filteredcharacterlist = characterlist.FindAll(cl => !cl.Removed && ( cl.character.SpeciesName.ToLowerInvariant() == "human"));
                        break;
                    default:
                        ChangeFilter(filterincrement);
                        break;
                }

            }
            //Single Player Filters
            else
            {
                switch (currentfilter)
                {
                    case 0:     //0 - All Clients
                        ingameInfoFilterText.Text = "Filter: Humans";
                        //Include the hosts original spawns and respawns, but only if their actually alive or controlled.
                        filteredcharacterlist = characterlist.FindAll(cl => !cl.Removed && (cl.character.SpeciesName.ToLowerInvariant() == "human"));
                        break;

                    default:
                        ChangeFilter(filterincrement);
                        break;
                }
            }
        }

        public void RunCommand(string Command, string[] Arguments)
        {

        }
    }
}
