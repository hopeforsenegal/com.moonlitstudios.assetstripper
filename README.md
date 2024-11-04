# Asset Stripper 
[![License](https://img.shields.io/badge/license-MIT-green)](https://github.com/hopeforsenegal/immediatestyle/blob/master/LICENSE.md)

**Asset Stripper** is a lean tool catered to increasing productivity and speed within the Unity Editor. With it, removing unused assets (or find assets that were thought to be used, but aren't, or perhaps have been duplicated). Finding and removing unused editor scripts or prefabs can increase the speed at which the editor opens. It also relieves us of wondering what can be safely removed from a project versus what should stay.

<img width="966" alt="Screenshot 2024-11-04 at 10 43 52 AM" src="https://github.com/user-attachments/assets/881b0646-ea44-4f3d-a340-fca2e9a94f65">


## Features

* **Back up assets** that were stripped with making a package
* **Scan** the project for unused game and editor scripts (including limited support for regular classes and structs)
* **Reference folders** which is useful for finding unused third party examples, prefabs, scenes, or scripts
* **Filter** by keyword on the results of a scan (match or exclude)

## Installation

- Add this GitHub URL to your package manager or, instead, in your 'manifest.json' add
```json
  "dependencies": {
	...

    	"com.moonlitstudios.assetstripper": "https://github.com/hopeforsenegal/com.moonlitstudios.assetstripper.git",

	...
  }
```

None of that working? **Honestly, just reach out us!** (Links & methods towards the bottom).


## How does it work?
The method is very brute force. It just scans all the files within a project that have visible meta files and builds an internal map. With the internal map we cross references to determine if they have a use in other files (using AssetDatabase.GetDependencies). 
It is very simple and straightforward.

## Need Help or want to chat?
Feel free to just drop us a line on [Discord](https://discord.gg/8y87EEaftE). It's always better to have a real conversation and we can also screenshare there. It's also not hard to reach us through our various other socials. There we can talk about about the individual needs that you might have with your projects.

## Other Unity Packages
Check out [Immediate Style](https://github.com/hopeforsenegal/com.moonlitstudios.immediatestyle)

## Support this project 
Please please please!! ⭐ Star this project! If you truly feel empowered at all by this project please give [our games](https://linktr.ee/moonlit_games) a shot (and drop 5 star reviews there too!). Each of these games are supported by this tool 

![icon512](https://github.com/user-attachments/assets/85141dc9-110e-4a8d-b684-6c9a686c278b)
[Apple](https://apps.apple.com/us/app/caribbean-dominoes/id1588590418)
[Android](https://play.google.com/store/apps/details?id=com.MoonlitStudios.CaribbeanDominoes)

![appIcon](https://github.com/user-attachments/assets/4266f475-ac9b-4176-9f97-985b8e1025ce)
[Apple](https://apps.apple.com/us/app/solitaire-islands/id6478837950)
[Android](https://play.google.com/store/apps/details?id=com.MoonlitStudios.SolitaireIslands)

![app_icon](https://github.com/user-attachments/assets/13ba91c7-53b4-4469-bdd0-9f0598048a28)
[Apple](https://apps.apple.com/us/app/ludi-classic/id1536964897)
[Android](https://play.google.com/store/apps/details?id=com.MoonlitStudios.Ludi)


Last but not least, drop some follows on the following socials if you want to keep updated on the latest happenings 😊

https://www.twitch.tv/caribbeandominoes

https://www.facebook.com/CaribbeanDominoes

https://x.com/moonlit_studios

https://x.com/_quietwarrior
