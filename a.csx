
// UMT Script: ExportToGMS2Project
// Version: 1.2 (Full Logic)
// Author: (Based on GMS1 script, adapted for GMS2 by AI & Community Knowledge)
// Description: Exports Undertale/Deltarune data.win contents to a GameMaker Studio 2 project format (.yyp, .yy, .gml).
// Requires: Newtonsoft.Json.dll accessible by UMT.

#r "Newtonsoft.Json.dll"

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Reflection;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using UndertaleModLib.Decompiler;
using System.Collections.Generic;
using System.Collections.Concurrent; // For thread-safe collections if needed
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ExportToGMS2Project
{
    // --- Script Configuration ---
    // If true, creates GMS2 configurations based on texture groups. Useful for organizing, but may be complex.
    private static bool EXPORT_TEXTURE_GROUPS_AS_CONFIGS = false;
    // If true, attempts to organize resources into subfolders in the GMS2 IDE view based on naming conventions.
    private static bool CREATE_IDE_SUBFOLDERS = true;
    private static string IDE_FOLDER_SEPARATOR = "/"; // Character used in names to denote subfolders (e.g., "Enemy/Goblin")

    // --- Global Variables ---
    private UndertaleData Data;
    private string FilePath; // Path to the data.win file being processed
    private Action<string, string, int, int> ShowProgressBar;
    private Action HideProgressBar;
    private Action<string> ScriptMessage;
    private Action<string, string> ScriptError;
    private Action<object, string, string, int, int> UpdateProgressBar;


    private string GameName;
    private int progress = 0;
    private int resourceNum = 0;
    private string projectRootFolder;
    private string projectName;
    private TextureWorker worker;
    private ThreadLocal<DecompileContext> DECOMPILE_CONTEXT;

    // Maps UMT resources to GUIDs and GMS2 paths. Using ConcurrentDictionary for potential thread safety, though population is sequential.
    private ConcurrentDictionary<UndertaleResource, ResourceInfo> resourceMapping = new ConcurrentDictionary<UndertaleResource, ResourceInfo>();
    // Separate storage for generated tileset info as they don't have a direct UMT resource key in the same way
    private ConcurrentDictionary<string, ResourceInfo> generatedTilesetMapping = new ConcurrentDictionary<string, ResourceInfo>();
    // Store generated folder info for YYP creation
    private ConcurrentDictionary<string, ResourceInfo> folderMapping = new ConcurrentDictionary<string, ResourceInfo>();

    // Helper class to store GUID and path info
    public class ResourceInfo
    {
        public Guid Guid { get; set; }
        public string Gms2Path { get; set; } // Relative path within the GMS2 project (e.g., "sprites/spr_player/spr_player.yy")
        public string Gms2Name { get; set; } // The GMS2 resource name (e.g., "spr_player")
        public string Gms2ResourceType { get; set; } // e.g., "GMSprite", "GMObject", "GMFolder"
        public string OriginalPath { get; set; } // Original full path if nested folders are used

        public ResourceInfo(string name, string resourceType, string originalPath = null)
        {
            Guid = Guid.NewGuid();
            Gms2Name = SanitizeGMS2Name(Path.GetFileName(name)); // Use only the final name part for the resource itself
            Gms2ResourceType = resourceType;
            OriginalPath = originalPath ?? name; // Store the full original path/name
            Gms2Path = GenerateDefaultPath(); // Set initial default path
        }

        // Generates the expected .yy file path based on type and name
        private string GenerateDefaultPath()
        {
            string folderName = Gms2ResourceType switch
            {
                "GMSprite" => "sprites",
                "GMSound" => "sounds",
                "GMObject" => "objects",
                "GMRoom" => "rooms",
                "GMScript" => "scripts",
                "GMFont" => "fonts",
                "GMPath" => "paths",
                "GMTileSet" => "tilesets",
                "GMTimeline" => "timelines",
                "GMFolder" => "folders",
                _ => "unknown"
            };

            // Special cases where resource name isn't used for subfolder
            if (Gms2ResourceType == "GMPath" || Gms2ResourceType == "GMTimeline" || Gms2ResourceType == "GMFolder")
            {
                 return Path.Combine(folderName, $"{Gms2Name}.yy").Replace('\\', '/');
            }
            else // Most resources have a subfolder named after them
            {
                 return Path.Combine(folderName, Gms2Name, $"{Gms2Name}.yy").Replace('\\', '/');
            }
        }

        // Basic sanitization for GMS2 resource names (letters, numbers, underscore)
        private static string SanitizeGMS2Name(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "resource_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // Replace separators used for folder structure temporarily
            string tempName = name.Replace(IDE_FOLDER_SEPARATOR, "__SEP__");

            StringBuilder sb = new StringBuilder();
            char firstChar = tempName[0];

            // Ensure starts with a letter or underscore
            if (!char.IsLetter(firstChar) && firstChar != '_')
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(firstChar);
            }


            for (int i = 1; i < tempName.Length; i++)
            {
                char c = tempName[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_'); // Replace other invalid characters
                }
            }

            // Restore separators
            string sanitizedBase = sb.ToString().Replace("__SEP__", IDE_FOLDER_SEPARATOR);

            // Get the final resource name part (after last separator)
            string finalNamePart = Path.GetFileName(sanitizedBase.Replace(IDE_FOLDER_SEPARATOR, Path.DirectorySeparatorChar.ToString()));


            // Avoid GMS2 reserved keywords (basic list, may need expansion)
            string[] keywords = {
                "all", "noone", "global", "local", "self", "other", "true", "false", "if", "then", "else", "begin", "end", "var",
                "while", "do", "for", "break", "continue", "return", "exit", "with", "switch", "case", "default", "enum",
                "constructor", "function", "new", "delete", "try", "catch", "finally", "throw", "static", "and", "or", "not", "xor", "mod", "div", "event_inherited"
                 // Add more keywords as needed
            };

            if (keywords.Contains(finalNamePart.ToLowerInvariant()))
            {
                // Prepend underscore only to the final name part if it conflicts
                 int lastSeparatorIndex = sanitizedBase.LastIndexOf(IDE_FOLDER_SEPARATOR);
                 if(lastSeparatorIndex != -1)
                 {
                     return sanitizedBase.Substring(0, lastSeparatorIndex + 1) + "_" + finalNamePart;
                 }
                 else
                 {
                     return "_" + finalNamePart;
                 }
            }

            return sanitizedBase; // Return potentially path-like name for folder creation logic
        }

        public JObject GetReference()
        {
            return new JObject(
                new JProperty("name", this.Gms2Name),
                new JProperty("path", this.Gms2Path)
            );
        }
         public JObject GetId()
        {
             // Used in YYP resources list
            return new JObject(
                new JProperty("name", this.Gms2Name), // Use sanitized name
                new JProperty("path", this.Gms2Path) // Path to the .yy file
            );
        }
    }


    // --- Main Execution Method ---
    public async Task Run(UndertaleData data, string filePath, Action<string, string, int, int> showProgressBar, Action hideProgressBar, Action<string> scriptMessage, Action<string, string> scriptError, Action<object, string, string, int, int> updateProgressBar)
    {
        // Assign arguments to instance variables
        this.Data = data;
        this.FilePath = filePath;
        this.ShowProgressBar = showProgressBar;
        this.HideProgressBar = hideProgressBar;
        this.ScriptMessage = scriptMessage;
        this.ScriptError = scriptError;
        this.UpdateProgressBar = updateProgressBar;

        // Initialize members that depend on Data
        GameName = Data.GeneralInfo?.Name?.ToString()?.Replace(@"""", "") ?? "MyGMS2Project";
        projectRootFolder = Path.Combine(Path.GetDirectoryName(FilePath), GameName);
        projectName = Path.GetFileName(projectRootFolder); // Get the actual project name from the folder path
        worker = new TextureWorker();
        DECOMPILE_CONTEXT = new ThreadLocal<DecompileContext>(() => new DecompileContext(Data, false));


        ShowProgressBar("Starting GMS2 Export...", "", 0, 1);

        if (Directory.Exists(projectRootFolder))
        {
            ScriptError($"Project folder '{projectRootFolder}' already exists. Please remove or rename it.", "Error");
            return;
        }

        try
        {
            Directory.CreateDirectory(projectRootFolder);

            // Calculate total resources for progress bar accurately
            CalculateTotalResources();

            // Pre-populate resource mapping dictionary with GUIDs and sanitized names
            PopulateResourceMapping();

            // --------------- Start exporting ---------------
            UpdateProgressBar(null, "Exporting Sprites...", progress, resourceNum);
            await ExportSpritesGMS2();

            UpdateProgressBar(null, "Exporting Backgrounds & Tilesets...", progress, resourceNum);
            await ExportBackgroundsAndTilesetsGMS2();

            UpdateProgressBar(null, "Exporting Fonts...", progress, resourceNum);
            await ExportFontsGMS2();

            UpdateProgressBar(null, "Exporting Sounds...", progress, resourceNum);
            await ExportSoundsGMS2();

            UpdateProgressBar(null, "Exporting Paths...", progress, resourceNum);
            await ExportPathsGMS2();

            UpdateProgressBar(null, "Exporting Scripts...", progress, resourceNum);
            await ExportScriptsGMS2();

            UpdateProgressBar(null, "Exporting Objects...", progress, resourceNum);
            await ExportGameObjectsGMS2();

            UpdateProgressBar(null, "Exporting Timelines...", progress, resourceNum);
            await ExportTimelinesGMS2();

            UpdateProgressBar(null, "Exporting Rooms...", progress, resourceNum);
            await ExportRoomsGMS2();

            // Generate main project file (.yyp) last, as it needs all resource info
            UpdateProgressBar(null, "Generating Project File...", progress, resourceNum);
            await GenerateProjectFileGMS2();

            // --------------- Export completed ---------------
            worker.Cleanup();
            HideProgressBar();
            ScriptMessage("GMS2 Export Complete.\n\nLocation: " + projectRootFolder);
        }
        catch (Exception ex)
        {
            worker.Cleanup();
            HideProgressBar();
            ScriptError($"An error occurred during export:\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Export Error");
        }
        finally
        {
             // Clean up thread-local context if necessary
            DECOMPILE_CONTEXT?.Dispose();
        }
    }

    // --- Helper Functions ---

    void CalculateTotalResources()
    {
        resourceNum = Data.Sprites.Count +
            Data.Backgrounds.Count + // Counted once, handles both sprite + potential tileset export internally
            Data.GameObjects.Count(o => o.Name?.Content != "<undefined>") + // Exclude placeholder object
            Data.Rooms.Count +
            Data.Sounds.Count +
            Data.Scripts.Count +
            Data.Fonts.Count +
            Data.Paths.Count +
            Data.Timelines.Count +
            (EXPORT_TEXTURE_GROUPS_AS_CONFIGS ? Data.TextureGroupInfo.Count : 0) +
            1; // For the main project file (.yyp)
    }

    void PopulateResourceMapping()
    {
        UpdateProgressBar(null, "Mapping resources...", 0, resourceNum); // Initial step
        int current = 0;

        // Helper to add resource and handle potential folders
        void AddResource(UndertaleNamedResource res, string type)
        {
            if (res == null || string.IsNullOrEmpty(res.Name?.Content)) return;
            string originalName = res.Name.Content;

            // Skip invalid GMS1 resources if they exist
            if (originalName == "<undefined>" && (type == "GMObject" || type == "GMSprite")) return;

            ResourceInfo info = new ResourceInfo(originalName, type, originalName);
            resourceMapping[res] = info;

            // If creating subfolders, register folder structure
            if (CREATE_IDE_SUBFOLDERS && originalName.Contains(IDE_FOLDER_SEPARATOR))
            {
                RegisterFolderStructure(Path.GetDirectoryName(originalName.Replace(IDE_FOLDER_SEPARATOR, Path.DirectorySeparatorChar.ToString())), type);
            }
             UpdateProgressBar(null, $"Mapping: {info.Gms2Name}", ++current, resourceNum);
        }

        // Map standard resources
        foreach (var res in Data.Sounds) AddResource(res, "GMSound");
        foreach (var res in Data.Sprites) AddResource(res, "GMSprite");
        foreach (var res in Data.GameObjects) AddResource(res, "GMObject");
        foreach (var res in Data.Rooms) AddResource(res, "GMRoom");
        foreach (var res in Data.Scripts) AddResource(res, "GMScript");
        foreach (var res in Data.Fonts) AddResource(res, "GMFont");
        foreach (var res in Data.Paths) AddResource(res, "GMPath");
        foreach (var res in Data.Timelines) AddResource(res, "GMTimeline");

        // Special handling for Backgrounds -> Sprites + potentially Tilesets
        foreach (var res in Data.Backgrounds)
        {
            if (res == null || string.IsNullOrEmpty(res.Name?.Content)) continue;
            string originalName = res.Name.Content;

            // Create a Sprite entry for *every* background's texture
            string spriteName = "spr_bkg_" + originalName; // Prefix to avoid name collisions
            ResourceInfo spriteInfo = new ResourceInfo(spriteName, "GMSprite", originalName); // Store original name for lookup/linking
            resourceMapping[res] = spriteInfo; // Store sprite info using the Background resource as the key

            if (CREATE_IDE_SUBFOLDERS && spriteName.Contains(IDE_FOLDER_SEPARATOR))
            {
                 RegisterFolderStructure(Path.GetDirectoryName(spriteName.Replace(IDE_FOLDER_SEPARATOR, Path.DirectorySeparatorChar.ToString())), "GMSprite");
            }


            // If it looks like a tileset, store info for later creation in ExportBackgroundsAndTilesetsGMS2
            if (res.TileWidth > 0 && res.TileHeight > 0)
            {
                string tilesetName = "ts_" + originalName; // Prefix tileset name
                ResourceInfo tilesetInfo = new ResourceInfo(tilesetName, "GMTileSet", originalName);
                generatedTilesetMapping[tilesetName] = tilesetInfo; // Store separately by its generated name

                if (CREATE_IDE_SUBFOLDERS && tilesetName.Contains(IDE_FOLDER_SEPARATOR))
                {
                    RegisterFolderStructure(Path.GetDirectoryName(tilesetName.Replace(IDE_FOLDER_SEPARATOR, Path.DirectorySeparatorChar.ToString())), "GMTileSet");
                }
            }
            UpdateProgressBar(null, $"Mapping Background: {spriteInfo.Gms2Name}", ++current, resourceNum);
        }
         progress = current; // Update global progress after mapping
    }

    void RegisterFolderStructure(string folderPath, string resourceType)
    {
        if (string.IsNullOrEmpty(folderPath)) return;

        string currentPath = "";
        string[] parts = folderPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        string parentFolderKey = GetFolderResourceKey(Path.GetDirectoryName(folderPath), resourceType); // Key for parent folder

        foreach (string part in parts)
        {
            currentPath = Path.Combine(currentPath, part);
            string folderKey = GetFolderResourceKey(currentPath, resourceType); // Unique key for this folder

            // Only add if not already present
            folderMapping.TryAdd(folderKey, new ResourceInfo(currentPath, "GMFolder", currentPath));
        }
    }
     // Generates a unique key for a folder based on its path and the primary resource type it contains
    string GetFolderResourceKey(string path, string resourceType) {
        // Example: "Sprites/Level1/Enemies" -> "Sprites:Level1/Enemies"
        string typePrefix = resourceType.Replace("GM", ""); // e.g., "Sprite", "Object"
        return $"{typePrefix}:{path?.Replace('\\', '/') ?? ""}"; // Use / consistently
    }

    ResourceInfo GetResourceInfo(UndertaleResource res)
    {
        if (res == null) return null;
        // Handle GMS1's "<undefined>" which might appear as parent/mask
        if (res is UndertaleGameObject && res.Name?.Content == "<undefined>") return null;
        if (res is UndertaleSprite && res.Name?.Content == "<undefined>") return null;

        if (resourceMapping.TryGetValue(res, out ResourceInfo info))
        {
            return info;
        }
        else
        {
            // This might happen for resources not directly in the main lists (e.g., instance creation code)
            // Or if mapping failed. Log a warning.
            Console.WriteLine($"Warning: Could not find pre-mapped ResourceInfo for {res?.Name?.Content} ({res?.GetType().Name}). Returning null.");
            return null;
        }
    }

    // Gets tileset info created during background export
    ResourceInfo GetTilesetInfo(string tilesetName)
    {
        generatedTilesetMapping.TryGetValue(tilesetName, out ResourceInfo info);
        return info;
    }

    string GetResourceGuidString(UndertaleResource res)
    {
        var info = GetResourceInfo(res);
        return info?.Guid.ToString("D") ?? Guid.Empty.ToString("D");
    }

    string GetResourceName(UndertaleResource res)
    {
        var info = GetResourceInfo(res);
        return info?.Gms2Name ?? "<undefined>"; // GMS2 often uses '<undefined>' or specific names for nulls
    }

    string DecompileCode(UndertaleCode code)
    {
        if (code == null) return "// Empty code block"; // Return comment instead of empty string
        try
        {
            // Use the ThreadLocal context
            DecompileContext context = DECOMPILE_CONTEXT.Value;
            if (context == null) {
                // This should ideally not happen if initialized correctly
                context = new DecompileContext(Data, false);
                DECOMPILE_CONTEXT.Value = context;
            }
            string decompiled = Decompiler.Decompile(code, context);
            return string.IsNullOrWhiteSpace(decompiled) ? "// Decompiled to empty code" : decompiled;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to decompile code {code.Name?.Content}: {ex.Message}");
            return $"// Decompilation failed for {code.Name?.Content}\n// Error: {ex.Message}\n// Original Name: {code.Name?.Content ?? "N/A"}";
        }
    }

    string DecompileActions(IList<UndertaleAction> actions, string contextName = "Action")
    {
        if (actions == null || actions.Count == 0) return $"// No actions for {contextName}";
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"// Code for {contextName}");
        bool codeFound = false;
        foreach (var action in actions)
        {
            if (action.CodeId != null)
            {
                sb.AppendLine($"// Action: {action.ActionName?.Content} (LibID: {action.LibID}, ID: {action.ID}, Kind: {action.Kind})");
                string decompiledActionCode = DecompileCode(action.CodeId);
                 sb.AppendLine(decompiledActionCode);
                 if (!decompiledActionCode.StartsWith("//")) codeFound = true; // Mark if actual code was found
            }
            else
            {
                // Append comments for non-code actions (basic representation)
                sb.AppendLine($"// Non-Code Action: {action.ActionName?.Content} (LibID: {action.LibID}, ID: {action.ID}, Kind: {action.Kind}) - Arguments may be lost.");
                // Potentially add logic here to translate very simple actions (like variable set) if desired, but highly complex.
            }
        }
         if (!codeFound) return $"// No executable code found in actions for {contextName}"; // Return comment if only non-code actions existed
        return sb.ToString();
    }

    async Task WriteJsonFile(JObject json, string relativePath)
    {
        string fullPath = Path.Combine(projectRootFolder, relativePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            using (StreamWriter file = File.CreateText(fullPath))
            using (JsonTextWriter writer = new JsonTextWriter(file))
            {
                writer.Formatting = Newtonsoft.Json.Formatting.Indented;
                await json.WriteToAsync(writer);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing JSON file {fullPath}: {ex.Message}");
            // Optionally re-throw or log more severely depending on importance
        }
    }

    async Task WriteTextFile(string content, string relativePath)
    {
        string fullPath = Path.Combine(projectRootFolder, relativePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            await File.WriteAllTextAsync(fullPath, content ?? "", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing text file {fullPath}: {ex.Message}");
        }
    }

    // Maps UMT event type index to GMS2 event category name (for file naming)
    string EventTypeToStringGMS2(int eventTypeIndex)
    {
        // Based on common GMS event names - needs mapping confirmation
        return eventTypeIndex switch {
            0 => "Create",
            1 => "Destroy",
            2 => "Alarm",
            3 => "Step",
            4 => "Collision",
            5 => "Keyboard", // Keyboard (direct key press)
            6 => "Mouse",
            7 => "Other",
            8 => "Draw",
            9 => "KeyPress", // KeyPress (held down)
            10 => "KeyRelease",
            11 => "Trigger", // Matches GMS1? Often unused. Use "Other"?
            12 => "CleanUp", // Added in GMS2
            13 => "Gesture", // Added in GMS2
            _ => $"UnknownEvent{eventTypeIndex}"
        };
    }

     // Maps UMT event subtype index/object to GMS2 event specific name/number (for file naming)
    string EventSubtypeToStringGMS2(int eventTypeIndex, uint subtype, UndertaleResource other = null)
    {
        switch (eventTypeIndex) {
            case 2: return $"Alarm_{subtype}"; // Alarm[0-11] -> Alarm_0, Alarm_1, ...
            case 3: // Step Events
                return subtype switch {
                    0 => "step_normal", // Begin Step = 0 in some older GM versions? Check UMT mapping. Assume 0=Normal, 1=Begin, 2=End
                    1 => "step_begin",
                    2 => "step_end",
                    _ => $"step_{subtype}" // Fallback
                };
            case 4: // Collision Events
                 ResourceInfo otherInfo = GetResourceInfo(other);
                 return $"collision_{(otherInfo?.Gms2Name ?? $"UnknownObject{subtype}")}"; // Collision_<ObjectName>
            case 5: return $"Keyboard_{subtype}"; // Keyboard event using keycode subtype
            case 6: // Mouse Events
                return subtype switch {
                    0 => "LeftButton", 1 => "RightButton", 2 => "MiddleButton",
                    3 => "NoButton", 4 => "LeftPressed", 5 => "RightPressed",
                    6 => "MiddlePressed", 7 => "LeftReleased", 8 => "RightReleased",
                    9 => "MiddleReleased", 10 => "MouseEnter", 11 => "MouseLeave",
                    16 => "MouseWheelUp", 17 => "MouseWheelDown", // Added later? Verify numbers
                    50 => "GlobLeftButton", 51 => "GlobRightButton", 52 => "GlobMiddleButton",
                    53 => "GlobLeftPressed", 54 => "GlobRightPressed", 55 => "GlobMiddlePressed",
                    56 => "GlobLeftReleased", 57 => "GlobRightReleased", 58 => "GlobMiddleReleased",
                    60 => "GestureTap", 61 => "GestureDoubleTap", 62 => "GestureDragStart",
                    63 => "GestureDragMove", 64 => "GestureDragEnd", 65 => "GestureFlick", 66 => "GesturePinchStart",
                    67 => "GesturePinchIn", 68 => "GesturePinchOut", 69 => "GesturePinchEnd", 70 => "GestureRotateStart",
                    71 => "GestureRotateMove", 72 => "GestureRotateEnd",
                    _ => $"Mouse_{subtype}"
                };
             case 7: // Other Events
                 return subtype switch {
                     0 => "OutsideRoom", 1 => "IntersectBoundary", 2 => "GameStart", 3 => "GameEnd",
                     4 => "RoomStart", 5 => "RoomEnd", 6 => "AnimationEnd", 7 => "AnimationUpdate", // Added GMS2?
                     8 => "AnimationEvent", // Added GMS2?
                     10 => "PathEnded", 11 => "UserEvent0", 12 => "UserEvent1", 13 => "UserEvent2",
                     14 => "UserEvent3", 15 => "UserEvent4", 16 => "UserEvent5", 17 => "UserEvent6",
                     18 => "UserEvent7", 19 => "UserEvent8", 20 => "UserEvent9", 21 => "UserEvent10",
                     22 => "UserEvent11", 23 => "UserEvent12", 24 => "UserEvent13", 25 => "UserEvent14",
                     // GMS2 Async events start around 60+
                     60 => "Async_ImageLoaded", 62 => "Async_AudioPlayback", 63 => "Async_AudioPlayEnd", 66 => "Async_System",
                     67 => "Async_PushNotification", 68 => "Async_SaveLoad", 69 => "Async_Networking", 70 => "Async_Social",
                     71 => "Async_Cloud", 72 => "Async_HTTP", 73 => "Async_Dialog", 74 => "Async_IAP", 75 => "Async_Steam",
                     76 => "Async_AudioRecording",
                     _ => $"Other_{subtype}"
                 };
             case 8: // Draw Events
                 return subtype switch {
                     0 => "Draw_Normal", // Draw
                     64 => "Draw_GUI",
                     65 => "Draw_GUI_Start", // Added GMS2?
                     66 => "Draw_GUI_End", // Added GMS2?
                     72 => "Draw_Pre", // Pre-Draw
                     73 => "Draw_Post", // Post-Draw
                     74 => "Draw_Window_Start", // Added GMS2?
                     75 => "Draw_Window_End", // Added GMS2?
                     _ => $"Draw_{subtype}"
                 };
             case 9: return $"KeyPress_{subtype}"; // Key Press (held)
             case 10: return $"KeyRelease_{subtype}"; // Key Release
             case 12: return "CleanUp_0"; // Cleanup has only one subtype typically
             case 13: // Gesture Events (more specific than Mouse ones)
                 return subtype switch {
                    0 => "Gesture_Tap", 1 => "Gesture_DoubleTap", 2 => "Gesture_DragStart", 3 => "Gesture_DragMove",
                    4 => "Gesture_DragEnd", 5 => "Gesture_Flick", 6 => "Gesture_PinchStart", 7 => "Gesture_PinchIn",
                    8 => "Gesture_PinchOut", 9 => "Gesture_PinchEnd", 10 => "Gesture_RotateStart", 11 => "Gesture_RotateMove",
                    12 => "Gesture_RotateEnd",
                     _ => $"Gesture_{subtype}"
                 };

            default: return $"{subtype}"; // Default to just the number if type is unknown/simple
        }
    }

    // Generates a default layer structure for a room
    private JArray CreateDefaultRoomLayers(string roomName, ResourceInfo roomInfo)
    {
        Guid instanceLayerGuid = Guid.NewGuid();
        Guid backgroundLayerGuid = Guid.NewGuid();

        return new JArray(
            // Instance Layer (usually first to be processed, drawn based on depth)
            new JObject(
                new JProperty("resourceType", "GMInstanceLayer"),
                new JProperty("resourceVersion", "1.0"),
                new JProperty("name", "Instances"), // Standard name
                new JProperty("instances", new JArray()), // Will be populated later
                new JProperty("visible", true),
                new JProperty("depth", 100), // Default depth, instances will override
                new JProperty("userdefinedDepth", false), // Let instances control depth
                new JProperty("inheritLayerDepth", false),
                new JProperty("inheritLayerSettings", false),
                new JProperty("gridX", 32),
                new JProperty("gridY", 32),
                new JProperty("layers", new JArray()), // No sub-layers initially
                new JProperty("hierarchyFrozen", false),
                new JProperty("effectEnabled", true),
                new JProperty("effectType", null),
                new JProperty("properties", new JArray()),
                new JProperty("parentLocked", false),
                new JProperty("isLocked", false),
                new JProperty("id", instanceLayerGuid.ToString("D")) // Layer ID
            ),
            // Background Layer (drawn behind instances)
            new JObject(
                new JProperty("resourceType", "GMBackgroundLayer"),
                new JProperty("resourceVersion", "1.0"),
                new JProperty("name", "Background"), // Standard name
                new JProperty("spriteId", null), // No single sprite for the layer itself
                new JProperty("colour", 4294967295), // Default color (white opaque) -> format is ABGR? or ARGB? GMS uses ARGB integer usually. 0xFFFFFFFF
                new JProperty("x", 0),
                new JProperty("y", 0),
                new JProperty("htiled", false),
                new JProperty("vtiled", false),
                new JProperty("stretch", false),
                new JProperty("animationFPS", 15.0),
                new JProperty("animationSpeedType", 0),
                new JProperty("userdefinedAnimFPS", false),
                new JProperty("visible", true),
                new JProperty("depth", 200), // Behind instances
                new JProperty("userdefinedDepth", false),
                new JProperty("inheritLayerDepth", false),
                new JProperty("inheritLayerSettings", false),
                new JProperty("gridX", 32),
                new JProperty("gridY", 32),
                new JProperty("layers", new JArray()),
                new JProperty("hierarchyFrozen", false),
                new JProperty("effectEnabled", true),
                new JProperty("effectType", null),
                new JProperty("properties", new JArray()),
                 new JProperty("parentLocked", false),
                 new JProperty("isLocked", false),
                new JProperty("id", backgroundLayerGuid.ToString("D")) // Layer ID
            )
            // Add Tile Layer later if needed
        );
    }


    // --- Resource Export Functions (GMS2 Version) ---

    // --------------- Export Sprite (GMS2) ---------------
    async Task ExportSpritesGMS2()
    {
        // Uses Parallel.ForEach for potential speedup, ensure thread safety if modifying shared state (resourceMapping is ok here)
        await Task.Run(() => Parallel.ForEach(Data.Sprites, sprite =>
        {
            // Need to call the async task wrapper
            ExportSpriteGMS2(sprite).Wait(); // Use .Wait() or make the lambda async and await
        }));
         // Alternative without Parallel.ForEach to avoid complexity/potential issues:
         // List<Task> tasks = new List<Task>();
         // foreach(var sprite in Data.Sprites) {
         //    tasks.Add(ExportSpriteGMS2(sprite));
         // }
         // await Task.WhenAll(tasks);
    }

    async Task ExportSpriteGMS2(UndertaleSprite sprite)
    {
        ResourceInfo spriteInfo = GetResourceInfo(sprite);
        if (spriteInfo == null) return;

        // Use Interlocked.Increment for thread-safe progress update if using Parallel.ForEach
        // UpdateProgressBar(null, $"Exporting sprite: {spriteInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
        // Sequential version:
        UpdateProgressBar(null, $"Exporting sprite: {spriteInfo.Gms2Name}", ++progress, resourceNum);

        // Use the pre-generated path from ResourceInfo
        string spriteRelativeFolder = Path.GetDirectoryName(spriteInfo.Gms2Path);
        string spriteFolderPath = Path.Combine(projectRootFolder, spriteRelativeFolder);
        Directory.CreateDirectory(spriteFolderPath);

        List<JObject> frameList = new List<JObject>();
        List<Task> imageTasks = new List<Task>();
        List<string> frameGuids = new List<string>(); // Store GUIDs for sequence keyframes

        for (int i = 0; i < sprite.Textures.Count; i++)
        {
            if (sprite.Textures[i]?.Texture != null)
            {
                Guid frameGuid = Guid.NewGuid();
                string frameGuidString = frameGuid.ToString("D");
                frameGuids.Add(frameGuidString); // Store for sequence
                string pngFileName = $"{frameGuidString}.png";
                string pngPath = Path.Combine(spriteFolderPath, pngFileName);

                // Capture texture locally for lambda
                var textureEntry = sprite.Textures[i];
                imageTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        worker.ExportAsPNG(textureEntry.Texture, pngPath, null, true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error exporting sprite frame {spriteInfo.Gms2Name}_{i}: {ex.Message}");
                    }
                }));

                 // Frame entry for the main sprite YY file
                 Guid layerGuid = Guid.NewGuid(); // Unique ID for the layer within the frame
                 var frameEntry = new JObject(
                    new JProperty("resourceType", "GMSpriteFrame"),
                    new JProperty("resourceVersion", "1.1"),
                    new JProperty("name", frameGuidString), // Frame name is its GUID
                    // CompositeImage and Images seem related to layer composition, GMS2 often auto-generates/updates these
                    // Providing a basic structure is usually sufficient for initial import.
                    new JProperty("compositeImage", new JObject(
                         new JProperty("FrameId", new JObject(new JProperty("name", frameGuidString), new JProperty("path", spriteInfo.Gms2Path))),
                         new JProperty("LayerId", null), // Often null initially
                         new JProperty("resourceVersion", "1.0"), new JProperty("name", ""), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMSpriteBitmap")
                    )),
                    new JProperty("images", new JArray(
                         new JObject(
                            new JProperty("FrameId", new JObject(new JProperty("name", frameGuidString), new JProperty("path", spriteInfo.Gms2Path))),
                            new JProperty("LayerId", new JObject(new JProperty("name", layerGuid.ToString("D")), new JProperty("path", spriteInfo.Gms2Path))), // Reference the layer ID
                            new JProperty("resourceVersion", "1.0"), new JProperty("name", pngFileName), // Use the actual image file name? GMS samples vary. GUID is safer. Let's use GUID.
                            // new JProperty("name", frameGuidString), // Using frame GUID seems more consistent with GMS structure
                            new JProperty("tags", new JArray()), new JProperty("resourceType", "GMSpriteBitmap")
                         )
                    ))
                 );
                frameList.Add(frameEntry);
            }
            else
            {
                 Console.WriteLine($"Warning: Sprite {spriteInfo.Gms2Name} has null texture at index {i}. Skipping frame.");
                 frameGuids.Add(null); // Add null placeholder to keep indexing correct for sequence
            }
        }

        await Task.WhenAll(imageTasks); // Ensure images are written before YY file

        // Determine texture group
        string texGroupName = "Default";
        string texGroupPath = "texturegroups/Default";
        if (sprite.TextureGroup != null && sprite.TextureGroup < Data.TextureGroupInfo.Count)
        {
            var groupInfo = Data.TextureGroupInfo[(int)sprite.TextureGroup];
            if (groupInfo != null && !string.IsNullOrWhiteSpace(groupInfo.Name))
            {
                 texGroupName = ResourceInfo.SanitizeGMS2Name(groupInfo.Name); // Sanitize group name
                 if (string.IsNullOrWhiteSpace(texGroupName) || texGroupName == "_") texGroupName = $"TexGroup_{sprite.TextureGroup}";
                 texGroupPath = $"texturegroups/{texGroupName}";
            }
        }

        // GMS2 uses a combined origin enum (0-9). Map UMT's X/Y origin or try to guess enum if possible.
        // Mapping X/Y is safer. GMS2 origin property seems unused if origin_x/y are set.
        int originEnumValue = 4; // Default to Middle Centre
        // Crude mapping attempt (can be improved)
        if (sprite.OriginX == 0 && sprite.OriginY == 0) originEnumValue = 0; // Top Left
        else if (sprite.OriginX == sprite.Width / 2 && sprite.OriginY == 0) originEnumValue = 1; // Top Centre
        else if (sprite.OriginX == sprite.Width && sprite.OriginY == 0) originEnumValue = 2; // Top Right
        else if (sprite.OriginX == 0 && sprite.OriginY == sprite.Height / 2) originEnumValue = 3; // Middle Left
        else if (sprite.OriginX == sprite.Width / 2 && sprite.OriginY == sprite.Height / 2) originEnumValue = 4; // Middle Centre
        else if (sprite.OriginX == sprite.Width && sprite.OriginY == sprite.Height / 2) originEnumValue = 5; // Middle Right
        else if (sprite.OriginX == 0 && sprite.OriginY == sprite.Height) originEnumValue = 6; // Bottom Left
        else if (sprite.OriginX == sprite.Width / 2 && sprite.OriginY == sprite.Height) originEnumValue = 7; // Bottom Centre
        else if (sprite.OriginX == sprite.Width && sprite.OriginY == sprite.Height) originEnumValue = 8; // Bottom Right
        else originEnumValue = 9; // Custom (use origin_x/y)


        // Create the main sprite YY JSON
        var gms2SpriteJson = new JObject(
            new JProperty("resourceType", "GMSprite"),
            new JProperty("resourceVersion", "1.0"), // Use 1.0, GMS2 upgrades if needed. 2.0 adds layers. Let's stick to 1.0 for broader compatibility.
            new JProperty("name", spriteInfo.Gms2Name),
            new JProperty("bboxmode", sprite.BBoxMode),
            new JProperty("bbox_left", sprite.MarginLeft),
            new JProperty("bbox_right", sprite.MarginRight),
            new JProperty("bbox_top", sprite.MarginTop),
            new JProperty("bbox_bottom", sprite.MarginBottom),
            new JProperty("HTile", false),
            new JProperty("VTile", false),
            new JProperty("For3D", sprite.Flags.HasFlag(UndertaleSprite.SpriteFlags.Texture3D)), // Map 3D flag if exists
            new JProperty("width", sprite.Width),
            new JProperty("height", sprite.Height),
            new JProperty("textureGroupId", new JObject(
                new JProperty("name", texGroupName),
                new JProperty("path", texGroupPath)
            )),
            new JProperty("swatchColours", null),
            new JProperty("gridX", 0),
            new JProperty("gridY", 0),
            new JProperty("frames", new JArray(frameList)), // Add frame objects
            new JProperty("sequence", new JObject(
                new JProperty("resourceType", "GMSequence"),
                new JProperty("resourceVersion", "1.4"), // Use current version
                new JProperty("name", spriteInfo.Gms2Name),
                new JProperty("timeUnits", 1), // 1 = Frames
                new JProperty("playback", 1), // Default playback mode
                new JProperty("playbackSpeed", sprite.PlaybackSpeed > 0 ? sprite.PlaybackSpeed : Data.GeneralInfo?.GameSpeed ?? 30.0f),
                new JProperty("playbackSpeedType", (int)sprite.SpeedType), // 0=FPS, 1=Game Frames per second
                new JProperty("autoRecord", true),
                new JProperty("volume", 1.0f),
                new JProperty("length", (float)frameGuids.Count(fg => fg != null)), // Length = number of valid frames
                new JProperty("events", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MessageEventKeyframe>"))),
                new JProperty("moments", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MomentsEventKeyframe>"))),
                new JProperty("tracks", new JArray(
                    new JObject(
                        new JProperty("resourceType", "GMSpriteFramesTrack"),
                        new JProperty("resourceVersion", "1.0"), // Use 2.0 if using newer sequence features? 1.0 is safer base.
                        new JProperty("name", "frames"),
                        new JProperty("spriteId", null),
                        new JProperty("keyframes", new JObject(
                            new JProperty("resourceType", "KeyframeStore<SpriteFrameKeyframe>"),
                            new JProperty("resourceVersion", "1.0"),
                            new JProperty("Keyframes", new JArray(
                                frameGuids.Select((guid, index) => guid == null ? null : new JObject(
                                    new JProperty("resourceType", "Keyframe<SpriteFrameKeyframe>"),
                                    new JProperty("resourceVersion", "1.0"),
                                    new JProperty("id", Guid.NewGuid().ToString("D")),
                                    new JProperty("Key", (float)index),
                                    new JProperty("Length", 1.0f),
                                    new JProperty("Stretch", false),
                                    new JProperty("Disabled", false),
                                    new JProperty("IsCreationKey", index == 0), // Mark first frame as creation key
                                    new JProperty("Channels", new JObject(
                                        new JProperty("0", new JObject(
                                            new JProperty("resourceType", "SpriteFrameKeyframe"),
                                            new JProperty("resourceVersion", "1.0"),
                                            new JProperty("Id", new JObject( // Reference frame GUID
                                                new JProperty("name", guid),
                                                new JProperty("path", spriteInfo.Gms2Path)
                                            ))
                                        ))
                                    ))
                                ))
                            ).Where(kf => kf != null) // Filter out nulls for skipped frames
                        )),
                        new JProperty("trackColour", 0),
                        new JProperty("inheritsTrackColour", true),
                        new JProperty("builtinName", 0),
                        new JProperty("traits", 0),
                        new JProperty("interpolation", 1), // 1 = Linear
                        new JProperty("tracks", new JArray()),
                        new JProperty("events", new JArray()),
                        new JProperty("modifiers", new JArray()),
                        new JProperty("isCreationTrack", false) // Default creation track? Usually false for sprite track.
                    )
                )), // End tracks
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
                new JProperty("xorigin", sprite.OriginX), // Keep X/Y for precision
                new JProperty("yorigin", sprite.OriginY),
                new JProperty("eventToFunction", new JObject()),
                new JProperty("eventStubScript", null),
                new JProperty("parent", spriteInfo.GetReference()) // Sequence parent is the sprite itself
            )), // End sequence
            // GMS 2.3+ uses origin_x, origin_y primarily. The 'origin' enum is less critical if x/y are set.
             new JProperty("origin", originEnumValue), // Use mapped enum or 9 for custom
             new JProperty("origin_x", sprite.OriginX), // Explicitly set origin X
             new JProperty("origin_y", sprite.OriginY), // Explicitly set origin Y
            new JProperty("separateMasks", sprite.SepMasks == 1), // SepMasks seems boolean (0 or 1)
            new JProperty("tags", new JArray()),
            new JProperty("parent", GetFolderReference(spriteInfo, "GMSprite")) // Parent folder in IDE view
        );

        string yyPath = spriteInfo.Gms2Path; // Use path from resource info
        await WriteJsonFile(gms2SpriteJson, yyPath);
    }


    // --------------- Export Backgrounds & Tilesets (GMS2) ---------------
    async Task ExportBackgroundsAndTilesetsGMS2()
    {
        // --- Step 1: Export all background textures as Sprites ---
        List<Task> spriteExportTasks = new List<Task>();
        foreach (var bg in Data.Backgrounds)
        {
            ResourceInfo spriteInfo = GetResourceInfo(bg); // Gets the pre-created *sprite* info
            if (spriteInfo == null || bg.Texture == null) continue;

            spriteExportTasks.Add(ExportBackgroundAsSpriteGMS2(bg, spriteInfo));
        }
        await Task.WhenAll(spriteExportTasks);

        // --- Step 2: Create Tileset resources if the background was tile-like ---
        List<Task> tilesetExportTasks = new List<Task>();
        foreach (var bg in Data.Backgrounds)
        {
            if (bg.TileWidth > 0 && bg.TileHeight > 0)
            {
                ResourceInfo spriteInfo = GetResourceInfo(bg); // Associated sprite
                if (spriteInfo == null) continue;

                // Construct the expected tileset name based on the original background name
                string tilesetName = "ts_" + Path.GetFileName(spriteInfo.OriginalPath); // Use original bg name base
                ResourceInfo tilesetInfo = GetTilesetInfo(ResourceInfo.SanitizeGMS2Name(tilesetName)); // Lookup in generated tileset map

                if (tilesetInfo != null)
                {
                    tilesetExportTasks.Add(ExportTilesetGMS2(bg, spriteInfo, tilesetInfo));
                }
                else
                {
                     Console.WriteLine($"Warning: Could not find generated tileset info for background '{bg.Name.Content}'. Skipping tileset export.");
                }
            }
        }
        await Task.WhenAll(tilesetExportTasks);
    }

    // Helper task for exporting a background's texture as a single-frame sprite
    async Task ExportBackgroundAsSpriteGMS2(UndertaleBackground bg, ResourceInfo spriteInfo)
    {
         // UpdateProgressBar(null, $"Exporting background sprite: {spriteInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
         UpdateProgressBar(null, $"Exporting background sprite: {spriteInfo.Gms2Name}", ++progress, resourceNum);


        string spriteRelativeFolder = Path.GetDirectoryName(spriteInfo.Gms2Path);
        string spriteFolderPath = Path.Combine(projectRootFolder, spriteRelativeFolder);
        Directory.CreateDirectory(spriteFolderPath);

        if (bg.Texture?.Texture == null) {
            Console.WriteLine($"Warning: Background {spriteInfo.Gms2Name} has no texture data. Skipping PNG export, creating empty YY.");
            // Create an empty YY file? Or just skip entirely? Let's skip creating YY if no texture.
             return;
        }


        // Export the single background texture
        Guid frameGuid = Guid.NewGuid();
        string frameGuidString = frameGuid.ToString("D");
        string pngFileName = $"{frameGuidString}.png";
        string pngPath = Path.Combine(spriteFolderPath, pngFileName);

        try
        {
            worker.ExportAsPNG(bg.Texture, pngPath, null, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting background texture {spriteInfo.Gms2Name}: {ex.Message}");
            return; // Don't create YY if image failed
        }

        // Determine texture group
        string texGroupName = "Default";
        string texGroupPath = "texturegroups/Default";
        if (bg.TextureGroup != null && bg.TextureGroup < Data.TextureGroupInfo.Count)
        {
             var groupInfo = Data.TextureGroupInfo[(int)bg.TextureGroup];
             if (groupInfo != null && !string.IsNullOrWhiteSpace(groupInfo.Name)) {
                 texGroupName = ResourceInfo.SanitizeGMS2Name(groupInfo.Name);
                 if (string.IsNullOrWhiteSpace(texGroupName) || texGroupName == "_") texGroupName = $"TexGroup_{bg.TextureGroup}";
                 texGroupPath = $"texturegroups/{texGroupName}";
             }
        }

        // Single frame entry
        Guid layerGuid = Guid.NewGuid();
        var frameEntry = new JObject(
            new JProperty("resourceType", "GMSpriteFrame"), new JProperty("resourceVersion", "1.1"), new JProperty("name", frameGuidString),
            new JProperty("compositeImage", new JObject(
                new JProperty("FrameId", new JObject(new JProperty("name", frameGuidString), new JProperty("path", spriteInfo.Gms2Path))),
                new JProperty("LayerId", null),
                new JProperty("resourceVersion", "1.0"), new JProperty("name", ""), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMSpriteBitmap")
            )),
            new JProperty("images", new JArray(
                 new JObject(
                    new JProperty("FrameId", new JObject(new JProperty("name", frameGuidString), new JProperty("path", spriteInfo.Gms2Path))),
                    new JProperty("LayerId", new JObject(new JProperty("name", layerGuid.ToString("D")), new JProperty("path", spriteInfo.Gms2Path))),
                    new JProperty("resourceVersion", "1.0"), new JProperty("name", frameGuidString), new JProperty("tags", new JArray()), new JProperty("resourceType", "GMSpriteBitmap")
                 )
            ))
        );

        // Create the background-sprite YY JSON (simplified sprite)
        var gms2SpriteJson = new JObject(
            new JProperty("resourceType", "GMSprite"), new JProperty("resourceVersion", "1.0"),
            new JProperty("name", spriteInfo.Gms2Name),
            new JProperty("bboxmode", 0), // Full image bbox for backgrounds usually
            new JProperty("bbox_left", 0),
            new JProperty("bbox_right", bg.Texture.TargetWidth > 0 ? bg.Texture.TargetWidth - 1: 0),
            new JProperty("bbox_top", 0),
            new JProperty("bbox_bottom", bg.Texture.TargetHeight > 0 ? bg.Texture.TargetHeight - 1: 0),
            new JProperty("HTile", bg.TileX), // Use original TileX/TileY flags from background?
            new JProperty("VTile", bg.TileY),
            new JProperty("For3D", false), // Backgrounds typically not 3D
            new JProperty("width", bg.Texture.TargetWidth),
            new JProperty("height", bg.Texture.TargetHeight),
            new JProperty("textureGroupId", new JObject(new JProperty("name", texGroupName), new JProperty("path", texGroupPath))),
            new JProperty("swatchColours", null),
            new JProperty("gridX", 0), new JProperty("gridY", 0),
            new JProperty("frames", new JArray(frameEntry)),
            new JProperty("sequence", new JObject( // Basic sequence for single frame
                new JProperty("resourceType", "GMSequence"), new JProperty("resourceVersion", "1.4"), new JProperty("name", spriteInfo.Gms2Name),
                new JProperty("timeUnits", 1), new JProperty("playback", 1), new JProperty("playbackSpeed", 15.0f), new JProperty("playbackSpeedType", 0),
                new JProperty("autoRecord", true), new JProperty("volume", 1.0f), new JProperty("length", 1.0f),
                new JProperty("events", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MessageEventKeyframe>"))),
                new JProperty("moments", new JObject(new JProperty("Keyframes", new JArray()), new JProperty("resourceVersion", "1.0"), new JProperty("resourceType", "KeyframeStore<MomentsEventKeyframe>"))),
                new JProperty("tracks", new JArray( new JObject(
                    new JProperty("resourceType", "GMSpriteFramesTrack"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "frames"), new JProperty("spriteId", null),
                    new JProperty("keyframes", new JObject(
                        new JProperty("resourceType", "KeyframeStore<SpriteFrameKeyframe>"), new JProperty("resourceVersion", "1.0"),
                        new JProperty("Keyframes", new JArray( new JObject( // Keyframe for frame 0
                            new JProperty("resourceType", "Keyframe<SpriteFrameKeyframe>"), new JProperty("resourceVersion", "1.0"), new JProperty("id", Guid.NewGuid().ToString("D")),
                            new JProperty("Key", 0.0f), new JProperty("Length", 1.0f), new JProperty("Stretch", false), new JProperty("Disabled", false), new JProperty("IsCreationKey", true),
                            new JProperty("Channels", new JObject( new JProperty("0", new JObject(
                                new JProperty("resourceType", "SpriteFrameKeyframe"), new JProperty("resourceVersion", "1.0"),
                                new JProperty("Id", new JObject( new JProperty("name", frameGuidString), new JProperty("path", spriteInfo.Gms2Path)))))))
                            )))
                        )),
                    new JProperty("trackColour", 0), new JProperty("inheritsTrackColour", true), new JProperty("builtinName", 0), new JProperty("traits", 0),
                    new JProperty("interpolation", 1), new JProperty("tracks", new JArray()), new JProperty("events", new JArray()), new JProperty("modifiers", new JArray()), new JProperty("isCreationTrack", false)
                    ))), // End tracks
                new JProperty("visibleRange", null), new JProperty("lockOrigin", false), new JProperty("showBackdrop", true), new JProperty("showBackdropImage", false),
                new JProperty("backdropImagePath", ""), new JProperty("backdropImageOpacity", 0.5f), new JProperty("backdropWidth", 1366), new JProperty("backdropHeight", 768),
                new JProperty("backdropXOffset", 0.0f), new JProperty("backdropYOffset", 0.0f), new JProperty("xorigin", 0), new JProperty("yorigin", 0),
                new JProperty("eventToFunction", new JObject()), new JProperty("eventStubScript", null), new JProperty("parent", spriteInfo.GetReference())
                )), // End sequence
            new JProperty("origin", 0), new JProperty("origin_x", 0), new JProperty("origin_y", 0),
            new JProperty("separateMasks", false),
            new JProperty("tags", new JArray("background_source")), // Tag it
            new JProperty("parent", GetFolderReference(spriteInfo, "GMSprite"))
            );

        string yyPath = spriteInfo.Gms2Path;
        await WriteJsonFile(gms2SpriteJson, yyPath);
    }

     // Helper task for exporting a tileset YY file
    async Task ExportTilesetGMS2(UndertaleBackground bg, ResourceInfo spriteInfo, ResourceInfo tilesetInfo)
    {
         // UpdateProgressBar(null, $"Exporting tileset: {tilesetInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
         // Tileset export doesn't advance main progress bar to avoid double counting, it's part of the background export step.
         Console.WriteLine($"Exporting tileset: {tilesetInfo.Gms2Name}"); // Log instead


         string tilesetRelativeFolder = Path.GetDirectoryName(tilesetInfo.Gms2Path);
         string tilesetFolderPath = Path.Combine(projectRootFolder, tilesetRelativeFolder);
         Directory.CreateDirectory(tilesetFolderPath);


         // Determine texture group (should match the sprite it came from)
         string texGroupName = "Default";
         string texGroupPath = "texturegroups/Default";
         if (bg.TextureGroup != null && bg.TextureGroup < Data.TextureGroupInfo.Count)
         {
             var groupInfo = Data.TextureGroupInfo[(int)bg.TextureGroup];
              if (groupInfo != null && !string.IsNullOrWhiteSpace(groupInfo.Name)) {
                 texGroupName = ResourceInfo.SanitizeGMS2Name(groupInfo.Name);
                 if (string.IsNullOrWhiteSpace(texGroupName) || texGroupName == "_") texGroupName = $"TexGroup_{bg.TextureGroup}";
                 texGroupPath = $"texturegroups/{texGroupName}";
              }
         }

        // Calculate tile count and columns based on sprite dimensions and tile properties
        int tileWidth = bg.TileWidth > 0 ? bg.TileWidth : 1;
        int tileHeight = bg.TileHeight > 0 ? bg.TileHeight : 1;
        int hSep = bg.TileHSep;
        int vSep = bg.TileVSep;
        int spriteWidth = bg.Texture?.TargetWidth ?? 0;
        int spriteHeight = bg.Texture?.TargetHeight ?? 0;

        int columns = (spriteWidth + hSep) > 0 ? (spriteWidth - tileWidth + hSep) / (tileWidth + hSep) + 1 : 0;
         int rows = (spriteHeight + vSep) > 0 ? (spriteHeight - tileHeight + vSep) / (tileHeight + vSep) + 1 : 0;
         int tileCount = columns * rows;
         if (tileCount < 0) tileCount = 0; // Ensure non-negative

        // Ensure borders are reasonable, default 2
         int border = 2;


         var gms2TilesetJson = new JObject(
             new JProperty("resourceType", "GMTileSet"),
             new JProperty("resourceVersion", "1.0"),
             new JProperty("name", tilesetInfo.Gms2Name),
             new JProperty("spriteId", spriteInfo.GetReference()), // Link to the background-sprite
             new JProperty("tileWidth", bg.TileWidth),
             new JProperty("tileHeight", bg.TileHeight),
             new JProperty("tilexoff", bg.TileXOffset),
             new JProperty("tileyoff", bg.TileYOffset),
             new JProperty("tilehsep", bg.TileHSep),
             new JProperty("tilevsep", bg.TileVSep),
             new JProperty("spriteNoExport", true), // Sprite is managed separately
             new JProperty("textureGroupId", new JObject(new JProperty("name", texGroupName), new JProperty("path", texGroupPath))),
             new JProperty("out_columns", columns),
             new JProperty("out_tilehborder", border),
             new JProperty("out_tilevborder", border),
             // out_borderX/Y seem legacy or internal, use tileh/vborder
             // --- Tile Animation Data (Defaults, not directly extractable from UMT Background) ---
             new JProperty("tile_count", tileCount), // Calculated total tiles
             new JProperty("autoTileSets", new JArray()), // No auto-tiling info extracted
             new JProperty("tileAnimationFrames", new JArray()), // No animation info extracted
             new JProperty("tileAnimationSpeed", 15.0), // Default speed
             // --- End Tile Animation Data ---
             new JProperty("macroPageTiles", new JObject( // IDE related, provide basic structure
                 new JProperty("SerialiseWidth", spriteWidth > 0 ? spriteWidth : 2048), // Use sprite width or a large default
                 new JProperty("SerialiseHeight", spriteHeight > 0 ? spriteHeight : 2048), // Use sprite height or a large default
                 new JProperty("TileSerialiseData", new JArray()) // Empty tile data array, GMS2 populates this
             )),
             new JProperty("parent", GetFolderReference(tilesetInfo, "GMTileSet"))
         );

         string yyPath = tilesetInfo.Gms2Path;
         await WriteJsonFile(gms2TilesetJson, yyPath);
    }


    // --------------- Export Font (GMS2) ---------------
    // WARNING: Exports metadata and texture sheet. GMS2 works best with source TTF/OTF.
    async Task ExportFontsGMS2()
    {
        var tasks = Data.Fonts.Select(font => ExportFontGMS2(font)).ToList();
        await Task.WhenAll(tasks);
    }

    async Task ExportFontGMS2(UndertaleFont font)
    {
        ResourceInfo fontInfo = GetResourceInfo(font);
        if (fontInfo == null) return;

        // UpdateProgressBar(null, $"Exporting font: {fontInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
        UpdateProgressBar(null, $"Exporting font: {fontInfo.Gms2Name}", ++progress, resourceNum);


        string fontRelativeFolder = Path.GetDirectoryName(fontInfo.Gms2Path);
        string fontFolderPath = Path.Combine(projectRootFolder, fontRelativeFolder);
        Directory.CreateDirectory(fontFolderPath);

        // Export font texture sheet for reference (if exists)
        string pngFileName = $"{fontInfo.Gms2Name}_texture.png";
        string pngPath = Path.Combine(fontFolderPath, pngFileName);
        if (font.Texture?.Texture != null)
        {
            try
            {
                worker.ExportAsPNG(font.Texture, pngPath, null, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting font texture {fontInfo.Gms2Name}: {ex.Message}");
                // Continue exporting YY even if texture fails
            }
        } else {
            Console.WriteLine($"Info: Font {fontInfo.Gms2Name} has no texture sheet.");
        }


         // Determine texture group (fonts usually default, but check UMT data)
        string texGroupName = "Default";
        string texGroupPath = "texturegroups/Default";
        if (font.TextureGroup != null && font.TextureGroup < Data.TextureGroupInfo.Count)
        {
             var groupInfo = Data.TextureGroupInfo[(int)font.TextureGroup];
              if (groupInfo != null && !string.IsNullOrWhiteSpace(groupInfo.Name)) {
                 texGroupName = ResourceInfo.SanitizeGMS2Name(groupInfo.Name);
                 if (string.IsNullOrWhiteSpace(texGroupName) || texGroupName == "_") texGroupName = $"TexGroup_{font.TextureGroup}";
                 texGroupPath = $"texturegroups/{texGroupName}";
              }
        }

        // Map AntiAliasing level (UMT uses 0-3?, GMS2 uses 0-2 or 1-3?). Let's clamp to 0-2.
        int antiAliasLevel = Math.Clamp(font.AntiAliasing, 0, 2); // 0=Off, 1=IDE Pref, 2=On. Assume UMT 3 maps to 2.

        // Map Charset (GMS2 uses 0=ANSI, 255=Default)
        int charsetValue = font.Charset == 1 ? 0 : 255; // Map UMT ANSI (1) to GMS2 ANSI (0), otherwise default


        // Create the font YY JSON (metadata focus)
        var gms2FontJson = new JObject(
            new JProperty("resourceType", "GMFont"),
            new JProperty("resourceVersion", "1.0"),
            new JProperty("name", fontInfo.Gms2Name),
            // Attempt to use a reasonable font name for GMS2's system lookup
            new JProperty("fontName", font.GMS2FontName ?? font.DisplayName?.Content ?? "Arial"), // Use explicit GMS2 name, DisplayName, or fallback
            new JProperty("size", font.EmSize),
            new JProperty("bold", font.Bold),
            new JProperty("italic", font.Italic),
            new JProperty("underline", font.Underline), // Assume false if not in UMT data
            new JProperty("strikeout", font.Strikeout), // Assume false if not in UMT data
            new JProperty("antialias", antiAliasLevel), // Mapped value
            new JProperty("scaleX", 1.0f),
            new JProperty("scaleY", 1.0f),
            new JProperty("editorRenderer", "FreeType"), // FreeType is generally better
            new JProperty("charset", charsetValue), // Mapped value
            new JProperty("first", font.RangeStart),
            new JProperty("last", font.RangeEnd > 0 ? font.RangeEnd : (font.Glyphs.Any() ? font.Glyphs.Max(g => g.Character) : 0)), // Use RangeEnd or max glyph char code
            // --- GMS2 Specific ---
            new JProperty("includeTTF", false), // Not including TTF
            new JProperty("TTFName", ""), // Empty
            new JProperty("textureGroupId", new JObject(new JProperty("name", texGroupName), new JProperty("path", texGroupPath))),
            new JProperty("glyphs", new JObject( // Export glyph data, mainly for reference
                 font.Glyphs.Select(g => new JProperty(g.Character.ToString(), new JObject(
                     new JProperty("character", g.Character),
                     new JProperty("h", g.SourceHeight),
                     new JProperty("offset", g.Offset),
                     new JProperty("shift", g.Shift),
                     new JProperty("w", g.SourceWidth),
                     new JProperty("x", g.SourceX),
                     new JProperty("y", g.SourceY)
                 )))
             )),
            new JProperty("kerningPairs", new JArray()), // Kerning not available
             // 'ranges' seems less used now, first/last cover it. Keep empty array.
             new JProperty("ranges", new JArray()),
            new JProperty("sampleText", "The quick brown fox jumps over the lazy dog."),
            new JProperty("fallbackSprite", null),
            new JProperty("fallbackSpriteScale", 1.0f),
            new JProperty("fallbackSpriteOffset", 0),
            new JProperty("parent", GetFolderReference(fontInfo, "GMFont"))
        );

        string yyPath = fontInfo.Gms2Path;
        await WriteJsonFile(gms2FontJson, yyPath);
    }


    // --------------- Export Sound (GMS2) ---------------
    async Task ExportSoundsGMS2()
    {
        var tasks = Data.Sounds.Select(sound => ExportSoundGMS2(sound)).ToList();
        await Task.WhenAll(tasks);
    }

    async Task ExportSoundGMS2(UndertaleSound sound)
    {
        ResourceInfo soundInfo = GetResourceInfo(sound);
        if (soundInfo == null) return;

        // UpdateProgressBar(null, $"Exporting sound: {soundInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
         UpdateProgressBar(null, $"Exporting sound: {soundInfo.Gms2Name}", ++progress, resourceNum);


        string soundRelativeFolder = Path.GetDirectoryName(soundInfo.Gms2Path);
        string soundFolderPath = Path.Combine(projectRootFolder, soundRelativeFolder);
        Directory.CreateDirectory(soundFolderPath);

        string audioFileName = Path.GetFileName(sound.File?.Content ?? soundInfo.Gms2Name + ".ogg"); // Use original filename or generate one
        string audioFilePath = Path.Combine(soundFolderPath, audioFileName);

        // Save sound file
        if (sound.AudioFile?.Data != null)
        {
            try
            {
                await File.WriteAllBytesAsync(audioFilePath, sound.AudioFile.Data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing sound file {audioFilePath}: {ex.Message}");
                // Create YY anyway, but it will be invalid in GMS2 without the file.
            }
        }
        else
        {
            Console.WriteLine($"Warning: Sound {soundInfo.Gms2Name} has no audio data. YY file will be created but may be invalid.");
        }

        // Determine GMS2 sound attributes based on UMT data and file extension
        bool isOgg = audioFileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
        bool isWav = audioFileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);

        // conversionMode: 0=Uncompressed, 1=Compressed, 2=UncompressOnLoad
        // preload: true/false (true means load into memory, false means stream)
        int conversionMode = 1; // Default to compressed
        bool preload = false; // Default to streamed
        int gms2SoundType = 1; // Default to Stereo
        int bitDepth = 1; // Default 16-bit
        int compression = 1; // Default standard compression
        int sampleRate = 44100;
        int bitRate = 192;

        if (isWav) {
             conversionMode = 0; // WAV usually uncompressed
             preload = true;    // WAV usually preloaded
             gms2SoundType = 0; // WAV often mono? Let's assume mono, GMS can detect.
             bitRate = 0; // Not applicable to uncompressed
        }
         // Override with UMT data if available and seems valid
        if ((sound.Flags & UndertaleSound.AudioFlags.PreferUncompressed) != 0) {
             conversionMode = 0;
             preload = true;
        }
         if ((sound.Flags & UndertaleSound.AudioFlags.Streamed) != 0) {
            preload = false;
            if(conversionMode == 0) conversionMode = 1; // Cannot stream uncompressed, force compressed
        }
        // GMS2 Type: 0=Mono, 1=Stereo, 2=3D. UMT Flags mapping needed.
        if((sound.Flags & UndertaleSound.AudioFlags.IsMono) != 0) {
            gms2SoundType = 0;
        }
        // Check for 3D flag if UMT Sound object has it (might require library update)
        // Example: if((sound.Flags & Some3DFlag) != 0) gms2SoundType = 2;

        if(sound.SampleRate > 0) sampleRate = (int)sound.SampleRate;
        if(sound.BitDepth > 0) bitDepth = (sound.BitDepth == 8 ? 0 : 1); // Map 8bit to 0, else 1 (16bit)
        if(sound.Bitrate > 0 && conversionMode == 1) bitRate = (int)sound.Bitrate; // Use bitrate only if compressed


        // Determine Audio Group
        string audioGroupName = "audiogroup_default";
        string audioGroupPath = "audiogroups/audiogroup_default";
        if (sound.AudioGroup > 0 && sound.AudioGroup < Data.AudioGroups.Count) { // Check if AudioGroup index is valid
            var audioGroup = Data.AudioGroups[(int)sound.AudioGroup];
            if(audioGroup != null && !string.IsNullOrWhiteSpace(audioGroup.Name?.Content)) {
                audioGroupName = ResourceInfo.SanitizeGMS2Name(audioGroup.Name.Content);
                 if (string.IsNullOrWhiteSpace(audioGroupName) || audioGroupName == "_") audioGroupName = $"audiogroup_{sound.AudioGroup}";
                 audioGroupPath = $"audiogroups/{audioGroupName}";
            }
        }


        // Create sound YY JSON
        var gms2SoundJson = new JObject(
            new JProperty("resourceType", "GMSound"),
            new JProperty("resourceVersion", "1.0"),
            new JProperty("name", soundInfo.Gms2Name),
            new JProperty("conversionMode", conversionMode),
            new JProperty("compression", compression),
            new JProperty("volume", sound.Volume),
            new JProperty("preload", preload),
            new JProperty("bitRate", bitRate),
            new JProperty("sampleRate", sampleRate),
            new JProperty("type", gms2SoundType),
            new JProperty("bitDepth", bitDepth), // 1 = 16 bit, 0 = 8 bit ? Check GMS2 docs. Default 1.
            new JProperty("audioGroupId", new JObject(
                new JProperty("name", audioGroupName),
                new JProperty("path", audioGroupPath)
            )),
            new JProperty("soundFile", audioFileName),
            new JProperty("duration", 0.0f), // GMS2 calculates this
            new JProperty("tags", new JArray()),
            new JProperty("parent", GetFolderReference(soundInfo, "GMSound"))
        );

        string yyPath = soundInfo.Gms2Path;
        await WriteJsonFile(gms2SoundJson, yyPath);
    }


    // --------------- Export Path (GMS2) ---------------
    async Task ExportPathsGMS2()
    {
        var tasks = Data.Paths.Select(path => ExportPathGMS2(path)).ToList();
        await Task.WhenAll(tasks);
    }

    async Task ExportPathGMS2(UndertalePath path)
    {
        ResourceInfo pathInfo = GetResourceInfo(path);
        if (pathInfo == null) return;

        // UpdateProgressBar(null, $"Exporting path: {pathInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
         UpdateProgressBar(null, $"Exporting path: {pathInfo.Gms2Name}", ++progress, resourceNum);


        // Paths don't have their own subfolder in GMS2 typically
        string pathsRootPath = Path.Combine(projectRootFolder, "paths");
        Directory.CreateDirectory(pathsRootPath);

        // Create path YY JSON
        var gms2PathJson = new JObject(
            new JProperty("resourceType", "GMPath"),
            new JProperty("resourceVersion", "1.0"),
            new JProperty("name", pathInfo.Gms2Name),
            new JProperty("kind", (int)path.Kind), // 0=Straight, 1=Smooth
            new JProperty("closed", path.IsClosed),
            new JProperty("precision", path.Precision),
            new JProperty("points", new JArray(
                path.Points.Select(p => new JObject(
                    new JProperty("x", p.X),
                    new JProperty("y", p.Y),
                    new JProperty("speed", p.Speed)
                ))
            )),
            new JProperty("parent", GetFolderReference(pathInfo, "GMPath"))
        );

        string yyPath = pathInfo.Gms2Path; // Path like "paths/path_name.yy"
        await WriteJsonFile(gms2PathJson, yyPath);
    }


    // --------------- Export Script (GMS2) ---------------
    async Task ExportScriptsGMS2()
    {
        var tasks = Data.Scripts.Select(script => ExportScriptGMS2(script)).ToList();
        await Task.WhenAll(tasks);
    }

    async Task ExportScriptGMS2(UndertaleScript script)
    {
        ResourceInfo scriptInfo = GetResourceInfo(script);
        if (scriptInfo == null) return;

        // UpdateProgressBar(null, $"Exporting script: {scriptInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
        UpdateProgressBar(null, $"Exporting script: {scriptInfo.Gms2Name}", ++progress, resourceNum);


        string scriptRelativeFolder = Path.GetDirectoryName(scriptInfo.Gms2Path);
        string scriptFolderPath = Path.Combine(projectRootFolder, scriptRelativeFolder);
        Directory.CreateDirectory(scriptFolderPath); // Scripts have a folder containing .gml and .yy

        // Save GML file
        string gmlCode = DecompileCode(script.Code);
        string gmlFilePath = Path.Combine(scriptRelativeFolder, scriptInfo.Gms2Name + ".gml"); // Relative path for writing
        await WriteTextFile(gmlCode, gmlFilePath);

        // Create script YY JSON
        var gms2ScriptJson = new JObject(
            new JProperty("resourceType", "GMScript"),
            new JProperty("resourceVersion", "1.0"), // Use 1.0, GMS2 updates to 2.0 if JSDoc etc. are added later
            new JProperty("name", scriptInfo.Gms2Name),
            new JProperty("isCompatibility", false), // Assume not compatibility mode
            new JProperty("isDnD", false),          // Assume not Drag and Drop
            new JProperty("parent", GetFolderReference(scriptInfo, "GMScript"))
        );

        string yyPath = scriptInfo.Gms2Path; // Path like "scripts/script_name/script_name.yy"
        await WriteJsonFile(gms2ScriptJson, yyPath);
    }


    // --------------- Export Object (GMS2) ---------------
    async Task ExportGameObjectsGMS2()
    {
        var tasks = Data.GameObjects
                        .Where(obj => obj.Name?.Content != "<undefined>") // Skip internal placeholder
                        .Select(obj => ExportGameObjectGMS2(obj))
                        .ToList();
        await Task.WhenAll(tasks);
    }

    async Task ExportGameObjectGMS2(UndertaleGameObject gameObject)
    {
        ResourceInfo objInfo = GetResourceInfo(gameObject);
        if (objInfo == null) return; // Should not happen due to Where clause above

        // UpdateProgressBar(null, $"Exporting object: {objInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
        UpdateProgressBar(null, $"Exporting object: {objInfo.Gms2Name}", ++progress, resourceNum);


        string objRelativeFolder = Path.GetDirectoryName(objInfo.Gms2Path);
        string objFolderPath = Path.Combine(projectRootFolder, objRelativeFolder);
        Directory.CreateDirectory(objFolderPath); // Objects have folders for .yy and event .gml files

        List<JObject> eventListJson = new List<JObject>();
        List<Task> eventTasks = new List<Task>();

        // Process events and create GML files
        for (int eventTypeIndex = 0; eventTypeIndex < gameObject.Events.Count; eventTypeIndex++)
        {
            if (gameObject.Events[eventTypeIndex] == null) continue;

            foreach (var ev in gameObject.Events[eventTypeIndex]) // Actual event instances
            {
                if (ev == null) continue;

                // Decompile actions for this specific event instance
                string eventCode = DecompileActions(ev.Actions, $"{objInfo.Gms2Name}:{EventTypeToStringGMS2(eventTypeIndex)}[{EventSubtypeToStringGMS2(eventTypeIndex, ev.EventSubtype, ev.Other)}]");

                // Only create GML and event entry if there's meaningful code
                if (!string.IsNullOrWhiteSpace(eventCode) && !eventCode.StartsWith("// No executable code"))
                {
                    string gms2EventTypeStr = EventTypeToStringGMS2(eventTypeIndex);
                    string gms2EventSubtypeStr = EventSubtypeToStringGMS2(eventTypeIndex, ev.EventSubtype, ev.Other);
                    string eventFileName = $"{gms2EventTypeStr}_{gms2EventSubtypeStr}.gml";
                    string eventRelativePath = Path.Combine(objRelativeFolder, eventFileName);

                    // Save GML file asynchronously
                    eventTasks.Add(WriteTextFile(eventCode, eventRelativePath));

                    // Create event entry for YY file
                    var eventEntry = new JObject(
                        new JProperty("resourceType", "GMEvent"),
                        new JProperty("resourceVersion", "1.0"),
                        new JProperty("name", ""), // Name is usually empty
                         // Use the mapped subtype string for clarity? No, GMS2 uses numbers here.
                        new JProperty("eventNum", (int)ev.EventSubtype), // Use the raw subtype number
                        new JProperty("eventType", eventTypeIndex), // Use the raw type number
                        new JProperty("isDnD", false)
                    );

                    // Add collision object ID if it's a collision event
                    if (eventTypeIndex == 4) // EventType 4 == Collision
                    {
                        ResourceInfo otherObjInfo = GetResourceInfo(ev.Other); // Other is UndertaleGameObject
                        eventEntry.Add(new JProperty("collisionObjectId", otherObjInfo?.GetReference() ?? null)); // Add reference or null
                    }
                    else
                    {
                         eventEntry.Add(new JProperty("collisionObjectId", null));
                    }
                    eventListJson.Add(eventEntry);
                }
            }
        }

        // Wait for all GML event files to be written
        await Task.WhenAll(eventTasks);

        // Get references for sprite, parent, mask
        ResourceInfo spriteInfo = GetResourceInfo(gameObject.Sprite);
        ResourceInfo parentInfo = GetResourceInfo(gameObject.ParentId);
        ResourceInfo maskInfo = GetResourceInfo(gameObject.TextureMaskId);

        // Map physics shape enum (ensure values match GMS2: 0:Circle, 1:Box, 2:Polygon)
        int physicsShape = gameObject.CollisionShape switch {
             UndertaleGameObject.PhysicsShape.Circle => 0,
             UndertaleGameObject.PhysicsShape.Box => 1,
             UndertaleGameObject.PhysicsShape.Custom => 2,
             _ => 1 // Default to Box if unknown/unmapped
        };


        // Create object YY JSON
        var gms2ObjectJson = new JObject(
            new JProperty("resourceType", "GMObject"),
            new JProperty("resourceVersion", "1.0"), // Use 1.0, GMS2 updates to 1.1 for physics if needed
            new JProperty("name", objInfo.Gms2Name),
            new JProperty("spriteId", spriteInfo?.GetReference() ?? null),
            new JProperty("solid", gameObject.Solid),
            new JProperty("visible", gameObject.Visible),
            new JProperty("managed", true), // Default true
            new JProperty("depth", gameObject.Depth),
            new JProperty("persistent", gameObject.Persistent),
            new JProperty("parentObjectId", parentInfo?.GetReference() ?? null),
            new JProperty("maskSpriteId", maskInfo?.GetReference() ?? null), // Use mask sprite if specified
            new JProperty("physicsObject", gameObject.UsesPhysics),
            new JProperty("physicsSensor", gameObject.IsSensor),
            new JProperty("physicsShape", physicsShape), // Mapped value
            new JProperty("physicsGroup", gameObject.Group),
            new JProperty("physicsDensity", gameObject.Density),
            new JProperty("physicsRestitution", gameObject.Restitution),
            new JProperty("physicsLinearDamping", gameObject.LinearDamping),
            new JProperty("physicsAngularDamping", gameObject.AngularDamping),
            new JProperty("physicsFriction", gameObject.Friction),
            new JProperty("physicsAwake", gameObject.Awake),
            new JProperty("physicsKinematic", gameObject.Kinematic),
            new JProperty("physicsShapePoints", new JArray(
                gameObject.PhysicsVertices.Select(p => new JObject(
                    new JProperty("x", p.X),
                    new JProperty("y", p.Y)
                ))
            )),
            new JProperty("eventList", new JArray(eventListJson)),
            new JProperty("properties", new JArray()), // Object Variables - not extracted
            new JProperty("overriddenProperties", new JArray()), // For child objects - not extracted
            new JProperty("parent", GetFolderReference(objInfo, "GMObject"))
        );

        string yyPath = objInfo.Gms2Path;
        await WriteJsonFile(gms2ObjectJson, yyPath);
    }


    // --------------- Export Timeline (GMS2) ---------------
    async Task ExportTimelinesGMS2()
    {
        var tasks = Data.Timelines.Select(timeline => ExportTimelineGMS2(timeline)).ToList();
        await Task.WhenAll(tasks);
    }

    async Task ExportTimelineGMS2(UndertaleTimeline timeline)
    {
        ResourceInfo tlInfo = GetResourceInfo(timeline);
        if (tlInfo == null) return;

        // UpdateProgressBar(null, $"Exporting timeline: {tlInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
        UpdateProgressBar(null, $"Exporting timeline: {tlInfo.Gms2Name}", ++progress, resourceNum);


        // Timelines don't have their own subfolder
        string timelinesRootPath = Path.Combine(projectRootFolder, "timelines");
        Directory.CreateDirectory(timelinesRootPath);

        List<JObject> momentListJson = new List<JObject>();

        // Process moments (entries)
        foreach (var moment in timeline.Moments) // moment is Tuple<uint, UndertalePointerList<UndertaleAction>>
        {
            uint step = moment.Item1;
            var actions = moment.Item2;

            if (actions == null || actions.Count == 0) continue;

            // Decompile actions into a single code block for the moment
            string momentCode = DecompileActions(actions, $"{tlInfo.Gms2Name}:Step_{step}");

            // Only add moment if there's meaningful code
            if (!string.IsNullOrWhiteSpace(momentCode) && !momentCode.StartsWith("// No executable code"))
            {
                 // Create moment entry for YY file
                 var momentEntry = new JObject(
                     new JProperty("resourceType", "GMMoment"),
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", ""), // Typically unused
                     new JProperty("moment", step), // The step number (time)
                      // GMS2 Timelines embed the event/code directly within the moment entry using "evnt": { ... "codestring": "..." } structure.
                     new JProperty("evnt", new JObject(
                         new JProperty("resourceType", "GMEvent"),
                         new JProperty("resourceVersion", "1.0"),
                         new JProperty("name", ""),
                         // Use a generic event type like Other - User Event 0? Or does GMS2 have a specific type for timeline actions?
                         // Often it's just treated as raw code execution. Let's use Other/UserEvent0 (type 7, subtype 11) as a placeholder convention.
                         new JProperty("eventNum", 11), // User Event 0
                         new JProperty("eventType", 7), // Other
                         new JProperty("isDnD", false),
                         new JProperty("collisionObjectId", null) // Not applicable
                         // How to embed the code? GMS2 examples needed. It might be a direct property or nested further.
                         // It seems GMS2 does *not* directly embed code here, but rather uses the moment as a trigger.
                         // The code for the *object* running the timeline would be in that object's Timeline event.
                         // This export approach (from GMS1 script) might be flawed for GMS2.
                         // Let's try embedding the code as a comment or find a better way.
                         // Alternative: Create a script asset for each moment's code and reference it? Too complex.
                         // Decision: Embed code as comment in description, GMS2 won't execute it automatically. User must move code to object event.
                         // OR, just store the step number, user adds code manually. Let's add the code as a non-standard field for reference.
                         // Update: GMS2.3+ *might* allow code directly. Let's try adding a 'codestring' property inside 'evnt' experimentally.
                         // , new JProperty("codestring", momentCode) // Non-standard, GMS2 might ignore this. Better to comment.
                     )),
                      new JProperty("description", $"Moment at step {step}:\n{momentCode}") // Put code in description for user reference
                 );
                momentListJson.Add(momentEntry);
            }
        }

        // Create timeline YY JSON
        var gms2TimelineJson = new JObject(
            new JProperty("resourceType", "GMTimeline"),
            new JProperty("resourceVersion", "1.0"),
            new JProperty("name", tlInfo.Gms2Name),
            new JProperty("momentList", new JArray(momentListJson)), // Add the processed moments
            new JProperty("parent", GetFolderReference(tlInfo, "GMTimeline"))
        );

        string yyPath = tlInfo.Gms2Path; // Path like "timelines/timeline_name.yy"
        await WriteJsonFile(gms2TimelineJson, yyPath);
    }


    // --------------- Export Room (GMS2) ---------------
    // This is complex due to GMS2's layer system. We create basic layers.
    async Task ExportRoomsGMS2()
    {
        var tasks = Data.Rooms.Select(room => ExportRoomGMS2(room)).ToList();
        await Task.WhenAll(tasks);
    }

    async Task ExportRoomGMS2(UndertaleRoom room)
    {
        ResourceInfo roomInfo = GetResourceInfo(room);
        if (roomInfo == null) return;

        // UpdateProgressBar(null, $"Exporting room: {roomInfo.Gms2Name}", Interlocked.Increment(ref progress), resourceNum);
        UpdateProgressBar(null, $"Exporting room: {roomInfo.Gms2Name}", ++progress, resourceNum);


        string roomRelativeFolder = Path.GetDirectoryName(roomInfo.Gms2Path);
        string roomFolderPath = Path.Combine(projectRootFolder, roomRelativeFolder);
        Directory.CreateDirectory(roomFolderPath); // Rooms have folders for .yy and creation code .gml

        // --- Room Creation Code ---
        string creationCode = "// Room Creation Code\n";
        string creationCodeRelativePath = ""; // Path to the GML file
        if (room.CreationCodeId != null)
        {
            creationCode += DecompileCode(room.CreationCodeId);
             // Only create file if code exists and isn't just comments
            if (!string.IsNullOrWhiteSpace(creationCode) && !creationCode.StartsWith("// Decompilation failed") && !creationCode.StartsWith("// No executable code") && !creationCode.StartsWith("// Empty code")) {
                creationCodeRelativePath = Path.Combine(roomRelativeFolder, $"{roomInfo.Gms2Name}_Create.gml");
                await WriteTextFile(creationCode, creationCodeRelativePath);
            } else {
                creationCode = "// No creation code found.";
                 creationCodeRelativePath = ""; // Reset path if no code file created
            }
        } else {
             creationCode = "// No creation code specified.";
             creationCodeRelativePath = "";
        }


        // --- Room Layers ---
        JArray layersArray = CreateDefaultRoomLayers(roomInfo.Gms2Name, roomInfo);
        JObject instanceLayer = layersArray.FirstOrDefault(l => l["name"]?.ToString() == "Instances") as JObject;
        JObject backgroundLayer = layersArray.FirstOrDefault(l => l["name"]?.ToString() == "Background") as JObject;
        JObject tileLayer = null; // Create dynamically if needed

        // Populate Background Layer
        JArray backgroundLayerElements = new JArray();
        if (backgroundLayer != null) backgroundLayer["elements"] = backgroundLayerElements; // GMS2.3+ uses "elements" array for background sprites
        foreach (var bgInstance in room.Backgrounds)
        {
            if (!bgInstance.Enabled && !bgInstance.Foreground) continue; // Skip disabled backgrounds unless they are foreground

            ResourceInfo bgSpriteInfo = GetResourceInfo(bgInstance.BackgroundDefinition); // Get the spr_bkg_xxx sprite info
            if (bgSpriteInfo == null) continue;

            uint bgColour = bgInstance.Color; // ABGR? GMS2 uses uint ARGB (e.g. 0xFF RRGGBB) or int with sign bit. Let's assume ARGB uint. Check GMS2 docs.
            // Convert Undertale ABGR to ARGB if necessary (or use directly if format matches)
            // Example conversion: (bgColour & 0xFF000000) | ((bgColour & 0x00FF0000) >> 16) | (bgColour & 0x0000FF00) | ((bgColour & 0x000000FF) << 16);
            // For now, assume color format is compatible or default white is acceptable.
            uint gmsColor = 0xFFFFFFFF; // Default white opaque

            // GMS2.3 Background Layer Element
             var bgElement = new JObject(
                 new JProperty("resourceType", "GMBackgroundLayerElement"),
                 new JProperty("resourceVersion", "1.0"),
                 new JProperty("name", Guid.NewGuid().ToString("D")), // Unique name for element
                 new JProperty("spriteId", bgSpriteInfo.GetReference()),
                 new JProperty("colour", gmsColor), // Use color
                 new JProperty("x", bgInstance.X),
                 new JProperty("y", bgInstance.Y),
                 new JProperty("htiled", bgInstance.TileX),
                 new JProperty("vtiled", bgInstance.TileY),
                 new JProperty("stretch", bgInstance.Stretch), // UMT bg has stretch? If not, default false. Assume false.
                 new JProperty("animationFPS", 15.0f), // Use sprite's speed? Default 15.
                 new JProperty("animationSpeedType", 0),
                 new JProperty("userdefinedAnimFPS", false),
                 new JProperty("visible", bgInstance.Enabled), // Visibility
                  // Add position/scale/rotation etc. if available in UMT background instance
                 new JProperty("depth", bgInstance.Foreground ? 50 : 1000000) // GMS2 Layer Element Depth (Foreground higher than instances, background very low)
             );
             // GMS2 Background layers can be foreground or background. Need separate layers?
             // Simpler: Put all in Background layer, rely on depth. But GMS2 uses layer order + depth.
             // Let's assign depth within the layer for now. Foreground backgrounds get higher depth.

             // Choose correct layer based on Foreground flag
             if (bgInstance.Foreground) {
                // Need a foreground layer. Let's add one dynamically if needed.
                JObject fgLayer = layersArray.FirstOrDefault(l => l["name"]?.ToString() == "Foreground") as JObject;
                if(fgLayer == null) {
                    fgLayer = new JObject(
                         new JProperty("resourceType", "GMBackgroundLayer"), new JProperty("resourceVersion", "1.0"),
                         new JProperty("name", "Foreground"), new JProperty("spriteId", null), new JProperty("colour", 4294967295),
                         new JProperty("x", 0), new JProperty("y", 0), new JProperty("htiled", false), new JProperty("vtiled", false), new JProperty("stretch", false),
                         new JProperty("animationFPS", 15.0), new JProperty("animationSpeedType", 0), new JProperty("userdefinedAnimFPS", false),
                         new JProperty("visible", true), new JProperty("depth", 50), // In front of default instances
                         new JProperty("userdefinedDepth", false), new JProperty("inheritLayerDepth", false), new JProperty("inheritLayerSettings", false),
                         new JProperty("gridX", 32), new JProperty("gridY", 32), new JProperty("layers", new JArray()), new JProperty("hierarchyFrozen", false),
                         new JProperty("effectEnabled", true), new JProperty("effectType", null), new JProperty("properties", new JArray()),
                         new JProperty("parentLocked", false), new JProperty("isLocked", false), new JProperty("id", Guid.NewGuid().ToString("D")),
                         new JProperty("elements", new JArray()) // Add elements array
                    );
                    layersArray.Add(fgLayer); // Add to room layers
                }
                ((JArray)fgLayer["elements"]).Add(bgElement);
             } else {
                // Add to standard background layer
                if(backgroundLayer != null) {
                    ((JArray)backgroundLayer["elements"]).Add(bgElement);
                }
             }
        }

        // Populate Tile Layer(s)
        Dictionary<int, JObject> tileLayersByDepth = new Dictionary<int, JObject>(); // Store tile layers by depth
        JArray tileLayerElements = null; // Current tile layer's elements

        foreach (var tileInstance in room.Tiles)
        {
            ResourceInfo bgDefInfo = GetResourceInfo(tileInstance.BackgroundDefinition); // This is the *Tileset* definition (e.g., ts_...)
             if (bgDefInfo == null || bgDefInfo.Gms2ResourceType != "GMTileSet") {
                 // If lookup failed, try finding the tileset info from the generated map
                 string expectedTilesetName = "ts_" + tileInstance.BackgroundDefinition?.Name?.Content;
                 bgDefInfo = GetTilesetInfo(ResourceInfo.SanitizeGMS2Name(expectedTilesetName));
                 if (bgDefInfo == null)
                 {
                      Console.WriteLine($"Warning: Could not find Tileset resource for tile using background '{tileInstance.BackgroundDefinition?.Name?.Content}' in room {roomInfo.Gms2Name}. Skipping tile.");
                      continue;
                 }
             }


            int tileDepth = tileInstance.TileDepth;

            // Get or create tile layer for this depth
            if (!tileLayersByDepth.TryGetValue(tileDepth, out tileLayer))
            {
                 tileLayer = new JObject(
                     new JProperty("resourceType", "GMTileLayer"),
                     new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", $"Tiles_{tileDepth}"), // Name layer by depth
                     new JProperty("tiles", new JObject( // Tile data structure
                         new JProperty("TileData", new JArray()) // Array of tile blob data (complex format)
                         // GMS2 tile data format is tricky. It's often a compressed blob or grid.
                         // Exporting individual tile placements is easier.
                     )),
                     new JProperty("visible", true),
                     new JProperty("depth", tileDepth),
                     new JProperty("userdefinedDepth", true), // Depth is explicit
                     new JProperty("inheritLayerDepth", false),
                     new JProperty("inheritLayerSettings", false),
                     new JProperty("gridX", 32),
                     new JProperty("gridY", 32),
                     new JProperty("layers", new JArray()),
                     new JProperty("hierarchyFrozen", false),
                     new JProperty("effectEnabled", true),
                     new JProperty("effectType", null),
                     new JProperty("properties", new JArray()),
                     new JProperty("parentLocked", false),
                     new JProperty("isLocked", false),
                     new JProperty("id", Guid.NewGuid().ToString("D")),
                     new JProperty("tilesetId", bgDefInfo.GetReference()), // Link to the tileset resource
                     // Simpler tile representation: Use "tilemap" array of individual tiles? (Check GMS2 format)
                     // GMS2 uses a complex `tiles.TileData` blob. Let's try `legacyTiles` or add individual elements if possible.
                     // Update: GMS2.3 seems to use layer elements for tiles too, or the TileData blob.
                     // Let's try adding individual tile elements similar to instances/backgrounds. This might require GMS 2.3+.
                     new JProperty("elements", new JArray()) // Use elements array for individual tiles (GMS 2.3+)
                 );
                 layersArray.Add(tileLayer);
                 tileLayersByDepth[tileDepth] = tileLayer;
                 tileLayerElements = tileLayer["elements"] as JArray;
            } else {
                 tileLayerElements = tileLayer["elements"] as JArray; // Get existing elements array
                 // Ensure the layer uses the correct tileset if multiple are used at same depth (unlikely but possible)
                 // If tileset differs, create a new layer? Or can one layer use multiple? Assume one per layer for simplicity.
                 if (tileLayer["tilesetId"]?["name"]?.ToString() != bgDefInfo.Gms2Name) {
                      Console.WriteLine($"Warning: Multiple tilesets detected at depth {tileDepth} in room {roomInfo.Gms2Name}. Creating separate layers.");
                      // Create a new layer with a unique name
                      string newLayerName = $"Tiles_{tileDepth}_{bgDefInfo.Gms2Name}";
                      tileLayer = new JObject( /* ... copy structure, use new name and ID ... */ );
                      tileLayer["name"] = newLayerName;
                      tileLayer["id"] = Guid.NewGuid().ToString("D");
                      tileLayer["tilesetId"] = bgDefInfo.GetReference();
                      tileLayer["elements"] = new JArray(); // New elements array
                      layersArray.Add(tileLayer);
                      tileLayersByDepth[tileDepth * 1000 + bgDefInfo.GetHashCode()] = tileLayer; // Use composite key
                      tileLayerElements = tileLayer["elements"] as JArray;
                 }
            }


             if (tileLayerElements == null) continue; // Safety check

            // Calculate tile ID within the tileset
            // Tile ID = (sourceY / tileHeight) * columns + (sourceX / tileWidth)
            int tileWidth = tileInstance.Width > 0 ? tileInstance.Width : 1; // Use tile instance W/H or Tileset W/H? Use Tileset's dimensions.
             int tileHeight = tileInstance.Height > 0 ? tileInstance.Height : 1;
             // Need tileset dimensions here
             UndertaleBackground tilesetBg = Data.Backgrounds.FirstOrDefault(b => GetResourceInfo(b)?.Gms2Name == bgDefInfo.Gms2Name.Replace("ts_", "spr_bkg_")); // Find original background
             int tsTileWidth = tilesetBg?.TileWidth ?? tileWidth;
             int tsTileHeight = tilesetBg?.TileHeight ?? tileHeight;
             int tsHSep = tilesetBg?.TileHSep ?? 0;
             int tsVSep = tilesetBg?.TileVSep ?? 0;
             int tsWidth = tilesetBg?.Texture?.TargetWidth ?? 0;

             if (tsTileWidth <= 0 || tsTileHeight <= 0 || tsWidth <= 0) {
                 Console.WriteLine($"Warning: Invalid tileset dimensions for '{bgDefInfo.Gms2Name}'. Skipping tile ID calculation.");
                 continue;
             }

            int columns = (tsWidth + tsHSep) > 0 ? (tsWidth - tsTileWidth + tsHSep) / (tsTileWidth + tsHSep) + 1 : 0;
            int tileIndex = ((tileInstance.SourceY / (tsTileHeight + tsVSep)) * columns) + (tileInstance.SourceX / (tsTileWidth + tsHSep));


            // GMS2.3 Tile Layer Element
            var tileElement = new JObject(
                new JProperty("resourceType", "GMTileLayerElement"),
                new JProperty("resourceVersion", "1.0"),
                new JProperty("name", Guid.NewGuid().ToString("D")), // Unique element name
                new JProperty("x", tileInstance.X),
                new JProperty("y", tileInstance.Y),
                new JProperty("id", tileIndex), // Index of the tile within the tileset
                new JProperty("colour", tileInstance.Color), // ABGR or ARGB? Assume ARGB uint 0xFFRRGGBB
                new JProperty("scaleX", tileInstance.ScaleX),
                new JProperty("scaleY", tileInstance.ScaleY),
                new JProperty("rotation", 0) // Tiles typically aren't rotated this way in GMS1/UMT? Default 0.
                // Note: GMS2 TileData blob is more efficient for large maps. This element approach might be slow in IDE for huge rooms.
            );
            tileLayerElements.Add(tileElement);
        }


        // Populate Instance Layer
        JArray instanceLayerInstances = new JArray();
        if (instanceLayer != null) instanceLayer["instances"] = instanceLayerInstances;
        foreach (var objInstance in room.GameObjects)
        {
            ResourceInfo objDefInfo = GetResourceInfo(objInstance.ObjectDefinition);
            if (objDefInfo == null) continue;

            // Instance Creation Code
            string instanceCode = "";
            string instanceCodeRelativePath = "";
            if (objInstance.CreationCode != null)
            {
                 instanceCode = $"// Instance Creation Code for inst_{objInstance.InstanceID:X}\n" + DecompileCode(objInstance.CreationCode);
                  // Only save if meaningful code exists
                 if (!string.IsNullOrWhiteSpace(instanceCode) && !instanceCode.StartsWith("// Decompilation failed") && !instanceCode.StartsWith("// No executable code") && !instanceCode.StartsWith("// Empty code")) {
                     instanceCodeRelativePath = Path.Combine(roomRelativeFolder, $"inst_{objInstance.InstanceID:X}_Create.gml");
                     await WriteTextFile(instanceCode, instanceCodeRelativePath);
                 } else {
                      instanceCodeRelativePath = ""; // Reset path if no file created
                 }
            }

            instanceLayerInstances.Add(new JObject(
                new JProperty("resourceType", "GMInstance"),
                new JProperty("resourceVersion", "1.0"),
                new JProperty("name", $"inst_{objInstance.InstanceID:X}"), // Standard GMS naming convention inst_XXXX
                new JProperty("x", objInstance.X),
                new JProperty("y", objInstance.Y),
                new JProperty("objectId", objDefInfo.GetReference()), // Link to the object resource
                new JProperty("creationCodeFile", instanceCodeRelativePath), // Path to instance creation code GML file (relative to project root)
                new JProperty("creationCodeType", "Relative"), // Path type
                new JProperty("colour", objInstance.Color), // ABGR or ARGB uint? Assume 0xFFRRGGBB
                new JProperty("rotation", objInstance.Rotation),
                new JProperty("scaleX", objInstance.ScaleX),
                new JProperty("scaleY", objInstance.ScaleY),
                new JProperty("imageIndex", 0), // Default image index
                new JProperty("imageSpeed", 1.0f), // Default image speed
                new JProperty("inheritCode", false), // Inherit creation code? Usually false for placed instances.
                new JProperty("inheritCreationOrder", false), // GMS2.3+ specific? Default false.
                new JProperty("properties", new JArray()), // Instance variable overrides - not extracted
                new JProperty("isLocked", false), // Default unlocked
                new JProperty("id", $"inst_{objInstance.InstanceID:X}") // Instance ID can be the name itself? GMS uses GUIDs internally. Use name for readability.
                                                                       // GMS2 internal format uses a proper GUID here. Generate one.
                 , new JProperty("ignore", false) // GMS2.3+? Default false.
                 , new JProperty("inheritItemSettings", false) // GMS2.3+? Default false.
                 , new JProperty("layerId", instanceLayer?["id"]?.ToString() ?? "") // Link back to instance layer ID
                 , new JProperty("m_originalName", $"inst_{objInstance.InstanceID:X}") // Store original name if needed?
                 // Ensure GUID for ID
                  , new JProperty("id", Guid.NewGuid().ToString("D")) // Use a proper GUID for the instance ID
            ));
        }


        // --- Room Settings ---
        // Map UndertaleRoom.RoomEntryFlags to GMS2 booleans
        bool enableViewsFlag = room.Flags.HasFlag(UndertaleRoom.RoomEntryFlags.EnableViews);
        bool clearViewBackgroundFlag = room.Flags.HasFlag(UndertaleRoom.RoomEntryFlags.ClearDisplayBuffer); // ClearDisplayBuffer seems closer to GMS2's clearViewBackground
        bool drawBgColorFlag = room.Flags.HasFlag(UndertaleRoom.RoomEntryFlags.DrawBgColor); // Equivalent to GMS2's drawBgColor

        // --- Views ---
        JArray viewsArray = new JArray();
        for (int i = 0; i < room.Views.Count; i++)
        {
            var view = room.Views[i];
            if (!view.Enabled && !enableViewsFlag) continue; // Only include enabled views, or all if enableViews is globally set

            ResourceInfo viewObjFollowInfo = GetResourceInfo(view.ObjectId);

            viewsArray.Add(new JObject(
                new JProperty("resourceType", "GMView"), // Is this correct? GMS2 uses view settings within the room YY.
                new JProperty("resourceVersion", "1.0"), // Check format. Views are part of room settings.
                // View properties directly within view entry object:
                 new JProperty("inherit", false), // Not inheriting from parent room
                 new JProperty("visible", view.Enabled),
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
                 new JProperty("objectId", viewObjFollowInfo?.GetReference() ?? null) // Object to follow
            ));
        }

        // --- Physics Settings ---
        bool physicsEnabled = room.World; // Assuming 'World' boolean enables physics
        float physicsGravityX = room.GravityX;
        float physicsGravityY = room.GravityY;
        float physicsPixToMeters = room.MetersPerPixel > 0 ? room.MetersPerPixel : 0.1f; // Use value or default 0.1


        // --- Final Room YY JSON ---
        var gms2RoomJson = new JObject(
            new JProperty("resourceType", "GMRoom"),
            new JProperty("resourceVersion", "1.0"), // Use 1.0, GMS2 updates for physics etc.
            new JProperty("name", roomInfo.Gms2Name),
            // --- Room Settings ---
            new JProperty("roomSettings", new JObject(
                 new JProperty("resourceType", "GMRoomSettings"), new JProperty("resourceVersion", "1.0"),
                 new JProperty("inheritRoomSettings", false),
                 new JProperty("Width", room.Width),
                 new JProperty("Height", room.Height),
                 new JProperty("persistent", room.Persistent)
            )),
             // --- View Settings ---
            new JProperty("viewSettings", new JObject(
                 new JProperty("resourceType", "GMViewSettings"), new JProperty("resourceVersion", "1.0"),
                 new JProperty("inheritViewSettings", false),
                 new JProperty("enableViews", enableViewsFlag),
                 new JProperty("clearViewBackground", clearViewBackgroundFlag), // Mapped flag
                 new JProperty("clearDisplayBuffer", drawBgColorFlag) // Mapped flag - GMS2 uses drawBgColor, let's map DrawBgColor flag here. ClearDisplayBuffer is closer to clear view background. Let's swap.
                 // Correct mapping: DrawBgColor -> drawBgColor, ClearDisplayBuffer -> clearViewBackground? Needs verification.
                 // Let's assume: DrawBgColor -> clearDisplayBuffer (for GMS2), ClearDisplayBuffer UMT -> clearViewBackground GMS2
            )),
            // --- Layer Settings (Layers array) ---
            new JProperty("layers", layersArray), // The array of layers we built
            // --- Physics Settings ---
             new JProperty("physicsSettings", new JObject(
                 new JProperty("resourceType", "GMRoomPhysicsSettings"), new JProperty("resourceVersion", "1.0"),
                 new JProperty("inheritPhysicsSettings", false),
                 new JProperty("PhysicsWorld", physicsEnabled),
                 new JProperty("PhysicsWorldGravityX", physicsGravityX),
                 new JProperty("PhysicsWorldGravityY", physicsGravityY),
                 new JProperty("PhysicsWorldPixToMetres", physicsPixToMeters)
            )),
             // --- Instance Creation Order ---
             new JProperty("instanceCreationOrder", new JArray()), // Array of instance references in creation order - complex to extract, leave empty?
             // --- Room Creation Code ---
            new JProperty("creationCodeFile", creationCodeRelativePath), // Path to GML file
             new JProperty("creationCodeType", "Relative"),
             // --- Other Settings ---
             new JProperty("isDnd", false),
             new JProperty("volume", 1.0f),
             new JProperty("parentRoom", null), // No parent room assumed
             new JProperty("SequenceId", null), // Not using room sequence features here
             new JProperty("m_serialiseData", null), // Internal GMS data? Leave null.
             new JProperty("gridSettings", new JObject( // Default grid settings
                new JProperty("resourceType", "GMGridSettings"), new JProperty("resourceVersion", "1.0"),
                new JProperty("inheritGridSettings", false), new JProperty("isGridEnabled", true),
                new JProperty("gridX", 32), new JProperty("gridY", 32), new JProperty("snapGridX", 32), new JProperty("snapGridY", 32)
             )),
            new JProperty("parent", GetFolderReference(roomInfo, "GMRoom"))
        );

        string yyPath = roomInfo.Gms2Path;
        await WriteJsonFile(gms2RoomJson, yyPath);
    }


    // --------------- Generate project file (.yyp) ---------------
    async Task GenerateProjectFileGMS2()
    {
         UpdateProgressBar(null, $"Generating project file: {projectName}.yyp", ++progress, resourceNum);

         List<JObject> resourceList = new List<JObject>();

         // Collect all mapped resources (sprites, sounds, objects, etc.)
         foreach (var kvp in resourceMapping)
         {
              if (kvp.Value != null) // Ensure ResourceInfo exists
              {
                  resourceList.Add(new JObject(
                      new JProperty("id", kvp.Value.GetId()), // Use GetId which returns { name: "...", path: "..." }
                      new JProperty("order", 0) // Placeholder order, GMS2 manages this
                  ));
              }
         }
        // Collect generated tilesets
         foreach (var kvp in generatedTilesetMapping)
         {
              if (kvp.Value != null)
              {
                  resourceList.Add(new JObject(
                      new JProperty("id", kvp.Value.GetId()),
                      new JProperty("order", 0)
                  ));
              }
         }
         // Collect folders
         foreach (var kvp in folderMapping)
         {
              if (kvp.Value != null)
              {
                   resourceList.Add(new JObject(
                       new JProperty("id", kvp.Value.GetId()),
                       new JProperty("order", 0)
                   ));
              }
         }


        // --- Create Folders Structure for IDE View ---
         JArray foldersArray = CreateFolderStructureJson();


         // --- Create Configurations (Default + Optional Texture Groups) ---
         JArray configsArray = new JArray();
         // Default config
         configsArray.Add(new JObject(
             new JProperty("name", "Default"),
             new JProperty("children", new JArray()) // Child configs for platform specifics, empty for default
         ));
         // Optional: Add configs based on texture groups
         if (EXPORT_TEXTURE_GROUPS_AS_CONFIGS)
         {
             foreach (var groupInfo in Data.TextureGroupInfo)
             {
                 if (groupInfo != null && !string.IsNullOrWhiteSpace(groupInfo.Name))
                 {
                     string configName = ResourceInfo.SanitizeGMS2Name(groupInfo.Name);
                      if (string.IsNullOrWhiteSpace(configName) || configName == "_") configName = $"TexGroup_{Data.TextureGroupInfo.IndexOf(groupInfo)}";

                     // Avoid duplicate config name with Default
                     if (configName.ToLowerInvariant() != "default")
                     {
                         configsArray.Add(new JObject(
                             new JProperty("name", configName),
                             new JProperty("children", new JArray())
                         ));
                     }
                 }
             }
         }


         // --- Create Audio Group Definitions ---
         JArray audioGroupsArray = new JArray();
         // Add default group first
         audioGroupsArray.Add(new JObject(
             new JProperty("resourceType", "GMAudioGroup"), new JProperty("resourceVersion", "1.0"),
             new JProperty("name", "audiogroup_default"), new JProperty("targets", -1) // All targets by default
         ));
         // Add groups from UMT data
         for(int i = 1; i < Data.AudioGroups.Count; i++) // Start from 1, 0 is default/handled above? Check UMT structure. Assume 0 is default.
         {
             var group = Data.AudioGroups[i];
             if (group != null && !string.IsNullOrWhiteSpace(group.Name?.Content))
             {
                 string groupName = ResourceInfo.SanitizeGMS2Name(group.Name.Content);
                 if (string.IsNullOrWhiteSpace(groupName) || groupName == "_") groupName = $"audiogroup_{i}";

                 // Ensure name doesn't clash with default
                 if (groupName.ToLowerInvariant() != "audiogroup_default")
                 {
                     audioGroupsArray.Add(new JObject(
                         new JProperty("resourceType", "GMAudioGroup"), new JProperty("resourceVersion", "1.0"),
                         new JProperty("name", groupName),
                         new JProperty("targets", -1) // Assume all targets unless more info available
                     ));
                 }
             }
         }

         // --- Create Texture Group Definitions ---
         JArray textureGroupsArray = new JArray();
         // Add default group first
         textureGroupsArray.Add(new JObject(
             new JProperty("resourceType", "GMTextureGroup"), new JProperty("resourceVersion", "1.0"),
             new JProperty("name", "Default"),
             new JProperty("isScaled", true), new JProperty("compressFormat", "bz2"), // Defaults
             new JProperty("loadType", "default"), new JProperty("directory", ""), new JProperty("autocrop", true),
             new JProperty("border", 2), new JProperty("mipsToGenerate", 0), new JProperty("groupParent", null),
             new JProperty("targets", -1)
         ));
          // Add groups from UMT data
         foreach (var groupInfo in Data.TextureGroupInfo)
         {
             if (groupInfo != null && !string.IsNullOrWhiteSpace(groupInfo.Name))
             {
                 string groupName = ResourceInfo.SanitizeGMS2Name(groupInfo.Name);
                 if (string.IsNullOrWhiteSpace(groupName) || groupName == "_") groupName = $"TexGroup_{Data.TextureGroupInfo.IndexOf(groupInfo)}";

                 // Ensure name doesn't clash with default
                 if (groupName.ToLowerInvariant() != "default")
                 {
                     textureGroupsArray.Add(new JObject(
                         new JProperty("resourceType", "GMTextureGroup"), new JProperty("resourceVersion", "1.0"),
                         new JProperty("name", groupName),
                         new JProperty("isScaled", true), new JProperty("compressFormat", "bz2"), // Defaults, use UMT info if available
                         new JProperty("loadType", "default"), new JProperty("directory", ""), new JProperty("autocrop", true),
                         new JProperty("border", 2), new JProperty("mipsToGenerate", 0), new JProperty("groupParent", null), // Parent group reference? None here.
                         new JProperty("targets", -1) // Assume all targets
                     ));
                 }
             }
         }


        // --- Main YYP JSON Structure ---
         var yypJson = new JObject(
             new JProperty("resourceType", "GMProject"),
             new JProperty("resourceVersion", "1.7"), // Use a recent GMS2 project version
             new JProperty("name", projectName), // Name of the project (folder name)
             new JProperty("AudioGroups", audioGroupsArray),
             new JProperty("configs", configsArray),
             new JProperty("defaultScriptType", 1), // 0=GML Legacy, 1=GML 2020+
             new JProperty("Folders", foldersArray), // IDE folder structure
             new JProperty("IncludedFiles", new JArray()), // No included files extracted
             new JProperty("isEcma", false), // Not using EcmaScript features? Default false.
             new JProperty("LibraryEcosystemSettings", new JObject( // GMS 2.3.7+
                  new JProperty("IsExternal", false), new JProperty("SourceLocation", ""), new JProperty("DownloadTarget", "")
             )),
             new JProperty("MetaData", new JObject(new JProperty("IDEVersion", "2023.8.0.98"))), // Example IDE version, adjust if needed
             new JProperty("resources", new JArray(resourceList.OrderBy(r => r["id"]["path"].ToString()))), // Add sorted resource list
             new JProperty("RoomOrderNodes", new JArray( // Define room order
                  Data.Rooms.Select(room => {
                      ResourceInfo info = GetResourceInfo(room);
                      return info == null ? null : new JObject(new JProperty("roomId", info.GetReference()));
                  }).Where(r => r != null)
              )),
             new JProperty("TextureGroups", textureGroupsArray),
              new JProperty("TutorialState", new JObject( // Default tutorial state
                  new JProperty("isComplete", true), new JProperty("hasBeenShown", true),
                  new JProperty("TutorialName", ""), new JProperty("TutorialPage", 0)
              ))
         );

         string yypPath = Path.Combine(projectRootFolder, projectName + ".yyp");
         await WriteJsonFile(yypJson, yypPath.Replace(projectRootFolder + Path.DirectorySeparatorChar, "")); // Write relative to root
    }


     // Helper to create the JSON array for the IDE folder structure in the YYP file
     JArray CreateFolderStructureJson()
     {
         if (!CREATE_IDE_SUBFOLDERS) {
             // Create only top-level folders if not using subfolders
              return new JArray(
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Sprites"), new JProperty("folderPath", "folders/Sprites.yy"), new JProperty("order", 1)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Tile Sets"), new JProperty("folderPath", "folders/Tile Sets.yy"), new JProperty("order", 2)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Sounds"), new JProperty("folderPath", "folders/Sounds.yy"), new JProperty("order", 3)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Paths"), new JProperty("folderPath", "folders/Paths.yy"), new JProperty("order", 4)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Scripts"), new JProperty("folderPath", "folders/Scripts.yy"), new JProperty("order", 5)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Fonts"), new JProperty("folderPath", "folders/Fonts.yy"), new JProperty("order", 6)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Timelines"), new JProperty("folderPath", "folders/Timelines.yy"), new JProperty("order", 7)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Objects"), new JProperty("folderPath", "folders/Objects.yy"), new JProperty("order", 8)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Rooms"), new JProperty("folderPath", "folders/Rooms.yy"), new JProperty("order", 9)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Notes"), new JProperty("folderPath", "folders/Notes.yy"), new JProperty("order", 10)),
                 new JObject(new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"), new JProperty("name", "Extensions"), new JProperty("folderPath", "folders/Extensions.yy"), new JProperty("order", 11))
              );
         }

         // Build hierarchy from folderMapping
         var folderJsonList = new List<JObject>();
         // Create JObject for each folder, ensuring sanitized name and correct folderPath
         foreach(var kvp in folderMapping.OrderBy(f => f.Key)) // Sort by key for consistent order
         {
             ResourceInfo folderInfo = kvp.Value;
             string folderPathYY = $"folders/{folderInfo.OriginalPath.Replace(IDE_FOLDER_SEPARATOR, "/")}.yy"; // GMS2 folder path convention
              // Update the folder info's path
              folderInfo.Gms2Path = folderPathYY;

             folderJsonList.Add(new JObject(
                 new JProperty("resourceType", "GMFolder"),
                 new JProperty("resourceVersion", "1.0"),
                 new JProperty("name", folderInfo.OriginalPath), // Use the full path as the unique name for the folder resource itself
                 new JProperty("folderPath", folderPathYY),
                 new JProperty("order", GetFolderOrder(folderInfo.OriginalPath)) // Assign order based on type/path
             ));
         }
          // Add top-level standard folders if they weren't created implicitly
          EnsureStandardFoldersExist(folderJsonList);

         return new JArray(folderJsonList);
     }

      // Ensures standard top-level GMS2 folders are included in the JSON list
     void EnsureStandardFoldersExist(List<JObject> folderJsonList)
     {
         string[] standardFolders = { "Sprites", "Tile Sets", "Sounds", "Paths", "Scripts", "Fonts", "Timelines", "Objects", "Rooms", "Notes", "Extensions" };
         int baseOrder = 1;
         foreach (string folderName in standardFolders)
         {
             string folderPath = $"folders/{folderName}.yy";
             if (!folderJsonList.Any(f => f["folderPath"]?.ToString() == folderPath))
             {
                 folderJsonList.Add(new JObject(
                     new JProperty("resourceType", "GMFolder"), new JProperty("resourceVersion", "1.0"),
                     new JProperty("name", folderName), // Name matches last part of folderPath
                     new JProperty("folderPath", folderPath),
                     new JProperty("order", baseOrder++)
                 ));
             }
             // Increment baseOrder even if folder exists to maintain relative order
             else baseOrder++;
         }
          // Re-sort based on order after ensuring all exist? Optional.
          folderJsonList.Sort((a, b) => (a["order"]?.ToObject<int>() ?? 99) - (b["order"]?.ToObject<int>() ?? 99));
     }


     // Basic ordering for folders in the IDE view
     int GetFolderOrder(string originalPath) {
         string topLevel = originalPath.Split(new[] { IDE_FOLDER_SEPARATOR }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
          int depth = originalPath.Count(c => c == IDE_FOLDER_SEPARATOR[0]); // Crude depth calculation

         int baseOrder = topLevel.ToLowerInvariant() switch {
             "sprites" => 100,
             "tilesets" or "tile sets" => 200,
             "sounds" => 300,
             "paths" => 400,
             "scripts" => 500,
             "fonts" => 600,
             "timelines" => 700,
             "objects" => 800,
             "rooms" => 900,
             "notes" => 1000,
             "extensions" => 1100,
             _ => 10000 // Unknown folders last
         };
         return baseOrder + depth; // Order by type, then depth
     }


     // Helper to get the parent folder reference for a resource's YY file
     JObject GetFolderReference(ResourceInfo resourceInfo, string resourceType)
     {
         if (!CREATE_IDE_SUBFOLDERS || !resourceInfo.OriginalPath.Contains(IDE_FOLDER_SEPARATOR))
         {
             // No subfolders, use default top-level folder based on type
             string topLevelFolderName = resourceType switch {
                 "GMSprite" => "Sprites",
                 "GMTileSet" => "Tile Sets",
                 "GMSound" => "Sounds",
                 "GMPath" => "Paths",
                 "GMScript" => "Scripts",
                 "GMFont" => "Fonts",
                 "GMTimeline" => "Timelines",
                 "GMObject" => "Objects",
                 "GMRoom" => "Rooms",
                 _ => "Sprites" // Fallback
             };
             return new JObject(
                 new JProperty("name", topLevelFolderName),
                 new JProperty("path", $"folders/{topLevelFolderName}.yy")
             );
         }
         else
         {
             // Resource is in a subfolder, find its parent folder resource
             string parentFolderPath = Path.GetDirectoryName(resourceInfo.OriginalPath.Replace(IDE_FOLDER_SEPARATOR, Path.DirectorySeparatorChar.ToString()));
              string parentFolderKey = GetFolderResourceKey(parentFolderPath, resourceType); // Find the key for the parent

              if (folderMapping.TryGetValue(parentFolderKey, out ResourceInfo parentFolderInfo))
              {
                   return parentFolderInfo.GetReference(); // Return reference to the parent folder's YY definition
              }
              else
              {
                   // Parent folder wasn't registered? Fallback to top-level.
                   Console.WriteLine($"Warning: Could not find parent folder '{parentFolderPath}' for resource '{resourceInfo.OriginalPath}'. Assigning to top-level.");
                    return GetFolderReference(resourceInfo, resourceType); // Recursive call with simplified condition
              }
         }
     }

} // End of ExportToGMS2Project class

// --- Entry Point for UMT Script Runner ---
// UMT script runner expects a static `Main` or instance method like `Run`.
// We'll wrap the execution in a static Main method if needed, or UMT might call Run directly.
// Assuming UMT calls an instance method `Run`:
// Create an instance and call Run.

// Example of how UMT might initiate (this part is usually handled by UMT itself):
