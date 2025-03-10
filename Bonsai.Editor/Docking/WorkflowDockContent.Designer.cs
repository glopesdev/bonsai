﻿namespace Bonsai.Editor.Docking
{
    partial class WorkflowDockContent
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.tabContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.closeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.closeAllToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.closeOtherToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator = new System.Windows.Forms.ToolStripSeparator();
            this.openNewTabToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openNewWindowToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tabContextMenuStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabContextMenuStrip
            // 
            this.tabContextMenuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.tabContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.closeToolStripMenuItem,
            this.closeAllToolStripMenuItem,
            this.closeOtherToolStripMenuItem,
            this.toolStripSeparator,
            this.openNewTabToolStripMenuItem,
            this.openNewWindowToolStripMenuItem});
            this.tabContextMenuStrip.Name = "tabContextMenuStrip";
            this.tabContextMenuStrip.Size = new System.Drawing.Size(234, 120);
            this.tabContextMenuStrip.Opening += new System.ComponentModel.CancelEventHandler(this.tabContextMenuStrip_Opening);
            // 
            // closeToolStripMenuItem
            // 
            this.closeToolStripMenuItem.Name = "closeToolStripMenuItem";
            this.closeToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F4)));
            this.closeToolStripMenuItem.Size = new System.Drawing.Size(233, 22);
            this.closeToolStripMenuItem.Text = "Close";
            this.closeToolStripMenuItem.Click += new System.EventHandler(this.closeToolStripMenuItem_Click);
            // 
            // closeAllToolStripMenuItem
            // 
            this.closeAllToolStripMenuItem.Name = "closeAllToolStripMenuItem";
            this.closeAllToolStripMenuItem.Size = new System.Drawing.Size(233, 22);
            this.closeAllToolStripMenuItem.Text = "Close All Tabs";
            this.closeAllToolStripMenuItem.Click += new System.EventHandler(this.closeAllToolStripMenuItem_Click);
            // 
            // closeOtherToolStripMenuItem
            // 
            this.closeOtherToolStripMenuItem.Name = "closeOtherToolStripMenuItem";
            this.closeOtherToolStripMenuItem.Size = new System.Drawing.Size(233, 22);
            this.closeOtherToolStripMenuItem.Text = "Close Other Tabs";
            this.closeOtherToolStripMenuItem.Click += new System.EventHandler(this.closeOtherToolStripMenuItem_Click);
            // 
            // toolStripSeparator
            // 
            this.toolStripSeparator.Name = "toolStripSeparator";
            this.toolStripSeparator.Size = new System.Drawing.Size(230, 6);
            // 
            // openNewTabToolStripMenuItem
            // 
            this.openNewTabToolStripMenuItem.Name = "openNewTabToolStripMenuItem";
            this.openNewTabToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.T)));
            this.openNewTabToolStripMenuItem.Size = new System.Drawing.Size(233, 22);
            this.openNewTabToolStripMenuItem.Text = "Open in New Tab";
            this.openNewTabToolStripMenuItem.Click += new System.EventHandler(this.openNewTabToolStripMenuItem_Click);
            // 
            // openNewWindowToolStripMenuItem
            // 
            this.openNewWindowToolStripMenuItem.Name = "openNewWindowToolStripMenuItem";
            this.openNewWindowToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.N)));
            this.openNewWindowToolStripMenuItem.Size = new System.Drawing.Size(233, 22);
            this.openNewWindowToolStripMenuItem.Text = "Open in New Window";
            this.openNewWindowToolStripMenuItem.Click += new System.EventHandler(this.openNewWindowToolStripMenuItem_Click);
            // 
            // WorkflowDockContent
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Icon = global::Bonsai.Editor.Properties.Resources.Icon;
            this.Name = "WorkflowDockContent";
            this.TabPageContextMenuStrip = this.tabContextMenuStrip;
            this.Text = "Workflow";
            this.tabContextMenuStrip.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip tabContextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem closeAllToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem closeOtherToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator;
        private System.Windows.Forms.ToolStripMenuItem openNewTabToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openNewWindowToolStripMenuItem;
    }
}
