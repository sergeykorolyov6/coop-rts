using System.Collections.Generic;
using System.Text;
using Game.Sim;

namespace Game.Net;

// network.md, Fail cases: machines with different balance, maps or missions would desync at
// once, so they compare one hash of all those texts before a match.
public static class ContentHash
{
    public static ulong Of(IEnumerable<string> texts)
    {
        var all = new StringBuilder();
        foreach (string text in texts)
            all.Append(text.Length).Append(':').Append(text);
        return XxHash64.Compute(Encoding.UTF8.GetBytes(all.ToString()));
    }
}
