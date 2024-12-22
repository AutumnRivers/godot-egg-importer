<center>
<h1><img src="./assets/marbles.png"/><br/>
Godot EGG Importer</h1>
<p>A simple import plugin for Panda3D egg files.</p>
</center>

---

`godot-egg-importer` is an [import plugin](https://docs.godotengine.org/en/stable/tutorials/plugins/editor/import_plugins.html) for Godot 4.3+ (.NET) that adds support for [Panda3D Egg files](https://docs.panda3d.org/1.10/python/pipeline/egg-files/index). It's mainly made for importing Toontown models, but can theoretically be used for any egg file made with Panda3D.

This importer makes the process of importing egg files into Godot 1000% faster; no need to go through the middleman of something like Blender anymore. It also includes some features that make importing that much smoother, such as being able to set the `maps` folder to automatically have textures applied to your model.

**NOTE: Modern Toontown servers are ran by unfathomably talented people! I do not condone the usage of this plug-in to use models from Toontown servers without first gaining express permission from their staff.**

---

# What Currently Works
* Importing models
* Importing textures

# What Doesn't Currently Work
* Importing skeletons ("Joints")
* Importing animations
* Importing collisions

---

# Installation
**This importer *requires* the .NET build of Godot 4.3 or later!**
* Clone this repo: `$ git clone https://www.github.com/AutumnRivers/godot-egg-importer`
* Place the `/addons` folder in the root folder of your Godot project
* Build your C# project -- *this is an important step!*
* Enable the plugin in `Project Settings -> Plugins -> Godot EGG Importer`

---

# Import Options
## Force Animation
**RESERVED FOR FUTURE USE**

## Automatically Convert Collisions
**RESERVED FOR FUTURE USE**

## Set Materials to Unshaded
Automatically set the `shading_mode` of each material in the model to `ShadingMode.Unshaded`. This is how Panda3D does it, so it's best if you're aiming for that Toontown feel.

## Convert Model Coordinates
Panda3D is a `Z-Up` game engine, Godot is a `Y-Up` game engine. When this is enabled, the importer will move coordinates around to have them properly import into Godot. **You should leave this on, unless you know what you're doing!!!**

## Change Maps Directory
When set, any filepath in the original egg (e.g., `phase_4/maps/fish_palette_3cmla_4.png`) will be changed to be local to the folder set in your project, instead. Default value is `res://maps`.