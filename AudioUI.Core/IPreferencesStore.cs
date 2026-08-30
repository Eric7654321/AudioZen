namespace AudioUI
{
    /// <summary>
    /// 使用者偏好的存取。抽成介面是為了讓設定頁在測試裡不必碰真實磁碟，
    /// 也讓「存在哪、存成什麼格式」不外洩到 UI。
    /// </summary>
    public interface IPreferencesStore
    {
        /// <summary>目前的偏好。永遠不是 null——讀不到檔案時是一份預設值。</summary>
        UserPreferences Current { get; }

        void Load();

        void Save();
    }
}
