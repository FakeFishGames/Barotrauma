using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class BasicTutorial : ScenarioTutorial
    {
        public BasicTutorial(XElement element)
            : base(element)
        {
        }

        public override IEnumerable<object> UpdateState()
        {
            Character Controlled = Character.Controlled;
            if (Controlled == null) yield return CoroutineStatus.Success;

            foreach (Item item in Item.ItemList)
            {
                var wire = item.GetComponent<Wire>();
                if (wire != null && wire.Connections.Any(c => c != null))
                {
                    wire.Locked = true;
                }
            }

            //remove all characters except the controlled one to prevent any unintended monster attacks
            var existingCharacters = Character.CharacterList.FindAll(c => c != Controlled);
            foreach (Character c in existingCharacters)
            {
                c.Remove();
            }
            
            yield return new WaitForSeconds(4.0f);

            infoBox = CreateInfoFrame("", "Use WASD to move and the mouse to look around");

            yield return new WaitForSeconds(5.0f);

            //-----------------------------------

            infoBox = CreateInfoFrame("", "Open the door at your right side by highlighting the button next to it with your cursor and pressing E");

            Door tutorialDoor = Item.ItemList.Find(i => i.HasTag("tutorialdoor")).GetComponent<Door>();

            while (!tutorialDoor.IsOpen && Controlled.WorldPosition.X < tutorialDoor.Item.WorldPosition.X)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            yield return new WaitForSeconds(2.0f);

            //-----------------------------------

            infoBox = CreateInfoFrame("", "Hold W or S to walk up or down stairs. Use shift to run.", hasButton: true);

            while (infoBox != null)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            //-----------------------------------

            infoBox = CreateInfoFrame("", "At the moment the submarine has no power, which means that crucial systems such as the oxygen generator or the engine aren't running. Let's fix this: go to the upper left corner of the submarine, where you'll find a nuclear reactor.");

            Reactor reactor = Item.ItemList.Find(i => i.HasTag("tutorialreactor")).GetComponent<Reactor>();
            //reactor.MeltDownTemp = 20000.0f;

            while (Vector2.Distance(Controlled.Position, reactor.Item.Position) > 200.0f)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "The reactor requires fuel rods to generate power. You can grab one from the steel cabinet by walking next to it and pressing E.");

            while (Controlled.SelectedConstruction == null || Controlled.SelectedConstruction.Prefab.Identifier != "steelcabinet")
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "Pick up one of the fuel rods either by double-clicking or dragging and dropping it into your inventory.");

            while (!HasItem("fuelrod"))
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "Select the reactor by walking next to it and pressing E.");

            while (Controlled.SelectedConstruction != reactor.Item)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("", "Load the fuel rod into the reactor by dropping it into any of the 5 slots.");

            while (reactor.AvailableFuel <= 0.0f)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "The reactor is now fueled up. Try turning it on by increasing the fission rate.");

            while (reactor.FissionRate <= 0.0f)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("", "The reactor core has started generating heat, which in turn generates power for the submarine. The power generation is very low at the moment,"
            + " because the reactor is set to shut itself down when the temperature rises above 500 degrees Celsius. You can adjust the temperature limit by changing the \"Shutdown Temperature\" in the control panel.", hasButton: true);

            //TODO: reimplement
            /*while (infoBox != null)
            {
                reactor.ShutDownTemp = Math.Min(reactor.ShutDownTemp, 5000.0f);
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("The amount of power generated by the reactor should be kept close to the amount of power consumed by the devices in the submarine. "
                + "If there isn't enough power, devices won't function properly (or at all), and if there's too much power, some devices may be damaged."
                + " Try to raise the temperature of the reactor close to 3000 degrees by adjusting the fission and cooling rates.", true);

            while (Math.Abs(reactor.Temperature - 3000.0f) > 100.0f)
            {
                reactor.AutoTemp = false;
                reactor.ShutDownTemp = Math.Min(reactor.ShutDownTemp, 5000.0f);
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("Looks like we're up and running! Now you should turn on the \"Automatic temperature control\", which will make the reactor "
                + "automatically adjust the temperature to a suitable level. Even though it's an easy way to keep the reactor up and running most of the time, "
                + "you should keep in mind that it changes the temperature very slowly and carefully, which may cause issues if there are sudden changes in grid load.");

            while (!reactor.AutoTemp)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }*/
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("", "That's the basics of operating the reactor! Now that there's power available for the engines, it's time to get the submarine moving. "
                + "Deselect the reactor by pressing E and head to the command room at the right edge of the vessel.");

            Steering steering = Item.ItemList.Find(i => i.HasTag("tutorialsteering")).GetComponent<Steering>();
            Sonar sonar = steering.Item.GetComponent<Sonar>();

            while (Vector2.Distance(Controlled.Position, steering.Item.Position) > 150.0f)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            CoroutineManager.StartCoroutine(KeepReactorRunning(reactor));

            infoBox = CreateInfoFrame("", "Select the navigation terminal by walking next to it and pressing E.");

            while (Controlled.SelectedConstruction != steering.Item)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("", "There seems to be something wrong with the navigation terminal." +
                " There's nothing on the monitor, so it's probably out of power. The reactor must still be"
                + " running or the lights would've gone out, so it's most likely a problem with the wiring."
                + " Deselect the terminal by pressing E to start checking the wiring.");

            while (Controlled.SelectedConstruction == steering.Item)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(1.0f);

            infoBox = CreateInfoFrame("", "You need a screwdriver to check the wiring of the terminal."
            + " Equip a screwdriver by pulling it to either of the slots with a hand symbol, and then use it on the terminal by left clicking.");

            while (Controlled.SelectedConstruction != steering.Item ||
                Controlled.SelectedItems.FirstOrDefault(i => i != null && i.Prefab.Identifier == "screwdriver") == null)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }


            infoBox = CreateInfoFrame("", "Here you can see all the wires connected to the terminal. Apparently there's no wire"
                + " going into the to the power connection - that's why the monitor isn't working."
                + " You should find a piece of wire to connect it. Try searching some of the cabinets scattered around the sub.");

            while (!HasItem("wire"))
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "Head back to the navigation terminal to fix the wiring.");

            PowerTransfer junctionBox = Item.ItemList.Find(i => i != null && i.HasTag("tutorialjunctionbox")).GetComponent<PowerTransfer>();

            while ((Controlled.SelectedConstruction != junctionBox.Item &&
                Controlled.SelectedConstruction != steering.Item) ||
            Controlled.SelectedItems.FirstOrDefault(i => i != null && i.Prefab.Identifier == "screwdriver") == null)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            if (Controlled.SelectedItems.FirstOrDefault(i => i != null && i.GetComponent<Wire>() != null) == null)
            {
                infoBox = CreateInfoFrame("", "Equip the wire by dragging it to one of the slots with a hand symbol.");

                while (Controlled.SelectedItems.FirstOrDefault(i => i != null && i.GetComponent<Wire>() != null) == null)
                {
                    yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
                }
            }

            infoBox = CreateInfoFrame("", "You can see the equipped wire at the middle of the connection panel. Drag it to the power connector.");

            var steeringConnection = steering.Item.Connections.Find(c => c.Name.Contains("power"));

            while (steeringConnection.Wires.FirstOrDefault(w => w != null) == null)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;

            }

            infoBox = CreateInfoFrame("", "Now you have to connect the other end of the wire to a power source. "
                + "The junction box in the room just below the command room should do.");

            while (Controlled.SelectedConstruction != null)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            yield return new WaitForSeconds(2.0f);

            infoBox = CreateInfoFrame("", "You can now move the other end of the wire around, and attach it on the wall by left clicking or "
                + "remove the previous attachment by right clicking. Or if you don't care for neatly laid out wiring, you can just "
                + "run it straight to the junction box.");

            while (Controlled.SelectedConstruction == null || Controlled.SelectedConstruction.GetComponent<PowerTransfer>() == null)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "Connect the wire to the junction box by pulling it to the power connection, the same way you did with the navigation terminal.");

            while (sonar.Voltage < 0.1f)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "Great! Now we should be able to get moving.");


            while (Controlled.SelectedConstruction != steering.Item)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "You can take a look at the area around the sub by selecting the \"Active Sonar\" checkbox.");

            while (!sonar.IsActive)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(0.5f);

            infoBox = CreateInfoFrame("", "The blue rectangle in the middle is the submarine, and the flickering shapes outside it are the walls of an underwater cavern. "
                + "Try moving the submarine by clicking somewhere on the monitor and dragging the pointer to the direction you want to go to.");

            while (steering.TargetVelocity == Vector2.Zero && steering.TargetVelocity.Length() < 50.0f)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(4.0f);

            infoBox = CreateInfoFrame("", "The submarine moves up and down by pumping water in and out of the two ballast tanks at the bottom of the submarine. "
                + "The engine at the back of the sub moves it forwards and backwards.", hasButton: true);

            while (infoBox != null)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "Steer the submarine downwards, heading further into the cavern.");

            while (Submarine.MainSub.WorldPosition.Y > 32000.0f)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }
            yield return new WaitForSeconds(1.0f);

            var moloch = Character.Create("moloch", steering.Item.WorldPosition + new Vector2(3000.0f, -500.0f), "");

            moloch.PlaySound(CharacterSound.SoundType.Attack);

            yield return new WaitForSeconds(1.0f);

            infoBox = CreateInfoFrame("", "Uh-oh... Something enormous just appeared on the sonar.");

            List<Structure> windows = new List<Structure>();
            foreach (Structure s in Structure.WallList)
            {
                if (s.CastShadow || !s.HasBody) continue;
                
                if (s.Rect.Right > steering.Item.CurrentHull.Rect.Right) windows.Add(s);
            }

            float slowdownTimer = 1.0f;
            bool broken = false;
            do
            {
                steering.TargetVelocity = Vector2.Zero;

                slowdownTimer = Math.Max(0.0f, slowdownTimer - CoroutineManager.DeltaTime * 0.3f);
                Submarine.MainSub.Velocity *= slowdownTimer;

                moloch.AIController.SelectTarget(steering.Item.CurrentHull.AiTarget);
                Vector2 steeringDir = windows[0].WorldPosition - moloch.WorldPosition;
                if (steeringDir != Vector2.Zero) steeringDir = Vector2.Normalize(steeringDir);

                moloch.AIController.SteeringManager.SteeringManual(CoroutineManager.DeltaTime, steeringDir * 100.0f);

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
                if (broken) break;

                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            } while (!broken);

            //fix everything except the command windows
            foreach (Structure w in Structure.WallList)
            {
                bool isWindow = windows.Contains(w);

                for (int i = 0; i < w.SectionCount; i++)
                {
                    if (!w.SectionIsLeaking(i)) continue;

                    if (isWindow)
                    {
                        //decrease window damage to slow down the leaking
                        w.AddDamage(i, -w.SectionDamage(i) * 0.48f);
                    }
                    else
                    {
                        w.AddDamage(i, -100000.0f);
                    }
                }
            }

            Submarine.MainSub.GodMode = true;

            var capacitor1 = Item.ItemList.Find(i => i.HasTag("capacitor1")).GetComponent<PowerContainer>();
            var capacitor2 = Item.ItemList.Find(i => i.HasTag("capacitor1")).GetComponent<PowerContainer>();
            CoroutineManager.StartCoroutine(KeepEnemyAway(moloch, new PowerContainer[] { capacitor1, capacitor2 }));

            infoBox = CreateInfoFrame("", "The hull has been breached! Close all the doors to the command room to stop the water from flooding the entire sub!");

            Door commandDoor1 = Item.ItemList.Find(i => i.HasTag("commanddoor1")).GetComponent<Door>();
            Door commandDoor2 = Item.ItemList.Find(i => i.HasTag("commanddoor2")).GetComponent<Door>();

            //wait until the player is out of the room and the doors are closed
            while (Controlled.WorldPosition.X > commandDoor1.Item.WorldPosition.X ||
                (commandDoor1.IsOpen || commandDoor2.IsOpen))
            {
                //prevent the hull from filling up completely and crushing the player
                steering.Item.CurrentHull.WaterVolume = Math.Min(steering.Item.CurrentHull.WaterVolume, steering.Item.CurrentHull.Volume * 0.9f);
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }


            infoBox = CreateInfoFrame("", "You should quickly find yourself a diving mask or a diving suit. " +
                "There are some in the room next to the airlock.");

            bool divingMaskSelected = false;

            while (!HasItem("divingmask") && !HasItem("divingsuit"))
            {
                if (!divingMaskSelected &&
                    Controlled.FocusedItem != null && Controlled.FocusedItem.Prefab.Identifier == "divingsuit")
                {
                    infoBox = CreateInfoFrame("", "There can only be one item in each inventory slot, so you need to take off "
                        + "the jumpsuit if you wish to wear a diving suit.");

                    divingMaskSelected = true;
                }

                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            if (HasItem("divingmask"))
            {
                infoBox = CreateInfoFrame("", "The diving mask will let you breathe underwater, but it won't protect from the water pressure outside the sub. " +
                    "It should be fine for the situation at hand, but you still need to find an oxygen tank and drag it into the same slot as the mask." +
                    "You should grab one or two from one of the cabinets.");
            }
            else if (HasItem("divingsuit"))
            {
                infoBox = CreateInfoFrame("", "In addition to letting you breathe underwater, the suit will protect you from the water pressure outside the sub " +
                    "(unlike the diving mask). However, you still need to drag an oxygen tank into the same slot as the suit to supply oxygen. " +
                    "You should grab one or two from one of the cabinets.");
            }

            while (!HasItem("oxygentank"))
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            yield return new WaitForSeconds(5.0f);

            infoBox = CreateInfoFrame("", "Now you should stop the creature attacking the submarine before it does any more damage. Head to the railgun room at the upper right corner of the sub.");

            var railGun = Item.ItemList.Find(i => i.GetComponent<Turret>() != null);

            while (Vector2.Distance(Controlled.Position, railGun.Position) > 500)
            {
                yield return new WaitForSeconds(1.0f);
            }

            infoBox = CreateInfoFrame("", "The railgun requires a large power surge to fire. The reactor can't provide a surge large enough, so we need to use the "
                + " supercapacitors in the railgun room. The capacitors need to be charged first; select them and crank up the recharge rate.");

            while (capacitor1.RechargeSpeed < 0.5f && capacitor2.RechargeSpeed < 0.5f)
            {
                yield return new WaitForSeconds(1.0f);
            }

            infoBox = CreateInfoFrame("", "The capacitors take some time to recharge, so now is a good " +
                "time to head to the room below and load some shells for the railgun.");


            var loader = Item.ItemList.Find(i => i.Prefab.Identifier == "railgunloader").GetComponent<ItemContainer>();

            while (Math.Abs(Controlled.Position.Y - loader.Item.Position.Y) > 80)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "Grab one of the shells. You can load it by selecting the railgun loader and dragging the shell to. "
                + "one of the free slots. You need two hands to carry a shell, so make sure you don't have anything else in either hand.");

            while (loader.Item.ContainedItems.FirstOrDefault(i => i != null && i.Prefab.Identifier == "railgunshell") == null)
            {
                //TODO: reimplement
                //moloch.Health = 50.0f;

                capacitor1.Charge += 5.0f;
                capacitor2.Charge += 5.0f;
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "Now we're ready to shoot! Select the railgun controller.");

            while (Controlled.SelectedConstruction == null || Controlled.SelectedConstruction.Prefab.Identifier != "railguncontroller")
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            moloch.AnimController.SetPosition(ConvertUnits.ToSimUnits(Controlled.WorldPosition + Vector2.UnitY * 600.0f));

            infoBox = CreateInfoFrame("", "Use the right mouse button to aim and wait for the creature to come closer. When you're ready to shoot, "
                + "press the left mouse button.");

            while (!moloch.IsDead)
            {
                if (moloch.WorldPosition.Y > Controlled.WorldPosition.Y + 600.0f)
                {
                    moloch.AIController.SteeringManager.SteeringManual(CoroutineManager.DeltaTime, Controlled.WorldPosition - moloch.WorldPosition);
                }

                moloch.AIController.SelectTarget(Controlled.AiTarget);
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            Submarine.MainSub.GodMode = false;

            infoBox = CreateInfoFrame("", "The creature has died. Now you should fix the damages in the control room: " +
                "Grab a welding tool from the closet in the railgun room.");

            while (!HasItem("weldingtool"))
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "The welding tool requires fuel to work. Grab a welding fuel tank and attach it to the tool " +
                "by dragging it into the same slot.");

            do
            {
                var weldingTool = Controlled.Inventory.Items.FirstOrDefault(i => i != null && i.Prefab.Identifier == "weldingtool");
                if (weldingTool != null &&
                    weldingTool.ContainedItems.FirstOrDefault(contained => contained != null && contained.Prefab.Identifier == "weldingfueltank") != null) break;

                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            } while (true);


            infoBox = CreateInfoFrame("", "You can aim with the tool using the right mouse button and weld using the left button. " +
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

            infoBox = CreateInfoFrame("", "The hull is fixed now, but there's still quite a bit of water inside the sub. It should be pumped out "
                + "using the bilge pump in the room at the bottom of the submarine.");

            Pump pump = Item.ItemList.Find(i => i.HasTag("tutorialpump")).GetComponent<Pump>();

            while (Vector2.Distance(Controlled.Position, pump.Item.Position) > 100.0f)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "The two pumps inside the ballast tanks "
                + "are connected straight to the navigation terminal and can't be manually controlled unless you mess with their wiring, " +
                "so you should only use the pump in the middle room to pump out the water. Select it, turn it on and adjust the pumping speed " +
                "to start pumping water out.", hasButton: true);

            while (infoBox != null)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }


            bool brokenMsgShown = false;

            Item brokenBox = null;

            while (pump.FlowPercentage > 0.0f || pump.CurrFlow <= 0.0f || !pump.IsActive)
            {
                if (!brokenMsgShown && pump.Voltage < pump.MinVoltage && Controlled.SelectedConstruction == pump.Item)
                {
                    brokenMsgShown = true;

                    infoBox = CreateInfoFrame("", "Looks like the pump isn't getting any power. The water must have short-circuited some of the junction "
                        + "boxes. You can check which boxes are broken by selecting them.");

                    while (true)
                    {
                        if (Controlled.SelectedConstruction!=null && 
                            Controlled.SelectedConstruction.GetComponent<PowerTransfer>() != null && 
                            Controlled.SelectedConstruction.Condition == 0.0f)
                        {
                            brokenBox = Controlled.SelectedConstruction;

                            infoBox = CreateInfoFrame("", "Here's our problem: this junction box is broken. Luckily engineers are adept at fixing electrical devices - "
                                + "you just need to find a spare wire and click the \"Fix\"-button to repair the box.");
                            break;
                        }

                        if (pump.Voltage > pump.MinVoltage) break;

                        yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
                    }
                }
                
                if (brokenBox != null && brokenBox.ConditionPercentage > 50.0f && pump.Voltage < pump.MinVoltage)
                {
                    yield return new WaitForSeconds(1.0f);

                    if (pump.Voltage < pump.MinVoltage)
                    {
                        infoBox = CreateInfoFrame("", "The pump is still not running. Check if there are more broken junction boxes between the pump and the reactor.");
                    }
                    brokenBox = null;
                }

                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "The pump is up and running. Wait for the water to be drained out.");

            while (pump.Item.CurrentHull.WaterVolume > 1000.0f)
            {
                yield return Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            infoBox = CreateInfoFrame("", "That was all there is to this tutorial! Now you should be able to handle " +
            "most of the basic tasks on board the submarine.");

            Completed = true;

            yield return new WaitForSeconds(4.0f);

            Controlled = null;
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;

            var cinematic = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam, Alignment.CenterLeft, Alignment.CenterRight, duration: 5.0f);

            while (cinematic.Running)
            {
                yield return Controlled != null && Controlled.IsDead ? CoroutineStatus.Success : CoroutineStatus.Running;
            }

            Submarine.Unload();
            GameMain.MainMenuScreen.Select();

            yield return CoroutineStatus.Success;
        }

        private bool HasItem(string itemIdentifier)
        {
            if (Character.Controlled == null) return false;

            return Character.Controlled.Inventory.FindItemByIdentifier(itemIdentifier) != null;
        }
        
        protected IEnumerable<object> KeepReactorRunning(Reactor reactor)
        {
            do
            {
                //TODO: reimplement
                /*reactor.AutoTemp = true;
                reactor.ShutDownTemp = 5000.0f;*/

                yield return CoroutineStatus.Running;
            } while (Item.ItemList.Contains(reactor.Item));

            yield return CoroutineStatus.Success;
        }


        /// <summary>
        /// keeps the enemy away from the sub until the capacitors are loaded
        /// </summary>
        private IEnumerable<object> KeepEnemyAway(Character enemy, PowerContainer[] capacitors)
        {
            do
            {
                if (enemy == null || Character.Controlled == null) break;

                //TODO: reimplement
                //enemy.Health = 50.0f;

                enemy.AIController.State = AIState.Idle;

                Vector2 targetPos = Character.Controlled.WorldPosition + new Vector2(0.0f, 3000.0f);

                Vector2 steering = targetPos - enemy.WorldPosition;
                if (steering != Vector2.Zero) steering = Vector2.Normalize(steering);

                enemy.AIController.Steering = steering;

                yield return CoroutineStatus.Running;
            } while (capacitors.FirstOrDefault(c => c.Charge > 0.4f) == null);

            yield return CoroutineStatus.Success;
        }

    }
}
