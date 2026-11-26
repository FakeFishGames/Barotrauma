using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Barotrauma.Particles;

namespace Barotrauma.Items.Components
{
    partial class ElectricalDischarger : Powered
    {
        [Serialize("1.0,1.0", IsPropertySaveable.No), Editable(DecimalCount = 2)]
        public Vector2 ElectricityScale { get; set; }
        
        [Serialize("0.5,2.0", IsPropertySaveable.No), Editable(DecimalCount = 2)]
        public Vector2 ElectricityScaleRandomMultiplier { get; set; }
        
        [Serialize("173,216,230,255", IsPropertySaveable.Yes, description: "The discharger effects are lerped randomly between two colors. This is the first color."), Editable]
        public Color ElectricityColor1 { get; set; }
        
        [Serialize("255,255,255,255", IsPropertySaveable.Yes, description: "The discharger effects are lerped randomly between two colors. This is the second color."), Editable]
        public Color ElectricityColor2 { get; set; }
        
        [Serialize("ElectricShock", IsPropertySaveable.No)]
        public Identifier SparkParticle { get; set; }
        private ParticlePrefab sparkParticlePrefab;
        
        [Serialize("0.3,0.3", IsPropertySaveable.No), Editable]
        public Vector2 SparkParticleSize { get; set; } 
        
        [Serialize("swirlysmoke", IsPropertySaveable.No)]
        public Identifier SmokeParticle { get; set; }
        private ParticlePrefab smokeParticlePrefab;
        
        [Serialize("1.0,1.0", IsPropertySaveable.No), Editable]
        public Vector2 SmokeParticleSize { get; set; } 
        
        [Editable, Serialize("255,255,255,255", IsPropertySaveable.No)]
        public Color SparkParticleColorMultiplier { get; set; }
        
        [Serialize(50f, IsPropertySaveable.No), Editable]
        public float SparkParticleInterval { get; set; }
        
        [Serialize(0f, IsPropertySaveable.No, description: "Overrides the particle lifetime. By default, uses the lifetime defined for the particle prefab."), Editable]
        public float SparkParticleLifetime { get; set; }
        
        private static SpriteSheet electricitySprite;

        private int frameOffset;

        //the ammo of the turret this discharger belongs to can change how the discharge looks
        private Color CurrentElectricityColor1 => dischargeAmmo?.DischargeColor1 ?? ElectricityColor1;
        private Color CurrentElectricityColor2 => dischargeAmmo?.DischargeColor2 ?? ElectricityColor2;
        private Color CurrentSparkParticleColorMultiplier => dischargeAmmo?.DischargeSparkColor ?? SparkParticleColorMultiplier;
        private Vector2 CurrentElectricityScale => dischargeAmmo?.DischargeElectricityScale ?? ElectricityScale;

        partial void InitProjSpecific()
        {
            if (electricitySprite == null)
            {
                electricitySprite = new SpriteSheet("Content/Lights/Electricity.png", 4, 4, new Vector2(0.5f, 0.0f));
            }
            if (!SparkParticle.IsEmpty)
            {
                sparkParticlePrefab = ParticleManager.FindPrefab(SparkParticle);   
            }
            if (!SmokeParticle.IsEmpty)
            {
                smokeParticlePrefab = ParticleManager.FindPrefab(SmokeParticle);   
            }
        }

        partial void DischargeProjSpecific()
        {
            PlaySound(ActionType.OnUse);
            foreach (Node node in nodes)
            {
                if (smokeParticlePrefab != null)
                {
                    Particle smoke = GameMain.ParticleManager.CreateParticle(smokeParticlePrefab, node.WorldPosition, Vector2.Zero);
                    if (smoke != null)
                    {
                        smoke.Size *= SmokeParticleSize;
                    }
                }
                if (node.ParentIndex > -1)
                {
                    CreateParticlesBetween(nodes[node.ParentIndex].WorldPosition, node.WorldPosition);
                }
            }
            foreach (var character in charactersInRange)
            {
                CreateParticlesBetween(character.character.WorldPosition, character.node.WorldPosition);
            }

            void CreateParticlesBetween(Vector2 start, Vector2 end)
            {
                if (sparkParticlePrefab == null) { return; }
                if (SparkParticleInterval <= 0) { return; }
                Vector2 diff = end - start;
                float dist = diff.Length();
                Vector2 normalizedDiff = MathUtils.NearlyEqual(dist, 0.0f) ? Vector2.Zero : diff / dist;
                
                for (float x = 0.0f; x < dist; x += SparkParticleInterval)
                {
                    Particle spark = GameMain.ParticleManager.CreateParticle(sparkParticlePrefab, start + normalizedDiff * x, Vector2.Zero);
                    if (spark != null)
                    {
                        if (SparkParticleLifetime > 0)
                        {
                            spark.SetLifeTime(SparkParticleLifetime);
                        }
                        spark.Size *= SparkParticleSize;
                        Color sparkParticleColorMultiplier = CurrentSparkParticleColorMultiplier;
                        if (sparkParticleColorMultiplier != Color.White)
                        {
                            spark.ColorMultiplier = sparkParticleColorMultiplier.ToVector4();
                        }
                    }
                }
            }
        }

        public void DrawElectricity(SpriteBatch spriteBatch)
        {
            if (timer <= 0.0f && Screen.Selected is { IsEditor: false }) { return; }
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Length <= 1.0f) { continue; }
                var node = nodes[i];
                electricitySprite.Draw(spriteBatch,
                    (i + frameOffset) % electricitySprite.FrameCount,
                    new Vector2(node.WorldPosition.X, -node.WorldPosition.Y),
                    Color.Lerp(CurrentElectricityColor1, CurrentElectricityColor2, Rand.Range(0.0f, 1.0f)),
                    electricitySprite.Origin, -node.Angle - MathHelper.PiOver2,
                    new Vector2(
                        Math.Min(node.Length / electricitySprite.FrameSize.X, 1.0f) * Rand.Range(ElectricityScaleRandomMultiplier.X, ElectricityScaleRandomMultiplier.Y),
                        node.Length / electricitySprite.FrameSize.Y) * CurrentElectricityScale);
            }

            if (GameMain.DebugDraw)
            {
                for (int i = 1; i < nodes.Count; i++)
                {
                    GUI.DrawLine(spriteBatch, 
                        new Vector2(nodes[i].WorldPosition.X, -nodes[i].WorldPosition.Y), 
                        new Vector2(nodes[nodes[i].ParentIndex].WorldPosition.X, -nodes[nodes[i].ParentIndex].WorldPosition.Y),
                         Color.LightCyan,
                         width: 3);

                    if (nodes[i].Length <= 1.0f) { continue; }
                    GUI.DrawRectangle(spriteBatch, new Vector2(nodes[i].WorldPosition.X, -nodes[i].WorldPosition.Y), Vector2.One * 10, Color.LightCyan, isFilled: true);
                }
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            UInt16 userID = msg.ReadUInt16();
            if (userID != Entity.NullEntityID)
            {
                user = Entity.FindEntityByID(userID) as Character;
            }
            CurrPowerConsumption = powerConsumption;
            charging = true;
            dischargeAmmo = GetTurretAmmo();
            timer = CurrentDischargeDuration;
            IsActive = true;
        }
    }
}
