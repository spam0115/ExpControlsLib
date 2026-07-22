namespace Demo_CS
{
    partial class Form1
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            splitContainer1 = new System.Windows.Forms.SplitContainer();
            expTree1 = new ExpControlsLib.ExpTree();
            expList1 = new ExpControlsLib.ExpList();
            columnHeader2 = new System.Windows.Forms.ColumnHeader();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer1.Location = new System.Drawing.Point(0, 0);
            splitContainer1.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(expTree1);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(expList1);
            splitContainer1.Size = new System.Drawing.Size(1652, 906);
            splitContainer1.SplitterDistance = 525;
            splitContainer1.SplitterWidth = 5;
            splitContainer1.TabIndex = 0;
            // 
            // expTree1
            // 
            expTree1.AllowFolderRename = true;
            expTree1.Dock = System.Windows.Forms.DockStyle.Fill;
            expTree1.Location = new System.Drawing.Point(0, 0);
            expTree1.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            expTree1.Name = "expTree1";
            expTree1.Root = null;
            expTree1.SelectedNode = null;
            expTree1.ShowRootLines = false;
            expTree1.Size = new System.Drawing.Size(525, 906);
            expTree1.StartUpDirectory = ExpControlsLib.ExpTree.StartDir.Desktop;
            expTree1.TabIndex = 0;
            expTree1.ExpTreeNodeSelected += expTree1_ExpTreeNodeSelected;
            // 
            // expList1
            // 
            expList1.CheckBoxes = true;
            expList1.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { columnHeader2 });
            expList1.CurrentFolderCsi = null;
            expList1.DisplayMode = ExpControlsLib.ListViewDisplayMode.Details;
            expList1.Dock = System.Windows.Forms.DockStyle.Fill;
            expList1.FullRowSelect = true;
            expList1.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Clickable;
            expList1.IsShuttingDown = false;
            expList1.LastMoveFolder = null;
            expList1.Location = new System.Drawing.Point(0, 0);
            expList1.Margin = new System.Windows.Forms.Padding(7, 6, 7, 6);
            expList1.MultiSelect = true;
            expList1.Name = "expList1";
            expList1.Size = new System.Drawing.Size(1122, 906);
            expList1.SortColumn = 0;
            expList1.TabIndex = 0;
            expList1.VerticalScrollPosition = 0;
            expList1.VirtualMode = true;
            expList1.ExpListItemDoubleClick += expList1_ExpListItemDoubleClick;
            expList1.ExpListCurrentFolderChanged += expList1_ExpListCurrentFolderChanged;
            expList1.ItemSelectionChanged += expList1_ItemSelectionChanged;
            // 
            // columnHeader2
            // 
            columnHeader2.Tag = ".DisplayName";
            columnHeader2.Text = "Name";
            columnHeader2.Width = 400;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1652, 906);
            Controls.Add(splitContainer1);
            Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            Name = "Form1";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "Form1";
            Load += Form1_Load;
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer1;
        private ExpControlsLib.ExpTree expTree1;
        private ExpControlsLib.ExpList expList1;
        private System.Windows.Forms.ColumnHeader columnHeader2;
    }
}

