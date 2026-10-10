// The island as exported from the web version (see Data/README.md): terrain
// grids, roads, guardrails, trees, colliders and layout. Pure C#: Unity code
// hands in the file bytes (from Resources), tests read them from disk.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Halcyon.Core
{
    public sealed class RoadNode
    {
        public string id;
        public double x, y, z;
        public readonly List<int> edges = new List<int>();
    }

    public enum CenterLine { None = 0, Dashed = 1, Double = 2, Solid = 3 }

    public sealed class RoadClassInfo
    {
        public string id;
        /// <summary>Half the paved width, m.</summary>
        public double halfWidth;
        /// <summary>Strip beyond the paved edge: gravel shoulder, or a raised sidewalk.</summary>
        public double shoulder;
        public double maxGrade;
        /// <summary>Speed limit for ambient traffic, m/s.</summary>
        public double traffic;
        public int surface;
        public int lanes;
        public CenterLine center;
        public bool edgeLines;
        public bool sidewalk;
    }

    public sealed class RoadEdge
    {
        public int id;
        public int cls;
        public RoadClassInfo info;
        public int a, b;
        public int n;
        public double length;
        /// <summary>Samples ~4 m apart: position, unit tangent, arc length, base terrain height.</summary>
        public double[] x, y, z, tx, tz, s, baseY;
        /// <summary>1 where the road runs on a bridge deck.</summary>
        public byte[] bridge;
        /// <summary>Per sample: guardrail offset on the left (index 2i) and right (2i + 1), 0 if none.</summary>
        public float[] railOffsets;
    }

    public sealed class RailRun
    {
        public int edge;
        /// <summary>+1 right of the edge direction, -1 left.</summary>
        public int side;
        /// <summary>First and last road sample covered.</summary>
        public int i0, i1;
        /// <summary>Lateral distance of the rail from the centreline, m.</summary>
        public double offset;
    }

    public sealed class ProvingCircuit
    {
        public double originX, originZ, height, length, startS;
        public RoadLine road;
        /// <summary>Curbs and run-off per sample: 0 none, 1 curb, 2 curb + gravel trap.</summary>
        public byte[] cornerKind;
        /// <summary>Which side the gravel trap is on (+1 right, -1 left).</summary>
        public sbyte[] outside;
        public double halfWidth, curbWidth, gravelWidth;
        public double skidX, skidZ, skidR, skidWidth;
        public double paddockX0, paddockZ0, paddockX1, paddockZ1;
        public double spawnX, spawnZ, spawnYaw;
    }

    public sealed class BuildingInfo
    {
        public double x, z, fx, fz, w, d, h, y0, floor, seed;
        public int kind, roof;
    }

    public sealed class FestivalPiece
    {
        public double x, z, fx, fz, y, w, d, h;
        /// <summary>0xRRGGBB.</summary>
        public int color;
    }

    public sealed class FestivalLayout
    {
        public FestivalPiece stage, wheel, arch;
        public FestivalPiece[] tents, poles;
        public int[] colors;
    }

    public struct Spawn
    {
        public double x, y, z, yaw;
    }

    public sealed class Poi
    {
        public string name, kind;
        public double x, y, z;
    }

    /// <summary>All trees of one 64 m tile, in placement order (the order defines tree ids).</summary>
    public sealed class TreeTile
    {
        public int count;
        public float[] x, y, z, scale, yaw;
        public byte[] kind, rank;
    }

    public static class TreeKind
    {
        public const int Broadleaf = 0, Conifer = 1, Palm = 2, Cypress = 3, Bush = 4, Dead = 5, Count = 6;
        /// <summary>Trunk collision radius per kind at scale 1, m (bushes break).</summary>
        public static readonly double[] Trunk = { 0.38, 0.32, 0.24, 0.26, 0.9, 0.22 };
        public static readonly string[] Names = { "broadleaf", "conifer", "palm", "cypress", "bush", "dead" };
    }

    public sealed class WorldData
    {
        public const string ResourceFolder = "HalcyonIsland";
        public static readonly string[] Files = { "meta.json", "height.bytes", "ground.bytes", "splat.bytes", "roads.bytes", "trees.bytes", "colliders.bytes" };

        public Dictionary<string, object> Meta;
        public int Seed;
        public double WorldHalf;

        // --- Grids (n x n vertices, row-major by z then x) --------------------
        public int N;
        public double Spacing, Origin, HMin, HMax;
        public float[] Height;
        public byte[] SurfaceGrid;
        /// <summary>Water surface height in cm, short.MinValue where there is none.</summary>
        public short[] Water;
        public byte[] Region, Forest, Desert, RoadDist;
        public byte[] SplatRock, SplatDirt, SplatSand, SplatSnow;

        // --- Roads ---------------------------------------------------------------
        public RoadNode[] Nodes;
        public RoadClassInfo[] Classes;
        public RoadEdge[] Edges;
        public RailRun[] Rails;
        public ProvingCircuit Proving;
        public double CurbHeight = 0.12, ShoulderMax = 4.2, ParapetWidth = 0.32;

        // --- Trees -----------------------------------------------------------------
        public int TreeTilesX, TreeTilesZ;
        public double TreeTile = 64, TreeOrigin;
        public TreeTile[] TreeTiles;
        public int TreeCount;

        // --- Colliders and layout ---------------------------------------------------
        public Segment[] Segments;
        public Circle[] Circles;
        public BuildingInfo[] Buildings;
        public FestivalLayout Festival;
        public Spawn Spawn;
        public Poi[] Pois;

        /// <summary>Load everything; read(name) returns a file's bytes ("meta.json", "height.bytes", ...).</summary>
        public static WorldData Load(Func<string, byte[]> read)
        {
            var w = new WorldData();
            w.ReadMeta(Encoding.UTF8.GetString(read("meta.json")));
            w.ReadHeight(Unpack(read("height.bytes"), "HLHT"));
            w.ReadGround(Unpack(read("ground.bytes"), "HLGR"));
            w.ReadSplat(Unpack(read("splat.bytes"), "HLSP"));
            w.ReadRoads(Unpack(read("roads.bytes"), "HLRD"));
            w.ReadTrees(Unpack(read("trees.bytes"), "HLTR"));
            w.ReadColliders(Unpack(read("colliders.bytes"), "HLCL"));
            return w;
        }

        public static WorldData LoadFromDirectory(string dir) => Load(name => File.ReadAllBytes(Path.Combine(dir, name)));

        /// <summary>Check the 16-byte header and inflate the payload.</summary>
        public static byte[] Unpack(byte[] file, string magic)
        {
            if (file.Length < 16 || Encoding.ASCII.GetString(file, 0, 4) != magic)
                throw new InvalidDataException($"not a {magic} file");
            uint version = BitConverter.ToUInt32(file, 4);
            if (version != 1) throw new InvalidDataException($"{magic}: unsupported version {version}");
            int len = (int)BitConverter.ToUInt32(file, 8);
            var outBuf = new byte[len];
            using (var ms = new MemoryStream(file, 16, file.Length - 16))
            using (var z = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int got = 0;
                while (got < len)
                {
                    int r = z.Read(outBuf, got, len - got);
                    if (r <= 0) throw new InvalidDataException($"{magic}: truncated payload");
                    got += r;
                }
            }
            return outBuf;
        }

        // --- Readers --------------------------------------------------------------
        sealed class Reader
        {
            readonly byte[] b;
            public int p;
            public Reader(byte[] b) { this.b = b; }
            public byte U8() => b[p++];
            public sbyte I8() => (sbyte)b[p++];
            public ushort U16() { ushort v = BitConverter.ToUInt16(b, p); p += 2; return v; }
            public int I32() { int v = BitConverter.ToInt32(b, p); p += 4; return v; }
            public float F32() { float v = BitConverter.ToSingle(b, p); p += 4; return v; }
            public string Str() { int n = U8(); string s = Encoding.UTF8.GetString(b, p, n); p += n; return s; }
            public byte[] Bytes(int n) { var r = new byte[n]; Buffer.BlockCopy(b, p, r, 0, n); p += n; return r; }
            public double[] F32s(int n) { var r = new double[n]; for (int i = 0; i < n; i++) r[i] = F32(); return r; }

            /// <summary>n 16-bit values stored as a low-byte plane then a high-byte plane.</summary>
            public ushort[] Planes16(int n)
            {
                var r = new ushort[n];
                for (int i = 0; i < n; i++) r[i] = (ushort)(b[p + i] | (b[p + n + i] << 8));
                p += 2 * n;
                return r;
            }
        }

        void ReadMeta(string json)
        {
            Meta = Json.Obj(MiniJson.Parse(json));
            Seed = Meta.Int("seed");
            WorldHalf = Meta.Num("worldHalf");
            var road = Meta.Obj("road");
            CurbHeight = road.Num("curbHeight");
            ShoulderMax = road.Num("shoulderMax");
            ParapetWidth = road.Num("parapetWidth");
            var sp = Meta.Obj("spawn");
            Spawn = new Spawn { x = sp.Num("x"), y = sp.Num("y"), z = sp.Num("z"), yaw = sp.Num("yaw") };
            var pois = new List<Poi>();
            foreach (var o in Meta.Arr("pois"))
            {
                var d = Json.Obj(o);
                pois.Add(new Poi { name = d.Str("name"), kind = d.Str("kind"), x = d.Num("x"), y = d.Num("y"), z = d.Num("z") });
            }
            Pois = pois.ToArray();
            var bl = Meta.Arr("buildings");
            Buildings = new BuildingInfo[bl.Count];
            for (int i = 0; i < bl.Count; i++)
            {
                var a = Json.Arr(bl[i]);
                Buildings[i] = new BuildingInfo
                {
                    x = Json.Num(a[0]), z = Json.Num(a[1]), fx = Json.Num(a[2]), fz = Json.Num(a[3]),
                    w = Json.Num(a[4]), d = Json.Num(a[5]), h = Json.Num(a[6]), y0 = Json.Num(a[7]), floor = Json.Num(a[8]),
                    kind = (int)Json.Num(a[9]), roof = (int)Json.Num(a[10]), seed = Json.Num(a[11]),
                };
            }
            var f = Meta.Obj("festival");
            FestivalPiece Piece(Dictionary<string, object> d) => new FestivalPiece
            {
                x = d.Num("x"), z = d.Num("z"), fx = d.Num("fx"), fz = d.Num("fz"), y = d.Num("y"),
                w = d.Num("w"), d = d.Num("d"), h = d.Num("h"), color = (int)d.Num("color"),
            };
            FestivalPiece[] Pieces(List<object> l)
            {
                var r = new FestivalPiece[l.Count];
                for (int i = 0; i < l.Count; i++) r[i] = Piece(Json.Obj(l[i]));
                return r;
            }
            var colors = f.Arr("colors");
            Festival = new FestivalLayout
            {
                stage = Piece(f.Obj("stage")),
                wheel = Piece(f.Obj("wheel")),
                arch = Piece(f.Obj("arch")),
                tents = Pieces(f.Arr("tents")),
                poles = Pieces(f.Arr("poles")),
                colors = new int[colors.Count],
            };
            for (int i = 0; i < colors.Count; i++) Festival.colors[i] = (int)Json.Num(colors[i]);
        }

        void ReadHeight(byte[] payload)
        {
            var r = new Reader(payload);
            N = r.I32();
            Spacing = r.F32();
            Origin = r.F32();
            HMin = r.F32();
            HMax = r.F32();
            int nn = N * N;
            ushort[] q = r.Planes16(nn);
            Height = new float[nn];
            double scale = (HMax - HMin) / 65535.0;
            for (int j = 0; j < N; j++)
            {
                int prev = 0;
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    int v = (prev + q[k]) & 0xffff;
                    prev = v;
                    Height[k] = (float)(HMin + v * scale);
                }
            }
        }

        void ReadGround(byte[] payload)
        {
            var r = new Reader(payload);
            int n = r.I32();
            if (n != N) throw new InvalidDataException("ground grid size differs from heights");
            int nn = n * n;
            SurfaceGrid = r.Bytes(nn);
            ushort[] w = r.Planes16(nn);
            Water = new short[nn];
            for (int i = 0; i < nn; i++) Water[i] = unchecked((short)w[i]);
            Region = r.Bytes(nn);
            Forest = r.Bytes(nn);
            Desert = r.Bytes(nn);
            RoadDist = r.Bytes(nn);
        }

        void ReadSplat(byte[] payload)
        {
            var r = new Reader(payload);
            int n = r.I32();
            if (n != N) throw new InvalidDataException("splat grid size differs from heights");
            int nn = n * n;
            SplatRock = r.Bytes(nn);
            SplatDirt = r.Bytes(nn);
            SplatSand = r.Bytes(nn);
            SplatSnow = r.Bytes(nn);
        }

        void ReadRoads(byte[] payload)
        {
            var r = new Reader(payload);
            int nodeCount = r.I32();
            Nodes = new RoadNode[nodeCount];
            for (int i = 0; i < nodeCount; i++) Nodes[i] = new RoadNode { id = r.Str(), x = r.F32(), y = r.F32(), z = r.F32() };
            int classCount = r.I32();
            Classes = new RoadClassInfo[classCount];
            for (int i = 0; i < classCount; i++)
            {
                Classes[i] = new RoadClassInfo
                {
                    id = r.Str(), halfWidth = r.F32(), shoulder = r.F32(), maxGrade = r.F32(), traffic = r.F32(),
                    surface = r.U8(), lanes = r.U8(), center = (CenterLine)r.U8(), edgeLines = r.U8() != 0, sidewalk = r.U8() != 0,
                };
            }
            int edgeCount = r.I32();
            Edges = new RoadEdge[edgeCount];
            for (int e = 0; e < edgeCount; e++)
            {
                var ed = new RoadEdge { id = e, cls = r.U8(), a = r.U16(), b = r.U16(), n = r.I32(), length = r.F32() };
                ed.info = Classes[ed.cls];
                int n = ed.n;
                ed.x = r.F32s(n);
                ed.y = r.F32s(n);
                ed.z = r.F32s(n);
                ed.tx = r.F32s(n);
                ed.tz = r.F32s(n);
                ed.s = r.F32s(n);
                ed.baseY = r.F32s(n);
                ed.bridge = r.Bytes(n);
                ed.railOffsets = new float[n * 2];
                Edges[e] = ed;
                Nodes[ed.a].edges.Add(e);
                Nodes[ed.b].edges.Add(e);
            }
            int railCount = r.I32();
            Rails = new RailRun[railCount];
            for (int i = 0; i < railCount; i++)
            {
                var run = new RailRun { edge = r.U16(), side = r.I8(), i0 = r.I32(), i1 = r.I32(), offset = r.F32() };
                Rails[i] = run;
                var offs = Edges[run.edge].railOffsets;
                int k = run.side > 0 ? 1 : 0;
                for (int s = run.i0; s <= run.i1; s++) offs[s * 2 + k] = (float)run.offset;
            }
            // The proving ground's circuit.
            var pc = new ProvingCircuit { originX = r.F32(), originZ = r.F32(), height = r.F32() };
            int cn = r.I32();
            pc.length = r.F32();
            pc.startS = r.F32();
            double[] cx = r.F32s(cn), cz = r.F32s(cn), ctx = r.F32s(cn), ctz = r.F32s(cn), cs = r.F32s(cn);
            pc.cornerKind = r.Bytes(cn);
            pc.outside = new sbyte[cn];
            for (int i = 0; i < cn; i++) pc.outside[i] = r.I8();
            var cy = new double[cn];
            for (int i = 0; i < cn; i++) cy[i] = pc.height;
            var pm = Meta.Obj("proving");
            pc.halfWidth = pm.Num("halfWidth");
            pc.road = RoadLine.FromSamples(cx, cy, cz, ctx, ctz, cs, pc.length, closed: true, halfWidth: pc.halfWidth);
            pc.curbWidth = pm.Num("curbWidth");
            pc.gravelWidth = pm.Num("gravelWidth");
            var sk = pm.Obj("skidpad");
            pc.skidX = sk.Num("x");
            pc.skidZ = sk.Num("z");
            pc.skidR = sk.Num("r");
            pc.skidWidth = sk.Num("width");
            var pd = pm.Obj("paddock");
            pc.paddockX0 = pd.Num("x0");
            pc.paddockZ0 = pd.Num("z0");
            pc.paddockX1 = pd.Num("x1");
            pc.paddockZ1 = pd.Num("z1");
            var ps = pm.Obj("spawn");
            pc.spawnX = ps.Num("x");
            pc.spawnZ = ps.Num("z");
            pc.spawnYaw = ps.Num("yaw");
            Proving = pc;
        }

        void ReadTrees(byte[] payload)
        {
            var r = new Reader(payload);
            TreeTilesX = r.I32();
            TreeTilesZ = r.I32();
            TreeTile = r.F32();
            TreeOrigin = r.F32();
            double hmin = r.F32(), hmax = r.F32();
            double hs = (hmax - hmin) / 65535.0;
            TreeTiles = new TreeTile[TreeTilesX * TreeTilesZ];
            TreeCount = 0;
            for (int tz = 0; tz < TreeTilesZ; tz++)
            {
                for (int tx = 0; tx < TreeTilesX; tx++)
                {
                    int n = r.U16();
                    var t = new TreeTile
                    {
                        count = n,
                        x = new float[n], y = new float[n], z = new float[n], scale = new float[n], yaw = new float[n],
                        kind = new byte[n], rank = new byte[n],
                    };
                    double x0 = TreeOrigin + tx * TreeTile;
                    double z0 = TreeOrigin + tz * TreeTile;
                    for (int m = 0; m < n; m++)
                    {
                        t.x[m] = (float)(x0 + r.U16() / 65535.0 * TreeTile);
                        t.z[m] = (float)(z0 + r.U16() / 65535.0 * TreeTile);
                        t.y[m] = (float)(hmin + r.U16() * hs);
                        t.scale[m] = (float)(0.6 + 0.8 * r.U8() / 255.0);
                        t.yaw[m] = (float)(r.U8() / 256.0 * Math.PI * 2);
                        t.kind[m] = r.U8();
                        t.rank[m] = r.U8();
                    }
                    TreeTiles[tz * TreeTilesX + tx] = t;
                    TreeCount += n;
                }
            }
        }

        void ReadColliders(byte[] payload)
        {
            var r = new Reader(payload);
            int ns = r.I32();
            Segments = new Segment[ns];
            for (int i = 0; i < ns; i++)
            {
                Segments[i] = new Segment
                {
                    ax = r.F32(), az = r.F32(), bx = r.F32(), bz = r.F32(), top = r.F32(), bottom = r.F32(),
                    bounce = r.F32(), friction = r.F32(), kind = (SegmentKind)r.U8(),
                };
            }
            int nc = r.I32();
            Circles = new Circle[nc];
            for (int i = 0; i < nc; i++)
            {
                Circles[i] = new Circle
                {
                    x = r.F32(), z = r.F32(), r = r.F32(), top = r.F32(), bottom = r.F32(),
                    kind = (CircleKind)r.U8(), breakable = r.U8() != 0,
                };
            }
        }

        // --- Grid helpers -------------------------------------------------------------
        public double HeightAt(int i, int j) => Height[j * N + i];

        /// <summary>Terrain height at (x, z), interpolated like the physics mesh (two triangles per cell).</summary>
        public double TerrainHeight(double x, double z)
        {
            double lx = (x - Origin) / Spacing;
            double lz = (z - Origin) / Spacing;
            int i = (int)Math.Floor(lx);
            int j = (int)Math.Floor(lz);
            if (i < 0) i = 0;
            if (j < 0) j = 0;
            if (i > N - 2) i = N - 2;
            if (j > N - 2) j = N - 2;
            double u = lx - i;
            double v = lz - j;
            int k = j * N + i;
            double h00 = Height[k], h10 = Height[k + 1], h01 = Height[k + N], h11 = Height[k + N + 1];
            return u >= v ? h00 + (h10 - h00) * u + (h11 - h10) * v : h00 + (h01 - h00) * v + (h11 - h01) * u;
        }

        // --- Unity frame ------------------------------------------------------------------
        /// <summary>Position in Unity's left-handed frame: z negated.</summary>
        public static void ToUnity(double x, double y, double z, out float ux, out float uy, out float uz)
        {
            ux = (float)x;
            uy = (float)y;
            uz = (float)-z;
        }

        /// <summary>Rotation in Unity's left-handed frame: (x, y, z, w) -> (-x, -y, z, w).</summary>
        public static void ToUnity(Quat q, out float qx, out float qy, out float qz, out float qw)
        {
            qx = (float)-q.x;
            qy = (float)-q.y;
            qz = (float)q.z;
            qw = (float)q.w;
        }
    }
}
