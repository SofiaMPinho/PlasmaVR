// ClippingUtils.hlsl
// Shared clipping utility functions for particles, streamlines, and isosurfaces

#ifndef CLIPPING_UTILS_INCLUDED
#define CLIPPING_UTILS_INCLUDED

// Global clipping box parameters
// Guard redefinitions so ShaderGraph-generated passes can declare them too.
#ifndef CLIPBOX_UNIFORMS_DEFINED
#define CLIPBOX_UNIFORMS_DEFINED
float4 _ClipBoxCenter1;
float4 _ClipBoxSize1;
float3 _ClipBoxInvRotRow0_1; // inverse rotation matrix row 0 for box 1
float3 _ClipBoxInvRotRow1_1; // inverse rotation matrix row 1 for box 1
float3 _ClipBoxInvRotRow2_1; // inverse rotation matrix row 2 for box 1
float4 _ClipBoxCenter2;
float4 _ClipBoxSize2;
float3 _ClipBoxInvRotRow0_2; // inverse rotation matrix row 0 for box 2
float3 _ClipBoxInvRotRow1_2; // inverse rotation matrix row 1 for box 2
float3 _ClipBoxInvRotRow2_2; // inverse rotation matrix row 2 for box 2
float _ClipBoxesEnabled;
#endif

// Optimized box containment test using pre-computed inverse rotation matrix
// Returns true if position is inside the box
bool IsInsideBox(float3 worldPos, float3 boxCenter, float3 boxHalfSize, float3 invRotRow0, float3 invRotRow1, float3 invRotRow2)
{
    // Transform world position to box local space using inverse rotation matrix
    float3 relPos = worldPos - boxCenter;
    float3 localPos = float3(
        dot(relPos, invRotRow0),
        dot(relPos, invRotRow1),
        dot(relPos, invRotRow2)
    );
    localPos = abs(localPos);
    
    // Use all() for a single vector comparison instead of 3 separate comparisons
    return all(localPos < boxHalfSize);
}

// Main clipping test function
// Returns true if the fragment should be clipped (discarded)
bool ShouldClip(float3 worldPos)
{
    // Early exit if clipping is disabled
    if (_ClipBoxesEnabled < 0.5)
        return false;
    float3 halfSize1 = _ClipBoxSize1.xyz * 0.5;
    float3 halfSize2 = _ClipBoxSize2.xyz * 0.5;
    
    // Check if size is effectively zero (box not in use)
    bool box1Active = any(halfSize1 > 0.001);
    bool box2Active = any(halfSize2 > 0.001);
    
    // Early exit if no boxes are active
    if (!box1Active && !box2Active)
        return false;
    
    bool insideBox1 = box1Active && IsInsideBox(worldPos, _ClipBoxCenter1.xyz, halfSize1, _ClipBoxInvRotRow0_1, _ClipBoxInvRotRow1_1, _ClipBoxInvRotRow2_1);
    bool insideBox2 = box2Active && IsInsideBox(worldPos, _ClipBoxCenter2.xyz, halfSize2, _ClipBoxInvRotRow0_2, _ClipBoxInvRotRow1_2, _ClipBoxInvRotRow2_2);
    
    return insideBox1 || insideBox2;
}

// Optimized version that discards directly
void ApplyClipping(float3 worldPos)
{
    if (ShouldClip(worldPos))
    {
        discard;
    }
}

#endif // CLIPPING_UTILS_INCLUDED
