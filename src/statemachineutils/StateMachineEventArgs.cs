using System;

namespace StateMachineAutomation
{
    /// <summary>
    /// Describes a state change. Used by <c>TransitionFired</c>, <c>StateEntered</c> and
    /// <c>MachineFinished</c> (<c>StateExited</c> uses <see cref="StateMachineExitEventArgs"/>). Every property is a non-null string so it wires cleanly to a Robot
    /// Studio data port; an empty string means "not applicable" (for example the initial entry into a
    /// machine has no previous state and no trigger).
    /// </summary>
    public sealed class StateMachineTransitionEventArgs : EventArgs
    {
        /// <summary>The state the machine was in before the change, or an empty string for the initial entry.</summary>
        public string PreviousState { get; }

        /// <summary>The state the machine is in after the change.</summary>
        public string NewState { get; }

        /// <summary>The trigger that caused the change, or an empty string for a Start/Reset.</summary>
        public string Trigger { get; }

        internal StateMachineTransitionEventArgs(string previousState, string newState, string trigger)
        {
            PreviousState = previousState ?? string.Empty;
            NewState = newState ?? string.Empty;
            Trigger = trigger ?? string.Empty;
        }
    }

    /// <summary>
    /// Describes the state the machine is leaving (<c>StateExited</c>): the same change as
    /// <see cref="StateMachineTransitionEventArgs"/>, plus how long the machine had been in that state.
    /// </summary>
    public sealed class StateMachineExitEventArgs : EventArgs
    {
        /// <summary>The state the machine is leaving.</summary>
        public string PreviousState { get; }

        /// <summary>The state the machine is entering.</summary>
        public string NewState { get; }

        /// <summary>The trigger that caused the change.</summary>
        public string Trigger { get; }

        /// <summary>
        /// Whole milliseconds the machine spent in <see cref="PreviousState"/>: from that state's recorded entry time to
        /// the entry time recorded for <see cref="NewState"/> (the very timestamp that is saved with the run when
        /// persistence is on). Entry times are recorded to the millisecond, so the durations of successive states add up
        /// exactly (a system clock set backwards makes the one spanning duration 0, never negative). It does not include the time a
        /// persistence write or an event handler takes. For a run restored from persistence it counts from the original
        /// entry time, so it includes the time the robot was down.
        /// </summary>
        public double ElapsedMs { get; }

        internal StateMachineExitEventArgs(string previousState, string newState, string trigger, double elapsedMs)
        {
            PreviousState = previousState ?? string.Empty;
            NewState = newState ?? string.Empty;
            Trigger = trigger ?? string.Empty;
            ElapsedMs = elapsedMs;
        }
    }

    /// <summary>Describes a trigger the machine declined to act on (<c>TransitionRejected</c>).</summary>
    public sealed class StateMachineRejectedEventArgs : EventArgs
    {
        /// <summary>The state the machine was in (and still is).</summary>
        public string State { get; }

        /// <summary>The trigger that was rejected.</summary>
        public string Trigger { get; }

        /// <summary>A stable code: <c>NoTransition</c>, <c>GuardFailed</c>, <c>Finished</c> or <c>NotStarted</c>.</summary>
        public string Reason { get; }

        /// <summary>Human-readable detail. For a failed guard it names the guard as declared in the definition (key, operator and expected value) but never the context's actual value.</summary>
        public string Detail { get; }

        internal StateMachineRejectedEventArgs(string state, string trigger, string reason, string detail)
        {
            State = state ?? string.Empty;
            Trigger = trigger ?? string.Empty;
            Reason = reason ?? string.Empty;
            Detail = detail ?? string.Empty;
        }
    }
}
