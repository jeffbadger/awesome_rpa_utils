# Drag & Drop

Left-button drag sequences.

## `DragAndDrop(int startX, int startY, int endX, int endY)`

**Scenario:** Reordering a task in a Kanban-style desktop app by dragging its
card from the "To Do" column into the "In Progress" column.

```csharp
mouse.DragAndDrop(startX: 220, startY: 300, endX: 620, endY: 300);
```

## `DragAndDrop(int startX, int startY, int endX, int endY, int steps, int stepDelayMilliseconds)`

**Scenario:** The same Kanban app runs inside a remote desktop session with
noticeable input lag; the default 30-step drag drops frames and the card snaps
back. Slowing the drag down with more steps and a longer per-step delay fixes it.

```csharp
mouse.DragAndDrop(
    startX: 220, startY: 300,
    endX: 620, endY: 300,
    steps: 80,
    stepDelayMilliseconds: 25); // slower glide for a laggy RDP session
```

## `RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers)`

**Scenario:** In a file-manager grid, the automation has already rubber-band
selected the first batch of files. It now needs to add a second, non-adjacent
group of files to that selection by holding Ctrl while drawing a second
selection rectangle.

```csharp
mouse.DragAndDrop(50, 50, 300, 200);                                  // first selection
mouse.RubberBandSelect(400, 250, 650, 400, ModifierKeys.Control);     // add a second group
```

## `RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers, int steps, int stepDelayMilliseconds)`

**Scenario:** The same Ctrl-drag selection needs to run more slowly over a
laggy remote desktop session, matching the pattern used for the plain
`DragAndDrop` overload above.

```csharp
mouse.RubberBandSelect(400, 250, 650, 400, ModifierKeys.Control, steps: 80, stepDelayMilliseconds: 25);
```

## `DragAndHold(int startX, int startY, int endX, int endY, int holdMilliseconds)`

**Scenario:** Moving a file into a collapsed folder in a tree view: the
target folder only expands to reveal its contents after the dragged item has
hovered over it for about a second. The automation drags to the folder and
holds there before releasing, giving the tree view time to auto-expand.

```csharp
mouse.DragAndHold(startX: 120, startY: 300, endX: 60, endY: 180, holdMilliseconds: 1200);
```
