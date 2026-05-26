using System;
using System.Collections.Generic;
using System.Text;
using Windows.Win32;

namespace ExpControlsLib
{
    /// <summary>
    /// this is basically a superset of the View enum from Forms.View.  
    /// The original values from user32.h doesn't include values to support thumbnails in XP (and later).
    /// </summary>
    public enum ListViewDisplayMode
    {
        /// <summary>
        ///  Each item appears as a full-sized icon with a label below it.
        /// </summary>
        LargeIcon = 0, // LV_VIEW_ICON

        /// <summary>
        ///  Each item appears on a separate line with further
        ///  information about each item arranged in columns. The left
        ///  most column
        ///  contains a small icon and
        ///  label, and subsequent columns contain subitems as specified by the application. A
        ///  column displays a header which can display a caption for the
        ///  column. The user can resize each column at runtime.
        /// </summary>
        Details = 1, // LV_VIEW_DETAILS

        /// <summary>
        ///  Each item appears as a small icon with a label to its right.
        /// </summary>
        SmallIcon = 2, // LV_VIEW_SMALLICON

        /// <summary>
        ///  Each item
        ///  appears as a small icon with a label to its right.
        ///  Items are arranged in columns with no column headers.
        /// </summary>
        List = 3, // LV_VIEW_LIST

        /// <summary>
        ///  Tile view.
        /// </summary>
        Tile = 4, // LV_VIEW_TILE

        //I don't know why there is no setting for medium icons which are 32px

        /// new values below

        /// <summary>
        /// </summary>
        Thumbnail = 5,

        /// <summary>
        /// 
        /// </summary>
        LargeThumbnail = 6,

        /// <summary>
        /// 
        /// </summary>
        ExtraLargeThumbnail = 7

    }
}
