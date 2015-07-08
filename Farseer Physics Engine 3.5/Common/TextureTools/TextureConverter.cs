using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common.TextureTools
{
    // User contribution from Sickbattery aka David Reschke.

    /// <summary>
    /// The detection type affects the resulting polygon data.
    /// </summary>
    public enum VerticesDetectionType
    {
        /// <summary>
        /// Holes are integrated into the main polygon.
        /// </summary>
        Integrated = 0,

        /// <summary>
        /// The data of the main polygon and hole polygons is returned separately.
        /// </summary>
        Separated = 1
    }

    public sealed class TextureConverter
    {
        private const int ClosepixelsLength = 8;

        /// <summary>
        /// This array is ment to be readonly.
        /// It's not because it is accessed very frequently.
        /// </summary>
        private static int[,] _closePixels = new[,] { { -1, -1 }, { 0, -1 }, { 1, -1 }, { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 }, { -1, 0 } };

        private uint[] _data;
        private int _dataLength;
        private int _width;
        private int _height;

        private VerticesDetectionType _polygonDetectionType;

        private uint _alphaTolerance;
        private float _hullTolerance;

        private bool _holeDetection;
        private bool _multipartDetection;
        private bool _pixelOffsetOptimization;

        private Matrix _transform = Matrix.Identity;

        #region Properties
        /// <summary>
        /// Get or set the polygon detection type.
        /// </summary>
        public VerticesDetectionType PolygonDetectionType
        {
            get { return _polygonDetectionType; }
            set { _polygonDetectionType = value; }
        }

        /// <summary>
        /// Will detect texture 'holes' if set to true. Slows down the detection. Default is false.
        /// </summary>
        public bool HoleDetection
        {
            get { return _holeDetection; }
            set { _holeDetection = value; }
        }

        /// <summary>
        /// Will detect texture multiple 'solid' isles if set to true. Slows down the detection. Default is false.
        /// </summary>
        public bool MultipartDetection
        {
            get { return _multipartDetection; }
            set { _multipartDetection = value; }
        }

        /// <summary>
        /// Will optimize the vertex positions along the interpolated normal between two edges about a half pixel (post processing). Default is false.
        /// </summary>
        public bool PixelOffsetOptimization
        {
            get { return _pixelOffsetOptimization; }
            set { _pixelOffsetOptimization = value; }
        }

        /// <summary>
        /// Can be used for scaling.
        /// </summary>
        public Matrix Transform
        {
            get { return _transform; }
            set { _transform = value; }
        }

        /// <summary>
        /// Alpha (coverage) tolerance. Default is 20: Every pixel with a coverage value equal or greater to 20 will be counts as solid.
        /// </summary>
        public byte AlphaTolerance
        {
            get { return (byte)(_alphaTolerance >> 24); }
            set { _alphaTolerance = (uint)value << 24; }
        }

        /// <summary>
        /// Default is 1.5f.
        /// </summary>
        public float HullTolerance
        {
            get { return _hullTolerance; }
            set
            {
                if (value > 4f)
                {
                    _hullTolerance = 4f;
                }
                else if (value < 0.9f)
                {
                    _hullTolerance = 0.9f;
                }
                else
                {
                    _hullTolerance = value;
                }
            }
        }
        #endregion

        #region Constructors
        public TextureConverter()
        {
            Initialize(null, null, null, null, null, null, null, null);
        }

        public TextureConverter(byte? alphaTolerance, float? hullTolerance,
            bool? holeDetection, bool? multipartDetection, bool? pixelOffsetOptimization, Matrix? transform)
        {
            Initialize(null, null, alphaTolerance, hullTolerance, holeDetection,
                multipartDetection, pixelOffsetOptimization, transform);
        }

        public TextureConverter(uint[] data, int width)
        {
            Initialize(data, width, null, null, null, null, null, null);
        }

        public TextureConverter(uint[] data, int width, byte? alphaTolerance,
            float? hullTolerance, bool? holeDetection, bool? multipartDetection,
            bool? pixelOffsetOptimization, Matrix? transform)
        {
            Initialize(data, width, alphaTolerance, hullTolerance, holeDetection,
                multipartDetection, pixelOffsetOptimization, transform);
        }
        #endregion

        #region Initialization
        private void Initialize(uint[] data, int? width, byte? alphaTolerance,
            float? hullTolerance, bool? holeDetection, bool? multipartDetection,
            bool? pixelOffsetOptimization, Matrix? transform)
        {
            if (data != null && !width.HasValue)
                throw new ArgumentNullException("width", "'width' can't be null if 'data' is set.");

            if (data == null && width.HasValue)
                throw new ArgumentNullException("data", "'data' can't be null if 'width' is set.");

            if (data != null && width.HasValue)
                SetTextureData(data, width.Value);

            if (alphaTolerance.HasValue)
                AlphaTolerance = alphaTolerance.Value;
            else
                AlphaTolerance = 20;

            if (hullTolerance.HasValue)
                HullTolerance = hullTolerance.Value;
            else
                HullTolerance = 1.5f;

            if (holeDetection.HasValue)
                HoleDetection = holeDetection.Value;
            else
                HoleDetection = false;

            if (multipartDetection.HasValue)
                MultipartDetection = multipartDetection.Value;
            else
                MultipartDetection = false;

            if (pixelOffsetOptimization.HasValue)
                PixelOffsetOptimization = pixelOffsetOptimization.Value;
            else
                PixelOffsetOptimization = false;

            if (transform.HasValue)
                Transform = transform.Value;
            else
                Transform = Matrix.Identity;
        }
        #endregion

        private void SetTextureData(uint[] data, int width)
        {
            if (data == null)
                throw new ArgumentNullException("data", "'data' can't be null.");

            if (data.Length < 4)
                throw new ArgumentOutOfRangeException("data", "'data' length can't be less then 4. Your texture must be at least 2 x 2 pixels in size.");

            if (width < 2)
                throw new ArgumentOutOfRangeException("width", "'width' can't be less then 2. Your texture must be at least 2 x 2 pixels in size.");

            if (data.Length % width != 0)
                throw new ArgumentException("'width' has an invalid value.");

            _data = data;
            _dataLength = _data.Length;
            _width = width;
            _height = _dataLength / width;
        }

        /// <summary>
        /// Detects the vertices of the supplied texture data. (PolygonDetectionType.Integrated)
        /// </summary>
        /// <param name="data">The texture data.</param>
        /// <param name="width">The texture width.</param>
        /// <returns></returns>
        public static Vertices DetectVertices(uint[] data, int width)
        {
            TextureConverter tc = new TextureConverter(data, width);

            List<Vertices> detectedVerticesList = tc.DetectVertices();

            return detectedVerticesList[0];
        }

        /// <summary>
        /// Detects the vertices of the supplied texture data.
        /// </summary>
        /// <param name="data">The texture data.</param>
        /// <param name="width">The texture width.</param>
        /// <param name="holeDetection">if set to <c>true</c> it will perform hole detection.</param>
        /// <returns></returns>
        public static Vertices DetectVertices(uint[] data, int width, bool holeDetection)
        {
            TextureConverter tc =
                new TextureConverter(data, width)
                {
                    HoleDetection = holeDetection
                };

            List<Vertices> detectedVerticesList = tc.DetectVertices();

            return detectedVerticesList[0];
        }

        /// <summary>
        /// Detects the vertices of the supplied texture data.
        /// </summary>
        /// <param name="data">The texture data.</param>
        /// <param name="width">The texture width.</param>
        /// <param name="holeDetection">if set to <c>true</c> it will perform hole detection.</param>
        /// <param name="hullTolerance">The hull tolerance.</param>
        /// <param name="alphaTolerance">The alpha tolerance.</param>
        /// <param name="multiPartDetection">if set to <c>true</c> it will perform multi part detection.</param>
        /// <returns></returns>
        public static List<Vertices> DetectVertices(uint[] data, int width, float hullTolerance, byte alphaTolerance, bool multiPartDetection, bool holeDetection)
        {
            TextureConverter tc =
                new TextureConverter(data, width)
                {
                    HullTolerance = hullTolerance,
                    AlphaTolerance = alphaTolerance,
                    MultipartDetection = multiPartDetection,
                    HoleDetection = holeDetection
                };

            List<Vertices> detectedVerticesList = tc.DetectVertices();
            List<Vertices> result = new List<Vertices>();

            for (int i = 0; i < detectedVerticesList.Count; i++)
            {
                result.Add(detectedVerticesList[i]);
            }

            return result;
        }

        public List<Vertices> DetectVertices()
        {
            #region Check TextureConverter setup.

            if (_data == null)
                throw new Exception("'_data' can't be null. You have to use SetTextureData(uint[] data, int width) before calling this method.");

            if (_data.Length < 4)
                throw new Exception("'_data' length can't be less then 4. Your texture must be at least 2 x 2 pixels in size. " +
                    "You have to use SetTextureData(uint[] data, int width) before calling this method.");

            if (_width < 2)
                throw new Exception("'_width' can't be less then 2. Your texture must be at least 2 x 2 pixels in size. " +
                    "You have to use SetTextureData(uint[] data, int width) before calling this method.");

            if (_data.Length % _width != 0)
                throw new Exception("'_width' has an invalid value. You have to use SetTextureData(uint[] data, int width) before calling this method.");

            #endregion

            List<Vertices> detectedPolygons = new List<Vertices>();

            Vector2? holeEntrance = null;
            Vector2? polygonEntrance = null;

            List<Vector2> blackList = new List<Vector2>();

            bool searchOn;
            do
            {
                Vertices polygon;
                if (detectedPolygons.Count == 0)
                {
                    // First pass / single polygon
                    polygon = new Vertices(CreateSimplePolygon(Vector2.Zero, Vector2.Zero));

                    if (polygon.Count > 2)
                        polygonEntrance = GetTopMostVertex(polygon);
                }
                else if (polygonEntrance.HasValue)
                {
                    // Multi pass / multiple polygons
                    polygon = new Vertices(CreateSimplePolygon(polygonEntrance.Value, new Vector2(polygonEntrance.Value.X - 1f, polygonEntrance.Value.Y)));
                }
                else
                    break;

                searchOn = false;


                if (polygon.Count > 2)
                {
                    if (_holeDetection)
                    {
                        do
                        {
                            holeEntrance = SearchHoleEntrance(polygon, holeEntrance);

                            if (holeEntrance.HasValue)
                            {
                                if (!blackList.Contains(holeEntrance.Value))
                                {
                                    blackList.Add(holeEntrance.Value);
                                    Vertices holePolygon = CreateSimplePolygon(holeEntrance.Value,
                                                                               new Vector2(holeEntrance.Value.X + 1, holeEntrance.Value.Y));

                                    if (holePolygon != null && holePolygon.Count > 2)
                                    {
                                        switch (_polygonDetectionType)
                                        {
                                            case VerticesDetectionType.Integrated:

                                                // Add first hole polygon vertex to close the hole polygon.
                                                holePolygon.Add(holePolygon[0]);

                                                int vertex1Index, vertex2Index;
                                                if (SplitPolygonEdge(polygon, holeEntrance.Value, out vertex1Index, out vertex2Index))
                                                    polygon.InsertRange(vertex2Index, holePolygon);

                                                break;

                                            case VerticesDetectionType.Separated:
                                                if (polygon.Holes == null)
                                                    polygon.Holes = new List<Vertices>();

                                                polygon.Holes.Add(holePolygon);
                                                break;
                                        }
                                    }
                                }
                                else
                                    break;
                            }
                            else
                                break;
                        }
                        while (true);
                    }

                    detectedPolygons.Add(polygon);
                }

                if (_multipartDetection || polygon.Count <= 2)
                {
                    if (SearchNextHullEntrance(detectedPolygons, polygonEntrance.Value, out polygonEntrance))
                        searchOn = true;
                }
            }
            while (searchOn);

            if (detectedPolygons == null || (detectedPolygons != null && detectedPolygons.Count == 0))
                throw new Exception("Couldn't detect any vertices.");

            // Post processing.
            if (PolygonDetectionType == VerticesDetectionType.Separated) // Only when VerticesDetectionType.Separated? -> Recheck.
                ApplyTriangulationCompatibleWinding(ref detectedPolygons);

            if (_transform != Matrix.Identity)
                ApplyTransform(ref detectedPolygons);

            return detectedPolygons;
        }

        private void ApplyTriangulationCompatibleWinding(ref List<Vertices> detectedPolygons)
        {
            for (int i = 0; i < detectedPolygons.Count; i++)
            {
                detectedPolygons[i].Reverse();

                if (detectedPolygons[i].Holes != null && detectedPolygons[i].Holes.Count > 0)
                {
                    for (int j = 0; j < detectedPolygons[i].Holes.Count; j++)
                        detectedPolygons[i].Holes[j].Reverse();
                }
            }
        }

        private void ApplyTransform(ref List<Vertices> detectedPolygons)
        {
            for (int i = 0; i < detectedPolygons.Count; i++)
                detectedPolygons[i].Transform(ref _transform);
        }

        #region Data[] functions
        private int _tempIsSolidX;
        private int _tempIsSolidY;
        public bool IsSolid(ref Vector2 v)
        {
            _tempIsSolidX = (int)v.X;
            _tempIsSolidY = (int)v.Y;

            if (_tempIsSolidX >= 0 && _tempIsSolidX < _width && _tempIsSolidY >= 0 && _tempIsSolidY < _height)
                return (_data[_tempIsSolidX + _tempIsSolidY * _width] >= _alphaTolerance);
            //return ((_data[_tempIsSolidX + _tempIsSolidY * _width] & 0xFF000000) >= _alphaTolerance);

            return false;
        }

        public bool IsSolid(ref int x, ref int y)
        {
            if (x >= 0 && x < _width && y >= 0 && y < _height)
                return (_data[x + y * _width] >= _alphaTolerance);
            //return ((_data[x + y * _width] & 0xFF000000) >= _alphaTolerance);

            return false;
        }

        public bool IsSolid(ref int index)
        {
            if (index >= 0 && index < _dataLength)
                return (_data[index] >= _alphaTolerance);
            //return ((_data[index] & 0xFF000000) >= _alphaTolerance);

            return false;
        }

        public bool InBounds(ref Vector2 coord)
        {
            return (coord.X >= 0f && coord.X < _width && coord.Y >= 0f && coord.Y < _height);
        }
        #endregion

        /// <summary>
        /// Function to search for an entrance point of a hole in a polygon. It searches the polygon from top to bottom between the polygon edges.
        /// </summary>
        /// <param name="polygon">The polygon to search in.</param>
        /// <param name="lastHoleEntrance">The last entrance point.</param>
        /// <returns>The next holes entrance point. Null if ther are no holes.</returns>
        private Vector2? SearchHoleEntrance(Vertices polygon, Vector2? lastHoleEntrance)
        {
            if (polygon == null)
                throw new ArgumentNullException("'polygon' can't be null.");

            if (polygon.Count < 3)
                throw new ArgumentException("'polygon.MainPolygon.Count' can't be less then 3.");


            List<float> xCoords;
            Vector2? entrance;

            int startY;
            int endY;

            int lastSolid = 0;
            bool foundSolid;
            bool foundTransparent;

            // Set start y coordinate.
            if (lastHoleEntrance.HasValue)
            {
                // We need the y coordinate only.
                startY = (int)lastHoleEntrance.Value.Y;
            }
            else
            {
                // Start from the top of the polygon if last entrance == null.
                startY = (int)GetTopMostCoord(polygon);
            }

            // Set the end y coordinate.
            endY = (int)GetBottomMostCoord(polygon);

            if (startY > 0 && startY < _height && endY > 0 && endY < _height)
            {
                // go from top to bottom of the polygon
                for (int y = startY; y <= endY; y++)
                {
                    // get x-coord of every polygon edge which crosses y
                    xCoords = SearchCrossingEdges(polygon, y);

                    // We need an even number of crossing edges. 
                    // It's always a pair of start and end edge: nothing | polygon | hole | polygon | nothing ...
                    // If it's not then don't bother, it's probably a peak ...
                    // ...which should be filtered out by SearchCrossingEdges() anyway.
                    if (xCoords.Count > 1 && xCoords.Count % 2 == 0)
                    {
                        // Ok, this is short, but probably a little bit confusing.
                        // This part searches from left to right between the edges inside the polygon.
                        // The problem: We are using the polygon data to search in the texture data.
                        // That's simply not accurate, but necessary because of performance.
                        for (int i = 0; i < xCoords.Count; i += 2)
                        {
                            foundSolid = false;
                            foundTransparent = false;

                            // We search between the edges inside the polygon.
                            for (int x = (int)xCoords[i]; x <= (int)xCoords[i + 1]; x++)
                            {
                                // First pass: IsSolid might return false.
                                // In that case the polygon edge doesn't lie on the texture's solid pixel, because of the hull tolearance.
                                // If the edge lies before the first solid pixel then we need to skip our transparent pixel finds.

                                // The algorithm starts to search for a relevant transparent pixel (which indicates a possible hole) 
                                // after it has found a solid pixel.

                                // After we've found a solid and a transparent pixel (a hole's left edge) 
                                // we search for a solid pixel again (a hole's right edge).
                                // When found the distance of that coodrinate has to be greater then the hull tolerance.

                                if (IsSolid(ref x, ref y))
                                {
                                    if (!foundTransparent)
                                    {
                                        foundSolid = true;
                                        lastSolid = x;
                                    }

                                    if (foundSolid && foundTransparent)
                                    {
                                        entrance = new Vector2(lastSolid, y);

                                        if (DistanceToHullAcceptable(polygon, entrance.Value, true))
                                            return entrance;

                                        entrance = null;
                                        break;
                                    }
                                }
                                else
                                {
                                    if (foundSolid)
                                        foundTransparent = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (xCoords.Count % 2 == 0)
                            Debug.WriteLine("SearchCrossingEdges() % 2 != 0");
                    }
                }
            }

            return null;
        }

        private bool DistanceToHullAcceptableHoles(Vertices polygon, Vector2 point, bool higherDetail)
        {
            if (polygon == null)
                throw new ArgumentNullException("polygon", "'polygon' can't be null.");

            if (polygon.Count < 3)
                throw new ArgumentException("'polygon.MainPolygon.Count' can't be less then 3.");

            // Check the distance to main polygon.
            if (DistanceToHullAcceptable(polygon, point, higherDetail))
            {
                if (polygon.Holes != null)
                {
                    for (int i = 0; i < polygon.Holes.Count; i++)
                    {
                        // If there is one distance not acceptable then return false.
                        if (!DistanceToHullAcceptable(polygon.Holes[i], point, higherDetail))
                            return false;
                    }
                }

                // All distances are larger then _hullTolerance.
                return true;
            }

            // Default to false.
            return false;
        }

        private bool DistanceToHullAcceptable(Vertices polygon, Vector2 point, bool higherDetail)
        {
            if (polygon == null)
                throw new ArgumentNullException("polygon", "'polygon' can't be null.");

            if (polygon.Count < 3)
                throw new ArgumentException("'polygon.Count' can't be less then 3.");


            Vector2 edgeVertex2 = polygon[polygon.Count - 1];
            Vector2 edgeVertex1;

            if (higherDetail)
            {
                for (int i = 0; i < polygon.Count; i++)
                {
                    edgeVertex1 = polygon[i];

                    if (LineTools.DistanceBetweenPointAndLineSegment(ref point, ref edgeVertex1, ref edgeVertex2) <= _hullTolerance || Vector2.Distance(point, edgeVertex1) <= _hullTolerance)
                        return false;

                    edgeVertex2 = polygon[i];
                }

                return true;
            }
            else
            {
                for (int i = 0; i < polygon.Count; i++)
                {
                    edgeVertex1 = polygon[i];

                    if (LineTools.DistanceBetweenPointAndLineSegment(ref point, ref edgeVertex1, ref edgeVertex2) <= _hullTolerance)
                        return false;

                    edgeVertex2 = polygon[i];
                }

                return true;
            }
        }

        private bool InPolygon(Vertices polygon, Vector2 point)
        {
            bool inPolygon = !DistanceToHullAcceptableHoles(polygon, point, true);

            if (!inPolygon)
            {
                List<float> xCoords = SearchCrossingEdgesHoles(polygon, (int)point.Y);

                if (xCoords.Count > 0 && xCoords.Count % 2 == 0)
                {
                    for (int i = 0; i < xCoords.Count; i += 2)
                    {
                        if (xCoords[i] <= point.X && xCoords[i + 1] >= point.X)
                            return true;
                    }
                }

                return false;
            }

            return true;
        }

        private Vector2? GetTopMostVertex(Vertices vertices)
        {
            float topMostValue = float.MaxValue;
            Vector2? topMost = null;

            for (int i = 0; i < vertices.Count; i++)
            {
                if (topMostValue > vertices[i].Y)
                {
                    topMostValue = vertices[i].Y;
                    topMost = vertices[i];
                }
            }

            return topMost;
        }

        private float GetTopMostCoord(Vertices vertices)
        {
            float returnValue = float.MaxValue;

            for (int i = 0; i < vertices.Count; i++)
            {
                if (returnValue > vertices[i].Y)
                {
                    returnValue = vertices[i].Y;
                }
            }

            return returnValue;
        }

        private float GetBottomMostCoord(Vertices vertices)
        {
            float returnValue = float.MinValue;

            for (int i = 0; i < vertices.Count; i++)
            {
                if (returnValue < vertices[i].Y)
                {
                    returnValue = vertices[i].Y;
                }
            }

            return returnValue;
        }

        private List<float> SearchCrossingEdgesHoles(Vertices polygon, int y)
        {
            if (polygon == null)
                throw new ArgumentNullException("polygon", "'polygon' can't be null.");

            if (polygon.Count < 3)
                throw new ArgumentException("'polygon.MainPolygon.Count' can't be less then 3.");

            List<float> result = SearchCrossingEdges(polygon, y);

            if (polygon.Holes != null)
            {
                for (int i = 0; i < polygon.Holes.Count; i++)
                {
                    result.AddRange(SearchCrossingEdges(polygon.Holes[i], y));
                }
            }

            result.Sort();
            return result;
        }

        /// <summary>
        /// Searches the polygon for the x coordinates of the edges that cross the specified y coordinate.
        /// </summary>
        /// <param name="polygon">Polygon to search in.</param>
        /// <param name="y">Y coordinate to check for edges.</param>
        /// <returns>Descending sorted list of x coordinates of edges that cross the specified y coordinate.</returns>
        private List<float> SearchCrossingEdges(Vertices polygon, int y)
        {
            // sick-o-note:
            // Used to search the x coordinates of edges in the polygon for a specific y coordinate.
            // (Usualy comming from the texture data, that's why it's an int and not a float.)

            List<float> edges = new List<float>();

            // current edge
            Vector2 slope;
            Vector2 vertex1;    // i
            Vector2 vertex2;    // i - 1

            // next edge
            Vector2 nextSlope;
            Vector2 nextVertex; // i + 1

            bool addFind;

            if (polygon.Count > 2)
            {
                // There is a gap between the last and the first vertex in the vertex list.
                // We will bridge that by setting the last vertex (vertex2) to the last 
                // vertex in the list.
                vertex2 = polygon[polygon.Count - 1];

                // We are moving along the polygon edges.
                for (int i = 0; i < polygon.Count; i++)
                {
                    vertex1 = polygon[i];

                    // Approx. check if the edge crosses our y coord.
                    if ((vertex1.Y >= y && vertex2.Y <= y) ||
                        (vertex1.Y <= y && vertex2.Y >= y))
                    {
                        // Ignore edges that are parallel to y.
                        if (vertex1.Y != vertex2.Y)
                        {
                            addFind = true;
                            slope = vertex2 - vertex1;

                            // Special threatment for edges that end at the y coord.
                            if (vertex1.Y == y)
                            {
                                // Create preview of the next edge.
                                nextVertex = polygon[(i + 1) % polygon.Count];
                                nextSlope = vertex1 - nextVertex;

                                // Ignore peaks. 
                                // If thwo edges are aligned like this: /\ and the y coordinate lies on the top,
                                // then we get the same x coord twice and we don't need that.
                                if (slope.Y > 0)
                                    addFind = (nextSlope.Y <= 0);
                                else
                                    addFind = (nextSlope.Y >= 0);
                            }

                            if (addFind)
                                edges.Add((y - vertex1.Y) / slope.Y * slope.X + vertex1.X); // Calculate and add the x coord.
                        }
                    }

                    // vertex1 becomes vertex2 :).
                    vertex2 = vertex1;
                }
            }

            edges.Sort();
            return edges;
        }

        private bool SplitPolygonEdge(Vertices polygon, Vector2 coordInsideThePolygon, out int vertex1Index, out int vertex2Index)
        {
            Vector2 slope;
            int nearestEdgeVertex1Index = 0;
            int nearestEdgeVertex2Index = 0;
            bool edgeFound = false;

            float shortestDistance = float.MaxValue;

            bool edgeCoordFound = false;
            Vector2 foundEdgeCoord = Vector2.Zero;

            List<float> xCoords = SearchCrossingEdges(polygon, (int)coordInsideThePolygon.Y);

            vertex1Index = 0;
            vertex2Index = 0;

            foundEdgeCoord.Y = coordInsideThePolygon.Y;

            if (xCoords != null && xCoords.Count > 1 && xCoords.Count % 2 == 0)
            {
                float distance;
                for (int i = 0; i < xCoords.Count; i++)
                {
                    if (xCoords[i] < coordInsideThePolygon.X)
                    {
                        distance = coordInsideThePolygon.X - xCoords[i];

                        if (distance < shortestDistance)
                        {
                            shortestDistance = distance;
                            foundEdgeCoord.X = xCoords[i];

                            edgeCoordFound = true;
                        }
                    }
                }

                if (edgeCoordFound)
                {
                    shortestDistance = float.MaxValue;

                    int edgeVertex2Index = polygon.Count - 1;

                    int edgeVertex1Index;
                    for (edgeVertex1Index = 0; edgeVertex1Index < polygon.Count; edgeVertex1Index++)
                    {
                        Vector2 tempVector1 = polygon[edgeVertex1Index];
                        Vector2 tempVector2 = polygon[edgeVertex2Index];
                        distance = LineTools.DistanceBetweenPointAndLineSegment(ref foundEdgeCoord,
                                                                                ref tempVector1, ref tempVector2);
                        if (distance < shortestDistance)
                        {
                            shortestDistance = distance;

                            nearestEdgeVertex1Index = edgeVertex1Index;
                            nearestEdgeVertex2Index = edgeVertex2Index;

                            edgeFound = true;
                        }

                        edgeVertex2Index = edgeVertex1Index;
                    }

                    if (edgeFound)
                    {
                        slope = polygon[nearestEdgeVertex2Index] - polygon[nearestEdgeVertex1Index];
                        slope.Normalize();

                        Vector2 tempVector = polygon[nearestEdgeVertex1Index];
                        distance = Vector2.Distance(tempVector, foundEdgeCoord);

                        vertex1Index = nearestEdgeVertex1Index;
                        vertex2Index = nearestEdgeVertex1Index + 1;

                        polygon.Insert(nearestEdgeVertex1Index, distance * slope + polygon[vertex1Index]);
                        polygon.Insert(nearestEdgeVertex1Index, distance * slope + polygon[vertex2Index]);

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entrance"></param>
        /// <param name="last"></param>
        /// <returns></returns>
        private Vertices CreateSimplePolygon(Vector2 entrance, Vector2 last)
        {
            bool entranceFound = false;
            bool endOfHull = false;

            Vertices polygon = new Vertices(32);
            Vertices hullArea = new Vertices(32);
            Vertices endOfHullArea = new Vertices(32);

            Vector2 current = Vector2.Zero;

            #region Entrance check

            // Get the entrance point. //todo: alle möglichkeiten testen
            if (entrance == Vector2.Zero || !InBounds(ref entrance))
            {
                entranceFound = SearchHullEntrance(out entrance);

                if (entranceFound)
                {
                    current = new Vector2(entrance.X - 1f, entrance.Y);
                }
            }
            else
            {
                if (IsSolid(ref entrance))
                {
                    if (IsNearPixel(ref entrance, ref last))
                    {
                        current = last;
                        entranceFound = true;
                    }
                    else
                    {
                        Vector2 temp;
                        if (SearchNearPixels(false, ref entrance, out temp))
                        {
                            current = temp;
                            entranceFound = true;
                        }
                        else
                        {
                            entranceFound = false;
                        }
                    }
                }
            }

            #endregion

            if (entranceFound)
            {
                polygon.Add(entrance);
                hullArea.Add(entrance);

                Vector2 next = entrance;

                do
                {
                    // Search in the pre vision list for an outstanding point.
                    Vector2 outstanding;
                    if (SearchForOutstandingVertex(hullArea, out outstanding))
                    {
                        if (endOfHull)
                        {
                            // We have found the next pixel, but is it on the last bit of the hull?
                            if (endOfHullArea.Contains(outstanding))
                            {
                                // Indeed.
                                polygon.Add(outstanding);
                            }

                            // That's enough, quit.
                            break;
                        }

                        // Add it and remove all vertices that don't matter anymore
                        // (all the vertices before the outstanding).
                        polygon.Add(outstanding);
                        hullArea.RemoveRange(0, hullArea.IndexOf(outstanding));
                    }

                    // Last point gets current and current gets next. Our little spider is moving forward on the hull ;).
                    last = current;
                    current = next;

                    // Get the next point on hull.
                    if (GetNextHullPoint(ref last, ref current, out next))
                    {
                        // Add the vertex to a hull pre vision list.
                        hullArea.Add(next);
                    }
                    else
                    {
                        // Quit
                        break;
                    }

                    if (next == entrance && !endOfHull)
                    {
                        // It's the last bit of the hull, search on and exit at next found vertex.
                        endOfHull = true;
                        endOfHullArea.AddRange(hullArea);

                        // We don't want the last vertex to be the same as the first one, because it causes the triangulation code to crash.
                        if (endOfHullArea.Contains(entrance))
                            endOfHullArea.Remove(entrance);
                    }

                } while (true);
            }

            return polygon;
        }

        private bool SearchNearPixels(bool searchingForSolidPixel, ref Vector2 current, out Vector2 foundPixel)
        {
            for (int i = 0; i < ClosepixelsLength; i++)
            {
                int x = (int)current.X + _closePixels[i, 0];
                int y = (int)current.Y + _closePixels[i, 1];

                if (!searchingForSolidPixel ^ IsSolid(ref x, ref y))
                {
                    foundPixel = new Vector2(x, y);
                    return true;
                }
            }

            // Nothing found.
            foundPixel = Vector2.Zero;
            return false;
        }

        private bool IsNearPixel(ref Vector2 current, ref Vector2 near)
        {
            for (int i = 0; i < ClosepixelsLength; i++)
            {
                int x = (int)current.X + _closePixels[i, 0];
                int y = (int)current.Y + _closePixels[i, 1];

                if (x >= 0 && x <= _width && y >= 0 && y <= _height)
                {
                    if (x == (int)near.X && y == (int)near.Y)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool SearchHullEntrance(out Vector2 entrance)
        {
            // Search for first solid pixel.
            for (int y = 0; y <= _height; y++)
            {
                for (int x = 0; x <= _width; x++)
                {
                    if (IsSolid(ref x, ref y))
                    {
                        entrance = new Vector2(x, y);
                        return true;
                    }
                }
            }

            // If there are no solid pixels.
            entrance = Vector2.Zero;
            return false;
        }

        /// <summary>
        /// Searches for the next shape.
        /// </summary>
        /// <param name="detectedPolygons">Already detected polygons.</param>
        /// <param name="start">Search start coordinate.</param>
        /// <param name="entrance">Returns the found entrance coordinate. Null if no other shapes found.</param>
        /// <returns>True if a new shape was found.</returns>
        private bool SearchNextHullEntrance(List<Vertices> detectedPolygons, Vector2 start, out Vector2? entrance)
        {
            int x;

            bool foundTransparent = false;
            bool inPolygon = false;

            for (int i = (int)start.X + (int)start.Y * _width; i <= _dataLength; i++)
            {
                if (IsSolid(ref i))
                {
                    if (foundTransparent)
                    {
                        x = i % _width;
                        entrance = new Vector2(x, (i - x) / (float)_width);

                        inPolygon = false;
                        for (int polygonIdx = 0; polygonIdx < detectedPolygons.Count; polygonIdx++)
                        {
                            if (InPolygon(detectedPolygons[polygonIdx], entrance.Value))
                            {
                                inPolygon = true;
                                break;
                            }
                        }

                        if (inPolygon)
                            foundTransparent = false;
                        else
                            return true;
                    }
                }
                else
                    foundTransparent = true;
            }

            entrance = null;
            return false;
        }

        private bool GetNextHullPoint(ref Vector2 last, ref Vector2 current, out Vector2 next)
        {
            int x;
            int y;

            int indexOfFirstPixelToCheck = GetIndexOfFirstPixelToCheck(ref last, ref current);
            int indexOfPixelToCheck;

            for (int i = 0; i < ClosepixelsLength; i++)
            {
                indexOfPixelToCheck = (indexOfFirstPixelToCheck + i) % ClosepixelsLength;

                x = (int)current.X + _closePixels[indexOfPixelToCheck, 0];
                y = (int)current.Y + _closePixels[indexOfPixelToCheck, 1];

                if (x >= 0 && x < _width && y >= 0 && y <= _height)
                {
                    if (IsSolid(ref x, ref y))
                    {
                        next = new Vector2(x, y);
                        return true;
                    }
                }
            }

            next = Vector2.Zero;
            return false;
        }

        private bool SearchForOutstandingVertex(Vertices hullArea, out Vector2 outstanding)
        {
            Vector2 outstandingResult = Vector2.Zero;
            bool found = false;

            if (hullArea.Count > 2)
            {
                int hullAreaLastPoint = hullArea.Count - 1;

                Vector2 tempVector1;
                Vector2 tempVector2 = hullArea[0];
                Vector2 tempVector3 = hullArea[hullAreaLastPoint];

                // Search between the first and last hull point.
                for (int i = 1; i < hullAreaLastPoint; i++)
                {
                    tempVector1 = hullArea[i];

                    // Check if the distance is over the one that's tolerable.
                    if (LineTools.DistanceBetweenPointAndLineSegment(ref tempVector1, ref tempVector2, ref tempVector3) >= _hullTolerance)
                    {
                        outstandingResult = hullArea[i];
                        found = true;
                        break;
                    }
                }
            }

            outstanding = outstandingResult;
            return found;
        }

        private int GetIndexOfFirstPixelToCheck(ref Vector2 last, ref Vector2 current)
        {
            // .: pixel
            // l: last position
            // c: current position
            // f: first pixel for next search

            // f . .
            // l c .
            // . . .

            //Calculate in which direction the last move went and decide over the next pixel to check.
            switch ((int)(current.X - last.X))
            {
                case 1:
                    switch ((int)(current.Y - last.Y))
                    {
                        case 1:
                            return 1;

                        case 0:
                            return 0;

                        case -1:
                            return 7;
                    }
                    break;

                case 0:
                    switch ((int)(current.Y - last.Y))
                    {
                        case 1:
                            return 2;

                        case -1:
                            return 6;
                    }
                    break;

                case -1:
                    switch ((int)(current.Y - last.Y))
                    {
                        case 1:
                            return 3;

                        case 0:
                            return 4;

                        case -1:
                            return 5;
                    }
                    break;
            }

            return 0;
        }
    }
}