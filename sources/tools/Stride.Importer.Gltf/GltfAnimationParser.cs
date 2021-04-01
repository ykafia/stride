using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Animations;
using Stride.Core.Collections;
using Stride.Core.Mathematics;
using Stride.Rendering;
using static Stride.Importer.Gltf.GltfUtils;

namespace Stride.Importer.Gltf
{
    public class GltfAnimationParser
    {
        public static Skeleton ConvertSkeleton(SharpGLTF.Schema2.ModelRoot root)
        {
            Skeleton result = new Skeleton();
            var skin = root.LogicalNodes.First(x => x.Mesh == root.LogicalMeshes.First()).Skin;
            // If there is no corresponding skins return null
            if (skin == null)
            {
                result.Nodes = new List<ModelNodeDefinition>() {
                    new ModelNodeDefinition
                    {
                        Name = "root",
                        Flags = ModelNodeFlags.EnableRender,
                        ParentIndex = -1,
                        Transform = new TransformTRS
                        {
                            Position = Vector3.Zero,
                            Rotation = Quaternion.Identity,
                            Scale = Vector3.Zero
                        }
                    },
                    new ModelNodeDefinition
                    {
                        Name = "Mesh",
                        Flags = ModelNodeFlags.EnableRender,
                        ParentIndex = -1,
                        Transform = new TransformTRS
                        {
                            Position = Vector3.Zero,
                            Rotation = Quaternion.Identity,
                            Scale = Vector3.Zero
                        }
                    },
                }.ToArray();
                return result;
            }

            var jointList = Enumerable.Range(0, skin.JointsCount).Select(x => skin.GetJoint(x).Joint).ToList();
            var mnd =
                jointList
                .Select(
                    x =>
                    new ModelNodeDefinition
                    {
                        Name = x.Name ?? "Joint_" + x.LogicalIndex,
                        Flags = ModelNodeFlags.Default,
                        ParentIndex = jointList.IndexOf(x.VisualParent) + 1,
                        Transform = new TransformTRS
                        {
                            Position = ConvertNumerics(x.LocalTransform.Translation),
                            Rotation = ConvertNumerics(x.LocalTransform.Rotation),
                            Scale = ConvertNumerics(x.LocalTransform.Scale)
                        }

                    }
                )
                .ToList();
            mnd.Insert(
                    0,
                    new ModelNodeDefinition
                    {
                        Name = "Armature",
                        Flags = ModelNodeFlags.EnableRender,
                        ParentIndex = -1,
                        Transform = new TransformTRS
                        {
                            Position = Vector3.Zero,
                            Rotation = Quaternion.Identity,
                            Scale = Vector3.Zero
                        }
                    });
            result.Nodes = mnd.ToArray();
            return result;
        }
        

        public static KeyFrameData<T> CreateKeyFrame<T>(float keyTime, T value)
        {
            return new KeyFrameData<T>((CompressedTimeSpan)TimeSpan.FromSeconds(keyTime), value);
        }

