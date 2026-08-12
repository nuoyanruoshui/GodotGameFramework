# Godot — Version Reference

| Field | Value |
|-------|-------|
| **Engine Version** | 4.7 |
| **Project Pinned** | 2026-06-13 |
| **LLM Knowledge Cutoff** | May 2025 |
| **Risk Level** | HIGH — version is well beyond LLM training data |

## Post-Cutoff Version Timeline

| Version | Release Date | Type |
|---------|-------------|------|
| 4.3 | August 2024 | Feature release (within training data) |
| 4.4 | ~March 2025 | Feature release (edge of training data) |
| 4.5 | ~mid 2025 | Feature release (beyond cutoff) |
| 4.5.2 | March 19, 2026 | Maintenance release |
| 4.6 | January 26, 2026 | Feature release (beyond cutoff) |
| 4.6.1 | February 16, 2026 | Maintenance release |
| 4.6.2 | April 1, 2026 | Maintenance release (current) |
| 4.6.3 | ~May 20, 2026 | Latest stable |

## Key Version-Specific Changes Since Training Cutoff

### 4.6.x
- D3D12 is now the **default** Windows renderer (was Vulkan)
- Jolt Physics is now the **default** 3D physics engine (was GodotPhysics)
- C# AnimationPlayer properties: `String` → `StringName` (source-breaking)
- EditorFileDialog consolidated into base `FileDialog` class
- New modular IK framework built-in
- Delta-encoded PCK patching for binary-diff updates
- OpenXR 1.1 support
- Unique Node IDs (stable across scene reorganization)
- Android: native Gradle builds, device mirroring
- LibGodot: embed Godot as a library

### 4.5.x
- Stencil buffer support (X-ray, outlines, portal effects)
- Shader Baker: pre-compile shaders on export (up to 20× faster loading)
- C# GLTFAccessor types: `int` → `long` (binary + source breaking)
- JSONRPC: `set_scope` → `set_method` (C# source-breaking)
- Bone constraints and modifiers for 3D
- visionOS (Apple Vision Pro) export
- Chunk Tilemap Physics for 2D
- Android: .NET 9 required for C# Android exports

### 4.4.x
- At edge of training data — most APIs should be known to the LLM
- Minor API additions and fixes

## Migration Resources

- [Upgrading from Godot 4.5 to 4.6](https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.6.html)
- [Upgrading from Godot 4.4 to 4.5](https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.5.html)
- [Godot 4.6 Release Blog](https://godotengine.org/releases/4.6/)

## Last Docs Verified

2026-06-13
