
// Undertale Mod Tool Script: Convert to GameMaker Studio 2 Project
// Version: 1.1
// Author: [Your Name or AI Assistant]
// Description: Converts the currently loaded UMT data into a GMS2 project structure.
//              Manual adjustments in GMS2 will likely be required after conversion.

#r "System.IO.Compression.FileSystem" // For potential future Zip creation
#r "System.Drawing" // For Bitmap operations
#r "Newtonsoft.Json" // Essential for creating .yy and .yyp files

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms; // For FolderBrowserDialog
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; // For creating JSON objects

public class GMS2Converter : IUMTScript
{
    // --- Configuration ---
    private const string GMS2_VERSION = "2.3.0.0"; // Target GMS2 version for compatibility info (adjust if needed)
    private const string RUNTIME_VERSION = "2.3.0.529"; // Example runtime version
    private const bool TRY_DECOMPILE_IF_NEEDED = true; // Attempt to decompile code if not already done
    private const bool WARN_ON_MISSING_DECOMPILED_CODE = true; // Log warnings for missing code

    // --- Internal State ---
    private UndertaleData Data;
    private string OutputPath;
    private string ProjectName;
    private Guid ProjectGuid;
    private Dictionary<string, Guid> ResourceGuids = new Dictionary<string, Guid>();
    private Dictionary<string, string> ResourcePaths = new Dictionary<string, string>();
    private Dictionary<UndertaleResource, string> ResourceToNameMap = new Dictionary<UndertaleResource, string>();
    private List<string> CreatedDirectories = new List<string>(); // Track created directories for potential cleanup on error

    // Resource Lists for .yyp file
    private List<JObject> ResourceList = new List<JObject>();
    private Dictionary<string, List<string>> FolderStructure = new Dictionary<string, List<string>>();

    public void Execute(UndertaleData data)
    {
        this.Data = data;
        if (Data == null)
        {
            UMT.ShowMessage("No data loaded in Undertale Mod Tool.");
            return;
        }

        // Check for decompiled code
        if (Data.Code == null || Data.Code.FirstOrDefault()?.Decompiled == null)
        {
            if (TRY_DECOMPILE_IF_NEEDED)
            {
                UMT.Log("Code not decompiled. Attempting decompilation...");
                try
                {
                    // This might vary depending on UMT version / API
                    // Assuming a hypothetical function exists. You might need to trigger this manually in UMT first.
                    // UMT.DecompileAllCode(); // Replace with actual UMT API call if available
                    UMT.Log("Decompilation might need to be triggered manually via UMT's interface if this step fails.");
                    // Re-check after potential decompilation attempt (or just proceed and handle errors later)
                    if (Data.Code == null || Data.Code.FirstOrDefault()?.Decompiled == null)
                    {
                         UMT.Log("Warning: Code still appears decompiled. Conversion will proceed but scripts/events will be empty.");
                    } else {
                         UMT.Log("Decompilation seems successful or was already done.");
                    }
                }
                catch (Exception ex)
                {
                    UMT.ShowError("Error during decompilation attempt: " + ex.Message);
                    return; // Stop if decompilation fails and is needed
                }
            }
            else if (WARN_ON_MISSING_DECOMPILED_CODE)
            {
                 UMT.Log("Warning: Code is not decompiled. Scripts and event code will be missing in the GMS2 project.");
            }
        }


        // --- 1. Select Output Directory ---
        using (var fbd = new FolderBrowserDialog())
        {
            fbd.Description = "Select the Output Folder for the GMS2 Project";
            DialogResult result = fbd.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                OutputPath = fbd.SelectedPath;
                ProjectName = SanitizeFolderName(Data.GeneralInfo?.DisplayName?.Content ?? Path.GetFileNameWithoutExtension(Data.Filename) ?? "ConvertedProject");
                OutputPath = Path.Combine(OutputPath, ProjectName); // Create a subfolder for the project

                 UMT.Log($"Output Path set to: {OutputPath}");
            }
            else
            {
                UMT.ShowMessage("Output folder selection cancelled. Aborting conversion.");
                return;
            }
        }

        // --- 2. Initialize Project Structure ---
        try
        {
            if (Directory.Exists(OutputPath))
            {
                var overwriteResult = MessageBox.Show($"The directory '{OutputPath}' already exists. Overwrite? (This will delete existing contents!)", "Directory Exists", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (overwriteResult == DialogResult.Yes)
                {
                     UMT.Log($"Deleting existing directory: {OutputPath}");
                    Directory.Delete(OutputPath, true);
                }
                else
                {
                    UMT.ShowMessage("Conversion aborted.");
                    return;
                }
            }
            Directory.CreateDirectory(OutputPath);
            CreatedDirectories.Add(OutputPath);

            ProjectGuid = Guid.NewGuid(); // Unique ID for this project conversion

            // Create basic GMS2 folders
            CreateGMS2Directory("sprites");
            CreateGMS2Directory("sounds");
            CreateGMS2Directory("objects");
            CreateGMS2Directory("rooms");
            CreateGMS2Directory("scripts");
            CreateGMS2Directory("shaders");
            CreateGMS2Directory("fonts");
            CreateGMS2Directory("tilesets");
            CreateGMS2Directory("notes");
            CreateGMS2Directory("extensions");
            CreateGMS2Directory("options");
            CreateGMS2Directory("datafiles"); // For Included Files
            CreateGMS2Directory("configs");
            CreateGMS2Directory("timelines");
            CreateGMS2Directory("paths");
            CreateGMS2Directory("audiogroups");
            CreateGMS2Directory("texturegroups");
            CreateGMS2Directory("includedfiles");

            // Create default config
             CreateDefaultConfig();

             // Build Resource Name Map (Handle potential duplicates)
             BuildResourceNameMap();

            // --- 3. Convert Resources ---
             UMT.Log("Starting resource conversion...");

            // Order matters sometimes (e.g., Sprites needed by Objects/Tilesets)
            ConvertAudioGroups();
            ConvertTextureGroups();
            ConvertSprites();       // Includes Backgrounds used as Sprites
            ConvertSounds();
            ConvertTilesets();      // Depends on Sprites (created from Backgrounds/Sprites)
            ConvertFonts();         // Depends on Sprites (for texture)
            ConvertPaths();
            ConvertScripts();
            ConvertShaders();
            ConvertTimelines();
            ConvertObjects();       // Depends on Sprites, Events use Scripts
            ConvertRooms();         // Depends on Objects, Tilesets, Sprites (backgrounds)
            ConvertIncludedFiles();
            ConvertExtensions();    // Basic structure only
            ConvertNotes();         // Basic structure only

            // --- 4. Create Project File (.yyp) ---
             UMT.Log("Creating main project file (.yyp)...");
             CreateProjectFile();

            // --- 5. Create Options Files ---
             UMT.Log("Creating default options files...");
             CreateOptionsFiles(); // Create basic GMS2 options files


             UMT.Log($"GMS2 Project '{ProjectName}' conversion process finished.");
             UMT.ShowMessage($"Conversion complete! Project saved to:\n{OutputPath}\n\nPlease open the project in GMS2 and perform manual checks and adjustments, especially for GML code, tilesets, and fonts.");

        }
        catch (Exception ex)
        {
            UMT.ShowError($"An error occurred during conversion: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
             UMT.Log($"Error: Conversion failed. See details above.");
            // Optional: Clean up partially created directories?
            /*
            DialogResult cleanupResult = MessageBox.Show("An error occurred. Attempt to clean up created project files?", "Cleanup on Error", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (cleanupResult == DialogResult.Yes)
            {
                try
                {
                    if (Directory.Exists(OutputPath))
                    {
                        Directory.Delete(OutputPath, true);
                        UMT.Log($"Cleaned up directory: {OutputPath}");
                    }
                }
                catch (Exception cleanupEx)
                {
                    UMT.Log($"Error during cleanup: {cleanupEx.Message}");
                }
            }
            */
        }
        finally
        {
            // Reset state for potential future runs within the same UMT session
            ResourceGuids.Clear();
            ResourcePaths.Clear();
            ResourceList.Clear();
            FolderStructure.Clear();
            ResourceToNameMap.Clear();
            CreatedDirectories.Clear();
        }
    }

    // === Helper Functions ===

    private void CreateGMS2Directory(string dirName)
    {
        string path = Path.Combine(OutputPath, dirName);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            CreatedDirectories.Add(path);
             UMT.Log($"Created directory: {path}");
        }
        // Initialize folder structure for .yyp
        FolderStructure[dirName] = new List<string>();
    }

    private string SanitizeFileName(string name)
    {
        // Remove invalid file path characters and trim whitespace
        string invalidChars = new string(Path.GetInvalidFileNameChars());
        string sanitized = new string(name.Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
        // Replace spaces with underscores (optional, but common GMS practice)
        sanitized = sanitized.Replace(' ', '_');
        // Handle potential empty names after sanitization
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "unnamed_" + Guid.NewGuid().ToString().Substring(0, 8);
        }
        // Prevent GMS2 reserved names (add more if needed)
        string[] reserved = { "all", "noone", "global", "local", "self", "other", "true", "false", "object_index", "id" /* ... add more ...*/ };
        if (reserved.Contains(sanitized.ToLower()))
        {
            sanitized += "_";
        }

        return sanitized;
    }
     private string SanitizeFolderName(string name)
    {
        // Similar to SanitizeFileName, but for directory names
        string invalidChars = new string(Path.GetInvalidPathChars());
         // Add common problematic chars for paths if not already included
         invalidChars += ":*?\"<>|";
        string sanitized = new string(name.Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
        sanitized = sanitized.Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "unnamed_folder_" + Guid.NewGuid().ToString().Substring(0, 8);
        }
        return sanitized;
    }


    private Guid GetResourceGuid(string resourceKey)
    {
        if (!ResourceGuids.ContainsKey(resourceKey))
        {
            ResourceGuids[resourceKey] = Guid.NewGuid();
        }
        return ResourceGuids[resourceKey];
    }

     // Builds a map from UndertaleResource to a unique, sanitized name
     private void BuildResourceNameMap()
     {
         UMT.Log("Building resource name map...");
         var nameCounts = new Dictionary<string, int>();

         Action<IList<UndertaleNamedResource>> processList = (list) => {
             if (list == null) return;
             foreach (var res in list) {
                 if (res == null || res.Name == null) continue;
                 string baseName = SanitizeFileName(res.Name.Content);
                 string uniqueName = baseName;
                 if (ResourceToNameMap.Values.Contains(uniqueName)) {
                      if (!nameCounts.ContainsKey(baseName)) nameCounts[baseName] = 1;
                      nameCounts[baseName]++;
                      uniqueName = $"{baseName}_{nameCounts[baseName]}";
                 } else if (nameCounts.ContainsKey(baseName)) { // Handle case where first instance conflicts later
                      // This scenario is less likely if processed linearly but good to consider
                 } else {
                      nameCounts[baseName] = 1; // Mark base name as used
                 }

                 ResourceToNameMap[res] = uniqueName;
             }
         };

         // Process main resource types
         processList(Data.Sprites?.Cast<UndertaleNamedResource>().ToList());
         processList(Data.Sounds?.Cast<UndertaleNamedResource>().ToList());
         processList(Data.Objects?.Cast<UndertaleNamedResource>().ToList());
         processList(Data.Rooms?.Cast<UndertaleNamedResource>().ToList());
         processList(Data.Scripts?.Cast<UndertaleNamedResource>().ToList());
         processList(Data.Shaders?.Cast<UndertaleNamedResource>().ToList());
         processList(Data.Fonts?.Cast<UndertaleNamedResource>().ToList());
         processList(Data.Paths?.Cast<UndertaleNamedResource>().ToList());
         processList(Data.Timelines?.Cast<UndertaleNamedResource>().ToList());
         processList(Data.Backgrounds?.Cast<UndertaleNamedResource>().ToList()); // For tilesets/backgrounds

         // Also handle TextureGroups and AudioGroups (they have names)
         if (Data.TextureGroups != null) {
              foreach(var tg in Data.TextureGroups) {
                    if (tg == null || tg.Name == null) continue;
                    string baseName = SanitizeFileName(tg.Name.Content);
                    string uniqueName = baseName;
                     if (ResourceToNameMap.Values.Contains(uniqueName)) {
                         if (!nameCounts.ContainsKey(baseName)) nameCounts[baseName] = 1;
                         nameCounts[baseName]++;
                         uniqueName = $"{baseName}_{nameCounts[baseName]}";
                     } else { nameCounts[baseName] = 1;}
                    ResourceToNameMap[tg] = uniqueName;
              }
         }
         if (Data.AudioGroups != null) {
              foreach(var ag in Data.AudioGroups) {
                    if (ag == null || ag.Name == null) continue;
                    string baseName = SanitizeFileName(ag.Name.Content);
                    string uniqueName = baseName;
                     if (ResourceToNameMap.Values.Contains(uniqueName)) {
                         if (!nameCounts.ContainsKey(baseName)) nameCounts[baseName] = 1;
                         nameCounts[baseName]++;
                         uniqueName = $"{baseName}_{nameCounts[baseName]}";
                     } else { nameCounts[baseName] = 1;}
                    ResourceToNameMap[ag] = uniqueName;
              }
         }
           // Included files also need names/keys
         if (Data.IncludedFiles != null) {
              foreach(var incFile in Data.IncludedFiles) {
                  if (incFile == null || incFile.Name == null) continue;
                   string baseName = SanitizeFileName(Path.GetFileName(incFile.Name.Content)); // Use filename part
                   string uniqueName = baseName;
                   if (ResourceToNameMap.Values.Contains(uniqueName)) {
                       if (!nameCounts.ContainsKey(baseName)) nameCounts[baseName] = 1;
                       nameCounts[baseName]++;
                       uniqueName = $"{baseName}_{nameCounts[baseName]}";
                   } else { nameCounts[baseName] = 1; }
                  ResourceToNameMap[incFile] = uniqueName; // Use the object itself as key
              }
         }


         UMT.Log($"Built map for {ResourceToNameMap.Count} named resources.");
     }

      // Gets the sanitized, unique name for a resource, returns default if not found
     private string GetResourceName(UndertaleResource res, string defaultName = "unknown_resource")
     {
         if (res != null && ResourceToNameMap.TryGetValue(res, out string name))
         {
             return name;
         }
          // Fallback for unnamed or unmapped resources
          if (res is UndertaleChunk Tagi t) {
              return SanitizeFileName($"resource_{t.Tag}_{Guid.NewGuid().ToString().Substring(0,4)}");
          }

         return defaultName;
     }

