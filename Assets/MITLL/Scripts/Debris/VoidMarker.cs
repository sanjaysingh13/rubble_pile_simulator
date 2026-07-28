// USAR extension to RubbleSim (not part of the original MITLL release).
// Lightweight marker left at the center of a confined-space void after the rubble
// pile is frozen. Purely informational at runtime; draws a Scene-view gizmo so the
// generated voids (survivable pockets) are easy to inspect and to place victims in.

using UnityEngine;

public class VoidMarker : MonoBehaviour
{
    public float radius = 1f;
    public bool isCapsule = false;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);
        // Small axis tick so the marker is visible even when the void is tiny.
        Gizmos.DrawLine(transform.position - Vector3.up * radius,
                        transform.position + Vector3.up * radius);
    }
}
