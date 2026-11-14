using System;
using UnityEngine;

/// <summary>
/// A procedural, tapered tail driven by Tail Start's position and rotation.
/// Uses a simulated tip and a constrained IK chain; no hand-animated target is required.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[DefaultExecutionOrder(100)]
public sealed class TailIK : MonoBehaviour
{
    [Header("Controls")]
    [Tooltip("The tail base. Its rotation determines the tail's resting direction.")]
    public Transform tailStart;
    [Tooltip("Optional output marker, updated to the solved tip in Play mode. This is not an IK target.")]
    public Transform tailEnd;
    [Tooltip("Direction in Tail Start's local space. -Z points behind the Dino.")]
    public Vector3 localDirection = Vector3.back;

    [Header("Shape (Tail Local Units)")]
    [Min(0.01f)] public float tailLength = 1.2f;
    [Range(3, 64)] public int segments = 16;
    [Range(3, 24)] public int sides = 10;
    [Min(0.001f)] public float baseRadius = 0.16f;
    [Min(0.001f)] public float tipRadius = 0.015f;
    [Tooltip("How much the resting tail curls toward Tail Start's local up axis.")]
    public float restCurl = 0.4f;

    [Header("Motion")]
    [Tooltip("Spring acceleration toward the resting pose. Higher values make the tail stiffer.")]
    [Min(0f)] public float stiffness = 35f;
    [Tooltip("Velocity damping per second.")]
    [Min(0f)] public float damping = 6f;
    [Min(0f)] public float gravityScale = 0.35f;
    [Range(1f, 90f)] public float maxBendAngle = 30f;
    [Range(1, 32)] public int iterations = 16;

    [Header("World Collision")]
    public bool collideWithWorld = true;
    public LayerMask collisionLayers = ~0;
    [Tooltip("Colliders under this transform are ignored. Assign the pet root, not an environment parent.")]
    public Transform ignoreCollisionRoot;
    [Min(0f)] public float collisionSkin = 0.003f;
    [Range(0f, 1f)] public float surfaceFriction = 0.2f;

    private Vector3[] positions;
    private Vector3[] velocities;
    private Vector3[] stepStart;
    private Vector3[] restPositions;
    private Vector3[] contactNormals;
    private Vector3[] vertices;
    private Vector3[] normals;
    private Vector2[] uvs;
    private int[] triangles;
    private Collider[] overlaps = new Collider[32];
    private RaycastHit[] castHits = new RaycastHit[32];
    private CapsuleCollider collisionProbe;
    private MeshFilter meshFilter;
    private Mesh generatedMesh;
    private Mesh originalMesh;
    private Transform initializedStart;
    private Vector3 previousRoot;
    private Quaternion previousRotation;
    private float previousLength;
    private int builtSegments;
    private int builtSides;
    private bool resetPose = true;
    private bool wasPlaying;

    private void Reset()
    {
        FindControls();
    }

    private void OnEnable()
    {
        FindControls();
        resetPose = true;
    }

    private void FindControls()
    {
        if (tailStart == null)
            tailStart = transform.Find("Tail Start");
        if (tailEnd == null)
            tailEnd = transform.Find("Tail End");
        if (ignoreCollisionRoot == null)
        {
            Animator animator = GetComponentInParent<Animator>();
            ignoreCollisionRoot = animator != null ? animator.transform : transform;
        }
    }

    private void OnValidate()
    {
        tailLength = Mathf.Max(0.01f, tailLength);
        segments = Mathf.Clamp(segments, 3, 64);
        sides = Mathf.Clamp(sides, 3, 24);
        baseRadius = Mathf.Max(0.001f, baseRadius);
        tipRadius = Mathf.Max(0.001f, tipRadius);
        stiffness = Mathf.Max(0f, stiffness);
        damping = Mathf.Max(0f, damping);
        gravityScale = Mathf.Max(0f, gravityScale);
        collisionSkin = Mathf.Max(0f, collisionSkin);
        maxBendAngle = Mathf.Clamp(maxBendAngle, 1f, 90f);
        iterations = Mathf.Clamp(iterations, 1, 32);
        surfaceFriction = Mathf.Clamp01(surfaceFriction);
        resetPose = true; // Rebuild on the main thread, never inside OnValidate.
    }

    [ContextMenu("Reset Tail Pose")]
    public void ResetTailPose()
    {
        resetPose = true;
    }

    private Vector3 LocalDirection => localDirection.sqrMagnitude > 0.000001f
        ? localDirection.normalized : Vector3.back;

    private float WorldLength => tailLength * Mathf.Max(0.0001f,
        transform.TransformVector(LocalDirection).magnitude);

