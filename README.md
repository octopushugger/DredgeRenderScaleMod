# Dredge RenderScale
Mod to render the game at different internal resolutions without touching the HUD. 

- 0.25-8.0 configurable (capped to maximum of 16384x16384). If your in-game resolution is 2560x1440 your actual max render scale is 6.4x = 16384x9216.

- RenderScale 2.0 = 4x as many pixels rendered, both dimensions multiplied by 2.

- Config file in mod folder

- Catmull-Rom (Bicubic) and Spline64 scaling supported. Spline64 is not recommended but you may like the look more (sharper but typically more ringing and aliasing and much slower compared to Catrom). Unity's default scaling is also supported but it looks terrible. 

- Defaults: 2.0 with Catrom

- Most likely not compatible with other mods that also modify graphics like the VR mod. Untested though.

- Image comparison between with and without mod here: https://slow.pics/c/42TB2OOr
