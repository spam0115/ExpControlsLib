using System;
using System.Collections.Generic;
using System.ComponentModel; // Win32Exception
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsApiLib
{

    public static class HResultLogger
    {
        // Add any app-specific HRESULTs you care about here.
        private static readonly Dictionary<int, string> KnownHResults = new()
        {
            // Core COM / OLE
            { unchecked((int)0x80004001), "E_NOTIMPL: Not implemented." },
            { unchecked((int)0x80004002), "E_NOINTERFACE: No such interface supported." },
            { unchecked((int)0x80004003), "E_POINTER: Invalid pointer." },
            { unchecked((int)0x80004004), "E_ABORT: Operation aborted." },
            { unchecked((int)0x80004005), "E_FAIL: Unspecified failure." },
            { unchecked((int)0x80004006), "E_HANDLE: Invalid handle." },
            { unchecked((int)0x8000FFFF), "E_UNEXPECTED: Catastrophic failure." },

            // Common Win32-as-HRESULT (HRESULT_FROM_WIN32)
            { unchecked((int)0x80070001), "ERROR_INVALID_FUNCTION: Incorrect function." },
            { unchecked((int)0x80070002), "ERROR_FILE_NOT_FOUND: The system cannot find the file specified." },
            { unchecked((int)0x80070003), "ERROR_PATH_NOT_FOUND: The system cannot find the path specified." },
            { unchecked((int)0x80070005), "E_ACCESSDENIED / ERROR_ACCESS_DENIED: Access denied." },
            { unchecked((int)0x80070006), "ERROR_INVALID_HANDLE: The handle is invalid." },
            { unchecked((int)0x80070008), "ERROR_NOT_ENOUGH_MEMORY: Not enough memory resources are available." },
            { unchecked((int)0x8007000D), "ERROR_INVALID_DATA: The data is invalid." },
            { unchecked((int)0x8007000E), "E_OUTOFMEMORY / ERROR_OUTOFMEMORY: Ran out of memory." },
            { unchecked((int)0x8007000F), "ERROR_INVALID_DRIVE: The system cannot find the drive specified." },
            { unchecked((int)0x80070013), "ERROR_WRITE_PROTECT: The media is write protected." },
            { unchecked((int)0x80070015), "ERROR_NOT_READY: The device is not ready." },
            { unchecked((int)0x80070020), "ERROR_SHARING_VIOLATION: Sharing violation." },
            { unchecked((int)0x80070026), "ERROR_HANDLE_EOF: Reached end of file." },
            { unchecked((int)0x80070032), "ERROR_NOT_SUPPORTED: The request is not supported." },
            { unchecked((int)0x80070050), "ERROR_FILE_EXISTS: The file exists." },
            { unchecked((int)0x80070052), "ERROR_CANNOT_MAKE: Cannot create a file or directory." },
            { unchecked((int)0x80070057), "E_INVALIDARG / ERROR_INVALID_PARAMETER: One or more arguments are invalid." },
            { unchecked((int)0x80070070), "ERROR_DISK_FULL: There is not enough space on disk." },
            { unchecked((int)0x8007007A), "ERROR_INSUFFICIENT_BUFFER: The data area passed to a system call is too small." },
            { unchecked((int)0x8007007B), "ERROR_INVALID_NAME: The filename, directory name, or volume label syntax is incorrect." },
            { unchecked((int)0x8007007E), "ERROR_MOD_NOT_FOUND: The specified module could not be found." },
            { unchecked((int)0x8007007F), "ERROR_PROC_NOT_FOUND: The specified procedure could not be found." },
            { unchecked((int)0x800700AA), "ERROR_BUSY: The requested resource is in use." },
            { unchecked((int)0x800700B7), "ERROR_ALREADY_EXISTS: Cannot create a file when that file already exists." },
            { unchecked((int)0x80070490), "ERROR_NOT_FOUND / ELEMENT_NOT_FOUND: Element not found." },
            { unchecked((int)0x800704C7), "ERROR_CANCELLED: The operation was canceled by the user." },
            { unchecked((int)0x80070522), "ERROR_PRIVILEGE_NOT_HELD: A required privilege is not held by the client." },
            { unchecked((int)0x8007052E), "ERROR_LOGON_FAILURE: Unknown user name or bad password." },
            { unchecked((int)0x80070569), "ERROR_LOGON_TYPE_NOT_GRANTED: The requested logon type is not granted." },
            { unchecked((int)0x800705B4), "ERROR_TIMEOUT: This operation returned because the timeout period expired." },
            { unchecked((int)0x800706BA), "RPC_S_SERVER_UNAVAILABLE: The RPC server is unavailable." },
            { unchecked((int)0x800706BE), "RPC_S_CALL_FAILED: The remote procedure call failed." },
            { unchecked((int)0x800706D9), "RPC_S_NO_ENDPOINT_FOUND: There are no more endpoints available from the endpoint mapper." },

            // COM activation / registration / marshaling
            { unchecked((int)0x80040154), "REGDB_E_CLASSNOTREG: Class not registered." },
            { unchecked((int)0x800401F0), "CO_E_NOTINITIALIZED: CoInitialize has not been called." },
            { unchecked((int)0x800401F3), "CO_E_CLASSSTRING: Invalid class string." },
            { unchecked((int)0x800401FD), "CO_E_OBJNOTREG: Object not registered." },
            { unchecked((int)0x80010001), "RPC_E_CALL_REJECTED: Call was rejected by callee." },
            { unchecked((int)0x80010105), "RPC_E_SERVERFAULT: The server threw an exception." },
            { unchecked((int)0x80010108), "RPC_E_DISCONNECTED: The object invoked has disconnected from its clients." },
            { unchecked((int)0x8001010A), "RPC_E_SERVERCALL_RETRYLATER: The message filter indicated that the application is busy." },
            { unchecked((int)0x8001010D), "RPC_E_CANTCALLOUT_ININPUTSYNCCALL: Cannot make outgoing call during input sync call." },
            { unchecked((int)0x8001010E), "RPC_E_WRONG_THREAD: The application called an interface that was marshaled for a different thread." },
            { unchecked((int)0x8001011F), "RPC_E_TIMEOUT: Operation timed out." },

            // IDispatch / Automation (DISP_E_*)
            { unchecked((int)0x80020003), "DISP_E_MEMBERNOTFOUND: Member not found." },
            { unchecked((int)0x80020004), "DISP_E_PARAMNOTFOUND: Parameter not found." },
            { unchecked((int)0x80020005), "DISP_E_TYPEMISMATCH: Type mismatch." },
            { unchecked((int)0x80020006), "DISP_E_UNKNOWNNAME: Unknown name." },
            { unchecked((int)0x80020007), "DISP_E_NONAMEDARGS: No named arguments." },
            { unchecked((int)0x80020008), "DISP_E_BADVARTYPE: Bad variable type." },
            { unchecked((int)0x80020009), "DISP_E_EXCEPTION: Exception occurred." },
            { unchecked((int)0x8002000A), "DISP_E_OVERFLOW: Overflow." },
            { unchecked((int)0x8002000B), "DISP_E_BADINDEX: Invalid index." },
            { unchecked((int)0x8002000C), "DISP_E_UNKNOWNLCID: Unknown locale ID." },
            { unchecked((int)0x8002000D), "DISP_E_ARRAYISLOCKED: Array is locked." },
            { unchecked((int)0x8002000E), "DISP_E_BADPARAMCOUNT: Wrong number of arguments." },
            { unchecked((int)0x8002000F), "DISP_E_PARAMNOTOPTIONAL: Parameter not optional." },
            { unchecked((int)0x80020010), "DISP_E_BADCALLEE: Invalid callee." },
            { unchecked((int)0x80020011), "DISP_E_NOTACOLLECTION: Object is not a collection." },
            { unchecked((int)0x80020012), "DISP_E_DIVBYZERO: Division by zero." },
            { unchecked((int)0x80020013), "DISP_E_BUFFERTOOSMALL: Buffer too small." },

            // Structured storage (STG_E_*)
            { unchecked((int)0x80030001), "STG_E_INVALIDFUNCTION: Invalid function." },
            { unchecked((int)0x80030002), "STG_E_FILENOTFOUND: File not found." },
            { unchecked((int)0x80030003), "STG_E_PATHNOTFOUND: Path not found." },
            { unchecked((int)0x80030005), "STG_E_ACCESSDENIED: Access denied." },
            { unchecked((int)0x80030006), "STG_E_INVALIDHANDLE: Invalid handle." },
            { unchecked((int)0x80030008), "STG_E_INSUFFICIENTMEMORY: Insufficient memory." },
            { unchecked((int)0x80030009), "STG_E_INVALIDPOINTER: Invalid pointer." },
            { unchecked((int)0x80030012), "STG_E_NOMOREFILES: No more files." },
            { unchecked((int)0x80030013), "STG_E_DISKISWRITEPROTECTED: Disk is write protected." },
            { unchecked((int)0x80030019), "STG_E_REVERTED: Object has been invalidated by a revert." },
            { unchecked((int)0x8003001D), "STG_E_CANTSAVE: Cannot save." },
            { unchecked((int)0x80030020), "STG_E_SHAREVIOLATION: Sharing violation." },
            { unchecked((int)0x80030021), "STG_E_LOCKVIOLATION: Lock violation." },
            { unchecked((int)0x80030050), "STG_E_FILEALREADYEXISTS: File already exists." },
            { unchecked((int)0x80030070), "STG_E_MEDIUMFULL: Disk is full." },

            // Moniker (MK_E_*)
            { unchecked((int)0x800401E3), "MK_E_UNAVAILABLE: Operation unavailable." },
            { unchecked((int)0x800401E4), "MK_E_SYNTAX: Invalid syntax." },
            { unchecked((int)0x800401E5), "MK_E_NOOBJECT: No object for moniker." },
            { unchecked((int)0x800401E6), "MK_E_INVALIDEXTENSION: Invalid extension." },
            { unchecked((int)0x800401E7), "MK_E_INTERMEDIATEINTERFACENOTSUPPORTED: Intermediate operation unsupported." },
            { unchecked((int)0x800401E8), "MK_E_NOTBINDABLE: Moniker is not bindable." },
            { unchecked((int)0x800401E9), "MK_E_NOTBOUND: Moniker is not bound." },
            { unchecked((int)0x800401EA), "MK_E_CANTOPENFILE: Cannot open file." },
            { unchecked((int)0x800401EB), "MK_E_MUSTBOTHERUSER: User input required." },
            { unchecked((int)0x800401EC), "MK_E_NOINVERSE: No inverse for operation." },
            { unchecked((int)0x800401ED), "MK_E_NOSTORAGE: No storage available." },
            { unchecked((int)0x800401EE), "MK_E_NOPREFIX: No prefix." },
            { unchecked((int)0x800401EF), "MK_E_ENUMERATION_FAILED: Enumeration failed." },

            // Type library
            { unchecked((int)0x8002801D), "TYPE_E_LIBNOTREGISTERED: Type library not registered." },
            { unchecked((int)0x8002802B), "TYPE_E_ELEMENTNOTFOUND: Element not found." },
            { unchecked((int)0x80029C4A), "TYPE_E_CANTLOADLIBRARY: Error loading type library/DLL." },

            // SSPI / security (common)
            { unchecked((int)0x8009030C), "SEC_E_LOGON_DENIED: The logon attempt failed." },
            { unchecked((int)0x8009030D), "SEC_E_UNKNOWN_CREDENTIALS: The credentials supplied were not recognized." },
            { unchecked((int)0x8009030E), "SEC_E_NO_CREDENTIALS: No credentials are available in the security package." },
            { unchecked((int)0x80090311), "SEC_E_NO_AUTHENTICATING_AUTHORITY: No authority could be contacted for authentication." },
            { unchecked((int)0x80090322), "SEC_E_WRONG_PRINCIPAL: The target principal name is incorrect." },
            { unchecked((int)0x80090325), "SEC_E_UNTRUSTED_ROOT: Certificate chain issued by an untrusted authority." },
            { unchecked((int)0x80090326), "SEC_E_ILLEGAL_MESSAGE: Message was altered or malformed." },
            { unchecked((int)0x80090327), "SEC_E_CERT_UNKNOWN: Unknown certificate error." },
            { unchecked((int)0x80090328), "SEC_E_CERT_EXPIRED: Certificate has expired." },
            { unchecked((int)0x80090331), "SEC_E_ALGORITHM_MISMATCH: Client and server cannot communicate; no common algorithm." }
        };

        /// <summary>
        /// Logs HRESULT details to Debug output, including hex form, decoded fields, and friendly message.
        /// </summary>
        public static void LogHResult(int hResult, string? context = null)
        {
            uint hr = unchecked((uint)hResult);

            int severity = (int)((hr >> 31) & 0x1);      // 0=success, 1=failure
            int facility = (int)((hr >> 16) & 0x1FFF);   // facility code
            int code = (int)(hr & 0xFFFF);               // status code
            
            string knownMessage;
            if (KnownHResults.TryGetValue(hResult, out var msg)) {
                knownMessage = msg;
            }
            else
            {
                var ex = Marshal.GetExceptionForHR(hResult);
                knownMessage = ex.Message;
            }

            // If this is HRESULT_FROM_WIN32(x), Win32Exception can often give useful text.
            // Common pattern is 0x8007xxxx (FACILITY_WIN32).
            string win32Text = (facility == 7)
                ? new Win32Exception(code).Message
                : "N/A";

            Debug.WriteLine("=== HRESULT ERROR ===");
            if (!string.IsNullOrWhiteSpace(context))
                Debug.WriteLine($"Context   : {context}");

            Debug.WriteLine($"Decimal   : {hResult}");
            Debug.WriteLine($"Hex       : 0x{hr:X8}");
            Debug.WriteLine($"Severity  : {(severity == 1 ? "Failure" : "Success")}");
            Debug.WriteLine($"Facility  : {facility}");
            Debug.WriteLine($"Code      : 0x{code:X4} ({code})");
            Debug.WriteLine($"Meaning   : {knownMessage}");
            Debug.WriteLine($"Win32 Text: {win32Text}");
            Debug.WriteLine("=====================");
        }
    }
}
