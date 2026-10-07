using System.Globalization;
using System.Text;
using MyDesktopApp.Geometry;

namespace MyDesktopApp.IO;

/// <summary>3B nokta (ACIS okuma ve izdüşüm için).</summary>
public readonly record struct P3(double X, double Y, double Z)
{
    public static P3 operator +(P3 a, P3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static P3 operator -(P3 a, P3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static P3 operator *(P3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public static P3 Cross(P3 a, P3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static double Dist(P3 a, P3 b) => (a - b).Length;
}

/// <summary>3B katı / bölge (ACIS SAT/SAB) verisinden kenar tel kafesini (wireframe) çıkarır.</summary>
public static class AcisReader
{
    private enum K { Ptr, Num, Word, Open, Close, End }

    private readonly record struct Tok(K Kind, double Num = 0, string? Text = null);

    private sealed class Rec
    {
        public string Type = "";
        public readonly List<Tok> Toks = new();
    }

    /// <summary>Kenarları 3B nokta dizileri olarak döndürür (düz kenar 2 nokta, eğriler örneklenmiş).</summary>
    public static List<List<P3>> ReadEdges(byte[]? data, string? text)
    {
        List<Rec> recs;
        if (data != null && data.Length > 15 && Encoding.ASCII.GetString(data, 0, 15) == "ACIS BinaryFile")
            recs = ParseSab(data);
        else
        {
            text ??= data != null ? Encoding.Latin1.GetString(data) : "";
            recs = ParseSat(DecodeSat(text));
        }
        return BuildEdges(recs);
    }

    // ================================================================ SAB (ikili)

    private static List<Rec> ParseSab(byte[] d)
    {
        int i = 15;
        i += 16;                                 // sürüm, kayıt/varlık sayısı, bayraklar
        var recs = new List<Rec>();
        var cur = new Rec();
        int header = 6;                          // ürün, sürüm, tarih, 3 ölçü değeri
        while (i < d.Length)
        {
            byte t = d[i++];
            Tok tok;
            switch (t)
            {
                case 0x02: tok = new Tok(K.Num, d[i]); i += 1; break;
                case 0x03: tok = new Tok(K.Num, BitConverter.ToInt16(d, i)); i += 2; break;
                case 0x04: tok = new Tok(K.Num, BitConverter.ToInt32(d, i)); i += 4; break;
                case 0x05: tok = new Tok(K.Num, BitConverter.ToSingle(d, i)); i += 4; break;
                case 0x06: tok = new Tok(K.Num, BitConverter.ToDouble(d, i)); i += 8; break;
                case 0x07: { int n = d[i++]; tok = new Tok(K.Word, Text: Encoding.Latin1.GetString(d, i, n)); i += n; break; }
                case 0x08: { int n = BitConverter.ToUInt16(d, i); i += 2; tok = new Tok(K.Word, Text: Encoding.Latin1.GetString(d, i, n)); i += n; break; }
                case 0x09: case 0x12: { int n = (int)BitConverter.ToUInt32(d, i); i += 4; tok = new Tok(K.Word, Text: Encoding.Latin1.GetString(d, i, n)); i += n; break; }
                case 0x0A: tok = new Tok(K.Word, Text: "T"); break;
                case 0x0B: tok = new Tok(K.Word, Text: "F"); break;
                case 0x0C: tok = new Tok(K.Ptr, BitConverter.ToInt32(d, i)); i += 4; break;
                case 0x0D:
                case 0x0E:
                    {
                        int n = d[i++];
                        string s = Encoding.Latin1.GetString(d, i, n);
                        i += n;
                        if (header > 0) { header--; continue; }
                        // 0x0E: bileşik ad parçası ("straight" + "curve" → "straight-curve")
                        if (cur.Toks.Count == 0 && (cur.Type.Length == 0 || cur.Type.EndsWith('-')))
                        {
                            cur.Type += s + (t == 0x0E ? "-" : "");
                            if (s.StartsWith("End-of", StringComparison.Ordinal)) return recs;
                            continue;
                        }
                        tok = new Tok(K.Word, Text: s);
                        break;
                    }
                case 0x0F: tok = new Tok(K.Open); break;
                case 0x10: tok = new Tok(K.Close); break;
                case 0x11:
                    recs.Add(cur);
                    cur = new Rec();
                    continue;
                case 0x13:
                case 0x14:
                    cur.Toks.Add(new Tok(K.Num, BitConverter.ToDouble(d, i)));
                    cur.Toks.Add(new Tok(K.Num, BitConverter.ToDouble(d, i + 8)));
                    tok = new Tok(K.Num, BitConverter.ToDouble(d, i + 16));
                    i += 24;
                    break;
                case 0x15: tok = new Tok(K.Word, Text: "#" + BitConverter.ToInt32(d, i)); i += 4; break;
                case 0x16: cur.Toks.Add(new Tok(K.Num, BitConverter.ToDouble(d, i))); tok = new Tok(K.Num, BitConverter.ToDouble(d, i + 8)); i += 16; break;
                case 0x17: tok = new Tok(K.Num, BitConverter.ToDouble(d, i)); i += 8; break;
                default:
                    throw new System.IO.InvalidDataException($"ACIS: bilinmeyen etiket 0x{t:X2}");
            }
            if (header > 0) { header--; continue; }
            cur.Toks.Add(tok);
        }
        return recs;
    }

    // ================================================================ SAT (metin)

    /// <summary>DWG/DXF içindeki SAT metni karakter kaydırmalı saklanır; gerekirse çözer.</summary>
    private static string DecodeSat(string s)
    {
        string t = s.TrimStart();
        if (t.Length == 0 || char.IsDigit(t[0])) return s;
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(c > ' ' && c < 159 ? (char)(159 - c) : c);
        return sb.ToString();
    }

    private static List<Rec> ParseSat(string s)
    {
        var lines = s.Replace("\r", "").Split('\n');
        // Başlık: ilk 3 satır (sürüm/sayılar, ürün bilgisi, birimler)
        var body = string.Join("\n", lines.Skip(3));
        var recs = new List<Rec>();
        var cur = new Rec();
        int i = 0;
        while (i < body.Length)
        {
            char c = body[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#') { recs.Add(cur); cur = new Rec(); i++; continue; }
            if (c == '{') { cur.Toks.Add(new Tok(K.Open)); i++; continue; }
            if (c == '}') { cur.Toks.Add(new Tok(K.Close)); i++; continue; }
            if (c == '@')
            {
                int j = i + 1;
                while (j < body.Length && char.IsDigit(body[j])) j++;
                int n = int.Parse(body.AsSpan(i + 1, j - i - 1), CultureInfo.InvariantCulture);
                j++;   // boşluk
                cur.Toks.Add(new Tok(K.Word, Text: body.Substring(Math.Min(j, body.Length), Math.Min(n, Math.Max(0, body.Length - j)))));
                i = j + n;
                continue;
            }
            int k = i;
            while (k < body.Length && !char.IsWhiteSpace(body[k]) && body[k] != '#' && body[k] != '{' && body[k] != '}') k++;
            string w = body.Substring(i, k - i);
            i = k;
            if (cur.Type.Length == 0 && cur.Toks.Count == 0)
            {
                // Kayıt numarası ("-12") olabilir
                if (w.StartsWith('-') && int.TryParse(w.AsSpan(1), out _)) continue;
                cur.Type = w;
                if (w.StartsWith("End-of", StringComparison.Ordinal)) return recs;
                continue;
            }
            if (w.StartsWith('$') && int.TryParse(w.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int p))
                cur.Toks.Add(new Tok(K.Ptr, p));
            else if (double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
                cur.Toks.Add(new Tok(K.Num, dv));
            else
                cur.Toks.Add(new Tok(K.Word, Text: w switch { "forward" => "F", "reversed" => "T", _ => w }));
        }
        return recs;
    }

    // ================================================================ Geometri

    private static List<double> Nums(Rec r) => r.Toks.Where(t => t.Kind == K.Num).Select(t => t.Num).ToList();
    private static List<int> Ptrs(Rec r) => r.Toks.Where(t => t.Kind == K.Ptr).Select(t => (int)t.Num).ToList();

    /// <summary>İlk işaretçiden hemen sonra sayı varsa (geçmiş kimliği), sayılar 1'den başlar.</summary>
    private static int NumStart(Rec r)
    {
        for (int i = 0; i < r.Toks.Count - 1; i++)
            if (r.Toks[i].Kind == K.Ptr) return r.Toks[i + 1].Kind == K.Num ? 1 : 0;
        return 0;
    }

    private static List<List<P3>> BuildEdges(List<Rec> recs)
    {
        // SAB'de 0. kayıt "asmheader" olabilir; işaretçiler kayıt sırasına göredir
        P3? PointOfVertex(int vi)
        {
            if (vi < 0 || vi >= recs.Count || recs[vi].Type != "vertex") return null;
            var pp = Ptrs(recs[vi]);
            if (pp.Count == 0) return null;
            int pi = pp[^1];
            if (pi < 0 || pi >= recs.Count || recs[pi].Type != "point") return null;
            var n = Nums(recs[pi]);
            return n.Count >= 3 ? new P3(n[^3], n[^2], n[^1]) : null;
        }

        var result = new List<List<P3>>();
        foreach (var r in recs)
        {
            if (r.Type != "edge") continue;
            var pp = Ptrs(r);
            if (pp.Count < 4) continue;
            int curveIdx = pp[^1], v1i = pp[^4], v2i = pp[^3];
            var a = PointOfVertex(v1i);
            var b = PointOfVertex(v2i);
            if (a == null || b == null) continue;
            var nums = Nums(r);
            double p0 = nums.Count >= 2 ? nums[^2] : 0, p1 = nums.Count >= 1 ? nums[^1] : 1;
            // Yön: eğri işaretçisinden sonraki ilk T/F
            bool reversed = false;
            int ci = r.Toks.FindLastIndex(t => t.Kind == K.Ptr);
            for (int k = ci + 1; k < r.Toks.Count; k++)
                if (r.Toks[k].Kind == K.Word) { reversed = r.Toks[k].Text == "T"; break; }

            List<P3>? pts = null;
            if (curveIdx >= 0 && curveIdx < recs.Count)
            {
                var cr = recs[curveIdx];
                try
                {
                    if (cr.Type == "ellipse-curve") pts = EllipseEdge(cr, a.Value, b.Value, Math.Abs(p1 - p0));
                    else if (cr.Type == "intcurve-curve") pts = SplineEdge(cr, a.Value, b.Value);
                }
                catch { pts = null; }
            }
            if (pts == null)
            {
                if (P3.Dist(a.Value, b.Value) < 1e-9) continue;
                pts = new List<P3> { a.Value, b.Value };
            }
            _ = reversed;
            result.Add(pts);
        }

        // Gövde dönüşümü (transform kaydı): p' = (p · M) · ölçek + öteleme
        var tr = recs.FirstOrDefault(x => x.Type == "transform");
        if (tr != null)
        {
            var n = Nums(tr);
            int s0 = NumStart(tr);
            if (n.Count >= s0 + 12)
            {
                double[] m = n.Skip(s0).Take(9).ToArray();
                var off = new P3(n[s0 + 9], n[s0 + 10], n[s0 + 11]);
                double sc = n.Count >= s0 + 13 && Math.Abs(n[s0 + 12]) > 1e-12 ? n[s0 + 12] : 1.0;
                bool identity = Math.Abs(m[0] - 1) + Math.Abs(m[4] - 1) + Math.Abs(m[8] - 1) + Math.Abs(m[1]) + Math.Abs(m[2]) +
                                Math.Abs(m[3]) + Math.Abs(m[5]) + Math.Abs(m[6]) + Math.Abs(m[7]) + off.Length + Math.Abs(sc - 1) < 1e-12;
                if (!identity)
                    foreach (var poly in result)
                        for (int j = 0; j < poly.Count; j++)
                        {
                            var q = poly[j];
                            poly[j] = new P3(
                                (q.X * m[0] + q.Y * m[3] + q.Z * m[6]) * sc + off.X,
                                (q.X * m[1] + q.Y * m[4] + q.Z * m[7]) * sc + off.Y,
                                (q.X * m[2] + q.Y * m[5] + q.Z * m[8]) * sc + off.Z);
                        }
            }
        }
        return result;
    }

    private static List<P3>? EllipseEdge(Rec cr, P3 a, P3 b, double sweepHint)
    {
        var n = Nums(cr);
        int s = NumStart(cr);
        if (n.Count < s + 10) return null;
        var c = new P3(n[s], n[s + 1], n[s + 2]);
        var nrm = new P3(n[s + 3], n[s + 4], n[s + 5]);
        var maj = new P3(n[s + 6], n[s + 7], n[s + 8]);
        double ratio = n[s + 9];
        double nl = nrm.Length;
        if (nl < 1e-12 || maj.Length < 1e-12) return null;
        nrm = nrm * (1 / nl);
        var min = P3.Cross(nrm, maj) * ratio;
        double R = maj.Length, r2 = min.Length;
        var u = maj * (1 / R);
        var v = r2 > 1e-12 ? min * (1 / r2) : P3.Cross(nrm, u);
        double Param(P3 p)
        {
            var d = p - c;
            double x = d.X * u.X + d.Y * u.Y + d.Z * u.Z;
            double y = d.X * v.X + d.Y * v.Y + d.Z * v.Z;
            return Math.Atan2(y / Math.Max(r2, 1e-12), x / R);
        }
        P3 At(double t) => c + maj * Math.Cos(t) + min * Math.Sin(t);

        double t0 = Param(a), t1 = Param(b);
        double ccw = GeoUtil.Sweep(t0, t1);                        // a → b saat yönü tersi
        bool closed = P3.Dist(a, b) < 1e-7;
        double sweep;
        if (closed) sweep = GeoUtil.TwoPi;
        else
        {
            // Kenar parametre aralığına (açı) en yakın yönü seç
            double cw = GeoUtil.TwoPi - ccw;
            sweep = Math.Abs(ccw - sweepHint) <= Math.Abs(cw - sweepHint) ? ccw : -cw;
        }
        int seg = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 36)), 2, 144);
        var pts = new List<P3>(seg + 1);
        for (int i = 0; i <= seg; i++) pts.Add(At(t0 + sweep * i / seg));
        pts[0] = a;
        pts[^1] = b;
        return pts;
    }

    /// <summary>intcurve: ilk "nubs"/"nurbs" eğri verisini okur, a–b arasını örnekler.</summary>
    private static List<P3>? SplineEdge(Rec cr, P3 a, P3 b)
    {
        var t = cr.Toks;
        int k = t.FindIndex(x => x.Kind == K.Word && (x.Text == "nubs" || x.Text == "nurbs"));
        if (k < 0) return null;
        bool rational = t[k].Text == "nurbs";
        int i = k + 1;
        double NextNum()
        {
            while (i < t.Count && t[i].Kind != K.Num) i++;
            if (i >= t.Count) throw new System.IO.InvalidDataException();
            return t[i++].Num;
        }
        int degree = (int)NextNum();
        // Kapalılık bilgisi (SAB'de sayı, SAT'ta sözcük) atlanır
        if (i < t.Count && t[i].Kind == K.Num) i++;
        else if (i < t.Count && t[i].Kind == K.Word) i++;
        int nk = (int)NextNum();
        if (degree < 1 || nk < 2 || nk > 100000) return null;
        var knots = new List<double>();
        for (int j = 0; j < nk; j++)
        {
            double kv = NextNum();
            int mult = (int)NextNum();
            for (int m = 0; m < mult; m++) knots.Add(kv);
        }
        int ncp = knots.Count - degree + 1;
        if (ncp < degree + 1) return null;
        // ACIS uçlarda derece kadar tekrar saklar; standart düğüm vektörü için birer ek
        knots.Insert(0, knots[0]);
        knots.Add(knots[^1]);
        var cps = new P3[ncp];
        var w = new double[ncp];
        for (int j = 0; j < ncp; j++)
        {
            cps[j] = new P3(NextNum(), NextNum(), NextNum());
            w[j] = rational ? NextNum() : 1.0;
        }

        // Tüm eğriyi örnekle
        int samples = Math.Clamp(ncp * 8, 32, 600);
        var all = new List<P3>(samples + 1);
        double u0 = knots[degree], u1 = knots[ncp];
        var dx = new double[degree + 1];
        var dy = new double[degree + 1];
        var dz = new double[degree + 1];
        var dw = new double[degree + 1];
        for (int s = 0; s <= samples; s++)
        {
            double u = s == samples ? u1 : u0 + (u1 - u0) * s / samples;
            int span;
            if (u >= knots[ncp]) span = ncp - 1;
            else
            {
                int lo = degree, hi = ncp;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (u < knots[mid]) hi = mid; else lo = mid; }
                span = lo;
            }
            for (int j = 0; j <= degree; j++)
            {
                int ix = span - degree + j;
                dx[j] = cps[ix].X * w[ix]; dy[j] = cps[ix].Y * w[ix]; dz[j] = cps[ix].Z * w[ix]; dw[j] = w[ix];
            }
            for (int r = 1; r <= degree; r++)
                for (int j = degree; j >= r; j--)
                {
                    int ix = span - degree + j;
                    double den = knots[ix + degree - r + 1] - knots[ix];
                    double al = den == 0 ? 0 : (u - knots[ix]) / den;
                    dx[j] = (1 - al) * dx[j - 1] + al * dx[j];
                    dy[j] = (1 - al) * dy[j - 1] + al * dy[j];
                    dz[j] = (1 - al) * dz[j - 1] + al * dz[j];
                    dw[j] = (1 - al) * dw[j - 1] + al * dw[j];
                }
            if (Math.Abs(dw[degree]) < 1e-300) return null;
            all.Add(new P3(dx[degree] / dw[degree], dy[degree] / dw[degree], dz[degree] / dw[degree]));
        }

        int Nearest(P3 p)
        {
            int best = 0;
            double bd = double.MaxValue;
            for (int j = 0; j < all.Count; j++)
            {
                double d = P3.Dist(all[j], p);
                if (d < bd) { bd = d; best = j; }
            }
            return best;
        }
        bool closedCurve = P3.Dist(all[0], all[^1]) < 1e-7;
        if (P3.Dist(a, b) < 1e-7)
        {
            var full = new List<P3>(all) { [0] = a };
            full[^1] = a;
            return full;
        }
        int ia = Nearest(a), ib = Nearest(b);
        List<P3> part;
        if (!closedCurve || Math.Abs(ib - ia) <= all.Count / 2)
        {
            part = ia <= ib ? all.GetRange(ia, ib - ia + 1) : all.GetRange(ib, ia - ib + 1);
            if (ia > ib) part.Reverse();
        }
        else
        {
            // Kapalı eğride sarmalanan kısa yol
            part = new List<P3>();
            int n = all.Count - 1;
            int step = ib > ia ? -1 : 1;
            for (int j = ia; j != ib; j = (j + step + n) % n) part.Add(all[j]);
            part.Add(all[ib]);
        }
        if (part.Count < 2) part = new List<P3> { a, b };
        part[0] = a;
        part[^1] = b;
        return part;
    }
}
