using System;using System.Drawing;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;using LcPack;
class DiagnosticsUiTests {
 static object Field(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Call(object o,string n){o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);}
 [STAThread] static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var w=new DiagnosticsWindow(args[0])){
  w.CreateControl();Call(w,"RefreshList");var sessions=(ListBox)Field(w,"sessions");for(int i=0;i<sessions.Items.Count;i++)if(((DiagnosticSession)sessions.Items[i]).Status=="正常结束"&&((DiagnosticSession)sessions.Items[i]).Marks>=2){sessions.SelectedIndex=i;break;}
  var end=DateTime.UtcNow.AddSeconds(4);while(DateTime.UtcNow<end){Application.DoEvents();System.Threading.Thread.Sleep(50);}
  var marks=(ListBox)Field(w,"marks");if(marks.Items.Count<2)throw new Exception("Need a real closed multi-marker session");
  for(int i=0;i<marks.Items.Count;i++){marks.SelectedIndex=i;Application.DoEvents();if(((PictureBox)Field(w,"photo")).Image!=null)break;}
  ((TextBox)Field(w,"note")).Text="隔离测试：假人死亡后仍然站着跳舞（备注功能样例，并非本次复现结论）。";((TextBox)Field(w,"group")).Text="diagnostics-v1-lan";Call(w,"Save");
  if(!((TextBox)Field(w,"report")).Text.Contains("备注功能样例"))throw new Exception("Note missing");
  if(((PictureBox)Field(w,"photo")).Image==null)throw new Exception("Screenshot preview missing");
  using(var b=new Bitmap(w.Width,w.Height)){w.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(args[1]);}
  Console.WriteLine("PASS real session/marker selection, screenshot decode, per-marker Chinese note save and report refresh; control render only, not human mouse verification");
 }}
}
