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
    * WARNING: Models are imported using a really, really stupid fix. If things break, let me know.
* Importing textures
* Importing collisions

# What Doesn't Currently Work
* Importing animations
    * **TECHNICALLY WORKS**, but is completely broken. Some animations look... okay. Most don't.
    * I do not recommend using this, at all. The ability to import animations is only provided for testing purposes.
    * If you're interested in helping me fix this, please check the [animation importer file](./addons/godoteggimporter/importers/EggAnimationImporter.cs).

# What WILL NOT Work
* Importing Joints ("Skeletons") -- listen, I tried, I really did. It's just impossible to do this from code in Godot.

---

# Installation
**This importer *requires* the .NET build of Godot 4.3 or later, along with .NET 8+!**
* Clone this repo: `$ git clone https://www.github.com/AutumnRivers/godot-egg-importer`
* Place the `/addons` folder in the root folder of your Godot project
* Build your C# project -- *this is an important step!*
* Enable the plugin in `Project Settings -> Plugins -> Godot EGG Importer`

---

# Model Import Options
## Automatically Convert Collisions
If enabled, automatically converts collision groups into `CollisionShape3D`s and creates a `StaticBody3D` if it doesn't exist. If disabled, collision groups are completely ignored.

## Set Materials to Unshaded
Automatically set the `shading_mode` of each material in the model to `ShadingMode.Unshaded`. This is how Panda3D does it, so it's best if you're aiming for that Toontown feel.

## Convert Model Coordinates
Panda3D is a `Z-Up` game engine, Godot is a `Y-Up` game engine. When this is enabled, the importer will move coordinates around to have them properly import into Godot. **You should leave this on, unless you know what you're doing!!!**

## Change Maps Directory
When set, any filepath in the original egg (e.g., `phase_4/maps/fish_palette_3cmla_4.png`) will be changed to be local to the folder set in your project, instead. Default value is `res://maps`.

## Force Import Possible Animations
Forces files that are detected as animations to be imported as models. This is entirely unsupported behavior, and you won't get any assistance if things break.

# Animation Import Options
## Use Blender Layout
Uses the same layout as if the animation were imported through Blender via the `blender-egg-importer`. If disabled, skips the root bundle name, as it has no animation values associated with it.

## Loop Animation
Determines whether or not the animation should import as infinitely looping.

## Apply Corporate Clash Fixes
Corporate Clash animation files have an odd format that breaks in things like importers, but works just fine in-game. When enabled, this will replace some text in the parsed EGG file to make Corporate Clash animations work. **NOTE**: You should not use Corporate Clash files without the express permission of their staff.

## Apply Very Stupid Fix
Some models imported through `blender-egg-importer` can have this oddity where bones have a `_2` tacked onto their names. Enabling this will fix that for animations, but you should leave it disabled, otherwise.