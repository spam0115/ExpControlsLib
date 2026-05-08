# 04-resolve-system-api-compatibility: Resolve System API Compatibility

Fix remaining API incompatibilities in all projects. This includes:
- Configuration migration (System.Configuration → System.Configuration.ConfigurationManager NuGet package, or migrate to Microsoft.Extensions.Configuration)
- System.Drawing / GDI+ (if needed, add System.Drawing.Common NuGet)
- System.Management / WMI (if needed, add System.Management NuGet)
- Any source-incompatible APIs (50 identified in assessment)

**Done when**:
- No `CS0246` errors for any removed or moved types
- No `CS0117` errors for missing methods/properties
- All NuGet package references are resolved
- Solution builds with 0 compilation errors
