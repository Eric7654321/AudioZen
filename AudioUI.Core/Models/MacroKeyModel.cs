using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioUI
{
    public class MacroKeyModel : INotifyPropertyChanged
    {
        public string KeyId { get; set; }
        public string KeyName { get; set; }

        private string _boundActionName;
        public string BoundActionName
        {
            get => _boundActionName;
            set { _boundActionName = value; OnPropertyChanged(); }
        }
        public string BoundConfigId { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
