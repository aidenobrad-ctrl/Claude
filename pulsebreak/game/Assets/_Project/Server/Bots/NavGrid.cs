using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Match;

namespace Pulsebreak.Server.Bots
{
    /// <summary>
    /// Where a bot can walk, worked out from the same block description the level's collision
    /// is built from. The map is cut into half-metre cells; each cell has the height of the
    /// surface a player would stand on there, or is blocked if a player could not stand there
    /// (inside a wall, or too close to one for their body to fit).
    ///
    /// Moving between neighbouring cells is a walk when the step is small, a climb (jump and
    /// mantle) when it is up to a ledge a player can pull themselves onto, and a drop when it
    /// is down. Paths are found with A* and then straightened wherever a straight walk works.
    /// </summary>
    public sealed class NavGrid
    {
        public const float CellSize = 0.5f;
        /// <summary>A step the motor takes without jumping.</summary>
        public const float StepHeight = 0.35f;
        /// <summary>The highest ledge a bot pulls itself onto: a little under the motor's mantle limit.</summary>
        public const float ClimbHeight = 1.45f;

        const float AgentRadius = 0.4f;
        const float AgentHeight = 1.8f;
        /// <summary>Tops higher than this are walls, never floors.</summary>
        const float HighestFloor = 2.5f;
        const float LongestDrop = 4.5f;
        const float Blocked = float.NegativeInfinity;

        readonly float _minX, _minZ;
        readonly int _width, _depth;
        readonly float[] _height;

        // A* working state, reused between searches.
        readonly float[] _cost;
        readonly int[] _from;
        readonly int[] _seen;
        readonly int[] _closed;
        readonly List<int> _heap = new List<int>(256);
        readonly List<int> _cells = new List<int>(128);
        int _search;

        NavGrid(float minX, float minZ, int width, int depth)
        {
            _minX = minX;
            _minZ = minZ;
            _width = width;
            _depth = depth;
            int count = width * depth;
            _height = new float[count];
            _cost = new float[count];
            _from = new int[count];
            _seen = new int[count];
            _closed = new int[count];
        }

        public int Width => _width;
        public int Depth => _depth;

        public static NavGrid Build(ArenaLayout arena)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (ArenaBox box in arena.Boxes)
            {
                minX = Mathf.Min(minX, box.Center.x - box.Size.x * 0.5f);
                maxX = Mathf.Max(maxX, box.Center.x + box.Size.x * 0.5f);
                minZ = Mathf.Min(minZ, box.Center.z - box.Size.z * 0.5f);
                maxZ = Mathf.Max(maxZ, box.Center.z + box.Size.z * 0.5f);
            }

