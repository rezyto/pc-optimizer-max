using System.Runtime.InteropServices;

// Every P/Invoke in this assembly targets a Windows system DLL: never look for them next to the app or in the
// current directory (DLL planting), only in System32.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
