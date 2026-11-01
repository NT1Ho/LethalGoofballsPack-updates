using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LcPack {
public sealed class ConfigSetting {
    public string Section {get;set;}public string Name {get;set;}public string Value {get;set;}
    internal int Start,Length;
}
public sealed class AdminConfig {
    readonly string original;
    readonly bool bom;
    public readonly List<ConfigSetting> Settings=new List<ConfigSetting>();
    public AdminConfig(string file){
        if(new FileInfo(file).Length>1024*1024)throw new IOException("文件超过 1 MB，请使用替换文件。");
        byte[] bytes=File.ReadAllBytes(file);bom=bytes.Take(3).SequenceEqual(new byte[]{239,187,191});
        original=new UTF8Encoding(false,true).GetString(bytes,bom?3:0,bytes.Length-(bom?3:0));
        string section="常规";
        foreach(Match line in Regex.Matches(original,@"[^\r\n]+")){
            string trimmed=line.Value.Trim();
            if(trimmed.StartsWith("[")&&trimmed.EndsWith("]")){section=trimmed.Substring(1,trimmed.Length-2);continue;}
            if(trimmed.StartsWith("#")||trimmed.StartsWith(";"))continue;
            int equals=line.Value.IndexOf('=');
            if(equals<=0)continue;
            int start=equals+1;while(start<line.Length&&Char.IsWhiteSpace(line.Value[start]))start++;
            Settings.Add(new ConfigSetting{Section=section,Name=line.Value.Substring(0,equals).Trim(),Value=line.Value.Substring(start),Start=line.Index+start,Length=line.Length-start});
        }
        if(Settings.Count==0)throw new IOException("未找到可编辑的键值设置，请使用替换文件。");
    }
    public void Write(string file){
        var text=new StringBuilder(original);
        foreach(var setting in Settings.AsEnumerable().Reverse()){
            string value=setting.Value??"";
            if(value.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new IOException("配置值必须为单行。");
            text.Remove(setting.Start,setting.Length).Insert(setting.Start,value);
        }
        File.WriteAllText(file,text.ToString(),new UTF8Encoding(bom));
    }
}
}
