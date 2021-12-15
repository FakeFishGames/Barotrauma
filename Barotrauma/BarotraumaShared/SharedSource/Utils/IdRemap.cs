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

        private readonly List<Range<int>> srcRanges;
        private readonly int destOffset;

        public IdRemap(XElement parentElement, int offset)
        {
            destOffset = offset;
            if (parentElement != null && parentElement.HasElements)
            {
                srcRanges = new List<Range<int>>();
                foreach (XElement subElement in parentElement.Elements())
                {
                    int id = subElement.GetAttributeInt("ID", -1);
                    if (id > 0) { InsertId(id); }
                }
                maxId = GetOffsetId(srcRanges.Last().End) + 1;
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
            for (int i = 0; i < srcRanges.Count; i++)
            {
                if (srcRanges[i].Start > id)
                {
                    if (srcRanges[i].Start == (id + 1))
                    {
                        srcRanges[i] = new Range<int>(id, srcRanges[i].End);
                        if (i > 0 && srcRanges[i].Start == srcRanges[i - 1].End)
                        {
                            srcRanges[i - 1] = new Range<int>(srcRanges[i - 1].Start, srcRanges[i].End);
                            srcRanges.RemoveAt(i);
                        }
                    }
                    else
                    {
                        srcRanges.Insert(i, new Range<int>(id, id));
                    }
                    return;
                }
                else if (srcRanges[i].End < id)
                {
                    if (srcRanges[i].End == (id - 1))
                    {
                        srcRanges[i] = new Range<int>(srcRanges[i].Start, id);
                        if (i < (srcRanges.Count - 1) && srcRanges[i].End == srcRanges[i + 1].Start)
                        {
                            srcRanges[i] = new Range<int>(srcRanges[i].Start, srcRanges[i + 1].End);
                            srcRanges.RemoveAt(i + 1);
                        }
                        return;
                    }
                }
            }
            srcRanges.Add(new Range<int>(id, id));
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
            for (int i = 0; i < srcRanges.Count; i++)
            {
                if (id >= srcRanges[i].Start && id <= srcRanges[i].End)
                {
                    return (ushort)(id - srcRanges[i].Start + 1 + currOffset);
                }
                currOffset += srcRanges[i].End - srcRanges[i].Start + 1;
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