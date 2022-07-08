using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stride.Core;
using Stride.Core.Mathematics;

namespace Stride.Rendering.Lights
{
    /// <summary>
    /// An Area light.
    /// </summary>
    [DataContract("LightDisc")]
    [Display("Disc")]
    public class LightDisc : DirectLightBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LightDisc"/> class.
        /// </summary>
        public LightDisc()
        {
            Range = 1.0f;
            Shadow = new LightDiscShadowMap()
            {
                Size = LightShadowMapSize.Small
            };
        }
        // public Texture Texture;
        public bool TwoSided { get; set; }
        public Vector3 PlaneNormal { get; set; }
        public float Range { get; set; }
        public float Radius { get; set; }
        public float Intensity { get; set; }


        public override bool HasBoundingBox => true;

        public override bool Update(RenderLight light)
        {
            Range = Math.Max(0.01f, Range);
            return true;
        }

        public override BoundingBox ComputeBounds(Vector3 positionWS, Vector3 directionWS)
        {
            // return new BoundingBox(positionWS - Radius, positionWS + Radius);
            return new(positionWS - 5, positionWS + 5);
        }

        public override float ComputeScreenCoverage(RenderView renderView, Vector3 position, Vector3 direction)
        {
            var targetPosition = new Vector4(position, 1.0f);
            Vector4.Transform(ref targetPosition, ref renderView.ViewProjection, out Vector4 projectedTarget);

            var d = Math.Abs(projectedTarget.W) + 0.00001f;
            var r = Radius;

            // Handle correctly the case where the eye is inside the sphere
            if (d < r)
                return Math.Max(renderView.ViewSize.X, renderView.ViewSize.Y);

            var coTanFovBy2 = renderView.Projection.M22;
            var pr = r * coTanFovBy2 / (Math.Sqrt(d * d - r * r) + 0.00001f);

            // Size on screen
            return (float)pr * Math.Max(renderView.ViewSize.X, renderView.ViewSize.Y) * 2;
        }
    }
}
