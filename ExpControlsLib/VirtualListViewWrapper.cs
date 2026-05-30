using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows.Forms;
using WindowsApiLib.Shell;

namespace ExpControlsLib
{
    internal class VirtualListViewWrapper
    {
        private ListView _listView;

        TreeLib.HugeList<CShellItem> _virtualItems = new();

        //...more fields

        //...more properties

        private bool _useVirtualMode = true;
        [Browsable(true), Category("Behavior"), DefaultValue(false)]
        public bool VirtualMode
        {
            get => _useVirtualMode;
            set
            {
                if (_useVirtualMode == value) return;
                _useVirtualMode = value;
                _listView.VirtualMode = value;

                ///mode code here
            }
        }


        private SortOrder _sortOrder = SortOrder.None;

        public SortOrder Ascending
        {
            get
            {
                if (_useVirtualMode)
                    return _sortOrder;
                else
                    return _listView.Sorting;
            }
            set
            {
                if (_useVirtualMode)
                    _sortOrder = value;
                else
                    _listView.Sorting = value;
            }
        }


        public VirtualListViewWrapper(ListView listView)
        {
            _listView = listView;
        }

        //...more methods
    }
}
