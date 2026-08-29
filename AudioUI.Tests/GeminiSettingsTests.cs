using Xunit;

namespace AudioUI.Tests
{
    public class GeminiSettingsTests
    {
        [Fact]
        public void BuildGenerateContentUrl_組出帶_key_的網址()
        {
            var s = new GeminiSettings { ApiKey = "K", Model = "m", Endpoint = "https://example.test/v1" };
            Assert.Equal("https://example.test/v1/models/m:generateContent?key=K", s.BuildGenerateContentUrl());
        }

        [Fact]
        public void BuildGenerateContentUrl_容忍結尾斜線()
        {
            var s = new GeminiSettings { ApiKey = "K", Model = "m", Endpoint = "https://example.test/v1/" };
            Assert.Equal("https://example.test/v1/models/m:generateContent?key=K", s.BuildGenerateContentUrl());
        }

        [Fact]
        public void 沒有_key_時丟出可行動的訊息()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new GeminiSettings().BuildGenerateContentUrl());
            Assert.Contains("appsettings.json", ex.Message);
            Assert.Contains(GeminiSettings.ApiKeyEnvironmentVariable, ex.Message);
        }
    }
}
