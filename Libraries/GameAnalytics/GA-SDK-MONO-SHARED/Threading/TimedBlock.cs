using System;

namespace GameAnalyticsSDK.Net
{
	internal class TimedBlock : IComparable<TimedBlock>
	{
		public readonly DateTime deadline;
		public readonly Action block;
		public readonly string blockName;

		public TimedBlock (DateTime deadline, Action block, string blockName)
		{
			this.deadline = deadline;
			this.block = block;
			this.blockName = blockName;
		}

		public int CompareTo(TimedBlock other)
		{
			return this.deadline.CompareTo (other.deadline);
		}

		public override string ToString ()
		{
			return "{TimedBlock: deadLine=" + this.deadline.Ticks + ", block=" + this.blockName + "}";
		}
	}
}

