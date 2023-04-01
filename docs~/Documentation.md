# DA Poly Paint Documentation

## Table of Contents
1. [Introduction](#introduction)
2. [Installation](#installation)
3. [Features](#features)
4. [Using DA Poly Paint](#using-da-poly-paint)
5. [Requirements and Compatibility](#requirements-and-compatibility)
6. [Keyboard Shortcuts](#keyboard-shortcuts)
7. [Additional Notes](#additional-notes)

## 1. Introduction <a name="introduction"></a>
DA Poly Paint is a powerful 3D Polygon Painting tool for Unity, offering an alternative to pX Poly Paint for 3ds Max. With DA Poly Paint, you can easily customize your low-poly models directly within the Unity environment, saving time and effort on traditional UV mapping and texturing tasks.

## 2. Installation <a name="installation"></a>
To install DA Poly Paint, follow these steps:

1. Download or clone the code from the GitHub repo: https://github.com/piXelicidio/DAPolyPaint
2. Place the downloaded code anywhere inside your Unity project assets.
3. Access the tool via the Unity Main Menu: DA-Tools > Poly Paint

## 3. Features <a name="features"></a>
- **Brush**: Paint polygons; auto-detect quads.
- **Mirror Cursor**: Enable symmetrical painting.
- **Loop**: Detect quad loops.
- **Pick**: Pick colors directly from the 3D object.

## 4. Using DA Poly Paint <a name="using-da-poly-paint"></a>
### 4.1 Getting Started
1. Select any mesh object in the scene.
2. Click 'START PAINT MODE' in the DA Poly Paint window.
3. Select a color by clicking the texture box.
4. Use the Brush, Fill, Loop, and Pick tools to paint.

### 4.2 Painting
- Use the Brush tool to paint individual polygons.
- Use the Fill tool to apply the selected color to an entire object.
- Use the Loop tool to paint along quad loops.
- Use the Pick tool to sample colors directly from the 3D object.

## 5. Requirements and Compatibility <a name="requirements-and-compatibility"></a>
- A mesh with a Mesh Filter or Skinned Mesh Renderer component.
- A material with a diffuse texture assigned.

## 6. Keyboard Shortcuts <a name="keyboard-shortcuts"></a>
- **Ctrl**: Fill
- **Ctrl + Shift**: Loop
- **Shift**: Pick

## 7. Additional Notes <a name="additional-notes"></a>
- When the selection changes, the DA Poly Paint window will indicate if it is ready for painting.
- After pressing 'START PAINT MODE', the focus is constrained to the selection and the painting tool takes control of the editor.
- When modifications are saved, a new mesh asset is created if necessary. Imported 3D models in FBX or OBJ format cannot be directly modified, so an editable copy is required.
