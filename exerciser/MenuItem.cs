using System;

namespace Exerciser
{
    /// <summary>
    /// One entry in a component's hand-written menu table: a label, a short hint
    /// carrying TESTING.md's per-method caveats, and the prompt/invoke/report
    /// handler itself.
    /// </summary>
    internal sealed class MenuItem
    {
        internal string Label { get; }
        internal string Hint { get; }
        internal Action Invoke { get; }

        internal MenuItem(string label, string hint, Action invoke)
        {
            Label = label;
            Hint = hint;
            Invoke = invoke;
        }
    }
}
