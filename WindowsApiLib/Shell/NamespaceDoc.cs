using System.Collections.Generic;

namespace WindowsApiLib.Shell
{

    /// <summary>
    /// Contains a large number of Windows Shell API, Data Structure, and Enumeration Declarations used by Methods in 
    /// the WindowsApiLib and ExpTree_Demo Namespaces. <br />
    /// For the majority of the entities in this Namespace, the documentation is found on MSDN. 
    /// </summary>
    /// <remarks>The content of this Namespace was built up over a long period of time. The MSDN definitions may not fully be
    ///          reflected in the Declarations here, but the Declarations here work for their intended purposes in WindowsApiLib and
    ///          ExpTree_Demo. </remarks>
    [System.Runtime.CompilerServices.CompilerGenerated()]
    public class NamespaceDoc
    {
    }


    public static class ShellNamespaceGuids
    {
        // Core shell namespace locations
        public static readonly Guid Desktop = new("00021400-0000-0000-C000-000000000046"); ///<summary>The Desktop namespace, which is the root of the Shell namespace hierarchy. It contains all other Shell objects, including virtual folders, special folders, and file system objects.</summary>
        public static readonly Guid DesktopFileSystem = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
        public static readonly Guid ThisPC = new("20D04FE0-3AEA-1069-A2D8-08002B30309D");
        public static readonly Guid RecycleBin = new("645FF040-5081-101B-9F08-00AA002F954E");
        public static readonly Guid ControlPanel_AllItems = new("21EC2020-3AEA-1069-A2DD-08002B30309D");
        public static readonly Guid ControlPanel_Home = new("5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0");
        public static readonly Guid ControlPanel_Category = new("26EE0668-A00A-44D7-9371-BEB064C98683");
        public static readonly Guid Network = new("208D2C60-3AEA-1069-A2D7-08002B30309D");
        public static readonly Guid Libraries = new("031E4825-7B94-4DC3-B131-E946B44C8DD5");
        public static readonly Guid Documents = new("450D8FBA-AD25-11D0-98A8-0800361B1103");
        public static readonly Guid Printers = new("2227A280-3AEA-1069-A2DE-08002B30309D");
        public static readonly Guid AdministrativeTools = new("D20EA4E1-3957-11D2-A40B-0C5020524153");
        public static readonly Guid Fonts = new("D20EA4E1-3957-11D2-A40B-0C5020524152");
        public static readonly Guid NetworkConnections = new("7007ACC7-3202-11D1-AAD2-00805FC1270E");
        public static readonly Guid DevicesAndPrinters = new("A8A91A66-3A7D-4424-8D24-04E180695C7A");
        public static readonly Guid ProgramsAndFeatures = new("7B81BE6A-CE2B-4676-A29E-EB907A5126C5");
        public static readonly Guid AllTasks_GodMode = new("ED7BA470-8E54-465E-825C-99712043E01C");
        public static readonly Guid Music = new("1CF1260C-4DD0-4EBB-811F-33C572699FDE");
        public static readonly Guid MusicFolder = new("4BD8D571-6D19-48D3-BE97-422220080E43");

        // convenient lookup table
        public static readonly IReadOnlyDictionary<string, Guid> DicByDisplayName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            ["Desktop"] = Desktop,
            ["ThisPC"] = ThisPC,
            ["RecycleBin"] = RecycleBin,
            ["ControlPanel_AllItems"] = ControlPanel_AllItems,
            ["ControlPanel_Home"] = ControlPanel_Home,
            ["ControlPanel_Category"] = ControlPanel_Category,
            ["Network"] = Network,
            ["Libraries"] = Libraries,
            ["Documents"] = Documents,
            ["Printers"] = Printers,
            ["AdministrativeTools"] = AdministrativeTools,
            ["Fonts"] = Fonts,
            ["NetworkConnections"] = NetworkConnections,
            ["DevicesAndPrinters"] = DevicesAndPrinters,
            ["ProgramsAndFeatures"] = ProgramsAndFeatures,
            ["AllTasks_GodMode"] = AllTasks_GodMode,
            ["Music"] = Music,
        };


        public static readonly IReadOnlyDictionary<string, Guid> DicByGuidString = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            ["00021400-0000-0000-C000-000000000046"] = Desktop,
            ["B4BFCC3A-DB2C-424C-B029-7FE99A87C641"] = DesktopFileSystem,
            ["20D04FE0-3AEA-1069-A2D8-08002B30309D"] = ThisPC,
            ["645FF040-5081-101B-9F08-00AA002F954E"] = RecycleBin,
            ["21EC2020-3AEA-1069-A2DD-08002B30309D"] = ControlPanel_AllItems,
            ["5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0"] = ControlPanel_Home,
            ["26EE0668-A00A-44D7-9371-BEB064C98683"] = ControlPanel_Category,
            ["208D2C60-3AEA-1069-A2D7-08002B30309D"] = Network,
            ["031E4825-7B94-4DC3-B131-E946B44C8DD5"] = Libraries,
            ["450D8FBA-AD25-11D0-98A8-0800361B1103"] = Documents,
            ["2227A280-3AEA-1069-A2DE-08002B30309D"] = Printers,
            ["D20EA4E1-3957-11D2-A40B-0C5020524153"] = AdministrativeTools,
            ["D20EA4E1-3957-11D2-A40B-0C5020524152"] = Fonts,
            ["7007ACC7-3202-11D1-AAD2-00805FC1270E"] = NetworkConnections,
            ["A8A91A66-3A7D-4424-8D24-04E180695C7A"] = DevicesAndPrinters,
            ["7B81BE6A-CE2B-4676-A29E-EB907A5126C5"] = ProgramsAndFeatures,
            ["ED7BA470-8E54-465E-825C-99712043E01C"] = AllTasks_GodMode,
            ["1CF1260C-4DD0-4EBB-811F-33C572699FDE"] = Music
    };


        // Builds explorer shell URI, e.g. shell:::{645FF040-...}
        public static string ToShellUri(Guid clsid) => $"shell:::{clsid:B}";

        // Builds explorer shell URI, e.g. shell:::{645FF040-...}
        public static string ToShellPath(Guid clsid) => $"::{clsid:B}";
    }
}