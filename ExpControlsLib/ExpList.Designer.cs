using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

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
                if (disposing && components != null)
                {
                    components.Dispose();
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
            _ListView = new ListView();
            SuspendLayout();
            // 
            // _ListView
            // 
            _ListView.BorderStyle = BorderStyle.FixedSingle;
            _ListView.Dock = DockStyle.Fill;
            _ListView.FullRowSelect = true;
            _ListView.LabelEdit = true;
            _ListView.Location = new Point(0, 0);
            _ListView.Margin = new Padding(4, 3, 4, 3);
            _ListView.Name = "_ListView";
            _ListView.Size = new Size(817, 346);
            _ListView.TabIndex = 2;
            _ListView.UseCompatibleStateImageBehavior = false;
            _ListView.View = View.Details;
            // 
            // ExpList
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_ListView);
            Margin = new Padding(4, 3, 4, 3);
            Name = "ExpList";
            Size = new Size(817, 346);
            ResumeLayout(false);
        }

        internal ListView _ListView;

        #endregion
    }
}