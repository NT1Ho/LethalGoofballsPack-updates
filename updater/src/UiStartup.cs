using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
namespace LcPack {
public static class UiStartup {
    public static void Run(Action run) {
        // Framework WinForms needs this section as well as the native DPI manifest.
        // Materialize it temporarily so each delivered application stays a single EXE.
        string config=Path.GetTempFileName();
        try {
            File.WriteAllText(config,"<configuration><System.Windows.Forms.ApplicationConfigurationSection><add key=\"DpiAwareness\" value=\"PerMonitorV2\" /></System.Windows.Forms.ApplicationConfigurationSection></configuration>");
            AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE",config);
            Start(run);
        }finally{try{File.Delete(config);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Start(Action run) {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);run();}
}
}