            int width = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / CellSize));
            int depth = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / CellSize));
            var grid = new NavGrid(minX, minZ, width, depth);

            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    float cx = minX + (x + 0.5f) * CellSize, cz = minZ + (z + 0.5f) * CellSize;
                    grid._height[z * width + x] = SurfaceAt(arena.Boxes, cx, cz);
                }
            }
            return grid;
        }

        /// <summary>The floor a player standing here would be on, or blocked if they would not fit.</summary>
        static float SurfaceAt(ArenaBox[] boxes, float x, float z)
        {
            float surface = Blocked;
            foreach (ArenaBox box in boxes)
            {
                if (!Inside(box, x, z, 0f)) continue;
                float top = box.Center.y + box.Size.y * 0.5f;
                if (top <= HighestFloor && top > surface) surface = top;
            }
            if (float.IsNegativeInfinity(surface)) return Blocked;

            // Room for a body: nothing solid within a body's width, between the knees and the top
            // of the head. Anything lower is a step the body walks straight up.
            foreach (ArenaBox box in boxes)
            {
                if (!Inside(box, x, z, AgentRadius)) continue;
                float bottom = box.Center.y - box.Size.y * 0.5f, top = box.Center.y + box.Size.y * 0.5f;
                if (top > surface + StepHeight && bottom < surface + AgentHeight) return Blocked;
            }
            return surface;
        }

        static bool Inside(in ArenaBox box, float x, float z, float margin) =>
            Mathf.Abs(x - box.Center.x) <= box.Size.x * 0.5f + margin &&
            Mathf.Abs(z - box.Center.z) <= box.Size.z * 0.5f + margin;

        // ------------------------------------------------------------------ cells

        int CellX(float x) => Mathf.FloorToInt((x - _minX) / CellSize);
        int CellZ(float z) => Mathf.FloorToInt((z - _minZ) / CellSize);
        bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < _width && z < _depth;
        bool Open(int cell) => !float.IsNegativeInfinity(_height[cell]);

        Vector3 CellPoint(int cell) =>
            new Vector3(_minX + (cell % _width + 0.5f) * CellSize, _height[cell], _minZ + (cell / _width + 0.5f) * CellSize);

        /// <summary>True if a player could stand here; <paramref name="height"/> is the floor's height.</summary>
        public bool TryGetHeight(Vector3 position, out float height)
        {
            int x = CellX(position.x), z = CellZ(position.z);
            height = 0f;
            if (!InGrid(x, z) || !Open(z * _width + x)) return false;
            height = _height[z * _width + x];
            return true;
        }

        /// <summary>The open cell nearest a point, preferring one at about the same height. -1 if none is near.</summary>
        int NearestOpen(Vector3 position)
        {
            int cx = CellX(position.x), cz = CellZ(position.z);
            int best = -1;
            float bestScore = float.MaxValue;
            for (int ring = 0; ring <= 6 && best < 0; ring++)
            {
                for (int dz = -ring; dz <= ring; dz++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                        int x = cx + dx, z = cz + dz;
                        if (!InGrid(x, z)) continue;
                        int cell = z * _width + x;
                        if (!Open(cell)) continue;
                        float score = dx * dx + dz * dz + Mathf.Abs(_height[cell] - position.y) * 8f;
                        if (score >= bestScore) continue;
                        bestScore = score;
                        best = cell;
                    }
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ paths

        /// <summary>
        /// A path from one point to another as points to walk through, the first being the
        /// first one to head for. False if there is no way there.
        /// </summary>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> path)
        {
            path.Clear();
            int start = NearestOpen(from), goal = NearestOpen(to);
            if (start < 0 || goal < 0) return false;
            if (start == goal)
            {
                path.Add(CellPoint(goal));
                return true;
            }

            _search++;
            _heap.Clear();
            _keys.Clear();
            Visit(start, 0f, -1, goal);

            bool found = false;
            int expanded = 0;
            while (_heap.Count > 0 && expanded < _width * _depth)
            {
                int cell = Pop();
                if (_closed[cell] == _search) continue;
                _closed[cell] = _search;
                expanded++;
                if (cell == goal)
                {
                    found = true;
                    break;
                }

                int x = cell % _width, z = cell / _width;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = x + dx, nz = z + dz;
                        if (!InGrid(nx, nz)) continue;
                        int next = nz * _width + nx;
                        if (_closed[next] == _search || !Open(next)) continue;

                        float rise = _height[next] - _height[cell];
                        if (rise > ClimbHeight || rise < -LongestDrop) continue;
                        if (dx != 0 && dz != 0)
                        {
                            // No cutting corners: both cells beside the diagonal must be open and level with this one.
                            int side1 = z * _width + nx, side2 = nz * _width + x;
                            if (!Open(side1) || !Open(side2)) continue;
                            if (Mathf.Abs(_height[side1] - _height[cell]) > StepHeight || Mathf.Abs(_height[side2] - _height[cell]) > StepHeight) continue;
                        }

                        float step = (dx != 0 && dz != 0 ? 1.4142f : 1f) * CellSize;
                        if (rise > StepHeight) step += 1.2f;       // climbing is slow
                        else if (rise < -StepHeight) step += 0.3f;
                        Visit(next, _cost[cell] + step, cell, goal);
                    }
                }
            }
            if (!found) return false;

            _cells.Clear();
            for (int cell = goal; cell >= 0; cell = _from[cell]) _cells.Add(cell);
            _cells.Reverse();
            Straighten(from);
            return path.Count > 0;

            void Straighten(Vector3 origin)
            {
                // Walk from the start, each time jumping to the furthest cell reachable in a straight, level walk.
                Vector3 at = origin;
                int index = 0;
                while (index < _cells.Count - 1)
                {
                    int furthest = index + 1;
                    for (int j = Mathf.Min(_cells.Count - 1, index + 24); j > index + 1; j--)
                    {
                        if (!StraightWalk(at, CellPoint(_cells[j]))) continue;
                        furthest = j;
                        break;
                    }
                    Vector3 point = CellPoint(_cells[furthest]);
                    path.Add(point);
                    at = point;
                    index = furthest;
                }
                if (path.Count == 0) path.Add(CellPoint(_cells[_cells.Count - 1]));
            }
        }

        /// <summary>True if a player can walk straight from one point to another without climbing.</summary>
        public bool StraightWalk(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float length = delta.magnitude;
            int samples = Mathf.Max(1, Mathf.CeilToInt(length / (CellSize * 0.5f)));
            float previous = float.NaN;
            for (int i = 0; i <= samples; i++)
            {
                Vector3 point = from + delta * (i / (float)samples);
                if (!TryGetHeight(point, out float height)) return false;
                if (!float.IsNaN(previous) && Mathf.Abs(height - previous) > StepHeight) return false;
                previous = height;
            }
            return true;
        }

        void Visit(int cell, float cost, int from, int goal)
        {
            if (_seen[cell] == _search && cost >= _cost[cell]) return;
            _seen[cell] = _search;
            _cost[cell] = cost;
            _from[cell] = from;
            Push(cell, goal);
        }

        float Priority(int cell, int goal)
        {
            // Octile distance: exact on an empty grid with diagonal moves.
            int dx = Mathf.Abs(cell % _width - goal % _width), dz = Mathf.Abs(cell / _width - goal / _width);
            float estimate = (Mathf.Max(dx, dz) + 0.4142f * Mathf.Min(dx, dz)) * CellSize;
            return _cost[cell] + estimate;
        }

        // A plain binary heap of cells ordered by cost plus estimate. A cell can be in it more
        // than once; the stale copies are skipped when they come out.
        readonly List<float> _keys = new List<float>(256);

        void Push(int cell, int goal)
        {
            _heap.Add(cell);
            _keys.Add(Priority(cell, goal));
            int i = _heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_keys[parent] <= _keys[i]) break;
                Swap(i, parent);
                i = parent;
            }
        }

        int Pop()
        {
            int top = _heap[0];
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _keys[0] = _keys[last];
            _heap.RemoveAt(last);
            _keys.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int left = i * 2 + 1, right = left + 1, smallest = i;
                if (left < _heap.Count && _keys[left] < _keys[smallest]) smallest = left;
                if (right < _heap.Count && _keys[right] < _keys[smallest]) smallest = right;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
            return top;
        }

        void Swap(int a, int b)
        {
            int cell = _heap[a];
            _heap[a] = _heap[b];
            _heap[b] = cell;
            float key = _keys[a];
            _keys[a] = _keys[b];
            _keys[b] = key;
        }
    }
}
