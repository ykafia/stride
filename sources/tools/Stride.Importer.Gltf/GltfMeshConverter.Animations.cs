using System;
using System.Linq;
using Stride.Core.Diagnostics;
using Stride.Importer.Common;
using SharpGLTF.Schema2;
using System.Collections.Generic;
using System.IO;
using Stride.Graphics;
using Stride.Core.Mathematics;
using Stride.Core.Serialization;
using Stride.Core.Assets;
using Stride.Rendering;
using Stride.Assets.Materials;
using Stride.Animations;
using Stride.Core.Collections;

namespace Stride.Importer.Gltf;

public partial class GltfMeshConverter
{
    public Dictionary<string, AnimationClip> ConvertAnimation(ModelRoot root, string filename)
    {
        var animations = root.LogicalAnimations;
        var meshName = filename;
        var sk = ConvertSkeleton(root).Nodes.Select(x => x.Name);

        var clips =
            animations
            .Select(x =>
            {
                //Create animation clip with 
                var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(x.Duration) };
                clip.RepeatMode = AnimationRepeatMode.LoopInfinite;
                // Add Curve
                ConvertCurves(x.Channels, root).ToList().ForEach(v => clip.AddCurve(v.Key, v.Value));
                string name = x.Name ?? filename + "_Animation_" + x.LogicalIndex;
                if (clip.Curves.Count > 1) clip.Optimize();
                return (name, clip);
            }
            )
            .ToList()
            .ToDictionary(x => x.name, x => x.clip);
        return clips;
    }
    public static KeyFrameData<T> CreateKeyFrame<T>(float keyTime, T value)
    {
        return new KeyFrameData<T>((CompressedTimeSpan)TimeSpan.FromSeconds(keyTime), value);
    }

    public Dictionary<string, AnimationCurve> ConvertCurves(IReadOnlyList<SharpGLTF.Schema2.AnimationChannel> channels, SharpGLTF.Schema2.ModelRoot root)
    {
        var result = new Dictionary<string, AnimationCurve>();
        if (root.LogicalAnimations.Count == 0) return result;
        var skins = root.LogicalSkins;
        var skNodes = ConvertSkeleton(root).Nodes.ToList();
        //var skin = root.LogicalNodes.First(x => x.Mesh == root.LogicalMeshes.First()).Skin;

        // In case there is no skin joints/bones, animate transform component
        if (skins.Count() == 0)
        {
            string basestring = "[TransformComponent.Key].type";
            foreach (var chan in channels)
            {
                switch (chan.TargetNodePath)
                {
                    case SharpGLTF.Schema2.PropertyPath.translation:
                        result.Add(basestring.Replace("type", "Position"), ConvertCurve(chan.GetTranslationSampler()));
                        break;
                    case SharpGLTF.Schema2.PropertyPath.rotation:
                        result.Add(basestring.Replace("type", "Rotation"), ConvertCurve(chan.GetRotationSampler()));
                        break;
                    case SharpGLTF.Schema2.PropertyPath.scale:
                        result.Add(basestring.Replace("type", "Scale"), ConvertCurve(chan.GetScaleSampler()));
                        break;
                };
            }
            return result;
        }


        foreach (var skin in skins)
        {
            var jointList = Enumerable.Range(0, skin.JointsCount).Select(x => skin.GetJoint(x).Joint).ToList();
            foreach (var chan in channels)
            {
                //var index0 = jointList.IndexOf(chan.TargetNode) + 1;
                var index = skNodes.IndexOf(skNodes.First(x => x.Name == chan.TargetNode.Name));
                switch (chan.TargetNodePath)
                {
                    case SharpGLTF.Schema2.PropertyPath.translation:
                        result.Add(
                            $"[ModelComponent.Key].Skeleton.NodeTransformations[{index}].Transform.Position",
                            ConvertCurve(chan.GetTranslationSampler())
                        );
                        break;
                    case SharpGLTF.Schema2.PropertyPath.rotation:
                        result.Add(
                            $"[ModelComponent.Key].Skeleton.NodeTransformations[{index}].Transform.Rotation",
                            ConvertCurve(chan.GetRotationSampler())
                        );
                        break;
                    case SharpGLTF.Schema2.PropertyPath.scale:
                        result.Add(
                            $"[ModelComponent.Key].Skeleton.NodeTransformations[{index}].Transform.Scale",
                            ConvertCurve(chan.GetScaleSampler())
                        );
                        break;
                };

            }
        }
        return result;

    }

    /// <summary>
    /// Converts a GLTF AnimationSampler into a Stride AnimationCurve
    /// </summary>
    /// <param name="sampler"></param>
    /// <returns></returns>
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
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value.ToStride())),
                AnimationCurveInterpolationType.Linear =>
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value.ToStride())),
                // Cubic might be broken
                AnimationCurveInterpolationType.Cubic =>
                    sampler.GetCubicKeys().Select(x => ConvertNumerics(x.Value).Select(y => CreateKeyFrame(x.Key, y))).SelectMany(x => x),
                _ => throw new NotImplementedException()
            };

        return new AnimationCurve<Quaternion>
        {
            InterpolationType = interpolationType,
            KeyFrames = new FastList<KeyFrameData<Quaternion>>(keyframes)
        };
    }

    /// <summary>
    /// Converts a GLTF AnimationSampler into a Stride AnimationCurve
    /// </summary>
    /// <param name="sampler"></param>
    /// <returns></returns>
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
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value.ToStride())),
                AnimationCurveInterpolationType.Linear =>
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value.ToStride())),
                // TODO : Cubic can be broken
                AnimationCurveInterpolationType.Cubic =>
                    sampler.GetCubicKeys().Select(x => ConvertNumerics(x.Value).Select(y => CreateKeyFrame(x.Key, y))).SelectMany(x => x),
                _ => throw new NotImplementedException()
            };

        return new AnimationCurve<Vector3>
        {
            InterpolationType = interpolationType,
            KeyFrames = new FastList<KeyFrameData<Vector3>>(keyframes)
        };
    }
    /// <summary>
    /// Converts a GLTF AnimationSampler into a Stride AnimationCurve
    /// </summary>
    /// <param name="sampler"></param>
    /// <returns></returns>
    public static AnimationCurve<Vector2> ConvertCurve(SharpGLTF.Schema2.IAnimationSampler<System.Numerics.Vector2> sampler)
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
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value.ToStride())),
                AnimationCurveInterpolationType.Linear =>
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value.ToStride())),
                // TODO : Cubic can be broken
                AnimationCurveInterpolationType.Cubic =>
                    sampler.GetCubicKeys().Select(x => ConvertNumerics(x.Value).Select(y => CreateKeyFrame(x.Key, y))).SelectMany(x => x),
                _ => throw new NotImplementedException()
            };

        return new AnimationCurve<Vector2>
        {
            InterpolationType = interpolationType,
            KeyFrames = new FastList<KeyFrameData<Vector2>>(keyframes)
        };
    }
    /// <summary>
    /// Converts a GLTF AnimationSampler into a Stride AnimationCurve
    /// </summary>
    /// <param name="sampler"></param>
    /// <returns></returns>
    public static AnimationCurve<Vector4> ConvertCurve(SharpGLTF.Schema2.IAnimationSampler<System.Numerics.Vector4> sampler)
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
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value.ToStride())),
                AnimationCurveInterpolationType.Linear =>
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value.ToStride())),
                // TODO : Cubic can be broken
                AnimationCurveInterpolationType.Cubic =>
                    sampler.GetCubicKeys().Select(x => ConvertNumerics(x.Value).Select(y => CreateKeyFrame(x.Key, y))).SelectMany(x => x),
                _ => throw new NotImplementedException()
            };

        return new AnimationCurve<Vector4>
        {
            InterpolationType = interpolationType,
            KeyFrames = new FastList<KeyFrameData<Vector4>>(keyframes)
        };
    }
    /// <summary>
    /// Converts a GLTF AnimationSampler into a Stride AnimationCurve
    /// </summary>
    /// <param name="sampler"></param>
    /// <returns></returns>
    public static AnimationCurve<float> ConvertCurve(SharpGLTF.Schema2.IAnimationSampler<float> sampler)
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
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value)),
                AnimationCurveInterpolationType.Linear =>
                    sampler.GetLinearKeys().Select(x => CreateKeyFrame(x.Key, x.Value)),
                _ => throw new NotImplementedException()
            };

        return new AnimationCurve<float>
        {
            InterpolationType = interpolationType,
            KeyFrames = new FastList<KeyFrameData<float>>(keyframes)
        };
    }

    public static List<Vector2> ConvertNumerics((System.Numerics.Vector2, System.Numerics.Vector2, System.Numerics.Vector2) value)
    {
        return new List<Vector2> { value.Item1.ToStride(), value.Item2.ToStride(), value.Item3.ToStride() };
    }

    public static List<Vector3> ConvertNumerics((System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3) value)
    {
        return new List<Vector3> { value.Item1.ToStride(), value.Item2.ToStride(), value.Item3.ToStride() };
    }

    public static List<Vector4> ConvertNumerics((System.Numerics.Vector4, System.Numerics.Vector4, System.Numerics.Vector4) value)
    {
        return new List<Vector4> { value.Item1.ToStride(), value.Item2.ToStride(), value.Item3.ToStride() };
    }

    public static List<Quaternion> ConvertNumerics((System.Numerics.Quaternion, System.Numerics.Quaternion, System.Numerics.Quaternion) value)
    {
        return new List<Quaternion> { value.Item1.ToStride(), value.Item2.ToStride(), value.Item3.ToStride() };
    }
}
