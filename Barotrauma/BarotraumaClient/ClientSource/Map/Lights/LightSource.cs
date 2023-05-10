using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Lights
{
    class LightSourceParams : ISerializableEntity
    {
        public string Name => "Light Source";

        public bool Persistent;

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; } = new Dictionary<Identifier, SerializableProperty>();

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes, alwaysUseInstanceValues: true), Editable]
        public Color Color
        {
            get;
            set;
        }

        private float range;

        [Serialize(100.0f, IsPropertySaveable.Yes, alwaysUseInstanceValues: true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 2048.0f)]
        public float Range
        {
            get { return range; }
            set
            {
                range = MathHelper.Clamp(value, 0.0f, 4096.0f);
                TextureRange = range;
                if (OverrideLightTexture != null)
                {
                    TextureRange += Math.Max(
                        Math.Abs(OverrideLightTexture.RelativeOrigin.X - 0.5f) * OverrideLightTexture.size.X,
                        Math.Abs(OverrideLightTexture.RelativeOrigin.Y - 0.5f) * OverrideLightTexture.size.Y);
                }
            }
        }

        [Serialize(1f, IsPropertySaveable.Yes), Editable(minValue: 0.01f, maxValue: 100f, ValueStep = 0.1f, DecimalCount = 2)]
        public float Scale { get; set; }

        [Serialize("0, 0", IsPropertySaveable.Yes), Editable(ValueStep = 1, DecimalCount = 1, MinValueFloat = -1000f, MaxValueFloat = 1000f)]
        public Vector2 Offset { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes), Editable(MinValueFloat = -360, MaxValueFloat = 360, ValueStep = 1, DecimalCount = 0)]
        public float Rotation { get; set; }

        public Vector2 GetOffset() => Vector2.Transform(Offset, Matrix.CreateRotationZ(MathHelper.ToRadians(Rotation)));

        private float flicker;
        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "How heavily the light flickers. 0 = no flickering, 1 = the light will alternate between completely dark and full brightness.")]
        public float Flicker
        {
            get { return flicker; }
            set
            {
                flicker = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [Editable, Serialize(1.0f, IsPropertySaveable.No, description: "How fast the light flickers.")]
        public float FlickerSpeed
        {
            get;
            set;
        }

        private float pulseFrequency;
        [Editable, Serialize(0.0f, IsPropertySaveable.Yes, description: "How rapidly the light pulsates (in Hz). 0 = no blinking.")]
        public float PulseFrequency
        {
            get { return pulseFrequency; }
            set
            {
                pulseFrequency = MathHelper.Clamp(value, 0.0f, 60.0f);
            }
        }

        private float pulseAmount;
        [Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, DecimalCount = 2), Serialize(0.0f, IsPropertySaveable.Yes, description: "How much light pulsates (in Hz). 0 = not at all, 1 = alternates between full brightness and off.")]
        public float PulseAmount
        {
            get { return pulseAmount; }
            set
            {
                pulseAmount = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        private float blinkFrequency;
        [Editable, Serialize(0.0f, IsPropertySaveable.Yes, description: "How rapidly the light blinks on and off (in Hz). 0 = no blinking.")]
        public float BlinkFrequency
        {
            get { return blinkFrequency; }
            set
            {
                blinkFrequency = MathHelper.Clamp(value, 0.0f, 60.0f);
            }
        }

        public float TextureRange
        {
            get;
            private set;
        }

        public Sprite OverrideLightTexture
        {
            get;
            private set;
        }
        //Additional sprite drawn on top of the lightsource. Ignores shadows.
        //Can be used to make lamp sprites glow for example.
        public Sprite LightSprite
        {
            get;
            private set;
        }

        public ContentXElement DeformableLightSpriteElement
        {
            get;
            private set;
        }

        //Override the alpha value of the light sprite (if not set, the alpha of the light color is used)
        //Can be used to make lamp sprites glow at full brightness even if the light itself is dim.
        public float? OverrideLightSpriteAlpha;

        public LightSourceParams(ContentXElement element)
        {
            Deserialize(element);

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                    case "lightsprite":
                        {
                            LightSprite = new Sprite(subElement);
                            float spriteAlpha = subElement.GetAttributeFloat("alpha", -1.0f);
                            if (spriteAlpha >= 0.0f)
                            {
                                OverrideLightSpriteAlpha = spriteAlpha;
                            }
                        }
                        break;
                    case "deformablesprite":
                        {
                            DeformableLightSpriteElement = subElement;
                            float spriteAlpha = subElement.GetAttributeFloat("alpha", -1.0f);
                            if (spriteAlpha >= 0.0f)
                            {
                                OverrideLightSpriteAlpha = spriteAlpha;
                            }
                        }
                        break;
                    case "lighttexture":
                        OverrideLightTexture = new Sprite(subElement);
                        //refresh TextureRange
                        Range = range;
                        break;
                }
            }
        }

        public LightSourceParams(float range, Color color)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this);
            Range = range;
            Color = color;
        }

        public bool Deserialize(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            return SerializableProperties != null;
        }

        public void Serialize(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element, true);
        }
    }

    class LightSource
    {
        //how many pixels the position of the light needs to change for the light volume to be recalculated
        const float MovementRecalculationThreshold = 10.0f;
        //how many radians the light needs to rotate for the light volume to be recalculated
        const float RotationRecalculationThreshold = 0.02f;

        private static Texture2D lightTexture;

        private VertexPositionColorTexture[] vertices;
        private short[] indices;

        private readonly List<ConvexHullList> convexHullsInRange;

        public Texture2D texture;

        public SpriteEffects LightSpriteEffect;

        public Submarine ParentSub;

        private bool castShadows;
        public bool CastShadows
        {
            get { return castShadows && !IsBackground; }
            set { castShadows = value; }
        }

        //what was the range of the light when lightvolumes were last calculated
        private float prevCalculatedRange;
        private Vector2 prevCalculatedPosition;

        //Which submarines' convex hulls are up to date? Resets when the item moves/rotates relative to the submarine.
        //Can contain null (means convex hulls that aren't part of any submarine).
        public HashSet<Submarine> HullsUpToDate = new HashSet<Submarine>();

        //do we need to recalculate the vertices of the light volume
        private bool needsRecalculation;
        public bool NeedsRecalculation
        {
            get { return needsRecalculation; }
            set
            {
                if (!needsRecalculation && value)
                {
                    foreach (ConvexHullList chList in convexHullsInRange)
                    {
                        chList.IsHidden.Clear();
                    }
                }
                needsRecalculation = value;
            }
        }

        //when were the vertices of the light volume last calculated
        public float LastRecalculationTime { get; private set; }

        private readonly Dictionary<Submarine, Vector2> diffToSub;

        private DynamicVertexBuffer lightVolumeBuffer;
        private DynamicIndexBuffer lightVolumeIndexBuffer;
        private int vertexCount;
        private int indexCount;

        private Vector2 translateVertices;
        private float rotateVertices;

        private readonly LightSourceParams lightSourceParams;

        public LightSourceParams LightSourceParams => lightSourceParams;

        private Vector2 position;
        public Vector2 Position
        {
            get { return position; }
            set
            {
                Vector2 moveAmount = value - position;
                if (Math.Abs(moveAmount.X) < 0.1f && Math.Abs(moveAmount.Y) < 0.1f) { return; }
                position = value;

                //translate light volume manually instead of doing a full recalculation when moving by a small amount
                if (Vector2.DistanceSquared(prevCalculatedPosition, position) < MovementRecalculationThreshold * MovementRecalculationThreshold && vertices != null)
                {
                    translateVertices = position - prevCalculatedPosition;
                    return;
                }

                HullsUpToDate.Clear();
                NeedsRecalculation = true;
            }
        }

        private float prevCalculatedRotation;
        private float rotation;
        public float Rotation
        {
            get { return rotation; }
            set
            {
                if (Math.Abs(value - rotation) < 0.001f) { return; }
                rotation = value;

                if (Math.Abs(rotation - prevCalculatedRotation) < RotationRecalculationThreshold && vertices != null)
                {
                    rotateVertices = rotation - prevCalculatedRotation;
                    return;
                }

                HullsUpToDate.Clear();
                NeedsRecalculation = true;
            }
        }

        private Vector2 _spriteScale = Vector2.One;

        public Vector2 SpriteScale
        {
            get { return _spriteScale * lightSourceParams.Scale; }
            set { _spriteScale = value; }
        }

        public float? OverrideLightSpriteAlpha
        {
            get { return lightSourceParams.OverrideLightSpriteAlpha; }
            set { lightSourceParams.OverrideLightSpriteAlpha = value; }
        }

        public Vector2 WorldPosition
        {
            get { return (ParentSub == null) ? position : position + ParentSub.Position; }
        }

        public static Texture2D LightTexture
        {
            get
            {
                if (lightTexture == null)
                {
                    lightTexture = TextureLoader.FromFile("Content/Lights/pointlight_bright.png");
                }

                return lightTexture;
            }
        }

        public Sprite OverrideLightTexture
        {
            get { return lightSourceParams.OverrideLightTexture; }
        }

        public Sprite LightSprite
        {
            get { return lightSourceParams.LightSprite; }
        }

        private Vector2 OverrideLightTextureOrigin => OverrideLightTexture.Origin + LightSourceParams.Offset;

        public Color Color
        {
            get { return lightSourceParams.Color; }
            set { lightSourceParams.Color = value; }
        }

        public float CurrentBrightness
        {
            get;
            private set;
        }

        public float Range
        {
            get { return lightSourceParams.Range; }
            set
            {

                lightSourceParams.Range = value;
                if (Math.Abs(prevCalculatedRange - lightSourceParams.Range) < 10.0f) return;

                HullsUpToDate.Clear();
                NeedsRecalculation = true;
                prevCalculatedRange = lightSourceParams.Range;
            }
        }

        public float Priority;

        public float PriorityMultiplier = 1.0f;

        private Vector2 lightTextureTargetSize;

        public Vector2 LightTextureTargetSize
        {
            get => lightTextureTargetSize;
            set
            {
                NeedsRecalculation = true;
                lightTextureTargetSize = value;
                HullsUpToDate.Clear();
            }
        }

        public Vector2 LightTextureOffset { get; set; }
        public Vector2 LightTextureScale { get; set; } = Vector2.One;

        public float TextureRange
        {
            get
            {
                return lightSourceParams.TextureRange;
            }
        }

        /// <summary>
        /// Background lights are drawn behind submarines and they don't cast shadows.
        /// </summary>
        public bool IsBackground
        {
            get;
            set;
        }

        public PhysicsBody ParentBody
        {
            get;
            set;
        }

        public DeformableSprite DeformableLightSprite
        {
            get;
            private set;
        }

        public bool Enabled = true;

        private readonly ISerializableEntity conditionalTarget;
        private readonly PropertyConditional.Comparison comparison;
        private readonly List<PropertyConditional> conditionals = new List<PropertyConditional>();

        public LightSource(ContentXElement element, ISerializableEntity conditionalTarget = null)
            : this(Vector2.Zero, 100.0f, Color.White, null)
        {
            lightSourceParams = new LightSourceParams(element);
            CastShadows = element.GetAttributeBool("castshadows", true);
            string comparison = element.GetAttributeString("comparison", null);
            if (comparison != null)
            {
                Enum.TryParse(comparison, ignoreCase: true, out this.comparison);
            }

            if (lightSourceParams.DeformableLightSpriteElement != null)
            {
                DeformableLightSprite = new DeformableSprite(lightSourceParams.DeformableLightSpriteElement, invert: true);
            }

            this.conditionalTarget = conditionalTarget;
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "conditional":
                        foreach (XAttribute attribute in subElement.Attributes())
                        {
                            if (PropertyConditional.IsValid(attribute))
                            {
                                conditionals.Add(new PropertyConditional(attribute));
                            }
                        }
                        break;
                }
            }
        }

        public LightSource(LightSourceParams lightSourceParams)
            : this(Vector2.Zero, 100.0f, Color.White, null)
        {
            this.lightSourceParams = lightSourceParams;
            lightSourceParams.Persistent = true;
            if (lightSourceParams.DeformableLightSpriteElement != null)
            {
                DeformableLightSprite = new DeformableSprite(lightSourceParams.DeformableLightSpriteElement, invert: true);
            }
        }

        public LightSource(Vector2 position, float range, Color color, Submarine submarine, bool addLight=true)
        {
            convexHullsInRange = new List<ConvexHullList>();
            this.ParentSub = submarine;
            this.position = position;
            lightSourceParams = new LightSourceParams(range, color);
            CastShadows = true;
            texture = LightTexture;
            diffToSub = new Dictionary<Submarine, Vector2>();
            if (addLight) { GameMain.LightManager.AddLight(this); }
        }

        public void Update(float time)
        {
            float brightness = 1.0f;
            if (lightSourceParams.BlinkFrequency > 0.0f)
            {
                float blinkTimer = (time * lightSourceParams.BlinkFrequency) % 1.0f;
                if (blinkTimer > 0.5f)
                {
                    CurrentBrightness = 0.0f;
                    return;
                }
            }
            if (lightSourceParams.PulseFrequency > 0.0f && lightSourceParams.PulseAmount > 0.0f)
            {
                float pulseState = (time * lightSourceParams.PulseFrequency) % 1.0f;
                //oscillate between 0-1
                brightness *= 1.0f - (float)(Math.Sin(pulseState * MathHelper.TwoPi) + 1.0f) / 2.0f * lightSourceParams.PulseAmount;
            }
            if (lightSourceParams.Flicker > 0.0f && lightSourceParams.FlickerSpeed > 0.0f)
            {
                float flickerState = (time * lightSourceParams.FlickerSpeed) % 255;
                brightness *= 1.0f - PerlinNoise.GetPerlin(flickerState, flickerState * 0.5f) * lightSourceParams.Flicker;
            }
            CurrentBrightness = brightness;
        }

        /// <summary>
        /// Update the contents of ConvexHullList and check if we need to recalculate vertices
        /// </summary>
        private void RefreshConvexHullList(ConvexHullList chList, Vector2 lightPos, Submarine sub)
        {
            var fullChList = ConvexHull.HullLists.FirstOrDefault(chList => chList.Submarine == sub);
            if (fullChList == null) { return; }

            chList.List.Clear();
            foreach (var convexHull in fullChList.List)
            {
                if (!convexHull.Enabled) { continue; }
                if (!MathUtils.CircleIntersectsRectangle(lightPos, TextureRange, convexHull.BoundingBox)) { continue; }
                chList.List.Add(convexHull);
            }
            chList.IsHidden.RemoveWhere(ch => !chList.List.Contains(ch));
            HullsUpToDate.Add(sub);    
        }

        /// <summary>
        /// Recheck which convex hulls are in range (if needed),
        /// and check if we need to recalculate vertices due to changes in the convex hulls
        /// </summary>
        private void CheckConvexHullsInRange()
        {
            foreach (Submarine sub in Submarine.Loaded)
            {
                CheckHullsInRange(sub);
            }
            //check convex hulls that aren't in any sub
            CheckHullsInRange(null);
        }

        private void CheckHullsInRange(Submarine sub)
        {
            //find the list of convexhulls that belong to the sub
            ConvexHullList chList = convexHullsInRange.FirstOrDefault(chList => chList.Submarine == sub);
            
            //not found -> create one
            if (chList == null)
            {
                chList = new ConvexHullList(sub);
                convexHullsInRange.Add(chList);
                NeedsRecalculation = true;
            }

            foreach (var ch in chList.List)
            {
                if (ch.LastVertexChangeTime > LastRecalculationTime && !chList.IsHidden.Contains(ch))
                {
                    NeedsRecalculation = true;
                    break;
                }
            }

            Vector2 lightPos = position;
            if (ParentSub == null)
            {
                //light and the convexhulls are both outside
                if (sub == null)
                {
                    if (!HullsUpToDate.Contains(null))
                    {
                        RefreshConvexHullList(chList, lightPos, null);
                    }
                }
                //light is outside, convexhulls inside a sub
                else
                {
                    lightPos -= sub.Position;

                    Rectangle subBorders = sub.Borders;
                    subBorders.Location += sub.HiddenSubPosition.ToPoint() - new Point(0, sub.Borders.Height);

                    //only draw if the light overlaps with the sub
                    if (!MathUtils.CircleIntersectsRectangle(lightPos, TextureRange, subBorders))
                    {
                        if (chList.List.Count > 0) { NeedsRecalculation = true; }
                        chList.List.Clear();
                        return;
                    }

                    RefreshConvexHullList(chList, lightPos, sub);
                }
            }
            else
            {
                //light is inside, convexhull outside
                if (sub == null) { return; }

                //light and convexhull are both inside the same sub
                if (sub == ParentSub)
                {
                    if (!HullsUpToDate.Contains(sub))
                    {
                        RefreshConvexHullList(chList, lightPos, sub);
                    }
                }
                //light and convexhull are inside different subs
                else
                {
                    if (sub.DockedTo.Contains(ParentSub) && HullsUpToDate.Contains(sub)) { return; }

                    lightPos -= (sub.Position - ParentSub.Position);

                    Rectangle subBorders = sub.Borders;
                    subBorders.Location += sub.HiddenSubPosition.ToPoint() - new Point(0, sub.Borders.Height);

                    //don't draw any shadows if the light doesn't overlap with the borders of the sub
                    if (!MathUtils.CircleIntersectsRectangle(lightPos, TextureRange, subBorders))
                    {
                        if (chList.List.Count > 0) { NeedsRecalculation = true; }
                        chList.List.Clear();
                        return;
                    }

                    //recalculate vertices if the subs have moved > 5 px relative to each other
                    Vector2 diff = ParentSub.WorldPosition - sub.WorldPosition;
                    if (!diffToSub.TryGetValue(sub, out Vector2 prevDiff))
                    {
                        diffToSub.Add(sub, diff);
                        NeedsRecalculation = true;
                    }
                    else if (Vector2.DistanceSquared(diff, prevDiff) > 5.0f * 5.0f)
                    {
                        diffToSub[sub] = diff;
                        NeedsRecalculation = true;
                    }

                    RefreshConvexHullList(chList, lightPos, sub);
                }
            }            
        }

        private static readonly List<Segment> visibleSegments = new List<Segment>();
        private static readonly List<SegmentPoint> points = new List<SegmentPoint>();
        private static readonly List<Vector2> output = new List<Vector2>();
        private static readonly SegmentPoint[] boundaryCorners = new SegmentPoint[4];
        private List<Vector2> FindRaycastHits()
        {
            if (!CastShadows || Range < 1.0f || Color.A < 1) { return null; }

            Vector2 drawPos = position;
            if (ParentSub != null) { drawPos += ParentSub.DrawPosition; }

            visibleSegments.Clear();
            foreach (ConvexHullList chList in convexHullsInRange)
            {
                foreach (ConvexHull hull in chList.List)
                {
                    if (!chList.IsHidden.Contains(hull)) 
                    {
                        //find convexhull segments that are close enough and facing towards the light source
                        hull.RefreshWorldPositions();
                        hull.GetVisibleSegments(drawPos, visibleSegments, ignoreEdges: false);                  
                    }
                }
                foreach (ConvexHull hull in chList.List)
                {
                    chList.IsHidden.Add(hull);
                }
            }

            //add a square-shaped boundary to make sure we've got something to construct the triangles from
            //even if there aren't enough hull segments around the light source

            //(might be more effective to calculate if we actually need these extra points)

            Vector2 drawOffset = Vector2.Zero;
            float boundsExtended = TextureRange;
            if (OverrideLightTexture != null)
            {
                float cosAngle = (float)Math.Cos(Rotation);
                float sinAngle = -(float)Math.Sin(Rotation);

                var overrideTextureDims = new Vector2(OverrideLightTexture.SourceRect.Width, OverrideLightTexture.SourceRect.Height);

                Vector2 origin = OverrideLightTextureOrigin;

                origin /= Math.Max(overrideTextureDims.X, overrideTextureDims.Y);
                origin -= Vector2.One * 0.5f;

                if (Math.Abs(origin.X) >= 0.45f || Math.Abs(origin.Y) >= 0.45f)
                {
                    boundsExtended += 5.0f;
                }

                origin *= TextureRange;

                drawOffset.X = -origin.X * cosAngle - origin.Y * sinAngle;
                drawOffset.Y = origin.X * sinAngle + origin.Y * cosAngle;
            }

            Vector2 boundsMin = drawPos + drawOffset + new Vector2(-boundsExtended, -boundsExtended);
            Vector2 boundsMax = drawPos + drawOffset + new Vector2(boundsExtended, boundsExtended);
            boundaryCorners[0] = new SegmentPoint(boundsMax, null);
            boundaryCorners[1] = new SegmentPoint(new Vector2(boundsMax.X, boundsMin.Y), null);
            boundaryCorners[2] = new SegmentPoint(boundsMin, null);
            boundaryCorners[3] = new SegmentPoint(new Vector2(boundsMin.X, boundsMax.Y), null);

            for (int i = 0; i < 4; i++)
            {
                var s = new Segment(boundaryCorners[i], boundaryCorners[(i + 1) % 4], null);
                visibleSegments.Add(s);
            }

            //Generate new points at the intersections between segments
            //This is necessary for the light volume to generate properly on some subs
            for (int i = 0; i < visibleSegments.Count; i++)
            {
                Vector2 p1a = visibleSegments[i].Start.WorldPos;
                Vector2 p1b = visibleSegments[i].End.WorldPos;

                for (int j = i + 1; j < visibleSegments.Count; j++)
                {
                    //ignore intersections between parallel axis-aligned segments
                    if (visibleSegments[i].IsAxisAligned && visibleSegments[j].IsAxisAligned &&
                        visibleSegments[i].IsHorizontal == visibleSegments[j].IsHorizontal)
                    {
                        continue;
                    }

                    Vector2 p2a = visibleSegments[j].Start.WorldPos;
                    Vector2 p2b = visibleSegments[j].End.WorldPos;

                    if (Vector2.DistanceSquared(p1a, p2a) < 5.0f ||
                        Vector2.DistanceSquared(p1a, p2b) < 5.0f ||
                        Vector2.DistanceSquared(p1b, p2a) < 5.0f ||
                        Vector2.DistanceSquared(p1b, p2b) < 5.0f)
                    {
                        continue;
                    }

                    bool intersects;
                    Vector2 intersection = Vector2.Zero;
                    if (visibleSegments[i].IsAxisAligned)
                    {
                        intersects = MathUtils.GetAxisAlignedLineIntersection(p2a, p2b, p1a, p1b, visibleSegments[i].IsHorizontal, out intersection);
                    }
                    else if (visibleSegments[j].IsAxisAligned)
                    {
                        intersects = MathUtils.GetAxisAlignedLineIntersection(p1a, p1b, p2a, p2b, visibleSegments[j].IsHorizontal, out intersection);
                    }
                    else
                    {
                        intersects = MathUtils.GetLineIntersection(p1a, p1b, p2a, p2b, out intersection);
                    }

                    if (intersects)
                    {
                        SegmentPoint start = visibleSegments[i].Start;
                        SegmentPoint end = visibleSegments[i].End;
                        SegmentPoint mid = new SegmentPoint(intersection, null);
                        if (visibleSegments[i].ConvexHull?.ParentEntity?.Submarine != null)
                        {
                            mid.Pos -= visibleSegments[i].ConvexHull.ParentEntity.Submarine.DrawPosition;
                        }

                        if (Vector2.DistanceSquared(start.WorldPos, mid.WorldPos) < 5.0f ||
                            Vector2.DistanceSquared(end.WorldPos, mid.WorldPos) < 5.0f)
                        {
                            continue;
                        }

                        Segment seg1 = new Segment(start, mid, visibleSegments[i].ConvexHull)
                        {
                            IsHorizontal = visibleSegments[i].IsHorizontal,
                        };

                        Segment seg2 = new Segment(mid, end, visibleSegments[i].ConvexHull)
                        {
                            IsHorizontal = visibleSegments[i].IsHorizontal
                        };

                        visibleSegments[i] = seg1;
                        visibleSegments.Insert(i + 1, seg2);
                        i--;
                        break;
                    }
                }
            }

            points.Clear();
            //remove segments that fall out of bounds
            for (int i = 0; i < visibleSegments.Count; i++)
            {
                Segment s = visibleSegments[i];
                if (Math.Abs(s.Start.WorldPos.X - drawPos.X - drawOffset.X) > boundsExtended + 1.0f ||
                    Math.Abs(s.Start.WorldPos.Y - drawPos.Y - drawOffset.Y) > boundsExtended + 1.0f ||
                    Math.Abs(s.End.WorldPos.X - drawPos.X - drawOffset.X) > boundsExtended + 1.0f ||
                    Math.Abs(s.End.WorldPos.Y - drawPos.Y - drawOffset.Y) > boundsExtended + 1.0f)
                {
                    visibleSegments.RemoveAt(i);
                    i--;
                }
                else
                {
                    points.Add(s.Start);
                    points.Add(s.End);
                }
            }

            //remove points that are very close to each other
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = Math.Min(i + 4, points.Count-1); j > i; j--)
                {
                    if (Math.Abs(points[i].WorldPos.X - points[j].WorldPos.X) < 6 &&
                        Math.Abs(points[i].WorldPos.Y - points[j].WorldPos.Y) < 6)
                    {
                        points.RemoveAt(j);
                    }
                }
            }

            var compareCCW = new CompareSegmentPointCW(drawPos);
            try
            {
                points.Sort(compareCCW);
            }
            catch (Exception e)
            {
                StringBuilder sb = new StringBuilder("Constructing light volumes failed! Light pos: " + drawPos + ", Hull verts:\n");
                foreach (SegmentPoint sp in points)
                {
                    sb.AppendLine(sp.Pos.ToString());
                }
                DebugConsole.ThrowError(sb.ToString(), e);
            }
                        
            visibleSegments.Sort((s1, s2) => 
                MathUtils.LineToPointDistanceSquared(s1.Start.WorldPos, s1.End.WorldPos, drawPos)
                .CompareTo(MathUtils.LineToPointDistanceSquared(s2.Start.WorldPos, s2.End.WorldPos, drawPos)));

            output.Clear();
            foreach (SegmentPoint p in points)
            {
                Vector2 dir = Vector2.Normalize(p.WorldPos - drawPos);
                Vector2 dirNormal = new Vector2(-dir.Y, dir.X) * 3;

                //do two slightly offset raycasts to hit the segment itself and whatever's behind it
                var intersection1 = RayCast(drawPos, drawPos + dir * boundsExtended * 2 - dirNormal, visibleSegments);
                if (intersection1.index < 0) { return null; }
                var intersection2 = RayCast(drawPos, drawPos + dir * boundsExtended * 2 + dirNormal, visibleSegments);
                if (intersection2.index < 0) { return null; }

                Segment seg1 = visibleSegments[intersection1.index];
                Segment seg2 = visibleSegments[intersection2.index];

                bool isPoint1 = MathUtils.LineToPointDistanceSquared(seg1.Start.WorldPos, seg1.End.WorldPos, p.WorldPos) < 25.0f;
                bool isPoint2 = MathUtils.LineToPointDistanceSquared(seg2.Start.WorldPos, seg2.End.WorldPos, p.WorldPos) < 25.0f;

                if (isPoint1 && isPoint2)
                {
                    //hit at the current segmentpoint -> place the segmentpoint into the list
                    output.Add(p.WorldPos);

                    foreach (ConvexHullList hullList in convexHullsInRange)
                    {
                        hullList.IsHidden.Remove(p.ConvexHull);
                        hullList.IsHidden.Remove(seg1.ConvexHull);
                        hullList.IsHidden.Remove(seg2.ConvexHull);
                    }
                }
                else if (intersection1.index != intersection2.index)
                {
                    //the raycasts landed on different segments
                    //we definitely want to generate new geometry here
                    output.Add(isPoint1 ? p.WorldPos : intersection1.pos);
                    output.Add(isPoint2 ? p.WorldPos : intersection2.pos);

                    foreach (ConvexHullList hullList in convexHullsInRange)
                    {
                        hullList.IsHidden.Remove(p.ConvexHull);
                        hullList.IsHidden.Remove(seg1.ConvexHull);
                        hullList.IsHidden.Remove(seg2.ConvexHull);
                    }
                }
                //if neither of the conditions above are met, we just assume
                //that the raycasts both resulted on the same segment
                //and creating geometry here would be wasteful
            }

            //remove points that are very close to each other
            for (int i = 0; i < output.Count - 1; i++)
            {
                for (int j = Math.Min(i + 4, output.Count - 1); j > i; j--)
                {
                    if (Math.Abs(output[i].X - output[j].X) < 6 &&
                       Math.Abs(output[i].Y - output[j].Y) < 6)
                    {
                        output.RemoveAt(j);
                    }
                }
            }

            return output;
        }

        private static (int index, Vector2 pos) RayCast(Vector2 rayStart, Vector2 rayEnd, List<Segment> segments)
        {
            Vector2? closestIntersection = null;
            int segment = -1;

            float minX = Math.Min(rayStart.X, rayEnd.X);
            float maxX = Math.Max(rayStart.X, rayEnd.X);
            float minY = Math.Min(rayStart.Y, rayEnd.Y);
            float maxY = Math.Max(rayStart.Y, rayEnd.Y);

            for (int i = 0; i < segments.Count; i++)
            {
                Segment s = segments[i];

                //segment's end position always has a higher or equal y coordinate than the start position
                //so we can do this comparison and skip segments that are at the wrong side of the ray
                /*if (s.End.WorldPos.Y < s.Start.WorldPos.Y)
                {
                    System.Diagnostics.Debug.Assert(s.End.WorldPos.Y >= s.Start.WorldPos.Y,
                    "LightSource raycast failed. Segment's end positions should never be below the start position. Parent entity: " + (s.ConvexHull?.ParentEntity == null ? "null" : s.ConvexHull.ParentEntity.ToString()));
                }*/
                if (s.Start.WorldPos.Y > maxY || s.End.WorldPos.Y < minY) { continue; }
                //same for the x-axis
                if (s.Start.WorldPos.X > s.End.WorldPos.X)
                {
                    if (s.Start.WorldPos.X < minX) { continue; }
                    if (s.End.WorldPos.X > maxX) { continue; }
                }
                else
                {
                    if (s.End.WorldPos.X < minX) { continue; }
                    if (s.Start.WorldPos.X > maxX) { continue; }
                }

                bool intersects;
                Vector2 intersection;
                if (s.IsAxisAligned)
                {
                    intersects = MathUtils.GetAxisAlignedLineIntersection(rayStart, rayEnd, s.Start.WorldPos, s.End.WorldPos, s.IsHorizontal, out intersection);
                }
                else
                {
                    intersects = MathUtils.GetLineIntersection(rayStart, rayEnd, s.Start.WorldPos, s.End.WorldPos, out intersection);
                }

                if (intersects)
                {
                    closestIntersection = intersection;

                    rayEnd = intersection;
                    minX = Math.Min(rayStart.X, rayEnd.X);
                    maxX = Math.Max(rayStart.X, rayEnd.X);
                    minY = Math.Min(rayStart.Y, rayEnd.Y);
                    maxY = Math.Max(rayStart.Y, rayEnd.Y);

                    segment = i;
                }
            }

            return (segment, closestIntersection == null ? rayEnd : (Vector2)closestIntersection);
        }


        private void CalculateLightVertices(List<Vector2> rayCastHits)
        {
            vertexCount = rayCastHits.Count * 2 + 1;
            indexCount = (rayCastHits.Count) * 9;

            //recreate arrays if they're too small or excessively large
            if (vertices == null || vertices.Length < vertexCount || vertices.Length > vertexCount * 3)
            {
                vertices = new VertexPositionColorTexture[vertexCount];
                indices = new short[indexCount];
            }

            Vector2 drawPos = position;
            if (ParentSub != null) { drawPos += ParentSub.DrawPosition; }

            float cosAngle = (float)Math.Cos(Rotation);
            float sinAngle = -(float)Math.Sin(Rotation);

            Vector2 uvOffset = Vector2.Zero;
            Vector2 overrideTextureDims = Vector2.One;
            if (OverrideLightTexture != null)
            {
                overrideTextureDims = new Vector2(OverrideLightTexture.SourceRect.Width, OverrideLightTexture.SourceRect.Height);

                Vector2 origin = OverrideLightTextureOrigin;
                if (LightSpriteEffect == SpriteEffects.FlipHorizontally)
                {
                    origin.X = OverrideLightTexture.SourceRect.Width - origin.X;
                    cosAngle = -cosAngle;
                    sinAngle = -sinAngle;
                }
                if (LightSpriteEffect == SpriteEffects.FlipVertically) { origin.Y = OverrideLightTexture.SourceRect.Height - origin.Y; }
                uvOffset = (origin / overrideTextureDims) - new Vector2(0.5f, 0.5f);
            }

            // Add a vertex for the center of the mesh
            vertices[0] = new VertexPositionColorTexture(new Vector3(position.X, position.Y, 0),
                Color.White, GetUV(new Vector2(0.5f, 0.5f) + uvOffset, LightSpriteEffect));

            //hacky fix to exc excessively large light volumes (they used to be up to 4x the range of the light if there was nothing to block the rays).
            //might want to tweak the raycast logic in a way that this isn't necessary
            /*float boundRadius = Range * 1.1f / (1.0f - Math.Max(Math.Abs(uvOffset.X), Math.Abs(uvOffset.Y)));
            Rectangle boundArea = new Rectangle((int)(drawPos.X - boundRadius), (int)(drawPos.Y + boundRadius), (int)(boundRadius * 2), (int)(boundRadius * 2));
            for (int i = 0; i < rayCastHits.Count; i++)
            {
                if (MathUtils.GetLineRectangleIntersection(drawPos, rayCastHits[i], boundArea, out Vector2 intersection))
                {
                    rayCastHits[i] = intersection;
                }
            }*/

            // Add all the other encounter points as vertices
            // storing their world position as UV coordinates
            for (int i = 0; i < rayCastHits.Count; i++)
            {
                Vector2 vertex = rayCastHits[i];

                //we'll use the previous and next vertices to calculate the normals
                //of the two segments this vertex belongs to
                //so we can add new vertices based on these normals
                Vector2 prevVertex = rayCastHits[i > 0 ? i - 1 : rayCastHits.Count - 1];
                Vector2 nextVertex = rayCastHits[i < rayCastHits.Count - 1 ? i + 1 : 0];

                Vector2 rawDiff = vertex - drawPos;

                //calculate normal of first segment
                Vector2 nDiff1 = vertex - nextVertex;
                float tx = nDiff1.X; nDiff1.X = -nDiff1.Y; nDiff1.Y = tx;
                nDiff1 /= Math.Max(Math.Abs(nDiff1.X), Math.Abs(nDiff1.Y));
                //if the normal is pointing towards the light origin
                //rather than away from it, invert it
                if (Vector2.DistanceSquared(nDiff1, rawDiff) > Vector2.DistanceSquared(-nDiff1, rawDiff)) nDiff1 = -nDiff1;

                //calculate normal of second segment
                Vector2 nDiff2 = prevVertex - vertex;
                tx = nDiff2.X; nDiff2.X = -nDiff2.Y; nDiff2.Y = tx;
                nDiff2 /= Math.Max(Math.Abs(nDiff2.X),Math.Abs(nDiff2.Y));
                //if the normal is pointing towards the light origin
                //rather than away from it, invert it
                if (Vector2.DistanceSquared(nDiff2, rawDiff) > Vector2.DistanceSquared(-nDiff2, rawDiff)) nDiff2 = -nDiff2;

                //add the normals together and use some magic numbers to create
                //a somewhat useful/good-looking blur
                Vector2 nDiff = nDiff1 * 40.0f;
                if (MathUtils.GetLineIntersection(vertex + (nDiff1 * 40.0f), nextVertex + (nDiff1 * 40.0f), vertex + (nDiff2 * 40.0f), prevVertex + (nDiff2 * 40.0f), true, out Vector2 intersection))
                {
                    nDiff = intersection - vertex;
                    if (nDiff.LengthSquared() > 10000.0f)
                    {
                        nDiff /= Math.Max(Math.Abs(nDiff.X), Math.Abs(nDiff.Y)); nDiff *= 100.0f;
                    }
                }

                Vector2 diff = rawDiff;
                diff /= Range * 2.0f;
                if (OverrideLightTexture != null)
                {
                    //calculate texture coordinates based on the light's rotation
                    Vector2 originDiff = diff;

                    diff.X = originDiff.X * cosAngle - originDiff.Y * sinAngle;
                    diff.Y = originDiff.X * sinAngle + originDiff.Y * cosAngle;
                    diff *= (overrideTextureDims / OverrideLightTexture.size);// / (1.0f - Math.Max(Math.Abs(uvOffset.X), Math.Abs(uvOffset.Y)));
                    diff += uvOffset;
                }

                //finally, create the vertices
                VertexPositionColorTexture fullVert = new VertexPositionColorTexture(new Vector3(position.X + rawDiff.X, position.Y + rawDiff.Y, 0),
                   Color.White, GetUV(new Vector2(0.5f, 0.5f) + diff, LightSpriteEffect));
                VertexPositionColorTexture fadeVert = new VertexPositionColorTexture(new Vector3(position.X + rawDiff.X + nDiff.X, position.Y + rawDiff.Y + nDiff.Y, 0),
                   Color.White * 0.0f, GetUV(new Vector2(0.5f, 0.5f) + diff, LightSpriteEffect));

                vertices[1 + i * 2] = fullVert;
                vertices[1 + i * 2 + 1] = fadeVert;
            }

            // Compute the indices to form triangles
            for (int i = 0; i < rayCastHits.Count - 1; i++)
            {
                //main light body
                indices[i * 9] = 0;
                indices[i * 9 + 1] = (short)((i * 2 + 3) % vertexCount);
                indices[i * 9 + 2] = (short)((i * 2 + 1) % vertexCount);

                //faded light
                indices[i * 9 + 3] = (short)((i * 2 + 1) % vertexCount);
                indices[i * 9 + 4] = (short)((i * 2 + 3) % vertexCount);
                indices[i * 9 + 5] = (short)((i * 2 + 4) % vertexCount);

                indices[i * 9 + 6] = (short)((i * 2 + 2) % vertexCount);
                indices[i * 9 + 7] = (short)((i * 2 + 1) % vertexCount);
                indices[i * 9 + 8] = (short)((i * 2 + 4) % vertexCount);
            }

            //main light body
            indices[(rayCastHits.Count - 1) * 9] = 0;
            indices[(rayCastHits.Count - 1) * 9 + 1] = (short)(1);
            indices[(rayCastHits.Count - 1) * 9 + 2] = (short)(vertexCount - 2);

            //faded light
            indices[(rayCastHits.Count - 1) * 9 + 3] = (short)(1);
            indices[(rayCastHits.Count - 1) * 9 + 4] = (short)(vertexCount - 1);
            indices[(rayCastHits.Count - 1) * 9 + 5] = (short)(vertexCount - 2);

            indices[(rayCastHits.Count - 1) * 9 + 6] = (short)(1);
            indices[(rayCastHits.Count - 1) * 9 + 7] = (short)(2);
            indices[(rayCastHits.Count - 1) * 9 + 8] = (short)(vertexCount - 1);

            //TODO: a better way to determine the size of the vertex buffer and handle changes in size?
            //now we just create a buffer for 64 verts and make it larger if needed
            if (lightVolumeBuffer == null)
            {
                lightVolumeBuffer = new DynamicVertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, Math.Max(64, (int)(vertexCount * 1.5)), BufferUsage.None);
                lightVolumeIndexBuffer = new DynamicIndexBuffer(GameMain.Instance.GraphicsDevice, typeof(short), Math.Max(64 * 3, (int)(indexCount * 1.5)), BufferUsage.None);
            }
            else if (vertexCount > lightVolumeBuffer.VertexCount || indexCount > lightVolumeIndexBuffer.IndexCount)
            {
                lightVolumeBuffer.Dispose();
                lightVolumeIndexBuffer.Dispose();

                lightVolumeBuffer = new DynamicVertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, (int)(vertexCount * 1.5), BufferUsage.None);
                lightVolumeIndexBuffer = new DynamicIndexBuffer(GameMain.Instance.GraphicsDevice, typeof(short), (int)(indexCount * 1.5), BufferUsage.None);
            }

            lightVolumeBuffer.SetData<VertexPositionColorTexture>(vertices, 0, vertexCount);
            lightVolumeIndexBuffer.SetData<short>(indices, 0, indexCount);

            static Vector2 GetUV(Vector2 vert, SpriteEffects effects)
            {
                if (effects == SpriteEffects.FlipHorizontally)
                {
                    vert.X = 1.0f - vert.X;
                }
                else if (effects == SpriteEffects.FlipVertically)
                {
                    vert.Y = 1.0f - vert.Y;
                }
                else if (effects == (SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically))
                {
                    vert.X = 1.0f - vert.X;
                    vert.Y = 1.0f - vert.Y;
                }
                vert.Y = 1.0f - vert.Y;
                return vert;
            }

            translateVertices = Vector2.Zero;
            rotateVertices = 0.0f;
            prevCalculatedPosition = position;
            prevCalculatedRotation = rotation;
        }

        /// <summary>
        /// Draws the optional "light sprite", just a simple sprite with no shadows
        /// </summary>
        /// <param name="spriteBatch"></param>
        public void DrawSprite(SpriteBatch spriteBatch, Camera cam)
        {
            if (GameMain.DebugDraw)
            {
                Vector2 drawPos = position;
                if (ParentSub != null)
                {
                    drawPos += ParentSub.DrawPosition;
                }
                drawPos.Y = -drawPos.Y;

                float cosAngle = (float)Math.Cos(Rotation);
                float sinAngle = -(float)Math.Sin(Rotation);

                float bounds = TextureRange;

                if (OverrideLightTexture != null)
                {
                    var overrideTextureDims = new Vector2(OverrideLightTexture.SourceRect.Width, OverrideLightTexture.SourceRect.Height);

                    Vector2 origin = OverrideLightTextureOrigin;

                    origin /= Math.Max(overrideTextureDims.X, overrideTextureDims.Y);
                    origin *= TextureRange;

                    drawPos.X += origin.X * sinAngle + origin.Y * cosAngle;
                    drawPos.Y += origin.X * cosAngle + origin.Y * sinAngle;
                }

                //add a square-shaped boundary to make sure we've got something to construct the triangles from
                //even if there aren't enough hull segments around the light source

                //(might be more effective to calculate if we actually need these extra points)
                var boundaryCorners = new SegmentPoint[] {
                    new SegmentPoint(new Vector2(drawPos.X + bounds, drawPos.Y + bounds), null),
                    new SegmentPoint(new Vector2(drawPos.X + bounds, drawPos.Y - bounds), null),
                    new SegmentPoint(new Vector2(drawPos.X - bounds, drawPos.Y - bounds), null),
                    new SegmentPoint(new Vector2(drawPos.X - bounds, drawPos.Y + bounds), null)
                };

                for (int i = 0; i < 4; i++)
                {
                    GUI.DrawLine(spriteBatch, boundaryCorners[i].Pos, boundaryCorners[(i + 1) % 4].Pos, Color.White, 0, 3);
                }
            }

            if (DeformableLightSprite != null)
            {
                Vector2 origin = DeformableLightSprite.Origin + LightSourceParams.GetOffset();
                Vector2 drawPos = position;
                if (ParentSub != null)
                {
                    drawPos += ParentSub.DrawPosition;
                }
                if (LightSpriteEffect == SpriteEffects.FlipHorizontally)
                {
                    origin.X = DeformableLightSprite.Sprite.SourceRect.Width - origin.X;
                }
                if (LightSpriteEffect == SpriteEffects.FlipVertically)
                {
                    origin.Y = DeformableLightSprite.Sprite.SourceRect.Height - origin.Y;
                }

                DeformableLightSprite.Draw(
                    cam, new Vector3(drawPos, 0.0f),
                    origin, -Rotation + MathHelper.ToRadians(LightSourceParams.Rotation), SpriteScale,
                    new Color(Color, (lightSourceParams.OverrideLightSpriteAlpha ?? Color.A / 255.0f) * CurrentBrightness),
                    LightSpriteEffect == SpriteEffects.FlipVertically);
            }

            if (LightSprite != null)
            {
                Vector2 origin = LightSprite.Origin + LightSourceParams.GetOffset();
                if ((LightSpriteEffect & SpriteEffects.FlipHorizontally) == SpriteEffects.FlipHorizontally)
                {
                    origin.X = LightSprite.SourceRect.Width - origin.X;
                }
                if ((LightSpriteEffect & SpriteEffects.FlipVertically) == SpriteEffects.FlipVertically)
                {
                    origin.Y = LightSprite.SourceRect.Height - origin.Y;
                }

                Vector2 drawPos = position;
                if (ParentSub != null)
                {
                    drawPos += ParentSub.DrawPosition;
                }
                drawPos.Y = -drawPos.Y;

                Color color = new Color(Color, (lightSourceParams.OverrideLightSpriteAlpha ?? Color.A / 255.0f) * CurrentBrightness);

                if (LightTextureTargetSize != Vector2.Zero)
                {
                    LightSprite.DrawTiled(spriteBatch, drawPos, LightTextureTargetSize, color, startOffset: LightTextureOffset, textureScale: LightTextureScale);
                }
                else
                {
                    LightSprite.Draw(
                        spriteBatch, drawPos,
                        color,
                        origin, -Rotation + MathHelper.ToRadians(LightSourceParams.Rotation), SpriteScale, LightSpriteEffect);
                }
            }

            if (GameMain.DebugDraw && Screen.Selected.Cam.Zoom > 0.1f)
            {
                Vector2 drawPos = position;
                if (ParentSub != null) { drawPos += ParentSub.DrawPosition; }
                drawPos.Y = -drawPos.Y;

                if (CastShadows && Screen.Selected == GameMain.SubEditorScreen)
                {
                    GUI.DrawRectangle(spriteBatch, drawPos - Vector2.One * 20, Vector2.One * 40, GUIStyle.Orange, isFilled: false);
                    GUI.DrawLine(spriteBatch, drawPos - Vector2.One * 20, drawPos + Vector2.One * 20, GUIStyle.Orange);
                    GUI.DrawLine(spriteBatch, drawPos - new Vector2(1.0f, -1.0f) * 20, drawPos + new Vector2(1.0f, -1.0f) * 20, GUIStyle.Orange);
                }

                //visualize light recalculations
                float timeSinceRecalculation = (float)Timing.TotalTime - LastRecalculationTime;
                if (timeSinceRecalculation < 0.1f)
                {
                    GUI.DrawRectangle(spriteBatch, drawPos - Vector2.One * 10, Vector2.One * 20, GUIStyle.Red * (1.0f - timeSinceRecalculation * 10.0f), isFilled: true);
                    GUI.DrawLine(spriteBatch, drawPos - Vector2.One * Range, drawPos + Vector2.One * Range, Color);
                    GUI.DrawLine(spriteBatch, drawPos - new Vector2(1.0f, -1.0f) * Range, drawPos + new Vector2(1.0f, -1.0f) * Range, Color);
                }
            }
        }

        public void CheckConditionals()
        {
            if (conditionals.None()) { return; }
            if (conditionalTarget == null) { return; }
            if (comparison == PropertyConditional.Comparison.And)
            {
                Enabled = conditionals.All(c => c.Matches(conditionalTarget));
            }
            else
            {
                Enabled = conditionals.Any(c => c.Matches(conditionalTarget));
            }
        }

        public void DrawLightVolume(SpriteBatch spriteBatch, BasicEffect lightEffect, Matrix transform, bool allowRecalculation, ref int recalculationCount)
        {
            if (Range < 1.0f || Color.A < 1 || CurrentBrightness <= 0.0f) { return; }

            //if the light doesn't cast shadows, we can simply render the texture without having to calculate the light volume
            if (!CastShadows)
            {
                Texture2D currentTexture = texture ?? LightTexture;
                if (OverrideLightTexture != null) { currentTexture = OverrideLightTexture.Texture; }

                Vector2 center = OverrideLightTexture == null ?
                    new Vector2(currentTexture.Width / 2, currentTexture.Height / 2) :
                    OverrideLightTexture.Origin;
                float scale = Range / (currentTexture.Width / 2.0f);

                Vector2 drawPos = position;
                if (ParentSub != null) { drawPos += ParentSub.DrawPosition; }
                drawPos.Y = -drawPos.Y;

                spriteBatch.Draw(currentTexture, drawPos, null, Color.Multiply(CurrentBrightness), -rotation + MathHelper.ToRadians(LightSourceParams.Rotation), center, scale, SpriteEffects.None, 1);
                return;
            }

            CheckConvexHullsInRange();

            if (NeedsRecalculation && allowRecalculation)
            {
                recalculationCount++;
                var verts = FindRaycastHits();
                if (verts == null)
                {
#if DEBUG
                    DebugConsole.ThrowError($"Failed to generate vertices for a light source. Range: {Range}, color: {Color}, brightness: {CurrentBrightness}, parent: {ParentBody?.UserData ?? "Unknown"}");
#endif
                    Enabled = false;
                    return;
                }

                CalculateLightVertices(verts);

                LastRecalculationTime = (float)Timing.TotalTime;
                NeedsRecalculation = false;
            }

            Vector2 offset = ParentSub == null ? Vector2.Zero : ParentSub.DrawPosition;
            lightEffect.World =
                Matrix.CreateTranslation(-new Vector3(position, 0.0f)) *
                Matrix.CreateRotationZ(rotateVertices - MathHelper.ToRadians(LightSourceParams.Rotation)) *
                Matrix.CreateTranslation(new Vector3(position + offset + translateVertices, 0.0f)) *
                transform;

            if (vertexCount == 0) { return; }

            lightEffect.DiffuseColor = (new Vector3(Color.R, Color.G, Color.B) * (Color.A / 255.0f * CurrentBrightness)) / 255.0f;
            if (OverrideLightTexture != null)
            {
                lightEffect.Texture = OverrideLightTexture.Texture;
            }
            else
            {
                lightEffect.Texture = texture ?? LightTexture;
            }
            lightEffect.CurrentTechnique.Passes[0].Apply();

            GameMain.Instance.GraphicsDevice.SetVertexBuffer(lightVolumeBuffer);
            GameMain.Instance.GraphicsDevice.Indices = lightVolumeIndexBuffer;

            GameMain.Instance.GraphicsDevice.DrawIndexedPrimitives
            (
                PrimitiveType.TriangleList, 0, 0, indexCount / 3
            );
        }

        public void Reset()
        {
            HullsUpToDate.Clear();
            convexHullsInRange.Clear();
            diffToSub.Clear();
            NeedsRecalculation = true;

            vertexCount = 0;
            if (lightVolumeBuffer != null)
            {
                lightVolumeBuffer.Dispose();
                lightVolumeBuffer = null;
            }

            indexCount = 0;
            if (lightVolumeIndexBuffer != null)
            {
                lightVolumeIndexBuffer.Dispose();
                lightVolumeIndexBuffer = null;
            }
        }

        public void Remove()
        {
            if (!lightSourceParams.Persistent)
            {
                LightSprite?.Remove();
                OverrideLightTexture?.Remove();
            }

            DeformableLightSprite?.Remove();
            DeformableLightSprite = null;

            lightVolumeBuffer?.Dispose();
            lightVolumeBuffer = null;

            lightVolumeIndexBuffer?.Dispose();
            lightVolumeIndexBuffer = null;

            GameMain.LightManager.RemoveLight(this);
        }
    }
}
