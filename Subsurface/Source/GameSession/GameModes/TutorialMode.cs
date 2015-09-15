using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{
    class TutorialMode : GameMode
    {
        public readonly CrewManager CrewManager;
                
        private GUIComponent infoBox;

        public static void Start()
        {
            Submarine.Load("Content/Map/TutorialSub.gz");

            Game1.GameSession = new GameSession(Submarine.Loaded, "", GameModePreset.list.Find(gm => gm.Name.ToLower()=="tutorial"));

            Game1.GameSession.StartShift(TimeSpan.Zero, "tutorial");

            Game1.GameSession.taskManager.Tasks.Clear();

            Game1.GameScreen.Select();
        }

        public TutorialMode(GameModePreset preset)
            : base(preset)
        {
            CrewManager = new CrewManager();
        }

        public override void Start(TimeSpan duration)
        {
            base.Start(duration);

            WayPoint wayPoint = WayPoint.GetRandom(SpawnType.Cargo, null);
            if (wayPoint==null)
            {
                DebugConsole.ThrowError("A waypoint with the spawntype ''cargo'' is required for the tutorial event");
                return;
            }

            CharacterInfo charInfo = new CharacterInfo(Character.HumanConfigFile, "", Gender.None, JobPrefab.List.Find(jp => jp.Name=="Engineer"));

            Character character = new Character(charInfo, wayPoint.SimPosition);
            Character.Controlled = character;
            character.GiveJobItems(null);
            
            foreach (Item item in character.Inventory.items)
            {
                if (item == null || item.Name != "ID Card") continue;

                item.AddTag("com");
                item.AddTag("eng");

                break;
            }

            CrewManager.AddCharacter(character);

            CoroutineManager.StartCoroutine(UpdateState());
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (Character.Controlled!=null && Character.Controlled.IsDead)
            {
                Character.Controlled = null;

                    CoroutineManager.StopCoroutine("TutorialMode.UpdateState");
                    infoBox = null;
                    CoroutineManager.StartCoroutine(Dead());
            }

            CrewManager.Update(deltaTime);

            if (infoBox!=null) infoBox.Update(deltaTime);
        }

        private IEnumerable<object> UpdateState()
        {

            yield return new WaitForSeconds(4.0f);

            infoBox = CreateInfoFrame("Use WASD to move and mouse to look around");

            yield return new WaitForSeconds(5.0f);

            //-----------------------------------

            infoBox = CreateInfoFrame("Open the door at your right side by highlighting the button next to it with your cursor and pressing E");

            Door tutorialDoor = Item.itemList.Find(i => i.HasTag("tutorialdoor")).GetComponent<Door>();
            
            while (!tutorialDoor.IsOpen)
            {
                yield return CoroutineStatus.Running;
            }

            yield return new WaitForSeconds(2.0f);

            //-----------------------------------

            infoBox = CreateInfoFrame("Now it's time to power up the submarine. Go to the upper left corner of the submarine, where you'll find a nuclear reactor.");

            Reactor reactor = Item.itemList.Find(i => i.HasTag("tutorialreactor")).GetComponent<Reactor>();

            while (Vector2.Distance(Character.Controlled.Position, reactor.Item.Position)>200.0f)
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("Select the reactor by walking next to it and pressing E.");

            while (Character.Controlled.SelectedConstruction != reactor.Item)
            {
                yield return CoroutineStatus.Running;
            }            
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("This is the control panel of the reactor. Try turning it on by increasing the fission rate.");

            while (reactor.FissionRate <= 0.0f)
            {
                yield return CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("The reactor core has started generating heat, which in turn generates power for the submarine. It won't generate much power at the moment, "
            +" because the shutdown temperature is set to 500. When the temperature of the reactor raises higher than the shutdown temperature, the reactor will automatically start to cool itself down."
            + " You should increase it to somewhere around 5000.");
            
            while (Math.Abs(reactor.ShutDownTemp-5000.0f) > 400.0f)
            {
                yield return CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("The amount of power generated by the reactor should be kept close to the amount of power consumed by the devices in the submarine. "
                +"If there's not enough power, devices won't function properly, and if there's too much power, some devices may be damaged. Turn on ''Automatic temperature control'' to "
                +"make the reactor automatically adjust the temperature to a suitable level.");

            while (!reactor.AutoTemp)
            {
                yield return CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("That's the basics you need to know to power up the reactor! Now that there's power available for the engines, let's try steering the sub. "
                +"Deselect the reactor by pressing E and head to the command room at the left edge of the vessel.");

            Steering steering = Item.itemList.Find(i => i.HasTag("tutorialsteering")).GetComponent<Steering>();
            Radar radar = steering.Item.GetComponent<Radar>();

            while (Vector2.Distance(Character.Controlled.Position, steering.Item.Position) > 150.0f)
            {
                yield return CoroutineStatus.Running;
            }
            
            infoBox = CreateInfoFrame("Select the navigation terminal by walking next to it and pressing E.");
            
            while (Character.Controlled.SelectedConstruction != steering.Item)
            {
                yield return CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("There seems to be something wrong with the navigation terminal."+
                " There's nothing on the monitor, so it's probably out of power. The reactor must still be"
                +" running or the lights would've gone out, so it's most likely a problem with the wiring."
                +" Deselect the terminal by pressing E to start checking the wiring.");

            while (Character.Controlled.SelectedConstruction == steering.Item)
            {
                yield return CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(1.0f);

            infoBox = CreateInfoFrame("You need a screwdriver to check the wiring of the terminal."
            + " Equip a screwdriver by pulling it to either of the slots with a hand symbol, and then select the terminal again by pressing E.");

            while (Character.Controlled.SelectedConstruction != steering.Item ||
                Character.Controlled.SelectedItems.FirstOrDefault(i => i != null && i.Name == "Screwdriver") == null)
            {
                yield return CoroutineStatus.Running;
            }


            infoBox = CreateInfoFrame("Here you can see all the wires connected to the terminal. Apparently there's no wire"
                + " going into the to the power connection - that's why the monitor isn't working."
                + " You should find a piece of wire to connect it. Try searching some of the cabinets scattered around the sub.");

            while (!HasItem("Wire"))
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("Head back to the navigation terminal to fix the wiring.");
            
            PowerTransfer junctionBox = Item.itemList.Find(i => i!=null && i.HasTag("tutorialjunctionbox")).GetComponent<PowerTransfer>();

            while ((Character.Controlled.SelectedConstruction != junctionBox.Item &&
                Character.Controlled.SelectedConstruction != steering.Item) ||
            Character.Controlled.SelectedItems.FirstOrDefault(i => i != null && i.Name == "Screwdriver") == null)
            {
                yield return CoroutineStatus.Running;
            }
            
            if (Character.Controlled.SelectedItems.FirstOrDefault(i => i != null && i.GetComponent<Wire>()!=null) == null)
            {
                infoBox = CreateInfoFrame("Equip the wire by dragging it to one of the slots with a hand symbol.");

                while (Character.Controlled.SelectedItems.FirstOrDefault(i => i != null && i.GetComponent<Wire>() != null) == null)
                {
                    yield return CoroutineStatus.Running;
                }
            }

            infoBox = CreateInfoFrame("You can see the equipped wire at the middle of the connection panel. Drag it to the power connector.");

            var steeringConnection = steering.Item.Connections.Find(c => c.Name.Contains("power"));
            var junctionConnection = junctionBox.Item.Connections.Find(c => c.Name.Contains("power"));

            while (steeringConnection.Wires.FirstOrDefault(w => w != null) == null)
            {
                yield return CoroutineStatus.Running;
                
            }
            
            infoBox = CreateInfoFrame("Now you have to connect the other end of the wire to a power source. "
                + "The junction box in the room just below the command room should do.");

            while (Character.Controlled.SelectedConstruction!=null)
            {
                yield return CoroutineStatus.Running;
            }

            yield return new WaitForSeconds(2.0f);

            infoBox = CreateInfoFrame("You can now move the other end of the wire around, and attach it on the wall by left clicking or "
                + "remove the previous attachment by right clicking. Or you can just run the wire straight to the junction box and attach it "
                + " the same way you did to the navigation terminal.");  

            while (radar.Voltage<0.1f)
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("Great! Now we should be able to get moving.");


            while (Character.Controlled.SelectedConstruction != steering.Item)
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("You can take a look at the area around the sub by pressing ''Activate Radar''.");
            
            while (!radar.IsActive)
            {
                yield return CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("The white box in the middle is the submarine, and the white lines outside it are the walls of an underwater cavern. "
                + "Try moving the submarine by clicking somewhere inside the rectangle and draggind the pointer to the direction you want to go to.");

            while (steering.CurrTargetVelocity == Vector2.Zero && steering.CurrTargetVelocity.Length() < 50.0f)
            {
                yield return CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(4.0f);

            infoBox = CreateInfoFrame("The submarine moves up and down by pumping water in and out of the two ballast tanks at the bottom of the submarine. "
                +"The engine at the back of the sub moves it forwards and backwards.");

            yield return new WaitForSeconds(8.0f);

            infoBox = CreateInfoFrame("Steer the submarine downwards, heading further into the cavern.");

            while (Submarine.Loaded.Position.Y > 31000.0f)
            {
                yield return CoroutineStatus.Running;
            }
            
            var moloch = new Character("Content/Characters/Moloch/moloch.xml", steering.Item.SimPosition + Vector2.UnitX * 25.0f);
            moloch.PlaySound(AIController.AiState.Attack);

            yield return new WaitForSeconds(1.0f);

            infoBox = CreateInfoFrame("Uh-oh... Something enormous just appeared on the radar.");

            List<Structure> windows = new List<Structure>();
            foreach (Structure s in Structure.wallList)
            {
                if (s.CastShadow || !s.HasBody) continue;

                if (s.Rect.Right > steering.Item.Position.X) windows.Add(s);
            }

            bool broken = false;
            do
            {
                Submarine.Loaded.Speed = Vector2.Zero;

                moloch.AIController.SelectTarget(steering.Item.CurrentHull.AiTarget);
                Vector2 steeringDir = windows[0].Position - moloch.Position;
                if (steeringDir != Vector2.Zero) steeringDir = Vector2.Normalize(steeringDir);

                foreach (Limb limb in moloch.AnimController.limbs)
                {
                    limb.body.LinearVelocity = new Vector2(limb.LinearVelocity.X, limb.LinearVelocity.Y + steeringDir.Y*10.0f);
                }

                moloch.AIController.Steering = steeringDir;

                foreach (Structure window in windows)
                {
                    for (int i = 0; i < window.SectionCount; i++)
                    {
                        if (!window.SectionIsLeaking(i)) continue;
                        broken = true;
                        break;
                    }
                    if (broken) break;
                }


                yield return new WaitForSeconds(1.0f);
            } while (!broken);

            yield return new WaitForSeconds(1.0f);
            

            var capacitor1 = Item.itemList.Find(i => i.HasTag("capacitor1")).GetComponent<PowerContainer>();
            var capacitor2 = Item.itemList.Find(i => i.HasTag("capacitor1")).GetComponent<PowerContainer>();
            CoroutineManager.StartCoroutine(KeepEnemyAway(moloch, new PowerContainer[] { capacitor1, capacitor2 }));


            infoBox = CreateInfoFrame("The hull has been breached! Close all the doors to the command room to stop the water from flooding the entire sub!");


            Door commandDoor1 = Item.itemList.Find(i => i.HasTag("commanddoor1")).GetComponent<Door>();
            Door commandDoor2 = Item.itemList.Find(i => i.HasTag("commanddoor2")).GetComponent<Door>();
            Door commandDoor3 = Item.itemList.Find(i => i.HasTag("commanddoor3")).GetComponent<Door>();

            while (commandDoor1.IsOpen || (commandDoor2.IsOpen || commandDoor3.IsOpen))
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("You should find yourself an diving mask or a diving suit, in case the creature causes more damage. "+
                "There are some in the room next to the airlock.");

            while (!HasItem("Diving Mask") && !HasItem("Diving Suit"))
            {
                yield return CoroutineStatus.Running; 
            }

            if (HasItem("Diving Mask"))
            {
                infoBox = CreateInfoFrame("The diving mask will let you breathe underwater, but it won't protect from the water pressure outside the sub. "+
                    "It should be fine for the situation at hand, but you still need to find an oxygen tank and drag it into the same slot as the mask." +
                    "You should grab one or two from one of the cabinets.");
            }
            else if (HasItem("Diving Suit"))
            {
                infoBox = CreateInfoFrame("In addition to letting you breathe underwater, the suit will protect you from the water pressure outside the sub " +
                    "(unlike the diving mask). However, you still need to drag an oxygen tank into the same slot as the suit to supply oxygen. "+
                    "You should grab one or two from one of the cabinets.");
            }

            while (!HasItem("Oxygen Tank"))
            {
                yield return CoroutineStatus.Running;
            }

            yield return new WaitForSeconds(5.0f);

            infoBox = CreateInfoFrame("Now it's time to stop the creature attacking the submarine. Head to the railgun room at the upper right corner of the sub.");

            var railGun = Item.itemList.Find(i => i.GetComponent<Turret>()!=null);

            while (Vector2.Distance(Character.Controlled.Position, railGun.Position)>500)
            {
                yield return new WaitForSeconds(1.0f);
            }

            infoBox = CreateInfoFrame("The railgun requires a large power surge to fire. The reactor can't provide a surge large enough, so we need to use the "
                +" supercapacitors in the railgun room. The capacitors need to be charged first; select them and crank up the recharge rate.");

            while (capacitor1.RechargeSpeed<0.5f && capacitor2.RechargeSpeed<0.5f)
            {
                yield return new WaitForSeconds(1.0f);
            }

            infoBox = CreateInfoFrame("The capacitors consume large amounts of power when they're being charged at a high rate, so "+
                "be careful not to overload the electrical grid or the reactor. They also take some time to recharge, so now is a good "+
                "time to head to the room below and load some shells for the railgun.");


            var loader = Item.itemList.Find(i => i.Name == "Railgun Loader").GetComponent<ItemContainer>();

            while (Math.Abs(Character.Controlled.Position.Y - loader.Item.Position.Y)>80)
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("Grab one of the shells. You can load it by selecting the railgun loader and dragging the shell to. "
                +"one of the free slots. You need two hands to carry a shell, so make sure you don't have anything else in either hand.");

            while (loader.Item.ContainedItems.FirstOrDefault(i => i != null && i.Name == "Railgun Shell") == null)
            {
                moloch.Health = 50.0f;

                capacitor1.Charge += 5.0f;
                capacitor2.Charge += 5.0f;
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("Now we're ready to shoot! Select the railgun controller.");

            while (Character.Controlled.SelectedConstruction == null || Character.Controlled.SelectedConstruction.Name != "Railgun Controller")
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("Use the right mouse button to aim and wait for the creature to come closer. When you're ready to shoot, "
                +"press the left mouse button.");

            while (!moloch.IsDead)
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("The creature has died. Now you should fix the damages in the control room: "+
                "Grab a welding tool from the closet in the railgun room.");

            while (!HasItem("Welding Tool"))
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("The welding tool requires fuel to work. Grab a welding fuel tank and attach it to the tool "+
                "by dragging it into the same slot.");

            do
            {
                var weldingTool = Character.Controlled.Inventory.items.FirstOrDefault(i => i != null && i.Name == "Welding Tool");
                if (weldingTool != null && 
                    weldingTool.ContainedItems.FirstOrDefault(contained => contained != null && contained.Name == "Welding Fuel Tank") != null) break;

                yield return CoroutineStatus.Running;
            } while (true);


            infoBox = CreateInfoFrame("You can aim with the tool using the right mouse button and weld using the left button. "+
                "Head to the command room to fix the leaks there.");

            do
            {
                broken = false;
                foreach (Structure window in windows)
                {
                    for (int i = 0; i < window.SectionCount; i++)
                    {
                        if (!window.SectionIsLeaking(i)) continue;
                        broken = true;
                        break;
                    }
                    if (broken) break;
                }

                yield return new WaitForSeconds(1.0f);
            } while (broken);

            infoBox = CreateInfoFrame("Great! However, there's still quite a bit of water inside the sub. It should be pumped out "
                +"using the pump in the room at the bottom of the submarine.");

            Pump pump = Item.itemList.Find(i => i.HasTag("tutorialpump")).GetComponent<Pump>();

            while (Vector2.Distance(Character.Controlled.Position, pump.Item.Position) > 100.0f)
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("The two pumps inside the ballast tanks "
                +"are connected straight to the navigation terminal and can't be manually controlled unless you mess with their wiring, "+
                "so you should only use the pump in the middle room to pump out the water. Select it, turn it on and adjust the pumping speed "+
                "to start pumping water out.");

            while (pump.Item.CurrentHull.Volume>1000.0f)
            {
                yield return CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("That was all there is to this tutorial! Now you should be able to handle "+
            "most of the basic tasks on board the submarine.");

            yield return new WaitForSeconds(4.0f);

            float endPreviewLength = 10.0f;

            DateTime endTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, (int)(1000.0f * endPreviewLength));
            float secondsLeft = endPreviewLength;

            Character.Controlled = null;
            Game1.GameScreen.Cam.TargetPos = Vector2.Zero;

            do
            {
                secondsLeft = (float)(endTime - DateTime.Now).TotalSeconds;

                float camAngle = (float)((DateTime.Now - endTime).TotalSeconds / endPreviewLength) * MathHelper.TwoPi;
                Vector2 offset = (new Vector2(
                    (float)Math.Cos(camAngle) * (Submarine.Borders.Width / 2.0f),
                    (float)Math.Sin(camAngle) * (Submarine.Borders.Height / 2.0f)));

                Game1.GameScreen.Cam.TargetPos = offset * 0.8f;
                //Game1.GameScreen.Cam.MoveCamera((float)deltaTime);

                yield return CoroutineStatus.Running;
            } while (secondsLeft > 0.0f);

            Submarine.Unload();
            Game1.MainMenuScreen.Select();

            yield return CoroutineStatus.Success;
        }

        private IEnumerable<object> Dead()
        {
            yield return new WaitForSeconds(3.0f);

            var messageBox = new GUIMessageBox("You have died", "Do you want to try again?", new string[] { "Yes", "No" });

            messageBox.Buttons[0].OnClicked += Restart;
            messageBox.Buttons[0].OnClicked += messageBox.Close;

            //messageBox.Buttons[1].UserData = MainMenuScreen.Tabs.Main;
            messageBox.Buttons[1].OnClicked = Game1.MainMenuScreen.SelectTab;
            messageBox.Buttons[1].OnClicked += messageBox.Close;


            yield return CoroutineStatus.Success;
        }

        private bool Restart(GUIButton button, object obj)
        {
            TutorialMode.Start();

            return true;
        }

        private bool HasItem(string itemName)
        {
            if (Character.Controlled == null) return false;
            return Character.Controlled.Inventory.items.FirstOrDefault(i => i != null && i.Name == itemName)!=null;
        }

        /// <summary>
        /// keeps the enemy away from the sub until the capacitors are loaded
        /// </summary>
        private IEnumerable<object> KeepEnemyAway(Character enemy, PowerContainer[] capacitors)
        {
            do
            {
                enemy.Health = 50.0f;

                enemy.AIController.State = AIController.AiState.None;

                Vector2 targetPos = Character.Controlled.Position + new Vector2(0.0f, 3000.0f);

                Vector2 steering = targetPos - enemy.Position;
                if (steering != Vector2.Zero) steering = Vector2.Normalize(steering);

                enemy.AIController.Steering = steering*2.0f;

                yield return CoroutineStatus.Running;
            } while (capacitors.FirstOrDefault(c => c.Charge > 0.4f) == null);

            yield return CoroutineStatus.Success;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);

            CrewManager.Draw(spriteBatch);
            if (infoBox != null) infoBox.Draw(spriteBatch);
        }

        private GUIComponent CreateInfoFrame(string text)
        {
            int width = 300;
            int height = 80;

            string wrappedText = ToolBox.WrapText(text, width, GUI.Font);

            height += wrappedText.Split('\n').Length*25;

            var infoBlock = new GUIFrame(new Rectangle(-20, 20, width, height), null, Alignment.TopRight, GUI.Style);
            //infoBlock.Color = infoBlock.Color * 0.8f;
            infoBlock.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            infoBlock.Flash(Color.Green);

            new GUITextBlock(new Rectangle(10, 10, width - 40, height), text, GUI.Style, infoBlock, true);


            GUI.PlayMessageSound();

            return infoBlock;
        }
    }
}
