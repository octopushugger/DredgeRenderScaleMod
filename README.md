# Dredge RenderScale
Mod to render the game at different internal resolutions without touching the HUD. 

- 0.25-8.0 configurable (capped to maximum of 16384x16384). If your in-game resolution is 2560x1440 your actual max render scale is 6.4x = 16384x9216. If you set higher than max it will automatically render at max. 

- RenderScale 2.0 = 4x as many pixels rendered, both dimensions multiplied by 2.

- Config file in mod folder

- Catmull-Rom (Bicubic) and Spline64 scaling supported. Spline64 is not recommended but you may like the look more (sharper but typically more ringing and aliasing and much slower compared to Catrom). Unity's default scaling is also supported but it looks terrible. 

- Defaults: 2.0 with Catrom

- Most likely not compatible with other mods that also modify graphics, like the VR mod. Untested though.

- Image comparison between with and without mod here: https://slow.pics/c/42TB2OOr

- Intended use case is supersampling but if you have a super potato gpu you could in theory use this to render lower than native resolution to increase fps while keeping the HUD sharp. 

Performance on a 3090 with in-game res set to 2560x1440 and otherwise max graphics:

- 0.5 Catrom ~450fps (memory bottlenecked?)

- Mod disabled ~450fps

- 2.0 Catrom ~305fps

- 2.0 Spline64 ~155fps

- 2.5 Catrom ~225fps

- 3.0 Catrom ~165fps

- 6.4 Catrom ~25fps

- 6.4 Spline64 ~12fps

Credits:

Winch Creators

Claude
