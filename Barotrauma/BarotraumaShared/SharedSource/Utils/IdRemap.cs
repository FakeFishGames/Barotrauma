using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public class IdRemap
    {
        public static readonly IdRemap DiscardId = new IdRemap(null, -1);

        private int maxId;

        private List<Point> srcRanges;
        private int destOffset;

        public IdRemap(XElement parentElement, int offset)
        {
            destOffset = offset;
            if (parentElement != null)
            {
                srcRanges = new List<Point>();
                foreach (XElement subElement in parentElement.Elements())
                {
                    int id = subElement.GetAttributeInt("ID", -1);
                    if (id > 0) { InsertId(id); }
                }
                maxId = GetOffsetId(srcRanges.Last().Y + 1);
            }
            else
            {
                maxId = offset + 1;
            }
        }

        public ushort AssignMaxId()
        {
            maxId++;
            return (ushort)maxId;
        }

        private void InsertId(int id)
        {
            for (int i=0;i<srcRanges.Count;i++)
            {
                if (srcRanges[i].X > id)
                {
                    if (srcRanges[i].X == (id + 1))
                    {
                        srcRanges[i] = new Point(id, srcRanges[i].Y);
                        if (i > 0 && srcRanges[i].X == srcRanges[i - 1].Y)
                        {
                            srcRanges[i - 1] = new Point(srcRanges[i - 1].X, srcRanges[i].Y);
                            srcRanges.RemoveAt(i);
                        }
                    }
                    else
                    {
                        srcRanges.Insert(i, new Point(id, id));
                    }
                    return;
                }
                else if (srcRanges[i].Y < id)
                {
                    if (srcRanges[i].Y == (id - 1))
                    {
                        srcRanges[i] = new Point(srcRanges[i].X, id);
                        if (i < (srcRanges.Count-1) && srcRanges[i].Y == srcRanges[i + 1].X)
                        {
                            srcRanges[i] = new Point(srcRanges[i].X, srcRanges[i + 1].Y);
                            srcRanges.RemoveAt(i+1);
                        }
                        return;
                    }
                }
            }
            srcRanges.Add(new Point(id, id));
        }

        public ushort GetOffsetId(XElement element)
        {
            return GetOffsetId(element.GetAttributeInt("ID", 0));
        }

        public ushort GetOffsetId(int id)
        {
            if (id <= 0) { return 0; }
            if (destOffset < 0) { return 0; }
            if (srcRanges == null) { return (ushort)(id + destOffset); }

            int currOffset = destOffset;
            for (int i=0;i<srcRanges.Count;i++)
            {
                if (id >= srcRanges[i].X && (id <= srcRanges[i].Y || (i == srcRanges.Count-1)))
                {
                    return (ushort)(id - srcRanges[i].X + 1 + currOffset);
                }
                currOffset += srcRanges[i].Y - srcRanges[i].X + 1;
            }
            return 0;
        }

        public static ushort DetermineNewOffset()
        {
            ushort idOffset = 0;
            foreach (Entity e in Entity.GetEntities())
            {
                if (e.ID > Entity.ReservedIDStart || e is Submarine) { continue; }
                idOffset = Math.Max(idOffset, e.ID);
            }
            return idOffset;
        }
    }
}