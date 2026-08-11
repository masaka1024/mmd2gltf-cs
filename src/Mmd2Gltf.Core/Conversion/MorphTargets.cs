// ===========================================================================
// どの PMX モーフを glTF のモーフターゲットにするかを決める。
// Python 版 convert.py の collect_target_morphs の移植。
//
//   頂点モーフ -> POSITION の差分
//   UV モーフ   -> TEXCOORD_0 の差分
//   グループモーフ -> 子が全て頂点/UVモーフのときだけ、展開して1つにまとめる
//
// scale は全体の単位スケール。モーフのオフセットは頂点位置と同じ単位の変位なので
// 位置差分には掛けるが、UV 差分には掛けない。
// ===========================================================================
using System.Collections.Generic;
using Mmd2Gltf.Pmx;

namespace Mmd2Gltf.Conversion
{
    public static class MorphTargets
    {
        public sealed class Entry
        {
            public int MorphIndex;
            public Dictionary<int, double[]> Pos = new();   // 頂点index -> (dx,dy,dz)
            public Dictionary<int, double[]> Uv = new();    // 頂点index -> (du,dv)
        }

        public static List<Entry> Collect(PmxModel model, double scale)
        {
            var morphs = model.Morphs;
            var outList = new List<Entry>();

            void Accumulate(PmxMorph mo, double weight, Entry acc)
            {
                if (mo.Type == 1)          // 頂点モーフ
                {
                    foreach (var off in mo.Offsets)
                    {
                        int vi = off.Vertex.Value;
                        if (!acc.Pos.TryGetValue(vi, out var d))
                            acc.Pos[vi] = d = new double[3];
                        for (int c = 0; c < 3; c++) d[c] += off.Offset[c] * weight * scale;
                    }
                }
                else if (mo.Type == 3)     // UV モーフ
                {
                    foreach (var off in mo.Offsets)
                    {
                        int vi = off.Vertex.Value;
                        if (!acc.Uv.TryGetValue(vi, out var d))
                            acc.Uv[vi] = d = new double[2];
                        d[0] += off.Offset[0] * weight;
                        d[1] += off.Offset[1] * weight;
                    }
                }
            }

            for (int i = 0; i < morphs.Count; i++)
            {
                var mo = morphs[i];
                if (mo.Type == 1 || mo.Type == 3)
                {
                    var e = new Entry { MorphIndex = i };
                    Accumulate(mo, 1.0, e);
                    outList.Add(e);
                }
                else if (mo.Type == 0)     // グループ
                {
                    bool allSimple = true;
                    foreach (var off in mo.Offsets)
                    {
                        int ci = off.Morph ?? -1;
                        if (ci < 0 || ci >= morphs.Count ||
                            (morphs[ci].Type != 1 && morphs[ci].Type != 3))
                        { allSimple = false; break; }
                    }
                    if (!allSimple) continue;
                    var e = new Entry { MorphIndex = i };
                    foreach (var off in mo.Offsets)
                        Accumulate(morphs[off.Morph.Value], off.Weight.Value, e);
                    outList.Add(e);
                }
            }
            return outList;
        }
    }
}