    private float RadiusScale
    {
        get
        {
            Vector3 scale = transform.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }
    }

    private float RadiusAt(int joint)
    {
        return Mathf.Lerp(baseRadius, tipRadius, joint / (float)segments) * RadiusScale;
    }

    private void LateUpdate()
    {
        if (tailStart == null)
            return;

        bool playing = Application.IsPlaying(gameObject);
        float length = WorldLength;
        if (generatedMesh == null || builtSegments != segments || builtSides != sides)
            BuildMesh();

        if (resetPose || initializedStart != tailStart || playing != wasPlaying ||
            !Mathf.Approximately(length, previousLength) ||
            (tailStart.position - previousRoot).sqrMagnitude > length * length * 4f)
        {
            BuildRestPose(tailStart.position, tailStart.rotation, length / segments);
            Array.Copy(restPositions, positions, positions.Length);
            Array.Clear(velocities, 0, velocities.Length);
            previousRoot = tailStart.position;
            previousRotation = tailStart.rotation;
            previousLength = length;
            initializedStart = tailStart;
            resetPose = false;
        }

        if (playing && Time.deltaTime > 0f)
            Simulate(Mathf.Min(Time.deltaTime, 1f / 15f));
        else if (!playing)
        {
            BuildRestPose(tailStart.position, tailStart.rotation, length / segments);
            Array.Copy(restPositions, positions, positions.Length);
        }

        previousRoot = tailStart.position;
        previousRotation = tailStart.rotation;
        wasPlaying = playing;
        UpdateMesh();

        // Never write into a prefab's controls while previewing in Edit mode.
        if (playing && tailEnd != null && tailEnd != tailStart &&
            tailEnd != transform && !tailStart.IsChildOf(tailEnd) && !transform.IsChildOf(tailEnd))
        {
            Vector3 tangent = positions[segments] - positions[segments - 1];
            tailEnd.SetPositionAndRotation(positions[segments],
                Quaternion.FromToRotation(LocalDirection, tangent.normalized));
        }
    }

    private void BuildRestPose(Vector3 root, Quaternion rotation, float segmentLength)
    {
        restPositions[0] = root;
        for (int i = 1; i <= segments; i++)
        {
            float t = (i - 1f) / segments;
            Vector3 direction = rotation * SafeDirection(LocalDirection + Vector3.up * (restCurl * t), LocalDirection);
            restPositions[i] = restPositions[i - 1] + direction * segmentLength;
        }
    }

