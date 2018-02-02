using Microsoft.Xna.Framework;
using System;
using System.Linq;
#if DEBUG && CLIENT
using Microsoft.Xna.Framework.Input;
#endif

namespace Barotrauma
{
    partial class GameScreen : Screen
    {
        private Camera cam;

        public override Camera Cam
        {
            get { return cam; }
        }
        
        public GameScreen()
        {
            cam = new Camera();
            cam.Translate(new Vector2(-10.0f, 50.0f));
        }

        public override void Select()
        {
            base.Select();

            if (Character.Controlled != null)
            {
                cam.Position = Character.Controlled.WorldPosition;
                cam.UpdateTransform(true);
            }
            else if (Submarine.MainSub != null)
            {
                cam.Position = Submarine.MainSub.WorldPosition;
                cam.UpdateTransform(true);
            }

            foreach (MapEntity entity in MapEntity.mapEntityList)
                entity.IsHighlighted = false;
        }

        public override void Deselect()
        {
            base.Deselect();

#if CLIENT
            Sounds.SoundManager.LowPassHFGain = 1.0f;
#endif
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        public override void Update(double deltaTime)
        {
#if CLIENT
            if(Character.Spied != null)
            {
                if ((PlayerInput.KeyDown(InputType.Up) || PlayerInput.KeyDown(InputType.Down) || PlayerInput.KeyDown(InputType.Left) || PlayerInput.KeyDown(InputType.Right)) && !DebugConsole.IsOpen)
                {
                    if (GameMain.NetworkMember != null && !GameMain.NetworkMember.chatMsgBox.Selected)
                    {
                        if (Character.Controlled != null)
                        {
                            cam.Position = Character.Controlled.WorldPosition;
                        }
                        else
                        {
                            cam.Position = Character.Spied.WorldPosition;
                        }
                        Character.Spied = null;
                        cam.UpdateTransform(true);
                    }
                }
            }
#endif

#if DEBUG && CLIENT
            if (GameMain.GameSession != null && GameMain.GameSession.Level != null && GameMain.GameSession.Submarine != null &&
                !DebugConsole.IsOpen && GUIComponent.KeyboardDispatcher.Subscriber == null)
            {
                /*
                var closestSub = Submarine.FindClosest(cam.WorldViewCenter);
                if (closestSub == null) closestSub = GameMain.GameSession.Submarine;

                Vector2 targetMovement = Vector2.Zero;
                if (PlayerInput.KeyDown(Keys.I)) targetMovement.Y += 1.0f;
                if (PlayerInput.KeyDown(Keys.K)) targetMovement.Y -= 1.0f;
                if (PlayerInput.KeyDown(Keys.J)) targetMovement.X -= 1.0f;
                if (PlayerInput.KeyDown(Keys.L)) targetMovement.X += 1.0f;

                if (targetMovement != Vector2.Zero)
                    closestSub.ApplyForce(targetMovement * closestSub.SubBody.Body.Mass * 100.0f);
                    */
            }
#endif

#if CLIENT
            GameMain.NilModProfiler.SWMapEntityUpdate.Start();
#endif

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                e.IsHighlighted = false;
            }

#if CLIENT
            GameMain.NilModProfiler.SWMapEntityUpdate.Stop();

            if (GameMain.GameSession != null)
            {
                GameMain.NilModProfiler.SWGameSessionUpdate.Start();
                GameMain.GameSession.Update((float)deltaTime);
                GameMain.NilModProfiler.RecordGameSessionUpdate();
            }

            GameMain.NilModProfiler.SWParticleManager.Start();

            GameMain.ParticleManager.Update((float)deltaTime);

            GameMain.NilModProfiler.RecordParticleManager();
            GameMain.NilModProfiler.SWLightManager.Start();

            GameMain.LightManager.Update((float)deltaTime);

            GameMain.NilModProfiler.RecordLightManager();
#endif

            if (Level.Loaded != null)
            {
#if CLIENT
                GameMain.NilModProfiler.SWLevelUpdate.Start();
#endif
                Level.Loaded.Update((float)deltaTime, cam);
#if CLIENT
                GameMain.NilModProfiler.RecordLevelUpdate();
#endif
            }

#if CLIENT

            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null && Character.Controlled.CanInteractWith(Character.Controlled.SelectedConstruction))
            {
                Character.Controlled.SelectedConstruction.UpdateHUD(cam, Character.Controlled);                
            }
            GameMain.NilModProfiler.SWCharacterUpdate.Start();
#endif
            Character.UpdateAll((float)deltaTime, cam);

#if CLIENT
            //NilMod spy Code
            if(Character.Spied != null)
            {
                Character.ViewSpied((float)deltaTime, Cam, true);
                Lights.LightManager.ViewTarget = Character.Spied;
                CharacterHUD.Update((float)deltaTime, Character.Spied);

                foreach (HUDProgressBar progressBar in Character.Spied.HUDProgressBars.Values)
                {
                    progressBar.Update((float)deltaTime);
                }

                foreach(var pb in Character.Spied.HUDProgressBars)
                {
                    if(pb.Value.FadeTimer <= 0.0f)
                    {
                        Character.Spied.HUDProgressBars.Remove(pb.Key);
                    }
                }
            }

