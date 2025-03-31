
// Undertale Mod Tool Script: Convert to GameMaker Studio 2 Project
// Version: 1.2 (Error Fixes & Improvements)
// Author: [Your Name or AI Assistant]
// Description: Converts the currently loaded UMT data into a GMS2 project structure.
//              Manual adjustments in GMS2 (especially GML code, tilesets, fonts)
//              will be required after conversion.
// Requirements: Newtonsoft.Json.dll must be accessible by UMT.

#r "System.IO.Compression.FileSystem"
#r "System.Drawing"
#r "Newtonsoft.Json" // <<< Essential dependency! Ensure UMT can find this.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class GMS2Converter : IUMTScript
{
    // --- Configuration ---
    private const string GMS2_VERSION = "2.3.7.606"; // Target GMS2 IDE version (adjust if needed)
    private const string RUNTIME_VERSION = "2.3.7.476"; // Target GMS2 Runtime version (adjust if needed)
    private const bool TRY_DECOMPILE_IF_NEEDED = true;
    private const bool WARN_ON_MISSING_DECOMPILED_CODE = true;

    // --- Internal State ---
    private UndertaleData Data;
    private string OutputPath;
    private string ProjectName;
    private Guid ProjectGuid;
    private Dictionary<string, Guid> ResourceGuids = new Dictionary<string, Guid>();
    private Dictionary<string, string> ResourcePaths = new Dictionary<string, string>();
    private Dictionary<UndertaleResource, string> ResourceToNameMap = new Dictionary<UndertaleResource, string>();
    private List<string> CreatedDirectories = new List<string>();

    // Resource Lists for .yyp file
    private List<JObject> ResourceList = new List<JObject>();
    private Dictionary<string, List<string>> FolderStructure = new Dictionary<string, List<string>>(); // Key: folderName, Value: List<ResourceGUID>

    public void Execute(UndertaleData data)
    {
        this.Data = data;
        if (Data == null)
        {
            UMT.ShowMessage("No data loaded in Undertale Mod Tool.");
            return;
        }

        // Pre-check for Newtonsoft.Json
        try
        {
             var jsonTest = JObject.Parse("{\"test\": \"ok\"}");
             if (jsonTest["test"].ToString() != "ok") throw new Exception("JSON Test Failed");
              UMT.Log("Newtonsoft.Json library seems accessible.");
        }
        catch(Exception jsonEx)
        {
             UMT.ShowError($"Error accessing Newtonsoft.Json library: {jsonEx.Message}\n\nThis script requires Newtonsoft.Json.dll to be available in the UMT environment (e.g., in the UMT folder).\nPlease ensure it's present and try again.\nConversion aborted.");
             UMT.Log("Newtonsoft.Json check failed. Aborting.");
             return;
        }


        // Check for decompiled code
        if (Data.Code == null || !Data.Code.Any() || Data.Code.All(c => c.Decompiled == null))
        {
            if (TRY_DECOMPILE_IF_NEEDED)
            {
                UMT.Log("Code not decompiled or missing. Attempting decompilation (this might require manual action in UMT)...");
                try
                {
                    // Trigger UMT's decompilation if possible (API might vary)
                    // Example: UndertaleModTool.Instance.DecompileAll();
                    // If no direct API, just log the need for manual decompilation.
                    UMT.Log(">> Please ensure you have run 'Decompile All' in UMT if automatic decompilation fails. <<");

                    // Re-check after potential decompilation attempt
                    if (Data.Code == null || !Data.Code.Any() || Data.Code.All(c => c.Decompiled == null))
                    {
                         UMT.Log("Warning: Code still appears decompiled after attempt. Conversion will proceed but scripts/events will be empty.");
                    } else {
                         UMT.Log("Decompilation seems successful or was already done.");
                    }
                }
                catch (Exception ex)
                {
                    UMT.ShowError("Error during decompilation attempt: " + ex.Message + "\nPlease try decompiling manually in UMT first.");
                    // Decide whether to continue or abort if decompilation fails
                    // return; // Optional: Abort if code is critical
                }
            }
            else if (WARN_ON_MISSING_DECOMPILED_CODE)
            {
                 UMT.Log("Warning: Code is not decompiled. Scripts and event code will be missing in the GMS2 project.");
            }
        } else {
             UMT.Log("Decompiled code found.");
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
                OutputPath = Path.Combine(OutputPath, ProjectName);

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
                    try {
                        Directory.Delete(OutputPath, true);
                    } catch (IOException ioEx) {
                         UMT.ShowError($"Error deleting directory (is it open elsewhere?): {ioEx.Message}");
                         return;
                    }
                }
                else
                {
                    UMT.ShowMessage("Conversion aborted.");
                    return;
                }
            }
            Directory.CreateDirectory(OutputPath);
            CreatedDirectories.Add(OutputPath);

            ProjectGuid = Guid.NewGuid();

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
            CreateGMS2Directory("options/main");
            CreateGMS2Directory("options/windows"); // Add other platforms as needed
            CreateGMS2Directory("datafiles");
            CreateGMS2Directory("configs");
            CreateGMS2Directory("timelines");
            CreateGMS2Directory("paths");
            CreateGMS2Directory("audiogroups");
            CreateGMS2Directory("texturegroups");
            CreateGMS2Directory("includedfiles");
            CreateGMS2Directory("folders"); // For IDE view definitions

            // Create default config
             CreateDefaultConfig();

             // Build Resource Name Map (Handle potential duplicates)
             BuildResourceNameMap();

            // --- 3. Convert Resources ---
             UMT.Log("Starting resource conversion...");
            // Define conversion order - dependencies first
            var conversionSteps = new List<Tuple<string, Action>> {
                Tuple.Create("Audio Groups", (Action)ConvertAudioGroups),
                Tuple.Create("Texture Groups", (Action)ConvertTextureGroups),
                Tuple.Create("Sprites (incl. Backgrounds as Sprites)", (Action)ConvertSprites),
                Tuple.Create("Sounds", (Action)ConvertSounds),
                Tuple.Create("Tilesets (from Backgrounds/Sprites)", (Action)ConvertTilesets), // Depends on Sprites
                Tuple.Create("Fonts", (Action)ConvertFonts), // Depends on Sprites
                Tuple.Create("Paths", (Action)ConvertPaths),
                Tuple.Create("Scripts", (Action)ConvertScripts),
                Tuple.Create("Shaders", (Action)ConvertShaders),
                Tuple.Create("Timelines", (Action)ConvertTimelines), // Depends on Scripts (for code)
                Tuple.Create("Objects", (Action)ConvertObjects), // Depends on Sprites, Scripts
                Tuple.Create("Rooms", (Action)ConvertRooms), // Depends on Objects, Tilesets, Sprites
                Tuple.Create("Included Files", (Action)ConvertIncludedFiles),
                Tuple.Create("Extensions", (Action)ConvertExtensions),
                Tuple.Create("Notes", (Action)ConvertNotes) // Placeholder
            };

            foreach(var step in conversionSteps) {
                 try {
                     step.Item2(); // Execute conversion action
                 } catch (Exception stepEx) {
                      UMT.Log($" >>> CRITICAL ERROR during '{step.Item1}' conversion step: {stepEx.Message}\n{stepEx.StackTrace}");
                      UMT.ShowError($"Critical error during '{step.Item1}' conversion. Check logs. Aborting further steps.");
                      // Decide whether to attempt cleanup or stop immediately
                      throw; // Re-throw to stop the main try-catch
                 }
            }


            // --- 4. Create Project File (.yyp) ---
             UMT.Log("Creating main project file (.yyp)...");
             CreateProjectFile();

            // --- 5. Create Options Files ---
             UMT.Log("Creating default options files...");
             CreateOptionsFiles();


             UMT.Log($"GMS2 Project '{ProjectName}' conversion process finished.");
             UMT.ShowMessage($"Conversion complete! Project saved to:\n{OutputPath}\n\n" +
                             "IMPORTANT:\n" +
                             "- Open the project in GameMaker Studio 2.\n" +
                             "- Expect GML code errors due to GMS1/GMS2 incompatibility. Manual code fixes are required.\n" +
                             "- Review Tilesets (tile size, offsets) and repaint Tile Layers in rooms.\n" +
                             "- Check Fonts and potentially re-import them in GMS2.\n" +
                             "- Verify resource references (sprites in objects, sounds, etc.).");

        }
        catch (Exception ex)
        {
            UMT.ShowError($"An error occurred during conversion: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
             UMT.Log($"Error: Conversion failed. See details above.");
            // Optional: Cleanup partially created directories on error
            /*
            DialogResult cleanupResult = MessageBox.Show("An error occurred. Attempt to clean up created project files?", "Cleanup on Error", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (cleanupResult == DialogResult.Yes) { ... }
            */
        }
        finally
        {
            // Reset state
            ResourceGuids.Clear();
            ResourcePaths.Clear();
            ResourceList.Clear();
            FolderStructure.Clear();
            ResourceToNameMap.Clear();
            CreatedDirectories.Clear();
        }
    }

    // === Helper Functions ===

    private void CreateGMS2Directory(string relativePath)
    {
        string fullPath = Path.Combine(OutputPath, relativePath);
        if (!Directory.Exists(fullPath))
        {
            try {
                Directory.CreateDirectory(fullPath);
                CreatedDirectories.Add(fullPath);
                // UMT.Log($"Created directory: {fullPath}"); // Less verbose logging

                // Add top-level folders to structure tracker immediately if needed for .yyp Folders section
                string topLevelFolderName = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                 if (topLevelFolderName != "options" && topLevelFolderName != "configs" && topLevelFolderName != "folders" && topLevelFolderName != "datafiles" && !FolderStructure.ContainsKey(topLevelFolderName)) {
                      FolderStructure[topLevelFolderName] = new List<string>();
                 }

            } catch (Exception ex) {
                 UMT.Log($"ERROR: Failed to create directory {fullPath}: {ex.Message}");
                 throw; // Propagate error if critical folder creation fails
            }
        }
    }

    private string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "unnamed";
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
        string sanitized = Regex.Replace(name, invalidRegStr, "_").Trim();
        sanitized = sanitized.Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "unnamed_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        // GMS2 Reserved Keywords (add more as needed) - case-insensitive check
        string[] reserved = {
             "all", "noone", "global", "local", "self", "other", "true", "false", "object_index", "id",
             "begin", "end", "if", "then", "else", "for", "while", "do", "until", "repeat", "switch",
             "case", "default", "break", "continue", "return", "exit", "with", "var", "mod", "div",
             "not", "and", "or", "xor", "enum", "constructor", "function", "new", "delete", "try",
             "catch", "finally", "throw", "static", "argument", "argument_count", "undefined", "infinity", "nan"
         };
        if (reserved.Contains(sanitized.ToLowerInvariant()))
        {
            sanitized += "_";
             UMT.Log($"Sanitized reserved name '{name}' to '{sanitized}'");
        }

         // Prevent names starting with numbers (common GML restriction)
         if (Regex.IsMatch(sanitized, @"^\d")) {
              sanitized = "_" + sanitized;
               UMT.Log($"Sanitized name starting with digit '{name}' to '{sanitized}'");
         }

        return sanitized;
    }
     private string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "unnamed_folder";
        string invalidChars = Regex.Escape(new string(Path.GetInvalidPathChars()));
         string invalidRegStr = string.Format(@"([{0}]+)", invalidChars);
        string sanitized = Regex.Replace(name, invalidRegStr, "_").Trim();
         sanitized = sanitized.Replace(' ', '_');
         if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "unnamed_folder_" + Guid.NewGuid().ToString("N").Substring(0,8);
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

     private void BuildResourceNameMap()
     {
         UMT.Log("Building resource name map...");
         var nameCounts = new Dictionary<string, int>();
         ResourceToNameMap.Clear(); // Ensure clean map

         Action<IList<UndertaleNamedResource>> processList = (list) => {
             if (list == null) return;
             foreach (var res in list.Where(r => r?.Name?.Content != null)) {
                  if (ResourceToNameMap.ContainsKey(res)) continue; // Already processed?

                 string baseName = SanitizeFileName(res.Name.Content);
                 string uniqueName = baseName;
                 int count = 0;

                 // Find unique name
                 while (ResourceToNameMap.Values.Contains(uniqueName, StringComparer.OrdinalIgnoreCase) || (nameCounts.ContainsKey(baseName.ToLowerInvariant()) && uniqueName == baseName) ) // Check against existing values AND original base name usage
                 {
                     if (!nameCounts.ContainsKey(baseName.ToLowerInvariant())) nameCounts[baseName.ToLowerInvariant()] = 1;
                     count = nameCounts[baseName.ToLowerInvariant()]++; // Increment *after* getting current count for suffix
                     uniqueName = $"{baseName}_{count}";
                 }

                 if (count > 0) { // If we appended a number, mark the base name as used
                     if (!nameCounts.ContainsKey(baseName.ToLowerInvariant())) {
                          nameCounts[baseName.ToLowerInvariant()] = count + 1;
                     }
                 } else if (!nameCounts.ContainsKey(baseName.ToLowerInvariant())) {
                      // Mark base name as used if it wasn't already
                      nameCounts[baseName.ToLowerInvariant()] = 1;
                 }

                 ResourceToNameMap[res] = uniqueName;
             }
         };

         // Process resource types that have names
          processList(Data.Sprites?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Sounds?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Objects?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Rooms?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Scripts?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Shaders?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Fonts?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Paths?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Timelines?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Backgrounds?.Cast<UndertaleNamedResource>().ToList()); // Will be used for Sprites/Tilesets

           // TextureGroups and AudioGroups
           Action<IList<UndertaleNamedResourceGroup>> processGroupList = (list) => {
                if (list == null) return;
                foreach (var res in list.Where(r => r?.Name?.Content != null)) {
                    if (ResourceToNameMap.ContainsKey(res)) continue;
                     string baseName = SanitizeFileName(res.Name.Content);
                     // Special handling for default group names to match GMS2 conventions
                     if (baseName.Equals("default", StringComparison.OrdinalIgnoreCase)) {
                          if (res is UndertaleAudioGroup) baseName = "audiogroup_default";
                          else if (res is UndertaleTextureGroup) baseName = "Default"; // GMS2 uses capital 'D'
                     }

                     string uniqueName = baseName;
                     int count = 0;
                     while (ResourceToNameMap.Values.Contains(uniqueName, StringComparer.OrdinalIgnoreCase) || (nameCounts.ContainsKey(baseName.ToLowerInvariant()) && uniqueName == baseName) ) {
                         if (!nameCounts.ContainsKey(baseName.ToLowerInvariant())) nameCounts[baseName.ToLowerInvariant()] = 1;
                         count = nameCounts[baseName.ToLowerInvariant()]++;
                         uniqueName = $"{baseName}_{count}";
                     }
                     if (count > 0) { if (!nameCounts.ContainsKey(baseName.ToLowerInvariant())) { nameCounts[baseName.ToLowerInvariant()] = count + 1; } }
                     else if (!nameCounts.ContainsKey(baseName.ToLowerInvariant())) { nameCounts[baseName.ToLowerInvariant()] = 1; }
                     ResourceToNameMap[res] = uniqueName;
                }
           };
            processGroupList(Data.TextureGroups?.Cast<UndertaleNamedResourceGroup>().ToList());
            processGroupList(Data.AudioGroups?.Cast<UndertaleNamedResourceGroup>().ToList());


           // Included files (use file name part)
          if (Data.IncludedFiles != null) {
              foreach(var incFile in Data.IncludedFiles.Where(f => f?.Name?.Content != null)) {
                  if (ResourceToNameMap.ContainsKey(incFile)) continue;
                  string baseName = SanitizeFileName(Path.GetFileName(incFile.Name.Content));
                  string uniqueName = baseName;
                  int count = 0;
                   while (ResourceToNameMap.Values.Contains(uniqueName, StringComparer.OrdinalIgnoreCase) || (nameCounts.ContainsKey(baseName.ToLowerInvariant()) && uniqueName == baseName) ) {
                      if (!nameCounts.ContainsKey(baseName.ToLowerInvariant())) nameCounts[baseName.ToLowerInvariant()] = 1;
                      count = nameCounts[baseName.ToLowerInvariant()]++;
                      uniqueName = $"{baseName}_{count}";
                  }
                   if (count > 0) { if (!nameCounts.ContainsKey(baseName.ToLowerInvariant())) { nameCounts[baseName.ToLowerInvariant()] = count + 1; } }
                   else if (!nameCounts.ContainsKey(baseName.ToLowerInvariant())) { nameCounts[baseName.ToLowerInvariant()] = 1; }
                  ResourceToNameMap[incFile] = uniqueName;
              }
          }

          // Extensions
         processList(Data.Extensions?.Cast<UndertaleNamedResource>().ToList());


         UMT.Log($"Built map for {ResourceToNameMap.Count} named resources.");
     }

     private string GetResourceName(UndertaleResource res, string defaultName = "unknown_resource")
     {
         if (res != null && ResourceToNameMap.TryGetValue(res, out string name))
         {
             return name;
         }
          // Fallback for unnamed or unmapped resources
          if (res is UndertaleChunk Tagi t) {
              return SanitizeFileName($"resource_{t.GetType().Name}_{Guid.NewGuid().ToString("N").Substring(0,4)}");
          }
          return defaultName;
     }

    private void WriteJsonFile(string filePath, JObject jsonContent)
    {
        try
        {
             // Ensure the directory exists before writing
             Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, jsonContent.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
             UMT.Log($"ERROR writing JSON file {filePath}: {ex.Message}");
             throw; // Re-throw to halt conversion on critical file write error
        }
    }

     // Creates a resource reference JSON object for linking (e.g., sprite in object)
     private JObject CreateResourceReference(string name, Guid guid, string resourceTypeFolder) {
         if (guid == Guid.Empty || string.IsNullOrEmpty(name)) {
             return null; // GMS2 uses null for no reference
         }
         // Construct the standard GMS2 path format
         string path = $"{resourceTypeFolder}/{name}/{name}.yy";
         // Special cases for default groups
         if(resourceTypeFolder == "audiogroups" && name == "audiogroup_default") path = "audiogroups/audiogroup_default";
         if(resourceTypeFolder == "texturegroups" && name == "Default") path = "texturegroups/Default";

         return new JObject(
             new JProperty("name", name),
             new JProperty("path", path)
         );
     }

      // Overload for resources looked up by the UndertaleResource object
       private JObject CreateResourceReference(UndertaleResource res, string resourceTypeFolder) {
           if (res == null) return null;
           string name = GetResourceName(res); // Get the sanitized, unique name
            if (string.IsNullOrEmpty(name) || name.StartsWith("unknown_")) {
                UMT.Log($"Warning: Could not get valid name for resource of type {res.GetType().Name}. Cannot create reference.");
                return null;
            }

            // Construct the expected resource key used in ResourceGuids dictionary
            string resourceKey = $"{resourceTypeFolder}/{name}";

            // Get the GUID, generating if it doesn't exist yet (e.g., forward reference)
             Guid guid = GetResourceGuid(resourceKey); // This ensures a GUID exists

            // Now create the reference using the name and GUID
            return CreateResourceReference(name, guid, resourceTypeFolder);
       }


    // === Resource Conversion Functions ===

    private void ConvertSprites()
    {
        UMT.Log("Converting Sprites (incl. Backgrounds as Sprites)...");
        if (Data.Sprites == null && Data.Backgrounds == null) {
             UMT.Log("No Sprites or Backgrounds found to convert.");
             return;
        }

        string spriteDir = Path.Combine(OutputPath, "sprites");
        List<UndertaleSprite> allSprites = new List<UndertaleSprite>();

        if (Data.Sprites != null) allSprites.AddRange(Data.Sprites);

        // Convert Backgrounds that have texture data into pseudo-Sprites
        if (Data.Backgrounds != null) {
             UMT.Log($"Checking {Data.Backgrounds.Count} Backgrounds for potential sprite conversion...");
            int bgConvertedCount = 0;
            foreach(var bg in Data.Backgrounds.Where(b => b?.Texture?.TexturePage != null && b.Name != null)) {
                 // Prevent duplicates if a Background has the same name as an existing Sprite
                 if (allSprites.Any(s => GetResourceName(s) == GetResourceName(bg))) {
                     // UMT.Log($"Skipping Background '{GetResourceName(bg)}' as a sprite, a Sprite with the same name exists.");
                     continue;
                 }

                 var pseudoSprite = new UndertaleSprite {
                     Name = bg.Name,
                     Width = bg.Texture.TexturePage.SourceWidth > 0 ? bg.Texture.TexturePage.SourceWidth : bg.Texture.TexturePage.TargetWidth,
                     Height = bg.Texture.TexturePage.SourceHeight > 0 ? bg.Texture.TexturePage.SourceHeight : bg.Texture.TexturePage.TargetHeight,
                     MarginLeft = 0,
                     MarginRight = (ushort)Math.Max(0, (bg.Texture.TexturePage.TargetWidth > 0 ? bg.Texture.TexturePage.TargetWidth : 1) - 1),
                     MarginBottom = (ushort)Math.Max(0, (bg.Texture.TexturePage.TargetHeight > 0 ? bg.Texture.TexturePage.TargetHeight : 1) - 1),
                     MarginTop = 0,
                     OriginX = 0,
                     OriginY = 0,
                     BBoxMode = UndertaleSprite.BoundingBoxMode.Automatic,
                     SepMasks = 0,
                     PlaybackSpeed = 15,
                     PlaybackSpeedType = UndertaleSprite.SpritePlaybackSpeedType.FramesPerSecond,
                     Textures = new List<UndertaleSprite.TextureEntry> { bg.Texture },
                     // Additional properties needed? Maybe store original type?
                 };

                  if (pseudoSprite.Width > 0 && pseudoSprite.Height > 0) {
                      allSprites.Add(pseudoSprite);
                      bgConvertedCount++;
                  } else {
                       UMT.Log($"Warning: Skipping Background '{GetResourceName(bg)}' as pseudo-sprite due to zero dimensions.");
                  }
            }
             UMT.Log($"- Converted {bgConvertedCount} Backgrounds to pseudo-sprites.");
        }


        UMT.Log($"Processing {allSprites.Count} total sprite resources...");
        foreach (var sprite in allSprites)
        {
            if (sprite?.Name?.Content == null) continue; // Skip invalid entries
            string spriteName = GetResourceName(sprite);
            string resourceKey = $"sprites/{spriteName}";
            Guid spriteGuid = GetResourceGuid(resourceKey);
            string spritePath = Path.Combine(spriteDir, spriteName);
            string yyPath = Path.Combine(spritePath, $"{spriteName}.yy");
            string imagesPath = Path.Combine(spritePath, "images"); // GMS2 standard

             // UMT.Log($"Converting Sprite: {spriteName}"); // Less verbose

            try
            {
                Directory.CreateDirectory(spritePath);
                Directory.CreateDirectory(imagesPath);
                CreatedDirectories.Add(spritePath); // Track for potential cleanup
                CreatedDirectories.Add(imagesPath);

                List<JObject> frameList = new List<JObject>();
                List<Guid> frameGuids = new List<Guid>();
                Guid firstFrameGuid = Guid.Empty; // Needed for layer ref

                // Extract Frames
                for (int i = 0; i < sprite.Textures.Count; i++)
                {
                    var texEntry = sprite.Textures[i];
                    if (texEntry?.TexturePage == null)
                    {
                        UMT.Log($"Warning: Sprite '{spriteName}' frame {i} has missing texture data. Skipping frame.");
                        continue;
                    }

                    Guid frameGuid = Guid.NewGuid();
                    frameGuids.Add(frameGuid);
                    if (i == 0) firstFrameGuid = frameGuid;

                    string frameFileName = $"{frameGuid}.png";
                    string frameFilePath = Path.Combine(imagesPath, frameFileName);

                    try
                    {
                        // Use DirectBitmap for potentially faster access if available
                        using (DirectBitmap frameBitmap = TextureWorker.GetTexturePageImageRect(texEntry.TexturePage, Data))
                        {
                            if (frameBitmap != null && frameBitmap.Bitmap != null)
                            {
                                frameBitmap.Bitmap.Save(frameFilePath, ImageFormat.Png);
                            }
                            else
                            {
                                 UMT.Log($"Warning: Failed to extract image for sprite '{spriteName}' frame {i}. Creating empty placeholder.");
                                using (var placeholder = new Bitmap(Math.Max(1, sprite.Width), Math.Max(1, sprite.Height), PixelFormat.Format32bppArgb))
                                using (var g = Graphics.FromImage(placeholder)) {
                                     g.Clear(Color.FromArgb(128, 255, 0, 255)); // Transparent Magenta
                                     placeholder.Save(frameFilePath, ImageFormat.Png);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UMT.Log($"ERROR extracting frame {i} for sprite '{spriteName}': {ex.Message}. Creating placeholder.");
                         try { // Ensure placeholder can be created even after error
                              using (var placeholder = new Bitmap(Math.Max(1, sprite.Width), Math.Max(1, sprite.Height), PixelFormat.Format32bppArgb))
                              using (var g = Graphics.FromImage(placeholder)) {
                                  g.Clear(Color.FromArgb(128, 255, 0, 255)); // Transparent Magenta
                                  placeholder.Save(frameFilePath, ImageFormat.Png);
                              }
                         } catch (Exception phEx) {
                              UMT.Log($"ERROR creating placeholder for frame {i} of sprite '{spriteName}': {phEx.Message}");
                         }
                    }

                    // Create the frame object for the main frame list in sprite .yy
                     var frameObject = new JObject(
                         // new JProperty("id", frameGuid.ToString("D")), // Old format? Check GMS2.3+ .yy
                         // new JProperty("Key", i.ToString()), // Old format?
                         new JProperty("Config", "Default"),
                         new JProperty("FrameId", frameGuid.ToString("D")),
                         new JProperty("LayerId", null), // Filled later?
                         new JProperty("resourceVersion", "1.0"),
                         new JProperty("name", frameGuid.ToString("N")), // Use N format (no hyphens) for name? Check GMS2 .yy
                         new JProperty("tags", new JArray()),
                         new JProperty("resourceType", "GMSpriteFrame")
                     );
                    frameList.Add(frameObject);
                }

                 // If no frames were added (e.g., all texture data missing), create a single placeholder frame
                 if (frameGuids.Count == 0) {
                      UMT.Log($"Warning: Sprite '{spriteName}' had no valid frames. Creating a single placeholder frame.");
                      Guid frameGuid = Guid.NewGuid();
                      frameGuids.Add(frameGuid);
                      firstFrameGuid = frameGuid;
                      string frameFileName = $"{frameGuid}.png";
                      string frameFilePath = Path.Combine(imagesPath, frameFileName);
                      try {
                           using (var placeholder = new Bitmap(Math.Max(1, sprite.Width), Math.Max(1, sprite.Height), PixelFormat.Format32bppArgb))
                           using (var g = Graphics.FromImage(placeholder)) {
                               g.Clear(Color.FromArgb(128, 0, 255, 255)); // Transparent Cyan placeholder
                               placeholder.Save(frameFilePath, ImageFormat.Png);
                           }
                           var frameObject = new JObject( /* ... create frame JObject like above ... */
                                new JProperty("Config", "Default"), new JProperty("FrameId", frameGuid.ToString("D")),
                                new JProperty("LayerId", null), new JProperty("resourceVersion", "1.0"),
                                new JProperty("name", frameGuid.ToString("N")), new JProperty("tags", new JArray()),
                                new JProperty("resourceType", "GMSpriteFrame")
                           );
                           frameList.Add(frameObject);
                      } catch (Exception phEx) {
                           UMT.Log($"ERROR creating placeholder frame for empty sprite '{spriteName}': {phEx.Message}");
                           // Allow continuing without frames? Might cause GMS2 load errors.
                      }
                 }


                 // Determine GMS2 BBox Mode & Collision Kind
                 int gms2BBoxMode = 0; // 0: Automatic, 1: Full Image, 2: Manual
                 int gms2CollisionKind = 1; // 1: Rectangle, 2: Rotated Rectangle, 0: Precise, 4: Diamond, 5: Precise Per Frame
                 switch (sprite.BBoxMode) {
                     case UndertaleSprite.BoundingBoxMode.Automatic: gms2BBoxMode = 0; break;
                     case UndertaleSprite.BoundingBoxMode.FullImage: gms2BBoxMode = 1; break;
                     case UndertaleSprite.BoundingBoxMode.Manual: gms2BBoxMode = 2; break;
                     default: gms2BBoxMode = 0; break;
                 }
                 if (sprite.SepMasks > 0) gms2CollisionKind = 0; // Precise (GMS1 SepMasks implies precise)
                 else gms2CollisionKind = 1; // Default to Rectangle otherwise

                 // Determine Texture Group
                 string textureGroupName = "Default";
                 Guid textureGroupGuid = GetResourceGuid("texturegroups/Default"); // Get default group GUID
                 if (sprite.Textures.Count > 0 && sprite.Textures[0].TexturePage != null && Data.TextureGroups != null) {
                    var utTexturePage = sprite.Textures[0].TexturePage;
                    var utGroup = Data.TextureGroups.FirstOrDefault(tg => tg.Pages.Contains(utTexturePage));
                     if(utGroup != null) {
                         textureGroupName = GetResourceName(utGroup); // Get potentially renamed group name
                         textureGroupGuid = GetResourceGuid($"texturegroups/{textureGroupName}");
                     }
                 }
                 JObject textureGroupRef = CreateResourceReference(textureGroupName, textureGroupGuid, "texturegroups");

                 // Layer information (GMS2 sprites need at least one layer)
                 Guid imageLayerGuid = Guid.NewGuid();
                 var imageLayer = new JObject(
                     new JProperty("visible", true),
                     new JProperty("isLocked", false),
                     new JProperty("blendMode", 0),
                     new JProperty("opacity", 100.0),
                     new JProperty("displayName", "default"), // Layer name in IDE
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", imageLayerGuid.ToString("D")), // Layer ID
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMImageLayer")
                 );
                 // Link the frames to this layer (by setting LayerId in frame objects - tricky with JObject)
                 // Alternatively, structure might associate frames with layers differently in sequence track.

                 // Sprite Sequence Track (defines frame order and timing)
                 var spriteFramesTrack = new JObject(
                     new JProperty("spriteId", null), // Usually null here
                     new JProperty("keyframes", new JObject(
                         new JProperty("Keyframes", new JArray(
                             frameGuids.Select((guid, index) => new JObject(
                                 new JProperty("Key", (float)index),
                                 new JProperty("Length", 1.0f),
                                 new JProperty("Stretch", false),
                                 new JProperty("Disabled", false),
                                 new JProperty("IsCreationKey", false),
                                 new JProperty("Channels", new JObject(
                                     new JProperty("0", new JObject( // Channel 0 holds the frame reference
                                         new JProperty("Id", new JObject( // Frame ID reference
                                             new JProperty("name", guid.ToString("D")), // Frame GUID
                                             new JProperty("path", $"sprites/{spriteName}/{spriteName}.yy")
                                         )),
                                         new JProperty("resourceVersion", "1.0"),
                                         new JProperty("resourceType", "SpriteFrameKeyframe") // Note: Type is SpriteFrameKeyframe here
                                     )) // End Channel 0
                                 )), // End Channels
                                 new JProperty("resourceVersion", "1.0"),
                                 new JProperty("resourceType", "Keyframe<SpriteFrameKeyframe>") // Type is Keyframe<SpriteFrameKeyframe>
                             )) // End Select
                         )), // End Keyframes Array
                         new JProperty("resourceVersion", "1.0"),
                         new JProperty("resourceType", "KeyframeStore<SpriteFrameKeyframe>") // Type is KeyframeStore<>
                     )), // End keyframes object
                     new JProperty("trackColour", 0),
                     new JProperty("inheritsTrackColour", true),
                     new JProperty("builtinName", 0), // 0 for frame tracks
                     new JProperty("traits", 0),
                     new JProperty("interpolation", 1), // 1 = Discrete interpolation
                     new JProperty("tracks", new JArray()), // Sub-tracks
                     new JProperty("events", new JArray()),
                     new JProperty("modifiers", new JArray()),
                     new JProperty("isCreationTrack", false),
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", "frames"), // Track name
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMSpriteFramesTrack") // Track type
                 );


                // Create Sprite .yy file content (JSON)
                var yyContent = new JObject(
                    new JProperty("bboxmode", gms2BBoxMode),
                    new JProperty("collisionKind", gms2CollisionKind),
                    new JProperty("type", 0), // Sprite type (always 0?)
                    new JProperty("origin", GetGMS2Origin(sprite.OriginX, sprite.OriginY, sprite.Width, sprite.Height)),
                    new JProperty("preMultiplyAlpha", false),
                    new JProperty("edgeFiltering", false),
                    new JProperty("collisionTolerance", 0),
                    new JProperty("swfPrecision", 2.525),
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
                    new JProperty("swatchColours", null),
                    new JProperty("gridX", 0),
                    new JProperty("gridY", 0),
                    new JProperty("frames", new JArray(frameList)), // List of frame definitions
                    new JProperty("sequence", new JObject(
                        new JProperty("timeUnits", 1), // 1 = Frames per second
                        new JProperty("playback", 1), // 1 = Normal playback?
                        new JProperty("playbackSpeed", (float)sprite.PlaybackSpeed),
                        // GMS1: 0=FPS, 1=FramesPerGameFrame. GMS2: 0=FramesPerGameFrame, 1=FramesPerSecond. Need to invert? Test this.
                        // Let's try mapping GMS1 0 -> GMS2 1, GMS1 1 -> GMS2 0
                        new JProperty("playbackSpeedType", sprite.PlaybackSpeedType == UndertaleSprite.SpritePlaybackSpeedType.FramesPerSecond ? 1 : 0),
                        new JProperty("autoRecord", true),
                        new JProperty("volume", 1.0f),
                        new JProperty("length", (float)frameGuids.Count), // Use actual number of frames converted
                        new JProperty("events", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MessageEventKeyframe>"))),
                        new JProperty("moments", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MomentsEventKeyframe>"))),
                        new JProperty("tracks", new JArray(spriteFramesTrack)), // Embed the sprite frames track
                        new JProperty("visibleRange", null),
                        new JProperty("lockOrigin", false),
                        new JProperty("showBackdrop", true),
                        new JProperty("showBackdropImage", false),
                        new JProperty("backdropImagePath", ""),
                        new JProperty("backdropImageOpacity", 0.5f),
                        new JProperty("backdropWidth", 1366),
                        new JProperty("backdropHeight", 768),
                        new JProperty("backdropXOffset", 0.0f),
                        new JProperty("backdropYOffset", 0.0f),
                        new JProperty("xorigin", sprite.OriginX),
                        new JProperty("yorigin", sprite.OriginY),
                        new JProperty("eventToFunction", new JObject()),
                        new JProperty("eventStubScript", null),
                        new JProperty("parent", CreateResourceReference(spriteName, spriteGuid, "sprites")), // Self reference
                        new JProperty("resourceVersion", "1.4"), // GMSequence version
                        new JProperty("name", spriteName), // Sequence name matches sprite name
                        new JProperty("tags", new JArray()),
                        new JProperty("resourceType", "GMSequence")
                    )),
                    new JProperty("layers", new JArray(imageLayer)), // Include the image layer definition
                    // *** ERROR FIX: Added comma after "layers" property ***
                    new JProperty("parent", new JObject(
                        new JProperty("name", "Sprites"),
                        new JProperty("path", "folders/Sprites.yy")
                    )),
                    // *** ERROR FIX: Added comma after "parent" property ***
                     new JProperty("resourceVersion", "1.0"), // GMSprite resource version
                     new JProperty("name", spriteName),
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMSprite")
                );


                WriteJsonFile(yyPath, yyContent);

                string relativePath = $"sprites/{spriteName}/{spriteName}.yy";
                AddResourceToProject(spriteName, spriteGuid, relativePath, "GMSprite", "sprites");
                ResourcePaths[resourceKey] = relativePath;

            }
            catch (Exception ex)
            {
                 UMT.Log($"ERROR processing sprite '{spriteName}': {ex.Message}\n{ex.StackTrace}");
                 // Optionally continue to next sprite or re-throw/abort
            }
        }
         UMT.Log("Sprite conversion finished.");
    }

    private int GetGMS2Origin(int x, int y, int w, int h) {
         // GMS2 Origin Enum: 0..8 TL to BR, 9 Custom
         // Check standard positions first
         if (x == 0 && y == 0) return 0;
         // Use integer division for center checks, similar to how GMS2 likely calculates it
         int midX = w / 2;
         int midY = h / 2;
         int rightX = Math.Max(0, w - 1); // Ensure non-negative index
         int bottomY = Math.Max(0, h - 1);

         if (x == midX && y == 0) return 1; // Top Centre
         if (x == rightX && y == 0) return 2; // Top Right
         if (x == 0 && y == midY) return 3; // Middle Left
         if (x == midX && y == midY) return 4; // Middle Centre
         if (x == rightX && y == midY) return 5; // Middle Right
         if (x == 0 && y == bottomY) return 6; // Bottom Left
         if (x == midX && y == bottomY) return 7; // Bottom Centre
         if (x == rightX && y == bottomY) return 8; // Bottom Right

         // If none of the standard positions match, it's custom
         return 9;
    }


    private void ConvertSounds()
    {
        UMT.Log("Converting Sounds...");
        if (Data.Sounds == null || !Data.Sounds.Any()) {
             UMT.Log("No sounds found to convert.");
             return;
        }

        string soundDir = Path.Combine(OutputPath, "sounds");
         int soundCount = 0;

        foreach (var sound in Data.Sounds)
        {
            if (sound?.Name?.Content == null || sound.AudioFile?.Data == null) continue;
            string soundName = GetResourceName(sound);
             string resourceKey = $"sounds/{soundName}";
             Guid soundGuid = GetResourceGuid(resourceKey);
            string soundPath = Path.Combine(soundDir, soundName);
            string yyPath = Path.Combine(soundPath, $"{soundName}.yy");
            string audioFileName = GetCompatibleAudioFileName(sound.AudioFile.Name.Content, soundName);
             if (string.IsNullOrEmpty(audioFileName)) {
                  UMT.Log($"Warning: Could not determine valid audio filename for sound '{soundName}'. Skipping.");
                  continue;
             }
            string audioFilePath = Path.Combine(soundPath, audioFileName);


            try
            {
                Directory.CreateDirectory(soundPath);
                CreatedDirectories.Add(soundPath);

                File.WriteAllBytes(audioFilePath, sound.AudioFile.Data);

                 // Determine Audio Group
                 string audioGroupName = "audiogroup_default"; // GMS2 Default group name
                 Guid audioGroupGuid = GetResourceGuid("audiogroups/audiogroup_default"); // Get default GUID
                  if (Data.AudioGroups != null) {
                      var utGroup = Data.AudioGroups.FirstOrDefault(ag => ag.Sounds.Contains(sound));
                      if (utGroup != null) {
                           audioGroupName = GetResourceName(utGroup); // Use mapped name
                           audioGroupGuid = GetResourceGuid($"audiogroups/{audioGroupName}");
                      }
                  }
                 JObject audioGroupRef = CreateResourceReference(audioGroupName, audioGroupGuid, "audiogroups");

                 // Create Sound .yy file content (JSON)
                 var yyContent = new JObject(
                     new JProperty("compression", GetGMS2CompressionType(sound.Type, Path.GetExtension(audioFilePath).ToLowerInvariant())),
                     new JProperty("volume", (float)sound.Volume),
                     new JProperty("preload", sound.Flags.HasFlag(UndertaleSound.AudioEntryFlags.Preload)),
                     // Bitrate/Samplerate might not be directly available or reliable in GMS1 data, use defaults?
                     new JProperty("bitRate", 128), // Default bitrate (kbps) - GMS2 might recalculate
                     new JProperty("sampleRate", 44100), // Default samplerate - GMS2 might recalculate
                     new JProperty("type", GetGMS2SoundType(sound.Type)),
                     new JProperty("bitDepth", 1), // 1 = 16-bit (default)
                     new JProperty("audioGroupId", audioGroupRef),
                     new JProperty("soundFile", audioFileName), // Just the filename
                     new JProperty("duration", 0.0f), // GMS2 calculates this
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

                string relativePath = $"sounds/{soundName}/{soundName}.yy";
                AddResourceToProject(soundName, soundGuid, relativePath, "GMSound", "sounds");
                 ResourcePaths[resourceKey] = relativePath;
                 soundCount++;

            }
            catch (Exception ex)
            {
                 UMT.Log($"ERROR processing sound '{soundName}': {ex.Message}");
            }
        }
         UMT.Log($"Sound conversion finished. Processed {soundCount} sounds.");
    }

     private string GetCompatibleAudioFileName(string originalFileName, string resourceName) {
          string extension = ".ogg"; // Default to ogg
          try {
               if (!string.IsNullOrEmpty(originalFileName)) {
                   extension = Path.GetExtension(originalFileName);
                    if (string.IsNullOrEmpty(extension) || extension.Length < 2) { // Basic validation
                         // Try guessing from data header if needed (complex)
                         extension = ".ogg"; // Fallback guess
                    }
               }
          } catch (ArgumentException) { // Handle invalid chars in originalFileName for Path methods
               extension = ".ogg"; // Fallback
          }
          // Ensure valid extension (.wav, .ogg, .mp3 are common)
          string extLower = extension.ToLowerInvariant();
          if (extLower != ".wav" && extLower != ".ogg" && extLower != ".mp3") {
               UMT.Log($"Warning: Original sound file '{originalFileName}' has unusual extension '{extension}'. Using '.ogg' instead.");
               extension = ".ogg";
          }

          return resourceName + extension;
     }

     private int GetGMS2SoundType(UndertaleSound.AudioTypeFlags utFlags) {
         // GMS2 type: 0 = Mono, 1 = Stereo, 2 = 3D
         // UT flags: Bit 1 might indicate 3D (needs verification per game)
         // If flags suggest 3D: return 2;
         // If flags suggest Mono: return 0;
         // Default to Stereo as it's most common for music/SFX.
         return 1;
     }

      private int GetGMS2CompressionType(UndertaleSound.AudioTypeFlags utFlags, string extension) {
         // GMS2 compression: 0 = Uncompressed, 1 = Compressed, 2 = UncompressOnLoad, 3 = CompressedStreamed
         bool isStreamed = utFlags.HasFlag(UndertaleSound.AudioTypeFlags.StreamFromDisk);
         // GMS1 flags for compression are less clear. Assume WAV = uncompressed, others = compressed.
         if (extension == ".wav") {
             // Is it streamed WAV? GMS2 might treat this as UncompressOnLoad or just Uncompressed.
             // Let's assume unstreamed WAV is Uncompressed, streamed WAV is UncompressOnLoad.
             return isStreamed ? 2 : 0;
         } else { // .ogg, .mp3
             return isStreamed ? 3 : 1; // Compressed Streamed or Compressed In Memory
         }
      }


    private void ConvertObjects()
    {
        UMT.Log("Converting Objects...");
        if (Data.Objects == null || !Data.Objects.Any()) {
            UMT.Log("No objects found to convert.");
            return;
        }

        string objDir = Path.Combine(OutputPath, "objects");
        int objCount = 0;

        foreach (var obj in Data.Objects)
        {
            if (obj?.Name?.Content == null) continue;
            string objName = GetResourceName(obj);
            string resourceKey = $"objects/{objName}";
            Guid objGuid = GetResourceGuid(resourceKey);
            string objPath = Path.Combine(objDir, objName);
            string yyPath = Path.Combine(objPath, $"{objName}.yy");


            try
            {
                Directory.CreateDirectory(objPath);
                CreatedDirectories.Add(objPath);

                List<JObject> eventList = new List<JObject>();

                // Process Events
                if (obj.Events != null)
                {
                    foreach (var eventContainer in obj.Events) // Events are grouped by type (List<List<Event>>)
                    {
                        if (eventContainer == null) continue;
                        foreach(var ev in eventContainer) // Iterate actual events in the group
                        {
                             if (ev == null) continue;
                             // Check if event has code actions (GMS1 stored code *in* actions)
                             bool hasCodeAction = ev.Actions?.Any(a => a.LibID == 1 && a.Kind == 7 && a.Function?.Name?.Content == "action_execute_script") ?? false;
                             if (!hasCodeAction) continue; // Skip events without code actions

                             UndertaleCode associatedCode = FindCodeForEvent(obj, ev.EventType, ev.EventSubtype);
                             string gmlCode = "// Event code not found or decompiled\n";
                              string gmlCompatibilityIssues = ""; // Track potential issues

                             if (associatedCode != null && associatedCode.Decompiled != null) {
                                  gmlCode = associatedCode.Decompiled.ToString(Data, true); // true = resolve names

                                   // Perform VERY basic GML compatibility checks/warnings here
                                   if (gmlCode.Contains("argument[")) gmlCompatibilityIssues += "// WARNING: Uses deprecated 'argument[n]' syntax. Use 'argumentn'.\n";
                                   if (gmlCode.Contains("self.")) gmlCompatibilityIssues += "// WARNING: Uses 'self.'. This is often redundant in GMS2.\n";
                                   if (Regex.IsMatch(gmlCode, @"\b(background_add|background_replace)\b")) gmlCompatibilityIssues += "// WARNING: Uses deprecated background functions. Use asset layers or surfaces.\n";
                                   if (Regex.IsMatch(gmlCode, @"\b(d3d_start|d3d_end|d3d_set_hidden)\b")) gmlCompatibilityIssues += "// WARNING: Uses deprecated d3d_* functions. Use GPU or shader functions.\n";
                                    // Add more checks as needed...

                                   if (!string.IsNullOrEmpty(gmlCompatibilityIssues)) {
                                        gmlCode = gmlCompatibilityIssues + gmlCode;
                                   }

                             } else if (WARN_ON_MISSING_DECOMPILED_CODE) {
                                  UMT.Log($"Warning: Decompiled code not found for Object '{objName}' Event: Type={ev.EventType}, Subtype={ev.EventSubtype}");
                             }


                             // Map GMS1 Event Type/Subtype to GMS2 Event Type/Number
                              GMS2EventMapping mapping = MapGMS1EventToGMS2(ev.EventType, ev.EventSubtype, objName);
                              if (mapping == null) {
                                   UMT.Log($"Warning: Skipping unmappable event Type={ev.EventType}, Subtype={ev.EventSubtype} for Object '{objName}'.");
                                   continue;
                              }

                             // Write GML code to file named according to GMS2 conventions
                             string gmlFileName = $"{mapping.GMS2EventTypeName}_{mapping.GMS2EventNumber}.gml";
                              if (mapping.IsCollisionEvent) gmlFileName = $"Collision_{mapping.GMS2EventTypeName}.gml"; // Collision events use object name
                             string gmlFilePath = Path.Combine(objPath, SanitizeFileName(gmlFileName));
                             File.WriteAllText(gmlFilePath, gmlCode);

                             // Create event entry for .yy file
                             var eventEntry = new JObject(
                                 new JProperty("collisionObjectId", mapping.CollisionObjectRef), // Null if not collision
                                 new JProperty("eventNum", mapping.GMS2EventNumber),
                                 new JProperty("eventType", mapping.GMS2EventType),
                                 new JProperty("isDnD", false),
                                 new JProperty("resourceVersion", "1.0"),
                                 new JProperty("name", ""), // Often empty
                                 new JProperty("tags", new JArray()),
                                 new JProperty("resourceType", "GMEvent")
                             );
                             eventList.Add(eventEntry);
                        } // End foreach ev in eventContainer
                    } // End foreach eventContainer
                } // End if obj.Events != null


                 // Get Sprite, Parent, Mask References
                 JObject spriteRef = CreateResourceReference(obj.Sprite, "sprites");
                 JObject parentRef = CreateResourceReference(obj.ParentId, "objects");
                 JObject maskRef = CreateResourceReference(obj.MaskSprite, "sprites");

                 // GMS2 Physics Properties (Defaults - UT likely didn't use GMS physics)
                 bool physicsObject = false; // Assume false unless UT data suggests otherwise (rare)
                 // ... (other physics properties defaulted) ...

                // Object Properties (GMS2 Object Variables) - GMS1 doesn't have these directly.
                // Could potentially parse Create event code for `var foo = ...`, but very complex/unreliable. Leave empty.
                 var properties = new JArray();


                 // Create Object .yy file content (JSON)
                 var yyContent = new JObject(
                     new JProperty("spriteId", spriteRef),
                     new JProperty("solid", obj.Solid),
                     new JProperty("visible", obj.Visible),
                     new JProperty("managed", true), // GMS2.3+ managed object flag
                     new JProperty("persistent", obj.Persistent),
                     new JProperty("parentObjectId", parentRef),
                     new JProperty("maskSpriteId", maskRef),
                     new JProperty("physicsObject", physicsObject),
                     new JProperty("physicsSensor", false), // Default
                     new JProperty("physicsShape", 1), // 1 = Box (Default)
                     new JProperty("physicsGroup", 0), // Default
                     new JProperty("physicsDensity", 0.5f), // Default
                     new JProperty("physicsRestitution", 0.1f), // Default
                     new JProperty("physicsLinearDamping", 0.1f), // Default
                     new JProperty("physicsAngularDamping", 0.1f), // Default
                     new JProperty("physicsFriction", 0.2f), // Default
                     new JProperty("physicsStartAwake", true), // Default
                     new JProperty("physicsKinematic", false), // Default
                     new JProperty("physicsShapePoints", new JArray()), // Empty for non-polygon shapes
                     new JProperty("eventList", new JArray(eventList)),
                     new JProperty("properties", properties), // Object variables
                     new JProperty("overriddenProperties", new JArray()),
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Objects"),
                         new JProperty("path", "folders/Objects.yy")
                     )),
                      new JProperty("resourceVersion", "1.0"), // GMObject version
                      new JProperty("name", objName),
                      new JProperty("tags", new JArray()),
                      new JProperty("resourceType", "GMObject")
                 );


                WriteJsonFile(yyPath, yyContent);

                 string relativePath = $"objects/{objName}/{objName}.yy";
                 AddResourceToProject(objName, objGuid, relativePath, "GMObject", "objects");
                  ResourcePaths[resourceKey] = relativePath;
                  objCount++;

            }
            catch (Exception ex)
            {
                 UMT.Log($"ERROR processing object '{objName}': {ex.Message}\n{ex.StackTrace}");
            }
        }
         UMT.Log($"Object conversion finished. Processed {objCount} objects.");
    }

      // Helper to find decompiled code for an event. Relies on UMT linking Code->Parent or specific naming.
     private UndertaleCode FindCodeForEvent(UndertaleObject obj, UndertaleInstruction.EventType eventType, int eventSubtype) {
         if (Data.Code == null) return null;
         string objName = GetResourceName(obj); // Use the sanitized name

         // Try finding code linked via UndertaleCode properties (if UMT populates them)
         // This structure might vary wildly between UMT versions or forks.
         // Example hypothetical check:
         /*
         var foundCode = Data.Code.FirstOrDefault(code =>
             code.ParentEntry == obj && // Or code.ParentId points to obj
             code.AssociatedEventType == eventType && // Hypothetical property
             code.AssociatedEventSubtype == eventSubtype // Hypothetical property
         );
         if (foundCode != null) return foundCode;
         */

         // Try finding code by expected name (less reliable, depends on decompiler naming)
         // Example: "objName_Create_0", "objName_Collision_objOtherName", "objName_Step_1"
         // This requires knowing the exact naming convention used by the decompiler.
         // Skipping this unreliable method for now.

          // Best bet: Iterate the object's events again and find the specific event object,
          // then check if that event object *itself* has a direct link to its code object.
          if (obj.Events != null) {
               foreach (var eventList in obj.Events) {
                   foreach (var ev in eventList) {
                        if (ev.EventType == eventType && ev.EventSubtype == eventSubtype) {
                             // Does 'ev' (UndertaleEvent type) have a 'Code' or 'CodeId' property linking to UndertaleCode?
                             // Example hypothetical check:
                              // if (ev.CodeReference != null) return ev.CodeReference;
                              // if (ev.CodeId != uint.MaxValue) return Data.Code.FirstOrDefault(c => c.Offset == ev.CodeId); // Or find by ID/Offset

                             // GMS1 stored code in "Execute Code" actions within the event.
                             // UMT might link the main UndertaleCode entry back based on this action.
                             var codeAction = ev.Actions?.FirstOrDefault(a => a.LibID == 1 && a.Kind == 7 && a.Function?.Name?.Content == "action_execute_script");
                             if (codeAction != null && codeAction.Arguments.Count > 0) {
                                 // Argument 0 usually holds the Code resource reference.
                                 var codeArg = codeAction.Arguments[0];
                                  // Check if codeArg.Code is the linked UndertaleCode object (depends on UMT version)
                                  // if (codeArg.Code != null) return codeArg.Code;

                                  // Or, if it stores an ID/Offset:
                                  // uint codeId = ParseCodeIdFromArgument(codeArg); // Need specific parsing logic
                                  // if (codeId != uint.MaxValue) return Data.Code.FirstOrDefault(c => c.Offset == codeId);

                                  // UMT might simply link the primary UndertaleCode entry for the event if only one code action exists.
                                  // Find *any* code entry linked to this object/event combo.
                                  // Fallback search (less precise):
                                  return Data.Code.FirstOrDefault(c => c?.Name?.Content?.StartsWith($"{objName}_{eventType}_{eventSubtype}") ?? false); // Name convention guess
                            }
                            // If no code action, or structure differs, we can't easily find the code this way.
                            return null; // Could not find code via action analysis for this event instance
                       }
                   }
               }
          }

         return null; // Code not found
     }

     private class GMS2EventMapping {
         public int GMS2EventType { get; set; }
         public int GMS2EventNumber { get; set; }
         public string GMS2EventTypeName { get; set; } // For filename (e.g., "Create", "Step", "obj_Enemy")
         public JObject CollisionObjectRef { get; set; } = null;
         public bool IsCollisionEvent => CollisionObjectRef != null;
     }

     private GMS2EventMapping MapGMS1EventToGMS2(UndertaleInstruction.EventType eventType, int eventSubtype, string currentObjName) {
         // GMS2 Event Types documented earlier
         switch (eventType) {
             case UndertaleInstruction.EventType.Create:
                 return new GMS2EventMapping { GMS2EventType = 0, GMS2EventNumber = 0, GMS2EventTypeName = "Create" };
             case UndertaleInstruction.EventType.Destroy:
                  return new GMS2EventMapping { GMS2EventType = 1, GMS2EventNumber = 0, GMS2EventTypeName = "Destroy" };
             case UndertaleInstruction.EventType.Alarm:
                  if (eventSubtype >= 0 && eventSubtype <= 11)
                       return new GMS2EventMapping { GMS2EventType = 2, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"Alarm{eventSubtype}" };
                  break;
             case UndertaleInstruction.EventType.Step:
                  if (eventSubtype >= 0 && eventSubtype <= 2) {
                      string stepTypeName = eventSubtype == 0 ? "Step" : (eventSubtype == 1 ? "BeginStep" : "EndStep");
                      return new GMS2EventMapping { GMS2EventType = 3, GMS2EventNumber = eventSubtype, GMS2EventTypeName = stepTypeName };
                  }
                  break;
             case UndertaleInstruction.EventType.Collision:
                  if (eventSubtype >= 0 && eventSubtype < Data.Objects.Count) {
                       var collisionObj = Data.Objects[eventSubtype];
                       if(collisionObj != null) {
                            string colObjName = GetResourceName(collisionObj);
                            // Use CreateResourceReference helper which handles GUID generation/lookup
                            JObject colRef = CreateResourceReference(collisionObj, "objects");
                             if (colRef != null) {
                                return new GMS2EventMapping {
                                    GMS2EventType = 4,
                                    GMS2EventNumber = eventSubtype, // GMS2 still uses index here, but IDE shows name
                                    GMS2EventTypeName = colObjName, // Use for filename
                                    CollisionObjectRef = colRef
                                };
                             } else { UMT.Log($"Warning: Could not create reference for collision object '{colObjName}' (Index: {eventSubtype}) in object '{currentObjName}'. Skipping event."); }
                       } else { UMT.Log($"Warning: Collision event in '{currentObjName}' references invalid object index {eventSubtype}. Skipping event."); }
                  }
                  break;
             case UndertaleInstruction.EventType.Keyboard: // Continuous press
                 return new GMS2EventMapping { GMS2EventType = 5, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"Keyboard_{VirtualKeyToString(eventSubtype)}" }; // Use VK name if possible
             case UndertaleInstruction.EventType.Mouse:
                 // Direct mapping for basic events 0-11 seems plausible
                 if (eventSubtype >= 0 && eventSubtype <= 11) {
                     string[] gms2MouseEventNames = { "LeftButton", "RightButton", "MiddleButton", "NoButton", "LeftPressed", "RightPressed", "MiddlePressed", "LeftReleased", "RightReleased", "MiddleReleased", "MouseEnter", "MouseLeave", /* WheelUp=30, WheelDown=31 */};
                     string mouseEventName = eventSubtype < gms2MouseEventNames.Length ? gms2MouseEventNames[eventSubtype] : $"UnknownMouse{eventSubtype}";
                     return new GMS2EventMapping { GMS2EventType = 6, GMS2EventNumber = eventSubtype, GMS2EventTypeName = mouseEventName };
                 }
                  // Add mapping for GMS1 wheel events if identifiable (e.g., specific subtypes?) to GMS2 numbers 30/31
                  UMT.Log($"Warning: Unhandled GMS1 Mouse event subtype {eventSubtype} in object '{currentObjName}'. Mapping might be incorrect.");
                 break; // Fall through or return basic mapping? Return basic for now.
                  return new GMS2EventMapping { GMS2EventType = 6, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"MouseRaw_{eventSubtype}" };
             case UndertaleInstruction.EventType.Other:
                 // Standard GMS1 subtypes 0-9 map directly to GMS2 numbers 0-9
                 if (eventSubtype >= 0 && eventSubtype <= 9) {
                     string[] otherEventNames = { "OutsideRoom", "IntersectBoundary", "GameStart", "GameEnd", "RoomStart", "RoomEnd", "AnimationEnd", "EndOfPath", "NoMoreLives", "NoMoreHealth"}; // GMS1 order might differ slightly? Check UT docs/GM8 docs. Let's assume mapping. GMS2: 6=NoMoreLives, 9=NoMoreHealth. GMS1 order was: 0:Outside, 1:Boundary, 2:Start Game, 3:End Game, 4:Start Room, 5:End Room, 6:No More Lives, 7:Animation End, 8:End of Path, 9:No More Health, 10..25:User Defined 0..15
                     // GMS2 mapping seems direct for 0-9 based on common usage.
                     if (eventSubtype < otherEventNames.Length) {
                          return new GMS2EventMapping { GMS2EventType = 7, GMS2EventNumber = eventSubtype, GMS2EventTypeName = otherEventNames[eventSubtype] };
                     }
                 }
                  // User Events 10-25 (GMS1 User 0-15) map directly to GMS2 numbers 10-25
                 else if (eventSubtype >= 10 && eventSubtype <= 25) {
                      int userEventIndex = eventSubtype - 10;
                      return new GMS2EventMapping { GMS2EventType = 7, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"UserEvent{userEventIndex}" };
                 }
                  // GMS2 Async events (60+) and others don't exist in GMS1 this way.
                  UMT.Log($"Warning: Unhandled GMS1 Other event subtype {eventSubtype} in object '{currentObjName}'. Skipping.");
                 break;
             case UndertaleInstruction.EventType.Draw:
                 // GMS1 Draw(0) -> GMS2 Draw(0)
                 if (eventSubtype == 0) return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 0, GMS2EventTypeName = "Draw" };
                 // GMS1 Draw GUI (subtype 1?) -> GMS2 Draw GUI (number 73, or 1 in older GMS2?) -> Use 73 for Draw GUI Begin, 74 for End? GMS2 Draw GUI is event number 1.
                 else if (eventSubtype == 1) return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 1, GMS2EventTypeName = "DrawGUI" }; // Direct map to Draw GUI
                 // Map others if GMS1 subtypes were used: 64:Begin, 65:End, 72:Resize, 73:GUI Begin, 74:GUI End, 75:Pre, 76:Post
                 else if (eventSubtype == 75) return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 75, GMS2EventTypeName = "PreDraw" };
                 else if (eventSubtype == 76) return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 76, GMS2EventTypeName = "PostDraw" };
                 // Other Draw subtypes are less common from GMS1. Default to Draw(0) if unknown subtype.
                  UMT.Log($"Warning: Unhandled GMS1 Draw event subtype {eventSubtype} in object '{currentObjName}'. Defaulting to Draw(0).");
                  return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 0, GMS2EventTypeName = "Draw" };
             case UndertaleInstruction.EventType.KeyPress: // Single press start
                   return new GMS2EventMapping { GMS2EventType = 9, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"KeyPress_{VirtualKeyToString(eventSubtype)}" };
             case UndertaleInstruction.EventType.KeyRelease: // Single press end
                   return new GMS2EventMapping { GMS2EventType = 10, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"KeyRelease_{VirtualKeyToString(eventSubtype)}" };
              case UndertaleInstruction.EventType.Trigger: // GMS1 Triggers - No clear GMS2 mapping. Skip.
                   UMT.Log($"Warning: Skipping GMS1 Trigger event {eventSubtype} in object '{currentObjName}'.");
                  break;
             default:
                  UMT.Log($"Warning: Unknown GMS1 EventType {eventType} encountered in object '{currentObjName}'. Skipping.");
                  break;
         }
         return null; // Unmappable
     }

      // Helper to get a string representation of VK codes (optional, for filenames)
      private string VirtualKeyToString(int vkCode) {
          // Simple mapping for common keys, otherwise use number
           try {
                System.Windows.Forms.Keys key = (System.Windows.Forms.Keys)vkCode;
                // Check if it's a standard printable key or a named key
                if (Enum.IsDefined(typeof(System.Windows.Forms.Keys), key) && !key.ToString().StartsWith("Oem")) {
                     return key.ToString();
                }
           } catch {} // Ignore invalid casts
           return vkCode.ToString(); // Fallback to number
      }


    private void ConvertRooms()
    {
        UMT.Log("Converting Rooms...");
        if (Data.Rooms == null || !Data.Rooms.Any()) {
            UMT.Log("No rooms found to convert.");
            return;
        }

        string roomDir = Path.Combine(OutputPath, "rooms");
        int roomCount = 0;

        foreach (var room in Data.Rooms)
        {
            if (room?.Name?.Content == null) continue;
            string roomName = GetResourceName(room);
             string resourceKey = $"rooms/{roomName}";
             Guid roomGuid = GetResourceGuid(resourceKey);
            string roomPath = Path.Combine(roomDir, roomName);
            string yyPath = Path.Combine(roomPath, $"{roomName}.yy");


            try
            {
                Directory.CreateDirectory(roomPath);
                CreatedDirectories.Add(roomPath);

                // --- Room Settings ---
                 var roomSettings = new JObject(
                     new JProperty("inheritRoomSettings", false),
                     new JProperty("Width", room.Width),
                     new JProperty("Height", room.Height),
                     new JProperty("persistent", room.Persistent)
                 );
                 var settingsWrapper = new JObject(
                      new JProperty("isDnD", false), new JProperty("volume", 1.0),
                      new JProperty("parentRoom", null), new JProperty("sequenceId", null),
                      new JProperty("roomSettings", roomSettings),
                      new JProperty("resourceVersion", "1.0"), new JProperty("name", "settings"), // Node name
                      new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRoomSettings")
                 );

                // --- Views ---
                 List<JObject> viewList = new List<JObject>();
                 bool enableViews = room.ViewsEnabled && room.Views != null && room.Views.Any(v => v.Enabled);
                 if (enableViews) {
                      for(int i=0; i < room.Views.Count; i++) {
                          var view = room.Views[i];
                          // GMS2 requires 8 views defined even if unused. Create default if enabled.
                           JObject followRef = null;
                           if(view.Enabled && view.ObjectId >= 0 && view.ObjectId < Data.Objects.Count) {
                                var followObj = Data.Objects[view.ObjectId];
                                if (followObj != null) followRef = CreateResourceReference(followObj, "objects");
                           }

                           viewList.Add(new JObject(
                               new JProperty("inherit", false),
                               new JProperty("visible", view.Enabled),
                               new JProperty("xview", view.ViewX), new JProperty("yview", view.ViewY),
                               new JProperty("wview", view.ViewWidth), new JProperty("hview", view.ViewHeight),
                               new JProperty("xport", view.PortX), new JProperty("yport", view.PortY),
                               new JProperty("wport", view.PortWidth), new JProperty("hport", view.PortHeight),
                               new JProperty("hborder", view.BorderX), new JProperty("vborder", view.BorderY),
                               new JProperty("hspeed", view.SpeedX), new JProperty("vspeed", view.SpeedY),
                               new JProperty("objectId", followRef),
                               new JProperty("resourceVersion", "1.0"), new JProperty("name", $"view_{i}"),
                               new JProperty("tags", new JArray()), new JProperty("resourceType", "GMView")
                           ));
                           // Ensure 8 views are defined if views are enabled
                           if (viewList.Count == 8) break;
                      }
                       // Add dummy views if fewer than 8 were defined but views are enabled
                       while (viewList.Count < 8) {
                            int i = viewList.Count;
                            viewList.Add(new JObject(
                                new JProperty("inherit", false), new JProperty("visible", false),
                                new JProperty("xview", 0), new JProperty("yview", 0),
                                new JProperty("wview", room.Width), new JProperty("hview", room.Height), // Default to room size
                                new JProperty("xport", 0), new JProperty("yport", 0),
                                new JProperty("wport", room.Width), new JProperty("hport", room.Height),
                                new JProperty("hborder", 32), new JProperty("vborder", 32),
                                new JProperty("hspeed", -1), new JProperty("vspeed", -1),
                                new JProperty("objectId", null),
                                new JProperty("resourceVersion", "1.0"), new JProperty("name", $"view_{i}"),
                                new JProperty("tags", new JArray()), new JProperty("resourceType", "GMView")
                            ));
                       }
                 }

                 var viewSettings = new JObject(
                     new JProperty("inheritViewSettings", false),
                     new JProperty("enableViews", enableViews),
                     new JProperty("clearViewBackground", room.ClearDisplayBuffer),
                     new JProperty("clearDisplayBuffer", room.ClearScreen), // GMS1 ClearScreen -> GMS2 ClearDisplayBuffer
                     new JProperty("views", new JArray(viewList))
                 );
                 var viewsWrapper = new JObject(
                      new JProperty("viewSettings", viewSettings),
                      new JProperty("resourceVersion", "1.0"), new JProperty("name", "views"), // Node name
                      new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRoomViewSettings")
                 );


                // --- Layers (Backgrounds, Instances, Tiles) ---
                List<JObject> layerList = new List<JObject>();
                 int currentDepth = 1000000; // Start high and decrease to mimic GMS1 depth order roughly

                 // Create Default Background Layer (for room background color)
                 layerList.Add(new JObject(
                      new JProperty("visible", true), new JProperty("depth", currentDepth), // Furthest back
                      new JProperty("userdefined_depth", false), new JProperty("inheritLayerDepth", false),
                      new JProperty("inheritLayerSettings", false), new JProperty("gridX", 32), new JProperty("gridY", 32),
                      new JProperty("layers", new JArray()), new JProperty("hierarchyFrozen", false),
                      new JProperty("effectEnabled", false), new JProperty("effectType", null),
                      new JProperty("properties", new JArray()), new JProperty("isLocked", false),
                      new JProperty("colour", ColorToGMS2JObject(room.BackgroundColor, true)), // Use room bg color
                      new JProperty("spriteId", null), new JProperty("htiled", false), new JProperty("vtiled", false),
                      new JProperty("hspeed", 0.0f), new JProperty("vspeed", 0.0f),
                      new JProperty("stretch", false), new JProperty("animationFPS", 15.0f),
                      new JProperty("animationSpeedType", 0), new JProperty("resourceVersion", "1.0"),
                      new JProperty("name", "Background"), // Standard GMS2 layer name
                      new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRBackgroundLayer")
                 ));
                 currentDepth -= 100;


                 // Convert GMS1 Background Layers to GMS2 Asset Layers
                 if (room.Backgrounds != null) {
                     // Sort by GMS1 depth (higher = further back)
                     var sortedBackgrounds = room.Backgrounds.Where(bg => bg.Enabled).OrderByDescending(bg => bg.Depth).ToList();

                     for (int i = 0; i < sortedBackgrounds.Count; i++) {
                         var bg = sortedBackgrounds[i];
                         JObject assetSpriteRef = null;
                         string assetLayerName = $"asset_layer_{i}";
                         UndertaleBackground bgResource = null;

                         if (bg.BackgroundId >= 0 && bg.BackgroundId < Data.Backgrounds.Count) {
                              bgResource = Data.Backgrounds[bg.BackgroundId];
                              if (bgResource != null) {
                                  assetSpriteRef = CreateResourceReference(bgResource, "sprites"); // Reference the pseudo-sprite
                                  assetLayerName = GetResourceName(bgResource);
                              }
                         }

                         if (assetSpriteRef == null) {
                              UMT.Log($"Warning: Could not find sprite for background layer (ID: {bg.BackgroundId}) in room '{roomName}'. Skipping layer.");
                              continue;
                         }

                         // Create Asset Layer
                         var assetLayer = new JObject(
                             new JProperty("spriteId", assetSpriteRef), // The asset (sprite) to draw
                             new JProperty("headPosition", 0.0f), // Animation frame?
                             new JProperty("inheritLayerSettings", false),
                             new JProperty("interpolation", 1), // Linear? Check GMS docs
                             new JProperty("isLocked", false),
                             new JProperty("layers", new JArray()), // Sub-layers
                             new JProperty("name", SanitizeFileName($"{assetLayerName}_AssetLayer_{Guid.NewGuid().ToString("N").Substring(0,4)}")),
                             new JProperty("properties", new JArray()),
                             new JProperty("resourceType", "GMAssetLayer"), // Type is GMAssetLayer
                             new JProperty("resourceVersion", "1.0"),
                             new JProperty("rotation", 0.0f),
                             new JProperty("scaleX", 1.0f),
                             new JProperty("scaleY", 1.0f),
                             new JProperty("sequenceId", null), // Not linked to sequence here
                             new JProperty("skewX", 0.0f),
                             new JProperty("skewY", 0.0f),
                             new JProperty("tags", new JArray()),
                             new JProperty("tint", 0xFFFFFFFF), // White tint (ABGR uint)
                             new JProperty("visible", true),
                             new JProperty("x", (float)bg.X), // Position in room
                             new JProperty("y", (float)bg.Y),
                              // Common Layer Properties
                             new JProperty("depth", bg.Depth), // Try using original depth
                             new JProperty("userdefined_depth", true),
                             new JProperty("gridX", 32), new JProperty("gridY", 32),
                             new JProperty("hierarchyFrozen", false),
                             new JProperty("effectEnabled", false), new JProperty("effectType", null)
                         );
                         layerList.Add(assetLayer);
                          // currentDepth -= 10; // Or use original depth
                     }
                 }

                  // Create Default Instance Layer (required by GMS2)
                  List<JObject> instanceRefs = new List<JObject>();
                  if (room.Instances != null) {
                       // Sort instances by depth? GMS2 sorts within layer. Maybe use original ID order?
                        var sortedInstances = room.Instances.OrderBy(inst => inst.InstanceID); // Or by Depth?

                       foreach(var inst in sortedInstances) {
                            if (inst.ObjectDefinition == null) continue;
                            JObject objRef = CreateResourceReference(inst.ObjectDefinition, "objects");
                            if (objRef == null) {
                                  UMT.Log($"Warning: Could not find object '{GetResourceName(inst.ObjectDefinition)}' for instance ID {inst.InstanceID} in room '{roomName}'. Skipping instance.");
                                 continue;
                            }

                             // Instance Creation Code (handle carefully)
                             string creationCode = ExtractCreationCode(inst, roomName);
                             string creationCodeFile = "";
                             if (!string.IsNullOrWhiteSpace(creationCode) && creationCode != "// Creation code not found/decompiled.") {
                                  // GMS2 prefers creation code in separate files for instances.
                                  string codeFileName = $"inst_{inst.InstanceID}_creationcode.gml";
                                  string codeFilePath = Path.Combine(roomPath, codeFileName);
                                  try {
                                       File.WriteAllText(codeFilePath, creationCode);
                                       creationCodeFile = codeFileName; // Store filename reference
                                       creationCode = ""; // Clear inline code if file used
                                  } catch (Exception ex) {
                                       UMT.Log($"ERROR writing instance creation code file {codeFileName}: {ex.Message}");
                                       // Fallback to inline code? GMS2 might not like long inline code.
                                  }
                             }


                            instanceRefs.Add(new JObject(
                                new JProperty("properties", new JArray()), // Instance variable overrides (complex to parse)
                                new JProperty("isDnD", false),
                                new JProperty("objectId", objRef),
                                new JProperty("inheritCode", false), // Did instance inherit code in GMS1? Assume no.
                                new JProperty("hasCreationCode", !string.IsNullOrEmpty(creationCodeFile) || !string.IsNullOrEmpty(creationCode)),
                                new JProperty("colour", ColorToGMS2JObject(inst.Color)), // Use helper for tint
                                new JProperty("rotation", (float)inst.Rotation),
                                new JProperty("scaleX", (float)inst.ScaleX),
                                new JProperty("scaleY", (float)inst.ScaleY),
                                new JProperty("imageIndex", (int)inst.ImageIndex), // GMS1 subtype? Usually 0. Use ImageIndex if available.
                                new JProperty("imageSpeed", (float)inst.ImageSpeed), // Use ImageSpeed if available
                                new JProperty("inheritedItemId", null), // For inheritance overrides
                                new JProperty("frozen", false), // Locked in IDE?
                                new JProperty("ignore", false), // Excluded from build?
                                new JProperty("inheritItemSettings", false),
                                new JProperty("x", (float)inst.X),
                                new JProperty("y", (float)inst.Y),
                                new JProperty("resourceVersion", "1.0"),
                                // GMS2 now uses GUIDs for instance names internally in the .yy file
                                new JProperty("name", Guid.NewGuid().ToString("D")), // Unique GUID for instance node name
                                new JProperty("tags", new JArray()),
                                new JProperty("resourceType", "GMRInstance")
                                // Note: Creation Code is stored in the *Instance Layer* in GMS2.3+, not the instance itself? Check format.
                                // Let's assume it's on the instance for now based on older formats/simplicity.
                                // UPDATE: GMS2.3 format seems to put creation code file ref on the instance layer element.
                                // Need to adjust how instanceRefs are added to the layer.
                             ));
                       } // End foreach instance
                  } // End if instances exist

                   // Create the main instance layer
                   var instanceLayer = new JObject(
                       new JProperty("visible", true),
                       new JProperty("depth", 0), // Default depth for main instance layer
                       new JProperty("userdefined_depth", false),
                       new JProperty("inheritLayerDepth", false),
                       new JProperty("inheritLayerSettings", false),
                       new JProperty("gridX", 32), new JProperty("gridY", 32),
                       new JProperty("layers", new JArray()),
                       new JProperty("hierarchyFrozen", false),
                       new JProperty("effectEnabled", false), new JProperty("effectType", null),
                       new JProperty("properties", new JArray()),
                       new JProperty("isLocked", false),
                       new JProperty("instances", new JArray(instanceRefs)), // Embed instances here
                       new JProperty("resourceVersion", "1.0"),
                       new JProperty("name", "Instances"), // Standard GMS2 layer name
                       new JProperty("tags", new JArray()),
                       new JProperty("resourceType", "GMRInstanceLayer")
                   );
                   layerList.Add(instanceLayer);
                   currentDepth -= 10;


                  // Convert Tile Layers (Highly problematic - create empty layers referencing tilesets)
                  if (room.Tiles != null && room.Tiles.Any()) {
                       UMT.Log($"Warning: Converting {room.Tiles.Count} GMS1 tiles to GMS2 Tile Layers. This requires MANUAL painting in GMS2.");
                       var tilesByLayer = room.Tiles
                           .Where(t => t.BackgroundDefinition != null)
                           .GroupBy(t => new { t.Depth, t.BackgroundDefinition })
                           .OrderByDescending(g => g.Key.Depth); // GMS1 depth sort

                       foreach (var layerGroup in tilesByLayer) {
                            var depth = layerGroup.Key.Depth;
                            var bgResource = layerGroup.Key.BackgroundDefinition;
                            string tilesetName = GetResourceName(bgResource); // Assumes Tileset created with same name
                            JObject tilesetRef = CreateResourceReference(bgResource, "tilesets");

                            if (tilesetRef == null) {
                                 UMT.Log($"Warning: Could not find Tileset resource '{tilesetName}' for tile layer (Depth: {depth}) in room '{roomName}'. Skipping layer.");
                                 continue;
                            }

                            string tileLayerName = SanitizeFileName($"Tiles_{tilesetName}_{depth}");
                            UMT.Log($" -- Creating EMPTY Tile Layer '{tileLayerName}' for Tileset '{tilesetName}'. Manual painting required.");

                            layerList.Add(new JObject(
                                new JProperty("tilesetId", tilesetRef),
                                new JProperty("x", 0), new JProperty("y", 0), // Layer offset
                                new JProperty("visible", true),
                                new JProperty("depth", depth), // Use original depth
                                new JProperty("userdefined_depth", true),
                                new JProperty("inheritLayerDepth", false),
                                new JProperty("inheritLayerSettings", false),
                                new JProperty("gridX", 32), new JProperty("gridY", 32),
                                new JProperty("layers", new JArray()),
                                new JProperty("hierarchyFrozen", false),
                                new JProperty("effectEnabled", false), new JProperty("effectType", null),
                                new JProperty("properties", new JArray()),
                                new JProperty("isLocked", false),
                                // Empty tile data structure for GMS2.3+
                                new JProperty("tiles", new JObject(
                                     new JProperty("TileData", new JArray( // Array dimensions match room tile grid
                                          // Needs width * height elements, all zero initially
                                          // Calculate grid dimensions
                                          // int gridW = (room.Width + 31) / 32; // Example if grid is 32x32
                                          // int gridH = (room.Height + 31) / 32;
                                          // Enumerable.Repeat(0, gridW * gridH) // Create array of zeros
                                     )),
                                     new JProperty("SerialiseWidth", 0), // Set based on actual grid calc
                                     new JProperty("SerialiseHeight", 0),
                                     new JProperty("TileSerialiseData", null) // Older format? Ensure null if using TileData
                                )),
                                new JProperty("tile_count", 0), // GMS2 calculates this? Set 0.
                                new JProperty("resourceVersion", "1.0"),
                                new JProperty("name", tileLayerName),
                                new JProperty("tags", new JArray()),
                                new JProperty("resourceType", "GMRTileLayer")
                            ));
                            // currentDepth -= 5; // Or use original depth
                       }
                  }


                // --- Room Creation Code ---
                string creationCodeContent = ExtractRoomCreationCode(room, roomName);
                string creationCodeFilePath = Path.Combine(roomPath, "RoomCreationCode.gml");
                File.WriteAllText(creationCodeFilePath, creationCodeContent);


                // --- Main Room .yy File ---
                 var layerFolder = new JObject(
                      new JProperty("visible", true), new JProperty("depth", 0), // Folder depth doesn't really matter
                      new JProperty("userdefined_depth", false), new JProperty("inheritLayerDepth", true),
                      new JProperty("inheritLayerSettings", true), new JProperty("gridX", 32), new JProperty("gridY", 32),
                      new JProperty("layers", new JArray(layerList)), // Embed actual layers here
                      new JProperty("hierarchyFrozen", false), new JProperty("effectEnabled", false),
                      new JProperty("effectType", null), new JProperty("properties", new JArray()),
                      new JProperty("isLocked", false), new JProperty("resourceVersion", "1.0"),
                      new JProperty("name", "layers"), // Node name for layers folder
                      new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRLayerFolder") // Note type: GMRLayerFolder
                 );

                 var instanceCreationOrder = new JArray(); // GMS2.3+ defines instance nodes here for creation order
                 // Populate with instance GUIDs from the instance layer
                 foreach (JObject instNode in (JArray)instanceLayer["instances"]) {
                      instanceCreationOrder.Add(new JObject(
                           new JProperty("name", instNode["name"].Value<string>()), // Instance node GUID
                           new JProperty("path", yyPath) // Path to the room yy file itself
                      ));
                 }


                var yyContent = new JObject(
                     new JProperty("isDnD", false),
                     new JProperty("volume", 1.0f), // Room volume modifier (usually 1)
                     new JProperty("parentRoom", null),
                     new JProperty("sequenceId", null),
                     new JProperty("roomSettings", settingsWrapper), // Embed settings object
                     new JProperty("viewSettings", viewsWrapper), // Embed views object
                     new JProperty("layers", new JArray(layerFolder)), // Embed layer folder wrapper
                     new JProperty("physicsSettings", new JObject( // Default physics settings
                         new JProperty("inheritPhysicsSettings", false),
                         new JProperty("PhysicsWorld", false), new JProperty("PhysicsWorldGravityX", 0.0f),
                         new JProperty("PhysicsWorldGravityY", 10.0f), new JProperty("PhysicsWorldPixToMetres", 0.1f)
                     )),
                     new JProperty("instanceCreationCode", new JObject()), // Per-instance code stored elsewhere? Check format. Seems empty usually.
                     new JProperty("inheritCode", false),
                      // GMS2.3+: Instance creation order defined here
                      new JProperty("instanceCreationOrder", instanceCreationOrder),
                     new JProperty("inheritCreationOrder", false),
                     new JProperty("sequenceCreationOrder", new JArray()), // For sequences placed in room
                     new JProperty("useCats", false), // Physics categories?
                     new JProperty("cats", new JArray()),
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Rooms"),
                         new JProperty("path", "folders/Rooms.yy")
                     )),
                     new JProperty("creationCodeFile", Path.GetFileName(creationCodeFilePath)), // Link to room creation code file
                     new JProperty("inheritGenerateOffsetY", false),
                     new JProperty("generateOffsetY", 0),
                     new JProperty("resourceVersion", "1.0"), // GMRoom version
                     new JProperty("name", roomName),
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMRoom")
                 );


                WriteJsonFile(yyPath, yyContent);

                string relativePath = $"rooms/{roomName}/{roomName}.yy";
                AddResourceToProject(roomName, roomGuid, relativePath, "GMRoom", "rooms");
                 ResourcePaths[resourceKey] = relativePath;
                 roomCount++;

            }
            catch (Exception ex)
            {
                 UMT.Log($"ERROR processing room '{roomName}': {ex.Message}\n{ex.StackTrace}");
            }
        }
         UMT.Log($"Room conversion finished. Processed {roomCount} rooms.");
    }

     // Helper to get creation code string for an instance
     private string ExtractCreationCode(UndertaleRoom.Instance inst, string roomName) {
          string creationCode = "// Creation code not found/decompiled.";
          UndertaleCode code = null;
          if (inst.CreationCode != null) { // Direct link?
               code = Data.Code?.FirstOrDefault(c => c == inst.CreationCode);
          } else if (inst.CreationCodeId != System.UInt32.MaxValue) { // Find by ID/Offset
               code = Data.Code?.FirstOrDefault(c => c.Offset == inst.CreationCodeId); // Offset might not be reliable ID across versions
               // Add other lookup methods if UMT uses different ID schemes
          }

           if (code != null && code.Decompiled != null) {
                creationCode = code.Decompiled.ToString(Data, true);
           } else if ((inst.CreationCode != null || inst.CreationCodeId != System.UInt32.MaxValue) && WARN_ON_MISSING_DECOMPILED_CODE) {
                UMT.Log($"Warning: Could not find/decompile creation code (ID: {inst.CreationCodeId}) for instance ID {inst.InstanceID} in room '{roomName}'.");
           }
          return creationCode;
     }
     // Helper to get creation code string for a room
     private string ExtractRoomCreationCode(UndertaleRoom room, string roomName) {
          string creationCode = "// Room Creation Code not found or decompiled.\n";
           UndertaleCode code = null;
           if (room.CreationCode != null) {
                code = Data.Code?.FirstOrDefault(c => c == room.CreationCode);
           } else if (room.CreationCodeId != System.UInt32.MaxValue) {
                code = Data.Code?.FirstOrDefault(c => c.Offset == room.CreationCodeId);
           }

           if (code != null && code.Decompiled != null) {
                creationCode = code.Decompiled.ToString(Data, true);
           } else if ((room.CreationCode != null || room.CreationCodeId != System.UInt32.MaxValue) && WARN_ON_MISSING_DECOMPILED_CODE) {
                UMT.Log($"Warning: Could not find/decompile creation code for room '{roomName}'.");
           }
          return creationCode;
     }


      // Helper to convert System.UInt32 color (ABGR or BGR?) to GMS2 JSON color object {r,g,b,a}
      // GMS1 instance colors were likely BGR (0xBBGGRR), room background ABGR? Let's assume ABGR for safety.
      private JObject ColorToGMS2JObject(System.UInt32 abgrColor, bool isRoomBg = false) {
           byte a, r, g, b;
            // Try interpreting as ABGR (common for general color values)
            try {
                 // Use System.Drawing.Color to handle potential endianness/format issues if possible
                 // Treat the uint as an ARGB int (might require byte order swap depending on source)
                 // int argb = unchecked((int)abgrColor); // Direct cast might be wrong endianness
                 // Let's manually parse ABGR assuming little-endian storage like most systems
                 a = (byte)((abgrColor >> 24) & 0xFF);
                 b = (byte)((abgrColor >> 16) & 0xFF);
                 g = (byte)((abgrColor >> 8) & 0xFF);
                 r = (byte)(abgrColor & 0xFF);

                  // Room background color in GMS1 might not have used alpha, default to opaque if room bg
                  if (isRoomBg && a == 0 && (r != 0 || g != 0 || b != 0)) {
                       // If alpha is 0 but color is not black, assume it should be opaque for room bg
                       // a = 255; // Or does GMS1 room bg ignore alpha? Let's keep original alpha.
                  }
                  // GMS1 Instance tint (inst.Color) was often BGR (0xBBGGRR) with alpha ignored or assumed 255?
                  // If !isRoomBg, maybe assume alpha is 255? Difficult to know for sure.
                  // Let's stick to ABGR parsing for now, but be aware inst.Color might need special handling.

            } catch (Exception ex) {
                 UMT.Log($"Warning: Error parsing color value {abgrColor}. Using white. Error: {ex.Message}");
                 r = g = b = a = 255;
            }

           // GMS2 JSON uses RGBA 0-255 integer format
           return new JObject(
                new JProperty("r", r), new JProperty("g", g),
                new JProperty("b", b), new JProperty("a", a)
           );
      }


    private void ConvertScripts()
    {
        UMT.Log("Converting Scripts...");
        if (Data.Scripts == null || !Data.Scripts.Any()) {
             UMT.Log("No scripts found to convert.");
             return;
        }

        string scriptDir = Path.Combine(OutputPath, "scripts");
        int scriptCount = 0;

        foreach (var script in Data.Scripts)
        {
            if (script?.Name?.Content == null) continue;
            string scriptName = GetResourceName(script);
            string resourceKey = $"scripts/{scriptName}";
             Guid scriptGuid = GetResourceGuid(resourceKey);
            string scriptPath = Path.Combine(scriptDir, scriptName);
            string yyPath = Path.Combine(scriptPath, $"{scriptName}.yy");
            string gmlPath = Path.Combine(scriptPath, $"{scriptName}.gml");


            try
            {
                Directory.CreateDirectory(scriptPath);
                CreatedDirectories.Add(scriptPath);

                string gmlCode = $"// Script code for {scriptName} not found or decompiled.\nfunction {scriptName}() {{\n\tshow_debug_message(\"Script {scriptName} not converted\");\n}}";
                 string gmlCompatibilityIssues = "";
                 UndertaleCode associatedCode = FindCodeForScript(script);

                 if (associatedCode != null && associatedCode.Decompiled != null) {
                     gmlCode = associatedCode.Decompiled.ToString(Data, true);

                      // Basic GML2+ function syntax check & wrap if needed
                      string trimmedCode = gmlCode.Trim();
                      if (!trimmedCode.StartsWith("function ") && !trimmedCode.StartsWith("#define")) {
                           // Only wrap if it doesn't look like a multi-script file (#define)
                           gmlCode = $"function {scriptName}() {{\n{gmlCode}\n}}";
                            gmlCompatibilityIssues += $"// WARNING: Automatically wrapped content in 'function {scriptName}() {{...}}'. Verify arguments and structure.\n";
                      }

                      // Add compatibility warnings (similar to object events)
                      if (gmlCode.Contains("argument[")) gmlCompatibilityIssues += "// WARNING: Uses deprecated 'argument[n]' syntax. Use 'argumentn'.\n";
                      if (gmlCode.Contains("background_")) gmlCompatibilityIssues += "// WARNING: Uses deprecated background_* functions.\n";
                      // ... add more checks ...

                      if (!string.IsNullOrEmpty(gmlCompatibilityIssues)) {
                           gmlCode = gmlCompatibilityIssues + gmlCode;
                      }

                 } else if (WARN_ON_MISSING_DECOMPILED_CODE) {
                      UMT.Log($"Warning: Decompiled code not found for Script '{scriptName}'.");
                 }

                File.WriteAllText(gmlPath, gmlCode);

                var yyContent = new JObject(
                    new JProperty("isDnD", false),
                    new JProperty("isCompatibility", true), // Mark as compatibility - GMS2 mostly ignores but maybe useful indicator
                    new JProperty("parent", new JObject(
                        new JProperty("name", "Scripts"),
                        new JProperty("path", "folders/Scripts.yy")
                    )),
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", scriptName),
                     new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMScript")
                );

                WriteJsonFile(yyPath, yyContent);

                 string relativePath = $"scripts/{scriptName}/{scriptName}.yy";
                 AddResourceToProject(scriptName, scriptGuid, relativePath, "GMScript", "scripts");
                  ResourcePaths[resourceKey] = relativePath;
                  scriptCount++;

            }
            catch (Exception ex)
            {
                 UMT.Log($"ERROR processing script '{scriptName}': {ex.Message}");
            }
        }
         UMT.Log($"Script conversion finished. Processed {scriptCount} scripts.");
    }

     private UndertaleCode FindCodeForScript(UndertaleScript script) {
         if (Data.Code == null || script == null) return null;
         // Option 1: Direct reference from UndertaleScript object (if UMT adds this)
         // if (script.CodeReference != null) return script.CodeReference;

         // Option 2: Find code entry where Parent points to script (if UMT links Code->Parent)
         // foreach(var code in Data.Code) { if (code.ParentEntry == script) return code; }

         // Option 3: Find code entry by matching name (most common if decompiled)
         string scriptName = GetResourceName(script); // Use the potentially sanitized name map
          return Data.Code.FirstOrDefault(code => GetResourceName(code) == scriptName); // Assumes Code entries are also in ResourceToNameMap

         // Fallback: Try original name if sanitized name fails?
          // if (script.Name?.Content != null) {
          //     return Data.Code.FirstOrDefault(code => code.Name?.Content == script.Name.Content);
          // }
          // return null;
     }


    private void ConvertShaders()
    {
        UMT.Log("Converting Shaders...");
        if (Data.Shaders == null || !Data.Shaders.Any()) {
             UMT.Log("No shaders found to convert.");
             return;
        }

        string shaderDir = Path.Combine(OutputPath, "shaders");
        int shaderCount = 0;

        foreach (var shader in Data.Shaders)
        {
            if (shader?.Name?.Content == null) continue;
            string shaderName = GetResourceName(shader);
             string resourceKey = $"shaders/{shaderName}";
             Guid shaderGuid = GetResourceGuid(resourceKey);
            string shaderPath = Path.Combine(shaderDir, shaderName);
            string yyPath = Path.Combine(shaderPath, $"{shaderName}.yy");
            string vshPath = Path.Combine(shaderPath, $"{shaderName}.vsh");
            string fshPath = Path.Combine(shaderPath, $"{shaderName}.fsh");


            try
            {
                Directory.CreateDirectory(shaderPath);
                CreatedDirectories.Add(shaderPath);

                string vertSource = shader.VertexShader?.Content ?? "// Vertex shader source not found";
                string fragSource = shader.FragmentShader?.Content ?? "// Fragment shader source not found";

                // Basic GLSL ES -> GMS2 compatibility adjustments (very basic)
                 vertSource = vertSource.Replace("attribute", "in").Replace("varying", "out"); // GLES 2 -> GLES 3+ ? GMS2 often uses legacy still. Check target runtime.
                 fragSource = fragSource.Replace("varying", "in");
                 // texture2D -> texture etc. might be needed depending on GMS2 runtime/shader version used.

                File.WriteAllText(vshPath, vertSource);
                File.WriteAllText(fshPath, fragSource);

                 // GMS2 Shader Type: 1 = GLSL ES, 2 = HLSL 11, 3 = GLSL, 4 = PSSL
                 // GMS1/Undertale typically used GLSL ES.
                 int gms2ShaderType = 1;

                 var yyContent = new JObject(
                     new JProperty("type", gms2ShaderType),
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

                 string relativePath = $"shaders/{shaderName}/{shaderName}.yy";
                 AddResourceToProject(shaderName, shaderGuid, relativePath, "GMShader", "shaders");
                 ResourcePaths[resourceKey] = relativePath;
                 shaderCount++;
            }
            catch (Exception ex)
            {
                 UMT.Log($"ERROR processing shader '{shaderName}': {ex.Message}");
            }
        }
         UMT.Log($"Shader conversion finished. Processed {shaderCount} shaders.");
    }


    private void ConvertFonts()
    {
        UMT.Log("Converting Fonts...");
         UMT.Log("WARNING: Font conversion is highly experimental. MANUAL review and regeneration in GMS2 is STRONGLY recommended.");
        if (Data.Fonts == null || !Data.Fonts.Any()) {
            UMT.Log("No fonts found to convert.");
            return;
        }

        string fontDir = Path.Combine(OutputPath, "fonts");
        int fontCount = 0;

        foreach (var font in Data.Fonts)
        {
            if (font?.Name?.Content == null) continue;
            string fontName = GetResourceName(font);
             string resourceKey = $"fonts/{fontName}";
             Guid fontGuid = GetResourceGuid(resourceKey);
            string fontPath = Path.Combine(fontDir, fontName);
            string yyPath = Path.Combine(fontPath, $"{fontName}.yy");


            try
            {
                Directory.CreateDirectory(fontPath);
                CreatedDirectories.Add(fontPath);

                string sourceFontName = font.FontName?.Content ?? "Arial"; // Fallback font name from system
                int size = (int)Math.Round(font.Size); // GMS2 uses integer point size
                bool bold = font.Bold;
                bool italic = font.Italic;
                 // GMS1 used RangeStart/End, GMS2 uses First/Last or Charset
                 uint firstChar = font.RangeStart;
                 uint lastChar = font.RangeEnd;
                 if (lastChar < firstChar) lastChar = firstChar; // Ensure last >= first


                 // Attempt to find the sprite generated from the font's texture page (highly unreliable)
                 JObject glyphSpriteRef = null;
                 string foundSpriteName = null;
                  if (font.Texture?.TexturePage != null) {
                       // This lookup is difficult. Requires finding the pseudo-sprite generated from the *exact* TexturePageItem.
                       // Heuristic: Find sprite resource whose name matches font name?
                       var potentialSpriteRes = FindResourceByName<UndertaleSprite>(fontName, Data.Sprites) ??
                                                FindResourceByName<UndertaleBackground>(fontName, Data.Backgrounds); // Check both
                        if (potentialSpriteRes != null) {
                             glyphSpriteRef = CreateResourceReference(potentialSpriteRes, "sprites");
                             foundSpriteName = GetResourceName(potentialSpriteRes);
                             UMT.Log($"  > Tentatively linked font '{fontName}' to sprite '{foundSpriteName}'. Manual verification needed in GMS2.");
                        } else {
                             UMT.Log($"Warning: Could not find a sprite resource matching name '{fontName}' for font texture page. Font may not render correctly.");
                        }
                  } else {
                       UMT.Log($"Warning: Font '{fontName}' has no texture data. Cannot link to sprite. GMS2 will likely use system font.");
                  }


                 // Create Font .yy file content (JSON) - Minimal info, relies on GMS2 regeneration
                 var yyContent = new JObject(
                     // Basic properties GMS2 might use for regeneration
                     new JProperty("sourceFontName", sourceFontName), // Hint for GMS2
                     new JProperty("size", size),
                     new JProperty("bold", bold),
                     new JProperty("italic", italic),
                     new JProperty("antiAlias", Math.Max(1, font.AntiAlias)), // GMS2 AA usually 1 or 2. Use at least 1.
                     new JProperty("charset", 255), // 255 = Custom Range? Check GMS2 docs. Use 0 for ANSI?
                     new JProperty("first", firstChar), // Custom range start
                     new JProperty("last", lastChar), // Custom range end

                     // Fields GMS2 uses for its own generated fonts (mostly placeholders here)
                     new JProperty("characterMap", null), // Usually null unless custom mapping
                     new JProperty("glyphOperations", new JArray()),
                     new JProperty("textureGroupId", CreateResourceReference("Default", GetResourceGuid("texturegroups/Default"), "texturegroups")), // Font textures go to a group
                     new JProperty("styleName", "Regular"), // Placeholder
                     new JProperty("kerningPairs", new JArray()),
                     new JProperty("includesTTF", false),
                     new JProperty("TTFName", ""),
                     new JProperty("ascender", 0), // Let GMS2 calculate
                     new JProperty("descender", 0),
                     new JProperty("lineHeight", 0),
                     // GMS2 stores detailed glyph data here if rendered, we can't replicate GMS1 easily.
                     // If we found a sprite, GMS2 *might* use it if font type is set to "Sprite Font" manually, but that's rare.
                     new JProperty("glyphs", new JObject()), // Empty glyph data

                     // Resource boilerplate
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Fonts"),
                         new JProperty("path", "folders/Fonts.yy")
                     )),
                      new JProperty("resourceVersion", "1.0"), // GMFont version
                      new JProperty("name", fontName),
                      new JProperty("tags", new JArray()),
                      new JProperty("resourceType", "GMFont")
                 );

                WriteJsonFile(yyPath, yyContent);

                 string relativePath = $"fonts/{fontName}/{fontName}.yy";
                 AddResourceToProject(fontName, fontGuid, relativePath, "GMFont", "fonts");
                  ResourcePaths[resourceKey] = relativePath;
                  fontCount++;
            }
            catch (Exception ex)
            {
                 UMT.Log($"ERROR processing font '{fontName}': {ex.Message}");
            }
        }
         UMT.Log($"Font conversion finished. Processed {fontCount} fonts.");
    }


    private void ConvertTilesets()
    {
        UMT.Log("Converting Tilesets (from Backgrounds/Sprites used in Rooms)...");
         UMT.Log("WARNING: Tileset properties (size, separation, offset) are GUESSED. MANUAL adjustment in GMS2 is ESSENTIAL.");

        string tilesetDir = Path.Combine(OutputPath, "tilesets");
        int tilesetCount = 0;

        // Identify unique Backgrounds/Sprites used as tilesets in Rooms
        HashSet<UndertaleResource> usedTilesetSources = new HashSet<UndertaleResource>();
        if (Data.Rooms != null) {
            foreach(var room in Data.Rooms.Where(r => r?.Tiles != null)) {
                foreach(var tile in room.Tiles.Where(t => t?.BackgroundDefinition != null)) {
                     usedTilesetSources.Add(tile.BackgroundDefinition);
                }
                // Add check for sprites used as tiles if GMS1 supported that via room.Tiles
            }
        }

        if (!usedTilesetSources.Any()) {
             UMT.Log("No resources identified as being used for tilesets in rooms.");
             return;
        }
         UMT.Log($"Found {usedTilesetSources.Count} unique resources used for tilesets.");

        foreach (var tilesetSourceResource in usedTilesetSources)
        {
             if (tilesetSourceResource == null) continue;

             // Find the Sprite resource that was created for this Background/Sprite
             // Assumes a pseudo-sprite was created with the same name during ConvertSprites
             string sourceName = GetResourceName(tilesetSourceResource);
             JObject sourceSpriteRef = CreateResourceReference(tilesetSourceResource, "sprites"); // Use helper

             if (sourceSpriteRef == null) {
                  UMT.Log($"Warning: Could not find or create reference to source Sprite '{sourceName}' for Tileset generation. Skipping tileset.");
                 continue;
             }

             string tilesetName = sourceName; // Use the same name for the Tileset resource
             string resourceKey = $"tilesets/{tilesetName}";
             Guid tilesetGuid = GetResourceGuid(resourceKey);
             string tilesetPath = Path.Combine(tilesetDir, tilesetName);
             string yyPath = Path.Combine(tilesetPath, $"{tilesetName}.yy");

              UMT.Log($"Converting Tileset: {tilesetName} (Source: {sourceName})");

             try {
                 Directory.CreateDirectory(tilesetPath);
                 CreatedDirectories.Add(tilesetPath);

                 // --- Guess Tileset Properties ---
                 // These NEED manual correction in GMS2.
                 int tileWidth = 16; int tileHeight = 16; // Default guess
                 int tileSepX = 0; int tileSepY = 0;
                 int tileOffsetX = 0; int tileOffsetY = 0;
                 int spriteWidth = 0; int spriteHeight = 0;

                 // Try getting sprite dimensions to help guess tile count
                  UndertaleSprite foundSprite = null; // Find the actual sprite resource if possible
                  if (tilesetSourceResource is UndertaleSprite s) foundSprite = s;
                  else if (tilesetSourceResource is UndertaleBackground b) {
                       // Find the *pseudo-sprite* added to allSprites in ConvertSprites
                       // This lookup is hard. Let's try finding the original resource again.
                       // Need sprite width/height from the CONVERTED sprite data.
                       // Alternative: Re-read dimensions from TexturePage if possible
                       if (b.Texture?.TexturePage != null) {
                            spriteWidth = b.Texture.TexturePage.SourceWidth > 0 ? b.Texture.TexturePage.SourceWidth : b.Texture.TexturePage.TargetWidth;
                            spriteHeight = b.Texture.TexturePage.SourceHeight > 0 ? b.Texture.TexturePage.SourceHeight : b.Texture.TexturePage.TargetHeight;
                       }
                  }
                  // If we have spriteWidth/Height, refine default tile size? Very risky guess.
                  // Example: if (spriteWidth % 32 == 0) tileWidth = 32;

                  // Calculate rough tile count based on guesses
                   int columns = (spriteWidth > 0 && tileWidth > 0) ? Math.Max(1, (spriteWidth - tileOffsetX * 2 + tileSepX) / (tileWidth + tileSepX)) : 1;
                   int rows = (spriteHeight > 0 && tileHeight > 0) ? Math.Max(1, (spriteHeight - tileOffsetY * 2 + tileSepY) / (tileHeight + tileSepY)) : 1;
                   int tileCount = columns * rows;


                 // Create Tileset .yy file content (JSON)
                 var yyContent = new JObject(
                     new JProperty("spriteId", sourceSpriteRef),
                     new JProperty("tileWidth", tileWidth),
                     new JProperty("tileHeight", tileHeight),
                     new JProperty("tilexoff", tileOffsetX),
                     new JProperty("tileyoff", tileOffsetY),
                     new JProperty("tilehsep", tileSepX),
                     new JProperty("tilevsep", tileSepY),
                     new JProperty("spriteNoExport", true), // Export the sprite separately? Usually yes for tilesets. Set false? Let's set false.
                     new JProperty("spriteNoExport", false),
                     new JProperty("textureGroupId", CreateResourceReference("Default", GetResourceGuid("texturegroups/Default"), "texturegroups")),
                     new JProperty("out_tilehborder", 2), // GMS2 texture packing border
                     new JProperty("out_tilevborder", 2),
                     new JProperty("out_columns", columns), // Guessed column count
                     new JProperty("tile_count", tileCount), // Guessed total tiles
                     new JProperty("autoTileSets", new JArray()), // Not converted
                     new JProperty("tileAnimationFrames", new JArray()), // Not converted
                     new JProperty("tileAnimationSpeed", 15.0f), // Default
                     new JProperty("macroPageTiles", new JObject( // IDE Tile Palette setup
                         new JProperty("SerialiseWidth", columns),
                         new JProperty("SerialiseHeight", rows),
                         new JProperty("TileSerialiseData", new JArray()) // Empty palette data
                     )),
                     new JProperty("parent", new JObject(
                         new JProperty("name", "Tilesets"),
                         new JProperty("path", "folders/Tilesets.yy")
                     )),
                      new JProperty("resourceVersion", "1.0"), // GMTileSet version
                      new JProperty("name", tilesetName),
                      new JProperty("tags", new JArray()),
                      new JProperty("resourceType", "GMTileSet")
                 );

                 WriteJsonFile(yyPath, yyContent);

                  string relativePath = $"tilesets/{tilesetName}/{tilesetName}.yy";
                  AddResourceToProject(tilesetName, tilesetGuid, relativePath, "GMTileSet", "tilesets");
                   ResourcePaths[resourceKey] = relativePath;
                   tilesetCount++;

             } catch (Exception ex) {
                 UMT.Log($"ERROR processing tileset '{tilesetName}': {ex.Message}");
             }
        }


         UMT.Log($"Tileset conversion finished. Processed {tilesetCount} tilesets.");
    }

     private T FindResourceByName<T>(string name, IEnumerable<T> list) where T : UndertaleNamedResource {
          if (list == null || string.IsNullOrEmpty(name)) return null;
           // Use the ResourceToNameMap for consistent lookup using the sanitized name
           return list.FirstOrDefault(item => GetResourceName(item) == name);
     }


    private void ConvertPaths() {
         UMT.Log("Converting Paths...");
         if (Data.Paths == null || !Data.Paths.Any()) {
             UMT.Log("No paths found.");
             return;
         }
         string pathDir = Path.Combine(OutputPath, "paths");
         int pathCount = 0;

         foreach (var path in Data.Paths.Where(p => p?.Name?.Content != null)) {
              string pathName = GetResourceName(path);
               string resourceKey = $"paths/{pathName}";
               Guid pathGuid = GetResourceGuid(resourceKey);
              string pathResPath = Path.Combine(pathDir, pathName);
              string yyPath = Path.Combine(pathResPath, $"{pathName}.yy");

               try {
                    Directory.CreateDirectory(pathResPath);
                    CreatedDirectories.Add(pathResPath);

                    List<JObject> points = path.Points?
                        .Select(pt => new JObject(new JProperty("speed", (float)pt.Speed), new JProperty("x", (float)pt.X), new JProperty("y", (float)pt.Y)))
                        .ToList() ?? new List<JObject>();

                    var yyContent = new JObject(
                        new JProperty("kind", path.Smooth ? 1 : 0), // 0 = Straight, 1 = Smooth
                        new JProperty("closed", path.Closed),
                        new JProperty("precision", (int)path.Precision),
                        new JProperty("points", new JArray(points)),
                        new JProperty("parent", new JObject(new JProperty("name", "Paths"), new JProperty("path", "folders/Paths.yy"))),
                        new JProperty("resourceVersion", "1.0"), new JProperty("name", pathName),
                        new JProperty("tags", new JArray()), new JProperty("resourceType", "GMPath")
                    );
                    WriteJsonFile(yyPath, yyContent);
                    string relativePath = $"paths/{pathName}/{pathName}.yy";
                    AddResourceToProject(pathName, pathGuid, relativePath, "GMPath", "paths");
                     ResourcePaths[resourceKey] = relativePath;
                     pathCount++;
               } catch (Exception ex) {
                    UMT.Log($"ERROR processing path '{pathName}': {ex.Message}");
               }
         }
          UMT.Log($"Path conversion finished. Processed {pathCount} paths.");
    }

    private void ConvertTimelines() {
         UMT.Log("Converting Timelines...");
          UMT.Log("WARNING: Timeline code conversion relies on finding associated scripts. Manual review needed.");
         if (Data.Timelines == null || !Data.Timelines.Any()) {
             UMT.Log("No timelines found.");
             return;
         }
         string timelineDir = Path.Combine(OutputPath, "timelines");
         int tlCount = 0;

         foreach (var timeline in Data.Timelines.Where(tl => tl?.Name?.Content != null)) {
             string tlName = GetResourceName(timeline);
             string resourceKey = $"timelines/{tlName}";
              Guid tlGuid = GetResourceGuid(resourceKey);
             string tlPath = Path.Combine(timelineDir, tlName);
             string yyPath = Path.Combine(tlPath, $"{tlName}.yy");

              try {
                   Directory.CreateDirectory(tlPath);
                   CreatedDirectories.Add(tlPath);

                   List<JObject> moments = new List<JObject>();
                    if (timeline.Moments != null) {
                         foreach(var moment in timeline.Moments) {
                              string gmlCode = $"// Code for timeline {tlName} moment {moment.Moment} not found/decompiled.";
                              UndertaleCode momentCode = FindCodeForTimelineMoment(timeline, moment);
                               if (momentCode != null && momentCode.Decompiled != null) {
                                   gmlCode = momentCode.Decompiled.ToString(Data, true);
                                   // Add GML compatibility warnings if needed
                               } else if (WARN_ON_MISSING_DECOMPILED_CODE && (moment.Actions?.Any() ?? false)) {
                                    UMT.Log($"Warning: Decompiled code not found for Timeline '{tlName}' Moment: {moment.Moment}");
                               }

                               // GMS2 stores moment code differently (often via embedded event editor or script ref).
                               // Simplest export: Save code to file, reference might need manual setup in GMS2.
                               // Or, embed code directly in the moment's event object (less ideal). Let's try embedding.

                               var momentEvent = new JObject(
                                    new JProperty("collisionObjectId", null), new JProperty("eventNum", 0),
                                    new JProperty("eventType", 7), // Other Event
                                    new JProperty("isDnD", false),
                                     // How to store code? GMS2 might have specific structure. Let's put it raw for now.
                                     // new JProperty("script", gmlCode), // Unofficial, likely ignored
                                    new JProperty("resourceVersion", "1.0"), new JProperty("name", ""),
                                    new JProperty("tags", new JArray()), new JProperty("resourceType", "GMEvent")
                               );
                               // NOTE: GMS2 likely requires manually creating a script and calling it from the timeline moment event.
                               // This conversion doesn't automate that link.

                               moments.Add(new JObject(
                                   new JProperty("moment", moment.Moment),
                                   new JProperty("evnt", momentEvent), // Embed event object
                                    new JProperty("resourceVersion", "1.0"), new JProperty("name", ""),
                                    new JProperty("tags", new JArray()), new JProperty("resourceType", "GMMoment")
                               ));
                         }
                    }

                   var yyContent = new JObject(
                        new JProperty("momentList", new JArray(moments)),
                        new JProperty("parent", new JObject(new JProperty("name", "Timelines"), new JProperty("path", "folders/Timelines.yy"))),
                        new JProperty("resourceVersion", "1.0"), new JProperty("name", tlName),
                        new JProperty("tags", new JArray()), new JProperty("resourceType", "GMTimeline")
                    );

                   WriteJsonFile(yyPath, yyContent);
                    string relativePath = $"timelines/{tlName}/{tlName}.yy";
                    AddResourceToProject(tlName, tlGuid, relativePath, "GMTimeline", "timelines");
                     ResourcePaths[resourceKey] = relativePath;
                     tlCount++;
              } catch (Exception ex) {
                   UMT.Log($"ERROR processing timeline '{tlName}': {ex.Message}");
              }
         }
          UMT.Log($"Timeline conversion finished. Processed {tlCount} timelines.");
    }

      // Helper to find code for timeline moments (relies on UMT linking or naming)
     private UndertaleCode FindCodeForTimelineMoment(UndertaleTimeline timeline, UndertaleTimelineMoment moment) {
         if (Data.Code == null || timeline == null || moment == null) return null;

         // Check actions for code reference (most likely GMS1 method)
          if (moment.Actions != null) {
               var codeAction = moment.Actions.FirstOrDefault(a => a.LibID == 1 && a.Kind == 7 && a.Function?.Name?.Content == "action_execute_script");
                if (codeAction != null && codeAction.Arguments.Count > 0) {
                     var codeArg = codeAction.Arguments[0];
                     // Try to resolve codeArg to UndertaleCode (implementation depends on UMT internals)
                     // Example: if (codeArg.Code != null) return codeArg.Code;
                     // Example: uint codeId = ParseCodeIdFromArgument(codeArg); return Data.Code.FirstOrDefault(c => c.Offset == codeId);
                     return null; // Placeholder: Requires UMT-specific logic to resolve code from action argument
                }
          }

          // Fallback: Check naming convention (less reliable)
          // string tlName = GetResourceName(timeline);
          // string expectedName = $"{tlName}_moment_{moment.Moment}";
          // return Data.Code.FirstOrDefault(c => c?.Name?.Content == expectedName);

         return null; // Not found
     }


    private void ConvertIncludedFiles() {
         UMT.Log("Converting Included Files...");
         if (Data.IncludedFiles == null || !Data.IncludedFiles.Any()) {
             UMT.Log("No included files found.");
             return;
         }

         string datafilesDir = Path.Combine(OutputPath, "datafiles"); // Target for actual file data
         string includedFilesDir = Path.Combine(OutputPath, "includedfiles"); // Target for .yy definitions
         int fileCount = 0;

         foreach (var file in Data.IncludedFiles.Where(f => f?.Name?.Content != null && f.Data != null)) {
              string resourceName = GetResourceName(file); // Mapped unique resource name
              string originalFilePath = file.Name.Content;
               string targetFileName = Path.GetFileName(originalFilePath);
               if (string.IsNullOrEmpty(targetFileName)) targetFileName = resourceName; // Use resource name if original filename is just a path
               targetFileName = SanitizeFileName(targetFileName); // Sanitize filename part

               // Ensure unique filename in datafiles directory
               string targetFilePath = Path.Combine(datafilesDir, targetFileName);
               int counter = 1;
               string baseName = Path.GetFileNameWithoutExtension(targetFileName);
               string ext = Path.GetExtension(targetFileName);
               while(File.Exists(targetFilePath) || Directory.Exists(targetFilePath)) { // Check collision with existing files/dirs
                   targetFileName = $"{baseName}_{counter}{ext}";
                   targetFilePath = Path.Combine(datafilesDir, targetFileName);
                   counter++;
               }

               string resourceKey = $"includedfiles/{resourceName}";
               Guid fileGuid = GetResourceGuid(resourceKey);
               string yyPath = Path.Combine(includedFilesDir, $"{resourceName}.yy");


               try {
                    // Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)); // Ensure datafiles dir exists (already done)
                   File.WriteAllBytes(targetFilePath, file.Data);

                   var yyContent = new JObject(
                       new JProperty("ConfigValues", new JObject()), // Usually empty
                       new JProperty("fileName", targetFileName), // Actual name in datafiles/
                       new JProperty("filePath", "datafiles"), // Folder within project
                       new JProperty("outputFolder", ""), // Optional subfolder on build
                       new JProperty("removeEnd", false), new JProperty("store", false), // GMS2 options
                       new JProperty("ConfigOptions", new JObject()), new JProperty("debug", false),
                       new JProperty("exportAction", 0), new JProperty("exportDir", ""), // 0 = Export as is
                       new JProperty("overwrite", false), new JProperty("freeData", false),
                       new JProperty("origName", originalFilePath), // Store original path/name for reference
                       new JProperty("parent", new JObject(new JProperty("name", "Included Files"), new JProperty("path", "folders/Included Files.yy"))),
                        new JProperty("resourceVersion", "1.0"), new JProperty("name", resourceName), // Resource name
                        new JProperty("tags", new JArray()), new JProperty("resourceType", "GMIncludedFile")
                   );

                   WriteJsonFile(yyPath, yyContent);

                    string relativePath = $"includedfiles/{resourceName}.yy";
                    AddResourceToProject(resourceName, fileGuid, relativePath, "GMIncludedFile", "includedfiles");
                     ResourcePaths[resourceKey] = relativePath;
                     fileCount++;

               } catch (Exception ex) {
                    UMT.Log($"ERROR processing included file '{resourceName}' (Original: {originalFilePath}): {ex.Message}");
               }
         }
          UMT.Log($"Included File conversion finished. Processed {fileCount} files.");
    }


     private void ConvertAudioGroups() {
         UMT.Log("Converting Audio Groups...");
         string agDir = Path.Combine(OutputPath, "audiogroups");
         int agCount = 0;

          // Ensure the default GMS2 audio group exists ("audiogroup_default")
          string defaultAgName = "audiogroup_default";
          Guid defaultAgGuid = GetResourceGuid($"audiogroups/{defaultAgName}"); // Ensure GUID exists
          string defaultAgYyPath = Path.Combine(agDir, $"{defaultAgName}.yy");
          ResourcePaths[$"audiogroups/{defaultAgName}"] = $"audiogroups/{defaultAgName}.yy"; // Ensure path exists
          if (!File.Exists(defaultAgYyPath)) {
              var defaultAgContent = new JObject(
                  new JProperty("targets", -1L), // Default target mask (use Long for -1)
                  new JProperty("parent", new JObject(new JProperty("name", "Audio Groups"), new JProperty("path", "folders/Audio Groups.yy"))),
                  new JProperty("resourceVersion", "1.0"), new JProperty("name", defaultAgName),
                  new JProperty("tags", new JArray()), new JProperty("resourceType", "GMAudioGroup")
              );
              WriteJsonFile(defaultAgYyPath, defaultAgContent);
               AddResourceToProject(defaultAgName, defaultAgGuid, $"audiogroups/{defaultAgName}.yy", "GMAudioGroup", "audiogroups");
               UMT.Log($"Created default audio group: {defaultAgName}");
               agCount++;
          }


         if (Data.AudioGroups != null) {
             foreach (var ag in Data.AudioGroups.Where(a => a?.Name?.Content != null)) {
                  string agName = GetResourceName(ag); // Get mapped name (might be audiogroup_default)
                   if (agName == defaultAgName) continue; // Skip if it mapped to the default one we already handled

                  string resourceKey = $"audiogroups/{agName}";
                  Guid agGuid = GetResourceGuid(resourceKey);
                  string agYyPath = Path.Combine(agDir, $"{agName}.yy");


                  try {
                      var yyContent = new JObject(
                          new JProperty("targets", -1L), // Default target mask
                          new JProperty("parent", new JObject(new JProperty("name", "Audio Groups"), new JProperty("path", "folders/Audio Groups.yy"))),
                           new JProperty("resourceVersion", "1.0"), new JProperty("name", agName),
                           new JProperty("tags", new JArray()), new JProperty("resourceType", "GMAudioGroup")
                      );
                      WriteJsonFile(agYyPath, yyContent);

                       string relativePath = $"audiogroups/{agName}.yy";
                       AddResourceToProject(agName, agGuid, relativePath, "GMAudioGroup", "audiogroups");
                        ResourcePaths[resourceKey] = relativePath;
                        agCount++;

                  } catch (Exception ex) {
                       UMT.Log($"ERROR processing audio group '{agName}': {ex.Message}");
                  }
             }
         }
          UMT.Log($"Audio Group conversion finished. Processed {agCount} groups.");
    }


     private void ConvertTextureGroups() {
         UMT.Log("Converting Texture Groups...");
         string tgDir = Path.Combine(OutputPath, "texturegroups");
         int tgCount = 0;

          // Ensure the default GMS2 texture group exists ("Default")
          string defaultTgName = "Default";
          Guid defaultTgGuid = GetResourceGuid($"texturegroups/{defaultTgName}"); // Ensure GUID
          string defaultTgYyPath = Path.Combine(tgDir, $"{defaultTgName}.yy");
           ResourcePaths[$"texturegroups/{defaultTgName}"] = $"texturegroups/{defaultTgName}.yy"; // Ensure path
          if (!File.Exists(defaultTgYyPath)) {
              var defaultTgContent = new JObject(
                  new JProperty("isScaled", true), new JProperty("autocrop", true),
                  new JProperty("border", 2), new JProperty("mipsToGenerate", 0),
                  new JProperty("groupParent", null), new JProperty("targets", -1L), // Use Long
                  new JProperty("loadImmediately", false),
                  new JProperty("parent", new JObject(new JProperty("name", "Texture Groups"), new JProperty("path", "folders/Texture Groups.yy"))),
                  new JProperty("resourceVersion", "1.0"), new JProperty("name", defaultTgName),
                  new JProperty("tags", new JArray()), new JProperty("resourceType", "GMTextureGroup")
              );
              WriteJsonFile(defaultTgYyPath, defaultTgContent);
               AddResourceToProject(defaultTgName, defaultTgGuid, $"texturegroups/{defaultTgName}.yy", "GMTextureGroup", "texturegroups");
               UMT.Log($"Created default texture group: {defaultTgName}");
               tgCount++;
          }


          if (Data.TextureGroups != null) {
                foreach (var tg in Data.TextureGroups.Where(t => t?.Name?.Content != null)) {
                    string tgName = GetResourceName(tg); // Get mapped name
                     if (tgName == defaultTgName) continue; // Skip default

                    string resourceKey = $"texturegroups/{tgName}";
                    Guid tgGuid = GetResourceGuid(resourceKey);
                    string tgYyPath = Path.Combine(tgDir, $"{tgName}.yy");


                    try {
                        var yyContent = new JObject(
                            new JProperty("isScaled", true), new JProperty("autocrop", true),
                            new JProperty("border", 2), new JProperty("mipsToGenerate", 0),
                            new JProperty("groupParent", null), new JProperty("targets", -1L), // Use Long
                            new JProperty("loadImmediately", false),
                            new JProperty("parent", new JObject(new JProperty("name", "Texture Groups"), new JProperty("path", "folders/Texture Groups.yy"))),
                             new JProperty("resourceVersion", "1.0"), new JProperty("name", tgName),
                             new JProperty("tags", new JArray()), new JProperty("resourceType", "GMTextureGroup")
                        );
                        WriteJsonFile(tgYyPath, yyContent);

                         string relativePath = $"texturegroups/{tgName}.yy";
                         AddResourceToProject(tgName, tgGuid, relativePath, "GMTextureGroup", "texturegroups");
                          ResourcePaths[resourceKey] = relativePath;
                          tgCount++;

                    } catch (Exception ex) {
                         UMT.Log($"ERROR processing texture group '{tgName}': {ex.Message}");
                    }
                }
          } else {
               UMT.Log("No specific UndertaleTextureGroup data found to convert.");
          }

          UMT.Log($"Texture Group conversion finished. Processed {tgCount} groups.");
     }


    private void ConvertExtensions() {
         UMT.Log("Converting Extensions (Basic Structure)...");
         if (Data.Extensions == null || !Data.Extensions.Any()) {
             UMT.Log("No extensions found.");
             return;
         }
         string extDir = Path.Combine(OutputPath, "extensions");
         int extCount = 0;

         foreach (var ext in Data.Extensions.Where(e => e?.Name?.Content != null)) {
              string extName = GetResourceName(ext);
              string resourceKey = $"extensions/{extName}";
              Guid extGuid = GetResourceGuid(resourceKey);
              string extPath = Path.Combine(extDir, extName);
              string yyPath = Path.Combine(extPath, $"{extName}.yy");

               try {
                    Directory.CreateDirectory(extPath);
                    CreatedDirectories.Add(extPath);

                    // Basic Extension .yy skeleton - Files/Functions need manual setup in GMS2
                   var yyContent = new JObject(
                       new JProperty("options", new JArray()), new JProperty("exportToGame", true),
                       new JProperty("supportedTargets", -1L), // Use Long
                       new JProperty("extensionVersion", ext.Version?.Content ?? "1.0.0"),
                       new JProperty("packageId", ""), new JProperty("productId", ""),
                       new JProperty("author", ""), new JProperty("date", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")), // ISO 8601 format
                       new JProperty("license", ""), new JProperty("description", ext.FolderName?.Content ?? ""),
                       new JProperty("helpfile", ""),
                       // Platform properties (defaults)
                       new JProperty("iosProps", true), new JProperty("tvosProps", true), new JProperty("androidProps", true), // Set true to allow platform specifics?
                       new JProperty("installdir", ""), new JProperty("classname", ext.ClassName?.Content ?? ""),
                       // ... (many empty platform specific fields) ...
                       new JProperty("IncludedResources", new JArray()), // <<< Needs manual population in GMS2
                       new JProperty("androidPermissions", new JArray()), new JProperty("copyToTargets", -1L),
                       // ... (more empty fields) ...
                       new JProperty("parent", new JObject(new JProperty("name", "Extensions"), new JProperty("path", "folders/Extensions.yy"))),
                        new JProperty("resourceVersion", "1.2"), // GMExtension version
                        new JProperty("name", extName), new JProperty("tags", new JArray()),
                        new JProperty("resourceType", "GMExtension")
                   );

                   WriteJsonFile(yyPath, yyContent);
                    string relativePath = $"extensions/{extName}/{extName}.yy";
                    AddResourceToProject(extName, extGuid, relativePath, "GMExtension", "extensions");
                     ResourcePaths[resourceKey] = relativePath;
                     extCount++;

                      UMT.Log($"Warning: Extension '{extName}' created with basic structure. Files and function definitions must be added manually in GMS2.");

               } catch (Exception ex) {
                    UMT.Log($"ERROR processing extension '{extName}': {ex.Message}");
               }
         }
          UMT.Log($"Extension conversion finished. Processed {extCount} extensions.");
    }

     private void ConvertNotes() {
          UMT.Log("Skipping Notes conversion (GMS1 had no equivalent resource).");
          // Just ensure the folder entry exists for the .yyp
           if (!FolderStructure.ContainsKey("notes")) FolderStructure["notes"] = new List<string>();
     }


    // === Project File Creation ===

    private void AddResourceToProject(string name, Guid guid, string path, string type, string folderKey)
    {
         // Check for duplicates before adding (shouldn't happen with GetResourceGuid)
         string guidString = guid.ToString("D");
         if (ResourceList.Any(r => r["id"]?["name"]?.ToString() == guidString)) {
              UMT.Log($"Warning: Duplicate resource GUID detected for '{name}' ({guidString}). Skipping add to project list.");
              return;
         }

        ResourceList.Add(new JObject(
            new JProperty("id", new JObject(
                new JProperty("name", guidString),
                new JProperty("path", path.Replace('\\', '/')) // Ensure forward slashes in path
            )),
            new JProperty("order", 0) // GMS2 determines order?
        ));

        // Add GUID to the folder structure tracker
        if (FolderStructure.ContainsKey(folderKey)) {
             if (!FolderStructure[folderKey].Contains(guidString)) {
                  FolderStructure[folderKey].Add(guidString);
             }
        } else {
             // This shouldn't happen if CreateGMS2Directory initializes the keys
             UMT.Log($"Warning: Folder key '{folderKey}' not found in structure map when adding resource '{name}'.");
             FolderStructure[folderKey] = new List<string> { guidString };
        }
    }


     // Creates the main .yyp project file
    private void CreateProjectFile()
    {
        string yypPath = Path.Combine(OutputPath, $"{ProjectName}.yyp");

        // Define standard GMS2 folders, types, and display names
         var folderDefs = new List<Tuple<string, string, string>> {
             Tuple.Create("sprites", "GMSprite", "Sprites"),
             Tuple.Create("tilesets", "GMTileSet", "Tile Sets"),
             Tuple.Create("sounds", "GMSound", "Sounds"),
             Tuple.Create("paths", "GMPath", "Paths"),
             Tuple.Create("scripts", "GMScript", "Scripts"),
             Tuple.Create("shaders", "GMShader", "Shaders"),
             Tuple.Create("fonts", "GMFont", "Fonts"),
             Tuple.Create("timelines", "GMTimeline", "Timelines"),
             Tuple.Create("objects", "GMObject", "Objects"),
             Tuple.Create("rooms", "GMRoom", "Rooms"),
             // Tuple.Create("sequences", "GMSequence", "Sequences"), // Add if converting sequences
             // Tuple.Create("notes", "GMNote", "Notes"), // Add if converting notes
             Tuple.Create("extensions", "GMExtension", "Extensions"),
             Tuple.Create("audiogroups", "GMAudioGroup", "Audio Groups"),
             Tuple.Create("texturegroups", "GMTextureGroup", "Texture Groups"),
             Tuple.Create("includedfiles", "GMIncludedFile", "Included Files")
         };

          // Ensure all keys exist in FolderStructure before iterating
          foreach(var def in folderDefs) {
               if (!FolderStructure.ContainsKey(def.Item1)) {
                    FolderStructure[def.Item1] = new List<string>();
               }
          }

         // Build the folder view structure for the IDE
         List<JObject> folderViews = new List<JObject>();
         string foldersMetaDir = Path.Combine(OutputPath, "folders");
         Directory.CreateDirectory(foldersMetaDir); // Ensure base 'folders' directory exists

         foreach (var def in folderDefs) {
             string folderKey = def.Item1;
             string resourceType = def.Item2;
             string displayName = def.Item3;
             Guid folderGuid = GetResourceGuid($"folders/{displayName}"); // Stable GUID for the folder node itself
             string folderMetaPath = Path.Combine(foldersMetaDir, $"{displayName}.yy");
             string folderMetaRelativePath = $"folders/{displayName}.yy".Replace('\\', '/');

             // Add folder to the IDE view structure
             folderViews.Add(new JObject(
                 new JProperty("folderPath", folderMetaRelativePath), // Path to the folder's .yy definition
                 new JProperty("order", folderViews.Count),
                 new JProperty("resourceVersion", "1.0"),
                 new JProperty("name", folderGuid.ToString("D")), // Use GUID as node name in YYP Folders list? Check GMS2 format. Or use displayName? Let's use GUID.
                  // Update: GMS2.3+ YYP "Folders" list seems to use the folderPath as the identifier, not name/guid. Let's match that.
                  // new JProperty("name", folderGuid.ToString("D")), // Old way?
                 new JProperty("tags", new JArray()),
                 new JProperty("resourceType", "GMFolder") // Type of this node is GMFolder
             ));

             // Create the actual folder definition .yy file
             var folderYyContent = new JObject(
                 new JProperty("isDefaultView", true), // Mark as standard IDE folder
                 new JProperty("localisedFolderName", $"ResourceTree_{displayName.Replace(" ", "")}"), // For IDE localization
                 new JProperty("filterType", resourceType), // Type of resource this folder holds
                 new JProperty("folderName", displayName), // Display name in IDE
                 new JProperty("isResourceFolder", true), // Is it a root resource type folder?
                 new JProperty("resourceVersion", "1.0"), // GMFolder version
                 new JProperty("name", displayName), // Name of the folder resource itself
                 new JProperty("tags", new JArray()),
                 new JProperty("resourceType", "GMFolder")
             );
             WriteJsonFile(folderMetaPath, folderYyContent);
         }


        // Build Room Order Nodes list - Use ResourceGuids to ensure room was processed
         var roomOrderNodes = new JArray();
          if (Data.Rooms != null) {
              foreach(var room in Data.Rooms) {
                   if (room == null) continue;
                   string roomName = GetResourceName(room);
                   string roomKey = $"rooms/{roomName}";
                   if (ResourceGuids.TryGetValue(roomKey, out Guid roomGuid) && ResourcePaths.TryGetValue(roomKey, out string roomPath)) {
                        roomOrderNodes.Add(new JObject(
                             new JProperty("roomId", new JObject(
                                  new JProperty("name", roomGuid.ToString("D")),
                                  new JProperty("path", roomPath.Replace('\\','/'))
                             )),
                              // Add physics category data if needed (usually default)
                             new JProperty("category", "DEFAULT")
                        ));
                   }
              }
          }


        // Main .yyp content structure (GMS2.3+ format)
        var yypContent = new JObject(
             new JProperty("projectName", ProjectName),
             new JProperty("projectDir", ""), // Relative to YYP? Usually empty.
             new JProperty("packageName", ProjectName), // Android package name etc.
             new JProperty("packageDir", ""),
             new JProperty("constants", new JArray()), // Global constants defined in IDE
             new JProperty("configs", new JObject( // Build Configurations
                 new JProperty("name", "Default"),
                 new JProperty("children", new JArray())
             )),
             new JProperty("RoomOrderNodes", roomOrderNodes), // Order of rooms in Room Manager
             new JProperty("Folders", new JArray(folderViews)), // IDE folder view structure (references folder .yy files)

             // Resource list: Array of objects like { "id": { "name": guid, "path": path }, "order": N }
             new JProperty("resources", new JArray(ResourceList)),

             // Other top-level properties
             new JProperty("Options", new JArray( // References to option .yy files
                 new JObject(new JProperty("name", "Main"), new JProperty("path", "options/main/options_main.yy")),
                 new JObject(new JProperty("name", "Windows"), new JProperty("path", "options/windows/options_windows.yy"))
                 // Add other platforms if created
             )),
             new JProperty("defaultScriptType", 1), // 0 = DnD, 1 = GML
             new JProperty("isEcma", false),
             new JProperty("tutorialPath", ""),
             new JProperty("configs", new JObject(new JProperty("name", "Default"), new JProperty("children", new JArray()))), // Duplicate? Remove if redundant. Check GMS2 yyp structure. Seems needed.
             new JProperty("RoomOrderNodes", roomOrderNodes), // Duplicate? Remove if redundant. Seems needed.
             new JProperty("Folders", new JArray(folderViews)), // Duplicate? Remove if redundant. Seems needed.

             // Resource GUID lists (might be legacy or used internally? GMS2.3+ often relies on the main resources list)
             // Let's include them based on observed YYP structure, referencing the main resource GUIDs
             new JProperty("AudioGroups", new JArray(ResourceList.Where(r => r["id"]["path"].ToString().StartsWith("audiogroups/")).Select(r => r["id"].DeepClone()))),
             new JProperty("TextureGroups", new JArray(ResourceList.Where(r => r["id"]["path"].ToString().StartsWith("texturegroups/")).Select(r => r["id"].DeepClone()))),
             new JProperty("IncludedFiles", new JArray(ResourceList.Where(r => r["id"]["path"].ToString().StartsWith("includedfiles/")).Select(r => r["id"].DeepClone()))),
             // Add similar lists for Sprites, Sounds, Objects etc. if required by GMS2 format. Often redundant if `resources` list is complete.

             new JProperty("MetaData", new JObject(new JProperty("IDEVersion", GMS2_VERSION))),
             new JProperty("projectVersion", "1.0"),
             new JProperty("packageId", ""), new JProperty("productId", ""),
             new JProperty("parentProject", null),
             new JProperty("YYPFormat", "1.2"), // YYP file format version (Check current GMS2 for latest)
             new JProperty("serialiseFrozenViewModels", false),

             // Root resource object properties
             new JProperty("resourceVersion", "1.7"), // GMProject resource version (Check current GMS2)
             new JProperty("name", ProjectName),
             new JProperty("tags", new JArray()),
             new JProperty("resourceType", "GMProject")
        );

        WriteJsonFile(yypPath, yypContent);
         UMT.Log($"Project file created: {yypPath}");
    }


    private void CreateOptionsFiles() {
         string optionsDir = Path.Combine(OutputPath, "options");
         string mainOptionsDir = Path.Combine(optionsDir, "main");
         string windowsOptionsDir = Path.Combine(optionsDir, "windows");
         // Ensure directories exist (should have been created earlier)
         Directory.CreateDirectory(mainOptionsDir);
         Directory.CreateDirectory(windowsOptionsDir);

         // --- options_main.yy ---
         string mainOptPath = Path.Combine(mainOptionsDir, "options_main.yy");
         var mainOptContent = new JObject(
             new JProperty("option_gameguid", ProjectGuid.ToString("D")), // Use standard GUID format D
             new JProperty("option_gameid", Data.GeneralInfo?.GameID?.ToString() ?? "0"), // Use original Game ID if available
             new JProperty("option_game_speed", Data.GeneralInfo?.GameSpeed ?? 60),
             new JProperty("option_mips_for_3d_textures", false),
             new JProperty("option_draw_colour", 4294967295), // White ARGB (0xFFFFFFFF)
             new JProperty("option_window_colour", 255), // Black BGR (0x000000FF)? Or 0xFF000000 (Black ARGB)? Use black ARGB.
              new JProperty("option_window_colour", 0xFF000000), // Black ARGB
             new JProperty("option_steam_app_id", Data.GeneralInfo?.SteamAppID?.ToString() ?? "0"),
             new JProperty("option_sci_usesci", false),
             new JProperty("option_author", Data.GeneralInfo?.Author?.Content ?? ""),
             new JProperty("option_collision_compatibility", true), // Assume needed for GMS1->2 port
             new JProperty("option_copy_on_write_enabled", Data.GeneralInfo?.CopyOnWriteEnabled ?? false), // Use original CoW setting
             new JProperty("option_lastchanged", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")),
             new JProperty("option_spine_licence", false), // Assume no Spine license
             new JProperty("option_template_image", "${base_options_dir}/main/template_image.png"),
             new JProperty("option_template_icon", "${base_options_dir}/main/template_icon.ico"), // Use .ico for main?
             new JProperty("option_template_description", null),
              new JProperty("resourceVersion", "1.4"), // GMMainOptions version
              new JProperty("name", "Main"),
              new JProperty("tags", new JArray()),
              new JProperty("resourceType", "GMMainOptions")
         );
         WriteJsonFile(mainOptPath, mainOptContent);
          // TODO: Copy/Create placeholder template_image.png, template_icon.ico if needed

         // --- options_windows.yy ---
         string winOptPath = Path.Combine(windowsOptionsDir, "options_windows.yy");
         // Try to get version info from GeneralInfo if available
         string version = "1.0.0.0";
         if(Data.GeneralInfo?.Version != null) {
              version = $"{Data.GeneralInfo.Version.Major}.{Data.GeneralInfo.Version.Minor}.{Data.GeneralInfo.Version.Release}.{Data.GeneralInfo.Version.Build}";
         }

         var winOptContent = new JObject(
             new JProperty("option_windows_display_name", Data.GeneralInfo?.DisplayName?.Content ?? ProjectName),
             new JProperty("option_windows_executable_name", $"${{project_name}}.exe"), // Use variable
             new JProperty("option_windows_version", version), // Use extracted version
             new JProperty("option_windows_company_info", Data.GeneralInfo?.Company?.Content ?? Data.GeneralInfo?.Author?.Content ?? ""),
             new JProperty("option_windows_product_info", Data.GeneralInfo?.Product?.Content ?? ProjectName),
             new JProperty("option_windows_copyright_info", Data.GeneralInfo?.Copyright?.Content ?? $"(c) {DateTime.Now.Year}"),
             new JProperty("option_windows_description_info", Data.GeneralInfo?.Description?.Content ?? ProjectName),
             new JProperty("option_windows_display_cursor", true), // Default: show cursor
             new JProperty("option_windows_icon", "${base_options_dir}/windows/icons/icon.ico"),
             new JProperty("option_windows_save_location", 0), // 0 = AppData, 1 = Local
             new JProperty("option_windows_splash_screen", "${base_options_dir}/windows/splash/splash.png"),
             new JProperty("option_windows_use_splash", false), // Disable splash screen by default
             new JProperty("option_windows_start_fullscreen", false), // Default to windowed
             new JProperty("option_windows_allow_fullscreen_switching", true),
             new JProperty("option_windows_interpolate_pixels", Data.GeneralInfo?.InterpolatePixels ?? false), // Use original interpolation setting
             new JProperty("option_windows_vsync", false), // Default VSync off
             new JProperty("option_windows_resize_window", true), // Allow resize
             new JProperty("option_windows_borderless", false),
             new JProperty("option_windows_scale", 0), // 0 = Keep aspect ratio
             new JProperty("option_windows_copy_exe_to_dest", false),
             new JProperty("option_windows_sleep_margin", 10), // GMS default sleep margin
              new JProperty("option_windows_texture_page", "2048x2048"), // Standard texture page size
              new JProperty("option_windows_installer_finished", "${base_options_dir}/windows/installer/finished.bmp"),
              new JProperty("option_windows_installer_header", "${base_options_dir}/windows/installer/header.bmp"),
              new JProperty("option_windows_license", "${base_options_dir}/windows/installer/license.txt"),
              new JProperty("option_windows_nsis_file", "${base_options_dir}/windows/installer/nsis_script.nsi"),
              new JProperty("option_windows_enable_steam", (Data.GeneralInfo?.SteamAppID ?? 0) > 0),
              new JProperty("option_windows_disable_sandbox", false),
              new JProperty("option_windows_steam_use_alternative_launcher", false),
              new JProperty("resourceVersion", "1.1"), // GMWindowsOptions version
              new JProperty("name", "Windows"),
              new JProperty("tags", new JArray()),
              new JProperty("resourceType", "GMWindowsOptions")
         );
         WriteJsonFile(winOptPath, winOptContent);
          // TODO: Copy/Create placeholder icon.ico, splash.png, installer files if needed

          UMT.Log("Default options files created.");
    }

     private void CreateDefaultConfig() {
          string configDir = Path.Combine(OutputPath, "configs");
          string defaultCfgPath = Path.Combine(configDir, "Default.config");

           // GMS2 uses simple key=value pairs under [ConfigName]
           // Minimal required seems to be just the section header.
           // Add build settings here if needed (e.g., included file overrides per config)
           string configContent = "[Default]\n"; // Just the section header is often enough

           try {
                File.WriteAllText(defaultCfgPath, configContent);
                 UMT.Log($"Created default config file: {defaultCfgPath}");
           } catch (Exception ex) {
                UMT.Log($"ERROR creating default config file: {ex.Message}");
           }
     }

} // End of GMS2Converter class

// Script entry point for UMT menu
public static class ScriptEntry
{
    public static IUMTScript Script = new GMS2Converter();
}
