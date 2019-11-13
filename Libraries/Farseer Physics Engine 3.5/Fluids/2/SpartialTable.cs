/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System.Collections;
using System.Collections.Generic;

namespace FarseerPhysics.Fluids
{
    public class SpatialTable : IEnumerable<Particle>
    {
        // default nearby table size
        private const int DefaultNearbySize = 50;
        private List<Particle> _table;
        private List<Particle> _voidList = new List<Particle>(1);
        private List<Particle>[][] _nearby;
        bool _initialized;

        private int _row;
        private int _column;
        private int _cellSize;

        public SpatialTable(int column, int row, int cellSize)
        {
            _row = row;
            _cellSize = cellSize;
            _column = column;
        }

        public void Initialize()
        {
            _table = new List<Particle>((_row * _column) / 2);
            _nearby = new List<Particle>[_column][];

            for (int i = 0; i < _column; ++i)
            {
                _nearby[i] = new List<Particle>[_row];

                for (int j = 0; j < _row; ++j)
                {
                    _nearby[i][j] = new List<Particle>(DefaultNearbySize);
                }
            }
            _initialized = true;
        }

        /// <summary>
        /// Append value to the table and identify its position in the space.
        /// Don't need to rehash table after append operation.</summary>
        /// <param name="value"></param>
        public void Add(Particle value)
        {
            if (!_initialized)
                Initialize();

            AddInterRadius(value);
            _table.Add(value);
        }

        public Particle this[int i]
        {
            get { return _table[i]; }
            set { _table[i] = value; }
        }

        public void Remove(Particle value)
        {
            _table.Remove(value);
        }

        public void Clear()
        {
            for (int i = 0; i < _column; ++i)
            {
                for (int j = 0; j < _row; ++j)
                {
                    _nearby[i][j].Clear();
                    _nearby[i][j] = null;
                }
            }
            _table.Clear();
        }

        public int Count
        {
            get { return (_table == null)? 0 : _table.Count; }
        }

        public List<Particle> GetNearby(Particle value)
        {
            int x = posX(value);
            int y = posY(value);

            if (!InRange(x, y))
                return _voidList;

            return _nearby[x][y];
        }

        private int posX(Particle value)
        {
            return (int)((value.Position.X + (_column / 2) + 0.3f) / _cellSize);
        }

        private int posY(Particle value)
        {
            return (int)((value.Position.Y + 0.3f) / _cellSize);
        }

        public int CountNearBy(Particle value)
        {
            return GetNearby(value).Count;
        }

        /// <summary>
        /// Updates the spatial relationships of objects. Rehash function
        /// needed if elements change their position in the space.
        /// </summary>
        public void Rehash()
        {
            if (_table == null || _table.Count == 0)
                return;

            for (int i = 0; i < _column; i++)
            {
                for (int j = 0; j < _row; j++)
                {
                    if (_nearby[i][j] != null)
                        _nearby[i][j].Clear();
                }
            }

            foreach (Particle particle in _table)
            {
                AddInterRadius(particle);
            }
        }

        /// <summary>
        /// Add element to its position and neighbor cells.
        /// </summary>
        /// <param name="value"></param>
        private void AddInterRadius(Particle value)
        {
            for (int i = -1; i < 2; ++i)
            {
                for (int j = -1; j < 2; ++j)
                {
                    int x = posX(value) + i;
                    int y = posY(value) + j;
                    if (InRange(x, y))
                        _nearby[x][y].Add(value);
                }
            }
        }

        /// <summary>
        /// Check if a position is out of the spatial range
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns>true if position is in range.</returns>
        private bool InRange(float x, float y)
        {
            return (x > 0 && x < _column && y > 0 && y < _row);
        }

        public IEnumerator<Particle> GetEnumerator()
        {
            return _table.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
