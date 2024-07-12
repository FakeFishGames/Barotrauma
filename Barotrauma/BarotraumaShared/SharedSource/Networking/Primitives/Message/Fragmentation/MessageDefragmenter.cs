using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.Networking;

sealed class MessageDefragmenter
{
    private readonly Dictionary<ushort, MessageFragment[]> partialMessages = new Dictionary<ushort, MessageFragment[]>();

    public Option<ImmutableArray<byte>> ProcessIncomingFragment(MessageFragment fragment)
    {
        if (!partialMessages.ContainsKey(fragment.FragmentId.MessageId))
        {
            partialMessages[fragment.FragmentId.MessageId] = new MessageFragment[fragment.FragmentId.FragmentCount];
        }
        else if (partialMessages[fragment.FragmentId.MessageId].Length != fragment.FragmentId.FragmentCount)
        {
            DebugConsole.AddWarning($"Got a fragment for message {fragment.FragmentId.MessageId} " +
                                    $"with a mismatched expected fragment count");
            return Option.None;
        }

        var fragmentBuffer = partialMessages[fragment.FragmentId.MessageId];
        if (fragment.FragmentId.FragmentIndex >= fragmentBuffer.Length)
        {
            DebugConsole.AddWarning($"Got a fragment for message {fragment.FragmentId.MessageId} " +
                                    $"with an index greater than or equal to the expected fragment count ({fragment.FragmentId.FragmentIndex} >= {fragmentBuffer.Length})");
            return Option.None;
        }

        fragmentBuffer[fragment.FragmentId.FragmentIndex] = fragment;
        if (fragmentBuffer.All(f => !f.Data.IsDefault && f.FragmentId.MessageId == fragment.FragmentId.MessageId))
        {
            partialMessages.Remove(fragment.FragmentId.MessageId);
            return Option.Some(fragmentBuffer.SelectMany(f => f.Data).ToImmutableArray());
        }
        return Option.None;
    }
}