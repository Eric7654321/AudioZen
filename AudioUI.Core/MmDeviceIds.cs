namespace AudioUI
{
    /// <summary>
    /// 音訊裝置 id 在兩種表示法之間的轉換。
    ///
    /// 列舉裝置拿到的是 <c>{0.0.0.00000000}.{guid}</c>，但要指定某個程式的輸出裝置時，
    /// 系統要的是包成裝置介面路徑的形式。差一個字元就是靜靜地沒有作用——
    /// 那個 API 不會抱怨你給了一個不存在的裝置，所以這段轉換單獨拉出來測。
    /// </summary>
    public static class MmDeviceIds
    {
        /// <summary>輸出裝置的裝置介面 GUID。</summary>
        public const string RenderInterface = "{e6327cad-dcec-4949-ae8a-991e976a79d2}";

        /// <summary>輸入裝置的裝置介面 GUID。</summary>
        public const string CaptureInterface = "{2eef81be-33fa-4800-9670-1cd474972c3f}";

        public const string Prefix = @"\\?\SWD#MMDEVAPI#";

        /// <summary>把列舉到的裝置 id 包成指定輸出裝置時要用的形式。</summary>
        public static string ToPolicyId(string? mmDeviceId, bool render = true) =>
            string.IsNullOrWhiteSpace(mmDeviceId)
                ? ""
                : $"{Prefix}{mmDeviceId.Trim()}#{(render ? RenderInterface : CaptureInterface)}";

        /// <summary>反向拆回列舉時看到的樣子，用來比對「現在指到哪裡」。</summary>
        public static string FromPolicyId(string? policyId)
        {
            string id = (policyId ?? "").Trim();
            if (id.Length == 0) return "";

            if (id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                id = id[Prefix.Length..];

            foreach (string suffix in new[] { "#" + RenderInterface, "#" + CaptureInterface })
            {
                if (id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    id = id[..^suffix.Length];
                    break;
                }
            }

            return id;
        }
    }
}
