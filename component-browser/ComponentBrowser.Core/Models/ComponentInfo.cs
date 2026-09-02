using System.Collections.Generic;

namespace ComponentBrowser
{
    /// <summary>Which kind of reflected member a <see cref="PmeInfo"/> represents.</summary>
    public enum PmeKind
    {
        Property,
        Method,
        Event
    }

    /// <summary>
    /// One reflected Property/Method/Event on a component type, as read directly from the
    /// DLL via <see cref="AssemblyInspector"/> - the ground truth, independent of whether the
    /// shipped documentation agrees with it.
    /// </summary>
    public class PmeInfo
    {
        public PmeKind Kind { get; set; }
        public string Name { get; set; }
        public string Signature { get; set; }

        /// <summary>From the reflected <c>[Category]</c> attribute, or null if absent.</summary>
        public string Category { get; set; }

        /// <summary>From the reflected <c>[Description]</c> attribute, or null if absent.</summary>
        public string DescriptionFromAttribute { get; set; }
    }

    /// <summary>
    /// One Component-derived type found in a DLL, with every public PME declared on it.
    /// </summary>
    public class ComponentInfo
    {
        public string AssemblyName { get; set; }
        public string TypeName { get; set; }
        public string DllPath { get; set; }
        public List<PmeInfo> Members { get; set; } = new List<PmeInfo>();
    }
}
