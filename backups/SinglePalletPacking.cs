using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

class Rect { public int X, Y, W, H; public Rect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; } }

class Box
{
    public string SKU = ""; public int Quantity, Length, Width, Height, Weight;
    public int X, Y, Z; public bool Rotated;
    public long Volume => (long)Length * Width * Height;
    public Box CloneSingle() => (Box)MemberwiseClone();
}

class Program
{
    const int PL = 1200, PW = 1000;
    const string IN = "data", OUT = "results";
    const int MAX_CAND = 15;
    static void Main(string[] a)
    {
        if (a.Length == 0 || !int.TryParse(a[0], out int cf) || cf <= 0)
        { Console.WriteLine("Usage: dotnet run <clarityFactor>"); return; }
        cf = Math.Min(cf, Math.Min(PL, PW));

        Directory.CreateDirectory(IN); Directory.CreateDirectory(OUT);
        var files = Directory.GetFiles(IN, "*.csv");
        if (files.Length == 0) { Console.WriteLine("Нет CSV-файлов в папке data"); return; }

        double sum = 0, min = double.MaxValue, max = 0, sumT = 0;
        var gSw = Stopwatch.StartNew();

        foreach (var f in files)
        {
            var res = Path.Combine(OUT, $"{Path.GetFileNameWithoutExtension(f)}_result.csv");
            var (fill, t) = PackFile(f, res, cf);
            Console.WriteLine($"{Path.GetFileName(f)}: {fill:F2}%  ({t:F3}s)");
            sum += fill; min = Math.Min(min, fill); max = Math.Max(max, fill); sumT += t;
        }
        gSw.Stop();
        File.WriteAllText("summary.csv", string.Format(CultureInfo.InvariantCulture,
            "TotalTime,AvgTime,MinFill,MaxFill,AvgFill\n{0:F3},{1:F3},{2:F2},{3:F2},{4:F2}",
            gSw.Elapsed.TotalSeconds, sumT / files.Length, min, max, sum / files.Length));
        Console.WriteLine($"Summary: AvgFill = {sum / files.Length:F2}%  (min {min:F2}, max {max:F2})");
    }

    static (double fill, double time) PackFile(string csvIn, string csvOut, int cf)
    {
        var sw = Stopwatch.StartNew();

        var unplaced = Read(csvIn, cf)
                       .SelectMany(b => Enumerable.Range(0, b.Quantity).Select(_ => b.CloneSingle()))
                       .OrderByDescending(b => b.Volume)
                       .ToList();

        var placed = new List<Box>();
        int z = 0;

        while (unplaced.Count > 0)
        {
            var candHeights = unplaced.Select(b => b.Height)
                                      .Distinct().OrderBy(h => h)
                                      .Take(MAX_CAND).ToList();
            double bestVF = -1; int bestH = candHeights[0];

            foreach (int h in candHeights)
            {
                SimulateLayer(unplaced, h, out double vf);
                if (vf > bestVF) { bestVF = vf; bestH = h; }
            }

            var layerBoxes = unplaced.Where(b => b.Height <= bestH).ToList();
            if (layerBoxes.Count == 0)
            {
                bestH = unplaced.Max(b => b.Height);
                layerBoxes = unplaced.Where(b => b.Height == bestH).ToList();
            }

            var free = new List<Rect> { new Rect(0, 0, PL, PW) };
            var layerPlaced = new List<Box>();

            while (true)
            {
                var c = FindBest(layerBoxes, free, layerPlaced);
                if (c.iBox < 0) break;
                Place(layerBoxes, layerPlaced, free, c, z);
            }

            if (layerPlaced.Count == 0) break;

            foreach (var b in layerPlaced) unplaced.Remove(b);
            placed.AddRange(layerPlaced);
            z += bestH;
        }

        sw.Stop();
        double used = placed.Sum(b => (double)b.Volume),
               full = (double)PL * PW * z,
               fill = full > 0 ? used / full * 100 : 0;
        Write(csvOut, placed, sw.Elapsed.TotalSeconds, fill);
        return (fill, sw.Elapsed.TotalSeconds);
    }

    static void SimulateLayer(List<Box> source, int maxH, out double volumeFill)
    {
        var boxes = source.Where(b => b.Height <= maxH)
                          .Select(b => b.CloneSingle()).ToList();
        var free = new List<Rect> { new Rect(0, 0, PL, PW) };
        double usedVol = 0;

        while (true)
        {
            var c = FindBest(boxes, free, null);
            if (c.iBox < 0) break;
            var b = boxes[c.iBox]; usedVol += b.Volume;
            var r = free[c.iRect]; free.RemoveAt(c.iRect);
            int right = r.W - c.W, top = r.H - c.H;
            void Add(int x, int y, int w, int h) { if (w > 0 && h > 0) free.Add(new Rect(x, y, w, h)); }
            Add(r.X + c.W, r.Y, right, c.H);
            Add(r.X, r.Y + c.H, r.W, top);
            boxes.RemoveAt(c.iBox);
            for (int i = 0; i < free.Count; i++)
                for (int j = i + 1; j < free.Count; j++)
                {
                    if (Contains(free[i], free[j])) free.RemoveAt(j--);
                    else if (Contains(free[j], free[i])) { free.RemoveAt(i--); break; }
                }
        }
        volumeFill = usedVol / (PL * PW * maxH);
    }

