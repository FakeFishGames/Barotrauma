using Barotrauma.Tutorials;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class SubTestMode : GameMode
    {
        public SubTestMode(GameModePreset preset, object param)
            : base(preset, param)
        {
            foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
            {
                for (int i = 0; i < jobPrefab.InitialCount; i++)
                {
                    var variant = Rand.Range(0, jobPrefab.Variants);
                    CrewManager.AddCharacterInfo(new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: jobPrefab, variant: variant));
                }
            }
        }

        public override void Start()
        {
            base.Start();
            
            isRunning = true;
            CrewManager.InitSinglePlayerRound();

            Submarine.MainSub.SetPosition(Vector2.Zero);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!isRunning|| GUI.DisableHUD || GUI.DisableUpperHUD) return;
            
            if (Submarine.MainSub == null) return;
        }

        public override void AddToGUIUpdateList()
        {
            if (!isRunning) return;

            base.AddToGUIUpdateList();
            CrewManager.AddToGUIUpdateList();
        }

        public override void Update(float deltaTime)
        {
            if (!isRunning) { return; }

            base.Update(deltaTime);
        }

        public override void End(string endMessage = "")
        {
            isRunning = false;

            GameMain.GameSession.EndRound("");

            CrewManager.EndRound();

            Submarine.Unload();
            
            GameMain.SubEditorScreen.Select();
        }

        private bool EndRound(Submarine leavingSub)
        {
            isRunning = false;

            End("");
            
            return true;
        }
    }
}
