using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace ExpControlsLib
{
    internal class VirtualListViewWrapper
    {
        private ListView _listView;

        public VirtualListViewWrapper(ListView listView)
        {
            _listView = listView;
        }
    }
}
