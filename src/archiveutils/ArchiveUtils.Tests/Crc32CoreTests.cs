using System.IO;
using System.Text;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>Verifies the hand-rolled CRC-32 implementation against known test vectors before anything else in this component relies on it.</summary>
    public class Crc32CoreTests
    {
        [Fact]
        public void Compute_EmptyInput_ReturnsZero()
        {
            Assert.Equal(0x00000000u, Crc32Core.Compute(new byte[0]));
        }

        [Fact]
        public void Compute_KnownVector_MatchesStandardCrc32()
        {
            // The canonical CRC-32 (IEEE 802.3) test vector.
            Assert.Equal(0xCBF43926u, Crc32Core.Compute(Encoding.ASCII.GetBytes("123456789")));
        }

        [Fact]
        public void Compute_ByteArrayAndStreamOverloads_AgreeForTheSameContent()
        {
            byte[] data = Encoding.UTF8.GetBytes("hello world, this is some test content for crc checking");

            uint fromBytes = Crc32Core.Compute(data);
            uint fromStream;
            using (var ms = new MemoryStream(data))
                fromStream = Crc32Core.Compute(ms);

            Assert.Equal(fromBytes, fromStream);
        }

        [Fact]
        public void Compute_StreamLargerThanChunkBuffer_MatchesByteArrayResult()
        {
            var data = new byte[200_000];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 251);

            uint fromBytes = Crc32Core.Compute(data);
            uint fromStream;
            using (var ms = new MemoryStream(data))
                fromStream = Crc32Core.Compute(ms);

            Assert.Equal(fromBytes, fromStream);
        }
    }
}
