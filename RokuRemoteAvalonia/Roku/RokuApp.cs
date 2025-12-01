using System.Collections.Generic;
using System.Xml.Serialization;
using Avalonia.Media.Imaging;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RRemote.Roku
{
    public class RokuApp : INotifyPropertyChanged
    {
        private Bitmap? _iconImage;

        [XmlAttribute("id")] 
        public string Id { get; set; } = string.Empty;

        [XmlText] 
        public string Name { get; set; } = string.Empty;

        [XmlAttribute("type")]
        public string Type { get; set; } = string.Empty;

        [XmlAttribute("version")] 
        public string Version { get; set; } = string.Empty;

        [XmlIgnore]
        public bool IsSelected { get; set; }

        [XmlIgnore]
        public string NumberAndName => $"{Id} {Name}";

        [XmlIgnore]
        public string Endpoint { get; set; } = string.Empty;
        
        [XmlIgnore]
        public string IconUrl => Endpoint + "query/icon/" + Id;
        
        // Add property for the actual bitmap image with proper change notification
        [XmlIgnore]
        public Bitmap? IconImage 
        { 
            get => _iconImage; 
            set 
            { 
                if (_iconImage != value)
                {
                    _iconImage = value;
                    OnPropertyChanged();
                }
            } 
        }
        
        // Keep the original Icon property for backward compatibility, but now return the IconUrl
        [XmlIgnore]
        public string Icon => IconUrl;

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [XmlRoot("apps")]
    public class DeviceAppWrapper
    {
        [XmlElement("app")] 
        public List<RokuApp> Apps { get; set; } = new();
    }
}