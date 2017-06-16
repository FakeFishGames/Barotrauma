using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using Barotrauma.Lights;

namespace Barotrauma
{
    partial class Structure : MapEntity, IDamageable, IServerSerializable
    {

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (prefab.sprite == null) return;

            Draw(spriteBatch, editing, back, null);
        }

        public override void DrawDamage(SpriteBatch spriteBatch, Effect damageEffect)
        {
            Draw(spriteBatch, false, false, damageEffect);
        }
        
        private void Draw(SpriteBatch spriteBatch, bool editing, bool back = true, Effect damageEffect = null)
        {
            if (prefab.sprite == null) return;

            Color color = (isHighlighted) ? Color.Orange : Color.White;
            if (IsSelected && editing)
            {
                color = Color.Red;

                GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, -rect.Y, rect.Width, rect.Height), color);
            }

            Vector2 drawOffset = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;

            float depth = prefab.sprite.Depth;
            depth -= (ID % 255) * 0.000001f;

            if (back && damageEffect == null)
            {
                if (prefab.BackgroundSprite != null)
                {
                    prefab.BackgroundSprite.DrawTiled(
                        spriteBatch,
                        new Vector2(rect.X + drawOffset.X, -(rect.Y + drawOffset.Y)),
                        new Vector2(rect.Width, rect.Height),
                        color, Point.Zero);
                }
            }

            SpriteEffects oldEffects = prefab.sprite.effects;
            prefab.sprite.effects ^= SpriteEffects;

            if (back == prefab.sprite.Depth > 0.5f || editing)
            {
                for (int i = 0; i < sections.Length; i++)
                {
                    if (damageEffect != null)
                    {
                        float newCutoff = Math.Min((sections[i].damage / prefab.MaxHealth), 0.65f);

                        if (Math.Abs(newCutoff - Submarine.DamageEffectCutoff) > 0.01f)
                        {
                            damageEffect.Parameters["aCutoff"].SetValue(newCutoff);
                            damageEffect.Parameters["cCutoff"].SetValue(newCutoff * 1.2f);

                            damageEffect.CurrentTechnique.Passes[0].Apply();

                            Submarine.DamageEffectCutoff = newCutoff;
                        }
                    }

                    Point textureOffset = new Point(
                        Math.Abs(rect.Location.X - sections[i].rect.Location.X),
                        Math.Abs(rect.Location.Y - sections[i].rect.Location.Y));

                    if (flippedX && isHorizontal)
                    {
                        textureOffset.X = rect.Width - textureOffset.X - sections[i].rect.Width;
                    }

                    prefab.sprite.DrawTiled(
                        spriteBatch,
                        new Vector2(sections[i].rect.X + drawOffset.X, -(sections[i].rect.Y + drawOffset.Y)),
                        new Vector2(sections[i].rect.Width, sections[i].rect.Height),
                        color,
                        textureOffset, depth);
                }
            }

            prefab.sprite.effects = oldEffects;
        }


        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Structure");

            element.Add(new XAttribute("name", prefab.Name),
                new XAttribute("ID", ID),
                new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height));

            for (int i = 0; i < sections.Length; i++)
            {
                if (sections[i].damage == 0.0f) continue;

                var sectionElement =
                    new XElement("section",
                        new XAttribute("i", i),
                        new XAttribute("damage", sections[i].damage));

                if (sections[i].gap != null)
                {
                    sectionElement.Add(new XAttribute("gap", sections[i].gap.ID));
                }

                element.Add(sectionElement);
            }

            parentElement.Add(element);

            return element;
        }
    }
}
