# Research: Organized

Sorts research projects into tech-level tabs and lays each tab out as a tidy, readable tree, in the game's own research window or, with UI Rethemed - Core, a Rethemed research window of its own.

## Features

- Projects are grouped into Primitive, Neolithic, Medieval, Industrial, High and Late Industrial, Spacer, Ultra, Archotech, Anomaly and Miscellaneous tabs.
- Each tab gets a clean, non-overlapping layout with fewer crossing lines. Follow-ups sit to the right of what they depend on, and branches fill the free space beside their prerequisites.
- **Combine All Tabs** puts every project except Anomaly, Gravship and Save Our Ship 2 on the Main tab, with each tech level in its own block from left to right. A gap between the blocks and the lines between eras can each be turned on or off; by default it looks like the vanilla tree, with no gap and every line drawn.
- **Combine Industrial** merges High and Late Industrial back into one Industrial tab.
- **Hide Unavailable Projects** shows only finished projects and those you can start now; **Hide Future Tech Levels** hides unfinished projects above your colony's tech level, and **Hide Finished Projects** hides what you have already researched.
- A project is raised to the tech level of its prerequisites, so a follow-up never sits in an earlier era than what it needs. Its research cost changes to match.
- Tab tooltips show completed projects and research points, and the counts can be shown on the tabs too.
- Empty tabs are hidden.
- Projects caught in a circular dependency are outlined in red and named in the log.
- Schematics can teach projects from every era tab.

## Two research windows

The **Research Window** setting picks which window the Research button opens.

**Classic** is the game's own research window, organized by this mod:

- Cards are tinted by tech level, with configurable colors that can also be turned off.
- The window remembers where you scrolled, and a gear in it opens the mod settings.
- **Full Screen Research Window** makes the window as tall as the screen and fits as many projects into each column as that allows; a button beside the gear toggles it.

**Rethemed** needs UI Rethemed - Core and is a research window of its own in UI Rethemed's look:

- Drag to pan and use the mouse wheel to zoom, with a Fit button, and each tab remembers its view. Full screen by default, with a button in its title bar to toggle it.
- Cards show their era's color and their state at a glance (finished, available, being researched, locked), with up to three unlock icons, techprint and study counters, a progress strip and a hover card listing what the project needs and what it unlocks without further research.
- Lines between projects are smooth curves, and selecting a project lights up its whole prerequisite chain.
- A research queue in a foldable panel on the left: shift-click a project to queue it along with any prerequisites it still needs, drag to reorder, and the next project starts when the current one finishes. Folded, it still shows the queued projects as icons.
- A foldable details panel on the right shows the selected project's requirements met and unmet, what it unlocks, what it leads to, and buttons to research or queue it; a bar along the top shows the current project's progress.
- Search finds projects by name or by anything they unlock, and chips in the window show or hide locked, finished and future-era projects.
- Era bands tint each era's columns, with the era's title pinned along the top, on tabs laid out era by era.
- Research times: roughly how long each project takes to research is shown on its card, hover card and details, the progress bar shows how long the current project has left, and queue rows show when each project should be done, all at the colony's research pace over the last day (saved with the game).
- Cards come in Compact, Normal and Large sizes, with adjustable column and row spacing and a choice of how many projects stack in each column.
- Era colors are picked on UI Rethemed - Core's settings page.

## Compatibility

- Works with Node Research: foundation technologies get a gold mark, and emergence nodes stay beside their branch without cluttering lines. In the Rethemed window, Node Research's vanilla menu button opens it, and a button in its title bar goes back.
- Semi Random Research - Continued and Research Queue open the Rethemed window from their tree buttons, and a button in its title bar goes back. The queue is off while Research Queue is active or Semi Random Research chooses research.
- Loads after the ProgressionLESS suite, so its research lands in the right tabs.
- With VFE Tribals, its Basics tab takes the Primitive projects.
- With Save Our Ship 2, its Ship and Archotech tabs keep their own projects.
- In the Classic window, Semi Random Research's button and the settings gear sit side by side, and Research Icons works alongside, with columns widened to fit its wider cards.
