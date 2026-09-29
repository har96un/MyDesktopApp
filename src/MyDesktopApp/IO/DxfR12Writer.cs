using System.Globalization;
using System.IO;
using System.Text;
using MyDesktopApp.Geometry;
using MyDesktopApp.Model;

namespace MyDesktopApp.IO;

/// <summary>
/// AutoCAD R12 (AC1009) ASCII DXF yazıcısı. Birçok CNC / lazer / abkant yazılımı yalnızca bu sürümü okur.
/// Yalnızca LINE, ARC, CIRCLE, POLYLINE, TEXT nesneleri yazılır; ölçüler ve taramalar parçalanır.
/// </summary>
public static class DxfR12Writer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Write(string path, CadDocument source, IEnumerable<Entity>? only = null)
    {
        Encoding enc;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            enc = Encoding.GetEncoding(1254);
        }
        catch
        {
            enc = Encoding.ASCII;
        }

        var ents = (only ?? source.Entities).SelectMany(Flatten).ToList();
        var ext = BBox.Empty;
        foreach (var e in ents) ext.Add(e.Bounds());
        if (ext.IsEmpty) ext = BBox.FromPoints(Vec2.Zero, Vec2.Zero);

        // Kullanılan katmanlar (R12 ad kurallarına uygun)
        var layerNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string LayerName(string n)
        {
            if (!layerNames.TryGetValue(n, out var r)) layerNames[n] = r = SafeName(n, layerNames.Values);
            return r;
        }
        LayerName("0");
        foreach (var e in ents) LayerName(e.Layer);

        using var w = new StreamWriter(path, false, enc) { NewLine = "\r\n" };
        void G(int code, string value) { w.WriteLine(code.ToString(Inv).PadLeft(3)); w.WriteLine(value); }
        void D(int code, double v) => G(code, v.ToString("0.0#########", Inv));
        void I(int code, int v) => G(code, v.ToString(Inv).PadLeft(6));

        // HEADER
        G(0, "SECTION"); G(2, "HEADER");
        G(9, "$ACADVER"); G(1, "AC1009");
        G(9, "$DWGCODEPAGE"); G(3, "ANSI_1254");
        G(9, "$INSBASE"); D(10, 0); D(20, 0); D(30, 0);
        G(9, "$EXTMIN"); D(10, ext.MinX); D(20, ext.MinY); D(30, 0);
        G(9, "$EXTMAX"); D(10, ext.MaxX); D(20, ext.MaxY); D(30, 0);
        G(0, "ENDSEC");

        // TABLES
        G(0, "SECTION"); G(2, "TABLES");
        G(0, "TABLE"); G(2, "LTYPE"); I(70, 1);
        G(0, "LTYPE"); G(2, "CONTINUOUS"); I(70, 0); G(3, "Solid line"); I(72, 65); I(73, 0); D(40, 0);
        G(0, "ENDTAB");

        G(0, "TABLE"); G(2, "LAYER"); I(70, layerNames.Count);
        foreach (var (orig, name) in layerNames)
        {
            var li = source.Layers.TryGetValue(orig, out var l) ? l : new LayerInfo { Name = orig };
            int aci = Aci(li.Color);
            G(0, "LAYER"); G(2, name); I(70, 0); I(62, li.Visible ? aci : -aci); G(6, "CONTINUOUS");
        }
        G(0, "ENDTAB");

        G(0, "TABLE"); G(2, "STYLE"); I(70, 1);
        G(0, "STYLE"); G(2, "STANDARD"); I(70, 0); D(40, 0); D(41, 1); D(50, 0); I(71, 0); D(42, 2.5); G(3, "txt"); G(4, "");
        G(0, "ENDTAB");
        G(0, "ENDSEC");

        G(0, "SECTION"); G(2, "BLOCKS"); G(0, "ENDSEC");

        // ENTITIES
        G(0, "SECTION"); G(2, "ENTITIES");
        foreach (var e in ents)
        {
            void Common(string type)
            {
                G(0, type);
                G(8, LayerName(e.Layer));
                if (e.Color is { } c) I(62, Aci(c));
            }
            switch (e)
            {
                case LineEntity l:
                    Common("LINE");
                    D(10, l.Start.X); D(20, l.Start.Y); D(30, 0);
                    D(11, l.End.X); D(21, l.End.Y); D(31, 0);
                    break;
                case CircleEntity c:
                    Common("CIRCLE");
                    D(10, c.Center.X); D(20, c.Center.Y); D(30, 0); D(40, c.Radius);
                    break;
                case ArcEntity a:
                    Common("ARC");
                    D(10, a.Center.X); D(20, a.Center.Y); D(30, 0); D(40, a.Radius);
                    D(50, GeoUtil.RadToDeg(GeoUtil.NormalizeAngle(a.StartAngle)));
                    D(51, GeoUtil.RadToDeg(GeoUtil.NormalizeAngle(a.EndAngle)));
                    break;
                case PolylineEntity p:
                    Common("POLYLINE");
                    I(66, 1); D(10, 0); D(20, 0); D(30, 0); I(70, p.Closed ? 1 : 0);
                    foreach (var v in p.Vertices)
                    {
                        G(0, "VERTEX"); G(8, LayerName(e.Layer));
                        D(10, v.P.X); D(20, v.P.Y); D(30, 0);
                        if (Math.Abs(v.Bulge) > 1e-12) D(42, v.Bulge);
                    }
                    G(0, "SEQEND"); G(8, LayerName(e.Layer));
                    break;
                case TextEntity t:
                    Common("TEXT");
                    D(10, t.Position.X); D(20, t.Position.Y); D(30, 0); D(40, t.Height);
                    G(1, t.Value.Replace("\r", " ").Replace("\n", " "));
                    if (Math.Abs(t.Rotation) > 1e-12) D(50, GeoUtil.RadToDeg(t.Rotation));
                    break;
            }
        }
        G(0, "ENDSEC");
        G(0, "EOF");
    }

    /// <summary>R12'de olmayan nesneleri basit nesnelere çevirir.</summary>
    private static IEnumerable<Entity> Flatten(Entity e)
    {
        switch (e)
        {
            case BlockRefEntity br:
                foreach (var x in br.Explode().SelectMany(Flatten)) yield return x;
                break;
            case DimensionEntity d:
                foreach (var x in d.Explode().SelectMany(Flatten)) yield return x;
                break;
            case HatchEntity h:
                {
                    // Sınırlar kapalı polyline, desen çizgileri LINE olarak
                    foreach (var loop in h.Loops)
                    {
                        var pl = new PolylineEntity { Closed = true, Layer = h.Layer, Color = h.Color };
                        pl.Vertices.AddRange(loop);
                        yield return pl;
                    }
                    if (!h.IsSolid)
                        foreach (var (a, b) in h.PatternSegments())
                            yield return new LineEntity(a, b) { Layer = h.Layer, Color = h.Color };
                    break;
                }
            default:
                yield return e;
                break;
        }
    }

    private static readonly (int Aci, byte R, byte G, byte B)[] BasicAci =
    {
        (1, 255, 0, 0), (2, 255, 255, 0), (3, 0, 255, 0), (4, 0, 255, 255), (5, 0, 0, 255),
        (6, 255, 0, 255), (7, 255, 255, 255), (8, 128, 128, 128), (9, 192, 192, 192), (30, 255, 127, 0)
    };

    private static int Aci(EntColor c)
    {
        if (c.Aci is > 0 and < 256) return c.Aci;
        int best = 7, bd = int.MaxValue;
        foreach (var (a, r, g, b) in BasicAci)
        {
            int d = (r - c.R) * (r - c.R) + (g - c.G) * (g - c.G) + (b - c.B) * (b - c.B);
            if (d < bd) { bd = d; best = a; }
        }
        return best;
    }

    /// <summary>R12 katman adı: en çok 31 karakter, büyük harf, rakam, $ - _.</summary>
    private static string SafeName(string name, IEnumerable<string> used)
    {
        var map = new Dictionary<char, char> { ['ç'] = 'C', ['Ç'] = 'C', ['ğ'] = 'G', ['Ğ'] = 'G', ['ı'] = 'I', ['İ'] = 'I', ['ö'] = 'O', ['Ö'] = 'O', ['ş'] = 'S', ['Ş'] = 'S', ['ü'] = 'U', ['Ü'] = 'U' };
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            char c = map.TryGetValue(ch, out var m) ? m : char.ToUpperInvariant(ch);
            sb.Append((c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9') || c is '$' or '-' or '_' ? c : '_');
        }
        string s = sb.Length == 0 ? "0" : sb.ToString();
        if (s.Length > 31) s = s[..31];
        var set = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);
        string res = s;
        for (int i = 2; set.Contains(res); i++)
        {
            string suf = "_" + i.ToString(Inv);
            res = (s.Length + suf.Length > 31 ? s[..(31 - suf.Length)] : s) + suf;
        }
        return res;
    }
}
