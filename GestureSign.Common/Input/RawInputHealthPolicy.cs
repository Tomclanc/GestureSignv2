using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Common.Input;

public readonly struct RawInputRegistrationStatus
{
    public RawInputRegistrationStatus(ushort page, ushort usage, int flags, long target)
    { Page = page; Usage = usage; Flags = flags; Target = target; }
    public ushort Page { get; }
    public ushort Usage { get; }
    public int Flags { get; }
    public long Target { get; }
}

public static class RawInputHealthPolicy
{
    // A failed query or mere input silence is never evidence for recovery.
    public static bool NeedsRepair(bool querySucceeded, long target, IEnumerable<ushort> expected,
        IEnumerable<RawInputRegistrationStatus> actual)
    {
        if (!querySucceeded || target == 0) return false;
        return expected.Any(usage => !actual.Any(r => r.Page == 0x0D && r.Usage == usage &&
            r.Target == target && (r.Flags & 0x100) != 0));
    }
}
