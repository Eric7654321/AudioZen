using Xunit;

namespace AudioUI.Tests
{
    public class MmDeviceIdTests
    {
        private const string RawId = "{0.0.0.00000000}.{0a4eba8e-e0ec-457a-90de-e84ce08d5844}";

        [Fact]
        public void 包成指定輸出裝置要用的形式()
        {
            string policy = MmDeviceIds.ToPolicyId(RawId);

            Assert.StartsWith(@"\\?\SWD#MMDEVAPI#", policy);
            Assert.EndsWith("#{e6327cad-dcec-4949-ae8a-991e976a79d2}", policy);
            Assert.Contains(RawId, policy);
        }

        [Fact]
        public void 輸入裝置用另一個介面_GUID()
        {
            Assert.EndsWith("#{2eef81be-33fa-4800-9670-1cd474972c3f}", MmDeviceIds.ToPolicyId(RawId, render: false));
        }

        [Fact]
        public void 包起來再拆開等於原本的()
        {
            Assert.Equal(RawId, MmDeviceIds.FromPolicyId(MmDeviceIds.ToPolicyId(RawId)));
            Assert.Equal(RawId, MmDeviceIds.FromPolicyId(MmDeviceIds.ToPolicyId(RawId, render: false)));
        }

        [Fact]
        public void 已經是原始形式的原樣回來()
        {
            Assert.Equal(RawId, MmDeviceIds.FromPolicyId(RawId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void 空的裝置_id_不會包出一個看起來合法的字串(string? id)
        {
            // 包出 "\\?\SWD#MMDEVAPI##{guid}" 的話，系統會收下然後什麼也不做。
            Assert.Equal("", MmDeviceIds.ToPolicyId(id));
            Assert.Equal("", MmDeviceIds.FromPolicyId(id));
        }

        [Fact]
        public void 前後空白會被去掉()
        {
            Assert.Contains(RawId, MmDeviceIds.ToPolicyId($"  {RawId}  "));
        }
    }
}
