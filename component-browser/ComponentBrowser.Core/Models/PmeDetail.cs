namespace ComponentBrowser
{
    /// <summary>
    /// A <see cref="PmeInfo"/> enriched with everything <see cref="ComponentCatalogBuilder"/>
    /// could cross-reference from the component's shipped documentation.
    /// </summary>
    public class PmeDetail
    {
        public PmeInfo Pme { get; set; }

        /// <summary>
        /// The best available description: the matching README table row's text if one was
        /// found, otherwise the reflected <c>[Description]</c> attribute text, otherwise null.
        /// </summary>
        public string Description { get; set; }

        /// <summary>The component's whole "## Notes &amp; Caveats" section, or null if the README has none.</summary>
        public string NotesAndCaveats { get; set; }

        /// <summary>
        /// Path (inside the extracted documentation bundle) to the <c>Documentation/&lt;Category&gt;.md</c>
        /// worked-example page for this PME's category, or null if the component has no
        /// <c>Documentation/</c> folder or no page matching this category.
        /// </summary>
        public string WorkedExamplePath { get; set; }
    }

    /// <summary>One component plus the detail-enriched view of every PME on it.</summary>
    public class ComponentCatalogEntry
    {
        public ComponentInfo Component { get; set; }
        public System.Collections.Generic.List<PmeDetail> Details { get; set; }
    }
}
