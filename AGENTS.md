# Global Codex instructions

## RimWorld mod development

Apply this section only to RimWorld mod work under `C:\Users\crues\source\repos`. A normal mod repository contains `About\About.xml` with a `ModMetaData` element. `_Branding` and `Archive` are support containers, not deployable mods themselves. `Genesis` is the retired suite hub and is not deployable even if it contains an `About` folder.

### Ownership and repository layout

- Treat each active Git repository as the authoritative source. Never treat a Steam deployment folder as a repository mirror.
- The mod repositories are independent sibling Git repositories. Run Git operations in each repository separately when work spans several mods.
- `Genesis` is the project hub for the Genesis suite. Its member repositories are `Genesis Core`, `Genesis Attire`, `Genesis Drugs`, `Genesis Factories`, `Genesis Furniture`, `Genesis Kitchen`, `Genesis Mod Configs`, `Genesis Production`, `Genesis Robotics`, `Genesis Storage`, `Genesis The Beginning`, and `Genesis Temperature`.
- Genesis suite compatibility policy and shared documentation live in `Genesis`. Keep `SUPPORTED_MODS.md` and `genesis-compatibility-ledger.html` synchronized when compatibility patches are added, removed, or moved.
- `_Branding` contains shared Workshop preview assets and generation tooling. It controls appearance; Codex's deployment workflow controls what ships.
- Do not use, recreate, or require `_ModKit`, `modkit.json`, `RIMWORLD_MODKIT`, `Publish-Mod`, `Deploy-Mod`, or `Test-AllMods`. Build and deployment are repository-local operations performed by Codex.

### Local paths and build defaults

- RimWorld root: `C:\Program Files (x86)\Steam\steamapps\common\RimWorld`, overridable with `RIMWORLD_DIR` or the MSBuild `RimWorldRoot` property.
- Managed assemblies: `<RimWorldRoot>\RimWorldWin64_Data\Managed`.
- Harmony workshop item `2009463077`: `C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll`, overridable with `HARMONY_DLL` or the MSBuild `HarmonyDll` property.
- Local deployment root: `<RimWorldRoot>\Mods`.
- Default game version: `1.6`.
- Repository-local `Directory.Build.props` and `Directory.Build.targets`, when present, own their build defaults and assembly-copy behavior. Do not introduce dependencies on sibling tooling directories.

### Deployment profiles

Use the following central profiles as the authoritative mapping from repository to Steam folder and allowed top-level runtime content. Items are comma-separated and names are exact.

- For a listed profile, resolve the target as the exact named child of the local deployment root. If it exists, verify that its `About\About.xml` package ID matches the repository before modifying it. If it does not exist, the profile authorizes creating that exact folder during deployment.

| Repository | Steam folder | Allowed runtime content |
| --- | --- | --- |
| `Archive\Progression-Kitchen` | `Progression Kitchen` | `About`, `Defs`, `LoadFolders.xml`, `Mods and Shit`, `Patches`, `Textures` |
| `AutoHuntTweaks` | `Auto Hunt Tweaks` | `1.6`, `About`, `Languages`, `Textures` |
| `AutoNameBabies` | `Auto Name Babies` | `1.6`, `About`, `Languages` |
| `BasicDropdownsReplaceStuffPatch` | `Basic Dropdowns Replace Stuff Patch` | `1.6`, `About` |
| `Colony Epitath` | `Colony Epitaph` | `1.6`, `About`, `Languages` |
| `CruesoesFixes` | `Genesis Fixes` | `1.6`, `About`, `LoadFolders.xml`, `Mods` |
| `DefaultWhiteLights` | `Default White Lights` | `1.6`, `About`, `Languages` |
| `DevModeHotkey` | `Dev Mode Hotkey` | `1.6`, `About`, `Common` |
| `DivisionOfLabor` | `Division of Labor` | `1.6`, `About` |
| `FavoriteColorPicker` | `Favorite Color Picker` | `1.6`, `About`, `Languages` |
| `GenderedMoralGuideTitles` | `Gendered Moral Guide Titles` | `1.6`, `About`, `Defs` |
| `Genesis Attire` | `Genesis Attire` | `1.6`, `About`, `LoadFolders.xml`, `Mods`, `Textures` |
| `Genesis Core` | `Genesis Core` | `1.6`, `About`, `Defs`, `Languages`, `LoadFolders.xml`, `Mods`, `Patches` |
| `Genesis Drugs` | `Genesis Drugs` | `About`, `Defs`, `LoadFolders.xml`, `Mods`, `Patches` |
| `Genesis Factories` | `Genesis Factories` | `About`, `Defs`, `LoadFolders.xml`, `Mods`, `Patches` |
| `Genesis Furniture` | `Genesis Furniture` | `1.6`, `About`, `LoadFolders.xml`, `Textures` |
| `Genesis Kitchen` | `Genesis Kitchen` | `About`, `Defs`, `LoadFolders.xml`, `Mods`, `Patches`, `Textures` |
| `Genesis Mod Configs` | `Genesis Mod Configs` | `About`, `Defs`, `Settings` |
| `Genesis Production` | `Genesis Production` | `About`, `Defs`, `LoadFolders.xml`, `Mods`, `Patches` |
| `Genesis Robotics` | `Genesis Robotics` | `1.6`, `About`, `Defs`, `LoadFolders.xml`, `Mods`, `Patches` |
| `Genesis Storage` | `Genesis Storage` | `1.6`, `About`, `Defs`, `LoadFolders.xml`, `Patches` |
| `Genesis Temperature` | `Genesis Temperature` | `About`, `LoadFolders.xml`, `Mods`, `Patches` |
| `Genesis The Beginning` | `Genesis The Beginning` | `About`, `Defs` |
| `IdeologyReformation` | `IdeologyReformation` | `1.6`, `About`, `Languages`, `LoadFolders.xml`, `Mods` |
| `ResearchAuto` | `Research Auto` | `1.6`, `About` |
| `ResearchInflation` | `Research Inflation` | `1.6`, `About` |
| `ResearchOrganized` | `Research Organized` | `1.6`, `About` |
| `ResearchTotal` | `Research Total` | `1.6`, `About` |
| `RimWorld-ReplaceStuff` | `Replace Stuff - Continued` | `1.1`, `1.2`, `1.3`, `1.4`, `1.5`, `1.6`, `About`, `Defs`, `Languages`, `LoadFolders.xml`, `News`, `Patches`, `Textures`, `LICENSE.txt` |
| `SaveAndQuit` | `Save and Quit` | `1.6`, `About`, `Languages` |
| `SaveOnNegativeEvent` | `Save on Negative Event` | `1.6`, `About` |
| `SemiRandomResearchProgressionContinued` | `Semi Random ResearchP-Fork Continued` | `1.6`, `About`, `Languages`, `Textures` |
| `Vanilla UI+` | `Vanilla UI+` | `1.6`, `About`, `Defs`, `LICENSE`, `Languages`, `Patches`, `Textures` |
| `VanillaOutpostsExpandedTweaks` | `Vanilla Outposts Expanded - Tweaks` | `1.6`, `About` |

