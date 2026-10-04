<p align="center">
  <img src="docs/icon.png" width="84" alt="TaskPad icon">
</p>

<h1 align="center">TaskPad</h1>

<p align="center">
  A tiny, fast, VS Code-flavoured editor for plain-text task lists on Windows.<br>
  One portable <code>.exe</code> (under 1 MB). No installer, no account, your files stay <code>.txt</code>.
</p>

<p align="center">
  <a href="https://github.com/ddatunashvili/TaskPad/releases/latest/download/TaskPad-Setup.exe"><b>⬇ Install (TaskPad-Setup.exe)</b></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/ddatunashvili/TaskPad/releases/latest/download/TaskPad.exe">Portable (TaskPad.exe)</a>
</p>

<p align="center">
  <img src="docs/screenshot.png" alt="TaskPad screenshot" width="880">
</p>

## Install

**Installer** — run `TaskPad-Setup.exe`. It installs for your user only (no admin) to
`%LOCALAPPDATA%\Programs\TaskPad`, adds a Start Menu shortcut, optionally a Desktop shortcut, the Explorer
right-click entries and an **Open with** entry for `.txt`, `.md`, `.todo`, `.log`. Uninstall from
**Settings → Apps** like any other app. Running the setup again updates in place; if TaskPad is open, the setup offers to
close it (tabs and unsaved text are kept) and reopens it afterwards.

Silent install / uninstall: `TaskPad-Setup.exe --install --quiet` and
`%LOCALAPPDATA%\Programs\TaskPad\TaskPad.exe --uninstall --quiet`.

**Portable** — just run `TaskPad.exe` from anywhere (settings live next to it).

Both are the same program and both update themselves.

## First run

TaskPad opens with a **Welcome** page: start a note, open a file or folder, recent files, a 2-minute interactive
**tour** note, handy keys, and dark/light + code colours. Bring it back anytime with `⋯` → **Welcome**, or tick
*Show this page when TaskPad starts*.

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

### Google Docs-style margin

Comments show in a panel on the right by default; the right-panel icon in the tab bar (or `⋯` → **Comments panel**) opens/closes it — when closed, hover the highlighted text instead.
Every thread becomes a card aligned with its line; drag the panel's left edge to resize it. Reply in place (Enter to send), edit ✎ or delete ✕ single messages,
**✓ Resolve** to remove the thread. Your name comes from `author=` in the ini (default: Windows user name).

<p align="center"><img src="docs/margin-comments.png" alt="Comments in the right margin" width="880"></p>

## Folders (VS Code-style explorer)

