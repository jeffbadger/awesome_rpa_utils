namespace ReconciliationAutomation
{
    /// <summary>How a comparison treats explicit JSON nulls. Shown as a drop-down on a Robot Studio block.</summary>
    public enum ComparisonNullPolicy
    {
        /// <summary>A null on either side makes the comparison invalid. This is the default.</summary>
        RequireValue = 0,

        /// <summary>Two nulls are equal; a null against a valid value is a difference. A missing field is still invalid.</summary>
        AllowBothNull = 1
    }
}
