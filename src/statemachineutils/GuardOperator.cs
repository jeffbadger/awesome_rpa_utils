namespace StateMachineAutomation
{
    /// <summary>The comparison a guard makes between a context value and the guard's value. Shown as a drop-down on a Robot Studio block.</summary>
    public enum GuardOperator
    {
        /// <summary>No guard: the transition is unconditional.</summary>
        None = 0,
        /// <summary>The key exists and its text equals the value (case-insensitive).</summary>
        Equal = 1,
        /// <summary>The key exists and its text differs from the value.</summary>
        NotEqual = 2,
        /// <summary>The key exists and equals one item of a comma-separated list.</summary>
        In = 3,
        /// <summary>The key exists and equals none of the items of a comma-separated list.</summary>
        NotIn = 4,
        /// <summary>The key exists, and both it and the value are numbers, and it is larger.</summary>
        GreaterThan = 5,
        /// <summary>The key exists, and both it and the value are numbers, and it is smaller.</summary>
        LessThan = 6,
        /// <summary>The key is present, even with an empty value. Takes no value.</summary>
        Exists = 7,
        /// <summary>The key is absent. Takes no value.</summary>
        NotExists = 8
    }
}
