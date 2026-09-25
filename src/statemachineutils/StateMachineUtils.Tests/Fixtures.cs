using Xunit;
using System;
using System.Collections.Generic;
using System.IO;

namespace StateMachineAutomation.Tests
{
    internal static class Defs
    {
        /// <summary>Received -> Validated -> Posted (guarded on amount) with an ordered Failed fallback and a wildcard abort.</summary>
        internal const string Invoice = """
            {
              "name": "InvoiceFlow",
              "initial": "Received",
              "states": [ { "name": "Received" }, { "name": "Validated" }, { "name": "Posted", "final": true }, { "name": "Failed", "final": true } ],
              "transitions": [
                { "from": "Received", "trigger": "validate", "to": "Validated" },
                { "from": "Validated", "trigger": "post", "to": "Posted", "guards": [ { "key": "amount", "op": "lessThan", "value": "10000" } ] },
                { "from": "Validated", "trigger": "post", "to": "Failed" },
                { "from": "*", "trigger": "abort", "to": "Failed" }
              ]
            }
            """;

        internal const string Minimal = """
            { "initial": "A", "states": [ "A", { "name": "B", "final": true } ], "transitions": [ { "from": "A", "trigger": "go", "to": "B" } ] }
            """;
    }

    internal static class MachineTestExtensions
    {
        /// <summary>
        /// The detailed view of <c>Fire</c> most tests want: whether it fired, and the reason it was declined. It is a thin
        /// reading of the real contract (result = the call worked; message empty = fired, text = the decline reason), so
        /// every test that uses it exercises the shipped <c>Fire</c>. On failure <c>message</c> is the error text.
        /// </summary>
        internal static bool Fire(this StateMachineUtils machine, string trigger, out bool fired, out string newState, out string reason, out string message)
        {
            bool ok = machine.Fire(trigger, out newState, out string text);
            fired = ok && text == null;
            reason = ok ? text : null;
            message = ok ? null : text;
            return ok;
        }
    }

    /// <summary>Creates components and cleans up any persisted machine folders a test made.</summary>
    public abstract class MachineTestBase : IDisposable
    {
        private readonly List<StateMachineUtils> components = new List<StateMachineUtils>();
        private readonly List<string> machineNames = new List<string>();

        protected StateMachineUtils New()
        {
            var machine = new StateMachineUtils();
            components.Add(machine);
            return machine;
        }

        protected StateMachineUtils Loaded(string json = Defs.Invoice)
        {
            StateMachineUtils machine = New();
            Assert.True(machine.LoadDefinitionJson(json, out string message), message);
            return machine;
        }

        protected StateMachineUtils Started(string json = Defs.Invoice)
        {
            StateMachineUtils machine = Loaded(json);
            Assert.True(machine.Start(out _, out string message), message);
            return machine;
        }

        protected string NewMachineName()
        {
            string name = "smtest-" + Guid.NewGuid().ToString("N");
            machineNames.Add(name);
            return name;
        }

        public virtual void Dispose()
        {
            foreach (StateMachineUtils machine in components) machine.Dispose();
            foreach (string name in machineNames)
            {
                string folder = Path.Combine(StateMachineCore.BasePath, name);
                if (!Directory.Exists(folder)) continue;
                if (!OperatingSystem.IsWindows())
                {
                    try { File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch (Exception) { /* folder already gone */ }
                }
                Directory.Delete(folder, true);
            }
        }
    }
}
