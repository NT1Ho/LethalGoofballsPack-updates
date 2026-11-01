using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LcPack {
public static class Channel {
    public const string Repo = "NT1Ho/LethalGoofballsPack-updates";
    public const string Web = "https://github.com/" + Repo;
    public const string Latest = Web + "/releases/latest/download/manifest.json";
    public const string ClientName = "Lethal company pack Updater.exe";
}
public class PackFile { public string path; public string sha256; public long size; public string url; }
public class Manifest {
    public int schema = 1;
    public string version = "";
    public List<PackFile> files = new List<PackFile>();
    public List<string> deletes = new List<string>();
}
public class LocalState { public int schema = 1; public int newBadgeSchema; public string version = ""; public List<PackFile> files = new List<PackFile>(); public List<string> updatedPaths = new List<string>(); }
public class Change { public string path; public bool existed; public string oldHash; public string newHash; public string backup; }
public class Journal { public bool committed; public bool hadState; public string stateOldHash; public List<Change> changes = new List<Change>(); }
public static class Data {
    public static string Json(object value) { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(value); }
    public static T Read<T>(string text) { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024, RecursionLimit = 32 }.Deserialize<T>(text); }
    public static T Load<T>(string path) { return Read<T>(File.ReadAllText(path, Encoding.UTF8)); }
    public static void Atomic(string path, string text) {
        string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try {
            using (var s = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                byte[] b = Encoding.UTF8.GetBytes(text); s.Write(b, 0, b.Length); s.Flush(true);
            }
            Replace(tmp, path);
        } finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static void Replace(string source, string dest) {
        if (File.Exists(dest)) File.Replace(source, dest, null); else File.Move(source, dest);
    }
    public static string Hash(string path) {
        using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var h = SHA256.Create()) return Hex(h.ComputeHash(s));
    }
    public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    public static void CopyDurable(string source, string dest) {
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
            input.CopyTo(output); output.Flush(true);
        }
    }
}
public static class SafePath {
    static readonly string[] roots = { "BepInEx/config/", "BepInEx/core/", "BepInEx/plugins/", "BepInEx/patchers/", "BepInEx/pack-info/" };
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FindData {
        public uint attributes, createdLow, createdHigh, accessedLow, accessedHigh, writtenLow, writtenHigh, sizeHigh, sizeLow, tag, reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string alternate;
    }
    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileW", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindFirstFile(string name, out FindData data);
    [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr handle);
    static bool IsCloudPlaceholder(string path) {
        FindData data; IntPtr handle = FindFirstFile(path, out data);
        if (handle == new IntPtr(-1)) throw new IOException("无法验证路径类型：" + path);
        try { return (data.tag & 0xFFFF0FFFu) == 0x9000001Au; } finally { FindClose(handle); }
    }
    public static string Validate(string path) {
        if (String.IsNullOrEmpty(path) || path.Length > 210 || path.Contains('\\') || path.StartsWith("/") || path.Any(c => c < 32) || path.IndexOfAny(new[]{ ':','*','?','"','<','>','|' }) >= 0)
            throw new InvalidDataException("文件路径不安全：" + path);
        foreach (string part in path.Split('/')) {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") ||
                Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("文件路径不安全：" + path);
        }
        if (!roots.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) &&
            !new[]{"winhttp.dll","doorstop_config.ini",".doorstop_version"}.Contains(path, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("只允许管理 MOD 文件：" + path);
        return path;
    }
    public static void NoLinks(string path) {
        string full = Path.GetFullPath(path);
        for (string p = full; !String.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            // Cloud placeholders store file data, unlike links which redirect the target path.
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0 && !IsCloudPlaceholder(p))
                throw new IOException("不支持符号链接或目录联接：" + p);
    }
    public static string Target(string root, string relative) {
        Validate(relative);
        string dir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(dir, StringComparison.OrdinalIgnoreCase) || path.Length >= 250) throw new IOException("路径越界或过长：" + relative);
        NoLinks(path); return path;
    }
    public static void ValidateManifest(Manifest m) {
        if (m == null || m.schema != 1 || String.IsNullOrWhiteSpace(m.version) || m.version.Length > 100 || m.files == null || m.deletes == null || m.files.Count > 10000 || m.deletes.Count > 10000)
            throw new InvalidDataException("版本清单无效或更新器版本过旧。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.files) {
            if (f == null || !names.Add(Validate(f.path)) || !Regex.IsMatch(f.sha256 ?? "", "^[a-f0-9]{64}$") || f.size < 0 || f.size > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("版本清单有重复路径或无效校验值。");
            DownloadUrl(f.url);
        }
        foreach (var p in m.deletes) if (!names.Add(Validate(p))) throw new InvalidDataException("删除清单与文件清单冲突。");
        foreach (string p in names) {
            int at = p.IndexOf('/');
            while (at >= 0) { if (names.Contains(p.Substring(0, at))) throw new InvalidDataException("文件和文件夹路径冲突。"); at = p.IndexOf('/', at + 1); }
        }
    }
    public static void DownloadUrl(string value) {
        Uri u;
        if (!Regex.IsMatch(value ?? "", "^https://github[.]com/" + Regex.Escape(Channel.Repo) + "/releases/download/[a-zA-Z0-9._-]+/[a-f0-9]{64}[.]bin$") || !Uri.TryCreate(value, UriKind.Absolute, out u) || u.Scheme != "https" || u.Host != "github.com" || !u.IsDefaultPort || u.UserInfo.Length != 0 || u.Query.Length != 0 || u.Fragment.Length != 0 ||
            !u.AbsolutePath.StartsWith("/" + Channel.Repo + "/releases/download/", StringComparison.Ordinal) || u.AbsolutePath.Contains("%"))
            throw new InvalidDataException("更新清单包含不可信下载地址。");
    }
}
public partial class Transfer : IDisposable {
    readonly HttpClient client;
    public Transfer() : this(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { }
    public Transfer(HttpMessageHandler handler) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.DefaultConnectionLimit = 8;
        client = new HttpClient(handler); client.Timeout = TimeSpan.FromMinutes(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LethalGoofballsPack-Updater/1.0");
    }
    static void Check(HttpResponseMessage response) {
        if (!response.IsSuccessStatusCode) {
            throw new SourceUnavailableException(response.StatusCode);
        }
        Uri u = response.RequestMessage == null ? null : response.RequestMessage.RequestUri;
        if (u != null && (u.Scheme != "https" || !(Cloud.IsUrl(u)||u.Host == "github.com" || u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))))
            throw new IOException("下载被重定向到不可信地址。");
    }
    public Task<Manifest> Latest() {return Cloud.Enabled?CloudManifest():ReadManifest(Channel.Latest);}
    public Task<Manifest> ForRelease(string tag) {
        if(!Regex.IsMatch(tag??"","^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")||tag.Contains(".."))throw new InvalidDataException("云端版本标记无效。");
        return ReadManifest(Channel.Web+"/releases/download/"+tag+"/manifest.json?check="+Guid.NewGuid().ToString("N"));
    }
    async Task<Manifest> ReadManifest(string url) {
        var manifest=Data.Read<Manifest>(await TextFrom(url,8*1024*1024,20).ConfigureAwait(false));SafePath.ValidateManifest(manifest);return manifest;
    }
    public async Task Download(PackFile file, string destination, Action<long> progress) {
        SafePath.DownloadUrl(file.url);
        if(Cloud.Enabled) {
            bool cos=await PreferCos(file).ConfigureAwait(false);string primary=cos?Cloud.Blob(file):file.url,backup=cos?file.url:Cloud.Blob(file);Exception error=null;
            try {await DownloadFrom(file,primary,destination,progress,false).ConfigureAwait(false);return;}
            catch(Exception e){if(!NetworkFailure(e))throw;error=e;}
            try{await DownloadFrom(file,backup,destination,progress).ConfigureAwait(false);preferredSource=cos?2:1;return;}
            catch(Exception e){e.Data["PrimaryURL"]=primary;e.Data["PrimaryFailure"]=error.GetType().Name+": "+error.Message;throw;}
        }
        await DownloadFrom(file,file.url,destination,progress).ConfigureAwait(false);
    }
    async Task DownloadFrom(PackFile file,string url,string destination,Action<long> progress,bool retry=true) {
        for (int attempt = 0; ; attempt++) {
            long received = 0;string stage="连接文件下载";
            try {
                using(var headersTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,headersTimeout.Token).ConfigureAwait(false)) {
                    Check(response);
                    stage="下载文件内容";
                    if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != file.size) throw new InvalidDataException("下载大小不符：" + file.path);
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true)) {
                        byte[] buffer = new byte[65536];
                        while (true) {
                            int n;
                            using (var idle = new CancellationTokenSource(TimeSpan.FromSeconds(45))) n = await ReadNetwork(input,buffer,idle.Token).ConfigureAwait(false);
                            if (n == 0) break;
                            received += n; if (received > file.size) throw new InvalidDataException("下载大小超出清单：" + file.path);
                            await output.WriteAsync(buffer, 0, n).ConfigureAwait(false); if (progress != null) progress(n);
                        }
                        output.Flush(true);
                    }
                }
                if (received != file.size || Data.Hash(destination) != file.sha256) throw new InvalidDataException("SHA-256 校验失败：" + file.path);
                return;
            } catch (Exception e) {
                Mark(e,url,stage);
                if (File.Exists(destination)) File.Delete(destination);
                if (progress != null && received > 0) progress(-received);
                // With two sources, try the backup before waiting on the same failed route again.
                if (!retry || attempt >= 1 || !(e is HttpRequestException || e is TaskCanceledException)) throw;
            }
            await Task.Delay(700).ConfigureAwait(false);
        }
    }
    public void Dispose() { client.Dispose(); }
}
public class UpdateResult { public string version; public int downloaded; public int deleted; public int skipped; public bool latest; public bool backupPending; }
public class Engine {
    readonly string root, home, statePath, pending;
    readonly Action guard;
    public Engine(string root, Action gameGuard) {
        this.root = Path.GetFullPath(root); home = Path.Combine(this.root, ".lcpack-updater"); statePath = Path.Combine(home, "state.json"); pending = Path.Combine(home, "pending"); guard = gameGuard;
    }
    public static void CheckGameDirectory(string root) {
        if (!File.Exists(Path.Combine(root, "Lethal Company.exe")) || !File.Exists(Path.Combine(root, "UnityPlayer.dll")) || !File.Exists(Path.Combine(root, "Lethal Company_Data", "Managed", "Assembly-CSharp.dll")))
            throw new IOException("请把更新器放在 Lethal Company.exe 所在的游戏根目录。");
        SafePath.NoLinks(root);
    }
    public static bool GameRunning() {
        var ps = Process.GetProcessesByName("Lethal Company");
        bool running = ps.Length != 0; foreach (var p in ps) p.Dispose();
        return running;
    }
    public static void CheckGameStopped() {
        if (GameRunning()) throw new IOException("此操作需要修改游戏文件，请先退出 Lethal Company；仍可浏览故障诊断、MOD 和历史记录。");
    }
    public async Task<UpdateResult> Run(Transfer transfer, Action<string, int> progress, Action<string,string> versions = null) {
        CheckGameDirectory(root); guard(); SafePath.NoLinks(home); Directory.CreateDirectory(home);
        SafePath.NoLinks(Path.Combine(home, "update.lock"));
        using (var updateLock = new FileStream(Path.Combine(home, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            Recover();
            string localVersion=versions==null?null:State().version;
            if(versions!=null)versions(localVersion,null);
            progress("检查更新中……", -1);
            Manifest manifest = await transfer.Latest().ConfigureAwait(false);
            if(versions!=null)versions(localVersion,manifest.version);
            return await Apply(manifest, transfer, progress).ConfigureAwait(false);
        }
    }
    LocalState State() {
        if (!File.Exists(statePath)) return new LocalState();
        SafePath.NoLinks(statePath); var state = Data.Load<LocalState>(statePath);
        if (state == null || state.schema != 1 || state.files == null) throw new InvalidDataException("本地更新记录损坏，已停止以保护文件。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in state.files) if (f == null || !names.Add(SafePath.Validate(f.path)) || !Regex.IsMatch(f.sha256 ?? "", "^[a-f0-9]{64}$")) throw new InvalidDataException("本地管理记录无效。");
        return state;
    }
    List<string> UpdatedPaths(Manifest manifest, LocalState state) {
        var previous=state.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
        var updated=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(state.version==manifest.version) {
            updated.UnionWith(state.updatedPaths??new List<string>());
            // One-time repair for clients that lost same-version badges before this fix.
            string backups=Path.Combine(home,"backups");
            if(state.newBadgeSchema<1&&Directory.Exists(backups))try {
                SafePath.NoLinks(backups);
                foreach(string directory in Directory.GetDirectories(backups).OrderBy(x=>x,StringComparer.Ordinal))try {
                    string old=Path.Combine(directory,"state.old"),log=Path.Combine(directory,"journal.json");
                    SafePath.NoLinks(old);SafePath.NoLinks(log);
                    if(!File.Exists(old)||!File.Exists(log)||new FileInfo(old).Length>8*1024*1024||new FileInfo(log).Length>8*1024*1024)continue;
                    var journal=Data.Load<Journal>(log);
                    if(journal==null||!journal.committed||!journal.hadState||Data.Hash(old)!=journal.stateOldHash)continue;
                    var baseline=Data.Load<LocalState>(old);
                    if(baseline==null||baseline.schema!=1||baseline.version!=manifest.version||baseline.files==null||baseline.files.Any(f=>f==null))continue;
                    previous=baseline.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);break;
                }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        updated.UnionWith(manifest.files.Where(f=>!previous.ContainsKey(f.path)||previous[f.path].sha256!=f.sha256).Select(f=>f.path));
        return manifest.files.Where(f=>updated.Contains(f.path)&&!manifest.deletes.Contains(f.path,StringComparer.OrdinalIgnoreCase)).Select(f=>f.path).ToList();
    }
    internal async Task<UpdateResult> Apply(Manifest m, Transfer transfer, Action<string,int> progress) {
        SafePath.ValidateManifest(m); var state = State();
        progress("正在检查本地文件……",-1);
        var owned = state.files.ToDictionary(f => f.path, StringComparer.OrdinalIgnoreCase);
        var nextState = new LocalState { version = m.version, files = m.files, newBadgeSchema = 1, updatedPaths = UpdatedPaths(m,state) };
        var changed = new List<PackFile>(); var deletions = new List<string>(); int skipped = 0;
        foreach (var f in m.files) {
            string p = SafePath.Target(root, f.path);bool exists=File.Exists(p);
            // Adopt existing personal configs; later replace only explicitly changed pack defaults (with backup).
            if(exists&&f.path.StartsWith("BepInEx/config/",StringComparison.OrdinalIgnoreCase)&&f.path.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase)&&(!owned.ContainsKey(f.path)||owned[f.path].sha256==f.sha256))continue;
            if (!exists || new FileInfo(p).Length != f.size || Data.Hash(p) != f.sha256) changed.Add(f);
        }
        foreach (string n in m.deletes) {
            string p = SafePath.Target(root, n); if (!File.Exists(p)) continue;
            if (!owned.ContainsKey(n)) { skipped++; continue; }
            if (Data.Hash(p) != owned[n].sha256) throw new IOException("待删除文件已被本地修改，未删除：" + n);
            deletions.Add(n);
        }
        guard();
        if (changed.Count == 0 && deletions.Count == 0) {
            Data.Atomic(statePath, Data.Json(nextState));
            return new UpdateResult { version = m.version, latest = true, skipped = skipped };
        }
        if (Directory.Exists(pending)) throw new IOException("上次更新尚未恢复。");
        Directory.CreateDirectory(pending); var journal = new Journal { hadState = File.Exists(statePath) };
        bool journalWritten = false;
        try {
            long total = changed.Sum(f => f.size), done = 0, lastTick = 0;
            if(changed.Count>0){
                progress("正在选择下载线路……",-1);
                await transfer.PrepareDownload(changed[0]).ConfigureAwait(false);
                progress("正在连接下载源……",-1);
            }
            using (var gate = new SemaphoreSlim(4)) {
                await Task.WhenAll(changed.Select(async (f, i) => {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try { await transfer.Download(f, Path.Combine(pending, "new-" + i), bytes => {
                        long nowDone = Interlocked.Add(ref done, bytes), tick = DateTime.UtcNow.Ticks;
                        if (tick - Interlocked.Read(ref lastTick) > TimeSpan.TicksPerMillisecond * 120) {
                            Interlocked.Exchange(ref lastTick, tick); int percent = total == 0 ? 100 : (int)Math.Min(100, Math.Max(0, nowDone * 100 / total));
                            if(nowDone<=0)progress("正在重新连接下载源……",-1);
                            else progress("下载中…… " + (percent==0?"不足 1%":percent+"%"), percent);
                        }
                    }).ConfigureAwait(false); } finally { gate.Release(); }
                })).ConfigureAwait(false);
            }
            guard(); progress("正在备份并安装……", 100);
            if (journal.hadState) { journal.stateOldHash = Data.Hash(statePath); Data.CopyDurable(statePath, Path.Combine(pending, "state.old")); if (Data.Hash(Path.Combine(pending,"state.old")) != journal.stateOldHash) throw new IOException("更新记录备份校验失败。"); }
            foreach (var f in changed) Backup(journal, f.path, f.sha256);
            foreach (string n in deletions) Backup(journal, n, null);
            Data.Atomic(Path.Combine(pending, "journal.json"), Data.Json(journal)); journalWritten = true;
            for (int i = 0; i < changed.Count; i++) {
                guard(); var f = changed[i]; string dest = SafePath.Target(root, f.path);
                CheckUnchanged(journal.changes[i], dest); Directory.CreateDirectory(Path.GetDirectoryName(dest));
                Data.Replace(Path.Combine(pending, "new-" + i), dest);
            }
            for (int i = 0; i < deletions.Count; i++) {
                guard(); string p = SafePath.Target(root, deletions[i]); CheckUnchanged(journal.changes[changed.Count + i], p); File.Delete(p);
            }
            guard(); Data.Atomic(statePath, Data.Json(nextState));
            journal.committed = true; Data.Atomic(Path.Combine(pending, "journal.json"), Data.Json(journal));
        } catch {
            if (journalWritten) { try { Rollback(journal); } catch (Exception ex) { throw new IOException("更新失败，部分文件暂时无法恢复。关闭游戏后重试；备份仍保留。 " + ex.Message); } }
            if (Directory.Exists(pending)) DeleteInternal(pending);
            throw;
        }
        // A failure to archive backups must not roll back an already committed update.
        bool backupPending = false;
        try { Archive(); } catch (IOException) { backupPending = true; } catch (UnauthorizedAccessException) { backupPending = true; }
        return new UpdateResult { version = m.version, downloaded = changed.Count, deleted = deletions.Count, skipped = skipped, backupPending = backupPending };
    }
    void Backup(Journal j, string relative, string hash) {
        string p = SafePath.Target(root, relative); var c = new Change { path = relative, newHash = hash, existed = File.Exists(p), backup = "old-" + j.changes.Count };
        if (c.existed) { c.oldHash = Data.Hash(p); Data.CopyDurable(p, Path.Combine(pending, c.backup)); if (Data.Hash(Path.Combine(pending,c.backup)) != c.oldHash) throw new IOException("备份校验失败。"); }
        j.changes.Add(c);
    }
    void CheckUnchanged(Change c, string p) {
        if (File.Exists(p) != c.existed || (c.existed && Data.Hash(p) != c.oldHash)) throw new IOException("更新期间文件被其他程序修改：" + c.path);
    }
    void Rollback(Journal j) {
        foreach (var c in j.changes.AsEnumerable().Reverse()) {
            string p = SafePath.Target(root, c.path);
            if (!Regex.IsMatch(c.backup ?? "", "^old-[0-9]+$")) throw new InvalidDataException("恢复记录无效。");
            if (File.Exists(p)) {
                string h = Data.Hash(p);
                if (h != c.oldHash && h != c.newHash) throw new IOException("文件被外部修改，保留备份：" + c.path);
                if (h == c.oldHash && c.existed) continue;
            }
            if (c.existed) {
                string source = Path.Combine(pending, c.backup); SafePath.NoLinks(source);
                if (Data.Hash(source) != c.oldHash) throw new IOException("备份损坏：" + c.path);
                Directory.CreateDirectory(Path.GetDirectoryName(p)); string tmp = Path.Combine(pending,"restore-" + Guid.NewGuid().ToString("N"));
                Data.CopyDurable(source, tmp); Data.Replace(tmp, p);
            } else if (File.Exists(p)) File.Delete(p);
        }
        SafePath.NoLinks(statePath);
        if (j.hadState) {
            string source = Path.Combine(pending,"state.old"); SafePath.NoLinks(source);
            if (Data.Hash(source) != j.stateOldHash) throw new IOException("更新记录备份损坏，已保留恢复文件。");
            if (!File.Exists(statePath) || Data.Hash(statePath) != j.stateOldHash) { string tmp = Path.Combine(pending, "restore-state-" + Guid.NewGuid().ToString("N")); Data.CopyDurable(source,tmp); Data.Replace(tmp,statePath); }
        }
        else if (File.Exists(statePath)) File.Delete(statePath);
    }
    void Recover() {
        SafePath.NoLinks(pending);
        if (!Directory.Exists(pending)) return;
        string jp = Path.Combine(pending,"journal.json"); SafePath.NoLinks(jp);
        if (!File.Exists(jp)) { DeleteInternal(pending); return; }
        var journal = Data.Load<Journal>(jp);
        if (journal == null || journal.changes == null || journal.changes.Count > 10000) throw new InvalidDataException("恢复记录无效。");
        if (journal.committed) { Archive(); return; }
        guard(); Rollback(journal); DeleteInternal(pending);
    }
    void Archive() {
        string dir = Path.Combine(home,"backups"); SafePath.NoLinks(dir); Directory.CreateDirectory(dir);
        Directory.Move(pending,Path.Combine(dir,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,8)));
    }
    void DeleteInternal(string directory) {
        if (!String.Equals(Path.GetFullPath(directory), Path.GetFullPath(pending), StringComparison.OrdinalIgnoreCase)) throw new IOException("内部清理路径无效。");
        SafePath.NoLinks(directory);
        foreach (var p in Directory.GetFileSystemEntries(directory)) { SafePath.NoLinks(p); if (Directory.Exists(p)) throw new IOException("内部缓存出现意外目录。"); }
        Directory.Delete(directory,true);
    }
}
}
