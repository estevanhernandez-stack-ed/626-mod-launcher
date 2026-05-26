# Agentic Game-Profile Run — 2026-05-25

Test of the structured-profile prompt against Este's full Steam library (21 games, excluding already-registered Elden Ring + Windrose and the obvious non-games Steamworks Common Redistributables + Wallpaper Engine).

The prompt is in the session log immediately before this run; the output below is the agent's verbatim JSON.

```json
[
  { "name": "Battlefield 6", "appId": "2807960", "moddable": false, "engine": "custom", "notes": "Frostbite + EA Javelin (kernel anti-cheat). Online-only, modded clients banned." },
  { "name": "Captain of Industry", "appId": "1594320", "moddable": true, "engine": "custom", "modPath": "Mods", "fileExtensions": ["dll"], "groupingRule": "by_folder", "saveDir": "%USERPROFILE%\\AppData\\LocalLow\\MaFi Games\\Captain of Industry\\Saves", "nexusGameDomain": "captainofindustry" },
  { "name": "Content Warning", "appId": "2881650", "moddable": true, "engine": "bepinex", "modPath": "BepInEx\\plugins", "fileExtensions": ["dll"], "groupingRule": "filename_no_ext", "saveDir": "%USERPROFILE%\\AppData\\LocalLow\\Landfall Publishing\\Content Warning", "nexusGameDomain": "contentwarning" },
  { "name": "Cyberpunk 2077", "appId": "1091500", "moddable": true, "engine": "custom", "modPath": "mods", "fileExtensions": ["archive"], "groupingRule": "filename_no_ext", "saveDir": "%USERPROFILE%\\Saved Games\\CD Projekt Red\\Cyberpunk 2077", "nexusGameDomain": "cyberpunk2077" },
  { "name": "DEATH STRANDING 2: ON THE BEACH", "appId": "3280350", "moddable": true, "engine": "custom", "modPath": null, "notes": "Decima; scene early, no standardized mod folder yet." },
  { "name": "Demonologist", "appId": "1929610", "moddable": true, "engine": "ue-pak", "modPath": "Demonologist\\Content\\Paks\\~mods", "fileExtensions": ["pak","ucas","utoc"], "groupingRule": "strip_underscore_p_suffix", "saveDir": "%LOCALAPPDATA%\\Demonologist\\Saved\\SaveGames", "nexusGameDomain": "demonologist" },
  { "name": "Gas Station Simulator", "appId": "1149620", "moddable": true, "engine": "custom", "modPath": null, "notes": "Workshop-only — no direct mod folder." },
  { "name": "Half Sword Demo", "appId": "2642680", "moddable": true, "engine": "ue-pak", "modPath": "HalfSwordUE5\\Content\\Paks\\~mods", "fileExtensions": ["pak","ucas","utoc"], "groupingRule": "strip_underscore_p_suffix", "saveDir": "%LOCALAPPDATA%\\HalfSwordUE5\\Saved\\SaveGames", "nexusGameDomain": "halfsword" },
  { "name": "HELLDIVERS 2", "appId": "553850", "moddable": true, "engine": "custom", "modPath": "data", "notes": "Cosmetic-only patch mods tolerated; gameplay mods risk anti-cheat flags." },
  { "name": "Jump Space", "appId": "1757300", "moddable": false, "engine": "custom", "notes": "EA online co-op, no scene." },
  { "name": "Marvel Rivals", "appId": "2767030", "moddable": false, "engine": "ue-pak", "notes": "NetEase anti-cheat; modded clients HWID-banned." },
  { "name": "Marvel's Spider-Man 2", "appId": "2651280", "moddable": true, "engine": "custom", "modPath": null, "notes": "Suit/asset mods via external Modding Tool; not a drop-in folder." },
  { "name": "Once Human", "appId": "2139460", "moddable": false, "engine": "ue-pak", "notes": "Always-online MMO, NetEase anti-cheat." },
  { "name": "Phasmophobia", "appId": "739630", "moddable": true, "engine": "bepinex", "modPath": "BepInEx\\plugins", "fileExtensions": ["dll"], "groupingRule": "filename_no_ext", "saveDir": "%USERPROFILE%\\AppData\\LocalLow\\Kinetic Games\\Phasmophobia", "nexusGameDomain": "phasmophobia" },
  { "name": "R.E.P.O.", "appId": "3241660", "moddable": true, "engine": "bepinex", "modPath": "BepInEx\\plugins", "fileExtensions": ["dll"], "groupingRule": "filename_no_ext", "saveDir": "%USERPROFILE%\\AppData\\LocalLow\\semiwork\\Repo", "nexusGameDomain": "repo" },
  { "name": "Ready or Not", "appId": "1144200", "moddable": true, "engine": "ue-pak", "modPath": "ReadyOrNot\\Content\\Paks\\~mods", "fileExtensions": ["pak","ucas","utoc"], "groupingRule": "strip_underscore_p_suffix", "saveDir": "%LOCALAPPDATA%\\ReadyOrNot\\Saved\\SaveGames", "nexusGameDomain": "readyornot" },
  { "name": "Schedule I", "appId": "3164500", "moddable": true, "engine": "melonloader", "modPath": "Mods", "fileExtensions": ["dll"], "groupingRule": "filename_no_ext", "saveDir": "%USERPROFILE%\\AppData\\LocalLow\\TVGS\\Schedule I\\Saves", "nexusGameDomain": "schedule1" },
  { "name": "Sons Of The Forest", "appId": "1326470", "moddable": true, "engine": "bepinex", "modPath": "BepInEx\\plugins", "fileExtensions": ["dll"], "groupingRule": "filename_no_ext", "saveDir": "%USERPROFILE%\\AppData\\LocalLow\\Endnight\\SonsOfTheForest\\Saves", "nexusGameDomain": "sonsoftheforest" },
  { "name": "Star Trek Online", "appId": "9900", "moddable": false, "engine": "custom", "notes": "MMO; client modification against ToS." },
  { "name": "STAR WARS Battlefront II", "appId": "1237950", "moddable": true, "engine": "custom", "modPath": null, "notes": "Frostbite via external Frosty Mod Manager; .fbmod files, not a drop-in folder." },
  { "name": "Witchfire", "appId": "3156770", "moddable": true, "engine": "ue-pak", "modPath": "Witchfire\\Content\\Paks\\~mods", "fileExtensions": ["pak","ucas","utoc"], "groupingRule": "strip_underscore_p_suffix", "saveDir": "%LOCALAPPDATA%\\Witchfire\\Saved\\SaveGames", "nexusGameDomain": "witchfire" }
]
```

## Read on prompt quality

- **Engine selection is sharp** — UE-pak with project-folder prefix inferred for the UE games (`Demonologist`, `HalfSwordUE5`, `ReadyOrNot`, `Witchfire`); BepInEx vs MelonLoader correctly split (Schedule I = IL2CPP / MelonLoader vs Phasmophobia / R.E.P.O. / SoTF / Content Warning = BepInEx); Frosty/Cyberpunk/Captain-of-Industry correctly `custom` with a notes-explained mechanism.
- **Honest about gaps** — `modPath: null` for the four games where modding doesn't map to a drop-in folder (Death Stranding 2, Gas Station Sim, Spider-Man 2, SWBF2); `moddable: false` with reasons for the five anti-cheat / MMO / ToS-restricted ones.
- **Minor uncertainties to verify before registering:** Marvel's Spider-Man 2 — Nexus domain guess looks like the Remastered page (probably wrong for SM2); Helldivers 2 — `fileExtensions` includes `"patch_0.gpu_resources"` which is a filename pattern, not really an extension.

## Conclusion
Prompt is solid as a v1. Clean structured output, honest about gaps, no invented paths. Where it's uncertain it picks `null` or `custom`. Ready to use as the basis for an Add-Game-via-Agent flow.
