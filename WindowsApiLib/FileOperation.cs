using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using WindowsApiLib.Shell;

namespace WindowsApiLib
{

    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig]
        int StartOperations();

        [PreserveSig]
        int FinishOperations(int hrResult);

        [PreserveSig]
        int PreRenameItem(
            uint dwFlags,
            IShellItem? psiItem,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

        [PreserveSig]
        int PostRenameItem(
            uint dwFlags,
            IShellItem? psiItem,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            int hrRename,
            IShellItem? psiNewlyCreated);

        [PreserveSig]
        int PreMoveItem(
            uint dwFlags,
            IShellItem? psiItem,
            IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

        [PreserveSig]
        int PostMoveItem(
            uint dwFlags,
            IShellItem? psiItem,
            IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            int hrMove,
            IShellItem? psiNewlyCreated);

        [PreserveSig]
        int PreCopyItem(
            uint dwFlags,
            IShellItem? psiItem,
            IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

        [PreserveSig]
        int PostCopyItem(
            uint dwFlags,
            IShellItem? psiItem,
            IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            int hrCopy,
            IShellItem? psiNewlyCreated);

        [PreserveSig]
        int PreDeleteItem(
            uint dwFlags,
            IShellItem? psiItem);

        [PreserveSig]
        int PostDeleteItem(
            uint dwFlags,
            IShellItem? psiItem,
            int hrDelete,
            IShellItem? psiNewlyCreated);

        [PreserveSig]
        int PreNewItem(
            uint dwFlags,
            IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);

        [PreserveSig]
        int PostNewItem(
            uint dwFlags,
            IShellItem? psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName,
            uint dwFileAttributes,
            int hrNew,
            IShellItem? psiNewItem);

        [PreserveSig]
        int UpdateProgress(
            uint iWorkTotal,
            uint iWorkSoFar);

        [PreserveSig]
        int ResetTimer();

        [PreserveSig]
        int PauseTimer();

        [PreserveSig]
        int ResumeTimer();
    }

    [ComImport, Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    public class FileOperation { }   // CLSID_FileOperation

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperation
    {
        uint Advise(
        [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink pfops);

        void Unadvise(uint dwCookie);

        void SetOperationFlags(uint dwOperationFlags);

        void SetProgressMessage(
            [MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

        void SetProgressDialog(
            [MarshalAs(UnmanagedType.Interface)] object popd);   // IOperationsProgressDialog*

        void SetProperties(
            [MarshalAs(UnmanagedType.Interface)] object pproparray); // IPropertyChangeArray*

        void SetOwnerWindow(IntPtr hwndOwner);

        void ApplyPropertiesToItem(IShellItem psiItem);

        void ApplyPropertiesToItems(
            [MarshalAs(UnmanagedType.IUnknown)] object punkItems);

        void RenameItem(
            IShellItem psiItem,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName,
            [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink pfopsItem);

        void RenameItems(
            [MarshalAs(UnmanagedType.IUnknown)] object pUnkItems,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        void MoveItem(
            IShellItem psiItem,
            IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName,
            [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink pfopsItem);

        void MoveItems(
            [MarshalAs(UnmanagedType.IUnknown)] object punkItems,
            IShellItem psiDestinationFolder);

        void CopyItem(
            IShellItem psiItem,
            IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszCopyName,
            [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink pfopsItem);

        void CopyItems(
            [MarshalAs(UnmanagedType.IUnknown)] object punkItems,
            IShellItem psiDestinationFolder);

        void DeleteItem(
            IShellItem psiItem,
            [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink pfopsItem);

        void DeleteItems(
            [MarshalAs(UnmanagedType.IUnknown)] object punkItems);

        void NewItem(
            IShellItem psiDestinationFolder,
            uint dwFileAttributes,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName,
            [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink? pfopsItem);

        int PerformOperations();

        [return: MarshalAs(UnmanagedType.Bool)]
        bool GetAnyOperationsAborted();
    }


}