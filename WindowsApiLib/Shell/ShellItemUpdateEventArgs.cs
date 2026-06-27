
namespace WindowsApiLib.Shell
{
    /// <summary>
    /// ShellItemUpdateEventArgs is used to pass information about a change in the Shell Namespace (that we actually care about) 
    ///   to an Event Handler.<br />
    /// See <see cref="WindowsApiLib.ShellItemUpdateEventArgs.UpdateType">UpdateType</see> for details.
    /// </summary>
    /// <remarks>
    /// </remarks>
    public class ShellItemUpdateEventArgs : EventArgs
    {
        private readonly CShellItem m_Item;
        private readonly CShItemUpdateType m_Type;

        public ShellItemUpdateEventArgs(CShellItem Item, CShItemUpdateType @type)
        {
            m_Item = Item;
            m_Type = type;
        }

        /// <summary>
        /// The CShellItem that changed.
        /// </summary>
        /// <returns>The CShellItem changed.</returns>
        /// <remarks>The precise role of this CShellItem in a change depends on the type of change.<br />
        /// See <see cref="WindowsApiLib.ShellItemUpdateEventArgs.UpdateType">UpdateType</see> for details.
        /// </remarks>
        public CShellItem Item
        {
            get
            {
                return m_Item;
            }
        }

        /// <summary>
        /// The type of change given as one of the CShItemUpdateType Enum values.
        /// </summary>
        /// <returns>The type of change given as one of the CShItemUpdateType Enum values.</returns>
        /// <remarks>The UpdateType has the following meaning:
        /// <table style="text-align: left" border="3">
        /// <caption>
        /// UpdateTypes</caption>  
        /// <tr>  
        /// <td style="width: 100px">  
        ///            <strong>UpdateType</strong></td>    
        ///                <td style="width: 181px">  
        ///                    <strong>sender</strong></td>  
        ///                <td style="width: 202px">  
        ///                    <strong>Item</strong></td>  
        ///                <td style="width: 295px">  
        ///                    <strong>  
        ///                    Occurs when:</strong></td>  
        ///            </tr>  
        ///            <tr>  
        ///                <td style="width: 100px">  
        ///                    Created
        ///                </td>
        ///                <td style="width: 181px">
        ///                    Folder of Item</td>
        ///                <td style="width: 202px">
        ///                    Newly Created Item</td>
        ///                <td style="width: 295px">
        ///                    Item has been created</td>
        ///            </tr>
        ///            <tr>
        ///                <td style="width: 100px">
        ///                    Deleted
        ///                </td>
        ///                <td style="width: 181px">
        ///                    Folder of Item</td>
        ///                <td style="width: 202px">
        ///                    Newly Deleted Item</td>
        ///                <td style="width: 295px">
        ///                    Item has been Deleted</td>
        ///            </tr>
        ///            <tr>
        ///                <td style="width: 100px">
        ///                    Renamed
        ///                </td>
        ///                <td style="width: 181px">
        ///                    Original Folder of Item</td>
        ///                <td style="width: 202px">
        ///                    Item that has been Renamed</td>
        ///                <td style="width: 295px">
        ///                    Item has been Renamed or Moved<span style="font-size: 8pt; vertical-align: super;
        ///                        font-family: Courier New">1</span></td>
        ///            </tr>
        ///            <tr>
        ///                <td style="width: 100px">
        ///                    Updated
        ///                </td>
        ///                <td style="width: 181px">
        ///                    Folder of Item</td>
        ///                <td style="width: 202px">
        ///                    Item that has changed</td>
        ///                <td style="width: 295px">
        ///                    Attributes of Item have changed</td>
        ///            </tr>
        ///            <tr>
        ///                <td style="width: 100px">
        ///                    UpdateDir
        ///                </td>
        ///                <td style="width: 181px">
        ///                    Folder that has Changed</td>
        ///                <td style="width: 202px">
        ///                    Folder that has Changed</td>
        ///                <td style="width: 295px">
        ///                    A Folder has had Items Added/Deleted<span style="font-size: 8pt; vertical-align: super;
        ///                        font-family: Courier New">2</span></td>
        ///            </tr>
        ///            <tr>
        ///                <td style="width: 100px">
        ///                    MediaChange</td>
        ///                <td style="width: 181px">
        ///                    Folder of Item</td>
        ///                <td style="width: 202px">
        ///                    CShellItem of Media</td>
        ///                <td style="width: 295px">
        ///                    When Media has been inserted or removed</td>
        ///            </tr>
        ///            <tr>
        ///                <td style="width: 100px">
        ///                    IconChange</td>
        ///                <td style="width: 181px">  
        ///                    Folder of Item</td>  
        ///                <td style="width: 202px">  
        ///                    Item that has changed</td>  
        ///                <td style="width: 295px">  
        ///                    When Icon has changed</td>  
        ///            </tr>  
        ///        </table> 
        ///        <br />
        ///     <span style="font-size: 8pt; vertical-align: super; font-family: Courier New">1</span>
        ///      In the Renamed case, sender is the Folder of the Item before it
        ///      was Renamed (or Moved). The Item may have moved to a new Folder, in which case,
        ///      the new Folder may be determined by e.Item.Parent.
        ///    <p>
        ///    <span style="font-size: 8pt; vertical-align: super; font-family: Courier New">2</span>
        ///      The UpdateDir UpdateType normally may be ignored since any Add or Deletes of Items
        ///      will have been already reported with previous Created and/or Deleted Events.
        /// </p>
        /// </remarks>
        public CShItemUpdateType UpdateType
        {
            get
            {
                return m_Type;
            }
        }

        /// <summary>
        /// For Renamed/Moved events: the FullPath of the item before the rename/move.
        /// Null for other update types.
        /// </summary>
        public string? OldPath { get; init; }

        /// <summary>
        /// For Renamed/Moved events: the FullPath of the item after the rename/move.
        /// Null for other update types.
        /// </summary>
        public string? NewPath { get; init; }
    }
}
