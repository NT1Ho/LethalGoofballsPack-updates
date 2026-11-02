using System;

namespace LcDiagnostics {
// Called once per performance report. A transient stall must not look like a sustained slowdown.
internal sealed class FrameDrop {
    readonly double[] recent=new double[7], sorted=new double[7];
    int count,head,bad,healthy;
    bool reported;
    public double ReferenceMs {get;private set;}
    public void Reset(){count=head=bad=healthy=0;reported=false;ReferenceMs=0;}
    public bool Observe(int frames,double totalMs,double worstMs,bool eligible){
        if(frames<2||totalMs<=worstMs){Reset();return false;}
        double typical=(totalMs-worstMs)/(frames-1);
        if(count>=5&&bad==0){
            Array.Copy(recent,sorted,count);Array.Sort(sorted,0,count);
            ReferenceMs=sorted[count/2];
        }
        bool slow=typical>=42||(ReferenceMs>0&&typical>=14&&typical>=2*ReferenceMs);
        if(slow){healthy=0;bad++;if(bad>=3&&!reported&&eligible){reported=true;return true;}return false;}
        if(++healthy>=2)reported=false;
        bad=0;recent[head]=typical;head=(head+1)%recent.Length;count=Math.Min(count+1,recent.Length);
        return false;
    }
}
}
