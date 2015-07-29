using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{
    class GUIMessage
    {
        ColoredText coloredText;
        Vector2 pos;

        float lifeTime;

        Vector2 size;


        public string Text
        {
            get { return coloredText.text; }
        }

        public Color Color
        {
            get { return coloredText.color; }
        }

        public Vector2 Pos
        {
            get { return pos; }
            set { pos = value; }
        }

        public Vector2 Size
        {
            get { return size; }
        }


        public float LifeTime
        {
            get { return lifeTime; }
            set { lifeTime = value; }
        }

        public GUIMessage(string text, Color color, Vector2 position, float lifeTime)
        {
            coloredText = new ColoredText(text, color);
            pos = position;
            this.lifeTime = lifeTime;

            size = GUI.Font.MeasureString(text);
        }
    }
}
