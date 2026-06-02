using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{ 
    partial class ExpTree: System.Windows.Forms.UserControl
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
            try
            {
                if (disposing && components != null)
                {
                    components.Dispose();
                }
                CShellItemUpdater.UpdateEvent -= OnItemUpdate;
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            _TreeView = new TreeView();
            SuspendLayout();
            // 
            // _TreeView
            // 
            _TreeView.BackColor = SystemColors.Window;
            _TreeView.Dock = DockStyle.Fill;
            _TreeView.ForeColor = SystemColors.ControlText;
            _TreeView.HideSelection = false;
            _TreeView.HotTracking = true;
            _TreeView.Location = new Point(0, 0);
            _TreeView.Name = "_TreeView";
            _TreeView.ShowRootLines = false;
            _TreeView.Size = new Size(200, 264);
            _TreeView.TabIndex = 1;
            _TreeView.BeforeLabelEdit += Tv1_BeforeLabelEdit;
            _TreeView.AfterLabelEdit += Tv1_AfterLabelEdit;
            _TreeView.BeforeCollapse += Tv1_BeforeCollapse;
            _TreeView.BeforeExpand += Tv1_BeforeExpand;
            _TreeView.AfterSelect += Tv1_AfterSelect;
            _TreeView.VisibleChanged += Tv1_VisibleChanged;
            _TreeView.HandleCreated += Tv1_HandleCreated;
            _TreeView.HandleDestroyed += Tv1_HandleDestroyed;
            _TreeView.KeyPress += Tv1_KeyPress;
            _TreeView.KeyUp += Tv1_KeyUp;
            _TreeView.MouseUp += ExpTree_MouseUp;
            // 
            // ExpTree
            // 
            Controls.Add(_TreeView);
            Name = "ExpTree";
            Size = new Size(200, 264);
            ResumeLayout(false);
        }

        private TreeView _TreeView;

        #endregion
    }
}