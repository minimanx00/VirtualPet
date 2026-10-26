using UnityEngine;

/// <summary>
/// Builds a tapered tail around a FABRIK chain between two independently movable controls.
/// All lengths and the bend direction are measured in this object's local space.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[DefaultExecutionOrder(100)]
public sealed class TailIK : MonoBehaviour
{
    [Header("Controls")]
    [SerializeField] private Transform tailStart;
    [SerializeField] private Transform tailEnd;

    [Header("Shape")]
    [Tooltip("Maximum reach from Tail Start in this object's local units. Tail End is clamped to this distance.")]
    [SerializeField, Min(0.01f)] private float tailLength = 1f;
    [Tooltip("Preferred bend direction in Tail local space. For example, (0, 1, 0) arches upward.")]
    [SerializeField] private Vector3 bendDirection = Vector3.up;
    [SerializeField, Min(0.001f)] private float baseRadius = 0.16f;
    [SerializeField, Min(0.001f)] private float tipRadius = 0.015f;
    [SerializeField, Range(2, 64)] private int segments = 20;
    [SerializeField, Range(3, 32)] private int sides = 12;

    [Header("IK Solver")]
    [SerializeField, Range(1, 128)] private int iterations = 64;
    [SerializeField, Min(0.00001f)] private float tolerance = 0.0001f;

    private MeshFilter meshFilter;
    private Mesh generatedMesh;
    private Vector3[] joints;
    private Vector3[] vertices;
    private int meshSegments;
    private int meshSides;

    private void OnEnable()
    {
        Rebuild();
    }

