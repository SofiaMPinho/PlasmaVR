using UnityEngine;

/// <summary>
/// Rotates the GameObject each frame so its front face always points toward the main camera.
/// Used on world-space text labels (e.g., grid axis labels) to keep them readable from any angle.
/// Handles Camera.main being null at startup and lazily caches the camera transform.
/// </summary>
public class LabelBillboard : MonoBehaviour
{
    Transform cam;

    void Start()
    {
        if (Camera.main != null) cam = Camera.main.transform;
    }

    void LateUpdate()
    {
        if (cam == null)
        {
            if (Camera.main == null) return;
            cam = Camera.main.transform;
        }
        // rotate so the forward of the label faces the camera (so the front faces camera)
        Vector3 dir = cam.position - transform.position;
        if (dir.sqrMagnitude < 1e-6f) return;
        // Some TextMesh prefabs face the opposite local forward direction,
        // so use -dir to ensure the front of the mesh faces the camera.
        Quaternion rot = Quaternion.LookRotation(-dir, Vector3.up);
        transform.rotation = rot;
    }
}
