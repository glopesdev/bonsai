﻿using System.Xml.Serialization;

namespace Bonsai.Shaders.Configuration
{
    [XmlInclude(typeof(HintState))]
    [XmlInclude(typeof(EnableState))]
    [XmlInclude(typeof(DisableState))]
    [XmlInclude(typeof(ViewportState))]
    [XmlInclude(typeof(ScissorState))]
    [XmlInclude(typeof(LineWidthState))]
    [XmlInclude(typeof(PointSizeState))]
    [XmlInclude(typeof(DepthMaskState))]
    [XmlInclude(typeof(PolygonModeState))]
    [XmlInclude(typeof(BlendFunctionState))]
    [XmlInclude(typeof(DepthFunctionState))]
    [XmlInclude(typeof(MemoryBarrierState))]
    [XmlType(Namespace = Constants.XmlNamespace)]
    public abstract class StateConfiguration
    {
        public abstract void Execute(ShaderWindow window);
    }
}
