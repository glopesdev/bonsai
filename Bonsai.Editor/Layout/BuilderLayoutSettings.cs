using System.Xml.Serialization;

namespace Bonsai.Design
{
    public class BuilderLayoutSettings
    {
        [XmlIgnore]
        public int? Index { get; set; }

        [XmlAttribute(nameof(Index))]
        public string IndexXml
        {
            get => Index.HasValue ? Index.GetValueOrDefault().ToString() : null;
            set => Index = !string.IsNullOrEmpty(value) ? int.Parse(value) : null;
        }

        [XmlIgnore]
        public object Tag { get; set; }

        public bool IsNestedExpanded { get; set; }

        public EditorLayout NestedLayout { get; set; }

        public VisualizerDialogSettings VisualizerDialogSettings { get; set; }

        public bool IsNestedExpandedSpecified => IsNestedExpanded;

        public bool NestedLayoutSpecified => NestedLayout?.BuilderSettings.Count > 0;
    }
}
