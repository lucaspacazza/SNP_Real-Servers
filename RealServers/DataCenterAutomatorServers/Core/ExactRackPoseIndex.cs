using System;
using System.Collections.Generic;

namespace DataCenterAutomatorServers;

// Migration only: old saves contain a world pose but no custom rack UID.
// Match the exact serialized pose, never the closest rack or an occupied slot.
internal sealed class ExactRackPoseIndex
{
    private const double Tolerance = 0.0001;
    private readonly Dictionary<(long X,long Y,long Z), List<int>> _cells = new();
    private readonly List<float[]> _poses = new();

    public ExactRackPoseIndex(IEnumerable<float[]> poses)
    {
        foreach (var pose in poses)
        {
            int index = _poses.Count;
            _poses.Add((float[])pose.Clone());
            if (!Valid(pose)) continue;
            var cell = Cell(pose);
            if (!_cells.TryGetValue(cell,out var entries)) _cells[cell]=entries=new List<int>();
            entries.Add(index);
        }
    }

    public int FindUnique(float[] saved, Predicate<int> available)
    {
        if (!Valid(saved)) return -1;
        var center=Cell(saved);
        int match=-1;
        for (int x=-1;x<=1;x++) for(int y=-1;y<=1;y++) for(int z=-1;z<=1;z++)
        {
            if (!_cells.TryGetValue((center.X+x,center.Y+y,center.Z+z),out var entries)) continue;
            foreach(int index in entries)
            {
                if (!SamePose(saved,_poses[index]) || !available(index)) continue;
                if (match >= 0) return -1;
                match=index;
            }
        }
        return match;
    }

    private static (long X,long Y,long Z) Cell(float[] p)=>
        ((long)Math.Floor(p[0]/Tolerance),(long)Math.Floor(p[1]/Tolerance),(long)Math.Floor(p[2]/Tolerance));

    private static bool Valid(float[] p)
    {
        if (p == null || p.Length != 7) return false;
        foreach(float value in p) if(float.IsNaN(value)||float.IsInfinity(value)||Math.Abs(value)>1000000) return false;
        return p[3]*p[3]+p[4]*p[4]+p[5]*p[5]+p[6]*p[6] > 0.0001f;
    }

    private static bool SamePose(float[] a,float[] b)
    {
        for(int axis=0;axis<3;axis++) if(Math.Abs((double)a[axis]-b[axis])>Tolerance) return false;
        double dot=0,na=0,nb=0;
        for(int axis=3;axis<7;axis++){dot+=(double)a[axis]*b[axis];na+=(double)a[axis]*a[axis];nb+=(double)b[axis]*b[axis];}
        return 1-Math.Abs(dot)/Math.Sqrt(na*nb) <= 0.000001;
    }
}
