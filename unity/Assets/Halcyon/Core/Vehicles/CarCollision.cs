// Car collisions: each car is an oriented box in the ground plane with a
// vertical extent. Contacts come from the separating axis test; response is
// an impulse at the contact point (restitution plus Coulomb friction) and a
// positional correction so bodies never stay interpenetrated. Ported from
// the web version's src/vehicles/collision.ts (collideWorld, collideCars).
//
// Named CarCollision so it does not clash with UnityEngine.Collision in
// scripts that use both namespaces. Allocation-free. Like the TypeScript
// module it keeps its scratch in statics: call it from one thread, and do not
// call CollideWorld again from inside an onBreak callback.
using System;

namespace Halcyon.Core
{
    /// <summary>What a car hit: a segment's kind (wall .. fence) or a circle's kind (post .. building).</summary>
    public enum CollisionKind { Wall, Barrier, Tires, Rail, Fence, Post, Tree, Rock, Pier, Building }

    public sealed class CollisionContact
    {
        /// <summary>Unit normal pointing from the obstacle (or car B) toward car A.</summary>
        public double nx;
        public double nz;
        public double depth;
        /// <summary>Contact point (world, ground plane).</summary>
        public double px;
        public double pz;
    }

    /// <summary>The strongest impact of a CollideWorld call.</summary>
    public struct CollisionEvent
    {
        /// <summary>Normal impulse, N·s.</summary>
        public double impulse;
        public double x;
        public double z;
        public CollisionKind kind;
    }

    public static class CarCollision
    {
        sealed class Box
        {
            public double cx, cz;
            /// <summary>Unit axes: forward (fx, fz) and right (rx, rz).</summary>
            public double fx, fz = -1, rx = 1, rz;
            public double hl = 2, hw = 1;
            public double bottom, top = 1;
        }

        static readonly Box boxA = new Box();
        static readonly Box boxB = new Box();
        static readonly CollisionContact contact = new CollisionContact();
        static readonly V3 tmp = new V3();
        static readonly V3 tmp2 = new V3();
        static readonly V3 pt = new V3();
        static readonly V3 r1 = new V3();
        static readonly V3 r2 = new V3();
        // collideCars allocates these three in the TypeScript (clone(), new V3).
        static readonly V3 nScratch = new V3();
        static readonly V3 impScratch = new V3();
        static readonly V3 tScratch = new V3();
        static readonly double[] axes = new double[8];

        // State of the collideWorld closures, and the cached callbacks.
        static Vehicle qVehicle;
        static Action<Circle> qOnBreak;
        static double qHeight;
        static bool qHasWorst;
        static CollisionEvent qWorst;
        static readonly Action<Segment> onSegment = OnSegment;
        static readonly Action<Circle> onCircle = OnCircle;

        static CollisionKind Kind(SegmentKind k) => (CollisionKind)(int)k;
        static CollisionKind Kind(CircleKind k) => (CollisionKind)((int)CollisionKind.Post + (int)k);

        static Box CarBox(Vehicle v, Box o)
        {
            VehicleParams p = v.Params;
            v.ToWorld(p.boxCenter, tmp);
            o.cx = tmp.x;
            o.cz = tmp.z;
            // Project the body axes onto the ground plane.
            double fx = v.fwd.x;
            double fz = v.fwd.z;
            double l = Math.Sqrt(fx * fx + fz * fz);
            if (l == 0 || double.IsNaN(l)) l = 1; // `|| 1`
            fx /= l;
            fz /= l;
            o.fx = fx;
            o.fz = fz;
            o.rx = -fz;
            o.rz = fx;
            o.hl = p.half.z * 0.98;
            o.hw = p.half.x * 0.96;
            o.bottom = tmp.y - p.half.y;
            o.top = tmp.y + p.half.y;
            return o;
        }

        static void Corner(Box b, int i, out double x, out double z)
        {
            int sl = (i & 1) != 0 ? 1 : -1;
            int sw = (i & 2) != 0 ? 1 : -1;
            x = b.cx + b.fx * b.hl * sl + b.rx * b.hw * sw;
            z = b.cz + b.fz * b.hl * sl + b.rz * b.hw * sw;
        }

