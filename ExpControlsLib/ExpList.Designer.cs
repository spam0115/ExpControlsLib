using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{ 
    partial class ExpList : System.Windows.Forms.UserControl
    {

        // Required by the Windows Form Designer
        private IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    Cleanup();
                    CShellItemUpdater.UpdateEvent -= UpdateInvoke;
                    if (components != null)
                    {
                        components.Dispose();
                    }
                }
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
            _listView = new ListView();
            SuspendLayout();
            // 
            // _listView
            // 
            _listView.BorderStyle = BorderStyle.FixedSingle;
            _listView.Dock = DockStyle.Fill;
            _listView.FullRowSelect = true;
            _listView.LabelEdit = true;
            _listView.Location = new Point(0, 0);
            _listView.Margin = new Padding(4, 3, 4, 3);
            _listView.Name = "_listView";
            _listView.Size = new Size(817, 346);
            _listView.TabIndex = 2;
            _listView.UseCompatibleStateImageBehavior = false;
            _listView.View = View.Details;
            // 
            // ExpList
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_listView);
            Margin = new Padding(4, 3, 4, 3);
            Name = "ExpList";
            Size = new Size(817, 346);
            Load += ExpList_Load;
            ResumeLayout(false);
        }

        internal ListView _listView;

        #endregion
    }
}