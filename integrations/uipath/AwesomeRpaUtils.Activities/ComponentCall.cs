using System;

namespace AwesomeRpaUtils.Activities
{
    // Thin glue between the components' never-throw (bool, out message) surface
    // and Workflow Foundation, where a "false" result surfaces as an activity
    // error carrying the component's own message. Wrap the activity in a
    // Try Catch when "false" is an expected outcome.
    internal static class ComponentCall
    {
        internal static void EnsureSuccess(string activityName, bool succeeded, string message)
        {
            if (!succeeded)
                throw new Exception($"{activityName} failed: {message ?? "no further detail"}");
        }
    }
}