# Integrated Configs
Lets you register custom XML config files that get loaded locally or
from server (whichever makes sense). This allows your XML to behave 
like vanilla xml loading. No more having to make sure all clients have
the same XML config values as they will pull from server values.

This mod was developed against 7D2D Alpha 21

***Works with Singleplayer, In-Game hosting, and Dedicated servers.***

***Must exist on Server (if there is one) AND Client.***

## Getting Started

Copy `ModInfo.xml` `IntegratedConfigs.dll` and `IntegratedConfigs.pdb (optional)` to a folder
named `IntegratedConfigs` and place in your game install's mod location.

## Now what?

If your mod wants to use this functionality:

1. Reference the IntegratedConfigs.dll in your project (you don't have to provide this reference inside your mod on release. 7D2D will load the IntegratedConfigs.dll dependency at runtime from this mod)
2. Create a C# class for a given XML (like CustomOptionsConfig.cs)
3. Extend and implement from IIntegratedConfig interface so it can be found and registered.

```asxx
using System.Collections;
using IntegratedConfigs.Core.Entities;
using IntegratedConfigs.Scripts;

namespace YourNamespace
{
    public class CustomOptionsConfig : IIntegratedConfig
    {
        public XmlLoadInfo RegistrationInfo { get; }

        private const string XML_NAME = "CustomConfig";
        
        // This provided here as 
        public static readonly string ModName = "YourModName";
        public static readonly Mod Mod = ModManager.GetMod(ModName, true);
        public static readonly string ModPath = Mod.Path;
        // an example.

        public CustomOptionsConfig()
        {
            RegistrationInfo = new XmlLoadInfo(XML_NAME, $"{ModPath}/YOUR_XML_FILE/LOCATION", 
                false, true, OnLoad, null, OnLoadComplete);
        }
        
        private IEnumerator OnLoad(XmlFile xmlFile)
        {
            Log.Out($"OnLoad called for custom XML: {XML_NAME}");
            yield break;
        }
        
        private static IEnumerator OnLoadComplete()
        {
            Log.Out($"OnLoadComplete called for custom XML: {XML_NAME}");
            yield break;
        }
    }
}
```

This mod will scan and register XML from all classes that implement the `IIntegratedConfig` interface.

Log Example:

```asxx
2023-08-05T17:10:52 10.583 INF [IntegratedConfigs] - Registering custom XML Mod1Options
2023-08-05T17:10:52 10.584 INF [IntegratedConfigs] - Registering custom XML Mod2Options
...
...
...
2023-08-05T17:11:16 35.245 INF Received config file 'Mod1Options' from server. Len: 35
2023-08-05T17:11:16 35.246 INF Received config file 'Mod2Options' from server. Len: 35
...
...
...
2023-08-05T17:11:16 35.295 INF [Mod1] - OnLoad called for custom XML: Mod1Options XmlFile
2023-08-05T17:11:16 35.329 INF [Mod1] - OnLoadComplete called for custom XML: Mod1Options
2023-08-05T17:11:16 35.345 INF Loaded (received): Mod1Options
2023-08-05T17:11:16 35.396 INF [Mod2] - OnLoad called for custom XML: Mod2Options XmlFile
2023-08-05T17:11:17 35.641 INF [Mod2] - OnLoadComplete called for custom XML: Mod2Options
2023-08-05T17:11:17 35.735 INF Loaded (received): Mod2Options
```

* The log `Loaded (received): Mod1Options` will say `Loaded (local): Mod1Options` if it uses local copy. (Host/Singleplayer)