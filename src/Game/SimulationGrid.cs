//
//  SimulationGrid.cs
//
//  Author:
//       Noah Ablaseau <nablaseau@hotmail.com>
//
//  Copyright (c) 2017 
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using linerider.Drawing;
using OpenTK;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using linerider.Game;
namespace linerider
{

    public class ChunkCollection
    {
        #region Fields

        public const int CellSize = 14;
        public int GridVersion = 62;
        private readonly Dictionary<ulong, Chunk> Cells = new Dictionary<ulong, Chunk>(4096);
        #endregion Fields

        #region Methods

        public void AddLine(Line line)
        {
            var positions = GetGridPositions(line);
            foreach (var pos in positions)
            {
                Register(line, pos.X, pos.Y);
            }
        }
        public CellLocation CellInfo(double posx, double posy)
        {
            int x = (int)Math.Floor(posx / CellSize);
            int y = (int)Math.Floor(posy / CellSize);
            return new CellLocation() { X = x, Y = y, Remainder = new Vector2d(posx - (CellSize * x), posy - (CellSize * y)) };
        }

        public Chunk GetChunk(int x, int y)
        {
            Chunk ret;
            Cells.TryGetValue(GetCellKey(x, y), out ret);
            return ret;
        }

        public List<CellLocation> GetGridPositions(Line line)
        {
            var ret = new List<CellLocation>();
            var cell = CellInfo(line.Position.X, line.Position.Y);
            var gridend = CellInfo(line.Position2.X, line.Position2.Y);

            ret.Add(cell);
            if ((line.diff.X == 0 && line.diff.Y == 0) || (cell.X == gridend.X && cell.Y == gridend.Y))
                return ret;

            int p1X = Math.Min(cell.X, gridend.X),
                p2X = Math.Max(cell.X, gridend.X),
                p1Y = Math.Min(cell.Y, gridend.Y),
                p2Y = Math.Max(cell.Y, gridend.Y);
            var box = Rectangle.FromLTRB(p1X, p1Y, p2X, p2Y);
            var current = line.Position;
            if (GridVersion == 62)
            {
                while (true)
                {
                    double maxstepx, maxstepy;
                    if (cell.X < 0)
                        maxstepy = line.diff.X > 0 ? (CellSize + cell.Remainder.X) : (-CellSize - cell.Remainder.X);
                    else
                        maxstepy = line.diff.X > 0 ? (CellSize - cell.Remainder.X) : (-(cell.Remainder.X + 1));

                    if (cell.Y < 0)
                        maxstepx = line.diff.Y > 0 ? (CellSize + cell.Remainder.Y) : (-CellSize - cell.Remainder.Y);
                    else
                        maxstepx = line.diff.Y > 0 ? (CellSize - cell.Remainder.Y) : (-(cell.Remainder.Y + 1));
                    var stepx = line.diff.X * maxstepx * (1 / line.diff.Y);
                    var stepy = line.diff.Y * maxstepy * (1 / line.diff.X);
                    current.X += (Math.Abs(stepx) < Math.Abs(maxstepy)) ? stepx : maxstepy;
                    current.Y += (Math.Abs(stepy) < Math.Abs(maxstepx)) ? stepy : maxstepx;
                    cell = CellInfo(current.X, current.Y);
                    if (!CheckBounds(box, cell.X, cell.Y))
                        return ret;
                    ret.Add(cell);
                }
            }
            if (GridVersion == 61) //eh
            {
                ret = GetGridPositions61(line);
            }
            return ret;
        }
        public List<CellLocation> GetGridPositions61(Line line)
        {
            var ret = new List<CellLocation>();
            var cell = CellInfo(line.Position.X, line.Position.Y);
            var gridend = CellInfo(line.Position2.X, line.Position2.Y);

            ret.Add(cell);
            if ((line.diff.X == 0 && line.diff.Y == 0) || (cell.X == gridend.X && cell.Y == gridend.Y))
                return ret;

            int p1X = Math.Min(cell.X, gridend.X),
                p2X = Math.Max(cell.X, gridend.X),
                p1Y = Math.Min(cell.Y, gridend.Y),
                p2Y = Math.Max(cell.Y, gridend.Y);
            var box = Rectangle.FromLTRB(p1X, p1Y, p2X, p2Y);
            var current = line.Position;
            double slope = 0;
            double _loc13 = 0;
            double isbelowactualY = 0;
            if (line.diff.X != 0 && line.diff.Y != 0)
            {
                slope = line.diff.Y / line.diff.X;
                _loc13 = 1.0 / slope;
                isbelowactualY = line.Position.Y - slope * line.Position.X;
            } // end if
            while (true)
            {
                double difY, difX;
                difX = -cell.Remainder.X + (line.diff.X > 0 ? CellSize : -1);
                difY = -cell.Remainder.Y + (line.diff.Y > 0 ? CellSize : -1);
                if (line.diff.X == 0)
                {
                    current.Y = current.Y + difY;
                }
                else if (line.diff.Y == 0)
                {
                    current.X = current.X + difX;
                }
                else
                {
                    var whyyy = Math.Round(slope * (current.X + difX) + isbelowactualY);
                    if (Math.Abs(whyyy - current.Y) < Math.Abs(difY))
                    {
                        current.X = current.X + difX;
                        current.Y = whyyy;
                    }
                    else if (Math.Abs(whyyy - current.Y) == Math.Abs(difY))
                    {
                        current.X = current.X + difX;
                        current.Y = current.Y + difY;
                    }
                    else
                    {
                        current.X = Math.Round((current.Y + difY - isbelowactualY) * _loc13);
                        current.Y = current.Y + difY;
                    }
                }
                cell = CellInfo(current.X, current.Y);
                if (CheckBounds(box, cell.X, cell.Y))
                {
                    ret.Add(cell);
                    continue;
                }
                return ret;
            }
        }

        public Chunk PointToChunk(Vector2d pos)
        {
            return GetChunk((int)Math.Floor(pos.X / CellSize), (int)Math.Floor(pos.Y / CellSize));
        }

        public void RemoveLine(Line line)
        {
            var positions = GetGridPositions(line);
            foreach (var pos in positions)
            {
                Unregister(line, pos.X, pos.Y);
            }
        }

        private bool CheckBounds(Rectangle r, int x, int y)
        {
            return x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
        }

        private void Register(Line l, int x, int y)
        {
            Chunk ret;
            var idx = GetCellKey(x, y);
            if (!Cells.TryGetValue(idx, out ret))
            {
                ret = new Chunk();
                Cells[idx] = ret;
            }
            ret.AddLine(l);
        }

        private void Unregister(Line l, int x, int y)
        {
            Chunk ret;
            var idx = GetCellKey(x, y);
            if (Cells.TryGetValue(idx, out ret))
            {
                ret.RemoveLine(l);
            }
        }
        private ulong GetCellKey(int x, int y)
        {
            return ((ulong)y & 0xFFFFFFFF) | ((((ulong)x) << 32) & 0xFFFFFFFF00000000);
        }

        #endregion Methods
        public struct CellLocation
        {
            public Vector2d Remainder;

            public int X;

            public int Y;
        }
    }
}
