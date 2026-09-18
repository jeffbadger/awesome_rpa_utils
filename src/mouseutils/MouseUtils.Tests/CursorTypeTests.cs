using System;
using System.Linq;
using MouseAutomation;
using Xunit;

namespace MouseAutomation.Tests
{
    /// <summary>
    /// Tests for the logic behind <see cref="MouseUtils.GetCurrentCursorType"/> that
    /// doesn't need a live desktop: how a GetCursorInfo result is classified against the
    /// system cursor slots, and that <see cref="CurrentCursorType"/> stays in step with
    /// <see cref="SystemCursorType"/>. The real Win32 behavior (which cursor a control
    /// actually shows) needs the interactive harness - see TESTING.md.
    /// </summary>
    public class CursorTypeTests
    {
        private const int Showing = 0x1;

        // A loader giving every system cursor slot its own distinct, nonzero handle.
        private static IntPtr HandleFor(SystemCursorType slot) => new IntPtr(1000 + (int)slot);

        public static TheoryData<SystemCursorType> AllSlots()
        {
            var data = new TheoryData<SystemCursorType>();
            foreach (SystemCursorType slot in Enum.GetValues(typeof(SystemCursorType)))
                data.Add(slot);
            return data;
        }

        [Theory]
        [MemberData(nameof(AllSlots))]
        public void ClassifyCursor_EveryStandardSlot_MapsToTheSameNamedType(SystemCursorType slot)
        {
            CurrentCursorType result = MouseUtils.ClassifyCursor(Showing, HandleFor(slot), HandleFor);

            Assert.Equal(slot.ToString(), result.ToString());
        }

        [Fact]
        public void ClassifyCursor_UnrecognizedHandle_IsUnknown()
        {
            Assert.Equal(CurrentCursorType.Unknown,
                MouseUtils.ClassifyCursor(Showing, new IntPtr(7), HandleFor));
        }

        [Fact]
        public void ClassifyCursor_NoSlotResolves_IsUnknownRatherThanMatchingZero()
        {
            // If LoadCursor fails for every slot (returns zero), a real handle must not
            // "match" one of them.
            Assert.Equal(CurrentCursorType.Unknown,
                MouseUtils.ClassifyCursor(Showing, new IntPtr(42), _ => IntPtr.Zero));
        }

        [Theory]
        [InlineData(0)]   // not showing
        [InlineData(2)]   // CURSOR_SUPPRESSED (touch) without CURSOR_SHOWING
        public void ClassifyCursor_ShowingBitClear_IsHidden(int flags)
        {
            // Even with a handle that would otherwise be recognized.
            Assert.Equal(CurrentCursorType.Hidden,
                MouseUtils.ClassifyCursor(flags, HandleFor(SystemCursorType.Arrow), HandleFor));
        }

        [Fact]
        public void ClassifyCursor_ShowingWithNoCursorHandle_IsHidden()
        {
            Assert.Equal(CurrentCursorType.Hidden,
                MouseUtils.ClassifyCursor(Showing, IntPtr.Zero, HandleFor));
        }

        [Fact]
        public void ClassifyCursor_OtherFlagBitsDoNotHideAShowingCursor()
        {
            Assert.Equal(CurrentCursorType.Hand,
                MouseUtils.ClassifyCursor(Showing | 2, HandleFor(SystemCursorType.Hand), HandleFor));
        }

        [Fact]
        public void ClassifyCursor_TwoSlotsSharingAHandle_FirstInEnumOrderWins()
        {
            // A cursor scheme can point two slots at one image; the answer is then the
            // first slot in enum order, and stays stable.
            IntPtr shared = new IntPtr(5);
            IntPtr Loader(SystemCursorType s) =>
                s == SystemCursorType.Hand || s == SystemCursorType.Arrow ? shared : HandleFor(s);

            Assert.Equal(CurrentCursorType.Arrow, MouseUtils.ClassifyCursor(Showing, shared, Loader));
        }

        [Fact]
        public void ClassifyCursor_StopsAtTheFirstMatch()
        {
            int loads = 0;
            IntPtr Loader(SystemCursorType s) { loads++; return HandleFor(s); }

            MouseUtils.ClassifyCursor(Showing, HandleFor(SystemCursorType.Arrow), Loader);

            Assert.Equal(1, loads);
        }

        // CurrentCursorType's standard members are cast straight from SystemCursorType's
        // numeric values, so they must never drift apart.
        [Fact]
        public void CurrentCursorType_StandardMembersMatchSystemCursorTypeByNameAndValue()
        {
            foreach (SystemCursorType slot in Enum.GetValues(typeof(SystemCursorType)))
            {
                Assert.True(Enum.IsDefined(typeof(CurrentCursorType), slot.ToString()), $"{slot} is missing from CurrentCursorType");
                Assert.Equal((int)slot, (int)Enum.Parse(typeof(CurrentCursorType), slot.ToString()));
            }
        }

        [Fact]
        public void CurrentCursorType_HasNoMembersBeyondTheSlotsPlusUnknownAndHidden()
        {
            string[] extras = Enum.GetNames(typeof(CurrentCursorType))
                .Except(Enum.GetNames(typeof(SystemCursorType)))
                .OrderBy(n => n)
                .ToArray();

            Assert.Equal(new[] { "Hidden", "Unknown" }, extras);
        }
    }
}