    private void WriteJsonFile(string filePath, JObject jsonContent)
    {
        try
        {
            File.WriteAllText(filePath, jsonContent.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
             UMT.Log($"Error writing JSON file {filePath}: {ex.Message}");
             throw; // Re-throw to halt conversion on critical file write error
        }
    }

     // Simple helper to get a resource reference JObject for linking (e.g., sprite in object)
     private JObject CreateResourceReference(string name, Guid guid, string resourceType) {
         if (guid == Guid.Empty) {
             // GMS2 uses null or a specific format for "no resource"
             // For sprites, it might be null or a specific { name: null, path: null }
             // For parents, it's often null. Let's return null for simplicity.
             return null;
         }
         string path = $"{resourceType}/{name}/{name}.yy";
         return new JObject(
             new JProperty("name", name),
             new JProperty("path", path)
         );
     }

      // Overload for resources looked up by the UndertaleResource object
       private JObject CreateResourceReference(UndertaleResource res, string resourceType) {
           if (res == null) return null;
           string name = GetResourceName(res);
           // Need to find the GUID associated with this *name* (as names are keys now)
           if (ResourcePaths.TryGetValue($"{resourceType}/{name}", out string path)) {
               Guid guid = Guid.Empty;
               // Extract GUID from path or look up differently? Assumes ResourceGuids uses "ResourceType/Name" key
               string key = $"{resourceType}/{name}";
                if(ResourceGuids.ContainsKey(key)) {
                    guid = ResourceGuids[key];
                    return new JObject(
                        new JProperty("name", name),
                        new JProperty("path", path)
                    );
                } else {
                     UMT.Log($"Warning: Could not find GUID for resource reference: {name} ({resourceType}). Path: {path}");
                    return null;
                }

           } else {
                 UMT.Log($"Warning: Could not find path for resource reference: {name} ({resourceType})");
                return null; // Or return a default/null representation
           }
       }


    // === Resource Conversion Functions ===

    private void ConvertSprites()
    {
        UMT.Log("Converting Sprites...");
        if (Data.Sprites == null) return;

        string spriteDir = Path.Combine(OutputPath, "sprites");

        // Also convert Backgrounds that might be used as sprites (common in UT/DR)
        List<UndertaleSprite> allSprites = new List<UndertaleSprite>(Data.Sprites);
        if (Data.Backgrounds != null) {
             UMT.Log("Checking Backgrounds for potential sprite conversion...");
            foreach(var bg in Data.Backgrounds) {
                if (bg != null && bg.Texture != null && bg.Texture.TexturePage != null) {
                     // Treat Backgrounds essentially like single-frame sprites for conversion purposes
                     // We need to give them sprite-like properties.
                     var pseudoSprite = new UndertaleSprite {
                         Name = bg.Name, // Use the background's name
                         Width = bg.Texture.TexturePage.SourceWidth, // Use source dimensions if available
                         Height = bg.Texture.TexturePage.SourceHeight,
                         MarginLeft = 0,
                         MarginRight = bg.Texture.TexturePage.TargetWidth -1, // Approximation
                         MarginBottom = bg.Texture.TexturePage.TargetHeight -1, // Approximation
                         MarginTop = 0,
                         OriginX = 0,
                         OriginY = 0,
                         BBoxMode = UndertaleSprite.BoundingBoxMode.Automatic, // Default
                         SepMasks = 0, // Default
                         PlaybackSpeed = 15, // Default GMS2 speed
                         PlaybackSpeedType = UndertaleSprite.SpritePlaybackSpeedType.FramesPerSecond,
                         // Store the background's texture entry for frame extraction
                         Textures = new List<UndertaleSprite.TextureEntry> { bg.Texture },
                         // Mark this somehow if needed later, maybe using a temporary tag or prefix?
                         // We'll rely on the name mapping for uniqueness
                         // Note: Collision masks from backgrounds aren't directly convertible
                     };
                      // Ensure the background texture entry has dimensions for extraction
                     if (pseudoSprite.Width == 0 || pseudoSprite.Height == 0) {
                         pseudoSprite.Width = bg.Texture.TexturePage.TargetWidth;
                         pseudoSprite.Height = bg.Texture.TexturePage.TargetHeight;
                     }
                      if (pseudoSprite.Width > 0 && pseudoSprite.Height > 0) {
                          allSprites.Add(pseudoSprite);
                           UMT.Log($"- Converted Background '{GetResourceName(bg)}' to pseudo-sprite.");
                      } else {
                           UMT.Log($"Warning: Skipping Background '{GetResourceName(bg)}' as pseudo-sprite due to zero dimensions.");
                      }
                }
            }
        }


        foreach (var sprite in allSprites)
        {
            if (sprite == null || sprite.Name == null) continue;
            string spriteName = GetResourceName(sprite);
            string resourceKey = $"sprites/{spriteName}";
            Guid spriteGuid = GetResourceGuid(resourceKey);
            string spritePath = Path.Combine(spriteDir, spriteName);
            string yyPath = Path.Combine(spritePath, $"{spriteName}.yy");
            string imagesPath = Path.Combine(spritePath, "images");

             UMT.Log($"Converting Sprite: {spriteName}");


            try
            {
                Directory.CreateDirectory(spritePath);
                Directory.CreateDirectory(imagesPath);
                CreatedDirectories.Add(spritePath);
                CreatedDirectories.Add(imagesPath);

                List<JObject> frameList = new List<JObject>();
                List<Guid> frameGuids = new List<Guid>();

                // Extract Frames
                for (int i = 0; i < sprite.Textures.Count; i++)
                {
                    var texEntry = sprite.Textures[i];
                    if (texEntry == null || texEntry.TexturePage == null)
                    {
                        UMT.Log($"Warning: Sprite '{spriteName}' frame {i} has missing texture data. Skipping frame.");
                        continue;
                    }

                    Guid frameGuid = Guid.NewGuid();
                    frameGuids.Add(frameGuid);
                    string frameFileName = $"{frameGuid}.png";
                    string frameFilePath = Path.Combine(imagesPath, frameFileName);

                    try
                    {
                        using (DirectBitmap frameBitmap = TextureWorker.GetTexturePageImageRect(texEntry.TexturePage, Data))
                        {
                            if (frameBitmap != null)
                            {
                                frameBitmap.Bitmap.Save(frameFilePath, ImageFormat.Png);
                            }
                            else
                            {
                                 UMT.Log($"Warning: Failed to extract image for sprite '{spriteName}' frame {i}. Creating empty placeholder.");
                                // Create a small placeholder bitmap if extraction fails
                                using (var placeholder = new Bitmap(Math.Max(1, sprite.Width), Math.Max(1, sprite.Height)))
                                using (var g = Graphics.FromImage(placeholder)) {
                                     g.Clear(Color.Magenta); // Use a noticeable color
                                     placeholder.Save(frameFilePath, ImageFormat.Png);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UMT.Log($"Error extracting frame {i} for sprite '{spriteName}': {ex.Message}. Creating placeholder.");
                         using (var placeholder = new Bitmap(Math.Max(1, sprite.Width), Math.Max(1, sprite.Height)))
                         using (var g = Graphics.FromImage(placeholder)) {
                              g.Clear(Color.Magenta);
                              placeholder.Save(frameFilePath, ImageFormat.Png);
                         }
                    }


                    // Add frame reference to layers structure in .yy
                    var frameData = new JObject(
                        new JProperty("id", frameGuid.ToString("D")), // Format D: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
                        new JProperty("Key", i.ToString()) // Frame index as Key
                    );

                    // Create the frame object for the main frame list
                    var frameObject = new JObject(
                         new JProperty("Config", "Default"), // Assuming default config
                         new JProperty("FrameId", frameGuid.ToString("D")),
                         new JProperty("PSF", ""), // Populated later? Usually empty.
                          // Create nested structure for layers - typically one layer for a simple sprite
                         new JProperty("LayerId", null), // Usually null at this level? Check GMS2 format
                         new JProperty("resourceVersion", "1.0"), // Standard GMS2 version strings
                         new JProperty("name", frameGuid.ToString("D")), // Frame name is its GUID
                         new JProperty("tags", new JArray()),
                         new JProperty("resourceType", "GMSpriteFrame") // Resource type
                     );

                    frameList.Add(frameObject);
                }


                 // Determine GMS2 BBox Mode
                 // This mapping is approximate
                 int gms2BBoxMode = 0; // 0: Automatic, 1: Full Image, 2: Manual
                 int gms2CollisionKind = 1; // 1: Rectangle, 2: Rotated Rectangle, 0: Precise(Slow), 4: Diamond, 5: Precise Per Frame(Slowest)
                  switch (sprite.BBoxMode) {
                      case UndertaleSprite.BoundingBoxMode.Automatic: gms2BBoxMode = 0; break;
                      case UndertaleSprite.BoundingBoxMode.FullImage: gms2BBoxMode = 1; break;
                      case UndertaleSprite.BoundingBoxMode.Manual: gms2BBoxMode = 2; break;
                      default: gms2BBoxMode = 0; break;
                  }
                  // GMS1.x precise mode (SepMasks > 0) roughly maps to GMS2 Precise or Precise Per Frame
                  // This is a difficult mapping. Defaulting to Rectangle unless SepMasks indicates precise.
                  if (sprite.SepMasks > 0) {
                      gms2CollisionKind = 0; // Precise
                       // If you want precise per frame (though UT rarely used this like GMS2 does):
                       // gms2CollisionKind = 5;
                  } else {
                       // Default to rectangle. Diamond/Ellipse weren't standard in GMS1.x like GMS2.
                       gms2CollisionKind = 1;
                  }


                 // Determine Texture Group
                 string textureGroupName = "Default"; // GMS2 Default group
                 Guid textureGroupGuid = Guid.Empty; // Need to get this properly
                 if (sprite.Textures.Count > 0 && sprite.Textures[0].TexturePage != null) {
                    var utTexturePage = sprite.Textures[0].TexturePage;
                    // Find the UndertaleTextureGroup this page belongs to (if possible)
                    // This requires iterating through Data.TextureGroupInfos or similar structure if available
                    // For simplicity, we'll try mapping by name if Texture Group info was converted
                    var utGroup = Data.TextureGroups.FirstOrDefault(tg => tg.Pages.Contains(utTexturePage));
                     if(utGroup != null) {
                         textureGroupName = GetResourceName(utGroup, "Default");
                         textureGroupGuid = GetResourceGuid($"texturegroups/{textureGroupName}"); // Get pre-assigned GUID
                     }
                 }

                 JObject textureGroupRef = null;
                 if (textureGroupGuid != Guid.Empty && textureGroupName != "Default") {
                      textureGroupRef = CreateResourceReference(textureGroupName, textureGroupGuid, "texturegroups");
                 } else {
                      // Standard GMS2 reference to the default group
                      textureGroupRef = new JObject(
                         new JProperty("name", "Default"),
                         new JProperty("path", "texturegroups/Default") // Special path for default
                     );
                 }



                // Create Sprite .yy file content (JSON)
                var yyContent = new JObject(
                    new JProperty("bboxmode", gms2BBoxMode),
                    new JProperty("collisionKind", gms2CollisionKind),
                    new JProperty("type", 0), // Always 0 for Sprites?
                    new JProperty("origin", GetGMS2Origin(sprite.OriginX, sprite.OriginY, sprite.Width, sprite.Height)), // Convert origin to GMS2 enum
                    new JProperty("preMultiplyAlpha", false), // Usually false
                    new JProperty("edgeFiltering", false), // Usually false
                    new JProperty("collisionTolerance", 0), // Default
                    new JProperty("swfPrecision", 2.525), // Default SWF precision
                    new JProperty("bbox_left", sprite.MarginLeft),
                    new JProperty("bbox_right", sprite.MarginRight),
                    new JProperty("bbox_top", sprite.MarginTop),
                    new JProperty("bbox_bottom", sprite.MarginBottom),
                    new JProperty("HTile", false),
                    new JProperty("VTile", false),
                    new JProperty("For3D", false),
                    new JProperty("width", sprite.Width),
                    new JProperty("height", sprite.Height),
                    new JProperty("textureGroupId", textureGroupRef),
                    new JProperty("swatchColours", null), // Typically null unless generated by IDE
                    new JProperty("gridX", 0),
                    new JProperty("gridY", 0),
                    new JProperty("frames", new JArray(frameList)), // The list of frame objects
                    new JProperty("sequence", new JObject( // Playback sequence info
                        new JProperty("resourceType", "GMSequence"),
                        new JProperty("resourceVersion", "1.4"), // GMS sequence version
                        new JProperty("name", spriteName),
                        new JProperty("timeUnits", 1), // 1 = Frames per second, 0 = Frames per game frame
                        new JProperty("playback", 1), // 1 = Play once? Check GMS docs. Usually 1 for sprites.
                        new JProperty("playbackSpeed", (float)sprite.PlaybackSpeed), // Use original speed
                        new JProperty("playbackSpeedType", (int)sprite.PlaybackSpeedType), // 0 = FPS, 1 = Frames per Game Frame (Mapping might be inverse in GMS2?) -> Check GMS2 docs. Let's assume direct mapping for now. 0=FramesPerGameFrame, 1=FramesPerSecond? Let's use UT value directly. UMT: 0=FramesPerSecond, 1=FramesPerGameFrame. GMS2: 0=FramesPerGameFrame, 1=FramesPerSecond. So, need to invert? Let's try mapping UT 0 -> GMS2 1, UT 1 -> GMS2 0.
                        //new JProperty("playbackSpeedType", sprite.PlaybackSpeedType == UndertaleSprite.SpritePlaybackSpeedType.FramesPerSecond ? 1 : 0), // Tentative mapping GMS1 -> GMS2
                        new JProperty("playbackSpeedType", 1), // Force FPS for now, safer default
                        new JProperty("autoRecord", true), // Default
                        new JProperty("volume", 1.0f), // Default
                        new JProperty("length", (float)sprite.Textures.Count), // Number of frames
                        new JProperty("events", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MessageEventKeyframe>"))), // Empty events
                        new JProperty("moments", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MomentsEventKeyframe>"))), // Empty moments
                        new JProperty("tracks", new JArray( // Sprite frames track
                             new JObject(
                                 new JProperty("resourceType", "GMSpriteFramesTrack"),
                                 new JProperty("resourceVersion", "1.0"),
                                 new JProperty("name", "frames"),
                                 new JProperty("spriteId", null), // Self-reference? Usually null here.
                                 new JProperty("keyframes", new JObject( // Keyframes linking to the actual frame images
                                     new JProperty("resourceType", "KeyframeStore<SpriteFrameKeyframe>"),
                                     new JProperty("resourceVersion", "1.0"),
                                     new JProperty("Keyframes", new JArray(
                                         // Create a keyframe for each frame extracted earlier
                                         frameGuids.Select((guid, index) => new JObject(
                                             new JProperty("id", Guid.NewGuid().ToString("D")),
                                             new JProperty("Key", (float)index), // Time position of the frame (0, 1, 2...)
                                             new JProperty("Length", 1.0f), // Duration of the frame (usually 1)
                                             new JProperty("Stretch", false), // Default
                                             new JProperty("Disabled", false), // Default
                                             new JProperty("IsCreationKey", false), // Default
                                             new JProperty("Channels", new JObject(
                                                 new JProperty("0", new JObject( // Channel 0 holds the frame ID
                                                     new JProperty("Id", new JObject( // The actual frame image reference
                                                         new JProperty("name", guid.ToString("D")), // Frame GUID
                                                         new JProperty("path", $"sprites/{spriteName}/{spriteName}.yy") // Path to the sprite resource itself
                                                     )),
                                                     new JProperty("resourceVersion", "1.0"),
                                                     new JProperty("resourceType", "SpriteFrameKeyframe")
                                                 ))
                                             )),
                                             new JProperty("resourceVersion", "1.0"),
                                             new JProperty("resourceType", "Keyframe<SpriteFrameKeyframe>")
                                         )) // End Select
                                     )) // End Keyframes JArray
                                 )), // End keyframes JObject
                                 new JProperty("trackColour", 0), // Default track color
                                 new JProperty("inheritsTrackColour", true), // Default
                                 new JProperty("builtinName", 0), // Default
                                 new JProperty("traits", 0), // Default
                                 new JProperty("interpolation", 1), // 1 = Discrete (standard for sprite frames)
                                 new JProperty("tracks", new JArray()), // No sub-tracks
                                 new JProperty("events", new JArray()), // No events on track
                                 new JProperty("modifiers", new JArray()), // No modifiers
                                 new JProperty("isCreationTrack", false) // Default
                             ) // End GMSpriteFramesTrack JObject
                         )), // End tracks JArray
                        new JProperty("visibleRange", null), // Default
                        new JProperty("lockOrigin", false), // Default
                        new JProperty("showBackdrop", true), // Default
                        new JProperty("showBackdropImage", false), // Default
                        new JProperty("backdropImagePath", ""),
                        new JProperty("backdropImageOpacity", 0.5f),
                        new JProperty("backdropWidth", 1366), // Default GMS2 backdrop size
                        new JProperty("backdropHeight", 768), // Default GMS2 backdrop size
                        new JProperty("backdropXOffset", 0.0f),
                        new JProperty("backdropYOffset", 0.0f),
                        new JProperty("xorigin", sprite.OriginX),
                        new JProperty("yorigin", sprite.OriginY),
                        new JProperty("eventToFunction", new JObject()), // Empty map
                        new JProperty("eventStubScript", null), // Default
                        new JProperty("parent", CreateResourceReference(spriteName, spriteGuid, "sprites")) // Reference to the sprite itself? Yes.
                    )), // End sequence JObject
                    new JProperty("layers", new JArray( // Layer information (usually one for simple sprites)
                        new JObject(
                            new JProperty("resourceType", "GMImageLayer"),
                            new JProperty("resourceVersion", "1.0"),
                            new JProperty("name", Guid.NewGuid().ToString("D")), // Layer needs a unique GUID name
                            new JProperty("visible", true),
                            new JProperty("isLocked", false),
                            new JProperty("blendMode", 0), // 0 = Normal
                            new JProperty("opacity", 100.0f), // Percentage
                            new JProperty("displayName", "default") // User-friendly name
                        )
                    )),
                    new JProperty("parent", new JObject( // Parent folder in IDE view
                        new JProperty("name", "Sprites"), // Top-level folder name
                        new JProperty("path", "folders/Sprites.yy") // Path to the folder definition
                    )),
                     new JProperty("resourceVersion", "1.0"), // GMSprite resource version
                     new JProperty("name", spriteName),
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMSprite")
                );


                WriteJsonFile(yyPath, yyContent);

                // Add to project resources
                string relativePath = $"sprites/{spriteName}/{spriteName}.yy";
                AddResourceToProject(spriteName, spriteGuid, relativePath, "GMSprite", "sprites");
                ResourcePaths[resourceKey] = relativePath; // Store path for lookups

            }
            catch (Exception ex)
            {
                 UMT.Log($"Error processing sprite '{spriteName}': {ex.Message}\n{ex.StackTrace}");
                 // Optionally continue to next sprite or re-throw/abort
            }
        }
         UMT.Log("Sprite conversion finished.");
    }

    private int GetGMS2Origin(int x, int y, int w, int h) {
         // GMS2 Origin Enum: 0: Top Left, 1: Top Centre, 2: Top Right,
         // 3: Middle Left, 4: Middle Centre, 5: Middle Right,
         // 6: Bottom Left, 7: Bottom Centre, 8: Bottom Right, 9: Custom
         if (x == 0 && y == 0) return 0;
         if (x == w / 2 && y == 0) return 1;
         if (x == w -1 && y == 0) return 2; // Assuming GMS1 origin was 0-based index
         if (x == 0 && y == h / 2) return 3;
         if (x == w / 2 && y == h / 2) return 4;
         if (x == w - 1 && y == h / 2) return 5;
         if (x == 0 && y == h - 1) return 6;
         if (x == w / 2 && y == h - 1) return 7;
         if (x == w - 1 && y == h - 1) return 8;
         return 9; // Custom
    }


    private void ConvertSounds()
    {
        UMT.Log("Converting Sounds...");
        if (Data.Sounds == null) return;

        string soundDir = Path.Combine(OutputPath, "sounds");

        foreach (var sound in Data.Sounds)
        {
            if (sound == null || sound.Name == null || sound.AudioFile == null) continue;
            string soundName = GetResourceName(sound);
             string resourceKey = $"sounds/{soundName}";
             Guid soundGuid = GetResourceGuid(resourceKey);
            string soundPath = Path.Combine(soundDir, soundName);
            string yyPath = Path.Combine(soundPath, $"{soundName}.yy");
            // GMS2 expects the audio file next to the .yy file
            string audioFilePath = Path.Combine(soundPath, GetCompatibleAudioFileName(sound.AudioFile.Name.Content, soundName));

             UMT.Log($"Converting Sound: {soundName}");

            try
            {
                Directory.CreateDirectory(soundPath);
                CreatedDirectories.Add(soundPath);

                // Write audio data
                File.WriteAllBytes(audioFilePath, sound.AudioFile.Data);

                 // Determine Audio Group
                 string audioGroupName = "audiogroup_default"; // GMS2 Default group name
                 Guid audioGroupGuid = Guid.Empty; // Need to get this properly
                  // Find the UndertaleAudioGroup this sound belongs to
                 var utGroup = Data.AudioGroups.FirstOrDefault(ag => ag.Sounds.Contains(sound));
                 if (utGroup != null) {
                     audioGroupName = GetResourceName(utGroup, "audiogroup_default");
                     audioGroupGuid = GetResourceGuid($"audiogroups/{audioGroupName}"); // Get pre-assigned GUID
                 }

                 JObject audioGroupRef = null;
                 if (audioGroupGuid != Guid.Empty && audioGroupName != "audiogroup_default") {
                      audioGroupRef = CreateResourceReference(audioGroupName, audioGroupGuid, "audiogroups");
                 } else {
                      // Standard GMS2 reference to the default group
                      audioGroupRef = new JObject(
                         new JProperty("name", "audiogroup_default"),
                         new JProperty("path", "audiogroups/audiogroup_default") // GMS2 path for default
                     );
                 }


                // Create Sound .yy file content (JSON)
                 var yyContent = new JObject(
                     new JProperty("compression", GetGMS2CompressionType(sound.Type, Path.GetExtension(audioFilePath).ToLowerInvariant())), // Map compression/type
                     new JProperty("volume", (float)sound.Volume),
                     new JProperty("preload", sound.Flags.HasFlag(UndertaleSound.AudioEntryFlags.Preload)),
                     new JProperty("bitRate", (int)sound.Bitrate), // Assuming kbps
                     new JProperty("sampleRate", (int)sound.SampleRate), // Use original sample rate
                     new JProperty("type", GetGMS2SoundType(sound.Type)), // Map type (Mono/Stereo/3D)
                     new JProperty("bitDepth", 1), // 1 = 16-bit (common default), 0 = 8-bit. UT likely used 16-bit.
                     new JProperty("audioGroupId", audioGroupRef),
                     new JProperty("soundFile", Path.GetFileName(audioFilePath)), // Just the filename
                     new JProperty("duration", 0.0f), // GMS2 calculates this, set default 0
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Sounds"),
                         new JProperty("path", "folders/Sounds.yy")
                     )),
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", soundName),
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMSound")
                 );

                WriteJsonFile(yyPath, yyContent);

                // Add to project resources
                string relativePath = $"sounds/{soundName}/{soundName}.yy";
                AddResourceToProject(soundName, soundGuid, relativePath, "GMSound", "sounds");
                 ResourcePaths[resourceKey] = relativePath;

            }
            catch (Exception ex)
            {
                 UMT.Log($"Error processing sound '{soundName}': {ex.Message}");
            }
        }
         UMT.Log("Sound conversion finished.");
    }

     // Attempt to make the audio filename match the resource name, keeping extension
     private string GetCompatibleAudioFileName(string originalFileName, string resourceName) {
          string extension = Path.GetExtension(originalFileName); // .ogg, .wav
          if (string.IsNullOrEmpty(extension)) {
               // Try to guess from data? Unreliable. Assume .ogg as common format.
               extension = ".ogg";
          }
          return resourceName + extension;
     }

     // Map UndertaleSound.AudioTypeFlags to GMS2 sound type enum
     private int GetGMS2SoundType(UndertaleSound.AudioTypeFlags utFlags) {
          // GMS2 type: 0 = Mono, 1 = Stereo, 2 = 3D
          // UT flags are complex bitmask. Need to check common combinations.
          // Often, bit 0 (Regular Play) is set. Bit 1 might indicate 3D?
          // Let's assume Stereo as default unless specific flags suggest otherwise.
          // This needs verification based on actual UT sound usage.
          // Example: if (utFlags.HasFlag(Some3DFlag)) return 2;
          // Example: if (utFlags.HasFlag(SomeMonoFlag)) return 0;
          return 1; // Default to Stereo
     }

      // Map UndertaleSound.AudioTypeFlags and extension to GMS2 compression enum
     private int GetGMS2CompressionType(UndertaleSound.AudioTypeFlags utFlags, string extension) {
         // GMS2 compression: 0 = Uncompressed, 1 = Compressed, 2 = UncompressOnLoad, 3 = CompressedStreamed
         // UT flags: Bit 2 = Use Original File (Uncompressed?), Bit 3 = Stream from Disk?
         bool isCompressed = !utFlags.HasFlag(UndertaleSound.AudioTypeFlags.UseOriginalFile); // Guess
         bool isStreamed = utFlags.HasFlag(UndertaleSound.AudioTypeFlags.StreamFromDisk);

         // GMS2 streaming usually applies only to compressed OGG/MP3
         if (isStreamed && (extension == ".ogg" || extension == ".mp3")) return 3; // Compressed Streamed
         if (isCompressed) return 1; // Compressed (in memory)
         if (extension == ".wav") return 0; // Uncompressed WAV
         // Default for other cases (e.g., uncompressed OGG?) - maybe UncompressOnLoad?
         return 2; // Uncompress on Load (safer default for non-WAV uncompressed)
     }

    private void ConvertObjects()
    {
        UMT.Log("Converting Objects...");
        if (Data.Objects == null) return;

        string objDir = Path.Combine(OutputPath, "objects");

        foreach (var obj in Data.Objects)
        {
            if (obj == null || obj.Name == null) continue;
            string objName = GetResourceName(obj);
            string resourceKey = $"objects/{objName}";
            Guid objGuid = GetResourceGuid(resourceKey);
            string objPath = Path.Combine(objDir, objName);
            string yyPath = Path.Combine(objPath, $"{objName}.yy");

             UMT.Log($"Converting Object: {objName}");

            try
            {
                Directory.CreateDirectory(objPath);
                CreatedDirectories.Add(objPath);

                List<JObject> eventList = new List<JObject>();

                // Process Events
                if (obj.Events != null)
                {
                    foreach (var eventContainer in obj.Events)
                    {
                        if (eventContainer == null) continue;
                        // GMS2 uses EventType + EventNumber (e.g., Collision with obj_X -> type=4, number=index_of_obj_X)
                        // Need mapping from GMS1 event types/subtypes to GMS2
                        foreach(var ev in eventContainer) {
                             if (ev == null || ev.Actions == null || ev.Actions.Count == 0) continue;
                             // In GMS1 data (like UMT models), events often contain Actions.
                             // Each Action *might* have associated code (if it's a Code action).
                             // We need the *decompiled code* associated with this specific event.
                             // UMT's `Data.Code` usually links `UndertaleCode` entries back to the object/script/event they belong to.

                             UndertaleCode associatedCode = FindCodeForEvent(obj, ev.EventType, ev.EventSubtype);
                             string gmlCode = "// Event code not found or decompiled\n";
                             if (associatedCode != null && associatedCode.Decompiled != null) {
                                  gmlCode = associatedCode.Decompiled.ToString(Data, true); // true for resolving names
                                   // Basic GML Compatibility Fixes (VERY basic examples)
                                   // This should ideally be a much more sophisticated process
                                   // gmlCode = gmlCode.Replace("self.", ""); // GMS2 doesn't need self usually
                                   // gmlCode = gmlCode.Replace("argument[", "argument"); // Array access deprecated
                                   // ... add more replacements ...
                             } else if (WARN_ON_MISSING_DECOMPILED_CODE) {
                                  UMT.Log($"Warning: Decompiled code not found for Object '{objName}' Event: Type={ev.EventType}, Subtype={ev.EventSubtype}");
                             }


                             // Map GMS1 Event Type/Subtype to GMS2 Event Type/Number
                              GMS2EventMapping mapping = MapGMS1EventToGMS2(ev.EventType, ev.EventSubtype, objName);
                              if (mapping == null) continue; // Skip unmappable events


                             // Write GML code to file
                             string gmlFileName = $"{(mapping.IsCollisionEvent ? "Collision_" : "")}{mapping.GMS2EventTypeName}_{mapping.GMS2EventNumber}.gml";
                             string gmlFilePath = Path.Combine(objPath, SanitizeFileName(gmlFileName)); // Sanitize just in case subtype name had issues
                             File.WriteAllText(gmlFilePath, gmlCode);

                             // Create event entry for .yy file
                             var eventEntry = new JObject(
                                 new JProperty("collisionObjectId", mapping.CollisionObjectRef), // Null if not collision, else object reference
                                 new JProperty("eventNum", mapping.GMS2EventNumber),
                                 new JProperty("eventType", mapping.GMS2EventType),
                                 new JProperty("isDnD", false), // Drag and Drop flag
                                 new JProperty("resourceVersion", "1.0"),
                                 new JProperty("name", ""), // Optional name field, usually empty
                                 new JProperty("tags", new JArray()),
                                 new JProperty("resourceType", "GMEvent")
                             );
                             eventList.Add(eventEntry);
                        }

                    }
                }


                 // Get Sprite, Parent, Mask References
                 JObject spriteRef = CreateResourceReference(obj.Sprite, "sprites");
                 JObject parentRef = CreateResourceReference(obj.ParentId, "objects"); // ParentId points to another UndertaleObject
                 JObject maskRef = CreateResourceReference(obj.MaskSprite, "sprites"); // MaskSprite is separate in GMS1

                 // GMS2 Physics Properties (Defaults if not using physics)
                 // UT didn't use the built-in physics engine heavily, so defaults are likely fine.
                 bool physicsObject = false; // Assume false unless properties suggest otherwise
                 float density = 0.5f;
                 float restitution = 0.1f;
                 int group = 0;
                 float linearDamping = 0.1f;
                 float angularDamping = 0.1f;
                 float friction = 0.2f;
                 bool awake = true;
                 bool kinematic = false;
                 int shape = 1; // 0: Circle, 1: Box, 2: Convex Shape
                  // Placeholder: If obj.PhysicsShape is available and mapped, use it
                  // if (obj.PhysicsShape == UndertaleObject.PhysicsShapeType.Circle) shape = 0;
                  // else if (obj.PhysicsShape == UndertaleObject.PhysicsShapeType.Box) shape = 1;
                  // ... map other physics properties ...


                 // Create Object .yy file content (JSON)
                 var yyContent = new JObject(
                     new JProperty("spriteId", spriteRef),
                     new JProperty("solid", obj.Solid),
                     new JProperty("visible", obj.Visible),
                     new JProperty("persistent", obj.Persistent),
                     new JProperty("physicsObject", physicsObject), // obj.PhysicsObject if available and mapped
                     new JProperty("physicsSensor", false), // obj.PhysicsSensor if available
                     new JProperty("physicsShape", shape),
                     new JProperty("physicsGroup", group),
                     new JProperty("physicsDensity", density),
                     new JProperty("physicsRestitution", restitution),
                     new JProperty("physicsLinearDamping", linearDamping),
                     new JProperty("physicsAngularDamping", angularDamping),
                     new JProperty("physicsFriction", friction),
                     new JProperty("physicsStartAwake", awake),
                     new JProperty("physicsKinematic", kinematic),
                     new JProperty("physicsShapePoints", new JArray()), // Define shape points if physicsShape=2
                     new JProperty("eventList", new JArray(eventList)),
                     new JProperty("properties", new JArray()), // For Object Variables defined in IDE (UT doesn't have this directly, parse from code?)
                     new JProperty("overriddenProperties", new JArray()), // For child objects overriding parent properties
                     new JProperty("parentObjectId", parentRef),
                     new JProperty("spriteMaskId", maskRef), // Use the separate mask if defined
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Objects"),
                         new JProperty("path", "folders/Objects.yy")
                     )),
                      new JProperty("resourceVersion", "1.0"),
                      new JProperty("name", objName),
                      new JProperty("tags", new JArray()),
                      new JProperty("resourceType", "GMObject")
                 );


                WriteJsonFile(yyPath, yyContent);

                // Add to project resources
                 string relativePath = $"objects/{objName}/{objName}.yy";
                 AddResourceToProject(objName, objGuid, relativePath, "GMObject", "objects");
                  ResourcePaths[resourceKey] = relativePath;

            }
            catch (Exception ex)
            {
                 UMT.Log($"Error processing object '{objName}': {ex.Message}\n{ex.StackTrace}");
            }
        }
         UMT.Log("Object conversion finished.");
    }

      // Helper to find the decompiled code associated with a specific object event
     private UndertaleCode FindCodeForEvent(UndertaleObject obj, UndertaleInstruction.EventType eventType, int eventSubtype) {
         if (Data.Code == null) return null;

         // UMT stores links from Code entries back to their owners (Object, Script, Timeline, etc.)
         // Find the Code entry whose `ParentEntry` matches the object and has matching event type/subtype.
          // This requires knowing how UMT links them. Let's assume `Code.ParentEntry` exists and can be cast or checked.
          // The exact property names might differ in your UMT version. Inspect Data.Code[n] in the debugger.

           // Hypothetical example (adjust based on actual UMT structure):
         /*
         foreach (UndertaleCode codeEntry in Data.Code) {
             if (codeEntry.ParentEntry is UndertaleGameObjectEvent owningEvent) { // Check if parent is an event
                 if (owningEvent.Parent == obj && // Check if event belongs to the correct object
                     owningEvent.EventType == eventType &&
                     owningEvent.EventSubtype == eventSubtype) // Or however subtypes are linked
                 {
                     return codeEntry; // Found the code
                 }
             }
              // Alternative linking: Sometimes Code might link directly to Object and store event info
               else if (codeEntry.ParentEntry == obj) {
                    // Need to check if this code entry *also* stores event type/subtype info
                    // This depends heavily on UMT's internal structure for `UndertaleCode`
                    // if (codeEntry.AssociatedEventType == eventType && codeEntry.AssociatedEventSubtype == eventSubtype) {
                    //    return codeEntry;
                    // }
               }
         }
         */

          // More robust approach: Iterate the object's events and find the matching one, then get its Code object.
          // This assumes the UndertaleEvent object itself holds a reference to its UndertaleCode.
          if (obj.Events != null) {
              foreach (var eventList in obj.Events) {
                  foreach (var ev in eventList) {
                       if (ev.EventType == eventType && ev.EventSubtype == eventSubtype) {
                            // Now, how does 'ev' link to its code?
                            // Common pattern: The event has Actions, and one action is type "Execute Code"
                            // which contains the reference.
                            var codeAction = ev.Actions?.FirstOrDefault(a => a.LibID == 1 && a.Kind == 7 && a.Function.Name.Content == "action_execute_script"); // Kind 7 = Code
                             if (codeAction != null && codeAction.Arguments.Count > 0) {
                                 var codeResourceRef = codeAction.Arguments[0]; // Argument might hold Code ID or index
                                  // Assuming argument holds the index/ID of the UndertaleCode entry
                                  // Need to parse codeResourceRef.Address or similar property.
                                  // This part is tricky and needs UMT specific knowledge.

                                   // Simpler approach: UMT might pre-link UndertaleCode to the specific UndertaleEvent object
                                   // Check if UndertaleCode has a property like `AssociatedEvent`
                                   foreach (UndertaleCode codeEntry in Data.Code) {
                                        // Hypothetical: Adjust property names based on actual UMT structure
                                        // if (codeEntry.AssociatedEvent == ev) {
                                        //     return codeEntry;
                                        // }
                                        // Another possibility: Code name might match object name + event type
                                         string expectedCodeName = $"{obj.Name.Content}__{eventType}_{eventSubtype}"; // Example naming convention
                                         if (codeEntry.Name.Content == expectedCodeName) {
                                             // Need confirmation this naming convention is used by UMT's decompiler
                                              // return codeEntry;
                                         }
                                   }

                                    // Last resort: Search code entries by Parent ID if available
                                     foreach (UndertaleCode codeEntry in Data.Code) {
                                          // if (codeEntry.ParentId == obj.Id && codeEntry.EventType == eventType && ...) { ... } // If such properties exist
                                     }

                                // If we found the code action, try finding the corresponding code entry by its name or ID
                                // This part is highly dependent on how UMT's decompiler links code actions to actual code entries.
                                // Placeholder: Return null if logic is not implemented yet.
                                return null; // Placeholder - Requires specific UMT knowledge

                           } else {
                               // Event might have no code action or structure is different
                           }

                       }
                  }
              }
          }


         return null; // Not found
     }


      // Class to hold GMS2 event mapping results
     private class GMS2EventMapping {
         public int GMS2EventType { get; set; }
         public int GMS2EventNumber { get; set; }
         public string GMS2EventTypeName { get; set; } // For filename clarity
         public JObject CollisionObjectRef { get; set; } = null; // Only for collision events
          public bool IsCollisionEvent => CollisionObjectRef != null;
     }

      // Maps GMS 1.x Event Type and Subtype to GMS2 Event Type and Number
     private GMS2EventMapping MapGMS1EventToGMS2(UndertaleInstruction.EventType eventType, int eventSubtype, string currentObjName) {
          // GMS2 Event Types:
          // 0: Create
          // 1: Destroy
          // 2: Alarm (Number 0-11)
          // 3: Step (Number 0: Step, 1: Begin Step, 2: End Step)
          // 4: Collision (Number: Object Index)
          // 5: Keyboard (Number: Keycode)
          // 6: Mouse (Number: 0: Left Button, 1: Right Button, 2: Middle Button, 3: No Button,
          //                  4: Left Pressed, 5: Right Pressed, 6: Middle Pressed,
          //                  7: Left Released, 8: Right Released, 9: Middle Released,
          //                  10: Mouse Enter, 11: Mouse Leave,
          //                  30: Mouse Wheel Up, 31: Mouse Wheel Down,
          //                  50: Global Left Button, ..., 55: Global Middle Released
          //                  56: Joystick 1 Button 1 ... up to 2 Axis 8
          //                  60: Gesture Tap, 61: Drag Start ... ) -> Very complex mapping
          // 7: Other (Number: 0: Outside Room, 1: Intersect Boundary, 2: Game Start, 3: Game End,
          //                 4: Room Start, 5: Room End, 6: No More Lives, 7: Animation End,
          //                 8: End of Path, 9: No More Health,
          //                 10: User Event 0-15,
          //                 25: Broadcast Message,
          //                 30: Close Button, 31: Gamepad Button Down...,
          //                 40: View 0 Outside... 47: View 7 Outside
          //                 50: View 0 Boundary... 57: View 7 Boundary
          //                 60: System Event Async, 62: Async Image Loaded, 63: Async Sound Loaded,... 75: Async Networking
          //                 76: Push Notification Event (iOS/Android)
          // 8: Draw (Number: 0: Draw, 1: Draw GUI, 64: Draw Begin, 65: Draw End, 72: Window Resize,
          //               73: Draw GUI Begin, 74: Draw GUI End, 75: Pre-Draw, 76: Post-Draw)
          // 9: Key Press (Number: Keycode)
          // 10: Key Release (Number: Keycode)
          // 11: Trigger (Rarely used, mapping unclear)

          // --- Mapping Logic ---
          switch (eventType) {
              case UndertaleInstruction.EventType.Create:
                  return new GMS2EventMapping { GMS2EventType = 0, GMS2EventNumber = 0, GMS2EventTypeName = "Create" };
              case UndertaleInstruction.EventType.Destroy:
                   return new GMS2EventMapping { GMS2EventType = 1, GMS2EventNumber = 0, GMS2EventTypeName = "Destroy" };
              case UndertaleInstruction.EventType.Alarm:
                   if (eventSubtype >= 0 && eventSubtype <= 11) {
                        return new GMS2EventMapping { GMS2EventType = 2, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"Alarm{eventSubtype}" };
                   }
                   break; // Invalid alarm number
              case UndertaleInstruction.EventType.Step:
                  // GMS1 Subtypes: 0=Normal, 1=Begin, 2=End
                  // GMS2 Numbers: 0=Normal, 1=Begin, 2=End
                  if (eventSubtype >= 0 && eventSubtype <= 2) {
                       string stepTypeName = eventSubtype == 0 ? "Step" : (eventSubtype == 1 ? "BeginStep" : "EndStep");
                       return new GMS2EventMapping { GMS2EventType = 3, GMS2EventNumber = eventSubtype, GMS2EventTypeName = stepTypeName };
                  }
                   break;
              case UndertaleInstruction.EventType.Collision:
                   // Subtype is the Object Index of the object being collided with
                   if (eventSubtype >= 0 && eventSubtype < Data.Objects.Count) {
                        var collisionObj = Data.Objects[eventSubtype];
                        if(collisionObj != null) {
                             string colObjName = GetResourceName(collisionObj);
                             Guid colObjGuid = GetResourceGuid($"objects/{colObjName}"); // Must have been processed or pre-generated
                             JObject colRef = CreateResourceReference(colObjName, colObjGuid, "objects");
                              if (colRef != null) {
                                 return new GMS2EventMapping {
                                     GMS2EventType = 4,
                                     GMS2EventNumber = eventSubtype, // GMS2 uses object index here too
                                     GMS2EventTypeName = colObjName, // Use object name for filename
                                     CollisionObjectRef = colRef
                                 };
                              } else {
                                    UMT.Log($"Warning: Could not create reference for collision object '{colObjName}' (Index: {eventSubtype}) in object '{currentObjName}'. Skipping event.");
                              }

                        } else {
                              UMT.Log($"Warning: Collision event in '{currentObjName}' references invalid object index {eventSubtype}. Skipping event.");
                        }
                   }
                   break; // Invalid object index for collision
              case UndertaleInstruction.EventType.Keyboard: // GMS1 "Keyboard" (continuous press) maps to GMS2 "Keyboard"
                  return new GMS2EventMapping { GMS2EventType = 5, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"Keyboard_{eventSubtype}" }; // Subtype is keycode
              case UndertaleInstruction.EventType.Mouse:
                    // This requires complex mapping from GMS1 subtypes (0-11?) to GMS2 (0-11, 30-31, 50-55 etc.)
                    // Simple cases:
                    if (eventSubtype >= 0 && eventSubtype <= 9) { // Direct map for basic button events? Needs verification. GMS1 had fewer mouse events.
                         // GMS1: 0=Left, 1=Right, 2=Middle, 3=No Button?, 4=Left Press, 5=Right Press, 6=Middle Press, 7=Left Release, 8=Right Release, 9=Middle Release, 10=Enter, 11=Leave
                         // GMS2: 0=Left Button, 1=Right Button, 2=Middle Button, 3=No Button, 4=Left Pressed, 5=Right Pressed, 6=Middle Pressed, 7=Left Released, 8=Right Released, 9=Middle Released, 10: Mouse Enter, 11: Mouse Leave
                         // Looks like a direct mapping for 0-11 might work initially.
                         string mouseEventName = $"Mouse_{eventSubtype}"; // Generic name for now
                         // Assign more descriptive names if possible
                         string[] gms2MouseEventNames = { "LeftButton", "RightButton", "MiddleButton", "NoButton", "LeftPressed", "RightPressed", "MiddlePressed", "LeftReleased", "RightReleased", "MiddleReleased", "MouseEnter", "MouseLeave" };
                         if(eventSubtype < gms2MouseEventNames.Length) mouseEventName = gms2MouseEventNames[eventSubtype];

                         return new GMS2EventMapping { GMS2EventType = 6, GMS2EventNumber = eventSubtype, GMS2EventTypeName = mouseEventName };
                    }
                    // Handle Wheel Up/Down? GMS1 might encode these differently. Assume GMS2 30/31 if detected.
                    // Handle Global events? GMS1 usually didn't have separate global mouse.
                    // Handle Gestures? Unlikely in UT.
                     UMT.Log($"Warning: Unhandled GMS1 Mouse event subtype {eventSubtype} in object '{currentObjName}'. Skipping.");
                    break;
              case UndertaleInstruction.EventType.Other:
                    // GMS1 Subtypes: 0=OutsideRoom, 1=IntersectBoundary, 2=GameStart, 3=GameEnd, 4=RoomStart, 5=RoomEnd, 6=NoMoreLives, 7=AnimationEnd, 8=EndOfPath, 9=NoMoreHealth, 10=UserEvent0-15
                    // GMS2 Other Numbers: Match GMS1 for 0-9. User Events 10-25 map to GMS1 10-25 (User 0-15). Async events are new in GMS2.
                    if (eventSubtype >= 0 && eventSubtype <= 9) { // Direct mapping for standard 'Other' events
                        string[] otherEventNames = { "OutsideRoom", "IntersectBoundary", "GameStart", "GameEnd", "RoomStart", "RoomEnd", "NoMoreLives", "AnimationEnd", "EndOfPath", "NoMoreHealth"};
                        return new GMS2EventMapping { GMS2EventType = 7, GMS2EventNumber = eventSubtype, GMS2EventTypeName = otherEventNames[eventSubtype] };
                    } else if (eventSubtype >= 10 && eventSubtype <= 25) { // User Events (GMS1 User 0-15 map to GMS2 User 0-15, Number = 10 + UserEventIndex)
                         int userEventIndex = eventSubtype - 10;
                         return new GMS2EventMapping { GMS2EventType = 7, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"UserEvent{userEventIndex}" };
                    }
                     UMT.Log($"Warning: Unhandled GMS1 Other event subtype {eventSubtype} in object '{currentObjName}'. Skipping.");
                    break;
              case UndertaleInstruction.EventType.Draw:
                    // GMS1 Subtypes: 0=Draw, 1=DrawGUI?, 2=Resize?, 3=DrawBegin?, 4=DrawEnd?, 5=PreDraw?, 6=PostDraw? -> Mapping is uncertain.
                    // GMS2 Numbers: 0=Draw, 1=Draw GUI, 64=Draw Begin, 65=Draw End, 72=Window Resize, 73=Draw GUI Begin, 74=Draw GUI End, 75=Pre-Draw, 76=Post-Draw
                    // Simplest assumption: GMS1 Draw(0) -> GMS2 Draw(0)
                    if (eventSubtype == 0) {
                         return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 0, GMS2EventTypeName = "Draw" };
                    }
                     // Map Draw GUI if GMS1 subtype 1 was used for it
                     else if (eventSubtype == 1) { // Assuming GMS1 subtype 1 maps to GMS2 Draw GUI
                          return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 1, GMS2EventTypeName = "DrawGUI" };
                     }
                     // Add mappings for Begin/End/Pre/Post Draw if GMS1 used specific subtypes for them.
                     // Example (Guess):
                     // else if (eventSubtype == 5) { // GMS1 Pre Draw?
                     //      return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 75, GMS2EventTypeName = "PreDraw" };
                     // }
                     // else if (eventSubtype == 6) { // GMS1 Post Draw?
                     //      return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 76, GMS2EventTypeName = "PostDraw" };
                     // }
                     // else if (eventSubtype == 73) { // GMS1 Draw GUI Begin?
                     //      return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 73, GMS2EventTypeName = "DrawGuiBegin" };
                     // }
                      // else if (eventSubtype == 74) { // GMS1 Draw GUI End?
                     //      return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 74, GMS2EventTypeName = "DrawGuiEnd" };
                     // }

                     UMT.Log($"Warning: Unhandled GMS1 Draw event subtype {eventSubtype} in object '{currentObjName}'. Defaulting to Draw(0) event.");
                     // Fallback to standard draw if subtype unknown
                     return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 0, GMS2EventTypeName = "Draw" };
              case UndertaleInstruction.EventType.KeyPress: // GMS1 KeyPress (single press) maps to GMS2 Key Press
                    return new GMS2EventMapping { GMS2EventType = 9, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"KeyPress_{eventSubtype}" }; // Subtype is keycode
              case UndertaleInstruction.EventType.KeyRelease: // GMS1 KeyRelease maps to GMS2 Key Release
                    return new GMS2EventMapping { GMS2EventType = 10, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"KeyRelease_{eventSubtype}" }; // Subtype is keycode
               case UndertaleInstruction.EventType.Trigger: // GMS1 Trigger events - mapping to GMS2 Trigger (11) is unclear. Rarely used.
                    UMT.Log($"Warning: GMS1 Trigger events are not directly supported/mapped to GMS2. Skipping Trigger event {eventSubtype} in object '{currentObjName}'.");
                   break; // Skip Trigger events
              default:
                   UMT.Log($"Warning: Unknown GMS1 EventType {eventType} encountered in object '{currentObjName}'. Skipping.");
                   break; // Unknown event type
          }

