using Microsoft.VisualBasic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WindowsApiLib.Shell;
using static WindowsApiLib.Shell.ShellAPI;

namespace WindowsApiLib
{
    /// <summary>
    /// A Class for reading and writing .lnk files. It is not used by ExpLib_Demo.
    /// </summary>
    /// <remarks>
    /// <pre>
    /// This is a slightly modified version of:
    /// Filename:     ShellShortcut.vb
    /// Author:       Mattias Sjögren (mattias@mvps.org)
    ///               http://www.msjogren.net/dotnet/
    /// 
    /// Description:  Defines a .NET friendly class, ShellShortcut, for reading
    ///               and writing shortcuts.
    /// </pre>
    /// </remarks>
    /// 
    [SupportedOSPlatform("windows")] // Added to indicate this control is Windows-only
    public class LinkFile : IDisposable
    {
        private IShellLink? m_Link;
        private bool m_Disposed = false;
        private readonly string m_LinkPath;
        private readonly bool m_IsValidLink = false;

        public LinkFile(string fPath)
        {
            IPersistFile pf;
            Type? tShellLink;
            tShellLink = Type.GetTypeFromCLSID(CLSID_ShellLink);
            m_Link = (IShellLink)Activator.CreateInstance(tShellLink);
            
            try
            {
                if (File.Exists(fPath))
                {
                    pf = (IPersistFile)m_Link;
                    int HR = pf.Load(fPath, 0);
                    if (HR == S_OK)
                    {
                        m_IsValidLink = true;
                    }
                    else
                    {
#if DEBUG
                        Marshal.ThrowExceptionForHR(HR);
#endif
                    }
                }
                m_LinkPath = fPath;
            }
            catch
            {
                // Clean up the COM object if initialization fails
                if (m_Link != null)
                {
                    Marshal.ReleaseComObject(m_Link);
                    m_Link = null;
                }
                throw;
            }
        }

        #region    Dispose
        public void Dispose()
        {
            Dispose(true);
            // Take yourself off of the finalization queue
            // to prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            // Allow your Dispose method to be called multiple times,
            // but throw an exception if the object has been disposed.
            // Whenever you do something with this class, 
            // check to see if it has been disposed.
            if (!m_Disposed)
            {
                // If disposing equals true, dispose all managed 
                // and unmanaged resources.
                m_Disposed = true;
                if (disposing)
                {
                }
                // Release unmanaged resources. If disposing is false,
                // only the following code is executed. 
                if (m_Link is null)
                    return;
                Marshal.ReleaseComObject(m_Link);
                m_Link = null;
            }
            else
            {
                throw new Exception("DragLink Disposed more than once");
            }
        }

        // This Finalize method will run only if the 
        // Dispose method does not get called.
        // By default, methods are NotOverridable. 
        // This prevents a derived class from overriding this method.
        /// <summary>
        /// Calls Dispose(False) to ensure release of the IShellLink object
        /// </summary>
        ~LinkFile()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }
        #endregion

        #region    Public Properties
        /// <summary>
        /// Returns a String containing the Path of the Link Target
        /// </summary>
        /// <returns>String containing the Path of the Link Target</returns>
        public string LinkTargetPath
        {
            get
            {
                WIN32_FIND_DATA wfd;
                var SB = new StringBuilder(WinSDK.MAX_PATH_NT);
                int HR;
                HR = m_Link.GetPath(SB, SB.Capacity, out wfd, SLGP.UNCPRIORITY);
                if (HR == S_OK)
                {
                    return SB.ToString();
                }
                else
                {
                    #if DEBUG
                    Marshal.ThrowExceptionForHR(HR);
                    #endif
                    return "";
                }
            }
            set
            {
                int HR = m_Link.SetPath(value);
                if (HR == S_OK)
                {
                }
                else
                {
                    #if DEBUG
                    Marshal.ThrowExceptionForHR(HR);
                    #endif
                }
            }
        }

        /// <summary>
        /// Returns True if the file associated with this instance is a Valid Link
        /// </summary>
        /// <returns>True if the file associated with this instance is a Valid Link</returns>
        /// <remarks>Validity is determined by Windows</remarks>
        public bool IsValidLink
        {
            get
            {
                return m_IsValidLink;
            }
        }
        #endregion

        #region    Public Methods
        /// <summary>
        /// Saves a copy of the instance Link File to a different location within the File System
        /// </summary>
        /// <param name="TargetPath">Location to Save the Link File</param>
        /// <returns>True if successful, False otherwise</returns>
        /// <remarks>It is normally best to use the System Context Menu for this operation</remarks>
        public bool SaveAs(string TargetPath)
        {
            bool SaveAsRet = default;
            SaveAsRet = true;   // errors change this
            try
            {
                IPersistFile pf = (IPersistFile)m_Link;
                int HR = pf.Save(TargetPath, true);
                if (HR == S_OK)
                {
                    HR = pf.SaveCompleted(m_LinkPath);
                    if (HR != S_OK)
                    {
                        #if DEBUG
                        Marshal.ThrowExceptionForHR(HR);
                        #endif
                    }
                }
                else
                {
                    SaveAsRet = false;
                    #if DEBUG
                    Marshal.ThrowExceptionForHR(HR);
                    #endif
                }
            }
            catch (Exception ex)
            {
                SaveAsRet = false;
#if DEBUG
                MessageBox.Show("Error Saving Link -- \n" + ex.Message, "Error on Link Copy/Move", MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
            }
            finally
            {
            }

            return SaveAsRet;
        }

        /// <summary>
        /// Saves a copy of the instance Link File with a different name to a different location within the File System
        /// </summary>
        /// <param name="TargetPath">Location to Save the Link File with a different name</param>
        /// <returns>True if successful, False otherwise</returns>
        /// <remarks>It is normally best to use the System Context Menu for this operation</remarks>
        public bool SaveCopyAs(string TargetPath)
        {
            bool SaveCopyAsRet = default;
            SaveCopyAsRet = true;   // Errors change this
            try
            {
                IPersistFile pf = (IPersistFile)m_Link;
                int HR = pf.Save(TargetPath, false);
                if (HR != S_OK)
                {
                    SaveCopyAsRet = false;
                    #if DEBUG
                    Marshal.ThrowExceptionForHR(HR);
                    #endif
                }
            }
            catch (Exception ex)
            {
                SaveCopyAsRet = false;
#if DEBUG
                MessageBox.Show("Error in SaveCopyAs Link -- \n" + ex.Message, "Error on Link Copy", MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
            }
            finally
            {
            }

            return SaveCopyAsRet;
        }
        #endregion
    }
}