    private void Simulate(float deltaTime)
    {
        // Small substeps keep springs stable across normal render frame rates.
        int steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime * 60f));
        float dt = deltaTime / steps;
        float segmentLength = WorldLength / segments;
        if (collideWithWorld && collisionLayers.value != 0)
        {
            EnsureCollisionProbe();
            // LateUpdate also sees obstacles moved by scripts/animation this frame.
            Physics.SyncTransforms();
            collisionProbe.enabled = true;
        }

        try
        {
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                BuildRestPose(Vector3.Lerp(previousRoot, tailStart.position, t),
                    Quaternion.Slerp(previousRotation, tailStart.rotation, t), segmentLength);
                Array.Copy(positions, stepStart, positions.Length);
                Array.Clear(contactNormals, 0, contactNormals.Length);
                float drag = Mathf.Exp(-damping * dt);
                for (int i = 2; i <= segments; i++)
                {
                    Vector3 acceleration = Physics.gravity * gravityScale +
                        (restPositions[i] - positions[i]) * stiffness;
                    velocities[i] = (velocities[i] + acceleration * dt) * drag;
                    positions[i] += velocities[i] * dt;
                    if (collideWithWorld && collisionLayers.value != 0)
                        SweepJoint(i, stepStart[i]);
                }

                for (int pass = 0; pass < iterations; pass++)
                {
                    // FABRIK backward/forward passes preserve link lengths. The first
                    // link is pinned to Tail Start's rotation; the remaining links bend.
                    for (int i = segments; i > 2; i--)
                        positions[i - 1] = positions[i] + SafeDirection(
                            positions[i - 1] - positions[i], restPositions[i - 1] - restPositions[i]) * segmentLength;

                    positions[0] = restPositions[0];
                    positions[1] = restPositions[1];
                    Vector3 previousDirection = (positions[1] - positions[0]).normalized;
                    for (int i = 2; i <= segments; i++)
                    {
                        Vector3 direction = SafeDirection(positions[i] - positions[i - 1], previousDirection);
                        direction = Vector3.RotateTowards(previousDirection, direction,
                            maxBendAngle * Mathf.Deg2Rad, 0f).normalized;
                        positions[i] = positions[i - 1] + direction * segmentLength;
                        previousDirection = direction;
                    }

                    if (collideWithWorld && collisionLayers.value != 0)
                        ResolveCollisions();
                }

                for (int i = 2; i <= segments; i++)
                {
                    velocities[i] = (positions[i] - stepStart[i]) / dt;
                    if (contactNormals[i].sqrMagnitude > 0.000001f)
                    {
                        Vector3 normal = contactNormals[i].normalized;
                        velocities[i] -= normal * Mathf.Min(0f, Vector3.Dot(velocities[i], normal));
                        velocities[i] *= Mathf.Pow(1f - surfaceFriction, dt * 60f);
                    }
                }
            }
        }
        finally
        {
            if (collisionProbe != null)
                collisionProbe.enabled = false;
        }
    }

    private static Vector3 SafeDirection(Vector3 direction, Vector3 fallback)
    {
        return direction.sqrMagnitude > 0.000001f ? direction.normalized : fallback.normalized;
    }

    private void EnsureCollisionProbe()
    {
        if (collisionProbe != null)
            return;
        GameObject probe = new GameObject("Tail IK collision query")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        collisionProbe = probe.AddComponent<CapsuleCollider>();
        collisionProbe.direction = 2;
        collisionProbe.isTrigger = true;
        // Enabled only during the synchronous solve: ComputePenetration needs an
        // enabled shape. It is disabled again before any physics simulation.
        collisionProbe.enabled = false;
    }

    private bool Ignore(Collider other)
    {
        return other == null || other == collisionProbe ||
            other.transform.IsChildOf(transform) ||
            (ignoreCollisionRoot != null && other.transform.IsChildOf(ignoreCollisionRoot));
    }

    private void SweepJoint(int joint, Vector3 from)
    {
        Vector3 movement = positions[joint] - from;
        float distance = movement.magnitude;
        if (distance < 0.00001f)
            return;
        Vector3 direction = movement / distance;
        float radius = RadiusAt(joint) + collisionSkin;
        int count;
        while (true)
        {
            count = Physics.SphereCastNonAlloc(from, radius, direction, castHits, distance,
                collisionLayers, QueryTriggerInteraction.Ignore);
            if (count < castHits.Length)
                break;
            Array.Resize(ref castHits, castHits.Length * 2);
        }

        float nearest = distance;
        Vector3 normal = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castHits[i];
            // Initial overlaps are handled by the penetration pass below.
            if (Ignore(hit.collider) || hit.distance <= 0f || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            normal = hit.normal;
        }
        if (normal.sqrMagnitude > 0f)
        {
            Vector3 remaining = movement - direction * nearest;
            positions[joint] = from + direction * Mathf.Max(0f, nearest - collisionSkin) +
                Vector3.ProjectOnPlane(remaining, normal);
            contactNormals[joint] += normal;
        }
    }

    private void ResolveCollisions()
    {
        // Capsule queries cover the whole link, including obstacles between joints.
        for (int i = 2; i <= segments; i++)
        {
            float radius = Mathf.Max(RadiusAt(i - 1), RadiusAt(i)) + collisionSkin;
            int count;
            while (true)
            {
                count = Physics.OverlapCapsuleNonAlloc(positions[i - 1], positions[i], radius,
                    overlaps, collisionLayers, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length)
                    break;
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }

            for (int j = 0; j < count; j++)
            {
                Collider other = overlaps[j];
                if (Ignore(other))
                    continue;
                Vector3 a = positions[i - 1];
                Vector3 b = positions[i];
                collisionProbe.radius = radius;
                collisionProbe.height = Vector3.Distance(a, b) + 2f * radius;
                Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, SafeDirection(b - a, Vector3.back));
                if (!Physics.ComputePenetration(collisionProbe, (a + b) * 0.5f, rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 normal, out float depth))
                    continue;

                Vector3 correction = normal * (depth + 0.0001f);
                // The base and first link stay attached to the pet.
                if (i > 2)
                {
                    positions[i - 1] += correction;
                    contactNormals[i - 1] += normal;
                }
                positions[i] += correction;
                contactNormals[i] += normal;
            }
        }
    }

    private void BuildMesh()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();
        if (generatedMesh == null)
        {
            originalMesh = meshFilter.sharedMesh;
            generatedMesh = new Mesh { name = "Procedural Tail IK", hideFlags = HideFlags.HideAndDontSave };
            generatedMesh.MarkDynamic();
        }
        generatedMesh.Clear();
        meshFilter.sharedMesh = generatedMesh;
        builtSegments = segments;
        builtSides = sides;
        positions = new Vector3[segments + 1];
        velocities = new Vector3[segments + 1];
        stepStart = new Vector3[segments + 1];
        restPositions = new Vector3[segments + 1];
        contactNormals = new Vector3[segments + 1];
        int stride = sides + 1;
        int sideCount = (segments + 1) * stride;
        int vertexCount = sideCount + 2 * (stride + 1);
        vertices = new Vector3[vertexCount];
        normals = new Vector3[vertexCount];
        uvs = new Vector2[vertexCount];
        triangles = new int[segments * sides * 6 + sides * 6];
        int triangle = 0;
        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = i * stride + j;
                int b = a + stride;
                triangles[triangle++] = a;
                triangles[triangle++] = a + 1;
                triangles[triangle++] = b;
                triangles[triangle++] = a + 1;
                triangles[triangle++] = b + 1;
                triangles[triangle++] = b;
            }
        }
        for (int cap = 0; cap < 2; cap++)
        {
            int first = sideCount + cap * (stride + 1);
            int center = first + stride;
            for (int j = 0; j < sides; j++)
            {
                triangles[triangle++] = center;
                triangles[triangle++] = first + j + (cap == 0 ? 1 : 0);
                triangles[triangle++] = first + j + (cap == 0 ? 0 : 1);
            }
        }
        resetPose = true;
    }

    private void UpdateMesh()
    {
        int stride = sides + 1;
        Vector3 frameNormal = tailStart.up;
        Vector3 previousTangent = SafeDirection(positions[1] - positions[0], tailStart.rotation * LocalDirection);
        Matrix4x4 normalToLocal = transform.localToWorldMatrix.transpose;
        for (int i = 0; i <= segments; i++)
        {
            Vector3 tangent = i == segments ? positions[i] - positions[i - 1] :
                i == 0 ? positions[1] - positions[0] : positions[i + 1] - positions[i - 1];
            tangent = SafeDirection(tangent, previousTangent);
            frameNormal = Quaternion.FromToRotation(previousTangent, tangent) * frameNormal;
            frameNormal = Vector3.ProjectOnPlane(frameNormal, tangent).normalized;
            if (frameNormal.sqrMagnitude < 0.000001f)
                frameNormal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 binormal = Vector3.Cross(tangent, frameNormal).normalized;
            float radius = RadiusAt(i);
            float taper = (baseRadius - tipRadius) * RadiusScale / WorldLength;
            for (int j = 0; j <= sides; j++)
            {
                float angle = j / (float)sides * Mathf.PI * 2f;
                Vector3 radial = frameNormal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                int index = i * stride + j;
                vertices[index] = transform.InverseTransformPoint(positions[i] + radial * radius);
                normals[index] = normalToLocal.MultiplyVector((radial + tangent * taper).normalized).normalized;
                uvs[index] = new Vector2(j / (float)sides, i / (float)segments);
            }
            if (i == 0 || i == segments)
            {
                int first = (segments + 1) * stride + (i == 0 ? 0 : stride + 1);
                Vector3 capNormal = normalToLocal.MultiplyVector(i == 0 ? -tangent : tangent).normalized;
                for (int j = 0; j <= sides; j++)
                {
                    vertices[first + j] = vertices[i * stride + j];
                    normals[first + j] = capNormal;
                    float angle = j / (float)sides * Mathf.PI * 2f;
                    uvs[first + j] = new Vector2(0.5f + 0.5f * Mathf.Cos(angle), 0.5f + 0.5f * Mathf.Sin(angle));
                }
                vertices[first + stride] = transform.InverseTransformPoint(positions[i]);
                normals[first + stride] = capNormal;
                uvs[first + stride] = new Vector2(0.5f, 0.5f);
            }
            previousTangent = tangent;
        }
        generatedMesh.vertices = vertices;
        generatedMesh.normals = normals;
        generatedMesh.uv = uvs;
        generatedMesh.triangles = triangles;
        generatedMesh.RecalculateBounds();
    }

    private void OnDrawGizmosSelected()
    {
        if (positions == null)
            return;
        Gizmos.color = Color.yellow;
        for (int i = 1; i < positions.Length; i++)
            Gizmos.DrawLine(positions[i - 1], positions[i]);
    }

    private void OnDisable()
    {
        if (meshFilter != null && meshFilter.sharedMesh == generatedMesh)
            meshFilter.sharedMesh = originalMesh;
        Release(generatedMesh);
        if (collisionProbe != null)
            Release(collisionProbe.gameObject);
        generatedMesh = null;
        collisionProbe = null;
    }

    private void Release(UnityEngine.Object ownedObject)
    {
        if (ownedObject == null)
            return;
        if (Application.IsPlaying(gameObject))
            Destroy(ownedObject);
        else
            DestroyImmediate(ownedObject);
    }
}
