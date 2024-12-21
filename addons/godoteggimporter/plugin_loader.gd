@tool
extends EditorPlugin

var modelImporter : EditorImportPlugin;

func _enter_tree() -> void:
	modelImporter = preload("res://addons/godoteggimporter/importers/EggModelImporter.cs").new()
	add_import_plugin(modelImporter)
	
func _exit_tree() -> void:
	remove_import_plugin(modelImporter)
