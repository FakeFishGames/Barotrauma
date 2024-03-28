using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma.Networking;

sealed class MessageFragmenter
{
    private UInt16 nextFragmentedMessageId = 0;
    private readonly List<MessageFragment> fragments = new List<MessageFragment>();

    public ImmutableArray<MessageFragment> FragmentMessage(ReadOnlySpan<byte> bytes)
    {
        UInt16 msgId = nextFragmentedMessageId;
        nextFragmentedMessageId++;

        int roundedByteCount = bytes.Length;
        roundedByteCount += (MessageFragment.MaxSize - (roundedByteCount % MessageFragment.MaxSize)) % MessageFragment.MaxSize;

        int fragmentCount = roundedByteCount / MessageFragment.MaxSize;
        fragments.Clear();
        fragments.EnsureCapacity(fragmentCount);
        for (int i = 0; i < fragmentCount; i++)
        {
            var subset = bytes[(i * MessageFragment.MaxSize)..];
            if (subset.Length > MessageFragment.MaxSize) { subset = subset[..MessageFragment.MaxSize]; }

            fragments.Add(new MessageFragment(
                FragmentId: new MessageFragment.Id(
                    FragmentIndex: (ushort)i,
                    FragmentCount: (ushort)fragmentCount,
                    MessageId: msgId),
                Data: subset.ToArray().ToImmutableArray()));
        }

        return fragments.ToImmutableArray();
    }
}