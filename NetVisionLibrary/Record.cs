using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace NetVisionLibrary
{
    [JsonConverter(typeof(RecordConverter))]
    public class Record : INotifyPropertyChanged
    {
        private Dictionary<string, string> properties = new Dictionary<string, string>();
        private List<string> columnOrder = new List<string>();

        public event PropertyChangedEventHandler? PropertyChanged;

        public Dictionary<string, string> Properties
        {
            get => properties;
            set
            {
                properties = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Properties)));
            }
        }

        [JsonIgnore]
        public List<string> ColumnOrder
        {
            get => columnOrder;
            set => columnOrder = value;
        }

        public string? this[string key]
        {
            get => properties.ContainsKey(key) ? properties[key] : null;
            set
            {
                properties[key] = value ?? string.Empty; // Handle null by setting to empty string
                if (!columnOrder.Contains(key))
                {
                    columnOrder.Add(key);
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
            }
        }

        public Record()
        {
        }
    }
}