using System.Collections.Generic;
using System.Text;
using Game.Sim;

namespace Game.Net;

// network.md, Fail cases: machines with different balance, maps or missions would desync at
// once, so they compare one hash of all those files before a match. Files go by name, so the
// order a caller lists them in (an Inspector list, a folder) does not matter.
public static class ContentHash
{
    public static ulong Of(IEnumerable<(string Name, string Text)> files)
    {
        var sorted = new List<(string Name, string Text)>(files);
        sorted.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        var all = new StringBuilder();
        foreach ((string name, string text) in sorted)
            all.Append(name).Append('\n').Append(text.Length).Append(':').Append(text);
        return XxHash64.Compute(Encoding.UTF8.GetBytes(all.ToString()));
    }
}