            GameMain.NilModProfiler.SWCharacterUpdate.Stop();
            GameMain.NilModProfiler.RecordCharacterUpdate();
            GameMain.NilModProfiler.SWStatusEffect.Start();
#endif
            StatusEffect.UpdateAll((float)deltaTime);

#if CLIENT
            GameMain.NilModProfiler.RecordStatusEffect();
            if (Character.Controlled != null && Lights.LightManager.ViewTarget != null || Character.Spied != null && Lights.LightManager.ViewTarget != null)
            {
                cam.TargetPos = Lights.LightManager.ViewTarget.WorldPosition;
            }
#endif

            cam.MoveCamera((float)deltaTime);
#if CLIENT
            GameMain.NilModProfiler.SWSetTransforms.Start();
#endif
            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.SetPrevTransform(sub.Position);
            }

            foreach (PhysicsBody pb in PhysicsBody.List)
            {
                pb.SetPrevTransform(pb.SimPosition, pb.Rotation);
            }
#if CLIENT
            GameMain.NilModProfiler.RecordSetTransforms();
            GameMain.NilModProfiler.SWMapEntityUpdate.Start();
#endif
            MapEntity.UpdateAll((float)deltaTime, cam);
#if CLIENT
            GameMain.NilModProfiler.RecordMapEntityUpdate();
            GameMain.NilModProfiler.SWCharacterAnimUpdate.Start();
#endif
            Character.UpdateAnimAll((float)deltaTime);
#if CLIENT
            GameMain.NilModProfiler.RecordCharacterAnimUpdate();
            GameMain.NilModProfiler.SWRagdollUpdate.Start();
#endif
            Ragdoll.UpdateAll((float)deltaTime, cam);
#if CLIENT
            GameMain.NilModProfiler.RecordRagdollUpdate();
            GameMain.NilModProfiler.SWSubmarineUpdate.Start();
#endif
            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.Update((float)deltaTime);
            }

#if CLIENT
            GameMain.NilModProfiler.RecordSubmarineUpdate();
            GameMain.NilModProfiler.SWCharacterUpdate.Start();
#endif

#if CLIENT
            GameMain.NilModProfiler.RecordCharacterUpdate();
            GameMain.NilModProfiler.SWPhysicsWorldStep.Start();
#endif
            GameMain.World.Step((float)deltaTime);
#if CLIENT
            GameMain.NilModProfiler.RecordPhysicsWorldStep();

            if (!PlayerInput.LeftButtonHeld())
            {
                Inventory.draggingSlot = null;
                Inventory.draggingItem = null;
            }
