# Unity Store Description
## Summary
Paint polygons directly on low-poly 3D models using color palettes, optimize workflow, enhance visuals, and boost performance with UV data editing in Unity
## Description
DA Poly Paint is an easy-to-use Polygon Painting tool for Unity. It allows you to customize your low-poly models directly within the editor, saving time and effort on traditional UV mapping and texturing tasks.

**Getting Started**
1. Select any mesh object in the scene.
2. Click 'START PAINT MODE' in the DA Poly Paint window.
3. Select a color by clicking the texture box.
4. Use the Brush, Fill, Loop, and Pick tools to paint.

**Painting**
- Use the Brush tool to paint individual polygons or quads if auto-detect quad is activated.
- Use Full Repaint button to apply the selected color to the entire object.
- Use the Fill tool to paint continuous areas of the same color.
- Use the Loop tool to paint along quad loops.
- Use the Pick tool to sample colors directly from the 3D object.

## Keyboard Shortcuts
- **Ctrl:** Fill
- **Ctrl + Shift:** Loop
- **Shift:** Pick

## Support

Please note that this tool is in the early stages; some bugs are expected. 
Feedback is welcome at GitHub: https://github.com/piXelicidio/DAPolyPaint/issues

## Technical details
- **Requierements:** Any static or skinned mesh with diffuse texture in the material.
- When the selection changes, the DA Poly Paint window will  indicate if it is ready for painting.
- After pressing 'START PAINT MODE', the focus is constrained to the selection and the painting tool takes control of the editor.
- When modifications are saved, a new mesh asset is created if necessary. Imported 3D models in FBX or OBJ format cannot be directly modified, so an editable copy is required.



