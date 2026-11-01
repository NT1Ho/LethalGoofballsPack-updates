using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace LcPack {
public class PageMod {
    public string Key{get;set;} public string Name{get;set;} public string Purpose{get;set;}
    public int Order{get;set;} public bool? New{get;set;} public bool Own{get;set;}
    public string Details{get;set;} public string VersionColor{get;set;}
    public ModFeature[] Features(){return String.IsNullOrEmpty(Details)?new ModFeature[0]:(Details??"").Replace("\r\n","\n").Replace('\r','\n').Split(new[]{'\n'},StringSplitOptions.None).Select(s=>new ModFeature{Text=s.TrimStart().StartsWith("*")?s.TrimStart().Substring(1).TrimStart():s,SelfDeveloped=s.TrimStart().StartsWith("*")}).ToArray();}
}
public class PageContent {
    public const string RelativePath="BepInEx/pack-info/display-pages.json";
    public int Schema=1;
    public List<PageMod> Mods=new List<PageMod>();
    public List<HistoryRelease> History=new List<HistoryRelease>();
    public int ModNamePercent=30, ModVersionPercent=14, HistoryModPercent=32;
    public int FontSize=12;
    static string Identity(string key){
        if(String.Equals(key,"Giant Sapsucker Bodies",StringComparison.OrdinalIgnoreCase))return "KiwiBodies";
        if(String.Equals(key,"Indoor Hazard Guard",StringComparison.OrdinalIgnoreCase))return "IndoorHazardGuard";
        if(String.Equals(key,"Universal Wrench",StringComparison.OrdinalIgnoreCase))return "UniversalWrench";
        return key;
    }
    public PageMod Find(string key){return Mods.FirstOrDefault(m=>String.Equals(m.Key,key,StringComparison.OrdinalIgnoreCase))??Mods.FirstOrDefault(m=>String.Equals(Identity(m.Key),Identity(key),StringComparison.OrdinalIgnoreCase));}
    public IEnumerable<T> DisplayOrder<T>(IEnumerable<T> items,Func<T,string> key,Func<T,PageMod> metadata,Func<T,bool> detectedNew){
        return items.Select(item=>{
            string id=key(item);var m=metadata(item);
            bool own=m==null?ModNames.OwnRank(id)>0:m.Own;
            bool fresh=m!=null&&m.New.HasValue?m.New.Value:detectedNew(item);
            return new{item,id=m==null?id:m.Key,group=own?0:fresh?1:2,order=m==null?Int32.MaxValue:m.Order};
        }).OrderBy(x=>x.group).ThenBy(x=>x.order).ThenBy(x=>x.id,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.id,StringComparer.Ordinal).Select(x=>x.item);
    }
    public static PageContent Load(string path){if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("展示资料超过 2 MB。");var p=Data.Load<PageContent>(path);p.Validate();return p;}
    public static PageContent ForRoot(string root){string p=Path.Combine(root,RelativePath.Replace('/',Path.DirectorySeparatorChar));return File.Exists(p)?Load(p):new PageContent{History=HistoryWindow.ReadHistory()};}
    public void Validate(){
        if(Schema!=1||Mods==null||History==null||Mods.Count>2000||History.Count>1000)throw new InvalidDataException("展示资料格式不支持。");
        if(FontSize<10||FontSize>20||ModNamePercent<20||ModNamePercent>50||ModVersionPercent<10||ModVersionPercent>25||HistoryModPercent<20||HistoryModPercent>50)throw new InvalidDataException("字号或列宽比例超出范围。");
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var m in Mods)if(m==null||String.IsNullOrWhiteSpace(m.Key)||String.IsNullOrWhiteSpace(m.Name)||!keys.Add(m.Key)||(m.Details??"").Length>20000||(m.Purpose??"").Length>4000)throw new InvalidDataException("MOD 标识重复、名称为空或介绍过长。");
        foreach(var m in Mods)if(!String.IsNullOrEmpty(m.VersionColor)&&!System.Text.RegularExpressions.Regex.IsMatch(m.VersionColor,@"\A#[0-9a-fA-F]{6}\z"))throw new InvalidDataException("版本颜色请填写 #RRGGBB，留空使用默认颜色。");
        var versions=new HashSet<string>();DateTimeOffset date;
        foreach(var h in History)if(h==null||String.IsNullOrWhiteSpace(h.Version)||!versions.Add(h.Version)||!DateTimeOffset.TryParse(h.PublishedUtc,out date)||h.Changes==null||h.Changes.Count>2000||h.Changes.Any(e=>e==null||String.IsNullOrWhiteSpace(e.Mod)||String.IsNullOrWhiteSpace(e.Summary)||e.Summary.Length>20000))throw new InvalidDataException("历史记录的版本、时间或更新内容无效。");
    }
}
}
