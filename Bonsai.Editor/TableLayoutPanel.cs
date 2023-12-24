using System.Drawing;
using System.Windows.Forms;
using Bonsai.Editor;

namespace Bonsai.Design
{
    internal class TableLayoutPanel : System.Windows.Forms.TableLayoutPanel
    {
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            if (EditorSettings.IsRunningOnMono)
            {
                foreach (ColumnStyle style in ColumnStyles)
                {
                    if (style.SizeType == SizeType.Absolute)
                    {
                        style.Width *= factor.Width;
                    }
                }

                foreach (RowStyle style in RowStyles)
                {
                    if (style.SizeType == SizeType.Absolute)
                    {
                        style.Height *= factor.Height;
                    }
                }
            }

            base.ScaleControl(factor, specified);
        }
    }
}