    private void LateUpdate()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        // OnValidate can run on a loading thread; mesh work belongs in LateUpdate.
        ClampSettings();
    }

    private void ClampSettings()
    {
        tailLength = Mathf.Max(0.01f, tailLength);
        baseRadius = Mathf.Max(0.001f, baseRadius);
        tipRadius = Mathf.Max(0.001f, tipRadius);
        segments = Mathf.Clamp(segments, 2, 64);
        sides = Mathf.Clamp(sides, 3, 32);
        iterations = Mathf.Clamp(iterations, 1, 128);
        tolerance = Mathf.Max(0.00001f, tolerance);
    }

    /// <summary>Refresh immediately after changing controls outside the normal frame loop.</summary>
    public void Rebuild()
    {
        if (!isActiveAndEnabled)
            return;

#if UNITY_EDITOR
        // Generate only on scene instances and in Prefab Mode, never on the imported asset.
        if (UnityEditor.EditorUtility.IsPersistent(this))
            return;
#endif

        if (tailStart == null || tailEnd == null)
        {
            if (generatedMesh != null)
                generatedMesh.Clear();
            joints = null;
            return;
        }

        ClampSettings();
        EnsureMesh();

        Vector3 root = transform.InverseTransformPoint(tailStart.position);
        Vector3 target = transform.InverseTransformPoint(tailEnd.position);
        Vector3 offset = target - root;
        if (offset.sqrMagnitude > tailLength * tailLength)
        {
            target = root + Vector3.ClampMagnitude(offset, tailLength);
            tailEnd.position = transform.TransformPoint(target);
        }

        Solve(root, target);
        UpdateMesh();
    }

    private void EnsureMesh()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        if (generatedMesh == null)
        {
            generatedMesh = new Mesh
            {
                name = "Tail IK (Generated)",
                hideFlags = HideFlags.HideAndDontSave
            };
            generatedMesh.MarkDynamic();
        }

        if (meshFilter.sharedMesh != generatedMesh)
            meshFilter.sharedMesh = generatedMesh;

        if (joints != null && meshSegments == segments && meshSides == sides)
            return;

        meshSegments = segments;
        meshSides = sides;
        joints = new Vector3[segments + 1];
        vertices = new Vector3[(segments + 1) * sides + 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[(segments + 1) * sides * 6];
        int index = 0;

        for (int ring = 0; ring <= segments; ring++)
        {
            for (int side = 0; side < sides; side++)
            {
                int a = ring * sides + side;
                int next = ring * sides + (side + 1) % sides;
                uv[a] = new Vector2((float)side / sides, (float)ring / segments);
                if (ring == segments)
                    continue;

                triangles[index++] = a;
                triangles[index++] = next;
                triangles[index++] = a + sides;
                triangles[index++] = next;
                triangles[index++] = next + sides;
                triangles[index++] = a + sides;
            }
        }

        int startCap = vertices.Length - 2;
        int endCap = vertices.Length - 1;
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            triangles[index++] = startCap;
            triangles[index++] = next;
            triangles[index++] = side;
            triangles[index++] = endCap;
            triangles[index++] = segments * sides + side;
            triangles[index++] = segments * sides + next;
        }

        generatedMesh.Clear();
        generatedMesh.vertices = vertices;
        generatedMesh.uv = uv;
        generatedMesh.triangles = triangles;
    }

    private void Solve(Vector3 root, Vector3 target)
    {
        Vector3 delta = target - root;
        float distance = delta.magnitude;
        float length = tailLength;
        float linkLength = length / segments;
        Vector3 forward = distance > 0.000001f ? delta / distance : Vector3.back;

        if (distance >= length)
        {
            for (int i = 0; i <= segments; i++)
                joints[i] = root + forward * (linkLength * i);
            return;
        }

        Vector3 bend = Perpendicular(forward, bendDirection);

        // Seed a circular arc with the chain's length and endpoint distance. A straight seed
        // cannot bend under axial compression, and an arbitrary curve converges slowly near
        // full extension. The chord of this polygon is length * sin(h) / (n * sin(h / n)).
        double lower = 0d;
        double upper = System.Math.PI;
        for (int i = 0; i < 28; i++)
        {
            double middle = (lower + upper) * 0.5d;
            double chord = length * System.Math.Sin(middle) / (segments * System.Math.Sin(middle / segments));
            if (chord > distance)
                lower = middle;
            else
                upper = middle;
        }

        double halfAngle = (lower + upper) * 0.5d;
        double radius = linkLength / (2d * System.Math.Sin(halfAngle / segments));
        double sinHalf = System.Math.Sin(halfAngle);
        double cosHalf = System.Math.Cos(halfAngle);
        for (int i = 0; i <= segments; i++)
        {
            double angle = halfAngle * (2d * i / segments - 1d);
            joints[i] = root
                + forward * (float)(radius * (System.Math.Sin(angle) + sinHalf))
                + bend * (float)(radius * (System.Math.Cos(angle) - cosHalf));
        }
        joints[0] = root;
        joints[segments] = target;

        float toleranceSquared = tolerance * tolerance;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            joints[segments] = target;
            for (int i = segments - 1; i >= 0; i--)
            {
                Vector3 direction = SafeDirection(joints[i] - joints[i + 1], -forward);
                joints[i] = joints[i + 1] + direction * linkLength;
            }

            joints[0] = root;
            for (int i = 1; i <= segments; i++)
            {
                Vector3 direction = SafeDirection(joints[i] - joints[i - 1], forward);
                joints[i] = joints[i - 1] + direction * linkLength;
            }

            if ((joints[segments] - target).sqrMagnitude <= toleranceSquared)
                break;
        }
    }

    private void UpdateMesh()
    {
        Vector3 normal = bendDirection;
        for (int ring = 0; ring <= segments; ring++)
        {
            Vector3 tangent = ring == 0 ? joints[1] - joints[0]
                : ring == segments ? joints[segments] - joints[segments - 1]
                : joints[ring + 1] - joints[ring - 1];
            tangent = SafeDirection(tangent, Vector3.back);
            normal = Perpendicular(tangent, normal);
            Vector3 binormal = Vector3.Cross(tangent, normal);
            float radius = Mathf.Lerp(baseRadius, tipRadius, (float)ring / segments);

            for (int side = 0; side < sides; side++)
            {
                float angle = side * Mathf.PI * 2f / sides;
                vertices[ring * sides + side] = joints[ring]
                    + (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radius;
            }
        }

        vertices[vertices.Length - 2] = joints[0];
        vertices[vertices.Length - 1] = joints[segments];
        generatedMesh.vertices = vertices;
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();
    }

    private static Vector3 SafeDirection(Vector3 direction, Vector3 fallback)
    {
        return direction.sqrMagnitude > 0.000000000001f ? direction.normalized : fallback;
    }

    private static Vector3 Perpendicular(Vector3 axis, Vector3 preferred)
    {
        Vector3 result = Vector3.ProjectOnPlane(preferred, axis);
        if (result.sqrMagnitude < 0.000001f)
            result = Vector3.ProjectOnPlane(Vector3.right, axis);
        if (result.sqrMagnitude < 0.000001f)
            result = Vector3.ProjectOnPlane(Vector3.forward, axis);
        return result.normalized;
    }

    private void OnDisable()
    {
        if (generatedMesh == null)
            return;

        if (meshFilter != null && meshFilter.sharedMesh == generatedMesh)
            meshFilter.sharedMesh = null;

        if (Application.IsPlaying(gameObject))
            Destroy(generatedMesh);
        else
            DestroyImmediate(generatedMesh);

        generatedMesh = null;
        joints = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (joints == null)
            return;

        Gizmos.color = Color.yellow;
        for (int i = 1; i < joints.Length; i++)
            Gizmos.DrawLine(transform.TransformPoint(joints[i - 1]), transform.TransformPoint(joints[i]));

        if (tailStart != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(tailStart.position, 0.04f);
        }
        if (tailEnd != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(tailEnd.position, 0.04f);
        }
    }
}
