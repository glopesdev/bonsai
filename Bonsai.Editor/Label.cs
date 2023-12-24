using System.Drawing;
using System.Windows.Forms;
using Bonsai.Editor;

namespace Bonsai.Design
{
    internal class Label : System.Windows.Forms.Label
    {
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            if (EditorSettings.IsRunningOnMono)
            {
                var max = MaximumSize;
                MaximumSize = Size.Truncate(new SizeF(max.Width * factor.Width, max.Height * factor.Height));
            }

            base.ScaleControl(factor, specified);
        }
    }
}
