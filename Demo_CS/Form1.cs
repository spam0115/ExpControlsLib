using ExpControlsLib;
using System;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace Demo_CS
{
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.expTree1.StartUpDirectory = ExpControlsLib.ExpTree.StartDir.Desktop;
            this.expList1.DisplayMode = ListViewDisplayMode.Thumbnail;
        }

        //Load files to ExpFileList
        private void expTree1_ExpTreeNodeSelected(string SelPath, CShellItem Item)
        {
            bool includeFolder = true;
            this.expList1.DisplayFiles(SelPath, Item, includeFolder);
        }

        private void expList1_ExpListItemDoubleClick(string SelPath, CShellItem Item)
        {
            this.expTree1.ExpandANode(Item);
        }
    }
}
