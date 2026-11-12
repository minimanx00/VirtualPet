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

}
