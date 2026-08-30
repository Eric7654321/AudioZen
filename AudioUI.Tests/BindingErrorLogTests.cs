using Xunit;

namespace AudioUI.Tests
{
    public class BindingErrorLogTests
    {
        [Fact]
        public void 沒收到東西時是空的()
        {
            var log = new BindingErrorLog();

            Assert.True(log.IsEmpty);
            Assert.Equal(0, log.DistinctCount);
            Assert.Equal("繫結全部解得開。", log.Summary);
        }

        [Fact]
        public void 同一條錯誤只留一份但次數照算()
        {
            // 壞掉的繫結寫在 DataTemplate 裡，畫面上有幾筆就報幾次；
            // 不去重的話一個錯誤會洗掉整張清單。
            var log = new BindingErrorLog();
            for (int i = 0; i < 50; i++) log.Record("BindingExpression path error: 'Foo'");

            Assert.Equal(1, log.DistinctCount);
            Assert.Equal(50, log.TotalCount);
            Assert.Single(log.Messages);
        }

        [Fact]
        public void 第一次看到才回報為新的()
        {
            var log = new BindingErrorLog();

            Assert.True(log.Record("A"));
            Assert.False(log.Record("A"));
            Assert.True(log.Record("B"));
        }

        [Fact]
        public void 前後空白不算兩條()
        {
            var log = new BindingErrorLog();
            log.Record("  path error: 'Foo'  ");
            log.Record("path error: 'Foo'");

            Assert.Equal(1, log.DistinctCount);
        }

        [Fact]
        public void 空訊息不算一筆()
        {
            var log = new BindingErrorLog();
            log.Record(null);
            log.Record("");
            log.Record("   ");

            Assert.True(log.IsEmpty);
            Assert.Equal(0, log.TotalCount);
        }

        [Fact]
        public void 超過上限只計數不再留內容()
        {
            var log = new BindingErrorLog(limit: 3);
            for (int i = 0; i < 10; i++) log.Record($"error {i}");

            Assert.Equal(10, log.DistinctCount);
            Assert.Equal(3, log.Messages.Count);
            Assert.Contains("另有 7 種未列出", log.Report());
        }

        [Fact]
        public void 報告列出每一條並講出總數()
        {
            var log = new BindingErrorLog();
            log.Record("第一條");
            log.Record("第二條");
            log.Record("第一條");

            string report = log.Report();

            Assert.Contains("有 2 種繫結解不開（共 3 次）。", report);
            Assert.Contains("1. 第一條", report);
            Assert.Contains("2. 第二條", report);
        }
    }
}
