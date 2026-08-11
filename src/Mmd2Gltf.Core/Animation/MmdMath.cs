// ===========================================================================
// ベクトル / クォータニオンのヘルパ。Python 版 mathutil.py の移植。
// クォータニオンは (x, y, z, w)。
//
// ★エンジン側 (BulletPhysics.Quat / Vec3) は使わない。理由:
//   1) こちらは Python 版と数値を突き合わせるため float64 で計算する必要がある
//      (IK は反復計算なので float32 だと目に見えて分岐しうる)。
//   2) エンジンの FromEuler は PMX 剛体向けの YXZ 順で、ここで使う
//      IK 角度制限の XYZ 順とは別物。混ぜると過去のオイラー順序バグを繰り返す。
// ===========================================================================
using System;
using System.Text;

namespace Mmd2Gltf.Animation
{
    public struct Vec3d
    {
        public double X, Y, Z;
        public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static readonly Vec3d Zero = new(0, 0, 0);

        public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public Vec3d Normalized
        {
            get
            {
                double n = Length;
                return n < 1e-12 ? Zero : new Vec3d(X / n, Y / n, Z / n);
            }
        }

        public static Vec3d Cross(Vec3d a, Vec3d b) =>
            new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public double this[int i] => i == 0 ? X : i == 1 ? Y : Z;
    }

    public struct Quatd
    {
        public double X, Y, Z, W;
        public Quatd(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
        public static readonly Quatd Identity = new(0, 0, 0, 1);

        public static Quatd operator *(Quatd a, Quatd b) => new(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>共役 (単位クォータニオンでは逆元)。</summary>
        public Quatd Inverse => new(-X, -Y, -Z, W);

        public Quatd Normalized
        {
            get
            {
                double n = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
                return n < 1e-12 ? Identity : new Quatd(X / n, Y / n, Z / n, W / n);
            }
        }

        public double this[int i] => i == 0 ? X : i == 1 ? Y : i == 2 ? Z : W;
    }

    public static class MmdMath
    {
        public static Vec3d Rotate(Quatd q, Vec3d v)
        {
            // t = 2 * cross(q.xyz, v)
            double tx = 2 * (q.Y * v.Z - q.Z * v.Y);
            double ty = 2 * (q.Z * v.X - q.X * v.Z);
            double tz = 2 * (q.X * v.Y - q.Y * v.X);
            return new Vec3d(
                v.X + q.W * tx + q.Y * tz - q.Z * ty,
                v.Y + q.W * ty + q.Z * tx - q.X * tz,
                v.Z + q.W * tz + q.X * ty - q.Y * tx);
        }

        public static Quatd Slerp(Quatd a, Quatd b, double t)
        {
            double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot < 0.0)
            {
                b = new Quatd(-b.X, -b.Y, -b.Z, -b.W);
                dot = -dot;
            }
            if (dot > 0.9995)
                return new Quatd(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y),
                                 a.Z + t * (b.Z - a.Z), a.W + t * (b.W - a.W)).Normalized;
            double th = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
            double s = Math.Sin(th);
            double wa = Math.Sin((1 - t) * th) / s;
            double wb = Math.Sin(t * th) / s;
            return new Quatd(a.X * wa + b.X * wb, a.Y * wa + b.Y * wb,
                             a.Z * wa + b.Z * wb, a.W * wa + b.W * wb);
        }

        public static Quatd FromAxisAngle(Vec3d axis, double angle)
        {
            double s = Math.Sin(angle * 0.5);
            return new Quatd(axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(angle * 0.5));
        }