For an unlisted RimWorld mod repository:

- Read `packageId` from the repository's `About\About.xml`.
- Compare it case-insensitively with `packageId` values in `About\About.xml` under immediate children of the local deployment root.
- Require exactly one match before modifying an existing deployment. If there is no match or more than one match, do not guess or modify the deployment root; ask the user for the exact target and runtime allowlist.

### Deployment safety and completion

- Deploy only the profile's allowed runtime content. Never deploy repository-only files or directories, including `.git`, `.agents`, `.codex`, `.claude`, `Source`, `Tests`, `docs`, `AGENTS.md`, `AGENTS.override.md`, `CLAUDE.md`, `README.md`, solution or project files, `Directory.Build.props`, `Directory.Build.targets`, IDE metadata, test results, packages used only for development, or build intermediates.
- Do not deploy PDB files unless the user explicitly requests a debug deployment.
- Before synchronizing or cleaning a deployment, resolve and verify the exact absolute target. Never clean the deployment root, a repository, or an unresolved/computed target.
- Preserve Steam Workshop identity. Before deployment, if the target contains `About\PublishedFileId.txt`, ensure the repository contains the same tracked file. Copy it back to the repository if missing. If both copies exist but differ, stop and report the conflict. Never add this file to `.gitignore` or delete it during cleanup.
- After every completed mod change, locate the repository's solution or C# project and successfully build the Release configuration. XML-only mods do not require a fake build.
- A build that copies a DLL into the repository is not a complete deployment. After a successful build, synchronize every allowed runtime item from the repository to the resolved Steam folder.
- Remove files and directories inside the resolved Steam folder that are not present in the allowed repository runtime content. Do not remove anything outside that exact folder.
- Verify deployment by comparing relative file paths and SHA-256 hashes for all allowed content. Completion requires zero missing, extra, or mismatched files.
- Report the build result, resolved deployment target, and verification result concisely.

### Release versioning

- When the user asks for a mod release or build, update the mod version to the current date in `YYMMDD` format unless the user specifies another versioning scheme for that request.
- Apply the version consistently to the version field the mod uses, such as `About.xml` `modVersion`, and to requested release or changelog material.

### Steam Workshop descriptions

- Write for players, not developers.
- Keep descriptions concise, friendly, and easy to scan.
- Lead with what the mod does and why it is useful.
- Use Steam-compatible BBCode, not Markdown.
- Prefer a short introduction followed by a compact feature list.
- Mention important compatibility behavior only when players may encounter it.
- Do not list dependencies.
- Do not include implementation details, class names, patches, file paths, build information, hashes, or testing notes.
- Do not claim universal compatibility or bug-free operation.
- Do not add installation instructions.
- Do not include changelog material unless explicitly requested.
- When the user asks for a Steam description, return only text ready to paste into Steam.
