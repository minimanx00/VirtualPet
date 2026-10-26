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

Open `Assets/Dino.prefab` in Prefab Mode, or drag the prefab into a scene. Under **Dino > Tail**, move **Tail Start** to position the base and **Tail End** to position the tip. The tapered mesh follows in Edit mode and Play mode; scripts and animation clips can move either control too. MainScene currently uses a separate pet object, so add the Dino prefab there to see this tail in that scene.

Select **Tail** to adjust the **Tail IK** component:

- **Tail Length** is the fixed chain length and maximum distance from Tail Start, measured in Tail local units. Dragging Tail End beyond that distance clamps the control itself to the reachable boundary in Edit mode and Play mode. Moving Tail Start or reducing Tail Length also reclamps Tail End. When the controls are closer together, the chain bends while preserving its segment lengths.
- **Bend Direction** sets the preferred arch in Tail local space. The default `(0, 1, 0)` arches upward.
- **Base Radius** and **Tip Radius** control thickness; **Segments** and **Sides** control mesh detail.
- **Iterations** and **Tolerance** control FABRIK convergence. Very low iteration counts can leave a small gap at the tip.

The component generates a capped mesh using the Crocodile Body material. Its yellow selected-object gizmo shows the IK chain. The old cylinder and its rigid collider have been replaced; the generated tail is visual geometry and has no collider. Keep the two controls outside any solved bone hierarchy. No additional Unity packages are required.

Implementation references: [Unity ExecuteAlways](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ExecuteAlways.html), [Unity Mesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.html), and [FABRIK chain IK](https://docs.unity.cn/Packages/com.unity.animation.rigging@1.1/manual/constraints/ChainIKConstraint.html).
