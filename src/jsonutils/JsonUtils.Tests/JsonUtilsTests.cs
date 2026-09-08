using JsonAutomation;
using Xunit;

namespace JsonAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for JsonUtils. This component is pure JSON/string
    /// logic over Newtonsoft.Json with no Win32 dependency, so unlike most of this suite's
    /// Windows-flavored components, every test here runs on Linux CI too.
    /// </summary>
    public class JsonUtilsTests
    {
        private readonly JsonUtils _json = new JsonUtils();

        [Fact]
        public void Constructor_DoesNotThrow()
        {
            Assert.NotNull(_json);
        }
    }
}