        /// <summary>クォータニオン → intrinsic XYZ オイラー角 (IK の角度制限用)。</summary>
        public static Vec3d ToEulerXyz(Quatd q)
        {
            double m20 = 2 * (q.X * q.Z - q.W * q.Y);
            double sy = Math.Max(-1.0, Math.Min(1.0, -m20));
            double ey = Math.Asin(sy);
            double ex, ez;
            if (Math.Abs(sy) < 0.99999)
            {
                ex = Math.Atan2(2 * (q.Y * q.Z + q.W * q.X), 1 - 2 * (q.X * q.X + q.Y * q.Y));
                ez = Math.Atan2(2 * (q.X * q.Y + q.W * q.Z), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
            }
            else
            {
                ex = Math.Atan2(-2 * (q.Y * q.Z - q.W * q.X), 1 - 2 * (q.X * q.X + q.Z * q.Z));
                ez = 0.0;
            }
            return new Vec3d(ex, ey, ez);
        }

        /// <summary>
        /// PMX 剛体 / Joint のオイラー角 (ラジアン) → クォータニオン。適用順は Z→X→Y、
        /// すなわち R = Ry * Rx * Rz (YXZ 順)。Bullet の btQuaternion(yaw,pitch,roll) と同型。
        /// エンジンの Quat.FromEulerYxz と同じ規約 (あちらは float の展開形、
        /// こちらは Python 版 physics.py と同じ「軸ごとに作って順に掛ける」形)。
        /// ★ZYX 順にしないこと。過去に取り違えて剛体の向きがズレた。
        /// </summary>
        public static Quatd FromEulerYxz(double rx, double ry, double rz)
        {
            Quatd Axis(double x, double y, double z, double ang)
            {
                double s = Math.Sin(ang * 0.5);
                return new Quatd(x * s, y * s, z * s, Math.Cos(ang * 0.5));
            }
            var qy = Axis(0, 1, 0, ry);
            var qx = Axis(1, 0, 0, rx);
            var qz = Axis(0, 0, 1, rz);
            return qy * qx * qz;
        }

        public static Quatd FromEulerXyz(Vec3d e)
        {
            double cx = Math.Cos(e.X / 2), sx = Math.Sin(e.X / 2);
            double cy = Math.Cos(e.Y / 2), sy = Math.Sin(e.Y / 2);
            double cz = Math.Cos(e.Z / 2), sz = Math.Sin(e.Z / 2);
            return new Quatd(
                sx * cy * cz + cx * sy * sz,
                cx * sy * cz - sx * cy * sz,
                cx * cy * sz + sx * sy * cz,
                cx * cy * cz - sx * sy * sz).Normalized;
        }
    }

    /// <summary>MMD/VMD の文字列は Shift-JIS。名前の突き合わせは
    /// 「Shift-JIS へエンコードして先頭 N バイト」で行うので、
    /// Python の encode("shift-jis", errors="replace") と同じ挙動に揃える。</summary>
    public static class MmdEncoding
    {
        /// <summary>ベストフィット変換を無効にした Shift-JIS
        /// (GetEncoding(int) の1引数版はベストフィットを使うので必ずこちらを使う)。</summary>
        public static readonly Encoding ShiftJis = Create();

        private static Encoding Create()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932,
                new EncoderReplacementFallback("?"),
                new DecoderReplacementFallback("�"));  // Python の errors="replace" と同じ
        }

        /// <summary>Shift-JIS へエンコードして先頭 maxBytes バイトを取る (名前照合キー)。</summary>
        public static byte[] TruncatedKey(string name, int maxBytes)
        {
            byte[] b = ShiftJis.GetBytes(name);
            if (b.Length <= maxBytes) return b;
            var t = new byte[maxBytes];
            Array.Copy(b, t, maxBytes);
            return t;
        }

        /// <summary>byte[] を辞書キーにするための比較子。</summary>
        public sealed class ByteArrayComparer : System.Collections.Generic.IEqualityComparer<byte[]>
        {
            public static readonly ByteArrayComparer Instance = new();
            public bool Equals(byte[] a, byte[] b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                return true;
            }
            public int GetHashCode(byte[] a)
            {
                int h = 17;
                foreach (byte x in a) h = h * 31 + x;
                return h;
            }
        }
    }
}
