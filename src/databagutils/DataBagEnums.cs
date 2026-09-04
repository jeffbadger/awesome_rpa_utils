namespace DataBagAutomation
{
    /// <summary>Supported data-bag value types.</summary>
    public enum DataBagValueType
    {
        /// <summary>A text value.</summary>
        String,
        /// <summary>A Boolean value.</summary>
        Boolean,
        /// <summary>A signed 32-bit integer.</summary>
        Int32,
        /// <summary>A signed 64-bit integer.</summary>
        Int64,
        /// <summary>A base-10 decimal value.</summary>
        Decimal,
        /// <summary>A double-precision floating-point value.</summary>
        Double,
        /// <summary>A date and time value.</summary>
        DateTime,
        /// <summary>Raw, syntactically valid JSON.</summary>
        Json,
        /// <summary>An explicitly null value.</summary>
        Null
    }

    /// <summary>Current lifecycle state of a data bag.</summary>
    public enum DataBagState
    {
        /// <summary>No definitions have been initialized.</summary>
        NotInitialized,
        /// <summary>Definitions are being staged.</summary>
        Initializing,
        /// <summary>The schema is sealed and available for runtime use.</summary>
        Ready,
        /// <summary>The component has released its values.</summary>
        Disposed
    }

    /// <summary>Selects the design-time initialization source.</summary>
    public enum DataBagInitializationSource
    {
        /// <summary>Initialize an empty bag.</summary>
        None,
        /// <summary>Load the InitialItemsJson property.</summary>
        DesignTimeJson,
        /// <summary>Load the InitialItemsFilePath file.</summary>
        JsonFile
    }

    /// <summary>Controls how initialization loads handle duplicate names.</summary>
    public enum DataBagConflictPolicy
    {
        /// <summary>Reject the complete load when a name already exists.</summary>
        Fail,
        /// <summary>Replace an existing definition.</summary>
        Replace,
        /// <summary>Keep an existing definition and skip its incoming duplicate.</summary>
        KeepExisting
    }

    /// <summary>Controls how runtime bulk updates handle unknown names.</summary>
    public enum DataBagUnknownNamePolicy
    {
        /// <summary>Reject the complete update when an input name is unknown.</summary>
        Fail,
        /// <summary>Skip unknown input names.</summary>
        Ignore
    }
}
