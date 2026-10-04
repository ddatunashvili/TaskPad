<p align="center">
  <img src="docs/icon.png" width="84" alt="TaskPad icon">
</p>

<h1 align="center">TaskPad</h1>

<p align="center">
  A tiny, fast, VS Code-flavoured editor for plain-text task lists on Windows.<br>
  One portable <code>.exe</code> (under 1 MB). No installer, no account, your files stay <code>.txt</code>.
</p>

<p align="center">
  <a href="https://github.com/ddatunashvili/TaskPad/releases/latest"><b>⬇ Download TaskPad.exe</b></a>
</p>

<p align="center">
  <img src="docs/screenshot.png" alt="TaskPad screenshot" width="880">
</p>

## Why

Notepad is too plain, VS Code is too heavy for a to-do list. TaskPad opens instantly from the Explorer
right-click menu and turns simple markers into checkboxes, badges and colours —
[Better Comments](https://marketplace.visualstudio.com/items?itemName=aaron-bond.better-comments) style —
while the file on disk stays plain text you can open anywhere.

## Smart typing

Type `[]` and it becomes a checkbox. Enter continues the list, Enter twice ends it. Click a box to tick it;
Ctrl+click marks it *in progress*. The status bar tracks how many tasks are done.

<p align="center"><img src="docs/typing.gif" alt="Typing tasks" width="880"></p>

## Keywords picker

Not sure what to type? Hit <kbd>Ctrl</kbd>+<kbd>K</kbd> or the ✦ button and click a marker to apply it to the current line.

<p align="center"><img src="docs/keywords.gif" alt="Keywords picker" width="880"></p>

## Select lines, pick a style

Select several lines and a small toolbar appears: turn them all into tasks, urgent badges, stars,
a numbered list, headings… or clear the markers. Click the same style again to remove it.
<kbd>Tab</kbd> on a line under a task makes it a **subtask** with a smaller checkbox.

<p align="center"><img src="docs/multiline.gif" alt="Multi-line markers and subtasks" width="880"></p>

## Comments

Select words and press <kbd>Ctrl</kbd>+<kbd>M</kbd> (or **💬 Comment** in the toolbar). The text gets a soft highlight;
hover it to read the note, click the bubble to edit or delete. The box is resizable.
Stored as [CriticMarkup](https://github.com/CriticMarkup/CriticMarkup-toolkit) —
`{==text==}{>>note<<}` — so the file is still plain text.

<p align="center"><img src="docs/comments.gif" alt="Adding and editing a comment" width="880"></p>

## Images

<kbd>Ctrl</kbd>+<kbd>V</kbd> a screenshot or drop image files. They are saved to `images/` next to your file and
shown as a small chip (`![](images/…png)` in the file). Hover for a preview, click to open the inspector:
wheel to zoom at the cursor, drag to pan, double-click for fit / 100 %, pixel coordinates and colour,
**📌 pin on top** to keep it as a reference while you type.

<p align="center"><img src="docs/images.gif" alt="Paste an image and inspect it" width="900"></p>

Want big inline thumbnails instead of chips? Set `imagePreviewHeight=160` in `TaskPad.ini`;
then hover a thumbnail to drag-resize it (stored as `![|320](…)`) or remove it.

## Tabs, splits and windows

Drag tabs to reorder them (they slide out of the way). Drop a tab on any edge of an editor to split,
or drop it outside the window to tear it into its own window — and drag it back to merge.

<p align="center"><img src="docs/split.gif" alt="Reorder tabs and split by dragging" width="880"></p>
<p align="center"><img src="docs/tear-out.gif" alt="Tear a tab out into a new window" width="880"></p>

## Syntax

Type these at the start of a line:

| You type | You get |
|---|---|
| `[ ]` or `[]` | clickable checkbox |
| `[x]` | done — dimmed and struck through |
| `[/]` | in progress (<kbd>Ctrl</kbd>+click a box) |
| `[-]` | cancelled (<kbd>Shift</kbd>+click a box) |
| `- [ ]` | markdown-style task |
| `!` | red **urgent** badge |
| `?` | purple *question* badge |
| `TODO:` | amber TODO pill |
| `*` | ★ starred |
| `>` | ❯ next up |
| `<` | ❮ waiting on someone |
| `/` | ✓ finished note |
| `-` | • bullet |
| `1.` | numbered list |
| `#` `##` `###` | headings |
| `Section:` | section title |
| `---` / `===` | horizontal line |
| `// !` | Better Comments prefix also works |
| `  [ ]` (indented) | subtask, smaller box |
| `![](path)` | image chip |
| `{==text==}{>>note<<}` | commented text |

Inline: `@person`, `#tag`, `2026-10-04 14:30`, `` `code` `` and links (<kbd>Ctrl</kbd>+click) are coloured too.

## Keys

| Key | Action |
|---|---|
| <kbd>Enter</kbd> | continue task / bullet / numbered list (twice to end it) |
| <kbd>Ctrl</kbd>+<kbd>Enter</kbd> | toggle task on the line(s), or turn the line into a task |
| <kbd>Tab</kbd> / <kbd>Shift</kbd>+<kbd>Tab</kbd> | indent / outdent list item |
| <kbd>Alt</kbd>+<kbd>↑</kbd>/<kbd>↓</kbd> | move line |
| <kbd>Shift</kbd>+<kbd>Alt</kbd>+<kbd>↓</kbd> | duplicate line |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>K</kbd> | delete line |
| <kbd>Ctrl</kbd>+<kbd>K</kbd> | keywords picker |
| <kbd>Ctrl</kbd>+<kbd>M</kbd> | comment on selection |
| <kbd>Ctrl</kbd>+<kbd>V</kbd> | paste text or an image |
| <kbd>Ctrl</kbd>+<kbd>\\</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>\\</kbd> | split right / down |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>M</kbd> | move tab to a new window |
| <kbd>Ctrl</kbd>+<kbd>F</kbd> / <kbd>Ctrl</kbd>+<kbd>H</kbd> | find / replace |
| <kbd>F5</kbd> / <kbd>Ctrl</kbd>+<kbd>;</kbd> | insert date-time / date |
| <kbd>Ctrl</kbd>+wheel, <kbd>Ctrl</kbd>+<kbd>=</kbd>/<kbd>-</kbd>/<kbd>0</kbd> | zoom |
| <kbd>Alt</kbd>+<kbd>Z</kbd> | word wrap |
| <kbd>Ctrl</kbd>+<kbd>N</kbd>/<kbd>O</kbd>/<kbd>S</kbd>/<kbd>W</kbd>, <kbd>Ctrl</kbd>+<kbd>Tab</kbd> | the usual |
| <kbd>F1</kbd> | cheat sheet |

Files auto-save one second after you stop typing and reload when changed on disk.

## Explorer right-click menu

Open the `⋯` menu → **Add to Explorer right-click menu** (or run `TaskPad.exe --register`).

- **Open with TaskPad** on `.txt`, `.md`, `.todo` and `.log` files
- **New task list (TaskPad)** on folders and folder backgrounds

It is per-user (HKCU), so no admin rights are needed. On Windows 11 the entries live under
**Show more options** (<kbd>Shift</kbd>+<kbd>F10</kbd>). Moved the exe? Register again.
`TaskPad.exe --unregister` removes everything.

## Settings

`TaskPad.ini` is created next to the exe (portable). Options: `font`, `fontSize`, `wordWrap`,
`autoSaveDelayMs`, `indentSize`, `leftMargin`, `imagePreviewHeight` (0 = chips), `commentWidth`, `commentHeight`. Install [Comic Mono](https://dtinth.github.io/comic-mono-font/)
for the intended look; it falls back to Cascadia Mono / Consolas.

## Build

Requires the .NET SDK (any recent version) on Windows. The app targets .NET Framework 4.8, which ships with Windows 10/11,
and embeds [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) so the result is a single file.

```bat
build.cmd
```

Output: `dist\TaskPad.exe`.

## License

[MIT](LICENSE)
