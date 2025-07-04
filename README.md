# DA Poly Paint for Unity

- [Introduction](#introduction)
- [Installation](#installation)
- [Using DA Poly Paint](#using-da-poly-paint)
- [Tool Action: Paint | Select](#tool-action-paint-select)
- [Save and Save as...](#save-and-save-as)
- [Export OBJ/ Import OBJ](#export-obj-import-obj)
- [Requirements and Compatibility](#requirements-and-compatibility)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Additional Notes](#additional-notes)

## Introduction <a name="introduction"></a>

[![Youtube Video](docs~/daPolyPaint_820px.png)*Watch YouTube Video*](https://www.youtube.com/watch?v=wEDbnaEky0Y)

DA Poly Paint is an easy-to-use Polygon Painting tool for Unity, offering an alternative to [pX Poly Paint for 3ds Max](https://github.com/piXelicidio/pxMaxScript/tree/master/PolyPainter). It lets you customize low-poly models directly in the Unity Editor without UV mapping, using the diffuse texture as a color palette.

## Installation from the Unity Store

Get it here: [DA PolyPaint - Atlas Color Mapper](https://assetstore.unity.com/packages/tools/painting/da-polypaint-low-poly-customizer-251157)

## Installation (from GitHub) <a name="installation"></a>

1. Download or clone the code from the GitHub repo: https://github.com/piXelicidio/DAPolyPaint
2. Place the downloaded code anywhere inside your Unity project assets.

## Using DA Poly Paint <a name="using-da-poly-paint"></a>
### Getting Started
1. Access the tool via the Unity Main Menu: **Tools** > **DA** > **Poly Paint**
2. Select any mesh object in the scene.
3. Click **'START PAINTING'** in the DA Poly Paint window.
4. Select a color by clicking the texture palette box.
5. Click and drag over the surface of your model to start painting with the default tool: Brush.

## Painting

### Brush
![Using Brush](docs~/using_brush.gif)

Paint individual triangles (Or quads if auto-detect quad is activated).

### Fill (Ctrl)
![Using Fill](docs~/using_fill.gif)
 
Paint areas with the following modes:
1. Flood: Fill continuous areas of the same color.
2. Replace: Replace all occurrences of the color for the whole model.
3. Element: Applies paint to a mesh section. All connected faces.
4. All: Full repaint the entire model.

### Loop (Ctrl+Shift)
![Using Loop](docs~/using_loop.gif)

Paint along quad loops. Uncheck "Two Ways" if you want the stroke to go only in the direction of the mouse drag. Note that loop only works if the cursor is over a detected quad, and the mouse drag should cross one of its edges.

### Pick (Shift)
This allows you to sample colors directly from the 3D object surface.

### Mirror Cursor Axis
![Using Mirror](docs~/using_mirror.gif)

Mirrors the current tool cursor along a selected axis. 

## Tool Action: Paint | Select <a name="tool-action-paint-select"></a>

### Paint
All strokes, fills and loops works as expected, they paint the polygons with the selected color. 

### Select
On selection mode the tools above will select faces instead of painting it. The selection is highlighted by drawing the faces with orange polylines or shaded faces. Then selection commands can be used.

When in select mode the keyboard state keys will behave as the standard in many other applications:

- **Ctrl** - Add to selection.
- **Shift** - Subtracts from selection.
- **None** - Replace selection.

### Selection commands
- **Clear selection:** Clear any selected faces.
- **Shade selected:** Use shaded polygons vs polylines to highlight the selected faces.
- **Restrict painting to selected:** When checked, any paint strokes would be restricted to the selected faces.
- **Move faces away -X +X:** This temporally detach and move the selected faces away in the X axis. Useful when you want to paint areas of the model that are occluded by other parts of itself. 
- **Move All Back:** Restore any detached and moved faces.

## Undo (Ctrl+Z) / Redo (Ctrl+Y)
Undo and Redo available as buttons. **Note** that Undo/Redo is currently only available for paint operations.

## Save and Save as... <a name="save-and-save-as"></a>
Saves the changes on the asset. DA PolyPaint works with UV channels. Any change to the mesh data is present only in memory unless saved to the asset. 

Recently imported assets like FBX can't be directly overwritten,  therefore DA PolyPaint will ask you to save as a separated asset. Now on your prefab will reference to the new asset created. If later on you wish to restore your original mesh just restore back the reference to the mesh inside your FBX asset.

## Export OBJ/ Import OBJ (EXPERIMENTAL) <a name="export-obj-import-obj"></a>
This new handy feature allows you to quickly export and re-import the mesh for the purpose of quick tweaks with external applications, bypassing Unity's built-in import system.

Intended for minor modifications of the mesh outside Unity, then import the result back directly to the tool to keep the painting process on the same asset without breaking any references.

NOTE: Skinned meshes not supported yet for Export/Import OBJ.


## Utilities
### Remap To Texture
Easily switch between color palettes without disrupting your model look. This feature analyzes a new texture and reassigns UVs to the most similar colors, allowing for seamless transitions between palettes and optimization across multiple models. Ideal for texture consolidation.


## Requirements and Compatibility <a name="requirements-and-compatibility"></a>
- A mesh with a Mesh Filter or Skinned Mesh Renderer component.
- A material with a diffuse texture assigned. (This will act as palette color)

## Keyboard Shortcuts <a name="keyboard-shortcuts"></a>

| shortcut 	| Tool Action: Paint 	| Tool Action: Select |
| --- 		| --- 					|
| *Ctrl* 	| Fill 					| Add to selection 	|
| *Ctrl + Shift* 	| Loop 			| 					|
| *Shift* 			| Pick 			| Subtract from selection |
| *None* 			| Brush 		| Replace selection |



## Additional Notes <a name="additional-notes"></a>
- When the selection changes, the DA Poly Paint window will indicate if it is ready for painting.
- **WARNING!:** After pressing **START PAINT**, the tool tries to constrain the focus to the selection. Trying to force a change in the currently selected object or using other editor features might conflict with DA Poly Paint. Always click **END SESSION** after finishing painting. 

## Fuel DA Poly Paint's Future 🚀 <a name="support-the-project"></a>
Digging DA PolyPaint? Keep the colors flowing by snagging my low-poly characters. You get awesome assets, and DA Poly Paint gets to grow - it's a win-win!

Check out: [City People Mega-Pack](https://assetstore.unity.com/packages/3d/characters/city-people-mega-pack-203329)

Your support helps paint a vibrant future for everyone. 🎨🌈

## Help & Support
| [PolyChat forum](https://github.com/piXelicidio/PolyChat/discussions/categories/scripted-tools) | [direct e-mail](mailto:denys.almaral@gmail.com) | 