#nullable enable
using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public sealed class IdRemap
    {
        public static readonly IdRemap DiscardId = new IdRemap(null, -1);

        private int maxId;

        private readonly List<Range<int>>? srcRanges;
        private readonly int destOffset;

        public IdRemap(XElement? parentElement, int offset)
        {
            destOffset = offset;
            if (parentElement is { HasElements: true })
            {
                srcRanges = new List<Range<int>>();
                foreach (XElement subElement in parentElement.Elements())
                {
                    int id = subElement.GetAttributeInt("ID", -1);
                    if (id > 0) { InsertId(id); }
                }
                maxId = GetOffsetId(srcRanges.Last().End);
            }
            else
            {
                maxId = offset;
            }
        }

        public void AssignMaxId(out ushort result)
        {
            maxId++;
            result = (ushort)maxId;
        }

        private void InsertId(int id)
        {
            if (srcRanges is null) { throw new NullReferenceException("Called InsertId when srcRanges is null"); }

            void tryMergeRangeWithNext(int indexA)
            {
                int indexB = indexA + 1;

                if (indexA < 0 /* Index A out of bounds */
                    || indexB >= srcRanges.Count /* Index B out of bounds */)
                {
                    return;
                }

                Range<int> rangeA = srcRanges[indexA];
                Range<int> rangeB = srcRanges[indexB];

                if ((rangeA.End+1) >= rangeB.Start) //The end of range A is right before the start of range B, this should be one range
                {
                    srcRanges[indexA] = new Range<int>(rangeA.Start, rangeB.End);
                    srcRanges.RemoveAt(indexB);
                }
            }

            int insertIndex = srcRanges.Count;
            for (int i = 0; i < srcRanges.Count; i++)
            {
                if (srcRanges[i].Contains(id)) //We already have a range that contains this ID, duplicates are invalid input!
                {
                    throw new InvalidOperationException($"Duplicate ID: {id}");
                }
                if (srcRanges[i].Start > id) //ID is between srcRanges[i-1] and srcRanges[i], insert at i
                {
                    insertIndex = i;
                    break;
                }
            }
            srcRanges.Insert(insertIndex, new Range<int>(id, id)); //Insert new range consisting of solely the new ID
            tryMergeRangeWithNext(insertIndex); //Try merging new range with the one that comes after it
            tryMergeRangeWithNext(insertIndex - 1); //Try merging new range with the one that comes before it
        }

        public ushort GetOffsetId(XElement element)
        {
            return GetOffsetId(element.GetAttributeInt("ID", 0));
        }

        public ushort GetOffsetId(int id)
        {
            if (id <= 0) //Input cannot be remapped because it's negative
            {
                return 0;
            }
            if (destOffset < 0) //Remapper has been defined to discard all input
            {
                return 0;
            }
            if (srcRanges is null) //Remapper defines no source ranges so it just adds an offset
            {
                return (ushort)(id + destOffset);
            }

            int rangeSize(in Range<int> r)
                => r.End - r.Start + 1;

            int currOffset = destOffset;
            for (int i = 0; i < srcRanges.Count; i++)
            {
                if (srcRanges[i].Contains(id))
                {
                    //The source range for this ID has been found!
                    //The return value is such that all IDs that
                    //are returned by this remapper are contiguous,
                    //even if they weren't originally
                    return (ushort)(id - srcRanges[i].Start + currOffset);
                }
                currOffset += rangeSize(srcRanges[i]);
            }
            return 0;
        }

        public static ushort DetermineNewOffset()
        {
            int largestEntityId = 0;
            foreach (Entity e in Entity.GetEntities())
            {
                if (e.ID > Entity.ReservedIDStart || e is Submarine) { continue; }
                largestEntityId = Math.Max(largestEntityId, e.ID);
            }
            return (ushort)(largestEntityId+1);
        }
    }
}