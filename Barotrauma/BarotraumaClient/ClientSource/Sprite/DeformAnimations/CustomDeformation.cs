﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.SpriteDeformations
{
    class CustomDeformationParams : SpriteDeformationParams
    {
        [Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f),
            Serialize(0.0f, IsPropertySaveable.Yes, description: "How fast the deformation \"oscillates\" back and forth. " +
            "For example, if the sprite is stretched up, setting this value above zero would make it do a wave-like movement up and down.")]
        public override float Frequency { get; set; } = 1;

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "The \"strength\" of the deformation."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f)]
        public float Amplitude { get; set; }

        public CustomDeformationParams(XElement element) : base(element)
        {
        }
    }

    class CustomDeformation : SpriteDeformation
    {
        private readonly List<Vector2[]> deformRows = new List<Vector2[]>();

        private readonly Vector2[,] flippedDeformation;

        private CustomDeformationParams CustomDeformationParams => Params as CustomDeformationParams;

        public override float Phase
        {
            get { return phase; }
            set
            {
                phase = value;
                //phase %= MathHelper.TwoPi;
            }
        }
        private float phase;

        public CustomDeformation(XElement element) : base(element, new CustomDeformationParams(element))
        {
            phase = Rand.Range(0.0f, MathHelper.TwoPi);

            if (element == null)
            {
                deformRows.Add(new Vector2[] { Vector2.Zero, Vector2.Zero });
                deformRows.Add(new Vector2[] { Vector2.Zero, Vector2.Zero });
            }
            else
            {
                for (int i = 0; ; i++)
                {
                    string row = element.GetAttributeString("row" + i, "");
                    if (string.IsNullOrWhiteSpace(row)) break;

                    string[] splitRow = row.Split(' ');
                    Vector2[] rowVectors = new Vector2[splitRow.Length];
                    for (int j = 0; j < splitRow.Length; j++)
                    {
                        rowVectors[j] = XMLExtensions.ParseVector2(splitRow[j]);
                    }
                    deformRows.Add(rowVectors);
                }
            }

            if (deformRows.Count() == 0 || deformRows.First() == null || deformRows.First().Length == 0)
            {
                return;
            }

            var configDeformation = new Vector2[deformRows.First().Length, deformRows.Count];
            for (int x = 0; x < configDeformation.GetLength(0); x++)
            {
                for (int y = 0; y < configDeformation.GetLength(1); y++)
                {
                    configDeformation[x, y] = deformRows[y][x];
                }
            }

            //construct an array for the desired resolution, 
            //interpolating values if the resolution configured in the xml is smaller
            //deformation = new Vector2[Resolution.X, Resolution.Y];
            int newWidth = Resolution.X, newHeight = Resolution.Y;
            Deformation = MathUtils.ResizeVector2Array(configDeformation, newWidth, newHeight);

            flippedDeformation = new Vector2[Resolution.X, Resolution.Y];
            for (int x = 0; x < Resolution.X; x++)
            {
                for (int y = 0; y < Resolution.Y; y++)
                {
                    flippedDeformation[x, y] = Deformation[Resolution.X - x - 1, y]; // read the rows from right to left
                }
            }
        }

        protected override void GetDeformation(out Vector2[,] deformation, out float multiplier, bool flippedHorizontally, bool inverseY)
        {
            deformation = flippedHorizontally ? flippedDeformation : Deformation;
            multiplier = CustomDeformationParams.Frequency <= 0.0f ? 
                CustomDeformationParams.Amplitude : 
                (float)Math.Sin(inverseY ? -phase : phase) * CustomDeformationParams.Amplitude;
            multiplier *= Params.Strength;
        }

        public override void Update(float deltaTime)
        {
            if (!Params.UseMovementSine)
            {
                phase += deltaTime * CustomDeformationParams.Frequency;
                phase %= MathHelper.TwoPi;
            }
        }

        public override void Save(XElement element)
        {
            base.Save(element);
            for (int i = 0; i < deformRows.Count; i++)
            {
                element.Add(new XAttribute("row" + i, string.Join(" ", deformRows[i].Select(r => XMLExtensions.Vector2ToString(r)))));
            }
        }
    }
}