Open a whole project folder: `⋯` → **Open Folder…** (Ctrl+Shift+O), right-click any folder in Windows →
**Open folder in TaskPad**, or `TaskPad.exe C:\path\to\folder`. The left sidebar (Ctrl+B, or the folder icon
in the status bar) shows files and folders with type icons (`.task`, `.txt`, Markdown, images, PDF, JS/TS/C#/JSON…)
and updates as files change on disk. Click a file to open it (images open in the viewer, PDFs and other binaries in
their default app). Right-click for **New File** (no extension = `.task`), **New Folder**, **Rename** (F2),
**Delete** (to the Recycle Bin), **Reveal in File Explorer**, **Copy Path / Relative Path**, **Close Folder** (also the ✕ in the
sidebar header). Drag the sidebar edge to resize. The two panel icons at the right of the tab bar open/close the explorer
(left) and the comments panel (right).

Each tab also has a small folder button next to ✕ that opens the file's folder in Windows Explorer.

## Code

Fenced blocks in notes (```` ```python ```` … ```` ``` ````, also `~~~`) get real syntax highlighting — task markers,
images and `#` headings are ignored inside them, and Enter after an opening fence adds the closing one. Code files
(`.py .js .ts .cs .java .kt .swift .go .rs .c/.cpp .php .rb .sh .ps1 .bat .sql .json .yaml .toml .ini .html .xml .css
.lua .dart .r .scala .vb Dockerfile Makefile .diff` …) open as code with language highlighting.
`⋯` → **Code highlight theme…**: Auto (One Dark / GitHub Light), One Dark, Dracula, Monokai, Nord, GitHub Dark,
GitHub Light, One Light, Solarized Light.

## Light and dark

Dark by default. `⋯` → **Light theme** switches every window instantly (or `theme=light` in `TaskPad.ini`).

## Colours and links

Colour codes like `#22C55E`, `#fff` or `#22D3EE80` get a live swatch. <kbd>Ctrl</kbd>+click a colour code or a link
to copy it — a toast confirms, and for links offers **Open ↗**.

## Markdown and plain text

`⋯` → **Import Markdown…** converts `- [ ]` / `- [x]` tasks and `*` bullets into TaskPad syntax in a new tab.
**Export as Markdown…** writes standard Markdown (GitHub task lists, ⚠/❓/⭐ markers, comments as footnotes);
**Export as plain text…** writes a clean `.txt` with ☐ ☑ boxes and no markup.

## Export to PDF

`⋯` → **Export to PDF…** or <kbd>Ctrl</kbd>+<kbd>P</kbd>. Produces a clean, print-friendly PDF: checkboxes, badges,
headings, colour swatches, embedded images, and comments as numbered notes. Uses Microsoft Edge (built into Windows)
in the background — nothing to install.

<p align="center"><img src="docs/pdf-export.png" alt="Exported PDF" width="560"></p>

## `.task` notes — one file with everything

A `.task` file is a whole note in one file: the text **and** its pasted images (and comments). Double-click it and
it opens in TaskPad — the installer (or first run) makes TaskPad the default app for `.task`, with its own icon,
and adds **New → TaskPad note** to Explorer. New notes save as `.task` by default; pick `.txt` in the Save dialog
for a plain text file. Save an existing `.txt` as `.task` and every image it references is packed in.

`⋯` → **Export as .task (with images)…** writes a self-contained copy of any note. Drop `.txt`, `.task` or
`.md` files onto the window to open them (PDFs open in your PDF viewer).

Pasted images are written into the `.task` file immediately; while a note is open it is unpacked to a temp folder
that is removed when you close it. Under the hood it is a ZIP archive (`note.txt` + `images/`), so nothing is locked in: rename it to `.zip` to look inside.

## Images

<kbd>Ctrl</kbd>+<kbd>V</kbd> a screenshot or drop image files. In a `.task` note they are stored inside the file; in a `.txt` note they
go to `%LOCALAPPDATA%\TaskPad\images` (no folders appear next to your notes). Shown as a small chip (`![](images/…png)` in the file). Hover for a preview, click to open the inspector:
wheel to zoom at the cursor, drag to pan, double-click for fit / 100 %, pixel coordinates and colour,
**📌 pin on top** to keep it as a reference while you type. **◀ ▶** (or ←/→) step through the note's images (or the
folder's, when opened from the sidebar). **✂ Crop** (C): drag the area to keep, Enter to apply, Ctrl+Z to undo;
**Ctrl+S** saves (images inside a `.task` note are repacked into it), **Ctrl+Shift+S** saves a copy.

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
| `#` … `######` | headings h1–h6 |
| `Section:` | section title |
| `---` / `===` | horizontal line |
| `// !` | Better Comments prefix also works |
| `  [ ]` (indented) | subtask, smaller box |
| `![](path)` | image chip |
| `{==text==}{>>note<<}` | commented text |

**Right-click** any checkbox or icon to switch that line to another keyword, remove it, or type your own.
**Double-click** it to show the raw code (`[ ]`, `!`, `TODO:` …) and edit it directly — e.g. type `x` to turn `[ ]` into `[x]`.
Heading `#` marks stay hidden except on the line you're editing.

**Deadlines:** add `due:2026-10-10`, `due:2026-10-10 18:00` or `due:18:00` to any line — it shows as a live countdown
pill (`⏰ Fri 10 Oct 18:00 · in 3d 4h`): green under a week, amber under a day, red when overdue, grey once the task is ticked.
Type `due:+2h`, `+3d`, `+1w`, `+1mo`, `tomorrow` or `friday` and press space — it turns into the date. A toast reminds you
when a deadline arrives, and the status bar counts overdue tasks.

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
| <kbd>Ctrl</kbd>+<kbd>P</kbd> | export to PDF |
| <kbd>Ctrl</kbd>+click | copy colour code / link |
| <kbd>Ctrl</kbd>+<kbd>V</kbd> | paste text or an image |
| <kbd>Ctrl</kbd>+<kbd>\\</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>\\</kbd> | split right / down |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>M</kbd> | move tab to a new window |
| <kbd>Ctrl</kbd>+<kbd>F</kbd> / <kbd>Ctrl</kbd>+<kbd>H</kbd> | find / replace |
| <kbd>F5</kbd> / <kbd>Ctrl</kbd>+<kbd>;</kbd> | insert date-time / date |
| <kbd>Ctrl</kbd>+wheel, <kbd>Ctrl</kbd>+<kbd>=</kbd>/<kbd>-</kbd>/<kbd>0</kbd> | zoom |
| <kbd>Alt</kbd>+<kbd>Z</kbd> | word wrap |
| <kbd>Ctrl</kbd>+<kbd>N</kbd>/<kbd>O</kbd>/<kbd>S</kbd>/<kbd>W</kbd>, <kbd>Ctrl</kbd>+<kbd>Tab</kbd> | the usual |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>T</kbd> | reopen last closed file (↺ button lists them all) |
| <kbd>Ctrl</kbd>+<kbd>B</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd> | toggle explorer / open folder |
| <kbd>F1</kbd> | cheat sheet |

Files auto-save one second after you stop typing (toggle with `⋯` → **Auto save**, or `autoSave=false`) and reload when changed on disk.

**Nothing is lost on close.** TaskPad remembers every window, split, tab and cursor, and keeps unsaved and
untitled text in a `session` folder (saved continuously, so it even survives a crash). Close the app without
being asked anything; next time everything is back exactly as you left it. Turn off with `restoreSession=false`.

## Updates

TaskPad updates itself in the background: at start-up and every hour it checks GitHub, downloads a new `TaskPad.exe`,
verifies its SHA-256 against the release notes and swaps it in place. A small toast offers **Restart**; otherwise the new
version starts the next time you open TaskPad (your tabs and unsaved text always come back). Prefer to be asked? Untick
`⋯` → **Auto-install updates** (or `autoUpdate=ask`); `autoUpdate=off` disables checks.
(Needs write access to the folder the exe is in; otherwise it links to the download page.)

## Explorer right-click menu

Open the `⋯` menu → **Add to Explorer right-click menu** (or run `TaskPad.exe --register`).

- **Open with TaskPad** on `.txt`, `.md`, `.todo` and `.log` files
- **New task list (TaskPad)** on folders and folder backgrounds

It is per-user (HKCU), so no admin rights are needed. On Windows 11 the entries live under
**Show more options** (<kbd>Shift</kbd>+<kbd>F10</kbd>). Moved the exe? Register again.
`TaskPad.exe --unregister` removes everything.

## Settings

`TaskPad.ini` is created next to the exe (portable). Options: `font`, `fontSize`, `wordWrap`,
`autoSaveDelayMs`, `indentSize`, `leftMargin`, `imagePreviewHeight` (0 = chips), `commentWidth`, `commentHeight`, `commentsMode` (`hover`/`margin`),
`commentMarginWidth`, `author`, `autoUpdate` (`ask`/`auto`/`off`), `restoreSession`, `autoSave`,
`markerSpacing` (extra px around task/icon lines and headings; `0` = compact), `theme` (`dark`/`light`), `codeTheme`. Install [Comic Mono](https://dtinth.github.io/comic-mono-font/)
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