        /// <summary>Box vs segment. Returns the contact with the normal pushing the box out.</summary>
        static bool BoxSegment(Box b, Segment s, CollisionContact o)
        {
            // Closest point on the segment to the box center, then test along the
            // segment normal and the box axes (SAT on 3 axes, segment as a thin box).
            double dx = s.bx - s.ax;
            double dz = s.bz - s.az;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) return false;
            double ux = dx / len;
            double uz = dz / len;
            double nx = -uz;
            double nz = ux;
            // Normal toward the box center.
            double side = (b.cx - s.ax) * nx + (b.cz - s.az) * nz;
            if (side < 0)
            {
                nx = -nx;
                nz = -nz;
            }
            // Box projection radius on the normal.
            double rN = b.hl * Math.Abs(b.fx * nx + b.fz * nz) + b.hw * Math.Abs(b.rx * nx + b.rz * nz);
            double dist = Math.Abs(side);
            if (dist >= rN) return false;
            // Overlap along the segment direction: the box extent must reach the segment span.
            double along = (b.cx - s.ax) * ux + (b.cz - s.az) * uz;
            double rU = b.hl * Math.Abs(b.fx * ux + b.fz * uz) + b.hw * Math.Abs(b.rx * ux + b.rz * uz);
            if (along + rU < 0 || along - rU > len) return false;
            // Deepest box corner that lies within the segment span is the contact point.
            double best = double.PositiveInfinity;
            double px = 0;
            double pz = 0;
            for (int i = 0; i < 4; i++)
            {
                Corner(b, i, out double cx, out double cz);
                double a = (cx - s.ax) * ux + (cz - s.az) * uz;
                if (a < -0.3 || a > len + 0.3) continue;
                double d = (cx - s.ax) * nx + (cz - s.az) * nz;
                if (d < best)
                {
                    best = d;
                    px = cx;
                    pz = cz;
                }
            }
            if (best == double.PositiveInfinity)
            {
                // Segment end poking into the box side: use the nearer endpoint.
                double ex = along < len / 2 ? s.ax : s.bx;
                double ez = along < len / 2 ? s.az : s.bz;
                return BoxPoint(b, ex, ez, 0, o);
            }
            if (best >= 0) return false;
            o.nx = nx;
            o.nz = nz;
            o.depth = -best;
            o.px = px;
            o.pz = pz;
            return true;
        }

        /// <summary>Box vs a circle of radius r at (x, z).</summary>
        static bool BoxPoint(Box b, double x, double z, double r, CollisionContact o)
        {
            double dx = x - b.cx;
            double dz = z - b.cz;
            double lf = dx * b.fx + dz * b.fz;
            double lr = dx * b.rx + dz * b.rz;
            // Closest point on the box to the circle center.
            double cf = JsMath.Max(-b.hl, JsMath.Min(b.hl, lf));
            double cr = JsMath.Max(-b.hw, JsMath.Min(b.hw, lr));
            bool inside = cf == lf && cr == lr;
            if (!inside)
            {
                double qx = b.cx + b.fx * cf + b.rx * cr;
                double qz = b.cz + b.fz * cf + b.rz * cr;
                double ex = qx - x;
                double ez = qz - z;
                double d = Math.Sqrt(ex * ex + ez * ez);
                if (d >= r || d < 1e-9) return false;
                o.nx = ex / d;
                o.nz = ez / d;
                o.depth = r - d;
                o.px = qx;
                o.pz = qz;
                return true;
            }
            // Center inside the box: push out along the shallowest box axis.
            double pf = b.hl - Math.Abs(lf);
            double pr = b.hw - Math.Abs(lr);
            if (pf < pr)
            {
                double sgn = lf > 0 ? -1 : 1;
                o.nx = b.fx * sgn;
                o.nz = b.fz * sgn;
                o.depth = pf + r;
            }
            else
            {
                double sgn = lr > 0 ? -1 : 1;
                o.nx = b.rx * sgn;
                o.nz = b.rz * sgn;
                o.depth = pr + r;
            }
            o.px = x;
            o.pz = z;
            return true;
        }

