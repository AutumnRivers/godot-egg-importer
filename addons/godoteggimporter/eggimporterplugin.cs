#if TOOLS
using Godot;
using System;

[Tool]
public partial class EggImporterPlugin : EditorPlugin
{
	EditorImportPlugin modelImportPlugin;
	EditorImportPlugin animationImportPlugin;

	public override void _EnterTree()
	{
		// Initialization of the plugin goes here.
		modelImportPlugin = new EggModelImporter();
		AddImportPlugin(modelImportPlugin, true);
	}

	public override void _ExitTree()
	{
		// Clean-up of the plugin goes here.
		RemoveImportPlugin(modelImportPlugin);
	}
}
#endif