        public static Dictionary<string, AnimationCurve> ConvertCurves(IReadOnlyList<SharpGLTF.Schema2.AnimationChannel> channels, SharpGLTF.Schema2.ModelRoot root)
        {
            var result = new Dictionary<string, AnimationCurve>();
            if (root.LogicalAnimations.Count == 0) return result;
            var skin = root.LogicalNodes.First(x => x.Mesh == root.LogicalMeshes.First()).Skin;

            // In case there is no skin joints/bones, animate transform component
            if (skin == null)
            {
                string base2 = "[TransformComponent].type";
                foreach (var chan in channels)
                {
                    switch (chan.TargetNodePath)
                    {
                        case SharpGLTF.Schema2.PropertyPath.translation:
                            result.Add(base2.Replace("type", "Position"), ConvertCurve(chan.GetTranslationSampler()));
                            break;
                        case SharpGLTF.Schema2.PropertyPath.rotation:
                            result.Add(base2.Replace("type", "Rotation"), ConvertCurve(chan.GetRotationSampler()));
                            break;
                        case SharpGLTF.Schema2.PropertyPath.scale:
                            result.Add(base2.Replace("type", "Scale"), ConvertCurve(chan.GetScaleSampler()));
                            break;
                    };
                }
                return result;
            }

            string baseString = "[ModelComponent.Key].Skeleton.NodeTransformations[index].Transform.type";
            
            
            
            var jointList = Enumerable.Range(0, skin.JointsCount).Select(x => skin.GetJoint(x).Joint).ToList();
            foreach (var chan in channels)
            {
                var index = jointList.IndexOf(chan.TargetNode) + 1;
                switch (chan.TargetNodePath)
                {
                    case SharpGLTF.Schema2.PropertyPath.translation:
                        result.Add(baseString.Replace("index", $"{index}").Replace("type", "Position"), ConvertCurve(chan.GetTranslationSampler()));
                        break;
                    case SharpGLTF.Schema2.PropertyPath.rotation:
                        result.Add(baseString.Replace("index", $"{index}").Replace("type", "Rotation"), ConvertCurve(chan.GetRotationSampler()));
                        break;
                    case SharpGLTF.Schema2.PropertyPath.scale:
                        result.Add(baseString.Replace("index", $"{index}").Replace("type", "Scale"), ConvertCurve(chan.GetScaleSampler()));
                        break;
                };

            }
            return result;

        }

        public static AnimationCurve<Quaternion> ConvertCurve(SharpGLTF.Schema2.IAnimationSampler<System.Numerics.Quaternion> sampler)
        {
            var interpolationType =
                sampler.InterpolationMode switch
                {
                    SharpGLTF.Schema2.AnimationInterpolationMode.LINEAR => AnimationCurveInterpolationType.Linear,
                    SharpGLTF.Schema2.AnimationInterpolationMode.STEP => AnimationCurveInterpolationType.Constant,
                    SharpGLTF.Schema2.AnimationInterpolationMode.CUBICSPLINE => AnimationCurveInterpolationType.Cubic,
                    _ => throw new NotImplementedException(),
                };

            var keyframes =
                interpolationType switch
                {
                    AnimationCurveInterpolationType.Constant =>
                        sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, ConvertNumerics(x.Value))),
                    AnimationCurveInterpolationType.Linear =>
                        sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, ConvertNumerics(x.Value))),
                    AnimationCurveInterpolationType.Cubic =>
                        sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, ConvertNumerics(x.Value))),
                        //sampler.GetCubicKeys().Select(x => CreateKeyFrame(x.Key, ConvertNumerics(x.Value))),
                        _ => throw new NotImplementedException()
                };

            return new AnimationCurve<Quaternion>
            {
                InterpolationType = interpolationType,
                KeyFrames = new FastList<KeyFrameData<Quaternion>>(keyframes)
            };
        }
        public static AnimationCurve<Vector3> ConvertCurve(SharpGLTF.Schema2.IAnimationSampler<System.Numerics.Vector3> sampler)
        {
            var interpolationType =
                sampler.InterpolationMode switch
                {
                    SharpGLTF.Schema2.AnimationInterpolationMode.LINEAR => AnimationCurveInterpolationType.Linear,
                    SharpGLTF.Schema2.AnimationInterpolationMode.STEP => AnimationCurveInterpolationType.Constant,
                    SharpGLTF.Schema2.AnimationInterpolationMode.CUBICSPLINE => AnimationCurveInterpolationType.Cubic,
                    _ => throw new NotImplementedException(),
                };

            var keyframes =
                interpolationType switch
                {
                    AnimationCurveInterpolationType.Constant =>
                        sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, ConvertNumerics(x.Value))),
                    AnimationCurveInterpolationType.Linear =>
                        sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, ConvertNumerics(x.Value))),
                    AnimationCurveInterpolationType.Cubic =>
                        sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, ConvertNumerics(x.Value))),
                        //sampler.GetCubicKeys().Select(x => CreateKeyFrame(x.Key, ConvertNumerics(x.Value))),
                        _ => throw new NotImplementedException()
                };

            return new AnimationCurve<Vector3>
            {
                InterpolationType = interpolationType,
                KeyFrames = new FastList<KeyFrameData<Vector3>>(keyframes)
            };
        }
    }
}