        /// <summary>Oriented box vs oriented box (2D SAT, 4 axes). Normal points from B to A.</summary>
        static bool BoxBox(Box a, Box b, CollisionContact o)
        {
            double[] ax = axes;
            ax[0] = a.fx; ax[1] = a.fz; ax[2] = a.rx; ax[3] = a.rz;
            ax[4] = b.fx; ax[5] = b.fz; ax[6] = b.rx; ax[7] = b.rz;
            double minDepth = double.PositiveInfinity;
            double mnx = 0;
            double mnz = 0;
            double dx = a.cx - b.cx;
            double dz = a.cz - b.cz;
            for (int k = 0; k < 4; k++)
            {
                double nx = ax[k * 2];
                double nz = ax[k * 2 + 1];
                double ra = a.hl * Math.Abs(a.fx * nx + a.fz * nz) + a.hw * Math.Abs(a.rx * nx + a.rz * nz);
                double rb = b.hl * Math.Abs(b.fx * nx + b.fz * nz) + b.hw * Math.Abs(b.rx * nx + b.rz * nz);
                double d = dx * nx + dz * nz;
                double overlap = ra + rb - Math.Abs(d);
                if (overlap <= 0) return false;
                if (overlap < minDepth)
                {
                    minDepth = overlap;
                    double sgn = d < 0 ? -1 : 1;
                    mnx = nx * sgn;
                    mnz = nz * sgn;
                }
            }
            // Contact point: the corner of either box that penetrates deepest.
            double bestPen = double.NegativeInfinity;
            double px = (a.cx + b.cx) / 2;
            double pz = (a.cz + b.cz) / 2;
            for (int i = 0; i < 4; i++)
            {
                Corner(a, i, out double cx, out double cz);
                double pen = -((cx - b.cx) * mnx + (cz - b.cz) * mnz) + (b.hl * Math.Abs(b.fx * mnx + b.fz * mnz) + b.hw * Math.Abs(b.rx * mnx + b.rz * mnz));
                if (pen > bestPen)
                {
                    bestPen = pen;
                    px = cx;
                    pz = cz;
                }
                Corner(b, i, out double dx2, out double dz2);
                double pen2 = (dx2 - a.cx) * mnx + (dz2 - a.cz) * mnz + (a.hl * Math.Abs(a.fx * mnx + a.fz * mnz) + a.hw * Math.Abs(a.rx * mnx + a.rz * mnz));
                if (pen2 > bestPen)
                {
                    bestPen = pen2;
                    px = dx2;
                    pz = dz2;
                }
            }
            o.nx = mnx;
            o.nz = mnz;
            o.depth = minDepth;
            o.px = px;
            o.pz = pz;
            return true;
        }

        /// <summary>Impulse response for car A against a static obstacle.</summary>
        static double ResolveStatic(Vehicle v, CollisionContact c, double bounce, double friction, double height)
        {
            pt.Set(c.px, height, c.pz);
            v.PointVelocity(pt, tmp);
            double vn = tmp.x * c.nx + tmp.z * c.nz;
            // Positional correction: move the car out of the obstacle.
            double push = JsMath.Max(0, c.depth - 0.005);
            v.pos.x += c.nx * push;
            v.pos.z += c.nz * push;
            if (vn >= 0) return 0;
            // Effective mass along the normal at the contact point.
            tmp2.Set(c.nx, 0, c.nz);
            double k = EffectiveInvMass(v, pt, tmp2);
            double jn = (-(1 + bounce) * vn) / k;
            // Friction impulse along the tangent, limited by the Coulomb cone.
            double tx = tmp.x - vn * c.nx;
            double tz = tmp.z - vn * c.nz;
            double tl = Math.Sqrt(tx * tx + tz * tz);
            tmp2.Set(c.nx * jn, 0, c.nz * jn);
            if (tl > 1e-4)
            {
                double ux = tx / tl;
                double uz = tz / tl;
                tmp.Set(ux, 0, uz);
                double kt = EffectiveInvMass(v, pt, tmp);
                double jt = JsMath.Min(tl / kt, friction * jn);
                tmp2.x -= ux * jt;
                tmp2.z -= uz * jt;
            }
            v.ApplyImpulse(pt, tmp2);
            v.impact = JsMath.Max(v.impact, jn);
            return jn;
        }

        static double EffectiveInvMass(Vehicle v, V3 point, V3 n)
        {
            r1.SubVectors(point, v.pos);
            r2.CrossVectors(r1, n);
            v.ApplyInvInertia(r2, r2);
            r2.CrossVectors(r2, r1);
            return v.Params.invMass + r2.Dot(n);
        }

        /// <summary>
        /// Resolve one car against the static world (call after Vehicle.Step).
        /// Returns the largest impact, or null if nothing was hit. Breakable
        /// circles are marked broken and reported through onBreak.
        /// </summary>
        public static CollisionEvent? CollideWorld(Vehicle v, Colliders world, Action<Circle> onBreak = null)
        {
            Box b = CarBox(v, boxA);
            double reach = Math.Sqrt(b.hl * b.hl + b.hw * b.hw) + 2;
            qVehicle = v;
            qOnBreak = onBreak;
            qHasWorst = false;
            qWorst = default;
            qHeight = (b.bottom + b.top) / 2 - 0.15;
            world.Query(b.cx, b.cz, reach, onSegment, onCircle);
            CollisionEvent? worst = qHasWorst ? qWorst : (CollisionEvent?)null;
            qVehicle = null;
            qOnBreak = null;
            return worst;
        }

