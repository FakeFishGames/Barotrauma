using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class MonsterQuest : Quest
    {
        private string monsterFile;

        private int state;

        private Character monster;

        public override Vector2 RadarPosition
        {
            get { return monster.Position; }
        }

        public MonsterQuest(XElement element)
            : base(element)
        {
            monsterFile = ToolBox.GetAttributeString(element, "monsterfile", "");
        }

        public override void Start(Level level)
        {
            Vector2 position = level.PositionsOfInterest[Rand.Int(level.PositionsOfInterest.Count, false)];

            monster = Character.Create(monsterFile, position);
        }

        public override void Update(float deltaTime)
        {
            switch (state)
            {
                case 0:
                    if (!monster.IsDead) return;
                    ShowMessage(state);
                    state = 1;
                    break;
            }
        }
        
        public override void End()
        {
            if (!monster.IsDead)
            {
                new GUIMessageBox("Quest failed", failureMessage);
                return;
            }
            
            GiveReward();

            completed = true;
        }
    }
}
