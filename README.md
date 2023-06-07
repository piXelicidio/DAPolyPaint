# DA Poly Paint for Unity

- [Introduction](#introduction)
- [Installation](#installation)
- [Using DA Poly Paint](#using-da-poly-paint)
- [Requirements and Compatibility](#requirements-and-compatibility)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Additional Notes](#additional-notes)

## Introduction <a name="introduction"></a>

[![Youtube Video](docs~/youtubeThumbnail.jpg)*Watch YouTube Video*](https://www.youtube.com/watch?v=wEDbnaEky0Y)

DA Poly Paint is an easy to use Polygon Painting tool for Unity, offering an alternative to [pX Poly Paint for 3ds Max](https://github.com/piXelicidio/pxMaxScript/tree/master/PolyPainter). With DA Poly Paint, you can easily customize your low-poly models directly within the Unity environment, saving time and effort on traditional UV mapping and texturing tasks.

## Installation from Unity Store

Get it here: [DA PolyPaint - Low Poly Customizer](https://assetstore.unity.com/packages/tools/painting/da-polypaint-low-poly-customizer-251157)

## Installation (from GitHub) <a name="installation"></a>

1. Download or clone the code from the GitHub repo: https://github.com/piXelicidio/DAPolyPaint
2. Place the downloaded code anywhere inside your Unity project assets.
3. Access the tool via the Unity Main Menu: **Tools** > **DA** > **Poly Paint**

## Using DA Poly Paint <a name="using-da-poly-paint"></a>
### Getting Started
1. Select any mesh object in the scene.
2. Click **'START PAINT MODE'** in the DA Poly Paint window.
3. Select a color by clicking the texture box.
4. Use the Brush, Fill, Loop, and Pick tools to paint.

### Painting
- Use the **Brush** tool to paint individual polygons. Or quads if auto-detect quad is activated.
- Use **Full Repaint** button to apply the selected color to the entire object.
- Use **Fill** tool to paint continous areas of the same color.
- Use the **Loop** tool to paint along quad loops.
- Use the **Pick** tool to sample colors directly from the 3D object.

## Requirements and Compatibility <a name="requirements-and-compatibility"></a>
- A mesh with a Mesh Filter or Skinned Mesh Renderer component.
- A material with a diffuse texture assigned acting as a palette color.

## Keyboard Shortcuts <a name="keyboard-shortcuts"></a>
- **Ctrl**: Fill
- **Ctrl + Shift**: Loop
- **Shift**: Pick

## Additional Notes <a name="additional-notes"></a>
- When the selection changes, the DA Poly Paint window will indicate if it is ready for painting.
- After pressing **'START PAINT MODE'**, the focus is constrained to the selection and the painting tool takes control of the editor.
- When modifications are saved, a new mesh asset is created if necessary. Imported 3D models in FBX or OBJ format cannot be directly modified, so an editable copy is required.
