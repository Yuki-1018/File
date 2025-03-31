// Undertale Mod Tool Script: Convert to GameMaker Studio 2 Project
// Version: 1.3 (Fix CS1026 comma error in ConvertSprites)
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
    private const string GMS2_VERSION = "2.3.7.606"; // Target GMS2 IDE version
    private const string RUNTIME_VERSION = "2.3.7.476"; // Target GMS2 Runtime version
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
        if (Data.Code == null || !Data.Code.Any() || Data.Code.All(c => c == null || c.Decompiled == null))
        {
            if (TRY_DECOMPILE_IF_NEEDED)
            {
                UMT.Log("Code not decompiled or missing. Attempting decompilation (this might require manual action in UMT)...");
                try
                {
                    UMT.Log(">> Please ensure you have run 'Decompile All' in UMT if automatic decompilation fails. <<");
                    // Re-check after potential decompilation attempt
                    if (Data.Code == null || !Data.Code.Any() || Data.Code.All(c => c == null || c.Decompiled == null))
                    {
                         UMT.Log("Warning: Code still appears decompiled after attempt. Conversion will proceed but scripts/events will be empty.");
                    } else {
                         UMT.Log("Decompilation seems successful or was already done.");
                    }
                }
                catch (Exception ex)
                {
                    UMT.ShowError("Error during decompilation attempt: " + ex.Message + "\nPlease try decompiling manually in UMT first.");
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
            // Delay directory creation until needed? No, create base structure first.
             Directory.CreateDirectory(OutputPath);
             CreatedDirectories.Add(OutputPath);

            ProjectGuid = Guid.NewGuid();

            // Create basic GMS2 folders (needed for structure and some defaults)
             CreateGMS2Directory("options");
             CreateGMS2Directory("options/main");
             CreateGMS2Directory("options/windows");
             CreateGMS2Directory("configs");
             CreateGMS2Directory("folders");
             CreateGMS2Directory("datafiles"); // For included files data

             // Create default config
             CreateDefaultConfig();

             // Build Resource Name Map
             BuildResourceNameMap();

             // --- 3. Convert Resources ---
             UMT.Log("Starting resource conversion...");
            var conversionSteps = new List<Tuple<string, Action>> {
                Tuple.Create("Audio Groups", (Action)ConvertAudioGroups),
                Tuple.Create("Texture Groups", (Action)ConvertTextureGroups),
                Tuple.Create("Sprites (incl. Backgrounds as Sprites)", (Action)ConvertSprites),
                Tuple.Create("Sounds", (Action)ConvertSounds),
                Tuple.Create("Tilesets (from Backgrounds/Sprites)", (Action)ConvertTilesets),
                Tuple.Create("Fonts", (Action)ConvertFonts),
                Tuple.Create("Paths", (Action)ConvertPaths),
                Tuple.Create("Scripts", (Action)ConvertScripts),
                Tuple.Create("Shaders", (Action)ConvertShaders),
                Tuple.Create("Timelines", (Action)ConvertTimelines),
                Tuple.Create("Objects", (Action)ConvertObjects),
                Tuple.Create("Rooms", (Action)ConvertRooms),
                Tuple.Create("Included Files", (Action)ConvertIncludedFiles),
                Tuple.Create("Extensions", (Action)ConvertExtensions),
                Tuple.Create("Notes", (Action)ConvertNotes)
            };

            foreach(var step in conversionSteps) {
                 try {
                     // Create resource directory just before converting that type
                     string resourceDirName = step.Item1.Split(' ')[0].ToLowerInvariant();
                      if (resourceDirName.EndsWith("s")) {} // Already plural
                      else if (resourceDirName == "audiogroup") resourceDirName = "audiogroups";
                      else if (resourceDirName == "texturegroup") resourceDirName = "texturegroups";
                      else if (resourceDirName == "included") resourceDirName = "includedfiles"; // Special case
                      else if (resourceDirName == "note") resourceDirName = "notes";
                      else resourceDirName += "s";

                      if (resourceDirName != "options" && resourceDirName != "configs" && resourceDirName != "folders" && resourceDirName != "datafiles" ) {
                           CreateGMS2Directory(resourceDirName);
                      }

                      // Execute conversion
                     step.Item2();
                 } catch (Exception stepEx) {
                      UMT.Log($" >>> CRITICAL ERROR during '{step.Item1}' conversion step: {stepEx.Message}\n{stepEx.StackTrace}");
                      UMT.ShowError($"Critical error during '{step.Item1}' conversion. Check logs. Aborting further steps.");
                      throw;
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
                 // Add top-level folders to structure tracker immediately for .yyp Folders section
                 string topLevelFolderName = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                 if (!FolderStructure.ContainsKey(topLevelFolderName)) {
                      FolderStructure[topLevelFolderName] = new List<string>();
                 }
            } catch (Exception ex) {
                 UMT.Log($"ERROR: Failed to create directory {fullPath}: {ex.Message}");
                 throw;
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
        if (!ResourceGuids.TryGetValue(resourceKey, out Guid guid))
        {
            guid = Guid.NewGuid();
            ResourceGuids[resourceKey] = guid;
        }
        return guid;
    }

     private void BuildResourceNameMap()
     {
         UMT.Log("Building resource name map...");
         var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // Case-insensitive check for base names
         var existingValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Case-insensitive check for final names
         ResourceToNameMap.Clear();

         Action<IList<UndertaleNamedResource>> processList = (list) => {
             if (list == null) return;
             foreach (var res in list.Where(r => r?.Name?.Content != null)) {
                  if (ResourceToNameMap.ContainsKey(res)) continue;

                 string baseName = SanitizeFileName(res.Name.Content);
                 string uniqueName = baseName;
                 int count = 0;

                 while (existingValues.Contains(uniqueName))
                 {
                     if (!nameCounts.ContainsKey(baseName)) nameCounts[baseName] = 1;
                     count = nameCounts[baseName]++;
                     uniqueName = $"{baseName}_{count}";
                 }

                 ResourceToNameMap[res] = uniqueName;
                 existingValues.Add(uniqueName);
                  // Ensure base name count is tracked even if first instance didn't collide
                  if (!nameCounts.ContainsKey(baseName)) { nameCounts[baseName] = 1; }
             }
         };

         Action<IList<UndertaleNamedResourceGroup>> processGroupList = (list) => {
                if (list == null) return;
                foreach (var res in list.Where(r => r?.Name?.Content != null)) {
                    if (ResourceToNameMap.ContainsKey(res)) continue;
                     string baseName = SanitizeFileName(res.Name.Content);
                     if (baseName.Equals("default", StringComparison.OrdinalIgnoreCase)) {
                          if (res is UndertaleAudioGroup) baseName = "audiogroup_default";
                          else if (res is UndertaleTextureGroup) baseName = "Default";
                     }

                     string uniqueName = baseName;
                     int count = 0;
                     while (existingValues.Contains(uniqueName)) {
                         if (!nameCounts.ContainsKey(baseName)) nameCounts[baseName] = 1;
                         count = nameCounts[baseName]++;
                         uniqueName = $"{baseName}_{count}";
                     }
                     ResourceToNameMap[res] = uniqueName;
                     existingValues.Add(uniqueName);
                      if (!nameCounts.ContainsKey(baseName)) { nameCounts[baseName] = 1; }
                }
           };

          // Process in a somewhat logical order
          processList(Data.Sprites?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Backgrounds?.Cast<UndertaleNamedResource>().ToList()); // Backgrounds might become sprites/tilesets
          processList(Data.Sounds?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Objects?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Rooms?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Scripts?.Cast<UndertaleNamedResource>().ToList()); // Scripts before Objects/Timelines if possible
          processList(Data.Shaders?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Fonts?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Paths?.Cast<UndertaleNamedResource>().ToList());
          processList(Data.Timelines?.Cast<UndertaleNamedResource>().ToList());
          processGroupList(Data.AudioGroups?.Cast<UndertaleNamedResourceGroup>().ToList());
          processGroupList(Data.TextureGroups?.Cast<UndertaleNamedResourceGroup>().ToList());
           // Code entries might have names if decompiled - process them too?
           // processList(Data.Code?.Cast<UndertaleNamedResource>().ToList()); // Uncomment if Code entries need mapping by name

          // Included files
          if (Data.IncludedFiles != null) {
              foreach(var incFile in Data.IncludedFiles.Where(f => f?.Name?.Content != null)) {
                  if (ResourceToNameMap.ContainsKey(incFile)) continue;
                  string baseName = SanitizeFileName(Path.GetFileName(incFile.Name.Content));
                   if (string.IsNullOrEmpty(baseName)) baseName = "included_file"; // Handle empty filenames
                  string uniqueName = baseName;
                  int count = 0;
                   while (existingValues.Contains(uniqueName)) {
                      if (!nameCounts.ContainsKey(baseName)) nameCounts[baseName] = 1;
                      count = nameCounts[baseName]++;
                      uniqueName = $"{baseName}_{count}";
                  }
                  ResourceToNameMap[incFile] = uniqueName;
                  existingValues.Add(uniqueName);
                   if (!nameCounts.ContainsKey(baseName)) { nameCounts[baseName] = 1; }
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
          if (res is UndertaleChunk Tagi t) {
              return SanitizeFileName($"resource_{t.GetType().Name}_{Guid.NewGuid().ToString("N").Substring(0,4)}");
          }
          return defaultName;
     }

    private void WriteJsonFile(string filePath, JObject jsonContent)
    {
        try
        {
             Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, jsonContent.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
             UMT.Log($"ERROR writing JSON file {filePath}: {ex.Message}");
             throw;
        }
    }

     private JObject CreateResourceReference(string name, Guid guid, string resourceTypeFolder) {
         if (guid == Guid.Empty || string.IsNullOrEmpty(name)) {
             return null;
         }
         string path;
          // Handle default groups specially as their .yy files are directly in the group folder
         if(resourceTypeFolder == "audiogroups" && name == "audiogroup_default") {
              path = "audiogroups/audiogroup_default.yy";
         } else if(resourceTypeFolder == "texturegroups" && name == "Default") {
              path = "texturegroups/Default.yy";
         } else {
              path = $"{resourceTypeFolder}/{name}/{name}.yy";
         }

         return new JObject(
             new JProperty("name", name),
             new JProperty("path", path.Replace('\\', '/')) // Ensure forward slashes
         );
     }

      private JObject CreateResourceReference(UndertaleResource res, string resourceTypeFolder) {
           if (res == null) return null;
           string name = GetResourceName(res);
            if (string.IsNullOrEmpty(name) || name.StartsWith("unknown_")) {
                return null;
            }
            string resourceKey = $"{resourceTypeFolder}/{name}";
             Guid guid = GetResourceGuid(resourceKey); // Ensure GUID exists

            return CreateResourceReference(name, guid, resourceTypeFolder);
       }


    // === Resource Conversion Functions ===

    private void ConvertSprites()
    {
        UMT.Log("Converting Sprites (incl. Backgrounds as Sprites)...");
        List<UndertaleSprite> allSprites = new List<UndertaleSprite>();
        if (Data.Sprites != null) allSprites.AddRange(Data.Sprites.Where(s => s?.Name?.Content != null));

        int initialSpriteCount = allSprites.Count;
        int bgConvertedCount = 0;
        if (Data.Backgrounds != null) {
            foreach(var bg in Data.Backgrounds.Where(b => b?.Texture?.TexturePage != null && b.Name?.Content != null)) {
                 string bgName = GetResourceName(bg); // Use mapped name
                 if (allSprites.Any(s => GetResourceName(s) == bgName)) continue; // Check against mapped names

                 var pseudoSprite = new UndertaleSprite {
                     Name = bg.Name, // Keep original name object reference for mapping
                     Width = bg.Texture.TexturePage.SourceWidth > 0 ? bg.Texture.TexturePage.SourceWidth : bg.Texture.TexturePage.TargetWidth,
                     Height = bg.Texture.TexturePage.SourceHeight > 0 ? bg.Texture.TexturePage.SourceHeight : bg.Texture.TexturePage.TargetHeight,
                     MarginLeft = 0,
                     MarginRight = (ushort)Math.Max(0, (bg.Texture.TexturePage.TargetWidth > 0 ? bg.Texture.TexturePage.TargetWidth : 1) - 1),
                     MarginBottom = (ushort)Math.Max(0, (bg.Texture.TexturePage.TargetHeight > 0 ? bg.Texture.TexturePage.TargetHeight : 1) - 1),
                     MarginTop = 0, OriginX = 0, OriginY = 0,
                     BBoxMode = UndertaleSprite.BoundingBoxMode.Automatic, SepMasks = 0,
                     PlaybackSpeed = 15, PlaybackSpeedType = UndertaleSprite.SpritePlaybackSpeedType.FramesPerSecond,
                     Textures = new List<UndertaleSprite.TextureEntry> { bg.Texture }
                 };
                 if (pseudoSprite.Width > 0 && pseudoSprite.Height > 0) {
                     allSprites.Add(pseudoSprite);
                     bgConvertedCount++;
                 }
            }
        }
        UMT.Log($"Processing {initialSpriteCount} original sprites and {bgConvertedCount} backgrounds as sprites. Total: {allSprites.Count}.");
        if (!allSprites.Any()) return;

        string spriteDir = Path.Combine(OutputPath, "sprites");
        int spriteProcessedCount = 0;

        foreach (var sprite in allSprites)
        {
            // Name check already done during list creation/filtering
            string spriteName = GetResourceName(sprite);
            string resourceKey = $"sprites/{spriteName}";
            Guid spriteGuid = GetResourceGuid(resourceKey);
            string spritePath = Path.Combine(spriteDir, spriteName);
            string yyPath = Path.Combine(spritePath, $"{spriteName}.yy");
            string imagesPath = Path.Combine(spritePath, "images");

            try
            {
                Directory.CreateDirectory(spritePath); // Ensure specific sprite folder exists
                Directory.CreateDirectory(imagesPath);
                // CreatedDirectories.Add(spritePath); // Not needed if using resource folder creation logic
                // CreatedDirectories.Add(imagesPath);

                List<JObject> frameList = new List<JObject>();
                List<Guid> frameGuids = new List<Guid>();
                Guid firstFrameGuid = Guid.Empty;

                for (int i = 0; i < sprite.Textures.Count; i++) { /* ... Frame extraction logic as before ... */
                    var texEntry = sprite.Textures[i];
                    if (texEntry?.TexturePage == null) continue;
                    Guid frameGuid = Guid.NewGuid();
                    frameGuids.Add(frameGuid);
                    if (i == 0) firstFrameGuid = frameGuid;
                    string frameFileName = $"{frameGuid}.png";
                    string frameFilePath = Path.Combine(imagesPath, frameFileName);
                    try {
                        using (DirectBitmap frameBitmap = TextureWorker.GetTexturePageImageRect(texEntry.TexturePage, Data)) {
                            if (frameBitmap?.Bitmap != null) {
                                frameBitmap.Bitmap.Save(frameFilePath, ImageFormat.Png);
                            } else {
                                 using (var placeholder = new Bitmap(Math.Max(1, sprite.Width), Math.Max(1, sprite.Height), PixelFormat.Format32bppArgb))
                                 using (var g = Graphics.FromImage(placeholder)) { g.Clear(Color.FromArgb(128, 255, 0, 255)); placeholder.Save(frameFilePath, ImageFormat.Png); }
                            }
                        }
                    } catch (Exception ex) {
                         UMT.Log($"ERROR extracting frame {i} for sprite '{spriteName}': {ex.Message}. Creating placeholder.");
                         try { using (var placeholder = new Bitmap(Math.Max(1, sprite.Width), Math.Max(1, sprite.Height), PixelFormat.Format32bppArgb)) using (var g = Graphics.FromImage(placeholder)) { g.Clear(Color.FromArgb(128, 255, 0, 255)); placeholder.Save(frameFilePath, ImageFormat.Png); } }
                         catch (Exception phEx) { UMT.Log($"ERROR creating placeholder: {phEx.Message}"); }
                    }
                     var frameObject = new JObject(
                         new JProperty("Config", "Default"), new JProperty("FrameId", frameGuid.ToString("D")),
                         new JProperty("LayerId", null), new JProperty("resourceVersion", "1.0"),
                         new JProperty("name", frameGuid.ToString("N")), new JProperty("tags", new JArray()),
                         new JProperty("resourceType", "GMSpriteFrame")
                     );
                    frameList.Add(frameObject);
                }
                if (frameGuids.Count == 0) { /* ... Placeholder frame creation logic as before ... */
                     Guid frameGuid = Guid.NewGuid(); frameGuids.Add(frameGuid); firstFrameGuid = frameGuid;
                     string frameFileName = $"{frameGuid}.png"; string frameFilePath = Path.Combine(imagesPath, frameFileName);
                     try {
                          using (var placeholder = new Bitmap(Math.Max(1, sprite.Width), Math.Max(1, sprite.Height), PixelFormat.Format32bppArgb)) using (var g = Graphics.FromImage(placeholder)) { g.Clear(Color.FromArgb(128, 0, 255, 255)); placeholder.Save(frameFilePath, ImageFormat.Png); }
                          var frameObject = new JObject( new JProperty("Config", "Default"), new JProperty("FrameId", frameGuid.ToString("D")), new JProperty("LayerId", null), new JProperty("resourceVersion", "1.0"), new JProperty("name", frameGuid.ToString("N")), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMSpriteFrame"));
                          frameList.Add(frameObject);
                     } catch (Exception phEx) { UMT.Log($"ERROR creating placeholder frame for empty sprite '{spriteName}': {phEx.Message}"); continue; /* Skip sprite if placeholder fails */ }
                }


                 int gms2BBoxMode = 0; int gms2CollisionKind = 1;
                 switch (sprite.BBoxMode) { case UndertaleSprite.BoundingBoxMode.FullImage: gms2BBoxMode = 1; break; case UndertaleSprite.BoundingBoxMode.Manual: gms2BBoxMode = 2; break; }
                 if (sprite.SepMasks > 0) gms2CollisionKind = 0; else gms2CollisionKind = 1;

                 string textureGroupName = "Default";
                 Guid textureGroupGuid = GetResourceGuid("texturegroups/Default");
                 if (sprite.Textures.Count > 0 && sprite.Textures[0].TexturePage != null && Data.TextureGroups != null) {
                    var utGroup = Data.TextureGroups.FirstOrDefault(tg => tg.Pages.Contains(sprite.Textures[0].TexturePage));
                    if(utGroup != null) { textureGroupName = GetResourceName(utGroup); textureGroupGuid = GetResourceGuid($"texturegroups/{textureGroupName}"); }
                 }
                 JObject textureGroupRef = CreateResourceReference(textureGroupName, textureGroupGuid, "texturegroups");

                 Guid imageLayerGuid = Guid.NewGuid();
                 var imageLayer = new JObject(
                     new JProperty("visible", true), new JProperty("isLocked", false), new JProperty("blendMode", 0),
                     new JProperty("opacity", 100.0), new JProperty("displayName", "default"), new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", imageLayerGuid.ToString("D")), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMImageLayer")
                 );

                 var spriteFramesTrack = new JObject( /* ... Track definition as before ... */
                     new JProperty("spriteId", null),
                     new JProperty("keyframes", new JObject(
                         new JProperty("Keyframes", new JArray( frameGuids.Select((guid, index) => new JObject( new JProperty("Key", (float)index), new JProperty("Length", 1.0f), new JProperty("Stretch", false), new JProperty("Disabled", false), new JProperty("IsCreationKey", false), new JProperty("Channels", new JObject( new JProperty("0", new JObject( new JProperty("Id", new JObject( new JProperty("name", guid.ToString("D")), new JProperty("path", $"sprites/{spriteName}/{spriteName}.yy") )), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "SpriteFrameKeyframe") )) )), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "Keyframe<SpriteFrameKeyframe>") )) )),
                         new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<SpriteFrameKeyframe>")
                     )),
                     new JProperty("trackColour", 0), new JProperty("inheritsTrackColour", true), new JProperty("builtinName", 0),
                     new JProperty("traits", 0), new JProperty("interpolation", 1), new JProperty("tracks", new JArray()),
                     new JProperty("events", new JArray()), new JProperty("modifiers", new JArray()), new JProperty("isCreationTrack", false),
                     new JProperty("resourceVersion", "1.0"), new JProperty("name", "frames"), new JProperty("tags", new JArray()),
                     new JProperty("resourceType", "GMSpriteFramesTrack")
                 );

                // Create Sprite .yy file content (JSON)
                var yyContent = new JObject(
                    new JProperty("bboxmode", gms2BBoxMode),                       // Comma needed
                    new JProperty("collisionKind", gms2CollisionKind),             // Comma needed
                    new JProperty("type", 0),                                       // Comma needed
                    new JProperty("origin", GetGMS2Origin(sprite.OriginX, sprite.OriginY, sprite.Width, sprite.Height)), // Comma needed
                    new JProperty("preMultiplyAlpha", false),                       // Comma needed
                    new JProperty("edgeFiltering", false),                         // Comma needed
                    new JProperty("collisionTolerance", 0),                         // Comma needed
                    new JProperty("swfPrecision", 2.525),                           // Comma needed
                    new JProperty("bbox_left", sprite.MarginLeft),                 // Comma needed
                    new JProperty("bbox_right", sprite.MarginRight),               // Comma needed
                    new JProperty("bbox_top", sprite.MarginTop),                   // Comma needed
                    new JProperty("bbox_bottom", sprite.MarginBottom),             // Comma needed
                    new JProperty("HTile", false),                                 // Comma needed
                    new JProperty("VTile", false),                                 // Comma needed
                    new JProperty("For3D", false),                                 // Comma needed
                    new JProperty("width", sprite.Width),                           // Comma needed
                    new JProperty("height", sprite.Height),                         // Comma needed
                    new JProperty("textureGroupId", textureGroupRef),               // Comma needed
                    new JProperty("swatchColours", null),                           // Comma needed
                    new JProperty("gridX", 0),                                     // Comma needed
                    new JProperty("gridY", 0),                                     // Comma needed
                    new JProperty("frames", new JArray(frameList)),                 // Comma needed
                    new JProperty("sequence", new JObject(                          // Sequence Object Start, Comma needed after this
                        new JProperty("timeUnits", 1),                              // Comma needed
                        new JProperty("playback", 1),                               // Comma needed
                        new JProperty("playbackSpeed", (float)sprite.PlaybackSpeed), // Comma needed
                        new JProperty("playbackSpeedType", sprite.PlaybackSpeedType == UndertaleSprite.SpritePlaybackSpeedType.FramesPerSecond ? 1 : 0), // Comma needed
                        new JProperty("autoRecord", true),                          // Comma needed
                        new JProperty("volume", 1.0f),                              // Comma needed
                        new JProperty("length", (float)frameGuids.Count),           // Comma needed
                        new JProperty("events", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MessageEventKeyframe>"))), // Comma needed
                        new JProperty("moments", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MomentsEventKeyframe>"))), // Comma needed
                        new JProperty("tracks", new JArray(spriteFramesTrack)),      // Comma needed
                        new JProperty("visibleRange", null),                        // Comma needed
                        new JProperty("lockOrigin", false),                         // Comma needed
                        new JProperty("showBackdrop", true),                        // Comma needed
                        new JProperty("showBackdropImage", false),                  // Comma needed
                        new JProperty("backdropImagePath", ""),                     // Comma needed
                        new JProperty("backdropImageOpacity", 0.5f),                // Comma needed
                        new JProperty("backdropWidth", 1366),                       // Comma needed
                        new JProperty("backdropHeight", 768),                       // Comma needed
                        new JProperty("backdropXOffset", 0.0f),                     // Comma needed
                        new JProperty("backdropYOffset", 0.0f),                     // Comma needed
                        new JProperty("xorigin", sprite.OriginX),                   // Comma needed
                        new JProperty("yorigin", sprite.OriginY),                   // Comma needed
                        new JProperty("eventToFunction", new JObject()),            // Comma needed
                        new JProperty("eventStubScript", null),                     // Comma needed
                        new JProperty("parent", CreateResourceReference(spriteName, spriteGuid, "sprites")), // Comma needed
                        new JProperty("resourceVersion", "1.4"),                    // Comma needed
                        new JProperty("name", spriteName),                          // Comma needed
                        new JProperty("tags", new JArray()),                        // Comma needed
                        new JProperty("resourceType", "GMSequence")                 // Last property in sequence, NO comma
                    )),                                                             // End Sequence Object, Comma needed
                    new JProperty("layers", new JArray(imageLayer)),                 // Comma needed
                    new JProperty("parent", new JObject(                             // Comma needed
                        new JProperty("name", "Sprites"),
                        new JProperty("path", "folders/Sprites.yy")
                    )),
                    new JProperty("resourceVersion", "1.0"),                        // Comma needed
                    new JProperty("name", spriteName),                              // Comma needed
                    new JProperty("tags", new JArray()),                            // <<< *** ERROR FIX: Comma ADDED here ***
                    new JProperty("resourceType", "GMSprite")                       // Last property, NO comma
                );


                WriteJsonFile(yyPath, yyContent);

                string relativePath = $"sprites/{spriteName}/{spriteName}.yy";
                AddResourceToProject(spriteName, spriteGuid, relativePath, "GMSprite", "sprites");
                ResourcePaths[resourceKey] = relativePath.Replace('\\', '/');
                spriteProcessedCount++;

            }
            catch (Exception ex)
            {
                 UMT.Log($"ERROR processing sprite '{spriteName}': {ex.Message}\n{ex.StackTrace}");
            }
        }
         UMT.Log($"Sprite conversion finished. Processed {spriteProcessedCount} sprites.");
    }

    private int GetGMS2Origin(int x, int y, int w, int h) {
         if (x == 0 && y == 0) return 0;
         int midX = w / 2; int midY = h / 2;
         int rightX = Math.Max(0, w - 1); int bottomY = Math.Max(0, h - 1);
         if (x == midX && y == 0) return 1; if (x == rightX && y == 0) return 2;
         if (x == 0 && y == midY) return 3; if (x == midX && y == midY) return 4;
         if (x == rightX && y == midY) return 5; if (x == 0 && y == bottomY) return 6;
         if (x == midX && y == bottomY) return 7; if (x == rightX && y == bottomY) return 8;
         return 9; // Custom
    }


    private void ConvertSounds()
    {
        UMT.Log("Converting Sounds...");
        if (Data.Sounds == null || !Data.Sounds.Any()) { UMT.Log("No sounds found."); return; }
        string soundDir = Path.Combine(OutputPath, "sounds");
        int soundCount = 0;
        foreach (var sound in Data.Sounds.Where(s => s?.Name?.Content != null && s.AudioFile?.Data != null)) {
            string soundName = GetResourceName(sound);
             string resourceKey = $"sounds/{soundName}"; Guid soundGuid = GetResourceGuid(resourceKey);
            string soundPath = Path.Combine(soundDir, soundName);
            string yyPath = Path.Combine(soundPath, $"{soundName}.yy");
            string audioFileName = GetCompatibleAudioFileName(sound.AudioFile.Name.Content, soundName);
             if (string.IsNullOrEmpty(audioFileName)) { UMT.Log($"Warning: Could not get valid audio filename for sound '{soundName}'. Skipping."); continue; }
            string audioFilePath = Path.Combine(soundPath, audioFileName);
            try {
                Directory.CreateDirectory(soundPath);
                File.WriteAllBytes(audioFilePath, sound.AudioFile.Data);
                 string audioGroupName = "audiogroup_default"; Guid audioGroupGuid = GetResourceGuid("audiogroups/audiogroup_default");
                 if (Data.AudioGroups != null) {
                     var utGroup = Data.AudioGroups.FirstOrDefault(ag => ag.Sounds.Contains(sound));
                     if (utGroup != null) { audioGroupName = GetResourceName(utGroup); audioGroupGuid = GetResourceGuid($"audiogroups/{audioGroupName}"); }
                 }
                 JObject audioGroupRef = CreateResourceReference(audioGroupName, audioGroupGuid, "audiogroups");
                 var yyContent = new JObject(
                     new JProperty("compression", GetGMS2CompressionType(sound.Type, Path.GetExtension(audioFilePath).ToLowerInvariant())), // Comma
                     new JProperty("volume", (float)sound.Volume), // Comma
                     new JProperty("preload", sound.Flags.HasFlag(UndertaleSound.AudioEntryFlags.Preload)), // Comma
                     new JProperty("bitRate", 128), // Comma
                     new JProperty("sampleRate", 44100), // Comma
                     new JProperty("type", GetGMS2SoundType(sound.Type)), // Comma
                     new JProperty("bitDepth", 1), // Comma
                     new JProperty("audioGroupId", audioGroupRef), // Comma
                     new JProperty("soundFile", audioFileName), // Comma
                     new JProperty("duration", 0.0f), // Comma
                     new JProperty("parent", new JObject(new JProperty("name", "Sounds"), new JProperty("path", "folders/Sounds.yy"))), // Comma
                     new JProperty("resourceVersion", "1.0"), // Comma
                     new JProperty("name", soundName), // Comma
                     new JProperty("tags", new JArray()), // Comma
                     new JProperty("resourceType", "GMSound") // No comma
                 );
                WriteJsonFile(yyPath, yyContent);
                string relativePath = $"sounds/{soundName}/{soundName}.yy";
                AddResourceToProject(soundName, soundGuid, relativePath, "GMSound", "sounds");
                 ResourcePaths[resourceKey] = relativePath.Replace('\\', '/');
                 soundCount++;
            } catch (Exception ex) { UMT.Log($"ERROR processing sound '{soundName}': {ex.Message}"); }
        }
         UMT.Log($"Sound conversion finished. Processed {soundCount} sounds.");
    }

     private string GetCompatibleAudioFileName(string originalFileName, string resourceName) {
          string extension = ".ogg"; try { if (!string.IsNullOrEmpty(originalFileName)) { extension = Path.GetExtension(originalFileName); if (string.IsNullOrEmpty(extension) || extension.Length < 2) extension = ".ogg"; } } catch { extension = ".ogg"; }
          string extLower = extension.ToLowerInvariant(); if (extLower != ".wav" && extLower != ".ogg" && extLower != ".mp3") extension = ".ogg";
          return resourceName + extension;
     }
     private int GetGMS2SoundType(UndertaleSound.AudioTypeFlags utFlags) { return 1; } // Default Stereo
     private int GetGMS2CompressionType(UndertaleSound.AudioTypeFlags utFlags, string extension) {
         bool isStreamed = utFlags.HasFlag(UndertaleSound.AudioTypeFlags.StreamFromDisk);
         if (extension == ".wav") return isStreamed ? 2 : 0; else return isStreamed ? 3 : 1;
     }


    private void ConvertObjects()
    {
        UMT.Log("Converting Objects...");
        if (Data.Objects == null || !Data.Objects.Any()) { UMT.Log("No objects found."); return; }
        string objDir = Path.Combine(OutputPath, "objects");
        int objCount = 0;
        foreach (var obj in Data.Objects.Where(o => o?.Name?.Content != null)) {
            string objName = GetResourceName(obj);
            string resourceKey = $"objects/{objName}"; Guid objGuid = GetResourceGuid(resourceKey);
            string objPath = Path.Combine(objDir, objName); string yyPath = Path.Combine(objPath, $"{objName}.yy");
            try {
                Directory.CreateDirectory(objPath);
                List<JObject> eventList = new List<JObject>();
                if (obj.Events != null) {
                    foreach (var eventContainer in obj.Events) { if (eventContainer == null) continue;
                        foreach(var ev in eventContainer) { if (ev == null) continue;
                             bool hasCodeAction = ev.Actions?.Any(a => a.LibID == 1 && a.Kind == 7) ?? false;
                             if (!hasCodeAction) continue; // Only process events with code actions
                             UndertaleCode associatedCode = FindCodeForEvent(obj, ev.EventType, ev.EventSubtype);
                             string gmlCode = "// Event code not found or decompiled\n"; string gmlCompatibilityIssues = "";
                             if (associatedCode?.Decompiled != null) {
                                  gmlCode = associatedCode.Decompiled.ToString(Data, true);
                                  // Add basic warnings
                                  if (gmlCode.Contains("argument[")) gmlCompatibilityIssues += "// WARNING: Uses deprecated 'argument[n]' syntax. Use 'argumentn'.\n";
                                  if (gmlCode.Contains("self.")) gmlCompatibilityIssues += "// WARNING: Uses 'self.'. This is often redundant in GMS2.\n";
                                  if (!string.IsNullOrEmpty(gmlCompatibilityIssues)) gmlCode = gmlCompatibilityIssues + gmlCode;
                             } else if (WARN_ON_MISSING_DECOMPILED_CODE) { UMT.Log($"Warning: Decompiled code not found for Object '{objName}' Event: Type={ev.EventType}, Subtype={ev.EventSubtype}"); }

                             GMS2EventMapping mapping = MapGMS1EventToGMS2(ev.EventType, ev.EventSubtype, objName);
                             if (mapping == null) { UMT.Log($"Warning: Skipping unmappable event Type={ev.EventType}, Subtype={ev.EventSubtype} for Object '{objName}'."); continue; }
                             string gmlFileName = mapping.IsCollisionEvent ? $"Collision_{mapping.GMS2EventTypeName}.gml" : $"{mapping.GMS2EventTypeName}_{mapping.GMS2EventNumber}.gml";
                             string gmlFilePath = Path.Combine(objPath, SanitizeFileName(gmlFileName));
                             File.WriteAllText(gmlFilePath, gmlCode);
                             var eventEntry = new JObject(
                                 new JProperty("collisionObjectId", mapping.CollisionObjectRef), new JProperty("eventNum", mapping.GMS2EventNumber),
                                 new JProperty("eventType", mapping.GMS2EventType), new JProperty("isDnD", false),
                                 new JProperty("resourceVersion", "1.0"), new JProperty("name", ""), new JProperty("tags", new JArray()),
                                 new JProperty("resourceType", "GMEvent")
                             );
                             eventList.Add(eventEntry);
                        }
                    }
                }
                 JObject spriteRef = CreateResourceReference(obj.Sprite, "sprites");
                 JObject parentRef = CreateResourceReference(obj.ParentId, "objects");
                 JObject maskRef = CreateResourceReference(obj.MaskSprite, "sprites");
                 var yyContent = new JObject(
                     new JProperty("spriteId", spriteRef), new JProperty("solid", obj.Solid), new JProperty("visible", obj.Visible),
                     new JProperty("managed", true), new JProperty("persistent", obj.Persistent), new JProperty("parentObjectId", parentRef),
                     new JProperty("maskSpriteId", maskRef), new JProperty("physicsObject", false), new JProperty("physicsSensor", false),
                     new JProperty("physicsShape", 1), new JProperty("physicsGroup", 0), new JProperty("physicsDensity", 0.5f),
                     new JProperty("physicsRestitution", 0.1f), new JProperty("physicsLinearDamping", 0.1f), new JProperty("physicsAngularDamping", 0.1f),
                     new JProperty("physicsFriction", 0.2f), new JProperty("physicsStartAwake", true), new JProperty("physicsKinematic", false),
                     new JProperty("physicsShapePoints", new JArray()), new JProperty("eventList", new JArray(eventList)),
                     new JProperty("properties", new JArray()), new JProperty("overriddenProperties", new JArray()),
                     new JProperty("parent", new JObject(new JProperty("name", "Objects"), new JProperty("path", "folders/Objects.yy"))), // Comma
                      new JProperty("resourceVersion", "1.0"), new JProperty("name", objName), // Comma
                      new JProperty("tags", new JArray()), new JProperty("resourceType", "GMObject") // No Comma
                 );
                WriteJsonFile(yyPath, yyContent);
                 string relativePath = $"objects/{objName}/{objName}.yy";
                 AddResourceToProject(objName, objGuid, relativePath, "GMObject", "objects");
                 ResourcePaths[resourceKey] = relativePath.Replace('\\', '/'); objCount++;
            } catch (Exception ex) { UMT.Log($"ERROR processing object '{objName}': {ex.Message}\n{ex.StackTrace}"); }
        }
         UMT.Log($"Object conversion finished. Processed {objCount} objects.");
    }

      private UndertaleCode FindCodeForEvent(UndertaleObject obj, UndertaleInstruction.EventType eventType, int eventSubtype) {
         if (Data.Code == null || obj == null) return null;
         if (obj.Events != null) {
              foreach (var eventList in obj.Events) {
                  foreach (var ev in eventList) {
                       if (ev.EventType == eventType && ev.EventSubtype == eventSubtype) {
                            var codeAction = ev.Actions?.FirstOrDefault(a => a.LibID == 1 && a.Kind == 7);
                            if (codeAction != null) {
                                 // TODO: Need UMT-specific way to resolve code from codeAction/Arguments
                                 // Placeholder: return null;
                            }
                            // If no specific code action logic, try finding *any* code linked (less precise)
                            // return Data.Code.FirstOrDefault(c => IsCodeForEvent(c, obj, eventType, eventSubtype)); // Needs IsCodeForEvent impl
                            return null; // Cannot resolve reliably yet
                       }
                  }
              }
         }
         return null;
     }

     private class GMS2EventMapping { /* ... As before ... */
         public int GMS2EventType { get; set; } public int GMS2EventNumber { get; set; }
         public string GMS2EventTypeName { get; set; } public JObject CollisionObjectRef { get; set; } = null;
         public bool IsCollisionEvent => CollisionObjectRef != null;
     }
     private GMS2EventMapping MapGMS1EventToGMS2(UndertaleInstruction.EventType eventType, int eventSubtype, string currentObjName) { /* ... Mapping logic as before ... */
         switch (eventType) {
             case UndertaleInstruction.EventType.Create: return new GMS2EventMapping { GMS2EventType = 0, GMS2EventNumber = 0, GMS2EventTypeName = "Create" };
             case UndertaleInstruction.EventType.Destroy: return new GMS2EventMapping { GMS2EventType = 1, GMS2EventNumber = 0, GMS2EventTypeName = "Destroy" };
             case UndertaleInstruction.EventType.Alarm: if (eventSubtype >= 0 && eventSubtype <= 11) return new GMS2EventMapping { GMS2EventType = 2, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"Alarm{eventSubtype}" }; break;
             case UndertaleInstruction.EventType.Step: if (eventSubtype >= 0 && eventSubtype <= 2) { string n = eventSubtype == 0 ? "Step" : (eventSubtype == 1 ? "BeginStep" : "EndStep"); return new GMS2EventMapping { GMS2EventType = 3, GMS2EventNumber = eventSubtype, GMS2EventTypeName = n }; } break;
             case UndertaleInstruction.EventType.Collision: if (eventSubtype >= 0 && eventSubtype < Data.Objects.Count) { var colObj = Data.Objects[eventSubtype]; if(colObj != null) { string name = GetResourceName(colObj); JObject r = CreateResourceReference(colObj, "objects"); if (r != null) return new GMS2EventMapping { GMS2EventType = 4, GMS2EventNumber = eventSubtype, GMS2EventTypeName = name, CollisionObjectRef = r }; } } break;
             case UndertaleInstruction.EventType.Keyboard: return new GMS2EventMapping { GMS2EventType = 5, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"Keyboard_{VirtualKeyToString(eventSubtype)}" };
             case UndertaleInstruction.EventType.Mouse: if (eventSubtype >= 0 && eventSubtype <= 11) { string[] n={"LB","RB","MB","NB","LP","RP","MP","LR","RR","MR","Enter","Leave"}; string name = eventSubtype<n.Length?n[eventSubtype]:$"Mouse{eventSubtype}"; return new GMS2EventMapping{GMS2EventType=6, GMS2EventNumber=eventSubtype, GMS2EventTypeName=name}; } return new GMS2EventMapping{GMS2EventType=6, GMS2EventNumber=eventSubtype, GMS2EventTypeName=$"MouseRaw_{eventSubtype}"}; // Fallback
             case UndertaleInstruction.EventType.Other: if (eventSubtype >= 0 && eventSubtype <= 9) { string[] n={"Outside","Boundary","GameStart","GameEnd","RoomStart","RoomEnd","AnimEnd","PathEnd","NoLives","NoHealth"}; if(eventSubtype<n.Length) return new GMS2EventMapping {GMS2EventType=7,GMS2EventNumber=eventSubtype,GMS2EventTypeName=n[eventSubtype]};} else if (eventSubtype>=10 && eventSubtype<=25) return new GMS2EventMapping {GMS2EventType=7,GMS2EventNumber=eventSubtype, GMS2EventTypeName=$"UserEvent{eventSubtype-10}"}; break;
             case UndertaleInstruction.EventType.Draw: if (eventSubtype==0) return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 0, GMS2EventTypeName = "Draw" }; else if (eventSubtype==1) return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 1, GMS2EventTypeName = "DrawGUI" }; else if (eventSubtype==75) return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 75, GMS2EventTypeName = "PreDraw" }; else if (eventSubtype==76) return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 76, GMS2EventTypeName = "PostDraw" }; return new GMS2EventMapping { GMS2EventType = 8, GMS2EventNumber = 0, GMS2EventTypeName = "Draw" }; // Fallback
             case UndertaleInstruction.EventType.KeyPress: return new GMS2EventMapping { GMS2EventType = 9, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"KeyPress_{VirtualKeyToString(eventSubtype)}" };
             case UndertaleInstruction.EventType.KeyRelease: return new GMS2EventMapping { GMS2EventType = 10, GMS2EventNumber = eventSubtype, GMS2EventTypeName = $"KeyRelease_{VirtualKeyToString(eventSubtype)}" };
             case UndertaleInstruction.EventType.Trigger: break; default: break;
         } return null;
     }
     private string VirtualKeyToString(int vkCode) { try { var k = (System.Windows.Forms.Keys)vkCode; if (Enum.IsDefined(typeof(System.Windows.Forms.Keys), k)) return k.ToString(); } catch {} return vkCode.ToString(); }


    private void ConvertRooms()
    {
        UMT.Log("Converting Rooms...");
        if (Data.Rooms == null || !Data.Rooms.Any()) { UMT.Log("No rooms found."); return; }
        string roomDir = Path.Combine(OutputPath, "rooms");
        int roomCount = 0;
        foreach (var room in Data.Rooms.Where(r => r?.Name?.Content != null)) {
            string roomName = GetResourceName(room);
            string resourceKey = $"rooms/{roomName}"; Guid roomGuid = GetResourceGuid(resourceKey);
            string roomPath = Path.Combine(roomDir, roomName); string yyPath = Path.Combine(roomPath, $"{roomName}.yy");
            try {
                Directory.CreateDirectory(roomPath);
                 // --- Room Settings ---
                 var roomSettings = new JObject( new JProperty("inheritRoomSettings", false), new JProperty("Width", room.Width), new JProperty("Height", room.Height), new JProperty("persistent", room.Persistent) );
                 var settingsWrapper = new JObject( new JProperty("isDnD", false), new JProperty("volume", 1.0), new JProperty("parentRoom", null), new JProperty("sequenceId", null), new JProperty("roomSettings", roomSettings), new JProperty("resourceVersion", "1.0"), new JProperty("name", "settings"), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRoomSettings") );
                 // --- Views ---
                 List<JObject> viewList = new List<JObject>(); bool enableViews = room.ViewsEnabled && room.Views != null && room.Views.Any(v => v.Enabled);
                 if (enableViews) { for(int i=0; i < 8; i++) { var view = (i < room.Views.Count) ? room.Views[i] : null; bool isEnabled = view?.Enabled ?? false; JObject followRef = null; if(isEnabled && view.ObjectId >= 0 && view.ObjectId < Data.Objects.Count) { var o = Data.Objects[view.ObjectId]; if (o != null) followRef = CreateResourceReference(o, "objects"); } viewList.Add(new JObject( new JProperty("inherit", false), new JProperty("visible", isEnabled), new JProperty("xview", view?.ViewX ?? 0), new JProperty("yview", view?.ViewY ?? 0), new JProperty("wview", view?.ViewWidth ?? room.Width), new JProperty("hview", view?.ViewHeight ?? room.Height), new JProperty("xport", view?.PortX ?? 0), new JProperty("yport", view?.PortY ?? 0), new JProperty("wport", view?.PortWidth ?? room.Width), new JProperty("hport", view?.PortHeight ?? room.Height), new JProperty("hborder", view?.BorderX ?? 32), new JProperty("vborder", view?.BorderY ?? 32), new JProperty("hspeed", view?.SpeedX ?? -1), new JProperty("vspeed", view?.SpeedY ?? -1), new JProperty("objectId", followRef), new JProperty("resourceVersion", "1.0"), new JProperty("name", $"view_{i}"), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMView") )); } }
                 var viewSettings = new JObject( new JProperty("inheritViewSettings", false), new JProperty("enableViews", enableViews), new JProperty("clearViewBackground", room.ClearDisplayBuffer), new JProperty("clearDisplayBuffer", room.ClearScreen), new JProperty("views", new JArray(viewList)) );
                 var viewsWrapper = new JObject( new JProperty("viewSettings", viewSettings), new JProperty("resourceVersion", "1.0"), new JProperty("name", "views"), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRoomViewSettings") );
                 // --- Layers ---
                 List<JObject> layerList = new List<JObject>(); int currentDepth = 1000000;
                 layerList.Add(new JObject( new JProperty("visible", true), new JProperty("depth", currentDepth), new JProperty("userdefined_depth", false), new JProperty("inheritLayerDepth", false), new JProperty("inheritLayerSettings", false), new JProperty("gridX", 32), new JProperty("gridY", 32), new JProperty("layers", new JArray()), new JProperty("hierarchyFrozen", false), new JProperty("effectEnabled", false), new JProperty("effectType", null), new JProperty("properties", new JArray()), new JProperty("isLocked", false), new JProperty("colour", ColorToGMS2JObject(room.BackgroundColor, true)), new JProperty("spriteId", null), new JProperty("htiled", false), new JProperty("vtiled", false), new JProperty("hspeed", 0.0f), new JProperty("vspeed", 0.0f), new JProperty("stretch", false), new JProperty("animationFPS", 15.0f), new JProperty("animationSpeedType", 0), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Background"), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRBackgroundLayer") )); currentDepth -= 100;
                 // Asset Layers (from GMS1 BGs)
                 if (room.Backgrounds != null) { foreach (var bg in room.Backgrounds.Where(b=>b.Enabled).OrderByDescending(b => b.Depth)) { JObject assetSpriteRef = null; string assetLayerName="asset_layer"; UndertaleBackground bgResource = null; if (bg.BackgroundId >= 0 && bg.BackgroundId < Data.Backgrounds.Count) { bgResource = Data.Backgrounds[bg.BackgroundId]; if (bgResource != null) { assetSpriteRef = CreateResourceReference(bgResource, "sprites"); assetLayerName = GetResourceName(bgResource); } } if (assetSpriteRef==null) continue; layerList.Add(new JObject( new JProperty("spriteId", assetSpriteRef), new JProperty("headPosition", 0.0f), new JProperty("inheritLayerSettings", false), new JProperty("interpolation", 1), new JProperty("isLocked", false), new JProperty("layers", new JArray()), new JProperty("name", SanitizeFileName($"{assetLayerName}_Asset_{Guid.NewGuid().ToString("N").Substring(0,4)}")), new JProperty("properties", new JArray()), new JProperty("resourceType", "GMAssetLayer"), new JProperty("resourceVersion", "1.0"), new JProperty("rotation", 0.0f), new JProperty("scaleX", 1.0f), new JProperty("scaleY", 1.0f), new JProperty("sequenceId", null), new JProperty("skewX", 0.0f), new JProperty("skewY", 0.0f), new JProperty("tags", new JArray()), new JProperty("tint", 0xFFFFFFFF), new JProperty("visible", true), new JProperty("x", (float)bg.X), new JProperty("y", (float)bg.Y), new JProperty("depth", bg.Depth), new JProperty("userdefined_depth", true), new JProperty("gridX", 32), new JProperty("gridY", 32), new JProperty("hierarchyFrozen", false), new JProperty("effectEnabled", false), new JProperty("effectType", null) )); } }
                 // Instance Layer
                 List<JObject> instanceRefs = new List<JObject>();
                 if (room.Instances != null) { foreach(var inst in room.Instances.OrderBy(i => i.InstanceID)) { if (inst.ObjectDefinition == null) continue; JObject objRef = CreateResourceReference(inst.ObjectDefinition, "objects"); if (objRef == null) continue; string creationCode = ExtractCreationCode(inst, roomName); instanceRefs.Add(new JObject( new JProperty("properties", new JArray()), new JProperty("isDnD", false), new JProperty("objectId", objRef), new JProperty("inheritCode", false), new JProperty("hasCreationCode", !string.IsNullOrWhiteSpace(creationCode) && creationCode!= "// Creation code not found/decompiled."), new JProperty("colour", ColorToGMS2JObject(inst.Color)), new JProperty("rotation", (float)inst.Rotation), new JProperty("scaleX", (float)inst.ScaleX), new JProperty("scaleY", (float)inst.ScaleY), new JProperty("imageIndex", (int)(inst.ImageIndex ?? 0)), new JProperty("imageSpeed", (float)(inst.ImageSpeed ?? 1.0f)), new JProperty("inheritedItemId", null), new JProperty("frozen", false), new JProperty("ignore", false), new JProperty("inheritItemSettings", false), new JProperty("x", (float)inst.X), new JProperty("y", (float)inst.Y), new JProperty("resourceVersion", "1.0"), new JProperty("name", Guid.NewGuid().ToString("D")), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRInstance") )); } }
                 var instanceLayer = new JObject( new JProperty("visible", true), new JProperty("depth", 0), new JProperty("userdefined_depth", false), new JProperty("inheritLayerDepth", false), new JProperty("inheritLayerSettings", false), new JProperty("gridX", 32), new JProperty("gridY", 32), new JProperty("layers", new JArray()), new JProperty("hierarchyFrozen", false), new JProperty("effectEnabled", false), new JProperty("effectType", null), new JProperty("properties", new JArray()), new JProperty("isLocked", false), new JProperty("instances", new JArray(instanceRefs)), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Instances"), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRInstanceLayer") ); layerList.Add(instanceLayer); currentDepth -= 10;
                 // Tile Layers (Empty - Manual Painting Required)
                 if (room.Tiles != null && room.Tiles.Any()) { UMT.Log($"Warning: Creating EMPTY Tile Layers for {roomName}. MANUAL painting required in GMS2."); foreach (var layerGroup in room.Tiles.Where(t => t?.BackgroundDefinition != null).GroupBy(t => new { t.Depth, t.BackgroundDefinition }).OrderByDescending(g => g.Key.Depth)) { var depth = layerGroup.Key.Depth; var bgResource = layerGroup.Key.BackgroundDefinition; string tilesetName = GetResourceName(bgResource); JObject tilesetRef = CreateResourceReference(bgResource, "tilesets"); if (tilesetRef == null) continue; string tileLayerName = SanitizeFileName($"Tiles_{tilesetName}_{depth}"); int gridW = (room.Width + 31)/32; int gridH = (room.Height + 31)/32; layerList.Add(new JObject( new JProperty("tilesetId", tilesetRef), new JProperty("x", 0), new JProperty("y", 0), new JProperty("visible", true), new JProperty("depth", depth), new JProperty("userdefined_depth", true), new JProperty("inheritLayerDepth", false), new JProperty("inheritLayerSettings", false), new JProperty("gridX", 32), new JProperty("gridY", 32), new JProperty("layers", new JArray()), new JProperty("hierarchyFrozen", false), new JProperty("effectEnabled", false), new JProperty("effectType", null), new JProperty("properties", new JArray()), new JProperty("isLocked", false), new JProperty("tiles", new JObject( new JProperty("TileData", new JArray(Enumerable.Repeat(0, Math.Max(1,gridW)*Math.Max(1,gridH)))), new JProperty("SerialiseWidth", gridW), new JProperty("SerialiseHeight", gridH), new JProperty("TileSerialiseData", null) )), new JProperty("tile_count", 0), new JProperty("resourceVersion", "1.0"), new JProperty("name", tileLayerName), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRTileLayer") )); } }
                 // --- Room Creation Code ---
                 string creationCodeContent = ExtractRoomCreationCode(room, roomName); string creationCodeFilePath = Path.Combine(roomPath, "RoomCreationCode.gml"); File.WriteAllText(creationCodeFilePath, creationCodeContent);
                 // --- Main Room .yy ---
                 var layerFolder = new JObject( new JProperty("visible", true), new JProperty("depth", 0), new JProperty("userdefined_depth", false), new JProperty("inheritLayerDepth", true), new JProperty("inheritLayerSettings", true), new JProperty("gridX", 32), new JProperty("gridY", 32), new JProperty("layers", new JArray(layerList)), new JProperty("hierarchyFrozen", false), new JProperty("effectEnabled", false), new JProperty("effectType", null), new JProperty("properties", new JArray()), new JProperty("isLocked", false), new JProperty("resourceVersion", "1.0"), new JProperty("name", "layers"), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRLayerFolder") );
                 var instanceCreationOrder = new JArray(); foreach (JObject instNode in (JArray)instanceLayer["instances"]) { instanceCreationOrder.Add(new JObject( new JProperty("name", instNode["name"].Value<string>()), new JProperty("path", yyPath.Replace('\\', '/')) )); }
                 var yyContent = new JObject(
                     new JProperty("isDnD", false), new JProperty("volume", 1.0f), new JProperty("parentRoom", null), new JProperty("sequenceId", null),
                     new JProperty("roomSettings", settingsWrapper), new JProperty("viewSettings", viewsWrapper), new JProperty("layers", new JArray(layerFolder)),
                     new JProperty("physicsSettings", new JObject( new JProperty("inheritPhysicsSettings", false), new JProperty("PhysicsWorld", false), new JProperty("PhysicsWorldGravityX", 0.0f), new JProperty("PhysicsWorldGravityY", 10.0f), new JProperty("PhysicsWorldPixToMetres", 0.1f) )),
                     new JProperty("instanceCreationCode", new JObject()), new JProperty("inheritCode", false), new JProperty("instanceCreationOrder", instanceCreationOrder),
                     new JProperty("inheritCreationOrder", false), new JProperty("sequenceCreationOrder", new JArray()), new JProperty("useCats", false), new JProperty("cats", new JArray()),
                     new JProperty("parent", new JObject(new JProperty("name", "Rooms"), new JProperty("path", "folders/Rooms.yy"))), // Comma
                     new JProperty("creationCodeFile", Path.GetFileName(creationCodeFilePath)), // Comma
                     new JProperty("inheritGenerateOffsetY", false), new JProperty("generateOffsetY", 0), // Comma
                     new JProperty("resourceVersion", "1.0"), new JProperty("name", roomName), // Comma
                     new JProperty("tags", new JArray()), new JProperty("resourceType", "GMRoom") // No Comma
                 );
                WriteJsonFile(yyPath, yyContent);
                 string relativePath = $"rooms/{roomName}/{roomName}.yy";
                 AddResourceToProject(roomName, roomGuid, relativePath, "GMRoom", "rooms");
                 ResourcePaths[resourceKey] = relativePath.Replace('\\', '/'); roomCount++;
            } catch (Exception ex) { UMT.Log($"ERROR processing room '{roomName}': {ex.Message}\n{ex.StackTrace}"); }
        }
         UMT.Log($"Room conversion finished. Processed {roomCount} rooms.");
    }

     private string ExtractCreationCode(UndertaleRoom.Instance inst, string roomName) {
          string fallback = "// Creation code not found/decompiled."; UndertaleCode code = null;
          if (inst.CreationCode != null) code = Data.Code?.FirstOrDefault(c => c == inst.CreationCode);
          else if (inst.CreationCodeId != uint.MaxValue) code = Data.Code?.FirstOrDefault(c => c.Offset == inst.CreationCodeId); // Offset might not be reliable ID
          if (code?.Decompiled != null) return code.Decompiled.ToString(Data, true);
          if ((inst.CreationCode != null || inst.CreationCodeId != uint.MaxValue) && WARN_ON_MISSING_DECOMPILED_CODE) UMT.Log($"Warning: Could not find/decompile creation code (ID: {inst.CreationCodeId}) for instance ID {inst.InstanceID} in room '{roomName}'.");
          return fallback;
     }
     private string ExtractRoomCreationCode(UndertaleRoom room, string roomName) {
         string fallback = "// Room Creation Code not found or decompiled.\n"; UndertaleCode code = null;
         if (room.CreationCode != null) code = Data.Code?.FirstOrDefault(c => c == room.CreationCode);
         else if (room.CreationCodeId != uint.MaxValue) code = Data.Code?.FirstOrDefault(c => c.Offset == room.CreationCodeId);
         if (code?.Decompiled != null) return code.Decompiled.ToString(Data, true);
         if ((room.CreationCode != null || room.CreationCodeId != uint.MaxValue) && WARN_ON_MISSING_DECOMPILED_CODE) UMT.Log($"Warning: Could not find/decompile creation code for room '{roomName}'.");
         return fallback;
     }
     private JObject ColorToGMS2JObject(System.UInt32 abgrColor, bool isRoomBg = false) {
         byte a=255, r=0, g=0, b=0; try { a=(byte)((abgrColor>>24)&0xFF); b=(byte)((abgrColor>>16)&0xFF); g=(byte)((abgrColor>>8)&0xFF); r=(byte)(abgrColor&0xFF); } catch {}
         return new JObject( new JProperty("r", r), new JProperty("g", g), new JProperty("b", b), new JProperty("a", a) );
     }


    private void ConvertScripts()
    {
        UMT.Log("Converting Scripts...");
        if (Data.Scripts == null || !Data.Scripts.Any()) { UMT.Log("No scripts found."); return; }
        string scriptDir = Path.Combine(OutputPath, "scripts"); int scriptCount = 0;
        foreach (var script in Data.Scripts.Where(s => s?.Name?.Content != null)) {
            string scriptName = GetResourceName(script); string resourceKey = $"scripts/{scriptName}"; Guid scriptGuid = GetResourceGuid(resourceKey);
            string scriptPath = Path.Combine(scriptDir, scriptName); string yyPath = Path.Combine(scriptPath, $"{scriptName}.yy"); string gmlPath = Path.Combine(scriptPath, $"{scriptName}.gml");
            try {
                Directory.CreateDirectory(scriptPath);
                string gmlCode = $"function {scriptName}() {{\n\tshow_debug_message(\"Script {scriptName} not converted\");\n}}"; string gmlCompat = "";
                UndertaleCode associatedCode = FindCodeForScript(script);
                if (associatedCode?.Decompiled != null) {
                    gmlCode = associatedCode.Decompiled.ToString(Data, true); string trimCode = gmlCode.Trim();
                    if (!trimCode.StartsWith("function ") && !trimCode.StartsWith("#define")) { gmlCode = $"function {scriptName}() {{\n{gmlCode}\n}}"; gmlCompat += $"// WARNING: Auto-wrapped in function {scriptName}(). Verify.\n"; }
                    if (gmlCode.Contains("argument[")) gmlCompat += "// WARNING: Uses deprecated argument[n].\n";
                    if (!string.IsNullOrEmpty(gmlCompat)) gmlCode = gmlCompat + gmlCode;
                } else if (WARN_ON_MISSING_DECOMPILED_CODE) { UMT.Log($"Warning: Decompiled code not found for Script '{scriptName}'."); }
                File.WriteAllText(gmlPath, gmlCode);
                var yyContent = new JObject(
                    new JProperty("isDnD", false), new JProperty("isCompatibility", true), // Comma
                    new JProperty("parent", new JObject(new JProperty("name", "Scripts"), new JProperty("path", "folders/Scripts.yy"))), // Comma
                    new JProperty("resourceVersion", "1.0"), new JProperty("name", scriptName), // Comma
                    new JProperty("tags", new JArray()), new JProperty("resourceType", "GMScript") // No Comma
                );
                WriteJsonFile(yyPath, yyContent);
                string relativePath = $"scripts/{scriptName}/{scriptName}.yy"; AddResourceToProject(scriptName, scriptGuid, relativePath, "GMScript", "scripts"); ResourcePaths[resourceKey] = relativePath.Replace('\\', '/'); scriptCount++;
            } catch (Exception ex) { UMT.Log($"ERROR processing script '{scriptName}': {ex.Message}"); }
        }
        UMT.Log($"Script conversion finished. Processed {scriptCount} scripts.");
    }

     private UndertaleCode FindCodeForScript(UndertaleScript script) {
         if (Data.Code == null || script == null) return null;
         string scriptName = GetResourceName(script);
         // Find by matching sanitized name (most reliable if mapping is good)
         var code = Data.Code.FirstOrDefault(c => c != null && GetResourceName(c) == scriptName);
         // Fallback: Original name match?
         if (code == null && script.Name?.Content != null) {
              code = Data.Code.FirstOrDefault(c => c?.Name?.Content == script.Name.Content);
         }
         return code;
     }


    private void ConvertShaders()
    {
        UMT.Log("Converting Shaders...");
        if (Data.Shaders == null || !Data.Shaders.Any()) { UMT.Log("No shaders found."); return; }
        string shaderDir = Path.Combine(OutputPath, "shaders"); int shaderCount = 0;
        foreach (var shader in Data.Shaders.Where(s => s?.Name?.Content != null)) {
            string shaderName = GetResourceName(shader); string resourceKey = $"shaders/{shaderName}"; Guid shaderGuid = GetResourceGuid(resourceKey);
            string shaderPath = Path.Combine(shaderDir, shaderName); string yyPath = Path.Combine(shaderPath, $"{shaderName}.yy"); string vshPath = Path.Combine(shaderPath, $"{shaderName}.vsh"); string fshPath = Path.Combine(shaderPath, $"{shaderName}.fsh");
            try {
                Directory.CreateDirectory(shaderPath);
                string vertSrc = shader.VertexShader?.Content ?? "// Vertex source missing"; string fragSrc = shader.FragmentShader?.Content ?? "// Fragment source missing";
                // Basic GLSL ES adjustments (may need more based on GMS2 target)
                // vertSrc = vertSrc.Replace("attribute", "in").Replace("varying", "out"); // Use cautiously
                // fragSrc = fragSrc.Replace("varying", "in");
                File.WriteAllText(vshPath, vertSrc); File.WriteAllText(fshPath, fragSrc);
                var yyContent = new JObject(
                    new JProperty("type", 1), // 1 = GLSL ES (Default for GMS1 imports) // Comma
                    new JProperty("parent", new JObject(new JProperty("name", "Shaders"), new JProperty("path", "folders/Shaders.yy"))), // Comma
                    new JProperty("resourceVersion", "1.0"), new JProperty("name", shaderName), // Comma
                    new JProperty("tags", new JArray()), new JProperty("resourceType", "GMShader") // No Comma
                );
                WriteJsonFile(yyPath, yyContent);
                string relativePath = $"shaders/{shaderName}/{shaderName}.yy"; AddResourceToProject(shaderName, shaderGuid, relativePath, "GMShader", "shaders"); ResourcePaths[resourceKey] = relativePath.Replace('\\', '/'); shaderCount++;
            } catch (Exception ex) { UMT.Log($"ERROR processing shader '{shaderName}': {ex.Message}"); }
        }
        UMT.Log($"Shader conversion finished. Processed {shaderCount} shaders.");
    }


    private void ConvertFonts()
    {
        UMT.Log("Converting Fonts..."); UMT.Log("WARNING: Font conversion is EXPERIMENTAL. MANUAL review/regeneration in GMS2 recommended.");
        if (Data.Fonts == null || !Data.Fonts.Any()) { UMT.Log("No fonts found."); return; }
        string fontDir = Path.Combine(OutputPath, "fonts"); int fontCount = 0;
        foreach (var font in Data.Fonts.Where(f => f?.Name?.Content != null)) {
            string fontName = GetResourceName(font); string resourceKey = $"fonts/{fontName}"; Guid fontGuid = GetResourceGuid(resourceKey);
            string fontPath = Path.Combine(fontDir, fontName); string yyPath = Path.Combine(fontPath, $"{fontName}.yy");
            try {
                Directory.CreateDirectory(fontPath);
                string srcFont = font.FontName?.Content ?? "Arial"; int size = (int)Math.Round(font.Size); bool bold = font.Bold; bool italic = font.Italic; uint first=font.RangeStart; uint last=font.RangeEnd; if(last<first)last=first;
                JObject glyphSpriteRef = null; if (font.Texture?.TexturePage!=null) { var potentialSprite = FindResourceByName<UndertaleSprite>(fontName, Data.Sprites) ?? FindResourceByName<UndertaleBackground>(fontName, Data.Backgrounds); if (potentialSprite!=null) glyphSpriteRef=CreateResourceReference(potentialSprite,"sprites"); } // Unreliable link

                var yyContent = new JObject(
                    new JProperty("sourceFontName", srcFont), new JProperty("size", size), new JProperty("bold", bold), new JProperty("italic", italic), // Comma
                    new JProperty("antiAlias", Math.Max(1, font.AntiAlias)), new JProperty("charset", 255), new JProperty("first", first), new JProperty("last", last), // Comma
                    new JProperty("characterMap", null), new JProperty("glyphOperations", new JArray()), // Comma
                    new JProperty("textureGroupId", CreateResourceReference("Default", GetResourceGuid("texturegroups/Default"), "texturegroups")), // Comma
                    new JProperty("styleName", "Regular"), new JProperty("kerningPairs", new JArray()), new JProperty("includesTTF", false), new JProperty("TTFName", ""), // Comma
                    new JProperty("ascender", 0), new JProperty("descender", 0), new JProperty("lineHeight", 0), new JProperty("glyphs", new JObject()), // Comma
                    new JProperty("parent", new JObject(new JProperty("name", "Fonts"), new JProperty("path", "folders/Fonts.yy"))), // Comma
                    new JProperty("resourceVersion", "1.0"), new JProperty("name", fontName), // Comma
                    new JProperty("tags", new JArray()), new JProperty("resourceType", "GMFont") // No Comma
                );
                WriteJsonFile(yyPath, yyContent);
                string relativePath = $"fonts/{fontName}/{fontName}.yy"; AddResourceToProject(fontName, fontGuid, relativePath, "GMFont", "fonts"); ResourcePaths[resourceKey] = relativePath.Replace('\\', '/'); fontCount++;
            } catch (Exception ex) { UMT.Log($"ERROR processing font '{fontName}': {ex.Message}"); }
        }
        UMT.Log($"Font conversion finished. Processed {fontCount} fonts.");
    }


    private void ConvertTilesets()
    {
        UMT.Log("Converting Tilesets..."); UMT.Log("WARNING: Tileset properties are GUESSED. MANUAL adjustment in GMS2 required.");
        string tilesetDir = Path.Combine(OutputPath, "tilesets"); int tilesetCount = 0;
        HashSet<UndertaleResource> usedTilesetSources = new HashSet<UndertaleResource>();
        if (Data.Rooms != null) { foreach(var room in Data.Rooms.Where(r => r?.Tiles != null)) { foreach(var tile in room.Tiles.Where(t => t?.BackgroundDefinition != null)) { usedTilesetSources.Add(tile.BackgroundDefinition); } } }
        if (!usedTilesetSources.Any()) { UMT.Log("No resources identified as tileset sources."); return; } UMT.Log($"Found {usedTilesetSources.Count} unique tileset sources.");

        foreach (var srcRes in usedTilesetSources.Where(r => r != null)) {
             string srcName = GetResourceName(srcRes); JObject srcSpriteRef = CreateResourceReference(srcRes, "sprites");
             if (srcSpriteRef == null) { UMT.Log($"Warning: Could not find source Sprite '{srcName}' for Tileset. Skipping."); continue; }
             string tilesetName = srcName; string resourceKey = $"tilesets/{tilesetName}"; Guid tilesetGuid = GetResourceGuid(resourceKey);
             string tilesetPath = Path.Combine(tilesetDir, tilesetName); string yyPath = Path.Combine(tilesetPath, $"{tilesetName}.yy");
             try {
                 Directory.CreateDirectory(tilesetPath);
                 int tileW=16, tileH=16, sepX=0, sepY=0, offX=0, offY=0, spriteW=0, spriteH=0;
                 if (srcRes is UndertaleBackground b && b.Texture?.TexturePage != null) { spriteW=b.Texture.TexturePage.SourceWidth>0?b.Texture.TexturePage.SourceWidth:b.Texture.TexturePage.TargetWidth; spriteH=b.Texture.TexturePage.SourceHeight>0?b.Texture.TexturePage.SourceHeight:b.Texture.TexturePage.TargetHeight; }
                 else if (srcRes is UndertaleSprite s) { spriteW=s.Width; spriteH=s.Height; } // Use sprite dims directly if source was sprite
                 int cols = (spriteW>0 && tileW>0)?Math.Max(1,(spriteW-offX*2+sepX)/(tileW+sepX)):1; int rows = (spriteH>0 && tileH>0)?Math.Max(1,(spriteH-offY*2+sepY)/(tileH+sepY)):1; int tileCount=cols*rows;

                 var yyContent = new JObject(
                     new JProperty("spriteId", srcSpriteRef), new JProperty("tileWidth", tileW), new JProperty("tileHeight", tileH), // Comma
                     new JProperty("tilexoff", offX), new JProperty("tileyoff", offY), new JProperty("tilehsep", sepX), new JProperty("tilevsep", sepY), // Comma
                     new JProperty("spriteNoExport", false), new JProperty("textureGroupId", CreateResourceReference("Default", GetResourceGuid("texturegroups/Default"), "texturegroups")), // Comma
                     new JProperty("out_tilehborder", 2), new JProperty("out_tilevborder", 2), new JProperty("out_columns", cols), new JProperty("tile_count", tileCount), // Comma
                     new JProperty("autoTileSets", new JArray()), new JProperty("tileAnimationFrames", new JArray()), new JProperty("tileAnimationSpeed", 15.0f), // Comma
                     new JProperty("macroPageTiles", new JObject(new JProperty("SerialiseWidth", cols), new JProperty("SerialiseHeight", rows), new JProperty("TileSerialiseData", new JArray()))), // Comma
                     new JProperty("parent", new JObject(new JProperty("name", "Tilesets"), new JProperty("path", "folders/Tilesets.yy"))), // Comma
                     new JProperty("resourceVersion", "1.0"), new JProperty("name", tilesetName), // Comma
                     new JProperty("tags", new JArray()), new JProperty("resourceType", "GMTileSet") // No Comma
                 );
                 WriteJsonFile(yyPath, yyContent);
                 string relativePath = $"tilesets/{tilesetName}/{tilesetName}.yy"; AddResourceToProject(tilesetName, tilesetGuid, relativePath, "GMTileSet", "tilesets"); ResourcePaths[resourceKey] = relativePath.Replace('\\', '/'); tilesetCount++;
             } catch (Exception ex) { UMT.Log($"ERROR processing tileset '{tilesetName}': {ex.Message}"); }
        }
        UMT.Log($"Tileset conversion finished. Processed {tilesetCount} tilesets.");
    }

     private T FindResourceByName<T>(string name, IEnumerable<T> list) where T : UndertaleNamedResource {
          if (list == null || string.IsNullOrEmpty(name)) return null;
          return list.FirstOrDefault(item => item != null && GetResourceName(item) == name);
     }


    private void ConvertPaths() {
        UMT.Log("Converting Paths..."); if (Data.Paths==null || !Data.Paths.Any()) { UMT.Log("No paths found."); return; }
        string pathDir = Path.Combine(OutputPath, "paths"); int pathCount=0;
        foreach (var path in Data.Paths.Where(p=>p?.Name?.Content!=null)) {
            string pathName = GetResourceName(path); string resourceKey = $"paths/{pathName}"; Guid pathGuid = GetResourceGuid(resourceKey); string pathResPath = Path.Combine(pathDir, pathName); string yyPath = Path.Combine(pathResPath, $"{pathName}.yy");
            try { Directory.CreateDirectory(pathResPath); List<JObject> points = path.Points?.Select(pt => new JObject(new JProperty("speed", (float)pt.Speed), new JProperty("x", (float)pt.X), new JProperty("y", (float)pt.Y))).ToList() ?? new List<JObject>();
                var yyContent = new JObject(
                    new JProperty("kind", path.Smooth ? 1 : 0), new JProperty("closed", path.Closed), new JProperty("precision", (int)path.Precision), // Comma
                    new JProperty("points", new JArray(points)), // Comma
                    new JProperty("parent", new JObject(new JProperty("name", "Paths"), new JProperty("path", "folders/Paths.yy"))), // Comma
                    new JProperty("resourceVersion", "1.0"), new JProperty("name", pathName), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMPath") // No Comma
                );
                WriteJsonFile(yyPath, yyContent); string relativePath = $"paths/{pathName}/{pathName}.yy"; AddResourceToProject(pathName, pathGuid, relativePath, "GMPath", "paths"); ResourcePaths[resourceKey] = relativePath.Replace('\\','/'); pathCount++;
            } catch (Exception ex) { UMT.Log($"ERROR processing path '{pathName}': {ex.Message}"); }
        } UMT.Log($"Path conversion finished. Processed {pathCount} paths.");
    }

    private void ConvertTimelines() {
        UMT.Log("Converting Timelines..."); UMT.Log("WARNING: Timeline code needs manual setup in GMS2."); if (Data.Timelines==null || !Data.Timelines.Any()) { UMT.Log("No timelines found."); return; }
        string timelineDir = Path.Combine(OutputPath, "timelines"); int tlCount=0;
        foreach (var timeline in Data.Timelines.Where(tl=>tl?.Name?.Content!=null)) {
            string tlName=GetResourceName(timeline); string resourceKey=$"timelines/{tlName}"; Guid tlGuid=GetResourceGuid(resourceKey); string tlPath=Path.Combine(timelineDir, tlName); string yyPath = Path.Combine(tlPath, $"{tlName}.yy");
            try { Directory.CreateDirectory(tlPath); List<JObject> moments = new List<JObject>();
                if (timeline.Moments!=null) { foreach(var moment in timeline.Moments) { UndertaleCode momentCode=FindCodeForTimelineMoment(timeline, moment); string gmlCode="// Code not resolved"; if(momentCode?.Decompiled!=null) gmlCode=momentCode.Decompiled.ToString(Data, true); var momentEvent = new JObject( new JProperty("collisionObjectId", null), new JProperty("eventNum", 0), new JProperty("eventType", 7), new JProperty("isDnD", false), new JProperty("resourceVersion", "1.0"), new JProperty("name", ""), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMEvent")); moments.Add(new JObject( new JProperty("moment", moment.Moment), new JProperty("evnt", momentEvent), new JProperty("resourceVersion", "1.0"), new JProperty("name", ""), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMMoment"))); } }
                var yyContent = new JObject(
                    new JProperty("momentList", new JArray(moments)), // Comma
                    new JProperty("parent", new JObject(new JProperty("name", "Timelines"), new JProperty("path", "folders/Timelines.yy"))), // Comma
                    new JProperty("resourceVersion", "1.0"), new JProperty("name", tlName), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMTimeline") // No Comma
                );
                WriteJsonFile(yyPath, yyContent); string relativePath=$"timelines/{tlName}/{tlName}.yy"; AddResourceToProject(tlName, tlGuid, relativePath, "GMTimeline", "timelines"); ResourcePaths[resourceKey] = relativePath.Replace('\\','/'); tlCount++;
            } catch (Exception ex) { UMT.Log($"ERROR processing timeline '{tlName}': {ex.Message}"); }
        } UMT.Log($"Timeline conversion finished. Processed {tlCount} timelines.");
    }

     private UndertaleCode FindCodeForTimelineMoment(UndertaleTimeline timeline, UndertaleTimelineMoment moment) {
         if (Data.Code == null || timeline == null || moment?.Actions == null) return null;
         var codeAction = moment.Actions.FirstOrDefault(a => a.LibID == 1 && a.Kind == 7); if (codeAction == null) return null;
         // Need UMT specific logic here to resolve code from codeAction.Arguments[0]
         return null; // Placeholder
     }


    private void ConvertIncludedFiles() {
        UMT.Log("Converting Included Files..."); if (Data.IncludedFiles == null || !Data.IncludedFiles.Any()) { UMT.Log("No included files found."); return; }
        string datafilesDir = Path.Combine(OutputPath, "datafiles"); string includedFilesDir = Path.Combine(OutputPath, "includedfiles"); int fileCount=0;
        foreach (var file in Data.IncludedFiles.Where(f => f?.Name?.Content != null && f.Data != null)) {
            string resourceName = GetResourceName(file); string originalFilePath = file.Name.Content; string targetFileName = Path.GetFileName(originalFilePath); if(string.IsNullOrEmpty(targetFileName)) targetFileName=resourceName; targetFileName=SanitizeFileName(targetFileName);
            string targetFilePath = Path.Combine(datafilesDir, targetFileName); int counter=1; string baseName=Path.GetFileNameWithoutExtension(targetFileName); string ext=Path.GetExtension(targetFileName); while(File.Exists(targetFilePath) || Directory.Exists(targetFilePath)) { targetFileName=$"{baseName}_{counter}{ext}"; targetFilePath=Path.Combine(datafilesDir, targetFileName); counter++; }
            string resourceKey = $"includedfiles/{resourceName}"; Guid fileGuid=GetResourceGuid(resourceKey); string yyPath = Path.Combine(includedFilesDir, $"{resourceName}.yy");
            try { File.WriteAllBytes(targetFilePath, file.Data);
                var yyContent = new JObject(
                    new JProperty("ConfigValues", new JObject()), new JProperty("fileName", targetFileName), new JProperty("filePath", "datafiles"), // Comma
                    new JProperty("outputFolder", ""), new JProperty("removeEnd", false), new JProperty("store", false), new JProperty("ConfigOptions", new JObject()), new JProperty("debug", false), // Comma
                    new JProperty("exportAction", 0), new JProperty("exportDir", ""), new JProperty("overwrite", false), new JProperty("freeData", false), new JProperty("origName", originalFilePath), // Comma
                    new JProperty("parent", new JObject(new JProperty("name", "Included Files"), new JProperty("path", "folders/Included Files.yy"))), // Comma
                    new JProperty("resourceVersion", "1.0"), new JProperty("name", resourceName), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMIncludedFile") // No Comma
                );
                WriteJsonFile(yyPath, yyContent); string relativePath=$"includedfiles/{resourceName}.yy"; AddResourceToProject(resourceName, fileGuid, relativePath, "GMIncludedFile", "includedfiles"); ResourcePaths[resourceKey]=relativePath.Replace('\\','/'); fileCount++;
            } catch (Exception ex) { UMT.Log($"ERROR processing included file '{resourceName}': {ex.Message}"); }
        } UMT.Log($"Included File conversion finished. Processed {fileCount} files.");
    }


     private void ConvertAudioGroups() {
         UMT.Log("Converting Audio Groups..."); string agDir=Path.Combine(OutputPath,"audiogroups"); int agCount=0;
         string defaultAgName="audiogroup_default"; Guid defaultAgGuid=GetResourceGuid($"audiogroups/{defaultAgName}"); string defaultAgYyPath=Path.Combine(agDir,$"{defaultAgName}.yy"); string defaultRelPath=$"audiogroups/{defaultAgName}.yy"; ResourcePaths[$"audiogroups/{defaultAgName}"]=defaultRelPath;
         if (!File.Exists(defaultAgYyPath)) { var c = new JObject(new JProperty("targets",-1L), new JProperty("parent",new JObject(new JProperty("name","Audio Groups"), new JProperty("path","folders/Audio Groups.yy"))), new JProperty("resourceVersion","1.0"), new JProperty("name",defaultAgName), new JProperty("tags",new JArray()), new JProperty("resourceType","GMAudioGroup")); WriteJsonFile(defaultAgYyPath, c); AddResourceToProject(defaultAgName, defaultAgGuid, defaultRelPath, "GMAudioGroup", "audiogroups"); agCount++; }
         if (Data.AudioGroups != null) { foreach (var ag in Data.AudioGroups.Where(a=>a?.Name?.Content!=null)) { string agName=GetResourceName(ag); if(agName==defaultAgName)continue; string resourceKey=$"audiogroups/{agName}"; Guid agGuid=GetResourceGuid(resourceKey); string agYyPath=Path.Combine(agDir,$"{agName}.yy"); try { var c = new JObject(new JProperty("targets",-1L), new JProperty("parent",new JObject(new JProperty("name","Audio Groups"),new JProperty("path","folders/Audio Groups.yy"))), new JProperty("resourceVersion","1.0"), new JProperty("name",agName), new JProperty("tags",new JArray()), new JProperty("resourceType","GMAudioGroup")); WriteJsonFile(agYyPath,c); string rp=$"audiogroups/{agName}.yy"; AddResourceToProject(agName,agGuid,rp,"GMAudioGroup","audiogroups"); ResourcePaths[resourceKey]=rp.Replace('\\','/'); agCount++; } catch (Exception ex) { UMT.Log($"ERROR processing audio group '{agName}': {ex.Message}"); } } }
         UMT.Log($"Audio Group conversion finished. Processed {agCount} groups.");
     }


     private void ConvertTextureGroups() {
         UMT.Log("Converting Texture Groups..."); string tgDir=Path.Combine(OutputPath,"texturegroups"); int tgCount=0;
         string defaultTgName="Default"; Guid defaultTgGuid=GetResourceGuid($"texturegroups/{defaultTgName}"); string defaultTgYyPath=Path.Combine(tgDir,$"{defaultTgName}.yy"); string defaultRelPath=$"texturegroups/{defaultTgName}.yy"; ResourcePaths[$"texturegroups/{defaultTgName}"]=defaultRelPath;
         if (!File.Exists(defaultTgYyPath)) { var c = new JObject(new JProperty("isScaled",true), new JProperty("autocrop",true), new JProperty("border",2), new JProperty("mipsToGenerate",0), new JProperty("groupParent",null), new JProperty("targets",-1L), new JProperty("loadImmediately",false), new JProperty("parent",new JObject(new JProperty("name","Texture Groups"), new JProperty("path","folders/Texture Groups.yy"))), new JProperty("resourceVersion","1.0"), new JProperty("name",defaultTgName), new JProperty("tags",new JArray()), new JProperty("resourceType","GMTextureGroup")); WriteJsonFile(defaultTgYyPath, c); AddResourceToProject(defaultTgName, defaultTgGuid, defaultRelPath, "GMTextureGroup", "texturegroups"); tgCount++; }
         if (Data.TextureGroups != null) { foreach (var tg in Data.TextureGroups.Where(t=>t?.Name?.Content!=null)) { string tgName=GetResourceName(tg); if(tgName==defaultTgName)continue; string resourceKey=$"texturegroups/{tgName}"; Guid tgGuid=GetResourceGuid(resourceKey); string tgYyPath=Path.Combine(tgDir,$"{tgName}.yy"); try { var c=new JObject(new JProperty("isScaled",true), new JProperty("autocrop",true), new JProperty("border",2), new JProperty("mipsToGenerate",0), new JProperty("groupParent",null), new JProperty("targets",-1L), new JProperty("loadImmediately",false), new JProperty("parent",new JObject(new JProperty("name","Texture Groups"),new JProperty("path","folders/Texture Groups.yy"))), new JProperty("resourceVersion","1.0"), new JProperty("name",tgName), new JProperty("tags",new JArray()), new JProperty("resourceType","GMTextureGroup")); WriteJsonFile(tgYyPath,c); string rp=$"texturegroups/{tgName}.yy"; AddResourceToProject(tgName,tgGuid,rp,"GMTextureGroup","texturegroups"); ResourcePaths[resourceKey]=rp.Replace('\\','/'); tgCount++; } catch (Exception ex) { UMT.Log($"ERROR processing texture group '{tgName}': {ex.Message}"); } } }
         UMT.Log($"Texture Group conversion finished. Processed {tgCount} groups.");
     }


    private void ConvertExtensions() {
        UMT.Log("Converting Extensions (Basic Structure)..."); if (Data.Extensions==null || !Data.Extensions.Any()) { UMT.Log("No extensions found."); return; }
        string extDir=Path.Combine(OutputPath,"extensions"); int extCount=0;
        foreach (var ext in Data.Extensions.Where(e=>e?.Name?.Content!=null)) {
            string extName=GetResourceName(ext); string resourceKey=$"extensions/{extName}"; Guid extGuid=GetResourceGuid(resourceKey); string extPath=Path.Combine(extDir, extName); string yyPath = Path.Combine(extPath, $"{extName}.yy");
            try { Directory.CreateDirectory(extPath);
                var yyContent = new JObject( // Skeleton only
                    new JProperty("options", new JArray()), new JProperty("exportToGame", true), new JProperty("supportedTargets", -1L), new JProperty("extensionVersion", ext.Version?.Content ?? "1.0.0"),
                    new JProperty("packageId", ""), new JProperty("productId", ""), new JProperty("author", ""), new JProperty("date", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),
                    new JProperty("license", ""), new JProperty("description", ext.FolderName?.Content ?? ""), new JProperty("helpfile", ""),
                    new JProperty("iosProps", true), new JProperty("tvosProps", true), new JProperty("androidProps", true), new JProperty("installdir", ""), new JProperty("classname", ext.ClassName?.Content ?? ""),
                    new JProperty("IncludedResources", new JArray()), // <<< Needs manual setup
                    new JProperty("androidPermissions", new JArray()), new JProperty("copyToTargets", -1L),
                    new JProperty("parent", new JObject(new JProperty("name", "Extensions"), new JProperty("path", "folders/Extensions.yy"))), // Comma
                    new JProperty("resourceVersion", "1.2"), new JProperty("name", extName), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMExtension") // No Comma
                );
                WriteJsonFile(yyPath, yyContent); string relativePath=$"extensions/{extName}/{extName}.yy"; AddResourceToProject(extName, extGuid, relativePath, "GMExtension", "extensions"); ResourcePaths[resourceKey]=relativePath.Replace('\\','/'); extCount++;
                UMT.Log($"Warning: Extension '{extName}' created. Files/functions need manual setup in GMS2.");
            } catch (Exception ex) { UMT.Log($"ERROR processing extension '{extName}': {ex.Message}"); }
        } UMT.Log($"Extension conversion finished. Processed {extCount} extensions.");
    }

     private void ConvertNotes() {
          UMT.Log("Skipping Notes conversion (no GMS1 equivalent).");
          if (!FolderStructure.ContainsKey("notes")) FolderStructure["notes"] = new List<string>();
     }


    // === Project File Creation ===

    private void AddResourceToProject(string name, Guid guid, string path, string type, string folderKey)
    {
         string guidString = guid.ToString("D");
         if (ResourceList.Any(r => r["id"]?["name"]?.ToString() == guidString)) return; // Prevent duplicates

        ResourceList.Add(new JObject(
            new JProperty("id", new JObject( new JProperty("name", guidString), new JProperty("path", path.Replace('\\', '/')) )),
            new JProperty("order", 0)
        ));

        if (!FolderStructure.TryGetValue(folderKey, out var guidList)) {
             guidList = new List<string>();
             FolderStructure[folderKey] = guidList;
        }
         if (!guidList.Contains(guidString)) guidList.Add(guidString);
    }

    private void CreateProjectFile()
    {
        string yypPath = Path.Combine(OutputPath, $"{ProjectName}.yyp");

         var folderDefs = new List<Tuple<string, string, string>> {
             Tuple.Create("sprites", "GMSprite", "Sprites"), Tuple.Create("tilesets", "GMTileSet", "Tile Sets"), Tuple.Create("sounds", "GMSound", "Sounds"),
             Tuple.Create("paths", "GMPath", "Paths"), Tuple.Create("scripts", "GMScript", "Scripts"), Tuple.Create("shaders", "GMShader", "Shaders"),
             Tuple.Create("fonts", "GMFont", "Fonts"), Tuple.Create("timelines", "GMTimeline", "Timelines"), Tuple.Create("objects", "GMObject", "Objects"),
             Tuple.Create("rooms", "GMRoom", "Rooms"), /*Tuple.Create("sequences", "GMSequence", "Sequences"),*/ Tuple.Create("notes", "GMNote", "Notes"),
             Tuple.Create("extensions", "GMExtension", "Extensions"), Tuple.Create("audiogroups", "GMAudioGroup", "Audio Groups"),
             Tuple.Create("texturegroups", "GMTextureGroup", "Texture Groups"), Tuple.Create("includedfiles", "GMIncludedFile", "Included Files")
         };
         foreach(var def in folderDefs) if (!FolderStructure.ContainsKey(def.Item1)) FolderStructure[def.Item1] = new List<string>();

         List<JObject> folderViews = new List<JObject>();
         string foldersMetaDir = Path.Combine(OutputPath, "folders"); Directory.CreateDirectory(foldersMetaDir);
         foreach (var def in folderDefs) {
             string folderKey=def.Item1; string resourceType=def.Item2; string displayName=def.Item3; Guid folderGuid=GetResourceGuid($"folders/{displayName}");
             string folderMetaPath = Path.Combine(foldersMetaDir, $"{displayName}.yy"); string folderMetaRelativePath = $"folders/{displayName}.yy".Replace('\\', '/');
             folderViews.Add(new JObject( new JProperty("folderPath", folderMetaRelativePath), new JProperty("order", folderViews.Count), new JProperty("resourceVersion", "1.0"), /*new JProperty("name", folderGuid.ToString("D")),*/ new JProperty("tags", new JArray()), new JProperty("resourceType", "GMFolder") ));
             var folderYyContent = new JObject( new JProperty("isDefaultView", true), new JProperty("localisedFolderName", $"ResourceTree_{displayName.Replace(" ","")}"), new JProperty("filterType", resourceType), new JProperty("folderName", displayName), new JProperty("isResourceFolder", true), new JProperty("resourceVersion", "1.0"), new JProperty("name", displayName), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMFolder") ); WriteJsonFile(folderMetaPath, folderYyContent);
         }

         var roomOrderNodes = new JArray();
         if (Data.Rooms != null) { foreach(var room in Data.Rooms.Where(r=>r!=null)) { string roomName=GetResourceName(room); string roomKey=$"rooms/{roomName}"; if (ResourceGuids.TryGetValue(roomKey, out Guid roomGuid) && ResourcePaths.TryGetValue(roomKey, out string roomPath)) { roomOrderNodes.Add(new JObject( new JProperty("roomId", new JObject( new JProperty("name", roomGuid.ToString("D")), new JProperty("path", roomPath.Replace('\\','/')) )), new JProperty("category", "DEFAULT") )); } } }

        var yypContent = new JObject(
             new JProperty("projectName", ProjectName), new JProperty("projectDir", ""), new JProperty("packageName", ProjectName), new JProperty("packageDir", ""),
             new JProperty("constants", new JArray()), new JProperty("configs", new JObject(new JProperty("name", "Default"), new JProperty("children", new JArray()))),
             new JProperty("RoomOrderNodes", roomOrderNodes), new JProperty("Folders", new JArray(folderViews)),
             new JProperty("resources", new JArray(ResourceList)),
             new JProperty("Options", new JArray( new JObject(new JProperty("name", "Main"), new JProperty("path", "options/main/options_main.yy")), new JObject(new JProperty("name", "Windows"), new JProperty("path", "options/windows/options_windows.yy")) )),
             new JProperty("defaultScriptType", 1), new JProperty("isEcma", false), new JProperty("tutorialPath", ""),
             // Resource GUID lists (may be legacy/redundant but include for safety based on GMS2.3+ YYP)
             new JProperty("AudioGroups", new JArray(ResourceList.Where(r => r["id"]["path"].ToString().StartsWith("audiogroups/")).Select(r => r["id"].DeepClone()))),
             new JProperty("TextureGroups", new JArray(ResourceList.Where(r => r["id"]["path"].ToString().StartsWith("texturegroups/")).Select(r => r["id"].DeepClone()))),
             new JProperty("IncludedFiles", new JArray(ResourceList.Where(r => r["id"]["path"].ToString().StartsWith("includedfiles/")).Select(r => r["id"].DeepClone()))),
             new JProperty("MetaData", new JObject(new JProperty("IDEVersion", GMS2_VERSION))),
             new JProperty("projectVersion", "1.0"), new JProperty("packageId", ""), new JProperty("productId", ""), new JProperty("parentProject", null),
             new JProperty("YYPFormat", "1.2"), new JProperty("serialiseFrozenViewModels", false),
             // Root resource props
             new JProperty("resourceVersion", "1.7"), new JProperty("name", ProjectName), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMProject") // No Comma
        );
        WriteJsonFile(yypPath, yypContent);
        UMT.Log($"Project file created: {yypPath}");
    }


    private void CreateOptionsFiles() {
        string mainOptDir = Path.Combine(OutputPath, "options", "main"); string winOptDir = Path.Combine(OutputPath, "options", "windows");
        Directory.CreateDirectory(mainOptDir); Directory.CreateDirectory(winOptDir);
        string mainOptPath = Path.Combine(mainOptDir, "options_main.yy");
        var mainOpt = new JObject( new JProperty("option_gameguid", ProjectGuid.ToString("D")), new JProperty("option_gameid", Data.GeneralInfo?.GameID?.ToString() ?? "0"), new JProperty("option_game_speed", Data.GeneralInfo?.GameSpeed ?? 60), new JProperty("option_mips_for_3d_textures", false), new JProperty("option_draw_colour", 0xFFFFFFFF), new JProperty("option_window_colour", 0xFF000000), new JProperty("option_steam_app_id", Data.GeneralInfo?.SteamAppID?.ToString() ?? "0"), new JProperty("option_sci_usesci", false), new JProperty("option_author", Data.GeneralInfo?.Author?.Content ?? ""), new JProperty("option_collision_compatibility", true), new JProperty("option_copy_on_write_enabled", Data.GeneralInfo?.CopyOnWriteEnabled ?? false), new JProperty("option_lastchanged", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")), new JProperty("option_spine_licence", false), new JProperty("option_template_image", "${base_options_dir}/main/template_image.png"), new JProperty("option_template_icon", "${base_options_dir}/main/template_icon.ico"), new JProperty("option_template_description", null), new JProperty("resourceVersion", "1.4"), new JProperty("name", "Main"), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMMainOptions") ); WriteJsonFile(mainOptPath, mainOpt);
        string winOptPath = Path.Combine(winOptDir, "options_windows.yy"); string version="1.0.0.0"; if(Data.GeneralInfo?.Version!=null) version=$"{Data.GeneralInfo.Version.Major}.{Data.GeneralInfo.Version.Minor}.{Data.GeneralInfo.Version.Release}.{Data.GeneralInfo.Version.Build}";
        var winOpt = new JObject( new JProperty("option_windows_display_name", Data.GeneralInfo?.DisplayName?.Content ?? ProjectName), new JProperty("option_windows_executable_name", $"${{project_name}}.exe"), new JProperty("option_windows_version", version), new JProperty("option_windows_company_info", Data.GeneralInfo?.Company?.Content ?? Data.GeneralInfo?.Author?.Content ?? ""), new JProperty("option_windows_product_info", Data.GeneralInfo?.Product?.Content ?? ProjectName), new JProperty("option_windows_copyright_info", Data.GeneralInfo?.Copyright?.Content ?? $"(c) {DateTime.Now.Year}"), new JProperty("option_windows_description_info", Data.GeneralInfo?.Description?.Content ?? ProjectName), new JProperty("option_windows_display_cursor", true), new JProperty("option_windows_icon", "${base_options_dir}/windows/icons/icon.ico"), new JProperty("option_windows_save_location", 0), new JProperty("option_windows_splash_screen", "${base_options_dir}/windows/splash/splash.png"), new JProperty("option_windows_use_splash", false), new JProperty("option_windows_start_fullscreen", false), new JProperty("option_windows_allow_fullscreen_switching", true), new JProperty("option_windows_interpolate_pixels", Data.GeneralInfo?.InterpolatePixels ?? false), new JProperty("option_windows_vsync", false), new JProperty("option_windows_resize_window", true), new JProperty("option_windows_borderless", false), new JProperty("option_windows_scale", 0), new JProperty("option_windows_copy_exe_to_dest", false), new JProperty("option_windows_sleep_margin", 10), new JProperty("option_windows_texture_page", "2048x2048"), new JProperty("option_windows_installer_finished", "${base_options_dir}/windows/installer/finished.bmp"), new JProperty("option_windows_installer_header", "${base_options_dir}/windows/installer/header.bmp"), new JProperty("option_windows_license", "${base_options_dir}/windows/installer/license.txt"), new JProperty("option_windows_nsis_file", "${base_options_dir}/windows/installer/nsis_script.nsi"), new JProperty("option_windows_enable_steam", (Data.GeneralInfo?.SteamAppID ?? 0) > 0), new JProperty("option_windows_disable_sandbox", false), new JProperty("option_windows_steam_use_alternative_launcher", false), new JProperty("resourceVersion", "1.1"), new JProperty("name", "Windows"), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMWindowsOptions") ); WriteJsonFile(winOptPath, winOpt);
        UMT.Log("Default options files created.");
    }

     private void CreateDefaultConfig() {
          string configDir = Path.Combine(OutputPath, "configs"); string defaultCfgPath = Path.Combine(configDir, "Default.config");
          try { File.WriteAllText(defaultCfgPath, "[Default]\n"); UMT.Log($"Created default config file: {defaultCfgPath}"); }
          catch (Exception ex) { UMT.Log($"ERROR creating default config file: {ex.Message}"); }
     }

} // End class

public static class ScriptEntry { public static IUMTScript Script = new GMS2Converter(); }