#endif
        }

        public void RunIngameCommand(string Command, Object[] Arguments)
        {
            Vector2 WorldCoordinate = new Vector2(0,0);
            Character character = null;

            //Server Commands
            if (GameMain.Server != null)
            {
                switch (Command)
                {
                    case "spawncreature":
                        //ARG0 = Character, ARG1 = WorldPosX, ARG2 = WorldPosY
                        WorldCoordinate = new Vector2(float.Parse((string)Arguments[1]), float.Parse((string)Arguments[2]));

                        if (Arguments[0].ToString().ToLowerInvariant() == "human")
                        {
                            character = Character.Create(Character.HumanConfigFile, WorldCoordinate);
                        }
                        else
                        {
                            character = Character.Create(
                            "Content/Characters/"
                            + Arguments[0].ToString().ToUpper().First() + Arguments[0].ToString().Substring(1)
                            + "/" + Arguments[0].ToString().ToLower() + ".xml", WorldCoordinate);
                        }
                        break;

                    case "heal":
                        //ARG0 = Character
                        character = (Character)Arguments[0];
                        character.Heal();
                        break;

                    case "revive":
                        //ARG0 = Character
                        character = (Character)Arguments[0];
                        character.Revive(true);
                        break;

                    case "kill":
                        //ARG0 = Character
                        character = (Character)Arguments[0];
                        character.Kill(CauseOfDeath.Disconnected, true);
                        break;

                    case "removecorpse":
                        //ARG0 = Character
                        character = (Character)Arguments[0];
                        GameMain.NilMod.HideCharacter(character);
                        break;

                    case "teleportsub":
                        //ARG0 = SUBID, ARG1 = WorldPosX, ARG2 = WorldPosY
                        WorldCoordinate = new Vector2(float.Parse((string)Arguments[1]), float.Parse((string)Arguments[2]));
                        GameMain.Server.MoveSub(int.Parse(Arguments[0].ToString()), WorldCoordinate);
                        break;

                    case "relocate":
                        //ARG0 = Character, ARG1 = WorldPosX, ARG2 = WorldPosY

                        character = (Character)Arguments[0];
                        WorldCoordinate = new Vector2(float.Parse((string)Arguments[1]), float.Parse((string)Arguments[2]));

                        Character.Controlled.AnimController.CurrentHull = null;
                        Character.Controlled.Submarine = null;
                        Character.Controlled.AnimController.SetPosition(FarseerPhysics.ConvertUnits.ToSimUnits(WorldCoordinate));
                        Character.Controlled.AnimController.FindHull(WorldCoordinate, true);
                        break;

                    //case "handcuff":
                        //ARG0 = Character
                        //break;

                    case "freeze":
                        //ARG0 = Character

                        character = (Character)Arguments[0];

                        if (GameMain.NilMod.FrozenCharacters.Find(c => c == character) != null)
                        {
                            GameMain.NilMod.FrozenCharacters.Remove(character);

                            if (GameMain.Server.ConnectedClients.Find(c => c.Character == character) != null)
                            {
                                var chatMsg = Barotrauma.Networking.ChatMessage.Create(
                                "Server Message",
                                ("You have been frozen by the server\n\nYou may now move again and perform actions."),
                                (Barotrauma.Networking.ChatMessageType)Barotrauma.Networking.ChatMessageType.MessageBox,
                                null);

                                GameMain.Server.SendChatMessage(chatMsg, GameMain.Server.ConnectedClients.Find(c => c.Character == character));
                            }
                        }
                        else
                        {
                            GameMain.NilMod.FrozenCharacters.Add(character);

                            if (GameMain.Server.ConnectedClients.Find(c => c.Character == character) != null)
                            {
                                var chatMsg = Barotrauma.Networking.ChatMessage.Create(
                                "Server Message",
                                ("You have been frozen by the server\n\nYou may still talk if able, but no longer perform any actions or movements."),
                                (Barotrauma.Networking.ChatMessageType)Barotrauma.Networking.ChatMessageType.MessageBox,
                                null);

                                GameMain.Server.SendChatMessage(chatMsg, GameMain.Server.ConnectedClients.Find(c => c.Character == character));
                            }
                        }
                        break;

                    case "setclientcharacter":
                        //ARG0 = Client, ARG1 = Character
                        GameMain.Server.SetClientCharacter((Barotrauma.Networking.Client)Arguments[0], (Character)Arguments[1]);
                        break;

                    default:
                        DebugConsole.ThrowError(@"NILMOD Error: Unrecognized Command Execution: """ + Command + @"""");
                        break;
                }
            }
        }
    }
}
