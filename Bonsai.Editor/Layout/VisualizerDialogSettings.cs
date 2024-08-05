using System.Drawing;
using System.Xml.Serialization;
using System.Collections.ObjectModel;
using System.Xml.Linq;
using System.Windows.Forms;
using System;

namespace Bonsai.Design
{
#pragma warning disable CS0612 // Type or member is obsolete
    [XmlInclude(typeof(WorkflowEditorSettings))]
#pragma warning restore CS0612 // Type or member is obsolete
    public class VisualizerDialogSettings
    {
        public bool Visible { get; set; }

        public Point Location { get; set; }

        public Size Size { get; set; }

        public FormWindowState WindowState { get; set; }

        [XmlIgnore]
        public Rectangle Bounds
        {
            get { return new Rectangle(Location, Size); }
            set
            {
                Location = value.Location;
                Size = value.Size;
            }
        }

        public string VisualizerTypeName { get; set; }

        public XElement VisualizerSettings { get; set; }

        // [Obsolete]
        public Collection<int> Mashups { get; } = new Collection<int>();

        public bool MashupsSpecified
        {
            get { return false; }
        }

        internal static VisualizerDialogSettings FromBuilderSettings(BuilderLayoutSettings builderSettings)
        {
            var other = builderSettings.VisualizerDialogSettings;
            return other is null ? default : new()
            {
                Visible = other.Visible,
                Location = other.Location,
                Size = other.Size,
                WindowState = other.WindowState,
                VisualizerTypeName = other.VisualizerTypeName,
                VisualizerSettings = other.VisualizerSettings
            };
        }
    }
}