        static void OnSegment(Segment s)
        {
            // The TypeScript's `b` is boxA itself, so the height check sees the latest carBox.
            Box b = boxA;
            if (b.bottom > s.top || b.top < s.bottom) return;
            Vehicle v = qVehicle;
            if (BoxSegment(CarBox(v, boxA), s, contact))
            {
                double j = ResolveStatic(v, contact, s.bounce, s.friction, qHeight);
                if (j > 0 && (!qHasWorst || j > qWorst.impulse))
                {
                    qHasWorst = true;
                    qWorst = new CollisionEvent { impulse = j, x = contact.px, z = contact.pz, kind = Kind(s.kind) };
                }
            }
        }

        static void OnCircle(Circle c)
        {
            Box b = boxA;
            if (b.bottom > c.top || b.top < c.bottom) return;
            Vehicle v = qVehicle;
            if (BoxPoint(CarBox(v, boxA), c.x, c.z, c.r, contact))
            {
                if (c.breakable)
                {
                    c.broken = true;
                    // A breakable prop costs a little speed but does not stop the car.
                    v.vel.Scale(0.97);
                    qOnBreak?.Invoke(c);
                    return;
                }
                double j = ResolveStatic(v, contact, c.kind == CircleKind.Tree ? 0.15 : 0.25, 0.4, qHeight);
                if (j > 0 && (!qHasWorst || j > qWorst.impulse))
                {
                    qHasWorst = true;
                    qWorst = new CollisionEvent { impulse = j, x = contact.px, z = contact.pz, kind = Kind(c.kind) };
                }
            }
        }

        /// <summary>Resolve a pair of cars (call after both have stepped). Returns the impulse magnitude (0 if no contact).</summary>
        public static double CollideCars(Vehicle a, Vehicle b)
        {
            Box A = CarBox(a, boxA);
            Box B = CarBox(b, boxB);
            double dx = A.cx - B.cx;
            double dz = A.cz - B.cz;
            double reach = A.hl + B.hl + 0.5;
            if (dx * dx + dz * dz > reach * reach) return 0;
            if (A.bottom > B.top || A.top < B.bottom) return 0;
            if (!BoxBox(A, B, contact)) return 0;
            double height = JsMath.Max(A.bottom, B.bottom) + 0.35;
            pt.Set(contact.px, height, contact.pz);
            // Split the positional correction by inverse mass.
            double ima = a.Params.invMass;
            double imb = b.Params.invMass;
            double push = JsMath.Max(0, contact.depth - 0.005) / (ima + imb);
            a.pos.x += contact.nx * push * ima;
            a.pos.z += contact.nz * push * ima;
            b.pos.x -= contact.nx * push * imb;
            b.pos.z -= contact.nz * push * imb;
            a.PointVelocity(pt, tmp);
            b.PointVelocity(pt, tmp2);
            double rvx = tmp.x - tmp2.x;
            double rvz = tmp.z - tmp2.z;
            double vn = rvx * contact.nx + rvz * contact.nz;
            if (vn >= 0) return 0;
            V3 n = nScratch.Copy(r1.Set(contact.nx, 0, contact.nz));
            double k = EffectiveInvMass(a, pt, n) + EffectiveInvMass(b, pt, n);
            const double bounce = 0.3;
            double jn = (-(1 + bounce) * vn) / k;
            V3 imp = impScratch.Copy(n).Scale(jn);
            // Friction between the bodies.
            double tx = rvx - vn * contact.nx;
            double tz = rvz - vn * contact.nz;
            double tl = Math.Sqrt(tx * tx + tz * tz);
            if (tl > 1e-4)
            {
                V3 t = tScratch.Set(tx / tl, 0, tz / tl);
                double kt = EffectiveInvMass(a, pt, t) + EffectiveInvMass(b, pt, t);
                double jt = JsMath.Min(tl / kt, 0.3 * jn);
                imp.AddScaled(t, -jt);
            }
            a.ApplyImpulse(pt, imp);
            b.ApplyImpulse(pt, imp.Negate());
            a.impact = JsMath.Max(a.impact, jn);
            b.impact = JsMath.Max(b.impact, jn);
            return jn;
        }
    }
}
