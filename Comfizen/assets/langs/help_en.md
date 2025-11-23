# 📘 Full Comfizen User Guide

**Comfizen** is a powerful control environment for ComfyUI, transforming complex JSON workflows into a convenient, customizable graphical interface with support for presets, queues, scripts, and result comparison.

---

## 📋 Table of Contents

1. [Getting Started & Interface](#1-getting-started--interface)
2. [Generation Control & Queue](#2-generation-control--queue)
3. [XY Grid System](#3-xy-grid-system)
4. [Presets & Global Control](#4-presets--global-control)
5. [Wildcards System & Prompting](#5-wildcards-system--prompting)
6. [Gallery & Viewer (Slider Compare)](#6-gallery--viewer-slider-compare)
7. [Inpaint Editor & Drawing](#7-inpaint-editor--drawing)
8. [Interface Designer (UI Constructor)](#8-interface-designer-ui-constructor)
9. [Scripting (Python)](#9-scripting-python)
10. [Settings & Hotkeys](#10-settings--hotkeys)

---

## 1. Getting Started & Interface

### Core Concepts
Comfizen does not replace ComfyUI but acts as a client.
* **Workflow**: A `.json` file in API format (saved in ComfyUI via "Save (API Format)"), to which Comfizen adds an interface layer.
* **Session**: Comfizen automatically remembers the state of all fields (text, sliders, selected models) in a temporary file. Upon restarting the program, you will pick up right where you left off.

### Workflow Tabs
* **Regular Tabs**: Open `.json` files from the `workflows` folder. Changes in them are saved to the session.
* **Virtual Tabs**: If you drag & drop a generated image into the program window, Comfizen opens a "virtual" tab with the settings used to create that image.

### Window Management (Undocking)
You can "undock" any settings group or the queue panel into a separate floating window.
* Click the ❐ button in the group header to move it to a separate window. This is useful for multi-monitor setups.

---

## 2. Generation Control & Queue

### Queue Control Panel
* **Queue Button**: Sends current settings to the generation queue.
* **Infinite Queue (∞)**: If enabled, Comfizen will automatically add new tasks to the queue as old ones complete.
* **Pause/Resume**: Allows pausing task submission to the server (the current task on the server will finish).
* **Stop/Interrupt**: Interrupts generation on the ComfyUI server.
* **Clear Local**: Clears the local waiting queue in Comfizen (does not affect tasks already sent to ComfyUI).

### Queue Manager
Click the list button next to the Queue button to open the detailed manager.
* **View Changes**: For each task in the queue, Comfizen shows *only the differences* from the original state (Diff). You will see exactly which parameters (seed, prompt, model) will be used.
* **Delete**: You can remove a specific task from the waiting list before it is sent.
* **Search**: Filter tasks in the queue by workflow name or parameter values.

---

## 3. XY Grid System

Built-in tool for creating comparison matrices (like in A1111), working on top of any workflow.
*Accessible via the "XY" button next to the Queue button.*

### Axis Setup
You can define variables for the X and Y axes.
* **Source (Field)**: Select any field from the interface (e.g., `CFG`, `Checkpoint`, `Denoise`).
* **Source (Preset Group)**: You can select an entire group of presets as an axis.

### Value Input
* **Manual**: Enter values, one per line.
  * *Example for CFG*: `5`, `7`, `9`.
  * *Example for Checkpoint*: `model_v1.safetensors`, `model_v2.safetensors`.
* **Search**: Use the dropdown search to add values (models, samplers, or preset names).

### Modes & Options
* **Create Grid Image**: Automatically stitches results into a single grid image.
* **Grid Mode**:
  * *Image*: Standard mode for images.
  * *Video*: Creates a "storyboard" from video frames (useful for AnimateDiff/VideoHelper).
* **Limit Cell Size**: Allows reducing the resolution of cells in the final grid to prevent huge file sizes (setting in megapixels).

---

## 4. Presets & Global Control

Comfizen offers a powerful preset system for managing complex settings.

### Preset Types
1. **Snippet**: Saves values of specific fields within a single group.
  * *Example*: "High Quality" preset changes Steps to 50 and Sampler to Euler.
2. **Layout**: A "preset of presets". It stores a list of other snippets rather than values.
  * *Example*: "4k Realistic" layout activates the "Resolution 4k" snippet and the "Realistic Style" snippet.

### Layer Management (Active Layers)
Comfizen uses a layer system. You can apply multiple presets simultaneously.
* **Indication**: Fields changed by a preset are temporarily highlighted.
* **Conflicts**: If a new preset changes the same field as an old one, the old layer is automatically disabled.
* **Status**: If you manually change a value after applying a preset, the layer is marked as "Modified" in orange. You can "Reapply" the layer to reset manual edits.

### Global Presets
Located in the "Global Settings" panel. Allow switching the state of the **entire workflow** (multiple groups simultaneously) with one click.

---

## 5. Wildcards System & Prompting

### Syntax
* **Files**: `__filename__` — picks a random line from a file in the `wildcards` folder.
  * Supports nested folders: `__styles/anime__`.
  * Supports Glob patterns: `__styles/*__` (selection from all files in the folder).
* **Dynamic Choice**: `{cat|dog|bird}` — randomly selects one option.
* **Weights**: `(text:1.2)` — standard ComfyUI syntax.
  * *Hotkeys*: `Ctrl + Up/Down` on selected text adjusts weight.
* **Quantifiers**:
  * `{2$$apple|banana|cherry}` — select 2 unique options.
  * `{1-3$$apple|banana|cherry}` — select 1 to 3 options.
  * `{2$$ and $$apple|banana|cherry}` — select 2 options separated by " and ".
* **Nesting**: Any depth of nesting: `{photo of a {cat|dog}|__artworks__}`.

### Wildcard Browser
Click the `...` button next to a prompt field or use the context menu to open the wildcard browser. It allows searching files, viewing content, and inserting into text. It can also pack/unpack wildcards to YAML.

---

## 6. Gallery & Viewer (Slider Compare)

### Gallery (Outputs)
* **Filtering**: By type (Photo/Video), Status (Saved/Unsaved), Search query.
* **Sorting**:
  * *Newest/Oldest*: By date.
  * *Similarity*: Groups images by visual similarity (Perceptual Hash). Useful for finding duplicates or seed variations.
* **Actions**: Select multiple images for bulk saving or deleting (`Delete`).

### Fullscreen Viewer
* **Video**: Built-in player with scrubbing and looping support.
* **Metadata**: Displays resolution, progress (index in series).

### Slider Compare
Select **two** images in the gallery and click the **"Compare"** button.
* Opens a window with a "Before/After" slider.
* Works for both images and **video** (synchronous playback).
* Supports Drag&Drop files from explorer to the left or right side for comparison with external references.

---

## 7. Inpaint Editor & Drawing

Automatically enabled for `ImageInput` or `MaskInput` fields.

### Modes
* **Mask**: Drawing B/W mask for inpainting (red indicator).
* **Sketch**: Drawing colored sketch (for ControlNet Scribble/Canvas). Sends color to ComfyUI.

### Tools
* **Brush**: Size changed by slider or `Ctrl + Mouse Wheel`.
* **Eraser**: Right mouse button.
* **Eyedropper**: Tool to pick color from image (for Sketch mode).
* **Feather**: Blurs mask edges before sending.
* **Paste**: `Ctrl+V` pastes image from clipboard directly into editor.

---

## 8. Interface Designer (UI Constructor)

Tool for creating the shell (`.json` workflow for Comfizen).
**Input**: API JSON file from ComfyUI.

### Layout & Global Tabs
In the **Layout** tab, you can organize your workflow into Tabs and Groups.

* **Global Tabs**: High-level navigation located at the top of the main window. Ideal for separating large workflows into logical steps (e.g., "Generation", "Upscaling", "Settings").
  * **Create**: Click the `+` button in the top-right of the layout panel.
  * **Assign**: Drag and drop Groups from the "Unassigned" list or from other tabs into the desired tab area.
  * **Unassigned**: Groups not placed in any tab will appear in a default list, but organizing them into tabs keeps the UI clean.

* **Groups**: Collapsible blocks containing fields. Can be moved between tabs via drag-and-drop.
* **Sub-tabs**: Inside a specific Group, you can create sub-tabs (e.g., "Basic", "Advanced") to save vertical space.

### Field Types & Capabilities
1. **Any**: Universal field (string, number) with smart file insertion.
  * **Default**: Pasting an image (`Ctrl+V` or Drag&Drop) adds its Base64 content. Pasting other files adds their paths.
  * **With `Shift`**: To paste the path to an image (instead of its content), use `Shift + Drag&Drop` or `Ctrl+Shift+V`.
2. **SliderInt / SliderFloat**: Sliders. Configurable Min/Max/Step/Precision.
3. **ComboBox**: Dropdown list. Item format: `Display::api_value` or just `value`.
4. **Seed**: Field with lock/unlock button and random generation.
5. **Model**: Dropdown list of files. Requires selecting `Model Subtype` (checkpoints, loras, etc.).
6.  **Inpaint (Image/Mask)**: Graphic editor. Sends Base64.
7. **Node Bypass**: Checkbox that physically disables/enables nodes in the graph (Bypass).
  * *Important*: When using Bypass, the system snapshots connections to correctly restore them when the node is enabled.
8. **Script Button**: Button to run a Python script (Action).
9. **Markdown**: Powerful text block for instructions or navigation.
  * **Custom Colors**: Use syntax `[Text](color:red)` or `[Text](color:#FF5500)` to style text.
  * **Workflow Links**: Use syntax `[Link Text](wf://relative/path/to/workflow.json)` to create clickable links that open other workflows within Comfizen.
  * **Standard Markdown**: Supports headers (`#`), lists (`-`), bold (`**`), and code blocks.
10. **Label / Separator**: Design elements.

---

## 9. Scripting (Python)

Built-in IronPython engine for automation.

### The `ctx` Object
Available in all scripts:
* `ctx.prompt`: Dictionary (JSON) of the current API request. Can read and modify parameters.
  * *Example*: `ctx.prompt['3']['inputs']['seed'] = 12345`
* `ctx.state`: Dictionary for storing data between runs (within a session).
* `ctx.settings`: Access to application settings.
* `ctx.log(msg)`: Output to Comfizen console.
* `ctx.queue(prompt)`: Queue a task.
* `ctx.output`: (Only in `on_output_received` hook) Result object (image/path).

### Hooks
Automatic triggers:
1. `on_workflow_load`: When workflow is opened.
2. `on_queue_start`: When Queue button is pressed.
3. `on_before_prompt_queue`: Before sending each task (ideal for dynamic prompt changes).
4. `on_output_received`: When an image is received.
5. `on_queue_finish`: When a single task is completed.
6. `on_batch_finished`: When the entire queue is empty.

---

## 10. Settings & Hotkeys

### Global Settings
* **Server Address**: ComfyUI IP and port.
* **Paths**: Folders for saving results and sessions.
* **Save Format**: PNG (with metadata), JPEG, WebP. Option to strip Base64 from files to save space.
* **Slider Defaults**: Rules for auto-detecting slider ranges in the Designer.
  * Format: `NodeClass::FieldName=Min Max Step Precision`.

### Hotkeys
| Keys | Context | Action |
| :--- | :--- | :--- |
| **Ctrl + Enter** | Everywhere | Start Queue |
| **Ctrl + G** | Everywhere | Group Navigation Window ("Go to...") |
| **Ctrl + V** | Fields / Inpaint | Paste image or text |
| **NumPad 4 / 6** | Fullscreen | Previous / Next image |
| **NumPad 5** | Fullscreen | Save image to Saved folder |
| **Esc** | Fullscreen | Exit |
| **Ctrl + Wheel** | Inpaint | Brush size |
| **Q** | Inpaint | Toggle Mask / Sketch |
| **Delete** | Gallery | Delete selected files |
| **Ctrl + Up/Down**| Text | Adjust token weight `(text:1.1)` |