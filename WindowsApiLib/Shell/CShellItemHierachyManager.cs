using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using WindowsApiLib;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using static WindowsApiLib.Shell.ShellAPI;


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
        public object Lock = new object();

        public CShellItem Root { get; set; }
        public CShellItem? CurrentFolder { get; set; }
        public string? CurrentPath { get {
                if (CurrentFolder?.PIDL == null) return string.Empty;
                return CPidl.ToString(CurrentFolder.PIDL);
            } }

        public CShellItem DesktopCSI { get; internal set; }

        /// <summary>
        /// A case-insensitive set of paths representing Shell items that should be excluded from
        /// lookup and expansion in the hierarchy. Items whose trimmed FullPath matches an entry
        /// in this set will be ignored by Find and FindAndAllowExpansion methods.
        /// </summary>
        private HashSet<string> _excludedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets or sets the collection of excluded item paths. Items whose trimmed FullPath
        /// is in this set will be ignored by Add, Find, and FindAndAllowExpansion methods.
        /// Any item whose path contains an excluded item as an ancestor will also be ignored.
        /// </summary>
        public HashSet<string> ExcludedItems
        {
            get => _excludedItems;
            set => _excludedItems = value ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether the specified <see cref="CShellItem"/> should be excluded based
        /// on the <see cref="ExcludedItems"/> collection.
        /// </summary>
        /// <param name="item">The <see cref="CShellItem"/> to test.</param>
        /// <returns>
        /// <c>true</c> if the item's path (stripped of leading/trailing <c>:</c>, <c>{</c>, and <c>}</c>
        /// characters) is found in <see cref="ExcludedItems"/>; otherwise <c>false</c>.
        /// </returns>
        public bool IsExcluded(CShellItem item)
        {
            if (_excludedItems.Count == 0 || item == null) return false;
            var path = (item.FullPath ?? "").Trim(':', '{', '}');
            return _excludedItems.Contains(path);
        }

        /// <summary>
        /// Determines whether the specified path should be excluded based on the
        /// <see cref="ExcludedItems"/> collection.
        /// </summary>
        /// <param name="path">The path to test.</param>
        /// <returns>
        /// <c>true</c> if the path (stripped of leading/trailing <c>:</c>, <c>{</c>, and <c>}</c>
        /// characters) is found in <see cref="ExcludedItems"/>; otherwise <c>false</c>.
        /// </returns>
        private bool IsExcludedPath(string? path)
        {
            if (_excludedItems.Count == 0 || string.IsNullOrEmpty(path)) return false;
            var trimmed = path.Trim(':', '{', '}');
            return _excludedItems.Contains(trimmed);
        }

        public CShellItemHierachyManager(CShellItem? root = null) {
            this.Root = root;

            //todo: move the item hierarchy code from cshellitem to over here.
        }

        /// <summary>
        /// FindCShItem attempts to locate a CShellItem in the internal tree. It will NOT expand the Tree during the
        /// search. If the Item identified by the Absolute PIDL parameter is not ALREADY in the internal tree, then
        /// FindCShItem will return NOTHING.
        /// </summary>
        /// <param name="ptr">An Absolute PIDL referencing the item to be Found.</param>
        /// <returns>The existant CShellItem if found, Nothing if not found.</returns>
        /// <remarks> 5/31/2012 - most code in this function replaced by a call to FindCShItem(BaseItem as CShellItem, Abs as IntPtr)</remarks>
        public CShellItem? Find(IntPtr ptr)
        {
            if (_excludedItems.Count > 0)
            {
                var name = CPidl.GetFullName(ptr);
                if (name != null && IsExcludedPath(name))
                    return null;
            }

            var result = Find(Root, ptr);
            return result;
        }

        /// <summary>
        /// Find by full filename with path
        /// </summary>
        /// <param name="fullFileName">full filename with path</param>
        /// <returns></returns>
        public CShellItem? Find(string fullFileName)
        {
            if (string.IsNullOrEmpty(fullFileName)) return null;
            if (IsExcludedPath(fullFileName)) return null;

            IntPtr pidl = ShellAPI.ILCreateFromPathW(fullFileName);
            if (pidl == IntPtr.Zero) return null;

            try
            {
                var pidlAndName = new PidlAndCanonicalParsingName(pidl, fullFileName);
                return Find(Root, pidlAndName);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        /// <summary>
        /// FindCShItem attempts to locate a CShellItem in the internal tree. It will NOT expand the Tree during the
        /// search. If the Item identified by the Absolute PIDL parameter is not ALREADY in the internal tree, then
        /// FindCShItem will return NOTHING.
        /// </summary>
        /// <param name="absPidl">An Absolute PIDL referencing the item to be Found.</param>
        /// <returns>The existant CShellItem if found, Nothing if not found.</returns>
        /// <remarks> 5/31/2012 -Function added to replace algorithm used in FindCShItem(ptr as IntPtr) which now only calls this routine.</remarks>
        public static CShellItem? Find(CShellItem rootItem, IntPtr absPidl)
        {
            if (rootItem is null || absPidl == IntPtr.Zero) return null;
            if (rootItem.PIDL == absPidl) return rootItem;

            var name = CPidl.GetFullName(absPidl);
            if (name == null)
            {
                Debug.WriteLine("Could not get name for pidl using Find...CPidl.GetFullName");
                return null;
            }

            var pidlAndName = new PidlAndCanonicalParsingName(absPidl, name);

            return Find(rootItem, pidlAndName);
        }

        private static CShellItem? Find(CShellItem rootItem, PidlAndCanonicalParsingName pidlAndName)
        {
            if (string.Compare(rootItem.FullPath, pidlAndName.Name, StringComparison.OrdinalIgnoreCase) == 0)
                return rootItem;

            if (rootItem.DirectoriesInitialized) //problem: if you jump multiple folders deep when navigating, you will have Folders that are not initialized and this search can fail.  This function isn't supposed to fill in the tree but not doing so makes it hard to navigate
            {
                foreach (CShellItem childDir in rootItem.Directories)
                {
                    if (childDir.FullPath == pidlAndName.Name)
                        return childDir;
                    if (CPidl.IsAncestorOf(childDir.PIDL, pidlAndName.Pidl, false)) //note that items are considered to be ancestors of themselves which is kinda weird
                        return Find(childDir, pidlAndName);
                }
            }

            if (rootItem.FilesInitialized && CPidl.IsAncestorOf(rootItem.PIDL, pidlAndName.Pidl, true))
            {
                var displayName = CPidl.GetDisplayName(pidlAndName.Pidl);
                if (rootItem.Files.Dictionary.TryGetValue(displayName, out CShellItem? fileItem))
                {
                    return fileItem;
                }
                else return null;
            }
            else
                return null;
        }

        /// <summary>
        /// FindCShItem attempts to locate a CShellItem in the internal tree. It will NOT expand the Tree during the
        /// search. If the Item identified by the Absolute PIDL parameter is not ALREADY in the internal tree, then
        /// FindCShItem will return NOTHING.
        /// </summary>
        /// <param name="b">A Byte array representation of a Full or Absolute PIDL 
        /// referencing the item to be Found.</param>
        /// <returns>The existant CShellItem if found, Nothing if not found.</returns>
        /// <remarks></remarks>
        public CShellItem? Find(byte[] b)
        {
            if (!CPidl.IsValid(b))
                return null;

            if (_excludedItems.Count > 0)
            {
                var tempPidl = Marshal.AllocCoTaskMem(b.Length);
                try
                {
                    Marshal.Copy(b, 0, tempPidl, b.Length);
                    var name = CPidl.GetFullName(tempPidl);
                    if (name != null && IsExcludedPath(name))
                        return null;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(tempPidl);
                }
            }

            CShellItem FindCShItemRet = default;
            var thisPidl = Marshal.AllocCoTaskMem(b.Length);
            if (thisPidl.Equals(IntPtr.Zero))
                return null;
            Marshal.Copy(b, 0, thisPidl, b.Length);
            FindCShItemRet = Find(Root, thisPidl);
            Marshal.FreeCoTaskMem(thisPidl);
            return FindCShItemRet;
        }

        public CShellItem Add(CShellItem csi)
        {
            if (csi == null) throw new ArgumentNullException(nameof(csi));
            var result = FindAndAllowExpansion(csi.PIDL, out CShellItem parent);
            return result;
        }

        public CShellItem? FindAndAllowExpansion(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            IntPtr pidl = ShellAPI.ILCreateFromPathW(path);
            if (pidl == IntPtr.Zero)
            {
                Debug.WriteLine("Invalid path provided to FindOrAdd(): '" + path + "'");
                return null;
            }
            try
            {
                return FindAndAllowExpansion(pidl, out _);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        public CShellItem? FindAndAllowExpansion(CShellItem? csi)
        {
            if (csi == null) throw new ArgumentNullException(nameof(csi));

            var result = FindAndAllowExpansion(csi.PIDL, out CShellItem parent);
            return result;
        }

        /// <summary>
        /// This is the "engine" that maintains the hierarchical relationship between items.
        /// - Method: internal static CShellItem BrowseTo(IntPtr absPidl, out CShellItem Parent)
        /// - Logic: It traverses the cached tree from the Desktop down to the target PIDL.  If an item 
        /// doesn't exist in the cache, it expands the parent folders to find or place the item. This ensures 
        /// that every CShellItem is correctly linked to its parent in the internal structure.
        /// 
        /// BrowseTo locates the desired item and places it in its proper location on the internal tree.
        /// Any and all sub-directories that need to be populated in the tree in order to properly place
        /// the desired item, are populated. This is the programmatic equivalent of Browsing to a node in 
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
        public CShellItem? FindAndAllowExpansion(IntPtr absPidl, out CShellItem? Parent)
        {
            Parent = null;

            if (absPidl == IntPtr.Zero) throw new ArgumentNullException(nameof(absPidl));

            if (_excludedItems.Count > 0)
            {
                var targetName = CPidl.GetFullName(absPidl);
                if (targetName != null && IsExcludedPath(targetName))
                    return null;
            }

            var currentFolder = Root;
            if (currentFolder == null) throw new Exception("The root of the shell hierarchy was null.");

            if (CPidl.ResolvesToSamePathOrName(currentFolder.PIDL, absPidl))  // we found the desired item
            {
                Parent = currentFolder.Parent;
                return currentFolder;
            }

            bool foundFinalExtantParentDirectory = false;
            while (!foundFinalExtantParentDirectory)
            {
                CShellItem nextFolder = null;
                lock (currentFolder)
                {
                    foreach (var currentCSI in currentFolder.Directories) 
                    {
                        if (IsAncestorOf(currentCSI.PIDL, absPidl))
                        {
                            if (IsExcluded(currentCSI))
                                return null;

                            if (CPidl.ResolvesToSamePathOrName(currentCSI.PIDL, absPidl))  // we found the desired item
                            {
                                Parent = currentFolder;
                                return currentCSI;
                            }
                            else // Found an ancestor and must delve into it
                            {
                                nextFolder = currentCSI;
                                goto NEXTWHILE;
                            }
                        }
                    }
                }

                foundFinalExtantParentDirectory = true; //can't delve any deeper.  currentFolder is the parent folder
            NEXTWHILE:;
                currentFolder = nextFolder == null ? currentFolder : nextFolder;
            }

            //Test for invalid paths and mismatched path lengths.
            //Basically, we will only try to create new items if their parent folders exist.  
            //This can happen for fake paths or for paths that require additional subfolders to be created.
            //This code has chosen not to try to created additoinal subfolders so we will return an error results for this case.
            if (CPidl.SegmentCount(currentFolder.PIDL) + 1 != CPidl.SegmentCount(absPidl)) {
                Debug.WriteLine("Invalid pidl provided to FindOrAdd(): '" + CPidl.ToString(absPidl) + "'");
                return null;
            }

            var name = CPidl.GetDisplayName(absPidl);

            // Check for files in the current folder
            lock (currentFolder)
            {
                if (currentFolder.Files.Dictionary.TryGetValue(name, out CShellItem fileItem))
                {
                    Parent = currentFolder;
                    return fileItem;
                }
            }

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


        /// <summary>
        /// Removes an item from the hierarchy if it is found.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Remove(CShellItem item)
        {
            return RemoveRange(new[] { item }, true);
        }

        public bool RemoveRange(IEnumerable<CShellItem> items, bool raiseEvents = true)
        {
            if (items == null) return false;

            var removedAny = false;
            var groupedByParent = new Dictionary<CShellItem, List<CShellItem>>();

            try
            {
                lock (this.Lock)
                {
                    foreach (var item in items)
                    {
                        var target = Find(item.PIDL);
                        if (target != null && target.Parent != null)
                        {
                            if (!groupedByParent.TryGetValue(target.Parent, out var list))
                            {
                                list = new List<CShellItem>();
                                groupedByParent[target.Parent] = list;
                            }
                            list.Add(target);
                        }
                    }
                }

                foreach (var kvp in groupedByParent)
                {
                    var parent = kvp.Key;
                    var targets = kvp.Value;

                    lock (parent)
                    {
                        var filesToRemove = new List<CShellItem>();
                        var dirsToRemove = new List<CShellItem>();

                        foreach (var target in targets)
                        {
                            if (target.IsFolder)
                            {
                                lock (target)
                                {
                                    if (target.FilesInitialized)
                                    {
                                        foreach (var child in target.Files)
                                            child.Parent = null;
                                        target.Files.Clear();
                                    }
                                    if (target.DirectoriesInitialized)
                                    {
                                        foreach (var child in target.Directories)
                                            child.Parent = null;
                                        target.Directories.Clear();
                                    }
                                }
                                dirsToRemove.Add(target);
                            }
                            else
                            {
                                filesToRemove.Add(target);
                            }
                        }

                        if (filesToRemove.Count > 0)
                        {
                            parent.Files.RemoveRange(filesToRemove);
                        }
                        if (dirsToRemove.Count > 0)
                        {
                            parent.Directories.RemoveRange(dirsToRemove);
                        }
                        
                        parent.ClearCaches();
                        removedAny = true;
                    }

                    if (raiseEvents)
                    {
                        foreach (var target in targets)
                        {
                            ShellController.Instance.ShellUpdater.RaiseUpdateEvent(this, new ShellItemUpdateEventArgs(target, CShItemUpdateType.Deleted));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error in CShellItemHierarchyManager.RemoveRange: " + ex.ToString());
            }

            return removedAny;
        }


        /// <summary>
        /// Clears all items from the hierarchy by recursively disposing of children,
        /// resets the <see cref="CurrentFolder"/> to null, and sets <see cref="Root"/>
        /// to a fresh Desktop <see cref="CShellItem"/>.
        /// </summary>
        /// <remarks>
        /// This is useful for resetting the hierarchy to a clean state, for example
        /// during application startup to avoid race conditions between controls that
        /// are loading concurrently. All child references (files and directories) are
        /// detached from their parents before being cleared.
        /// </remarks>
        public void Clear()
        {
            lock (this.Lock)
            {
                if (Root is not null)
                {
                    ClearRecursive(Root);
                }

                CurrentFolder = null;

                CShellItemFactory.ResetDesktopCache();
                var desktopCsi = CShellItemFactory.Create(CSIDL.DESKTOP);
                this.DesktopCSI = desktopCsi;
                Root = desktopCsi;
            }
        }

        /// <summary>
        /// Recursively clears all children (files and directories) from the given
        /// <see cref="CShellItem"/> and its descendants, detaching parent references.
        /// </summary>
        private void ClearRecursive(CShellItem item)
        {
            if (item is null) return;

            if (item.DirectoriesInitialized)
            {
                foreach (var child in item.Directories)
                {
                    child.Parent = null;
                    ClearRecursive(child);
                }
                item.Directories.Clear();
            }

            if (item.FilesInitialized)
            {
                foreach (var child in item.Files)
                {
                    child.Parent = null;
                }
                item.Files.Clear();
            }

            item.ClearCaches();
        }

    }

    public readonly struct PidlAndCanonicalParsingName
    {
        public IntPtr Pidl { get; }
        public string Name { get; }
        public PidlAndCanonicalParsingName(IntPtr pidl, string name)
        {
            Pidl = pidl;
            Name = name;
        }
    }

}
