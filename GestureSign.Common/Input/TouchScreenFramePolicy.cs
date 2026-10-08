using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Common.Input
{
    public static class TouchScreenFramePolicy
    {
        // Missing up reports must cancel the old gesture, never synthesize a
        // successful release. Partial hybrid packets cannot remove anchors.
        public static int[] MissingContacts(IEnumerable<int> active, IEnumerable<int> frame, bool complete)
        {
            if (!complete) return new int[0];
            var reported = new HashSet<int>(frame);
            return active.Where(id => !reported.Contains(id)).ToArray();
        }
    }
}
