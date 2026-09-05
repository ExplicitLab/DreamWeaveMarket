using System.Reflection;
using System.Runtime.InteropServices;

// The version lives here and nowhere else. build.bat rewrites these two lines when given a
// version argument, the DLL is renamed to carry it (DreamweaveMarket-1.0.2.dll), and the dist
// folder and zip are named from the version read back out of the compiled assembly - so the
// file name can never claim a version the DLL does not have.
[assembly: AssemblyTitle("DreamweaveMarket")]
[assembly: AssemblyDescription("Opens the Dreamweave market site in a window over Asheron's Call.")]
[assembly: AssemblyProduct("DreamweaveMarket")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("b7e0f3a2-5c1d-4e8a-9f60-3d2a6c8e1b47")]
[assembly: AssemblyVersion("1.8.2.0")]
[assembly: AssemblyFileVersion("1.8.2.0")]