    struct Cand { public int iBox, iRect, W, H; public bool Rot; public int Waste, Contact; }

    static Cand FindBest(List<Box> boxes, List<Rect> free, List<Box>? curLayer)
    {
        Cand best = new() { iBox = -1 }; var layer = curLayer ?? new();   // при симуляции curLayer=null
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            foreach (var v in new[] { (b.Length, b.Width, false), (b.Width, b.Length, true) })
            {
                int bw = v.Item1, bh = v.Item2; if (bw > PL || bh > PW) continue;
                for (int j = 0; j < free.Count; j++)
                {
                    var r = free[j]; if (bw > r.W || bh > r.H) continue;
                    int waste = r.W * r.H - bw * bh, contact = 0;
                    if (r.X == 0) contact += bh; if (r.Y == 0) contact += bw;
                    if (r.X + bw == PL) contact += bh; if (r.Y + bh == PW) contact += bw;
                    foreach (var p in layer)
                    {
                        if (r.Y == p.Y + p.Width || r.Y + bh == p.Y)
                            contact += Overlap(r.X, r.X + bw, p.X, p.X + p.Length);
                        if (r.X == p.X + p.Length || r.X + bw == p.X)
                            contact += Overlap(r.Y, r.Y + bh, p.Y, p.Y + p.Width);
                    }
                    bool better = best.iBox < 0 || waste < best.Waste ||
                                   (waste == best.Waste && contact > best.Contact);
                    if (better) best = new Cand
                    {
                        iBox = i,
                        iRect = j,
                        W = bw,
                        H = bh,
                        Rot = v.Item3,
                        Waste = waste,
                        Contact = contact
                    };
                }
            }
        }
        return best;
    }
    static int Overlap(int a1, int a2, int b1, int b2) => Math.Max(0, Math.Min(a2, b2) - Math.Max(a1, b1));

    static void Place(List<Box> src, List<Box> pl, List<Rect> free, Cand c, int z)
    {
        var b = src[c.iBox]; var r = free[c.iRect];
        b.Length = c.W; b.Width = c.H; b.Rotated = c.Rot; b.X = r.X; b.Y = r.Y; b.Z = z;
        pl.Add(b); src.RemoveAt(c.iBox); free.RemoveAt(c.iRect);
        int right = r.W - c.W, top = r.H - c.H;
        void Add(int x, int y, int w, int h) { if (w > 0 && h > 0) free.Add(new Rect(x, y, w, h)); }
        Add(r.X + c.W, r.Y, right, c.H); Add(r.X, r.Y + c.H, r.W, top);
        for (int i = 0; i < free.Count; i++)
            for (int j = i + 1; j < free.Count; j++)
            {
                if (Contains(free[i], free[j])) free.RemoveAt(j--);
                else if (Contains(free[j], free[i])) { free.RemoveAt(i--); break; }
            }
    }
    static bool Contains(Rect a, Rect b) =>
        b.X >= a.X && b.Y >= a.Y && b.X + b.W <= a.X + a.W && b.Y + b.H <= a.Y + a.H;

    static IEnumerable<Box> Read(string p, int cf)
    {
        foreach (var (l, i) in File.ReadLines(p).Select((l, idx) => (l, idx)))
        {
            if (i == 0 || string.IsNullOrWhiteSpace(l)) continue;
            var s = l.Split(','); if (s.Length < 6) continue;
            if (!int.TryParse(s[1], out int q)) continue;
            if (!double.TryParse(s[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var Ld)) continue;
            if (!double.TryParse(s[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var Wd)) continue;
            if (!double.TryParse(s[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var Hd)) continue;
            if (!int.TryParse(s[5], out int w)) continue;
            int L = (int)Math.Ceiling(Ld / cf) * cf,
                W = (int)Math.Ceiling(Wd / cf) * cf,
                H = (int)Math.Ceiling(Hd / cf) * cf;
            if (L <= 0 || W <= 0 || H <= 0 || q <= 0) continue;
            yield return new Box { SKU = s[0], Quantity = q, Length = L, Width = W, Height = H, Weight = w };
        }
    }
    static void Write(string p, IEnumerable<Box> b, double t, double f)
    {
        using var w = new StreamWriter(p);
        var iv = CultureInfo.InvariantCulture;
        w.WriteLine("SKU,X,Y,Z,Length,Width,Height,Rotated");
        foreach (var x in b)
            w.WriteLine($"{x.SKU},{x.X},{x.Y},{x.Z},{x.Length},{x.Width},{x.Height},{x.Rotated}");
        w.WriteLine(); w.WriteLine($"ExecutionTime,{t.ToString("F3", iv)} seconds");
        w.WriteLine($"FillRate,{f.ToString("F2", iv)}%");
    }
}
