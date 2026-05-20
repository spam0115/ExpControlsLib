using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WindowsApiLib;


namespace WindowsApiLib.Shell
{
    /// <summary>
    /// The purpose of this class is to maintain and manipulate a collection of CShellItems 
    /// and to do so in a hierachical structure.  The hierachical structure is to enable 
    /// navigation and updating of the shell items which have a hierachical relationship
    /// with each other in the Windows Shell namespace.
    /// </summary>
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class CShellItemHierachyManager
    {
        public CShellItem? Root {  get; set; }
        public CShellItem? CurrentFolder { get; set; }
        public string? CurrentPath { get {
                if (CurrentFolder?.PIDL == null) return string.Empty;
                return CPidl.ToString(CurrentFolder.PIDL);
            } }

        public CShellItemHierachyManager(CShellItem? root = null) {
            this.Root = root;

            //todo: move the item hierarchy code from cshellitem to over here.
        }

        public CShellItem AddToHierarchy(CShellItem csi)
        {
            var result = FindInShellHierarchy(csi.PIDL, out CShellItem parent);
            return result;
        }

        public CShellItem? FindInShellHierarchy(string path)
        {
            IntPtr pidl = ShellAPI.ILCreateFromPathW(path);
            if (pidl == IntPtr.Zero)
            {
                Debug.WriteLine("Invalid path provided to FindInShellHierarchy(): '" + path + "'");
                return null;
            }
            try
            {
                return FindInShellHierarchy(pidl, out _);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        /// <summary>
        /// This is the "engine" that maintains the hierarchical relationship between items.
        /// - Method: internal static CShellItem BrowseTo(IntPtr absPidl, out CShellItem Parent)
        /// - Logic: It traverses the cached tree from the Desktop down to the target PIDL.If an item doesn't
        /// exist in the cache, it expands the parent folders to find or place the item. This ensures that 
        /// every CShellItem is correctly linked to its parent in the internal structure.
        /// 
        /// BrowseTo locates the desired item and places it in its proper location on the internal tree.
        /// Any and all sub-directories that need to be populated in the tree in order to properly place
        /// the desired item, are populated. This is the programatic equivalent of Browsing to a node in 
        /// <code>ExpTree's</code> TreeView.<br /> 
        /// BrowseTo also returns the Parent CShellItem. 
        /// If the desired CShellItem does not exist, the returned Parent is the CShellItem that would be the
        /// Immediate ancestor (containing CShellItem or Parent) of the desired item should it be created.
        /// </summary>
        /// <param name="absPidl">A Absolute PIDL whose CShellItem is to be found</param>
        /// <param name="Parent">Output parameter -- Immediate Ancestor CShellItem of the found item OR 
        /// the CShellItem that would contain the item if it existed OR Nothing if NO Immediate ancestor found 
        /// in the Shell namespace. </param>
        /// <returns>The desired CShellItem or, if not found, Nothing.</returns>
        /// <remarks>A by-product of this search is that any sub-dirs of the tree along the path will be 
        /// populated with their sub directories.
        /// It is logically possible that NO Immediate ancestor can be found.
        /// For Example: GetCShItem(Path) may be given a string specifying a non-existant directory.
        /// (eg -- C:\Test\NonExistant\junk.txt). 
        /// In that case, and that case only, Parent may be returned as Nothing.</remarks>
        public CShellItem? FindInShellHierarchy(IntPtr absPidl, out CShellItem? Parent)
        {
            Parent = null;

            var currentFolder = Root;
            if (currentFolder == null) return null;

            bool foundFinalExtantParentDirectory = false;
            while (!foundFinalExtantParentDirectory)
            { //todo: I don't like how reading of folder contents is hidden inside the Directories and Files properties rather than being explicit
                foreach (var currentCSI in currentFolder.Directories) //check directories before files because there tend to be fewer directories and there's no point checking files if we haven't delved deeply enough into the tree yet
                {
                    if (IsAncestorOf(currentCSI.PIDL, absPidl))
                    {
                        if (CPidl.IsEqual(currentCSI.PIDL, absPidl))  // we found the desired item
                        {
                            Parent = currentFolder;
                            return currentCSI;
                        }
                        else // Found an ancestor and must delve into it
                        {
                            currentFolder = currentCSI;
                            goto NEXTWHILE;
                        }
                    }
                }

                foundFinalExtantParentDirectory = true; //can't delve any deeper.  currentFolder is the parent folder
            NEXTWHILE:;
            }

            //Test for invalid paths
            if (CPidl.SegmentCount(currentFolder.PIDL) + 1 != CPidl.SegmentCount(absPidl)) //the root folder plus 1 final pidl should be the same length as the given pidl if the given pidl is real
            {
                Debug.WriteLine("Invalid pidl provided to FindInShellHierarchy(): '" + CPidl.ToString(absPidl) + "'");
                return null;
            }

            // Check for files in the current folder
            foreach (var currentCSI in currentFolder.Files)
            {
                if (CPidl.IsEqual(currentCSI.PIDL, absPidl))
                {
                    Parent = currentFolder;
                    return currentCSI;
                }
            }

            Debug.WriteLine("Could not find file in the current folder: '" + CPidl.ToString(absPidl) + "'");
            return null;
        }

        /// <summary>True if parameter "ancestor" is an ancestor of parameter "current" 
        /// </summary>
        /// <returns>IsAncestorOf returns True if input CShellItem ancestor is an ancestor of input CShellItem current</returns>
        /// <remarks>if OS is Win2K or above, uses the ILIsParent API, otherwise uses the
        /// cPidl function StartsWith.  This is necessary since ILIsParent in only available
        /// in Win2K or above systems AND StartsWith fails on some folders on XP systems (most
        /// obviously some Network Folder Shortcuts, but also Control Panel. Note, StartsWith
        /// always works on systems prior to XP.<br />
        /// NOTE: if ancestor and current reference the same Item, both
        /// methods return True</remarks>
        public static bool IsAncestorOf(CShellItem ancestor, CShellItem current, bool fParent = false)
        {
            return IsAncestorOf(ancestor.PIDL, current.PIDL, fParent);
        }
        /// <summary> Compares a candidate Ancestor PIDL with a Child PIDL and
        /// returns True if Ancestor is an ancestor of the child.
        /// if fParent is True, then only return True if Ancestor is the immediate
        /// parent of the Child</summary>
        /// <param name="AncestorPidl">The Absolute PIDL that is the candidate for being an Ancestor of ChildPidl.</param>
        /// <param name="ChildPidl">The Absolute PIDL whose ancestory is being searched for.</param>
        /// <param name="fParent">A flag. If True, then only return True if AncestorPidl is the immediate Parent of ChildPidl.</param>
        /// <returns>True if AncestorPidl is an ancestor of ChildPidl.
        ///          If fParent is False then will also return True if AncestorPidl and ChildPidl are equal. 
        ///          If fParent is True, <i>only</i> returns True if AncestorPidl is the Parent of ChildPidl</returns>
        ///          
        public static bool IsAncestorOf(IntPtr AncestorPidl, IntPtr ChildPidl, bool fParent = false)
        {
            return ShellAPI.ILIsParent(AncestorPidl, ChildPidl, fParent);
        }


    }
}
