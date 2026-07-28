using ExpControlsLib;
using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace Demo_CS
{
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public partial class Form1 : Form
    {
        private bool _initialized = false;

        public Form1()
        {
            InitializeComponent();

            expTree1.Initialize(ShellController.Instance);
            expList1.Initialize(ShellController.Instance);

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.expTree1.AllowDrop = true;

            _ = this.expTree1.ExpandANodeAsync("C:\\temp");

            _initialized = true;
        }

        //Load files to ExpFileList
        private async void expTree1_ExpTreeNodeSelected(string SelPath, CShellItem Item)
        {
            bool includeFolder = true;
            await this.expList1.LoadDirectoryAsync(Item, includeFolder);
        }

        private void expList1_ExpListItemDoubleClick(string SelPath, CShellItem Item)
        {
            if (string.IsNullOrEmpty(SelPath)) return;

            try
            {
                Process.Start(new ProcessStartInfo(SelPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void expList1_ItemSelectionChanged(ListViewItemSelectionChangedEventArgs e)
        {
            var x = e.Item;


        }

        private void expList1_ExpListCurrentFolderChanged(CShellItem newCsi, CShellItem oldCsi)
        {
            if (!_initialized) return;

            this.expTree1.ExpandANodeAsync(newCsi);
        }
    }
}
