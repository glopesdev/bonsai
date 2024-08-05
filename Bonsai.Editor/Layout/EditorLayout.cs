using System.Xml.Serialization;

namespace Bonsai.Design
{
    public class EditorLayout
    {
        [XmlElement(nameof(BuilderSettings))]
        public BuilderLayoutSettingsCollection BuilderSettings { get; } = new BuilderLayoutSettingsCollection();

        public static XmlSerializer Serializer
        {
            get { return SerializerFactory.instance; }
        }

        #region SerializerFactory

        static class SerializerFactory
        {
            internal static readonly XmlSerializer instance = new(typeof(EditorLayout));
        }

        #endregion
    }
}
