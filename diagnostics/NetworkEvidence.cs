using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Netcode;

namespace LcDiagnostics {
internal sealed class NetworkEvidence : IDisposable {
    internal const string Version = "0.2.0";
    internal const string Bridge = "NT1Ho.LethalDiagnostics.Report.v1";
    const string Channel = "nt1ho.lethaldiagnostics.coop";
    const int Protocol = 1, MaxPacket = 8192, MaxPeers = 32, MaxSignals = 12;
    const double HelloTimeout = 6, Retention = 180;

    internal sealed class Signal {
        public string source, category, code, status, link, detail, utc;
        public double time;
        public long id;
        public bool critical;
        internal object Row() => Json.Obj("source",source,"category",category,"code",code,"status",status,
            "link",link,"detail",detail,"utc",utc,"time",time,"id",id,"critical",critical);
    }
    sealed class Packet {
        public string op, version, room, stream, scope, mark;
        public int protocol, seq, lost, lostKey;
        public bool share;
        public double time;
        public Signal[] items;
    }
    sealed class Peer {
        public ulong Client;
        public string Token, Status;
        public double Joined;
        public bool Online;
    }
    sealed class Stream {
        public ulong Client;
        public int Last;
        public bool CanSend;
        public double NextBatch;
        public Receipt Pending;
    }
    sealed class Receipt {
        public int Sequence;
        public volatile bool Saved;
    }
    sealed class Stamp {
        internal readonly string Room;
        internal readonly double Time, At;
        internal readonly bool Host, Guest;
        internal Stamp(string room,double time,bool host,bool guest) {
            Room=room;Time=time;At=Now;Host=host;Guest=guest;
        }
        internal double Estimate => Time<0 ? -1 : Time + Math.Max(0,Now-At);
    }

    static double Now => Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    static readonly Regex Sensitive = new Regex(@"(?i)\b(?:https?://\S+|(?:steamid|authorization|password|token|secret|api[_ -]?key|connectionaddress)\s*[:=]\s*\S+)|(?:[a-z]:[\\/](?:users|documents and settings)[\\/][^\\/\s]+)|\b(?:\d{1,3}\.){3}\d{1,3}\b|\b76561\d{12}\b",
        RegexOptions.CultureInvariant);
    readonly Recorder recorder;
    readonly bool sharing;
    string token=Guid.NewGuid().ToString("N");
    readonly object gate=new object();
    readonly Queue<Signal> high=new Queue<Signal>(), standard=new Queue<Signal>();
    readonly Dictionary<ulong,Peer> peers=new Dictionary<ulong,Peer>();
    readonly Dictionary<string,Stream> streams=new Dictionary<string,Stream>();
    readonly Dictionary<ulong,double> rejectedUntil=new Dictionary<ulong,double>();
    string scope="", gapRoom="";
    int gapCritical,gapOrdinary;
    NetworkManager net;
    CustomMessagingManager messaging;
    volatile Stamp stamp=new Stamp("",-1,false,false);
    string room="",boundRoom="";
    double nextTick,nextHello,nextSend,offlineSince;
    int dropped,droppedKey,nextSequence;
    long nextSignal;
    Packet inflight;
    volatile string[] startupIssues;
    bool host,guest,inventorySent,failed;
    long receivedBytes,sentBytes;
    public bool IsGuest => guest;
    public object Metrics => Json.Obj("coopSentBytes",sentBytes,"coopReceivedBytes",receivedBytes,"coopOrdinaryLost",dropped,"coopImportantLost",droppedKey,"coopPending",inflight?.seq);

