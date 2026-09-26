using System;
using System.Globalization;

namespace ReconciliationAutomation.Tests
{
    /// <summary>Runs an action under another culture (both the formatting culture and the UI culture) and puts back exactly what was there before.</summary>
    internal static class CultureScope
    {
        /// <summary>Returns false, without running the action, on a host that has no data for the culture (there is then nothing to prove).</summary>
        internal static bool Run(string cultureName, Action action)
        {
            CultureInfo culture;
            try { culture = new CultureInfo(cultureName); }
            catch (CultureNotFoundException) { return false; }

            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                action();
                return true;
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }
    }
}
