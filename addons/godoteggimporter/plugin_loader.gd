@tool
extends EditorPlugin

var modelImporter : EditorImportPlugin;
var animImporter : EditorImportPlugin;

func _enter_tree() -> void:
	modelImporter = preload("res://addons/godoteggimporter/importers/EggModelImporter.cs").new()
	animImporter = preload("res://addons/godoteggimporter/importers/EggAnimationImporter.cs").new()
	add_import_plugin(modelImporter)
	add_import_plugin(animImporter)
	
func _exit_tree() -> void:
	remove_import_plugin(modelImporter)
	remove_import_plugin(animImporter)
