// ===========================================================================
// enginedump — 物理エンジンへ渡す PmxPhysicsModel を JSON で出す。
//
// 同じモデルを2経路で作れるので、突き合わせて検算できる:
//   .pmx を渡す → PmxParser + EnginePhysicsModel.From (統合パイプラインの経路)
//   .glb を渡す → GlbPhysicsReader                    (physbake の経路)
// ここがズレていると物理の結果が食い違う。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BulletPhysics;
using BulletPhysics.Pmx;
using Mmd2Gltf.Physics;

namespace Mmd2Gltf.Cli
{
    public static class EngineDump
    {
        public static int Run(string[] args)
        {
            string input = null, output = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-o") output = args[++i];
                else if (input == null) input = args[i];
                else throw new ArgumentException("入力が複数指定されています。");
            }
            if (input == null)
                throw new ArgumentException("使い方: enginedump <model.pmx|model.glb> [-o out.json]");

            PmxPhysicsModel m;
            string via;
            if (Path.GetExtension(input).ToLowerInvariant() == ".glb")
            {
                m = GlbPhysicsReader.LoadBytes(File.ReadAllBytes(input), out _, out var warn);
                foreach (var w in warn) Console.Error.WriteLine("  警告: " + w);
                via = "GlbPhysicsReader";
            }
            else
            {
                m = EnginePhysicsModel.From(Mmd2Gltf.Pmx.PmxParser.Parse(input));
                via = "PmxParser + EnginePhysicsModel";
            }

            var root = new JsonObject
            {
                ["via"] = via,
                ["boneNames"] = Arr(m.BoneNames, n => (JsonNode)n),
                ["bonePositions"] = Arr(m.BonePositions, V3),
                ["boneParents"] = Arr(m.BoneParents, p => (JsonNode)p),
                ["boneDeformLayers"] = Arr(m.BoneDeformLayers, p => (JsonNode)p),
                ["rigidBodies"] = Arr(m.RigidBodies, r => (JsonNode)new JsonObject
                {
                    ["name"] = r.Name,
                    ["bone"] = r.BoneIndex,
                    ["group"] = r.Group,
                    ["nonCollisionGroup"] = r.NonCollisionGroup,
                    ["shape"] = r.ShapeType,
                    ["size"] = V3(r.Size),
                    ["position"] = V3(r.Position),
                    ["rotation"] = V3(r.Rotation),
                    ["mass"] = (double)r.Mass,
                    ["linearDamping"] = (double)r.LinearDamping,
                    ["angularDamping"] = (double)r.AngularDamping,
                    ["restitution"] = (double)r.Restitution,
                    ["friction"] = (double)r.Friction,
                    ["mode"] = r.PhysicsMode,
                }),
                ["joints"] = Arr(m.Joints, j => (JsonNode)new JsonObject
                {
                    ["name"] = j.Name,
                    ["type"] = j.JointType,
                    ["rigidA"] = j.RigidBodyAIndex,
                    ["rigidB"] = j.RigidBodyBIndex,
                    ["position"] = V3(j.Position),
                    ["rotation"] = V3(j.Rotation),
                    ["linearLower"] = V3(j.LinearLowerLimit),
                    ["linearUpper"] = V3(j.LinearUpperLimit),
                    ["angularLower"] = V3(j.AngularLowerLimit),
                    ["angularUpper"] = V3(j.AngularUpperLimit),
                    ["springLinear"] = V3(j.SpringLinear),
                    ["springAngular"] = V3(j.SpringAngular),
                }),
            };

            string json = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            if (output != null) File.WriteAllText(output, json);
            else Console.WriteLine(json);
            Console.Error.WriteLine(
                $"{via}: ボーン{m.BoneNames.Count} 剛体{m.RigidBodies.Count} Joint{m.Joints.Count}");
            return 0;
        }

        private static JsonArray Arr<T>(IEnumerable<T> src, Func<T, JsonNode> f)
        {
            var a = new JsonArray();
            foreach (var x in src) a.Add(f(x));
            return a;
        }

        private static JsonNode V3(Vec3 v) => new JsonArray((double)v.x, (double)v.y, (double)v.z);
    }
}
