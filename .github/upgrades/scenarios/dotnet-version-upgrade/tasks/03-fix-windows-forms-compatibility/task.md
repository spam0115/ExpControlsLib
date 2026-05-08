# 03-fix-windows-forms-compatibility: Fix Windows Forms Compatibility

Address Windows Forms API incompatibilities in Demo_CS and ExpListLib (854 Windows Forms issues identified in assessment). This includes updating control properties, event handlers, and drag-drop logic to use .NET 10–compatible APIs. Focus on the most frequent issues: ListView, TreeView, TreeNode, DragDropEffects, Keys, and Control.

**Key concerns**: Windows Forms underwent binary breaking changes between .NET Framework and .NET Core. Many property names and enum values differ slightly.

**Done when**:
- Demo_CS compiles without Windows Forms–related errors
- ExpListLib compiles without Windows Forms–related errors
- All TreeView/ListView/Node references resolve correctly
- No `CS0246` (type not found) errors for System.Windows.Forms types