    public NetworkEvidence(Recorder recorder,bool sharing) {
        this.recorder=recorder;this.sharing=sharing;

    }
    static bool IsFirstParty(string id) => !id.StartsWith("000.",StringComparison.Ordinal)&&
        (id.IndexOf("hatchet",StringComparison.OrdinalIgnoreCase)>=0 || id.IndexOf("universalwrench",StringComparison.OrdinalIgnoreCase)>=0);
    internal static bool PrivateLog(string source,string text) {
        return source.IndexOf("chat",StringComparison.OrdinalIgnoreCase)>=0 ||
            source.IndexOf("voice",StringComparison.OrdinalIgnoreCase)>=0 ||
            text.IndexOf("AddChatMessage",StringComparison.OrdinalIgnoreCase)>=0 ||
            text.IndexOf("chat message",StringComparison.OrdinalIgnoreCase)>=0 ||
            text.StartsWith("[Chat]",StringComparison.OrdinalIgnoreCase);
    }
    internal static string Clean(string text,int max) {
        if(String.IsNullOrEmpty(text))return "";
        if(text.Length>max*2)text=text.Substring(0,max*2);
        text=Sensitive.Replace(text,"[已隐藏]").Replace("\0","");
        return text.Length>max?text.Substring(0,max):text;
    }
    internal void Emit(string source,string category,string code,string status,string link,string detail,bool critical) {
        var clock=stamp;
        var item=new Signal {source=Clean(source,96),category=Clean(category,48),code=Clean(code,64),
            status=Clean(status,40),link=Clean(link,116),detail=Clean(detail,920),utc=DateTime.UtcNow.ToString("O"),
            time=clock.Estimate,id=System.Threading.Interlocked.Increment(ref nextSignal),critical=critical};
        WriteLocal(item,clock);
        if(!sharing||!clock.Guest||String.IsNullOrEmpty(clock.Room))return;
        lock(gate){
            if(clock.Room!=room)return;
            var q=critical?high:standard;
            if(q.Count>=(critical?256:512)){q.Dequeue();if(critical)droppedKey++;else dropped++;}
            q.Enqueue(item);
        }
    }
    void WriteLocal(Signal signal,Stamp clock) {
        var row=Json.Obj("room",clock.Room,"role",clock.Host?"host":clock.Guest?"guest":"local","peer",token,"event",signal.Row());
        if(signal.critical)recorder.AddImportant("coop_event",row);else recorder.Add("coop_event",row);
    }
    internal void Tick() {
        if(failed||Now<nextTick)return;
        nextTick=Now+.25;
        try {
            var current=NetworkManager.Singleton;
            var active=current!=null&&current.IsListening&&!current.ShutdownInProgress?current.CustomMessagingManager:null;
            if(!ReferenceEquals(active,messaging)||active!=null&&host!=current.IsServer) {
                Disconnect();
                if(active!=null){
                    net=current;messaging=active;host=current.IsServer;guest=!host;
                    scope=String.Join(", ",Chainloader.PluginInfos.Values.Where(i=>IsFirstParty(i.Metadata.GUID)).Select(i=>i.Metadata.Name+" "+i.Metadata.Version));
                    if(host){
                        if(room.Length!=0)Expire("became_host");
                        room=Guid.NewGuid().ToString("N");boundRoom="";stamp=new Stamp(room,net.ServerTime.Time,true,false);
                    }
                    else if(!stamp.Guest)stamp=new Stamp("",-1,false,true);
                    if(sharing||guest)messaging.RegisterNamedMessageHandler(Channel,Receive);
                    nextHello=0;
                    recorder.AddImportant("coop_room",Json.Obj("room",host?room:"","role",host?"host":"guest",
                        "sharing",sharing,"protocol",Protocol,"version",Version,"scope",scope));
                }
            }
            if(messaging==null){
                if(offlineSince>0&&Now-offlineSince>Retention)Expire("reconnect_timeout");
                return;
            }
            if(host) {
                stamp=new Stamp(room,net.ServerTime.Time,true,false);
                if(sharing)PollPeers();
                return;
            }
            if(!net.IsConnectedClient){
                if(boundRoom.Length>0)GuestOffline();
                if(offlineSince>0&&Now-offlineSince>Retention)Expire("reconnect_timeout");
                return;
            }
            if(boundRoom.Length==0){
                if(Now>=nextHello){
                    nextHello=Now+1.5;
                    Send(NetworkManager.ServerClientId,new Packet{op="hello",protocol=Protocol,version=Version,stream=token,scope=scope,share=sharing,room=room});
                }
                return;
            }
            stamp=new Stamp(room,net.ServerTime.Time,false,true);
            if(!sharing)return;
            if(!inventorySent){inventorySent=true;Inventory();}
            var issueBatch=System.Threading.Interlocked.Exchange(ref startupIssues,null);
            if(issueBatch!=null)foreach(string issue in issueBatch)
                Emit("BepInEx","inventory","startup_issue","unavailable","",issue,true);
            Pump();
        }catch(Exception e){
            failed=true;
            recorder.AddImportant("coop_fault",Json.Obj("type",e.GetType().Name,"detail",Clean(e.Message,320)));
            Disconnect();
        }
    }
    internal void SetStartupIssues(string[] issues) => startupIssues=issues;
    void Inventory() {
        foreach(var mod in Chainloader.PluginInfos.Values.Take(400)) {
            string dependencies=String.Join(", ",mod.Dependencies.Select(d=>d.DependencyGUID+" "+d.MinimumVersion+" "+d.Flags));
            Emit(mod.Metadata.GUID,"inventory","mod_loaded","loaded","","name="+mod.Metadata.Name+" | version="+mod.Metadata.Version+" | dependencies="+dependencies,false);
        }
        Emit("BepInEx","inventory","inventory_end","complete","","loaded="+Chainloader.PluginInfos.Count+" cap=400",false);
    }
    void Disconnect() {
        if(messaging==null)return;
        try{messaging.UnregisterNamedMessageHandler(Channel);}catch{}
        if(host){
            foreach(var p in peers.Values.Where(p=>p.Online))Coverage(p,"room_closed",null);
            room="";stamp=new Stamp("",-1,false,false);
        }else if(room.Length>0)GuestOffline();
        peers.Clear();streams.Clear();rejectedUntil.Clear();
        net=null;messaging=null;boundRoom="";host=false;guest=false;
    }
    void GuestOffline() {
        if(offlineSince>0)return;
        recorder.AddImportant("coop_link",Json.Obj("room",room,"status","disconnected","unconfirmed",inflight?.seq,"criticalLost",droppedKey,"ordinaryLost",dropped));
        boundRoom="";nextHello=0;offlineSince=Now;
    }
    void Expire(string why) {
        lock(gate){
            recorder.AddImportant("coop_link",Json.Obj("room",room,"status",why,"criticalBuffered",high.Count,
                "ordinaryBuffered",standard.Count,"unconfirmed",inflight?.seq,"criticalLost",droppedKey,"ordinaryLost",dropped));
            gapRoom=room;
            gapCritical=droppedKey+high.Count+(inflight?.items.Count(x=>x.critical)??0);
            gapOrdinary=dropped+standard.Count+(inflight?.items.Count(x=>!x.critical)??0);
            high.Clear();standard.Clear();inflight=null;nextSequence=dropped=droppedKey=0;
            room="";stamp=new Stamp("",-1,false,true);
        }
        if(why=="reconnect_timeout")token=Guid.NewGuid().ToString("N");
        boundRoom="";offlineSince=0;inventorySent=false;
    }
    void PollPeers() {
        foreach(ulong id in net.ConnectedClientsIds) {
            if(id==NetworkManager.ServerClientId)continue;
            Peer p;
            if(!peers.TryGetValue(id,out p)){
                if(peers.Count>=MaxPeers)continue;
                peers[id]=p=new Peer{Client=id,Joined=Now,Online=true};
                Coverage(p,"waiting",null);
            }
            if(p.Status=="waiting"&&Now-p.Joined> HelloTimeout)Coverage(p,"unreported",null);
        }
        foreach(var p in peers.Values.Where(p=>!net.ConnectedClients.ContainsKey(p.Client)).ToArray()){
            if(p.Online)Coverage(p,"disconnected",null);
            peers.Remove(p.Client);rejectedUntil.Remove(p.Client);
        }
        foreach(var pair in streams){
            var st=pair.Value;var r=st.Pending;
            if(r==null||!r.Saved)continue;
            st.Last=r.Sequence;st.Pending=null;
            Peer p;
            if(peers.TryGetValue(st.Client,out p)&&p.Token==pair.Key)Ack(st.Client,pair.Key,st.Last);
        }
    }
    int? Slot(ulong id) {
        var round=StartOfRound.Instance;
        int slot;
        return round!=null&&round.ClientPlayerList.TryGetValue(id,out slot)?slot:(int?)null;
    }
    void Coverage(Peer peer,string status,Packet packet) {
        if(peer.Status==status)return;
        peer.Status=status;
        recorder.AddImportant("coop_peer",Json.Obj("room",room,"role","guest","peer",peer.Token,
            "client",peer.Client,"player",Slot(peer.Client),"status",status,"time",net!=null?net.ServerTime.Time:-1,
            "scope",packet==null?null:Clean(packet.scope,320),"plugin",packet==null?null:Clean(packet.version,30)));
    }
    void NotifyDisconnectBacklog(Packet p) {
        recorder.AddImportant("coop_link",Json.Obj("room",p.room,"status","connected","plugin",Clean(p.version,30),
            "localPeer",token,"source",NetworkManager.ServerClientId,"priorPending",inflight?.seq,"hostTime",p.time));
    }
    internal void Mark(string id) {
        if(!host||!sharing||messaging==null)return;
        var time=net.ServerTime.Time;
        recorder.AddImportant("coop_mark",Json.Obj("room",room,"role","host","peer",token,"id",id,"time",time));
        foreach(var p in peers.Values)
            if(p.Status=="active"&&net.ConnectedClients.ContainsKey(p.Client))
                Send(p.Client,new Packet{op="mark",room=room,mark=id,time=time});
    }
    void Pump() {
        if(Now<nextSend)return;
        if(inflight==null) {
            lock(gate) {
                if(high.Count+standard.Count==0)return;
                var batch=new List<Signal>();
                int budget=MaxPacket/2-460;
                while(batch.Count<MaxSignals&&(high.Count>0||standard.Count>0)){
                    var q=high.Count>0?high:standard;
                    int cost=JsonConvert.SerializeObject(q.Peek()).Length+2;
                    if(batch.Count!=0&&cost>budget)break;
                    budget-=cost;batch.Add(q.Dequeue());
                }
                inflight=new Packet{op="batch",room=room,stream=token,seq=++nextSequence,lost=dropped,lostKey=droppedKey,items=batch.ToArray()};
            }
        }
        nextSend=Now+1;
        Send(NetworkManager.ServerClientId,inflight);
    }
    void Ack(ulong id,string stream,int seq) => Send(id,new Packet{op="ack",room=room,stream=stream,seq=seq});
    void Send(ulong id,Packet packet) {
        string json=JsonConvert.SerializeObject(packet);
        int bytes=4+json.Length*2;
        if(bytes>MaxPacket)return;
        using(var writer=new FastBufferWriter(bytes,Allocator.Temp)){
            writer.WriteValueSafe(json);
            messaging.SendNamedMessage(Channel,id,writer,NetworkDelivery.ReliableFragmentedSequenced);
        }
        sentBytes+=bytes;
    }
    void Receive(ulong sender,FastBufferReader reader) {
        int bytes=reader.Length-reader.Position;
        if(net==null||bytes>MaxPacket||bytes<4)return;
        if(host&&(!sharing||sender==NetworkManager.ServerClientId||!net.ConnectedClients.ContainsKey(sender)))return;
        if(!host&&sender!=NetworkManager.ServerClientId)return;
        double throttle;
        if(host&&rejectedUntil.TryGetValue(sender,out throttle)&&Now<throttle)return;
        try {
            int start=reader.Position;
            reader.ReadValueSafe(out int characters);
            if(characters<0||characters!=(bytes-4)/2||bytes!=4+2*characters)throw new InvalidDataException("Invalid packet length");
            reader.Seek(start);
            reader.ReadValueSafe(out string value);
            Packet p;
            using(var json=new JsonTextReader(new StringReader(value)) {MaxDepth=8,DateParseHandling=DateParseHandling.None})
                p=Newtonsoft.Json.JsonSerializer.CreateDefault().Deserialize<Packet>(json);
            if(p==null)return;
            receivedBytes+=bytes;
            if(host)HostReceive(sender,p);else GuestReceive(p);
        }catch(Exception e) {
            if(host&&rejectedUntil.Count<MaxPeers)rejectedUntil[sender]=Now+1;
            recorder.AddImportant("coop_packet_rejected",Json.Obj("room",room,"client",sender,"reason",e.GetType().Name,"detail",Clean(e.Message,240)));
        }
    }
    static bool Token(string text) => text!=null&&text.Length==32&&Guid.TryParseExact(text,"N",out _);
    void HostReceive(ulong sender,Packet p) {
        Peer peer;
        if(!peers.TryGetValue(sender,out peer)){
            if(peers.Count>=MaxPeers)return;
            peers[sender]=peer=new Peer{Client=sender,Joined=Now,Online=true};
        }
        if(p.op=="hello"){
            if(p.protocol!=Protocol||!Token(p.stream)){Coverage(peer,"incompatible",p);return;}
            Stream known;
            if(!streams.TryGetValue(p.stream,out known)){
                if(streams.Count>=MaxPeers*2){Coverage(peer,"stream_limit",p);return;}
                streams[p.stream]=known=new Stream{Client=sender};
            }else if(known.Client!=sender&&net.ConnectedClients.ContainsKey(known.Client))return;
            peer.Token=p.stream;known.Client=sender;known.CanSend=p.share;
            Coverage(peer,p.share?"active":"opted_out",p);
            Send(sender,new Packet{op="welcome",protocol=Protocol,version=Version,room=room,stream=p.stream,time=net.ServerTime.Time,seq=known.Last});
            return;
        }
        Stream st;
        if(p.op!="batch"||p.room!=room||p.stream==null||p.stream!=peer.Token||!streams.TryGetValue(p.stream,out st)||!st.CanSend)return;
        if(p.seq<=st.Last){if(Now>=st.NextBatch){st.NextBatch=Now+.15;Ack(sender,p.stream,st.Last);}return;}
        if(st.Pending!=null||p.seq!=st.Last+1||p.items==null||p.items.Length<1||p.items.Length>MaxSignals||Now<st.NextBatch)return;
        st.NextBatch=Now+.15;
        var rows=new List<object>(p.items.Length);
        int rejected=0;
        foreach(var s in p.items) {
            if(s==null||!Valid(s)){rejected++;continue;}
            bool metadata=s.category=="inventory"&&s.code=="mod_loaded";
            if(!metadata&&PrivateLog(s.source??"",s.detail??"")){rejected++;continue;}
            s.source=Clean(s.source,96);s.detail=Clean(s.detail,920);s.link=Clean(s.link,116);
            s.category=Clean(s.category,48);s.code=Clean(s.code,64);s.status=Clean(s.status,40);
            if(!double.IsFinite(s.time)||s.time>net.ServerTime.Time+2||s.time<net.ServerTime.Time-Retention-5)s.time=-1;
            rows.Add(s.Row());
        }
        var r=new Receipt{Sequence=p.seq};
        if(recorder.AddImportant("coop_batch",Json.Obj("room",room,"role","guest","peer",p.stream,"client",sender,
            "player",Slot(sender),"seq",p.seq,"receivedTime",net.ServerTime.Time,"criticalLost",Math.Max(0,p.lostKey),"ordinaryLost",Math.Max(0,p.lost),"rejectedItems",rejected,"items",rows),()=>r.Saved=true))st.Pending=r;
    }
    static bool Valid(Signal s) {
        return !String.IsNullOrEmpty(s.category)&&s.category.Length<=48&&s.code!=null&&s.code.Length<=64&&
            s.status!=null&&s.status.Length<=40&&s.utc!=null&&s.utc.Length<=40&&
            DateTimeOffset.TryParseExact(s.utc,"O",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _)&&
            (s.source?.Length??0)<=96&&(s.detail?.Length??0)<=920&&(s.link?.Length??0)<=116;
    }
    void GuestReceive(Packet p) {
        if(p.op=="welcome"){
            if(p.protocol!=Protocol||p.stream!=token||!Token(p.room))return;
            if(room.Length!=0&&room!=p.room)Expire("room_changed");
            room=boundRoom=p.room;offlineSince=0;nextSend=0;
            stamp=new Stamp(room,net.ServerTime.Time,false,true);
            if(inflight!=null&&p.seq==inflight.seq)inflight=null;
            NotifyDisconnectBacklog(p);
            if(gapRoom==room&&(gapCritical>0||gapOrdinary>0)){
                Emit(Version,"diagnostics","reconnect_gap","missing","",
                    "Earlier unrecoverable or unconfirmed critical="+gapCritical+" ordinary="+gapOrdinary,true);
            }
            gapRoom="";gapCritical=gapOrdinary=0;
            return;
        }
        if(!sharing||p.room!=boundRoom||boundRoom.Length==0)return;
        if(p.op=="ack"&&p.stream==token&&inflight!=null&&p.seq==inflight.seq){inflight=null;nextSend=0;}
        if(p.op=="mark"&&p.mark!=null&&Regex.IsMatch(p.mark,@"^\d{9}-\d{3}$"))
            recorder.Mark("host_mark",Json.Obj("room",room,"hostMark",Clean(p.mark,13),"time",p.time));
    }
    public void Dispose() {
        if(!host&&room.Length!=0)Expire("client_exit");
        Disconnect();
    }
}
}

