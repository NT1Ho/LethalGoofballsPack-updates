using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class DpiProbe { [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd); [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd); [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);
 [STAThread] static void Main(){
  
  Application.EnableVisualStyles();
  using(var f=new Form()){
   f.AutoScaleDimensions=new SizeF(96,96);f.AutoScaleMode=AutoScaleMode.Dpi;f.ClientSize=new Size(480,132);f.Show();Application.DoEvents();
   Console.WriteLine("NativeDpi="+GetDpiForWindow(f.Handle)+" Awareness="+GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(f.Handle))+" GraphicsDpi="+f.CreateGraphics().DpiX);Console.WriteLine("DeviceDpi="+f.DeviceDpi+" size="+f.ClientSize);
   foreach(var type in typeof(Form).Assembly.GetTypes())if(type.Name=="DpiHelper"){
    foreach(var p in type.GetProperties(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))try{Console.WriteLine(p.Name+"="+p.GetValue(null,null));}catch{}
   }
   foreach(var screen in Screen.AllScreens){f.Location=new Point(screen.WorkingArea.X+30,screen.WorkingArea.Y+30);Application.DoEvents();Console.WriteLine(screen.DeviceName+" bounds="+screen.Bounds+" dpi="+f.DeviceDpi+" size="+f.ClientSize);}
  }
 }
}
