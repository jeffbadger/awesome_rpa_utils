using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace StateMachineAutomation.Tests
{
    /// <summary>
    /// Proves that every public method re-checks liveness inside its own critical section. The fast-path check at the top
    /// of a method is not enough: another thread can dispose the component between that check and the lock. The
    /// <c>AfterLivenessCheck</c> seam disposes the component in exactly that window, deterministically.
    /// </summary>
    public sealed class DisposalTests : MachineTestBase
    {
        // Methods with no critical section of their own: a pure function of its argument that never touches component state.
        private static readonly string[] Exempt = { "ValidateDefinitionJson" };

        private static readonly string[] Names =
        {
            "Fire", "CanFire", "Start", "Reset", "GetCurrentState", "IsStarted", "IsInFinalState",
            "GetAvailableTriggersDelimited", "GetAvailableTriggersJson", "GetSecondsInState", "GetHistoryJson",
            "SetContext", "GetContext", "RemoveContext", "ClearContext", "GetContextJson",
            "LoadDefinitionJson", "GetDefinitionJson", "AddState", "SetInitialState", "AddTransition", "ClearDefinition",
            "EnablePersistence", "DisablePersistence", "DiscardPersistedState", "SetMaximumHistoryEntries"
        };

        public static IEnumerable<object[]> MethodNames() => Names.Select(n => new object[] { n });

        private static bool Call(StateMachineUtils m, string name, out string message)
        {
            message = null;
            switch (name)
            {
                case "Fire": return m.Fire("validate", out _, out _, out _, out message);
                case "CanFire": return m.CanFire("validate", out _, out _, out message);
                case "Start": return m.Start(out _, out message);
                case "Reset": return m.Reset(false, out _, out message);
                case "GetCurrentState": return m.GetCurrentState(out _, out message);
                case "IsStarted": return m.IsStarted(out _, out message);
                case "IsInFinalState": return m.IsInFinalState(out _, out message);
                case "GetAvailableTriggersDelimited": return m.GetAvailableTriggersDelimited(out _, out message);
                case "GetAvailableTriggersJson": return m.GetAvailableTriggersJson(out _, out message);
                case "GetSecondsInState": return m.GetSecondsInState(out _, out message);
                case "GetHistoryJson": return m.GetHistoryJson(out _, out message);
                case "SetContext": return m.SetContext("k", "v", out message);
                case "GetContext": return m.GetContext("k", out _, out _, out message);
                case "RemoveContext": return m.RemoveContext("k", out _, out message);
                case "ClearContext": return m.ClearContext(out message);
                case "GetContextJson": return m.GetContextJson(out _, out message);
                case "LoadDefinitionJson": return m.LoadDefinitionJson(Defs.Minimal, out message);
                case "GetDefinitionJson": return m.GetDefinitionJson(out _, out message);
                case "AddState": return m.AddState("Extra", out message);
                case "SetInitialState": return m.SetInitialState("Received", out message);
                case "AddTransition": return m.AddTransition("Received", "t", "Validated", out message);
                case "ClearDefinition": return m.ClearDefinition(out message);
                case "EnablePersistence": return m.EnablePersistence("smtest-never-created", out _, out _, out message);
                case "DisablePersistence": return m.DisablePersistence(out message);
                case "DiscardPersistedState": return m.DiscardPersistedState("smtest-never-created", out _, out message);
                case "SetMaximumHistoryEntries": return m.SetMaximumHistoryEntries(5, out message);
                default: throw new ArgumentException("unknown method " + name);
            }
        }

        [Theory]
        [MemberData(nameof(MethodNames))]
        public void EveryPublicMethod_ReChecksLivenessInsideItsCriticalSection(string name)
        {
            StateMachineUtils machine = Started();
            int maximumBefore = machine.MaximumHistoryEntries;
            machine.AfterLivenessCheck = () => { machine.AfterLivenessCheck = null; machine.Dispose(); }; // dispose in the gap

            bool ok = Call(machine, name, out string message);

            Assert.False(ok);
            Assert.Contains("disposed", message);
            Assert.Equal(maximumBefore, machine.MaximumHistoryEntries); // and nothing was changed on the disposed component
        }

        [Fact]
        public void EveryPublicMethodOfTheComponent_IsCoveredAboveOrExplicitlyExempt()
        {
            string[] actual = typeof(StateMachineUtils)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .Distinct()
                .ToArray();
            string[] uncovered = actual.Except(Names).Except(Exempt).ToArray();
            Assert.True(uncovered.Length == 0, "add these to DisposalTests (or to Exempt with a reason): " + string.Join(", ", uncovered));
        }

        [Fact]
        public void AMethodDisposedInTheGap_LeavesTheMaximumHistoryUntouched_SpecificallyForTheSetterFinding()
        {
            StateMachineUtils machine = Started();
            machine.AfterLivenessCheck = () => { machine.AfterLivenessCheck = null; machine.Dispose(); };
            Assert.False(machine.SetMaximumHistoryEntries(7, out string message));
            Assert.Contains("disposed", message);
            Assert.Equal(100, machine.MaximumHistoryEntries);
        }
    }
}
