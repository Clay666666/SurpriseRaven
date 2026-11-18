extends SceneTree

# Usage: -- <source_manifest_path> [<asset_root> <report_json>] <output_pck_path>
# Files below asset_root are packed at res://<relative path>; the report lists every entry.
var _entries: Array = []

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2 and args.size() != 4:
		push_error("Usage: -- <source_manifest_path> [<asset_root> <report_json>] <output_pck_path>")
		quit(1)
		return

	var source_manifest: String = args[0]
	var output_pck: String = args[args.size() - 1]
	var packer := PCKPacker.new()

	var start_error := packer.pck_start(output_pck)
	if start_error != OK:
		push_error("Failed to start PCK packer: %s" % start_error)
		quit(1)
		return

	if not _add(packer, "res://mod_manifest.json", source_manifest):
		quit(1)
		return

	if args.size() == 4:
		if not _add_tree(packer, args[1], ""):
			quit(1)
			return

	var flush_error := packer.flush()
	if flush_error != OK:
		push_error("Failed to finalize PCK: %s" % flush_error)
		quit(1)
		return

	if args.size() == 4:
		var report := FileAccess.open(args[2], FileAccess.WRITE)
		if report == null:
			push_error("Failed to write PCK report: %s" % args[2])
			quit(1)
			return
		report.store_string(JSON.stringify({"entries": _entries}, "  "))
		report.close()

	print("Packed %s (%d entries)" % [output_pck, _entries.size()])
	quit(0)

func _add(packer: PCKPacker, target: String, source: String) -> bool:
	# Staging checks container bounds. Decode pixels as well: valid headers and
	# lengths do not guarantee a valid compressed image. Never rewrite the bytes.
	var extension := source.get_extension().to_lower()
	if extension in ["png", "webp"]:
		var bytes := FileAccess.get_file_as_bytes(source)
		var image := Image.new()
		var decode_error := image.load_png_from_buffer(bytes) if extension == "png" else image.load_webp_from_buffer(bytes)
		if decode_error != OK or image.is_empty():
			push_error("Image decode failed for %s: %s" % [target, decode_error])
			return false
	elif extension == "mp3":
		var stream := AudioStreamMP3.load_from_buffer(FileAccess.get_file_as_bytes(source))
		if stream == null or stream.get_length() <= 0.0:
			push_error("MP3 decode failed for %s" % target)
			return false
	var error := packer.add_file(target, source)
	if error != OK:
		push_error("Failed to add %s: %s" % [target, error])
		return false
	var file := FileAccess.open(source, FileAccess.READ)
	_entries.append({"path": target, "size": file.get_length() if file else -1})
	return true

func _add_tree(packer: PCKPacker, root: String, relative: String) -> bool:
	var directory := DirAccess.open(root.path_join(relative))
	if directory == null:
		push_error("Asset directory missing: %s" % root.path_join(relative))
		return false
	for name in directory.get_directories():
		if not _add_tree(packer, root, relative.path_join(name) if relative != "" else name):
			return false
	for name in directory.get_files():
		var child := relative.path_join(name) if relative != "" else name
		if not _add(packer, "res://" + child, root.path_join(child)):
			return false
	return true
