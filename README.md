# AvatarFavLocal
A VRChat mod to add a favorite list of avatars to the game.

This fork focuses on removing the dependence on Slaynash's network, which is heavily relied upon for multiple mods.  While occasionally useful,
the upstream version uses it for silly things like storing public avatar IDs, when a simple local text file would have sufficed. In addition, 
VRCNW (VRChat Mod Network) often can't handle the load, and has been known to glitch out occasionally, resulting in mass-bans.

## Disclaimer

1. The developers of this mod are not affiliated with or working for VRChat in any way, shape, or form. We are third-party, unauthorized developers.
1. This software is provided to you without warranty.  
   1. You accept the risks of using third-party, unauthorized client mods when you download and install this mod.
      1. **Unauthorized client modifications are against VRChat Terms of Service.** Use of this software can result in a ban from the game.
      1. While we try to keep issues to a minimum, **this software may result in crashes and unwanted behaviour ("bugs").** Don't ask VRChat for support or you may get banned.
   1. We are not responsible if something happens, like your account getting banned or VRChat crashing.
1. This software is provided to you under the terms of the MIT Open-Source License agreement. By forking or using it, you agree to the terms of said license, as well.

## Current features

### Upstream
These features are available in both versions of the mod:

 * Favoriting/Unfavoriting public avatars (With UI)
 * Avatar Search 

### This Fork

 * Disable/enable VRCModNetwork integration with the flick of a switch (Mod settings)
 * Local storage of all avatars (AvatarFav.json).
 * Resilience in the event VRCNW goes down.

## Planned features
### Upstream
These features are planned by Slaynash:

 * Avatar description page (of yours and other players)
 * List of worlds where an avatar can be found

### This Fork
 * Opt-out of pedestal scans
 * SYNC button for downloading favs from Slaynash's network.

## Installation

To install this mod, you will need to install [VRCModLoader](https://github.com/Slaynash/VRCModLoader).<br>

0. Remove any older version of this mod and AvatarFav.
1. Download AvatarFavLocal.#.#.#.dll from [releases](https://github.com/Anonymous-BCFED/AvatarFav/releases/latest).
2. Make sure the VRCTools mod is also updated.

## Issues

Please file any issue reports to this repository's issues board. We do not have a Discord.