          return null; // Event could not be mapped
      }


    private void ConvertRooms()
    {
        UMT.Log("Converting Rooms...");
        if (Data.Rooms == null) return;

        string roomDir = Path.Combine(OutputPath, "rooms");

        foreach (var room in Data.Rooms)
        {
            if (room == null || room.Name == null) continue;
            string roomName = GetResourceName(room);
             string resourceKey = $"rooms/{roomName}";
             Guid roomGuid = GetResourceGuid(resourceKey);
            string roomPath = Path.Combine(roomDir, roomName);
            string yyPath = Path.Combine(roomPath, $"{roomName}.yy");

             UMT.Log($"Converting Room: {roomName}");

            try
            {
                Directory.CreateDirectory(roomPath);
                CreatedDirectories.Add(roomPath);

                // --- Room Settings ---
                 var settings = new JObject(
                     new JProperty("isDnD", false),
                     new JProperty("volume", 1.0f),
                     new JProperty("parentRoom", null), // Parent room concept less direct in GMS2 via room manager
                     new JProperty("sequenceId", null), // For room-level sequence playback
                     new JProperty("roomSettings", new JObject(
                         new JProperty("inheritRoomSettings", false),
                         new JProperty("Width", room.Width),
                         new JProperty("Height", room.Height),
                         new JProperty("persistent", room.Persistent)
                     )),
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", "Default"), // Room settings object name
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMRoomSettings")
                 );

                // --- Views ---
                 List<JObject> viewList = new List<JObject>();
                 if (room.ViewsEnabled && room.Views != null) {
                      for(int i=0; i < room.Views.Count; i++) {
                          var view = room.Views[i];
                          if (!view.Enabled) continue;

                           // Find object being followed (if any)
                           JObject followRef = null;
                            if(view.ObjectId >= 0 && view.ObjectId < Data.Objects.Count) {
                                 var followObj = Data.Objects[view.ObjectId];
                                 if (followObj != null) {
                                     followRef = CreateResourceReference(followObj, "objects");
                                 }
                            }

                           viewList.Add(new JObject(
                               new JProperty("inherit", false),
                               new JProperty("visible", view.Enabled), // GMS1 Enabled maps to GMS2 Visible
                               new JProperty("xview", view.ViewX),
                               new JProperty("yview", view.ViewY),
                               new JProperty("wview", view.ViewWidth),
                               new JProperty("hview", view.ViewHeight),
                               new JProperty("xport", view.PortX),
                               new JProperty("yport", view.PortY),
                               new JProperty("wport", view.PortWidth),
                               new JProperty("hport", view.PortHeight),
                               new JProperty("hborder", view.BorderX),
                               new JProperty("vborder", view.BorderY),
                               new JProperty("hspeed", view.SpeedX),
                               new JProperty("vspeed", view.SpeedY),
                               new JProperty("objectId", followRef),
                               new JProperty("resourceVersion", "1.0"),
                               new JProperty("name", $"view_{i}"), // Default view name
                               new JProperty("tags", new JArray()),
                               new JProperty("resourceType", "GMView")
                           ));
                      }
                 }

                 var views = new JObject(
                     new JProperty("inheritViewSettings", false),
                     new JProperty("enableViews", room.ViewsEnabled),
                     new JProperty("clearViewBackground", room.ClearDisplayBuffer), // GMS1 Clear Buffer maps to GMS2 Clear View Background
                     new JProperty("clearDisplayBuffer", room.ClearScreen), // GMS1 Clear Screen maps to GMS2 Clear Display Buffer
                     new JProperty("views", new JArray(viewList)), // Add the list of views
                      new JProperty("resourceVersion", "1.0"),
                      new JProperty("name", "Default"), // View settings object name
                      new JProperty("tags", new JArray()),
                      new JProperty("resourceType", "GMRoomViewSettings")
                 );


                // --- Layers (Backgrounds, Instances, Tiles) ---
                List<JObject> layerList = new List<JObject>();
                int layerDepthCounter = 10000; // Start with a high depth and decrease for proper layering

                // Convert Background Layers
                if (room.Backgrounds != null)
                {
                    // Sort backgrounds by depth (ascending, so lowest depth is drawn first)
                     // GMS1 depth is higher = further back. GMS2 depth is lower = further back. Reverse sort needed?
                     // Let's process in original order, but assign decreasing GMS2 depth values.
                     var sortedBackgrounds = room.Backgrounds // No, process in defined order
                        // .OrderByDescending(bg => bg.Depth) // GMS1 depth logic
                        .ToList();

                    for (int i = 0; i < sortedBackgrounds.Count; i++)
                    {
                        var bg = sortedBackgrounds[i];
                        if (!bg.Enabled) continue;

                        JObject spriteRef = null;
                        string bgName = $"layer_bg_{i}";
                        if (bg.BackgroundId >= 0 && bg.BackgroundId < Data.Backgrounds.Count) {
                            var bgResource = Data.Backgrounds[bg.BackgroundId];
                             if (bgResource != null) {
                                 // Use the pseudo-sprite created earlier from this background
                                 spriteRef = CreateResourceReference(bgResource, "sprites");
                                 bgName = GetResourceName(bgResource); // Use the resource name if available
                             }
                        } else {
                             UMT.Log($"Warning: Background layer {i} in room '{roomName}' has invalid BackgroundId {bg.BackgroundId}.");
                             // Optionally create a placeholder layer or skip
                        }

                         // If spriteRef is still null (e.g., invalid ID or background wasn't converted), skip or create placeholder
                         if (spriteRef == null) {
                              UMT.Log($"Warning: Could not find sprite for background layer {i} in room '{roomName}'. Skipping layer.");
                             continue;
                         }


                        layerList.Add(new JObject(
                            new JProperty("visible", bg.Enabled),
                            new JProperty("depth", bg.Depth), // Use original depth for GMS2 (lower = front)
                            new JProperty("spriteId", spriteRef),
                            new JProperty("colour", new JObject( // Background color tint (usually white)
                                new JProperty("r", 255), new JProperty("g", 255), new JProperty("b", 255), new JProperty("a", 255)
                            )),
                            new JProperty("x", bg.X),
                            new JProperty("y", bg.Y),
                            new JProperty("htiled", bg.HTiled),
                            new JProperty("vtiled", bg.VTiled),
                            new JProperty("hspeed", (float)bg.HSpeed),
                            new JProperty("vspeed", (float)bg.VSpeed),
                            new JProperty("stretch", bg.Stretch), // GMS1 Stretch flag
                            new JProperty("animationFPS", 15), // Default FPS if not specified
                            new JProperty("animationSpeedType", 0), // 0 = FPS
                            new JProperty("userdefined_depth", true), // Indicate that the depth value is explicitly set
                            new JProperty("isLocked", false),
                            new JProperty("blendtype", 0), // Normal blend
                            new JProperty("properties", new JArray()),
                             new JProperty("resourceVersion", "1.0"),
                             new JProperty("name", SanitizeFileName($"{bgName}_layer_{Guid.NewGuid().ToString().Substring(0,4)}")), // Unique layer name
                             new JProperty("tags", new JArray()),
                             new JProperty("resourceType", "GMRBackgroundLayer")
                        ));
                         // layerDepthCounter -= 10; // Assign decreasing depth
                    }
                }

                 // Convert Instance Layers
                 if (room.Instances != null) {
                      // Group instances by depth? GMS2 uses layers per depth.
                      // Simpler approach: Create one main instance layer. GMS2 handles depth sorting within the layer.
                       List<JObject> instanceRefs = new List<JObject>();
                       foreach(var inst in room.Instances) {
                            if (inst.ObjectDefinition == null) continue;

                            string objName = GetResourceName(inst.ObjectDefinition);
                            Guid objGuid = GetResourceGuid($"objects/{objName}");
                            JObject objRef = CreateResourceReference(objName, objGuid, "objects");
                            if (objRef == null) {
                                  UMT.Log($"Warning: Could not find object '{objName}' for instance ID {inst.InstanceID} in room '{roomName}'. Skipping instance.");
                                 continue;
                            }

                            // Instance Creation Code
                             string creationCode = "";
                             if (inst.CreationCode != null && inst.CreationCode.Decompiled != null) {
                                 creationCode = inst.CreationCode.Decompiled.ToString(Data, true);
                             } else if (inst.CreationCodeId != System.UInt32.MaxValue && WARN_ON_MISSING_DECOMPILED_CODE) {
                                // Need to find the code entry by ID if not directly linked
                                var codeEntry = Data.Code.FirstOrDefault(c => c.Offset == inst.CreationCodeId); // Assuming ID maps to offset or unique ID
                                 if (codeEntry != null && codeEntry.Decompiled != null) {
                                      creationCode = codeEntry.Decompiled.ToString(Data, true);
                                 } else {
                                      UMT.Log($"Warning: Could not find/decompile creation code (ID: {inst.CreationCodeId}) for instance ID {inst.InstanceID} (Object: {objName}) in room '{roomName}'.");
                                 }

                             }

                            instanceRefs.Add(new JObject(
                                new JProperty("x", (float)inst.X),
                                new JProperty("y", (float)inst.Y),
                                new JProperty("objectId", objRef),
                                new JProperty("sequenceId", null), // For instance-specific sequences
                                new JProperty("layerId", null), // Set later when layer GUID is known
                                new JProperty("properties", new JArray()), // Instance variables overrides
                                new JProperty("rotation", (float)inst.Rotation), // Degrees
                                new JProperty("scaleX", (float)inst.ScaleX),
                                new JProperty("scaleY", (float)inst.ScaleY),
                                new JProperty("imageIndex", 0), // Default image index
                                new JProperty("imageSpeed", 1.0f), // Default image speed
                                new JProperty("colour", ColorToGMS2JObject(inst.Color)), // Color tint
                                new JProperty("isLocked", false),
                                new JProperty("creationCodeFile", ""), // Store creation code inline? GMS2 often uses files. Let's try inline first.
                                new JProperty("creationCode", creationCode), // Store inline for simplicity? GMS2 might prefer file ref.
                                new JProperty("inheritCode", false),
                                new JProperty("instanceId", inst.InstanceID.ToString()), // Use original ID if possible, but GMS2 might reassign. Use for reference only. Needs unique GUID in reality.
                                new JProperty("resourceVersion", "1.0"),
                                new JProperty("name", $"{objName}_inst_{Guid.NewGuid().ToString().Substring(0,6)}"), // Generate unique instance name
                                new JProperty("tags", new JArray()),
                                new JProperty("resourceType", "GMRInstance")
                             ));
                       }

                        // Create the instance layer itself
                         Guid instanceLayerGuid = Guid.NewGuid();
                         var instanceLayer = new JObject(
                             new JProperty("instances", new JArray(instanceRefs)), // Add collected instances
                             new JProperty("visible", true),
                             new JProperty("depth", 0), // Default depth for main instance layer (adjust if needed)
                             new JProperty("userdefined_depth", false),
                             new JProperty("inheritLayerDepth", false),
                             new JProperty("inheritLayerSettings", false),
                             new JProperty("gridX", room.GridWidth > 0 ? room.GridWidth : 32), // Use room grid or default
                             new JProperty("gridY", room.GridHeight > 0 ? room.GridHeight : 32),
                             new JProperty("layers", new JArray()), // Sublayers for organisation (optional)
                             new JProperty("hierarchyFrozen", false),
                             new JProperty("effectEnabled", false), // Layer effects
                             new JProperty("effectType", null),
                             new JProperty("properties", new JArray()),
                             new JProperty("isLocked", false),
                              new JProperty("resourceVersion", "1.0"),
                              new JProperty("name", "Instances"), // Standard GMS2 layer name
                              new JProperty("tags", new JArray()),
                              new JProperty("resourceType", "GMRInstanceLayer")
                         );

                          // Update instance references with the layer ID (requires iterating again or modifying JObject)
                          // For simplicity, we might skip this back-reference or do it if essential for GMS2 loading.
                          // GMS2 format might actually define instances *within* the layer JSON. Let's check.
                          // Yes, instances are listed within the GMRInstanceLayer JSON. The code above reflects that.

                         layerList.Add(instanceLayer);
                         // layerDepthCounter -= 10; // Assign depth if managing multiple instance layers
                 }

                  // Convert Tile Layers
                  // This is the most complex part due to GMS1 vs GMS2 differences.
                  // GMS1: room.Tiles references Background resources and uses Tile IDs within that background's texture.
                  // GMS2: Tile Layers reference Tileset resources (which reference Sprites), and use tile indices based on the Tileset definition.
                  // Step 1: Ensure Tileset resources were created (ConvertTilesets function).
                  // Step 2: Map GMS1 Background IDs used in room.Tiles to the corresponding GMS2 Tileset resources.
                  // Step 3: Convert GMS1 tile data (X, Y, BgID, TileX, TileY, Width, Height, Depth, ID) to GMS2 tilemap format.
                   if (room.Tiles != null && room.Tiles.Any()) {
                        // Group tiles by depth and background ID (potential GMS2 layers)
                         var tilesByLayer = room.Tiles
                             .Where(t => t.BackgroundDefinition != null) // Ensure background reference is valid
                             .GroupBy(t => new { t.Depth, t.BackgroundDefinition })
                             .OrderBy(g => g.Key.Depth); // Process layers by depth

                         foreach (var layerGroup in tilesByLayer) {
                              var depth = layerGroup.Key.Depth;
                              var bgResource = layerGroup.Key.BackgroundDefinition;
                              string tileLayerName = $"Tiles_{GetResourceName(bgResource)}_{depth}";
                              UMT.Log($" -- Processing Tile Layer: {tileLayerName}");


                              // Find the GMS2 Tileset resource corresponding to this Background resource
                              string tilesetName = GetResourceName(bgResource); // Assumes Tileset name matches Background name
                              Guid tilesetGuid = GetResourceGuid($"tilesets/{tilesetName}");
                              JObject tilesetRef = CreateResourceReference(tilesetName, tilesetGuid, "tilesets");

                              if (tilesetRef == null) {
                                   UMT.Log($"Warning: Could not find Tileset resource '{tilesetName}' for tile layer in room '{roomName}'. Skipping layer.");
                                   continue;
                              }

                               // Create the tile data array for GMS2
                               // GMS2 uses a flat array representing the room grid, storing tile indices (or 0 for empty)
                               // Mapping GMS1 tile (X, Y, SourceX, SourceY, Width, Height, ID) to GMS2 index requires knowing the Tileset's layout.
                               // This is EXTREMELY complex to do automatically and accurately.
                               // Option A: Store raw GMS1 tile data in a custom format (requires GMS2 code to interpret).
                               // Option B: Attempt a best-effort conversion (likely inaccurate without perfect Tileset info).
                               // Option C: Create empty tile layers referencing the correct tileset (requires manual painting in GMS2).

                               // Let's try Option C (Empty Layer) for simplicity, adding a note.
                                UMT.Log($"Note: Creating EMPTY Tile Layer '{tileLayerName}'. Tiles must be manually placed in GMS2 using Tileset '{tilesetName}'.");

                                var tileDataArray = new JArray(); // Empty array for now
                                // If attempting Option B, you'd iterate `layerGroup` and calculate GMS2 tile indices based on `t.TileX`, `t.TileY` and the tileset sprite dimensions.


                               layerList.Add(new JObject(
                                   new JProperty("tilesetId", tilesetRef),
                                   new JProperty("x", 0), // Layer offset X
                                   new JProperty("y", 0), // Layer offset Y
                                   new JProperty("depth", depth), // Use original tile depth
                                   new JProperty("visible", true), // Assume visible unless data suggests otherwise
                                   new JProperty("userdefined_depth", true),
                                   new JProperty("inheritLayerDepth", false),
                                   new JProperty("inheritLayerSettings", false),
                                   new JProperty("gridX", room.GridWidth > 0 ? room.GridWidth : 32),
                                   new JProperty("gridY", room.GridHeight > 0 ? room.GridHeight : 32),
                                    // "tiles": { "TileDataFormat": 2, "SerialisedTileData": [...] } // GMS2 format
                                   new JProperty("tiles", new JObject(
                                        new JProperty("TileDataFormat", 2), // Current GMS2 format ID
                                        // Store the raw tile data as a base64 string or similar if needed for later manual processing
                                        // new JProperty("SerialisedTileData", ConvertTileDataToGMS2Format(layerGroup.ToList(), room.Width, room.Height, tilesetName /*needed for mapping*/))
                                        new JProperty("SerialisedTileData", new JArray()) // Empty for Option C
                                    )),
                                   new JProperty("isLocked", false),
                                   new JProperty("effectEnabled", false),
                                   new JProperty("effectType", null),
                                   new JProperty("properties", new JArray()),
                                    new JProperty("resourceVersion", "1.0"),
                                    new JProperty("name", SanitizeFileName(tileLayerName)),
                                    new JProperty("tags", new JArray()),
                                    new JProperty("resourceType", "GMRTileLayer")
                               ));
                                // layerDepthCounter -= 5; // Assign depth slightly differently from BGs/Instances
                         }
                   }


                // --- Room Creation Code ---
                string creationCodeContent = "// Room Creation Code not found or decompiled.\n";
                 UndertaleCode roomCode = null;
                 if (room.CreationCode != null) { // Check if directly linked
                      roomCode = Data.Code.FirstOrDefault(c => c == room.CreationCode); // Verify it's in the main list
                 } else if (room.CreationCodeId != System.UInt32.MaxValue) { // Check by ID
                      roomCode = Data.Code.FirstOrDefault(c => c.Offset == room.CreationCodeId); // Assuming ID maps to offset or unique ID
                 }

                 if (roomCode != null && roomCode.Decompiled != null) {
                     creationCodeContent = roomCode.Decompiled.ToString(Data, true);
                 } else if ((room.CreationCode != null || room.CreationCodeId != System.UInt32.MaxValue) && WARN_ON_MISSING_DECOMPILED_CODE) {
                      UMT.Log($"Warning: Could not find/decompile creation code for room '{roomName}'.");
                 }

                string creationCodeFilePath = Path.Combine(roomPath, "RoomCreationCode.gml");
                File.WriteAllText(creationCodeFilePath, creationCodeContent);


                // --- Main Room .yy File ---
                 var layerWrapper = new JObject(
                     new JProperty("layers", new JArray(layerList)), // The actual layer definitions
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", "Default"), // Layer folder object name
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMRoomLayerFolder")
                 );

                var yyContent = new JObject(
                     new JProperty("isDnD", false), // Drag and Drop flag for room
                     new JProperty("volume", 1.0f), // Room volume modifier? Usually 1.0
                     new JProperty("parentRoom", null), // See room settings
                     new JProperty("sequenceId", null), // Room sequence
                     new JProperty("roomSettings", settings), // Reference RoomSettings object
                     new JProperty("viewSettings", views), // Reference ViewSettings object
                     new JProperty("layerSettings", layerWrapper), // Embed Layer definitions
                     new JProperty("physicsSettings", new JObject( // Default physics settings
                         new JProperty("inheritPhysicsSettings", false),
                         new JProperty("PhysicsWorld", false), // Room uses physics? UT usually doesn't.
                         new JProperty("PhysicsWorldGravityX", 0.0f),
                         new JProperty("PhysicsWorldGravityY", 10.0f), // Default GMS2 gravity
                         new JProperty("PhysicsWorldPixToMetres", 0.1f) // Default scale
                     )),
                     new JProperty("creationCodeFile", Path.GetFileName(creationCodeFilePath)), // Relative path to GML file
                     new JProperty("inheritCode", false),
                     new JProperty("instanceCreationOrder", new JArray()), // GMS2 uses this to sort instance creation - complex to replicate GMS1 order accurately. Leave empty for now.
                     new JProperty("inheritCreationOrder", false),
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Rooms"),
                         new JProperty("path", "folders/Rooms.yy")
                     )),
                     new JProperty("resourceVersion", "1.4"), // GMRoom resource version
                     new JProperty("name", roomName),
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMRoom")
                 );


                WriteJsonFile(yyPath, yyContent);

                // Add to project resources
                string relativePath = $"rooms/{roomName}/{roomName}.yy";
                AddResourceToProject(roomName, roomGuid, relativePath, "GMRoom", "rooms");
                 ResourcePaths[resourceKey] = relativePath;

            }
            catch (Exception ex)
            {
                 UMT.Log($"Error processing room '{roomName}': {ex.Message}\n{ex.StackTrace}");
            }
        }
         UMT.Log("Room conversion finished.");
    }

      // Helper to convert System.Drawing.Color (or uint ABGR) to GMS2 JSON color object
      private JObject ColorToGMS2JObject(System.UInt32 abgrColor) {
           // GMS2 uses "colour": { "r": 0..255, "g": 0..255, "b": 0..255, "a": 0..255 }
           // Undertale uses ABGR format (Alpha, Blue, Green, Red)
           byte a = (byte)((abgrColor >> 24) & 0xFF);
           byte b = (byte)((abgrColor >> 16) & 0xFF);
           byte g = (byte)((abgrColor >> 8) & 0xFF);
           byte r = (byte)(abgrColor & 0xFF);

           // GMS1 color blending might differ from GMS2 interpretation.
           // GMS1 instance_change uses BGR, but room instance color tint (like Draw Color) might be ABGR? Check Undertale behavior.
           // Assume ABGR from room data based on common formats.

           // GMS2 expects premultiplied alpha for draw colors, but maybe not for instance tint?
           // Let's output non-premultiplied RGBA for the instance tint color property.
           return new JObject(
                new JProperty("r", r),
                new JProperty("g", g),
                new JProperty("b", b),
                new JProperty("a", a)
           );
      }

       // Placeholder for the complex GMS1 -> GMS2 tile data conversion
       // This would need the tileset layout, room dimensions, and tile list
       private JArray ConvertTileDataToGMS2Format(List<UndertaleRoom.Tile> tiles, int roomWidth, int roomHeight, string tilesetName) {
            // Create a 2D grid or flat array representing the room
            // For each GMS1 tile in `tiles`:
            // 1. Get its position (t.X, t.Y)
            // 2. Get its source position in the background/tileset image (t.TileX, t.TileY)
            // 3. Get the corresponding GMS2 Tileset resource (`tilesetName`)
            // 4. Determine the GMS2 tile index based on TileX, TileY, and the Tileset's definition (tile width, height, separation, offset). This is the hardest part.
            // 5. Place this index into the correct position (based on t.X, t.Y) in the GMS2 tile data array.
            // 6. Handle tile flipping/rotation if GMS1 stored it (UT usually doesn't use advanced GMS tile bits).
            // 7. Serialize the final array according to GMS2's "SerialisedTileData" format (often needs bit packing or specific structure).

             UMT.Log($"Warning: Accurate tile data conversion (ConvertTileDataToGMS2Format for {tilesetName}) is not implemented due to complexity. Tile layer will be empty.");
            return new JArray(); // Return empty array as placeholder
       }


    private void ConvertScripts()
    {
        UMT.Log("Converting Scripts...");
        if (Data.Scripts == null) return;

        string scriptDir = Path.Combine(OutputPath, "scripts");

        foreach (var script in Data.Scripts)
        {
            if (script == null || script.Name == null) continue;
            string scriptName = GetResourceName(script);
            string resourceKey = $"scripts/{scriptName}";
             Guid scriptGuid = GetResourceGuid(resourceKey);
            string scriptPath = Path.Combine(scriptDir, scriptName);
            string yyPath = Path.Combine(scriptPath, $"{scriptName}.yy");
            string gmlPath = Path.Combine(scriptPath, $"{scriptName}.gml");

             UMT.Log($"Converting Script: {scriptName}");

            try
            {
                Directory.CreateDirectory(scriptPath);
                CreatedDirectories.Add(scriptPath);

                // Get Decompiled Code
                string gmlCode = $"// Script code for {scriptName} not found or decompiled.\nfunction {scriptName}() {{\n\t// Script contents here\n}}";
                 UndertaleCode associatedCode = FindCodeForScript(script);
                 if (associatedCode != null && associatedCode.Decompiled != null) {
                     gmlCode = associatedCode.Decompiled.ToString(Data, true);
                      // Ensure GMS2+ function syntax (very basic check)
                      if (!gmlCode.TrimStart().StartsWith("function ")) {
                           // Try to wrap it - assumes the script name is the function name
                           gmlCode = $"function {scriptName}() {{\n{gmlCode}\n}}";
                            UMT.Log($"Note: Added basic 'function {scriptName}() {{...}}' wrapper to script '{scriptName}'. Review required.");
                      }
                     // Perform basic GML compatibility fixes here if desired
                 } else if (WARN_ON_MISSING_DECOMPILED_CODE) {
                      UMT.Log($"Warning: Decompiled code not found for Script '{scriptName}'.");
                 }

                // Write GML file
                File.WriteAllText(gmlPath, gmlCode);

                // Create Script .yy file content (JSON)
                var yyContent = new JObject(
                    new JProperty("isDnD", false), // GMS2 Drag and Drop scripts
                    new JProperty("isCompatibility", true), // Mark as compatibility script initially? GMS2 mostly ignores this now. Set based on original?
                    // new JProperty("isCompatibility", script.IsCompatibilityGMS2), // If UMT stores this flag
                    new JProperty("parent", new JObject(
                        new JProperty("name", "Scripts"), // Or subfolder if organized
                        new JProperty("path", "folders/Scripts.yy")
                    )),
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", scriptName),
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMScript")
                );

                WriteJsonFile(yyPath, yyContent);

                // Add to project resources
                 string relativePath = $"scripts/{scriptName}/{scriptName}.yy";
                 AddResourceToProject(scriptName, scriptGuid, relativePath, "GMScript", "scripts");
                  ResourcePaths[resourceKey] = relativePath;

            }
            catch (Exception ex)
            {
                 UMT.Log($"Error processing script '{scriptName}': {ex.Message}");
            }
        }
         UMT.Log("Script conversion finished.");
    }

      // Helper to find the decompiled code associated with a specific script resource
     private UndertaleCode FindCodeForScript(UndertaleScript script) {
         if (Data.Code == null) return null;
         // Find Code entry where the Name matches the Script's name, or linked via ParentEntry
          foreach (UndertaleCode codeEntry in Data.Code) {
               // Option 1: Direct reference (if UMT links Script -> Code)
               // if (script.AssociatedCode == codeEntry) return codeEntry; // Hypothetical

               // Option 2: Parent reference (if UMT links Code -> Script)
               // if (codeEntry.ParentEntry == script) return codeEntry; // Hypothetical

               // Option 3: Name matching (most likely if decompiled separately)
               if (codeEntry.Name?.Content == script.Name?.Content) {
                    return codeEntry;
               }
          }
          return null; // Not found
     }


    private void ConvertShaders()
    {
        UMT.Log("Converting Shaders...");
        if (Data.Shaders == null) return;

        string shaderDir = Path.Combine(OutputPath, "shaders");

        foreach (var shader in Data.Shaders)
        {
            if (shader == null || shader.Name == null) continue;
             // Skip default pass-through shaders often present in UT data?
             // if (shader.Name.Content.StartsWith("shd_passthru")) continue; // Optional skip

            string shaderName = GetResourceName(shader);
             string resourceKey = $"shaders/{shaderName}";
             Guid shaderGuid = GetResourceGuid(resourceKey);
            string shaderPath = Path.Combine(shaderDir, shaderName);
            string yyPath = Path.Combine(shaderPath, $"{shaderName}.yy");
            string vshPath = Path.Combine(shaderPath, $"{shaderName}.vsh");
            string fshPath = Path.Combine(shaderPath, $"{shaderName}.fsh");

             UMT.Log($"Converting Shader: {shaderName}");

            try
            {
                Directory.CreateDirectory(shaderPath);
                CreatedDirectories.Add(shaderPath);

                // Write Vertex Shader
                File.WriteAllText(vshPath, shader.VertexShader?.Content ?? "// Vertex shader source not found");
                // Write Fragment Shader
                File.WriteAllText(fshPath, shader.FragmentShader?.Content ?? "// Fragment shader source not found");

                 // Map Shader Type (GLSL ES, HLSL, etc.) -> GMS2 uses type string
                 // UT/GMS1 typically used GLSL ES
                 string gms2ShaderType = "GLSLES"; // Default for GMS1 projects
                 // if (shader.Type == UndertaleShader.ShaderType.HLSL11) gms2ShaderType = "HLSL11"; // If type info available

                 // Create Shader .yy file content (JSON)
                 var yyContent = new JObject(
                     new JProperty("type", 1), // GMS2 shader type enum? 1 = GLSL ES, 2=HLSL11 ? Check GMS2 .yy file. Let's assume 1 for GLSLES.
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Shaders"),
                         new JProperty("path", "folders/Shaders.yy")
                     )),
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", shaderName),
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMShader")
                 );

                WriteJsonFile(yyPath, yyContent);

                // Add to project resources
                 string relativePath = $"shaders/{shaderName}/{shaderName}.yy";
                 AddResourceToProject(shaderName, shaderGuid, relativePath, "GMShader", "shaders");
                 ResourcePaths[resourceKey] = relativePath;

            }
            catch (Exception ex)
            {
                 UMT.Log($"Error processing shader '{shaderName}': {ex.Message}");
            }
        }
         UMT.Log("Shader conversion finished.");
    }


    private void ConvertFonts()
    {
        UMT.Log("Converting Fonts...");
         UMT.Log("Warning: GMS1 Font conversion to GMS2 is often problematic due to different rendering and definition methods. Manual adjustment in GMS2 is highly recommended.");

        if (Data.Fonts == null) return;

        string fontDir = Path.Combine(OutputPath, "fonts");

        foreach (var font in Data.Fonts)
        {
            if (font == null || font.Name == null) continue;
            string fontName = GetResourceName(font);
             string resourceKey = $"fonts/{fontName}";
             Guid fontGuid = GetResourceGuid(resourceKey);
            string fontPath = Path.Combine(fontDir, fontName);
            string yyPath = Path.Combine(fontPath, $"{fontName}.yy");

             UMT.Log($"Converting Font: {fontName}");

            try
            {
                Directory.CreateDirectory(fontPath);
                CreatedDirectories.Add(fontPath);

                // --- Font Properties Mapping ---
                string sourceFontName = font.FontName?.Content ?? "Arial"; // Fallback font name
                int size = (int)font.Size; // GMS1 size is float, GMS2 uses int points
                bool bold = font.Bold;
                bool italic = font.Italic;
                uint rangeStart = font.RangeStart;
                uint rangeEnd = font.RangeEnd; // GMS2 uses character ranges

                 // GMS2 stores glyph data differently (often relies on system fonts or pre-rendered textures)
                 // GMS1 stored glyph metrics (shift, offset) and UVs referencing a texture page (font.Texture.TexturePage)

                 // Get the Sprite resource that was created for the font's texture page
                 JObject spriteRef = null;
                 UndertaleSprite associatedSprite = null;
                  if (font.Texture?.TexturePage != null) {
                      // Find the pseudo-sprite created from the texture page entry used by this font
                      // This requires searching the `allSprites` list created in ConvertSprites or similar mapping.
                      // We need to find the sprite whose *single frame* uses this specific TexturePageItem.
                      // This is complex. Let's try finding a sprite whose name might match or was derived from the font texture.
                      // A better way would be to store a mapping during Sprite conversion: TexturePageItem -> Generated Sprite GUID/Name

                      // Heuristic: Find a sprite whose texture *might* match the font's page dimensions and origin? Very unreliable.
                       // Simpler (but maybe fragile) Heuristic: Assume a sprite was created with a name derived from the font or texture page name.
                       // Need the original texture page name if possible.

                       // Fallback: Assume the texture page was converted to a sprite named something like "spr_font_<fontname>" or similar convention.
                       // This requires consistency in how background/font textures were converted to sprites.

                       // Let's try finding a sprite that uses the same texture page as the font
                       foreach(var kvp in ResourceToNameMap) {
                            if (kvp.Key is UndertaleSprite potentialSprite) {
                                if (potentialSprite.Textures.Any(t => t.TexturePage == font.Texture.TexturePage)) {
                                    associatedSprite = potentialSprite;
                                    spriteRef = CreateResourceReference(associatedSprite, "sprites");
                                     UMT.Log($"  - Linked font '{fontName}' to sprite '{GetResourceName(associatedSprite)}' based on texture page.");
                                    break;
                                }
                            }
                       }

                       if (spriteRef == null) {
                            UMT.Log($"Warning: Could not reliably find the sprite associated with the texture page for font '{fontName}'. Font rendering in GMS2 will likely fail or require manual setup.");
                       }
                  } else {
                        UMT.Log($"Warning: Font '{fontName}' does not have associated texture data. Cannot convert glyph info.");
                  }


                  // GMS2 Font properties
                  int antiAlias = font.AntiAlias; // GMS1 AA level (0-3?) mapping to GMS2 (0-?) might need adjustment
                  string charset = "0"; // Default charset (check GMS2 values: 0=ANSI, 1=Symbol, etc.) -> GMS1 didn't specify this way.
                   // Map GMS1 range to GMS2 charset/range if possible (e.g., if range covers basic ASCII)
                   if (rangeStart <= 32 && rangeEnd >= 127) charset = "0"; // Assume ANSI if common range covered

                  // Glyph Data: GMS2 stores this differently. We can't easily replicate GMS1's glyph metrics + texture UVs.
                  // Best bet: Store basic properties and let GMS2 regenerate the font from system fonts if possible,
                  // OR rely on the linked sprite (if found) and hope manual adjustments can make it work (less likely).

                // Create Font .yy file content (JSON)
                 var yyContent = new JObject(
                     new JProperty("sourceFontName", sourceFontName), // Underlying font (e.g., "Arial", "Times New Roman")
                     new JProperty("size", size), // Point size
                     new JProperty("bold", bold),
                     new JProperty("italic", italic),
                     new JProperty("antiAlias", antiAlias), // AA level (1 might be default on GMS2)
                     new JProperty("charset", int.Parse(charset)), // Charset identifier
                     new JProperty("first", rangeStart), // Start of character range
                     new JProperty("last", rangeEnd), // End of character range (GMS2 might use this or charset)
                     new JProperty("characterMap", new JObject()), // For custom character mapping (leave empty)
                     new JProperty("glyphOperations", new JArray()), // For modifying glyphs (leave empty)
                     // Include reference to the sprite if found, GMS2 *might* use this if configured correctly (unlikely default)
                     // GMS2 usually has a "textureGroupId" and info on how it generated the font texture itself.
                     // Replicating GMS1's approach requires custom handling or manual GMS2 setup.
                     new JProperty("fontName", fontName), // The resource name in GMS2
                     new JProperty("styleName", "Regular"), // "Regular", "Bold", "Italic", "Bold Italic"
                     new JProperty("kerningPairs", new JArray()), // Kerning info (leave empty)
                     new JProperty("includesTTF", false), // Was a TTF embedded? (UT doesn't usually)
                     new JProperty("TTFName", ""), // Embedded TTF filename
                     new JProperty("textureGroupId", CreateResourceReference("Default", Guid.Empty, "texturegroups")), // Default texture group (GMS2 usually creates its own)
                     new JProperty("ascender", 0), // Font metrics (let GMS2 calculate)
                     new JProperty("descender", 0),
                     new JProperty("lineHeight", 0),
                     new JProperty("glyphs", new JObject()), // GMS2 stores detailed glyph info here if pre-rendered. Difficult to generate from GMS1.
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Fonts"),
                         new JProperty("path", "folders/Fonts.yy")
                     )),
                      new JProperty("resourceVersion", "1.0"),
                      new JProperty("name", fontName),
                      new JProperty("tags", new JArray()),
                      new JProperty("resourceType", "GMFont")
                 );

                WriteJsonFile(yyPath, yyContent);

                // Add to project resources
                 string relativePath = $"fonts/{fontName}/{fontName}.yy";
                 AddResourceToProject(fontName, fontGuid, relativePath, "GMFont", "fonts");
                  ResourcePaths[resourceKey] = relativePath;

            }
            catch (Exception ex)
            {
                 UMT.Log($"Error processing font '{fontName}': {ex.Message}");
            }
        }
         UMT.Log("Font conversion finished.");
    }


    private void ConvertTilesets()
    {
        UMT.Log("Converting Tilesets...");
         UMT.Log("Note: Creating GMS2 Tilesets based on GMS1 Backgrounds used for tiling. Manual review and adjustment of tile properties (size, separation, offset) in GMS2 is highly recommended.");

        if (Data.Backgrounds == null && Data.Sprites == null) return; // Need potential source images

        string tilesetDir = Path.Combine(OutputPath, "tilesets");

        // Identify which Backgrounds or Sprites are used as tilesets in Rooms
        HashSet<UndertaleResource> usedAsTileset = new HashSet<UndertaleResource>();
        if (Data.Rooms != null) {
            foreach(var room in Data.Rooms) {
                if (room.Tiles != null) {
                    foreach(var tile in room.Tiles) {
                        if (tile.BackgroundDefinition != null) {
                             usedAsTileset.Add(tile.BackgroundDefinition);
                        }
                         // Also consider if Sprites were used directly for tiles (less common in GMS1)
                         // if (tile.SpriteDefinition != null) usedAsTileset.Add(tile.SpriteDefinition);
                    }
                }
            }
        }

         UMT.Log($"Identified {usedAsTileset.Count} Backgrounds/Sprites used for tiles.");

        foreach (var resource in usedAsTileset)
        {
             if (resource == null) continue;

             // Determine the Sprite resource associated with this Background/Sprite
             string spriteName = GetResourceName(resource); // Name should match the pseudo-sprite created earlier
             Guid spriteGuid = GetResourceGuid($"sprites/{spriteName}"); // Get the GUID assigned during sprite conversion
             JObject spriteRef = CreateResourceReference(spriteName, spriteGuid, "sprites");

             if (spriteRef == null) {
                  UMT.Log($"Warning: Could not find the source Sprite resource '{spriteName}' for creating Tileset. Skipping tileset generation for this resource.");
                 continue;
             }

             string tilesetName = spriteName; // Use the same name for the tileset resource
             string resourceKey = $"tilesets/{tilesetName}";
             Guid tilesetGuid = GetResourceGuid(resourceKey); // Get or assign GUID for the tileset itself
             string tilesetPath = Path.Combine(tilesetDir, tilesetName);
             string yyPath = Path.Combine(tilesetPath, $"{tilesetName}.yy");

              UMT.Log($"Converting Tileset: {tilesetName} (from resource: {GetResourceName(resource)})");

             try {
                 Directory.CreateDirectory(tilesetPath);
                 CreatedDirectories.Add(tilesetPath);

                 // --- Determine Tileset Properties ---
                 // These are best guesses and likely need manual correction in GMS2.
                 // We need the dimensions of the *sprite* derived from the background/sprite resource.
                 UndertaleSprite sourceSprite = Data.Sprites.FirstOrDefault(s => GetResourceName(s) == spriteName)
                                             ?? Data.Backgrounds.FirstOrDefault(b => GetResourceName(b) == spriteName); // Need to find the sprite/bg object itself

                  int tileWidth = 16; // Common UT tile size - GUESS!
                  int tileHeight = 16; // Common UT tile size - GUESS!
                  int tileSepX = 0; // Assume no separation - GUESS!
                  int tileSepY = 0; // Assume no separation - GUESS!
                  int tileOffsetX = 0; // Assume no offset - GUESS!
                  int tileOffsetY = 0; // Assume no offset - GUESS!
                  int spriteWidth = 0;
                  int spriteHeight = 0;

                 // Try to get actual dimensions from the source sprite/background resource if possible
                 // This assumes the sprite conversion accurately stored width/height
                  var foundSprite = FindResourceByName<UndertaleSprite>(spriteName, Data.Sprites) ??
                                   FindResourceByName<UndertaleBackground>(spriteName, Data.Backgrounds); // Find the original resource

                  if (foundSprite is UndertaleSprite sp) {
                      spriteWidth = sp.Width;
                      spriteHeight = sp.Height;
                       // Can we infer tile size? If sprite dims are multiples of 16, 32? Highly speculative.
                       // Example GUESS: If a room using this has grid settings, use those?
                       // Example GUESS: Check common divisors?
                       // Stick to default guess for now.
                  } else if (foundSprite is UndertaleBackground bg) {
                       if (bg.Texture?.TexturePage != null) {
                            spriteWidth = bg.Texture.TexturePage.TargetWidth; // Use target dims as proxy? Or source?
                            spriteHeight = bg.Texture.TexturePage.TargetHeight;
                       }
                  }

                  if (spriteWidth == 0 || spriteHeight == 0) {
                       UMT.Log($"Warning: Could not determine dimensions for source sprite '{spriteName}' for tileset '{tilesetName}'. Using default tile size guesses (16x16).");
                  }


                  // Calculate tile count based on guesses and sprite dimensions (if available)
                  int tileColumns = (spriteWidth > 0 && tileWidth > 0) ? (spriteWidth + tileSepX - tileOffsetX * 2) / (tileWidth + tileSepX) : 1;
                  int tileRows = (spriteHeight > 0 && tileHeight > 0) ? (spriteHeight + tileSepY - tileOffsetY * 2) / (tileHeight + tileSepY) : 1;
                  int tileCount = Math.Max(1, tileColumns * tileRows); // Ensure at least 1 tile


                 // Create Tileset .yy file content (JSON)
                 var yyContent = new JObject(
                     new JProperty("spriteId", spriteRef), // Link to the source sprite
                     new JProperty("tileWidth", tileWidth),
                     new JProperty("tileHeight", tileHeight),
                     new JProperty("tilexoff", tileOffsetX),
                     new JProperty("tileyoff", tileOffsetY),
                     new JProperty("tilehsep", tileSepX),
                     new JProperty("tilevsep", tileSepY),
                     new JProperty("spriteNoExport", false), // Should the source sprite be exported? Usually yes.
                     new JProperty("textureGroupId", CreateResourceReference("Default", Guid.Empty, "texturegroups")), // Tilesets use texture groups too
                     new JProperty("out_tilehborder", 2), // Default border for texture packing
                     new JProperty("out_tilevborder", 2), // Default border
                     new JProperty("out_columns", tileColumns), // Calculated columns
                     new JProperty("tile_count", tileCount), // Calculated tile count
                     new JProperty("autoTileSets", new JArray()), // Auto-tiling rules (leave empty)
                     new JProperty("tileAnimationFrames", new JArray()), // Tile animation frames (leave empty)
                     new JProperty("tileAnimationSpeed", 15.0f), // Default animation speed
                     new JProperty("macroPageTiles", new JObject( // Tile palette settings
                         new JProperty("SerialiseWidth", Math.Max(1, tileColumns)), // Width of palette (columns)
                         new JProperty("SerialiseHeight", Math.Max(1, tileRows)), // Height of palette (rows)
                         new JProperty("TileSerialiseData", new JArray()) // Data for palette (leave empty)
                     )),
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Tilesets"),
                         new JProperty("path", "folders/Tilesets.yy")
                     )),
                      new JProperty("resourceVersion", "1.0"),
                      new JProperty("name", tilesetName),
                      new JProperty("tags", new JArray()),
                      new JProperty("resourceType", "GMTileSet")
                 );

                 WriteJsonFile(yyPath, yyContent);

                 // Add to project resources
                  string relativePath = $"tilesets/{tilesetName}/{tilesetName}.yy";
                  AddResourceToProject(tilesetName, tilesetGuid, relativePath, "GMTileSet", "tilesets");
                   ResourcePaths[resourceKey] = relativePath;

             } catch (Exception ex) {
                 UMT.Log($"Error processing tileset '{tilesetName}': {ex.Message}");
             }
        }


         UMT.Log("Tileset conversion finished.");
    }

     // Helper to find a resource by its sanitized name from a list
     private T FindResourceByName<T>(string name, IList<T> list) where T : UndertaleNamedResource {
          if (list == null) return null;
          foreach (T item in list) {
               if (GetResourceName(item) == name) {
                    return item;
               }
          }
          return null;
     }


    // Basic placeholder conversions for less critical or harder-to-map resources

    private void ConvertPaths() {
         UMT.Log("Converting Paths...");
         if (Data.Paths == null) return;
         string pathDir = Path.Combine(OutputPath, "paths");

         foreach (var path in Data.Paths) {
              if (path == null || path.Name == null) continue;
              string pathName = GetResourceName(path);
               string resourceKey = $"paths/{pathName}";
               Guid pathGuid = GetResourceGuid(resourceKey);
              string pathResPath = Path.Combine(pathDir, pathName);
              string yyPath = Path.Combine(pathResPath, $"{pathName}.yy");

               UMT.Log($"Converting Path: {pathName}");
               try {
                    Directory.CreateDirectory(pathResPath);
                    CreatedDirectories.Add(pathResPath);

                    List<JObject> points = new List<JObject>();
                    if (path.Points != null) {
                         foreach(var pt in path.Points) {
                              points.Add(new JObject(
                                  new JProperty("speed", (float)pt.Speed), // Speed at this point
                                  new JProperty("x", (float)pt.X),
                                  new JProperty("y", (float)pt.Y)
                              ));
                         }
                    }

                    var yyContent = new JObject(
                        new JProperty("kind", (int)path.Kind), // 0 = Straight, 1 = Smooth
                        new JProperty("closed", path.Closed),
                        new JProperty("precision", (int)path.Precision), // Number of steps between points for smooth paths
                        new JProperty("points", new JArray(points)),
                        new JProperty("parent", new JObject(
                            new JProperty("name", "Paths"),
                            new JProperty("path", "folders/Paths.yy")
                        )),
                        new JProperty("resourceVersion", "1.0"),
                        new JProperty("name", pathName),
                        new JProperty("tags", new JArray()),
                        new JProperty("resourceType", "GMPath")
                    );
                    WriteJsonFile(yyPath, yyContent);
                    string relativePath = $"paths/{pathName}/{pathName}.yy";
                    AddResourceToProject(pathName, pathGuid, relativePath, "GMPath", "paths");
                     ResourcePaths[resourceKey] = relativePath;
               } catch (Exception ex) {
                    UMT.Log($"Error processing path '{pathName}': {ex.Message}");
               }
         }
          UMT.Log("Path conversion finished.");
    }

    private void ConvertTimelines() {
         UMT.Log("Converting Timelines...");
         if (Data.Timelines == null) return;
         string timelineDir = Path.Combine(OutputPath, "timelines");

         foreach (var timeline in Data.Timelines) {
             if (timeline == null || timeline.Name == null) continue;
             string tlName = GetResourceName(timeline);
             string resourceKey = $"timelines/{tlName}";
              Guid tlGuid = GetResourceGuid(resourceKey);
             string tlPath = Path.Combine(timelineDir, tlName);
             string yyPath = Path.Combine(tlPath, $"{tlName}.yy");

              UMT.Log($"Converting Timeline: {tlName}");
              try {
                   Directory.CreateDirectory(tlPath);
                   CreatedDirectories.Add(tlPath);

                   List<JObject> moments = new List<JObject>();
                    if (timeline.Moments != null) {
                         foreach(var moment in timeline.Moments) {
                              // Find the code associated with this moment's actions
                              string gmlCode = $"// Code for timeline {tlName} moment {moment.Moment} not found/decompiled.";
                              UndertaleCode momentCode = FindCodeForTimelineMoment(timeline, moment);
                               if (momentCode != null && momentCode.Decompiled != null) {
                                   gmlCode = momentCode.Decompiled.ToString(Data, true);
                               } else if (WARN_ON_MISSING_DECOMPILED_CODE) {
                                    // Check if the action list itself contains a code reference ID
                                    // This requires inspecting moment.Actions structure
                                    UMT.Log($"Warning: Decompiled code not found for Timeline '{tlName}' Moment: {moment.Moment}");
                               }

                              // Write GML to a separate file for this moment
                               string momentGmlFileName = $"moment_{moment.Moment}.gml";
                               string momentGmlPath = Path.Combine(tlPath, momentGmlFileName);
                               File.WriteAllText(momentGmlPath, gmlCode);

                               moments.Add(new JObject(
                                   new JProperty("moment", moment.Moment), // Time step
                                   new JProperty("evnt", new JObject( // Event data (the code to run)
                                        // How does GMS2 link timeline moments to code? Action list? Script reference?
                                        // Assume it might reference the GML file directly or use an internal format.
                                        // Let's store the GML filename, though GMS2 might parse this differently.
                                        new JProperty("eventNum", 0), // Placeholder
                                        new JProperty("eventType", 0), // Placeholder
                                         // GMS2 seems to store DnD or script reference here.
                                         // For simplicity, we'll just store the moment time. Manual code insertion might be needed.
                                         new JProperty("collisionObjectId", null),
                                         new JProperty("isDnD", false),
                                         new JProperty("resourceVersion", "1.0"),
                                         new JProperty("name", ""),
                                         new JProperty("tags", new JArray()),
                                         new JProperty("resourceType", "GMEvent") // Generic event type
                                   )),
                                    new JProperty("resourceVersion", "1.0"),
                                    new JProperty("name", ""), // Moment name, usually empty
                                    new JProperty("tags", new JArray()),
                                    new JProperty("resourceType", "GMMoment")
                               ));
                         }
                    }

                   var yyContent = new JObject(
                        new JProperty("momentList", new JArray(moments)),
                        new JProperty("parent", new JObject(
                            new JProperty("name", "Timelines"),
                            new JProperty("path", "folders/Timelines.yy")
                        )),
                        new JProperty("resourceVersion", "1.0"),
                        new JProperty("name", tlName),
                        new JProperty("tags", new JArray()),
                        new JProperty("resourceType", "GMTimeline")
                    );

                   WriteJsonFile(yyPath, yyContent);
                    string relativePath = $"timelines/{tlName}/{tlName}.yy";
                    AddResourceToProject(tlName, tlGuid, relativePath, "GMTimeline", "timelines");
                     ResourcePaths[resourceKey] = relativePath;
              } catch (Exception ex) {
                   UMT.Log($"Error processing timeline '{tlName}': {ex.Message}");
              }
         }
          UMT.Log("Timeline conversion finished.");
    }

      // Helper to find the decompiled code associated with a specific timeline moment
      // This assumes UMT links Code entries to Timeline Moments somehow (e.g., via ParentEntry or naming convention)
     private UndertaleCode FindCodeForTimelineMoment(UndertaleTimeline timeline, UndertaleTimelineMoment moment) {
          if (Data.Code == null) return null;

          // Try finding code linked directly to the moment or via the timeline + moment index
           foreach (UndertaleCode codeEntry in Data.Code) {
               // Hypothetical linking checks (adjust based on UMT structure)
               // if (codeEntry.ParentEntry == moment) return codeEntry;
               // if (codeEntry.ParentEntry == timeline && codeEntry.AssociatedMomentIndex == moment.Moment) return codeEntry;
                // Check naming convention?
                 string expectedName = $"{timeline.Name.Content}_moment_{moment.Moment}";
                 // if (codeEntry.Name.Content == expectedName) return codeEntry;

                // Check if the moment's actions list contains a direct reference to a Code object or ID
                 if (moment.Actions != null) {
                      var codeAction = moment.Actions.FirstOrDefault(a => a.LibID == 1 && a.Kind == 7 && a.Function.Name.Content == "action_execute_script");
                       if (codeAction != null && codeAction.Arguments.Count > 0) {
                            // Argument likely holds Code ID or index. Need to parse and lookup.
                            // Placeholder: Return null, requires UMT specific logic.
                            return null; // Placeholder
                       }
                 }
           }
          return null; // Not found
     }


    private void ConvertIncludedFiles() {
         UMT.Log("Converting Included Files...");
         if (Data.IncludedFiles == null) return;

         string datafilesDir = Path.Combine(OutputPath, "datafiles"); // Target for actual files
         string includedFilesDir = Path.Combine(OutputPath, "includedfiles"); // Target for .yy definitions

         foreach (var file in Data.IncludedFiles) {
              if (file == null || file.Name == null) continue;
              // Use the sanitized filename as the resource name/key
               string fileName = GetResourceName(file); // Get unique name from map
               string originalFilePath = file.Name.Content; // Original path/name stored in data.win
               string targetFileName = Path.GetFileName(originalFilePath); // Get just the filename part
                targetFileName = SanitizeFileName(targetFileName); // Sanitize it again just in case

               // Handle potential duplicate filenames in datafiles after sanitization
               string targetFilePath = Path.Combine(datafilesDir, targetFileName);
               int counter = 1;
               while(File.Exists(targetFilePath)) {
                   string nameWithoutExt = Path.GetFileNameWithoutExtension(targetFileName);
                   string ext = Path.GetExtension(targetFileName);
                   targetFileName = $"{nameWithoutExt}_{counter}{ext}";
                   targetFilePath = Path.Combine(datafilesDir, targetFileName);
                   counter++;
               }


               string resourceKey = $"includedfiles/{fileName}"; // Use the mapped unique name for the resource key
               Guid fileGuid = GetResourceGuid(resourceKey);
               // string fileResPath = Path.Combine(includedFilesDir, fileName); // .yy files go here
               string yyPath = Path.Combine(includedFilesDir, $"{fileName}.yy");

               UMT.Log($"Converting Included File: {fileName} (Original: {originalFilePath}) -> {targetFileName}");

               try {
                    // IncludedFiles directory for .yy is already created
                    // Directory.CreateDirectory(fileResPath); // Not needed if .yy goes directly in includedfiles/
                    // CreatedDirectories.Add(fileResPath);

                   // Write the actual file data
                   File.WriteAllBytes(targetFilePath, file.Data);

                   // Create .yy file content (JSON)
                   var yyContent = new JObject(
                       new JProperty("ConfigValues", new JObject()), // Config specific values (usually empty)
                       new JProperty("fileName", targetFileName), // The actual filename in datafiles/
                       new JProperty("filePath", "datafiles"), // The folder within the GMS2 project
                       new JProperty("outputFolder", ""), // Optional output subfolder on build
                       new JProperty("removeEnd", false), // GMS2 options
                       new JProperty("store", false), // GMS2 options
                       new JProperty("ConfigOptions", new JObject()), // More config options
                       new JProperty("debug", false),
                       new JProperty("exportAction", 0), // 0 = Export, 1 = Copy, 2 = Create Empty, 3 = Ignore
                       new JProperty("exportDir", ""),
                       new JProperty("overwrite", false),
                       new JProperty("freeData", false),
                       new JProperty("origName", ""), // Original name before potential rename
                       new JProperty("parent", new JObject(
                           new JProperty("name", "Included Files"), // GMS2 IDE Folder
                           new JProperty("path", "folders/Included Files.yy") // GMS2 IDE Folder Path
                       )),
                        new JProperty("resourceVersion", "1.0"),
                        new JProperty("name", fileName), // The name of the resource itself (mapped unique name)
                        new JProperty("tags", new JArray()),
                        new JProperty("resourceType", "GMIncludedFile")
                   );

                   WriteJsonFile(yyPath, yyContent);

                   // Add to project resources
                    string relativePath = $"includedfiles/{fileName}.yy"; // Path to the .yy file
                    AddResourceToProject(fileName, fileGuid, relativePath, "GMIncludedFile", "includedfiles");
                     ResourcePaths[resourceKey] = relativePath; // Store path using the unique key

               } catch (Exception ex) {
                    UMT.Log($"Error processing included file '{fileName}': {ex.Message}");
               }
         }
          UMT.Log("Included File conversion finished.");
    }


     private void ConvertAudioGroups() {
         UMT.Log("Converting Audio Groups...");
         if (Data.AudioGroups == null) return;

         string agDir = Path.Combine(OutputPath, "audiogroups");

          // Ensure the default GMS2 audio group exists in our tracking, even if not in UT data
          string defaultAgName = "audiogroup_default";
          Guid defaultAgGuid = GetResourceGuid($"audiogroups/{defaultAgName}");
          string defaultAgYyPath = Path.Combine(agDir, $"{defaultAgName}.yy");
          if (!File.Exists(defaultAgYyPath)) {
              var defaultAgContent = new JObject(
                  new JProperty("targets", -1), // Default target mask
                  new JProperty("parent", new JObject(
                      new JProperty("name", "Audio Groups"),
                      new JProperty("path", "folders/Audio Groups.yy")
                  )),
                  new JProperty("resourceVersion", "1.0"),
                  new JProperty("name", defaultAgName),
                  new JProperty("tags", new JArray()),
                  new JProperty("resourceType", "GMAudioGroup")
              );
              WriteJsonFile(defaultAgYyPath, defaultAgContent);
               string defaultRelativePath = $"audiogroups/{defaultAgName}.yy";
               AddResourceToProject(defaultAgName, defaultAgGuid, defaultRelativePath, "GMAudioGroup", "audiogroups");
                ResourcePaths[$"audiogroups/{defaultAgName}"] = defaultRelativePath;
               UMT.Log($"Created default audio group: {defaultAgName}");
          }


         foreach (var ag in Data.AudioGroups) {
              if (ag == null || ag.Name == null) continue;
              string agName = GetResourceName(ag);
               // Skip if it's the default group we already handled (or if UT data has a group named 'default')
               if (agName.Equals(defaultAgName, StringComparison.OrdinalIgnoreCase)) continue;

              string resourceKey = $"audiogroups/{agName}";
              Guid agGuid = GetResourceGuid(resourceKey); // Get/Assign GUID
              string agYyPath = Path.Combine(agDir, $"{agName}.yy");

               UMT.Log($"Converting Audio Group: {agName}");

               try {
                   // Audio group .yy file is simple, just defines the group existence
                   var yyContent = new JObject(
                       new JProperty("targets", -1), // Target mask (platforms) - default to all (-1)
                       new JProperty("parent", new JObject(
                           new JProperty("name", "Audio Groups"), // GMS2 IDE Folder
                           new JProperty("path", "folders/Audio Groups.yy") // GMS2 IDE Folder Path
                       )),
                        new JProperty("resourceVersion", "1.0"),
                        new JProperty("name", agName),
                        new JProperty("tags", new JArray()),
                        new JProperty("resourceType", "GMAudioGroup")
                   );
                   WriteJsonFile(agYyPath, yyContent);

                   // Add to project resources
                    string relativePath = $"audiogroups/{agName}.yy";
                    AddResourceToProject(agName, agGuid, relativePath, "GMAudioGroup", "audiogroups");
                     ResourcePaths[resourceKey] = relativePath;

               } catch (Exception ex) {
                    UMT.Log($"Error processing audio group '{agName}': {ex.Message}");
               }
         }
          UMT.Log("Audio Group conversion finished.");
    }


     private void ConvertTextureGroups() {
         UMT.Log("Converting Texture Groups...");
         // GMS1 didn't have named texture groups exactly like GMS2. It had TextureGroupInfo.
         // We map based on the texture pages associated with sprites/backgrounds/fonts.
         // UMT might group pages under UndertaleTextureGroup objects.

         string tgDir = Path.Combine(OutputPath, "texturegroups");

          // Ensure the default GMS2 texture group exists
          string defaultTgName = "Default";
          Guid defaultTgGuid = GetResourceGuid($"texturegroups/{defaultTgName}");
          string defaultTgYyPath = Path.Combine(tgDir, $"{defaultTgName}.yy");
           if (!File.Exists(defaultTgYyPath)) {
               var defaultTgContent = new JObject(
                   new JProperty("isScaled", true),
                   new JProperty("autocrop", true),
                   new JProperty("border", 2),
                   new JProperty("mipsToGenerate", 0),
                   new JProperty("groupParent", null), // Parent group (for hierarchy)
                   new JProperty("targets", -1), // Target mask
                   new JProperty("loadImmediately", false), // Load group immediately?
                   new JProperty("parent", new JObject(
                       new JProperty("name", "Texture Groups"),
                       new JProperty("path", "folders/Texture Groups.yy")
                   )),
                   new JProperty("resourceVersion", "1.0"),
                   new JProperty("name", defaultTgName),
                   new JProperty("tags", new JArray()),
                   new JProperty("resourceType", "GMTextureGroup")
               );
               WriteJsonFile(defaultTgYyPath, defaultTgContent);
                string defaultRelativePath = $"texturegroups/{defaultTgName}.yy";
                AddResourceToProject(defaultTgName, defaultTgGuid, defaultRelativePath, "GMTextureGroup", "texturegroups");
                 ResourcePaths[$"texturegroups/{defaultTgName}"] = defaultRelativePath;
                UMT.Log($"Created default texture group: {defaultTgName}");
           }


          // Iterate through the UndertaleTextureGroup resources if they exist
          if (Data.TextureGroups != null) {
                foreach (var tg in Data.TextureGroups) {
                    if (tg == null || tg.Name == null) continue;
                    string tgName = GetResourceName(tg);
                     if (tgName.Equals(defaultTgName, StringComparison.OrdinalIgnoreCase)) continue; // Skip default

                    string resourceKey = $"texturegroups/{tgName}";
                    Guid tgGuid = GetResourceGuid(resourceKey);
                    string tgYyPath = Path.Combine(tgDir, $"{tgName}.yy");

                     UMT.Log($"Converting Texture Group: {tgName}");

                    try {
                        // Create the .yy file for the texture group definition
                        var yyContent = new JObject(
                            new JProperty("isScaled", true), // Default GMS2 settings
                            new JProperty("autocrop", true),
                            new JProperty("border", 2),
                            new JProperty("mipsToGenerate", 0),
                            new JProperty("groupParent", null), // Parent texture group
                            new JProperty("targets", -1), // Target mask
                            new JProperty("loadImmediately", false), // Load immediately? Usually false for non-default groups
                            new JProperty("parent", new JObject(
                                new JProperty("name", "Texture Groups"),
                                new JProperty("path", "folders/Texture Groups.yy")
                            )),
                             new JProperty("resourceVersion", "1.0"),
                             new JProperty("name", tgName),
                             new JProperty("tags", new JArray()),
                             new JProperty("resourceType", "GMTextureGroup")
                        );
                        WriteJsonFile(tgYyPath, yyContent);

                        // Add to project resources
                         string relativePath = $"texturegroups/{tgName}.yy";
                         AddResourceToProject(tgName, tgGuid, relativePath, "GMTextureGroup", "texturegroups");
                          ResourcePaths[resourceKey] = relativePath;

                    } catch (Exception ex) {
                         UMT.Log($"Error processing texture group '{tgName}': {ex.Message}");
                    }
                }
          } else {
               UMT.Log("No specific UndertaleTextureGroup data found. Only default texture group created.");
          }

          UMT.Log("Texture Group conversion finished.");
     }


    private void ConvertExtensions() {
         UMT.Log("Converting Extensions (Basic Structure)...");
         if (Data.Extensions == null) return;
         string extDir = Path.Combine(OutputPath, "extensions");

         foreach (var ext in Data.Extensions) {
             if (ext == null || ext.Name == null) continue;
              // Extensions are complex (contain files, functions, proxies)
              // We will just create the basic .yy structure. Files need manual adding.
              string extName = GetResourceName(ext);
              string resourceKey = $"extensions/{extName}";
              Guid extGuid = GetResourceGuid(resourceKey);
              string extPath = Path.Combine(extDir, extName);
              string yyPath = Path.Combine(extPath, $"{extName}.yy");

               UMT.Log($"Converting Extension: {extName}");
               try {
                    Directory.CreateDirectory(extPath);
                    CreatedDirectories.Add(extPath);

                    // Create basic Extension .yy file content
                   var yyContent = new JObject(
                       new JProperty("options", new JArray()), // Extension options
                       new JProperty("exportToGame", true),
                       new JProperty("supportedTargets", -1), // All targets
                       new JProperty("extensionVersion", ext.Version?.Content ?? "1.0.0"),
                       new JProperty("packageId", ""), // Marketplace Package ID
                       new JProperty("productId", ""), // Marketplace Product ID
                       new JProperty("author", ""), // Author info (not in UT data)
                       new JProperty("date", DateTime.UtcNow.ToString("yyyy-MM-dd")),
                       new JProperty("license", ""),
                       new JProperty("description", ext.FolderName?.Content ?? ""), // Use folder name as description?
                       new JProperty("helpfile", ""),
                       new JProperty("iosProps", false), // Platform properties
                       new JProperty("tvosProps", false),
                       new JProperty("androidProps", false),
                       new JProperty("installdir", ""), // GMS1 extension install dir?
                       new JProperty("classname", ext.ClassName?.Content ?? ""), // Java class name for Android?
                       new JProperty("tvosclassname", ""),
                       new JProperty("iosclassname", ""),
                       new JProperty("androidclassname", ""),
                       new JProperty("sourcedir", ""),
                       new JProperty("androidsourcedir", ""),
                       new JProperty("macsourcedir", ""),
                       new JProperty("maclinkerflags", ""),
                       new JProperty("tvosmacosourcedir", ""),
                       new JProperty("tvosmaclinkerflags", ""),
                       new JProperty("xboxonesourcedir", ""),
                       new JProperty("xboxonelinkerflags", ""),
                       new JProperty("ps4sourcedir", ""),
                       new JProperty("ps4linkerflags", ""),
                       new JProperty("linuxsourcedir", ""),
                       new JProperty("linuxlinkerflags", ""),
                       new JProperty("iosSystemFrameworks", new JArray()),
                       new JProperty("iosThirdPartyFrameworks", new JArray()),
                       new JProperty("tvosSystemFrameworks", new JArray()),
                       new JProperty("tvosThirdPartyFrameworks", new JArray()),
                       new JProperty("IncludedResources", new JArray()), // List of files, functions, proxies (needs manual population)
                       new JProperty("androidPermissions", new JArray()),
                       new JProperty("copyToTargets", -1), // Targets to copy files to
                       new JProperty("iosCocoaPods", ""),
                       new JProperty("tvosCocoaPods", ""),
                       new JProperty("iosCocoaPodDependencies", ""),
                       new JProperty("tvosCocoaPodDependencies", ""),
                       new JProperty("parent", new JObject(
                           new JProperty("name", "Extensions"),
                           new JProperty("path", "folders/Extensions.yy")
                       )),
                        new JProperty("resourceVersion", "1.2"), // GMExtension version
                        new JProperty("name", extName),
                        new JProperty("tags", new JArray()),
                        new JProperty("resourceType", "GMExtension")
                   );

                   WriteJsonFile(yyPath, yyContent);
                    string relativePath = $"extensions/{extName}/{extName}.yy";
                    AddResourceToProject(extName, extGuid, relativePath, "GMExtension", "extensions");
                     ResourcePaths[resourceKey] = relativePath;

                      UMT.Log($"Warning: Extension '{extName}' created with basic structure. Files and function definitions must be added manually in GMS2.");

               } catch (Exception ex) {
                    UMT.Log($"Error processing extension '{extName}': {ex.Message}");
               }
         }
          UMT.Log("Extension conversion finished.");
    }

     private void ConvertNotes() {
          UMT.Log("Converting Notes (Basic Structure)...");
          // GMS1 didn't have Notes. This just creates the folder structure.
          string notesDir = Path.Combine(OutputPath, "notes");
          // You could potentially convert comments from scripts or objects into notes,
          // but that's beyond the scope of this basic conversion.
           UMT.Log("Note conversion skipped (GMS1 had no equivalent resource).");
     }


    // === Project File Creation ===

    private void AddResourceToProject(string name, Guid guid, string path, string type, string folderName)
    {
        // Add to the main resource list for the .yyp
        ResourceList.Add(new JObject(
            new JProperty("id", new JObject( // GMS2 uses nested object for id
                new JProperty("name", guid.ToString("D")), // GUID as name
                new JProperty("path", path) // Relative path to the .yy file
            )),
            new JProperty("order", 0) // Order value (usually 0, GMS2 manages internally?)
        ));

        // Add to the folder structure for the .yyp View section
        if (!FolderStructure.ContainsKey(folderName)) {
             FolderStructure[folderName] = new List<string>();
        }
        FolderStructure[folderName].Add(guid.ToString("D")); // Add GUID to the folder's list
    }

    private void CreateProjectFile()
    {
        string yypPath = Path.Combine(OutputPath, $"{ProjectName}.yyp");

        // Build the folder view structure from FolderStructure dictionary
        List<JObject> folderViews = new List<JObject>();
        // Define standard GMS2 top-level folders and their types
         // Order roughly matches GMS2 IDE view
         string[] folderOrder = {
             "sprite", "tileset", "sound", "path", "script", "shader", "font",
             "timeline", "object", "room", "sequence", "animationcurve", // Add Sequence/AnimCurve if converting them
             "note", "extension", "audiogroup", "texturegroup", "includedfile"
         };
         Dictionary<string, string> folderTypes = new Dictionary<string, string> {
              {"sprites", "GMSprite"}, {"tilesets", "GMTileSet"}, {"sounds", "GMSound"},
              {"paths", "GMPath"}, {"scripts", "GMScript"}, {"shaders", "GMShader"},
              {"fonts", "GMFont"}, {"timelines", "GMTimeline"}, {"objects", "GMObject"},
              {"rooms", "GMRoom"}, {"notes", "GMNote"}, {"extensions", "GMExtension"},
              {"audiogroups", "GMAudioGroup"}, {"texturegroups", "GMTextureGroup"},
              {"includedfiles", "GMIncludedFile"},
               // Add mappings for Sequences, AnimationCurves if needed
               {"sequences", "GMSequence"}, {"animationcurves", "GMAnimationCurve"}
         };
         Dictionary<string, string> folderDisplayNames = new Dictionary<string, string> {
              {"sprites", "Sprites"}, {"tilesets", "Tile Sets"}, {"sounds", "Sounds"},
              {"paths", "Paths"}, {"scripts", "Scripts"}, {"shaders", "Shaders"},
              {"fonts", "Fonts"}, {"timelines", "Timelines"}, {"objects", "Objects"},
              {"rooms", "Rooms"}, {"notes", "Notes"}, {"extensions", "Extensions"},
              {"audiogroups", "Audio Groups"}, {"texturegroups", "Texture Groups"},
              {"includedfiles", "Included Files"},
               // Add display names for Sequences, AnimationCurves if needed
                {"sequences", "Sequences"}, {"animationcurves", "Animation Curves"}
         };


          // Ensure all expected folder keys exist in FolderStructure even if empty
          foreach(var kvp in folderTypes) {
              if (!FolderStructure.ContainsKey(kvp.Key)) {
                  FolderStructure[kvp.Key] = new List<string>();
              }
          }


         // Create folders in a standard order
         foreach (string folderKeyBase in folderOrder) {
              string folderKey = folderKeyBase + "s"; // e.g., "sprite" -> "sprites" (adjust if inconsistent)
               if (folderKey == "includedfiles") folderKey = "includedfiles"; // Handle exceptions
               if (folderKey == "audiogroups") folderKey = "audiogroups";
               if (folderKey == "texturegroups") folderKey = "texturegroups";
               // Handle other pluralization exceptions if needed

              if (FolderStructure.ContainsKey(folderKey) && folderTypes.ContainsKey(folderKey)) {
                   string folderName = folderDisplayNames.ContainsKey(folderKey) ? folderDisplayNames[folderKey] : folderKey; // Use display name
                   Guid folderGuid = GetResourceGuid($"folders/{folderName}"); // Assign GUID to the folder itself
                   string folderPath = $"folders/{folderName}.yy"; // Path for the folder definition file

                   folderViews.Add(new JObject(
                       new JProperty("folderPath", folderPath),
                       new JProperty("order", folderViews.Count), // Assign order based on position
                       new JProperty("resourceVersion", "1.0"), // GMFolder version
                       new JProperty("name", folderName), // Folder display name
                       new JProperty("tags", new JArray()),
                       new JProperty("resourceType", "GMFolder")
                   ));

                    // Create the actual folder .yy file (simple definition)
                    string fullFolderPath = Path.Combine(OutputPath, "folders");
                     Directory.CreateDirectory(fullFolderPath);
                     string folderYyPath = Path.Combine(fullFolderPath, $"{folderName}.yy");
                     var folderYyContent = new JObject(
                          new JProperty("IsResourceFolder", true), // Mark as a root resource folder
                          new JProperty("filterType", folderTypes[folderKey]), // Type of resource it holds
                          new JProperty("folderName", folderName), // Display name
                          new JProperty("isDefaultView", true), // Standard view folder
                           new JProperty("localisedFolderName", $"ResourceTree_{folderName.Replace(" ", "")}"), // Localization key
                           new JProperty("resourceVersion", "1.1"), // GMFolder definition version
                           new JProperty("name", folderName),
                           new JProperty("tags", new JArray()),
                           new JProperty("resourceType", "GMFolder")
                     );
                     WriteJsonFile(folderYyPath, folderYyContent);
              } else {
                   UMT.Log($"Warning: Could not find definition or type for standard folder '{folderKey}'. It will be missing from the IDE view.");
              }
         }


        // Main .yyp content
        var yypContent = new JObject(
            new JProperty("resources", new JArray(ResourceList)), // List of all resource GUIDs and paths
            new JProperty("Options", new JArray( // List of Option files (e.g., options_main.yy, options_windows.yy)
                // Add references to created options files here
                new JObject(new JProperty("name", "Main"), new JProperty("path", "options/main/options_main.yy")),
                 new JObject(new JProperty("name", "Windows"), new JProperty("path", "options/windows/options_windows.yy"))
                 // Add other platforms (macOS, Linux, HTML5, etc.) if default files created
            )),
            new JProperty("isDnDProject", false), // Was it a Drag and Drop project?
            new JProperty("isEcma", false), // Using ECMA script variant? (Usually false)
            new JProperty("tutorialPath", ""), // Path to tutorial file (if any)
            new JProperty("configs", new JObject( // Build configurations
                new JProperty("name", "Default"), // Default config name
                new JProperty("children", new JArray()) // Child configs
            )),
            new JProperty("RoomOrderNodes", new JArray( // Order of rooms in the IDE Room Manager
                 // Add room GUIDs here in the desired order (e.g., based on index in Data.Rooms)
                 Data.Rooms.Select((room, index) => {
                      if (room == null) return null;
                      string roomName = GetResourceName(room);
                      Guid roomGuid = GetResourceGuid($"rooms/{roomName}");
                      return new JObject(new JProperty("roomId", new JObject(
                           new JProperty("name", roomGuid.ToString("D")),
                           new JProperty("path", $"rooms/{roomName}/{roomName}.yy")
                      )));
                 }).Where(j => j != null) // Filter out nulls if rooms were skipped
            )),
            new JProperty("Folders", new JArray(folderViews)), // The IDE folder structure
             new JProperty("AudioGroups", new JArray( // List of Audio Group resources (references to their .yy)
                 Data.AudioGroups.Select(ag => {
                     if (ag == null) return null;
                     string agName = GetResourceName(ag);
                      // Skip default if it matches standard GMS2 default name
                      if (agName.Equals("audiogroup_default", StringComparison.OrdinalIgnoreCase)) return null;
                     Guid agGuid = GetResourceGuid($"audiogroups/{agName}");
                     return new JObject(new JProperty("targets", -1), /*...*/ new JProperty("name", agName), new JProperty("path", $"audiogroups/{agName}.yy")); // Simplified - just ref path
                 }).Where(j => j != null)
                  // Add the default group reference manually
                 .Concat(new[] { new JObject( new JProperty("name", "audiogroup_default"), new JProperty("path", "audiogroups/audiogroup_default.yy")) })
            )),
             new JProperty("TextureGroups", new JArray( // List of Texture Group resources
                 (Data.TextureGroups ?? new List<UndertaleTextureGroup>()).Select(tg => {
                      if (tg == null) return null;
                     string tgName = GetResourceName(tg);
                       if (tgName.Equals("Default", StringComparison.OrdinalIgnoreCase)) return null; // Skip default
                     Guid tgGuid = GetResourceGuid($"texturegroups/{tgName}");
                      return new JObject(new JProperty("name", tgName), new JProperty("path", $"texturegroups/{tgName}.yy")); // Simplified ref
                 }).Where(j => j != null)
                 // Add the default group reference manually
                 .Concat(new[] { new JObject( new JProperty("name", "Default"), new JProperty("path", "texturegroups/Default.yy")) })
            )),
            new JProperty("IncludedFiles", new JArray( // List of Included File resources
                 (Data.IncludedFiles ?? new List<UndertaleIncludedFile>()).Select(incFile => {
                      if (incFile == null) return null;
                      string fileName = GetResourceName(incFile);
                      Guid fileGuid = GetResourceGuid($"includedfiles/{fileName}");
                      return new JObject(new JProperty("CopyToMask", -1), /*...*/ new JProperty("name", fileName), new JProperty("path", $"includedfiles/{fileName}.yy")); // Simplified ref
                 }).Where(j => j != null)
            )),
             // Metadata for the project
             new JProperty("MetaData", new JObject(
                 new JProperty("IDEVersion", GMS2_VERSION) // Target IDE version used for conversion
             )),
             // --- Root Level Properties ---
             new JProperty("projectVersion", "1.0"), // YYP format version
             new JProperty("packageId", ""), // Marketplace IDs
             new JProperty("productId", ""),
             new JProperty("parentProject", null), // Parent YYP file (usually null)
             new JProperty("YYPFormat", "1.2"), // YYP structure format version
              new JProperty("serialiseFrozenViewModels", false), // IDE state saving option
              new JProperty("resourceVersion", "1.0"), // Project resource version
              new JProperty("name", ProjectName), // Project name
              new JProperty("tags", new JArray()),
              new JProperty("resourceType", "GMProject")
        );

        WriteJsonFile(yypPath, yypContent);
         UMT.Log($"Project file created: {yypPath}");
    }


    private void CreateOptionsFiles() {
         // Create very basic option files. GMS2 will populate defaults if missing,
         // but having placeholders is good practice.
         string optionsDir = Path.Combine(OutputPath, "options");
         string mainOptionsDir = Path.Combine(optionsDir, "main");
         string windowsOptionsDir = Path.Combine(optionsDir, "windows");
          // Add other platform dirs (mac, linux, html5, android, ios) if needed

         Directory.CreateDirectory(mainOptionsDir);
         Directory.CreateDirectory(windowsOptionsDir);

         // options_main.yy
         string mainOptPath = Path.Combine(mainOptionsDir, "options_main.yy");
         var mainOptContent = new JObject(
             new JProperty("option_gameguid", ProjectGuid.ToString("B")), // {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
             new JProperty("option_gameid", ""), // Usually empty unless specific ID assigned
             new JProperty("option_game_speed", Data.GeneralInfo?.GameSpeed ?? 60), // Use original game speed (FPS) or default
             new JProperty("option_mips_for_3d_textures", false),
             new JProperty("option_draw_colour", 4294967295), // White (UINT ARGB)
             new JProperty("option_window_colour", 255), // Black (UINT BGR?) -> Check GMS format. Let's use black.
             new JProperty("option_steam_app_id", Data.GeneralInfo?.SteamAppID ?? 0), // Use original Steam ID or 0
             new JProperty("option_sci_usesci", false), // Use source control?
             new JProperty("option_author", Data.GeneralInfo?.Author?.Content ?? ""),
             new JProperty("option_collision_compatibility", true), // GMS1 collision compatibility? (Might be needed)
             new JProperty("option_copy_on_write_enabled", false), // CoW setting
             new JProperty("option_lastchanged", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")),
             new JProperty("option_spine_licence", false), // Spine license needed?
             new JProperty("option_template_image", "${base_options_dir}/main/template_image.png"), // Default paths
             new JProperty("option_template_icon", "${base_options_dir}/main/template_icon.png"),
             new JProperty("option_template_description", null),
              new JProperty("resourceVersion", "1.4"), // GMMainOptions version
              new JProperty("name", "Main"),
              new JProperty("tags", new JArray()),
              new JProperty("resourceType", "GMMainOptions")
         );
         WriteJsonFile(mainOptPath, mainOptContent);
          // Copy/Create placeholder template images/icons if desired (optional)

         // options_windows.yy (Example)
         string winOptPath = Path.Combine(windowsOptionsDir, "options_windows.yy");
         var winOptContent = new JObject(
             new JProperty("option_windows_display_name", Data.GeneralInfo?.DisplayName?.Content ?? ProjectName),
             new JProperty("option_windows_executable_name", $"{ProjectName}.exe"),
             new JProperty("option_windows_version", "1.0.0.0"), // Default version
             new JProperty("option_windows_company_info", Data.GeneralInfo?.Author?.Content ?? ""),
             new JProperty("option_windows_product_info", ProjectName),
             new JProperty("option_windows_copyright_info", $"(c) {DateTime.Now.Year}"),
             new JProperty("option_windows_description_info", ProjectName),
             new JProperty("option_windows_display_cursor", true),
             new JProperty("option_windows_icon", "${base_options_dir}/windows/icons/icon.ico"),
             new JProperty("option_windows_save_location", 0), // 0 = AppData, 1 = Local directory
             new JProperty("option_windows_splash_screen", "${base_options_dir}/windows/splash/splash.png"),
             new JProperty("option_windows_use_splash", false),
             new JProperty("option_windows_start_fullscreen", room?.Flags.HasFlag(UndertaleRoom.RoomFlags.FullScreen) ?? false), // Use first room's fullscreen flag? Risky. Default false.
             new JProperty("option_windows_allow_fullscreen_switching", true),
             new JProperty("option_windows_interpolate_pixels", false), // Pixel interpolation
             new JProperty("option_windows_vsync", false), // VSync enabled?
             new JProperty("option_windows_resize_window", true),
             new JProperty("option_windows_borderless", false),
             new JProperty("option_windows_scale", 0), // 0 = Keep aspect ratio, 1 = Full scale
             new JProperty("option_windows_copy_exe_to_dest", false),
             new JProperty("option_windows_sleep_margin", 10),
              new JProperty("option_windows_texture_page", "2048x2048"), // Default texture page size
              new JProperty("option_windows_installer_finished", "${base_options_dir}/windows/installer/finished.bmp"),
              new JProperty("option_windows_installer_header", "${base_options_dir}/windows/installer/header.bmp"),
              new JProperty("option_windows_license", "${base_options_dir}/windows/installer/license.txt"),
              new JProperty("option_windows_nsis_file", "${base_options_dir}/windows/installer/nsis_script.nsi"),
              new JProperty("option_windows_enable_steam", (Data.GeneralInfo?.SteamAppID ?? 0) > 0), // Enable Steam if AppID exists?
              new JProperty("option_windows_disable_sandbox", false),
              new JProperty("option_windows_steam_use_alternative_launcher", false),
              new JProperty("resourceVersion", "1.1"), // GMWindowsOptions version
              new JProperty("name", "Windows"),
              new JProperty("tags", new JArray()),
              new JProperty("resourceType", "GMWindowsOptions")
         );
         WriteJsonFile(winOptPath, winOptContent);
          // Copy/Create placeholder icon/splash/installer files if desired (optional)

          UMT.Log("Default options files created.");
    }

     private void CreateDefaultConfig() {
          string configDir = Path.Combine(OutputPath, "configs");
          string defaultCfgPath = Path.Combine(configDir, "Default.config"); // GMS2 uses .config extension

          // Very basic config file structure
          string configContent = @"
[Default]
ConfigName=""Default""
";
           try {
                File.WriteAllText(defaultCfgPath, configContent);
                 UMT.Log($"Created default config file: {defaultCfgPath}");
           } catch (Exception ex) {
                UMT.Log($"Error creating default config file: {ex.Message}");
           }
     }

}

// Add the script to UMT's Script menu
public static class ScriptEntry
{
    public static IUMTScript Script = new GMS2Converter();
}
