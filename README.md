# VirtualPet

## Environment skyboxes

MainScene uses `Assets/Environment/Jungle Skybox.mat`. The jungle texture wraps around the four sides and bottom, with the square cloud texture overhead.

`Assets/Environment/Cloud Skybox.mat` is an alternative: the wide cloud texture fills the four sides and the square cloud texture fills the top and bottom.

To switch backgrounds in Unity:

1. Open MainScene.
2. Open **Window > Rendering > Lighting** and find **Environment > Skybox Material**.
3. Assign **Jungle Skybox** or **Cloud Skybox**, then save the scene.

Select either material to adjust **Tint Color**, **Exposure**, or **Rotation**. Both use Unity's built-in [6 Sided skybox shader](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/sky/skybox-material-reference/shader-skybox-6sided).

The supplied images are low-resolution and are not a matched six-face cubemap, so repeated scenery and visible seams are expected, especially when looking up or down. Clamp wrapping prevents texture-edge bleeding; it does not make the source images seamless. The jungle is a distant background and does not provide movement parallax or foreground trees.

## Dino tail IK

`Assets/TailIK.cs` is attached to **Dino > Body > Tail** in the prefab and MainScene. The component generates a capped, tapered mesh using the existing tail material. No additional Unity packages are required.

Move or rotate **Tail Start** to set the base and resting direction. **Local Direction** defaults to `(0, 0, -1)`, behind the Dino; **Rest Curl** bends the resting shape toward Tail Start's local up axis. Edit mode previews this resting shape. In Play mode the tip is calculated automatically: gravity, movement inertia, springs, and a constrained FABRIK chain determine the pose. The first link follows Tail Start's rotation, and the other links bend within **Max Bend Angle**. **Tail End** is an optional output marker updated in Play mode, not a target to drag.

Select **Tail** to tune:

- **Tail Length**, **Base Radius**, **Tip Radius**, **Segments**, and **Sides** control shape. Dimensions use Tail local units and follow its scale. World-space circular cross sections use the largest absolute scale component for their radius.
- **Stiffness** returns the chain toward its resting shape; **Damping** reduces swinging; **Gravity Scale** controls droop. Move or animate the pet to see the tip lag behind.
- **Collide With World** and **Collision Layers** select obstacles. Floors, walls, and other obstacles need enabled 3D colliders. Triggers are ignored. **Ignore Collision Root** is assigned to Dino, so the tail ignores the pet's own colliders.
- **Collision Skin** adds clearance; **Surface Friction** reduces sliding. Capsule penetration queries cover links, and sphere sweeps reduce tunneling during movement. **Iterations** controls how closely collision, length, and bend constraints converge.

The tail reacts visually to the environment; it does not push rigidbodies or collide with itself. Its base/first link remain attached, so keep Tail Start outside world obstacles. Very tight spaces or very fast motion can exceed the iterative solver's limits. Large teleports reset the pose. Disable/re-enable the component or use its **Reset Tail Pose** context menu to reset it manually. The selected object's yellow gizmo shows the solved chain.

Implementation references: [Unity ComputePenetration](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/physics/computepenetration), [Unity SphereCastNonAlloc](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/physics/spherecastnonalloc), and [Unity ExecuteAlways](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/executealways).
