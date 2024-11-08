using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class AssetStripperWindow : EditorWindow
{
    [MenuItem("Moonlit/Asset Stripper/Prepare Stripping", priority = 0)]
    private static void CreateWindow() => GetWindow<AssetStripperWindow>();

    private enum WindowEvents { StripProject = 1, SelectAll, DeselectAll, InvertSelection, ScanProject }
    private class AssetListing
    {
        public string Path;
        public Texture Icon;
        public bool IsMarkedForDelete, IsExpanded;
        public readonly Dictionary<string, AssetListing> Children = new Dictionary<string, AssetListing>();
    }

    private static readonly Color BetterBlue = new Color(75 / 255f, 175f / 255f, 1f);
    private static Dictionary<string, AssetListing> sDeleteAssetEntries = new Dictionary<string, AssetListing>();
    private static Vector2 sScroll;
    private static string sFilterWords;
    private static bool sIsIncludingScripts, sIsIncludingEditorScripts;

    private void OnGUI()
    {
        WindowEvents events = default;
        var selectedAsset = string.Empty;
        var isWaitingForScan = sDeleteAssetEntries == null || sDeleteAssetEntries.Count == 0;
        var isHiddenMetaFiles = VersionControlSettings.mode.ToLower().Contains("hidden");

        /*- UI -*/
        if (isHiddenMetaFiles) {
            EditorGUILayout.HelpBox($"Unable to Scan Project! .meta files are currently set to \"Hidden\" in project! Update \"Version Control\" under [Edit->Project Settings->Editor]", MessageType.Error);
        }
        using (new EditorGUILayout.VerticalScope("box")) {
            if (!sIsIncludingScripts) EditorGUILayout.LabelField("Showing assets that are not referenced by the Editor.", EditorStyles.largeLabel);
            if (!sIsIncludingScripts) EditorGUILayout.LabelField("Currently ignoring Game + Editor Scripts.");
            if (sIsIncludingScripts && !sIsIncludingEditorScripts) EditorGUILayout.LabelField("Showing assets that are not referenced in a build of the game.", EditorStyles.largeLabel);
            if (sIsIncludingScripts && !sIsIncludingEditorScripts) EditorGUILayout.LabelField("Currently ignoring Editor Scripts.");
            if (sIsIncludingScripts && sIsIncludingEditorScripts) EditorGUILayout.LabelField("Showing assets that are not referenced at all by the Game or Editor.", EditorStyles.largeLabel);
            if (sIsIncludingScripts && sIsIncludingEditorScripts) EditorGUILayout.LabelField("text/json/jar files may appear here that get used in nonspecific ways.");
        }
        using (new EditorGUILayout.HorizontalScope()) {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Backup By Creating Package", GUILayout.Width(170));
            Runtime.EditorPrefsBackupAssetsByCreatingPackage = EditorGUILayout.Toggle(Runtime.EditorPrefsBackupAssetsByCreatingPackage);
            GUILayout.FlexibleSpace();
        }
        using (new EditorGUILayout.HorizontalScope()) {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Include Game Scripts", GUILayout.Width(170));
            sIsIncludingScripts = EditorGUILayout.Toggle(sIsIncludingScripts);
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                var infoContent = new GUIContent(EditorGUIUtility.IconContent("console.infoicon").image);
                var infoStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.blue }, fontSize = 12, alignment = TextAnchor.MiddleCenter };
                GUILayout.Label(infoContent, infoStyle, GUILayout.Width(20), GUILayout.Height(15));
                EditorGUILayout.LabelField("Including scripts can take significantly longer on larger projects.", EditorStyles.wordWrappedLabel);
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledGroupScope(!sIsIncludingScripts)) {
            GUILayout.Space(40);
            EditorGUILayout.LabelField("Include Editor Scripts", GUILayout.Width(140));
            sIsIncludingEditorScripts = EditorGUILayout.Toggle(sIsIncludingEditorScripts);
            GUILayout.FlexibleSpace();
        }
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Reference Folders");
        Runtime.EditorPrefsReferenceFolder = EditorGUILayout.TextField(Runtime.EditorPrefsReferenceFolder);
        using (new EditorGUILayout.HorizontalScope("box")) {
            EditorGUILayout.LabelField("Separate Reference Folders by ';'. ex. [Assets/Plugins;Assets/Gizmos]");
        }
        sFilterWords = EditorGUILayout.TextField("Filters:", sFilterWords, EditorStyles.toolbarSearchField);
        using (new EditorGUILayout.HorizontalScope("box")) {
            EditorGUILayout.LabelField("Use keywords to filter on files (not folders). Use '-' before a keyword to exclude it. (ex. '.png' filters on png files)");
        }
        EditorGUILayout.Space();
        using (var scrollScope = new EditorGUILayout.ScrollViewScope(sScroll)) {
            sScroll = scrollScope.scrollPosition;
            if (!isWaitingForScan) {
                foreach (var asset in sDeleteAssetEntries.Values) {
                    DrawAssetItem(asset, 0, ref selectedAsset);
                }
            }
        }
        if (!isWaitingForScan) {
            EditorGUILayout.LabelField("Select unneeded assets for removal. Click on assets to view them in the Inspector.");
            using (new EditorGUILayout.HorizontalScope()) {
                events = GUILayout.Button("Select All") ? WindowEvents.SelectAll : events;
                events = GUILayout.Button("Deselect All") ? WindowEvents.DeselectAll : events;
                events = GUILayout.Button("Invert Selection") ? WindowEvents.InvertSelection : events;
            }
        }
        using (new EditorGUILayout.HorizontalScope("box")) {
            var customButtonStyle = new GUIStyle(GUI.skin.button)
            {
                normal = { background = CreateColorTexture(new Color(0.2f, 0.2f, 0.2f)), textColor = Color.white },
                active = { background = CreateColorTexture(new Color(0.1f, 0.1f, 0.1f)), textColor = Color.white },
                hover = { textColor = Color.white },
                fontSize = 12,
                padding = new RectOffset(10, 10, 5, 5),
                margin = new RectOffset(2, 2, 2, 2),
            };
            if (!isHiddenMetaFiles) {
                events = GUILayout.Button("Scan Project", customButtonStyle, GUILayout.Width(200)) ? WindowEvents.ScanProject : events;
            }
            EditorGUILayout.Space();
            if (!isWaitingForScan) {
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = BetterBlue;
                var ok = GUILayout.Button("Strip Assets", customButtonStyle, GUILayout.Width(140));
                GUI.backgroundColor = originalColor;
                if (ok) {
                    events = EditorUtility.DisplayDialog("Are you sure?", "This will remove the chosen assets from the project.", "OK", "Cancel") ? WindowEvents.StripProject : events;
                }
            }
        }

        /*- Handle Events -*/
        if (!string.IsNullOrWhiteSpace(selectedAsset)) {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(selectedAsset);
        }
        if (events == WindowEvents.SelectAll || events == WindowEvents.DeselectAll) {
            foreach (var kv in sDeleteAssetEntries) {
                MarkAllChildrenAsDelete(kv.Value, events == WindowEvents.SelectAll);
            }
        }
        if (events == WindowEvents.InvertSelection) {
            foreach (var kv in sDeleteAssetEntries) {
                MarkAllChildrenAsDeleteInvert(kv.Value);
            }
        }
        if (events == WindowEvents.ScanProject) {
            var assetDeleteFileGUIDList = Runtime.GatherAssetsToStrip(sIsIncludingScripts, sIsIncludingEditorScripts);
            sDeleteAssetEntries = PopulateDeleteFileListing(assetDeleteFileGUIDList);
        }
        if (events == WindowEvents.StripProject) {
            EditorApplication.delayCall += () =>
            {
                try {
                    var filePathsToThoseMarkedForDelete = PathsToThoseMarkedForDelete(sDeleteAssetEntries);
                    var backupPackageName = Path.Combine("BackupStrippedAssets", $"backup{DateTime.Now:yyyyMMddHHmm}.unitypackage");
                    if (filePathsToThoseMarkedForDelete.Length == 0) return;

                    if (Runtime.EditorPrefsBackupAssetsByCreatingPackage) {
                        EditorUtility.DisplayProgressBar("Creating backup", backupPackageName, 0);
                        Directory.CreateDirectory("BackupStrippedAssets");
                        AssetDatabase.ExportPackage(filePathsToThoseMarkedForDelete, backupPackageName);
                        Debug.Log($"Backup of stripped assets will be at: '{backupPackageName}'");
                    }
                    for (var i = 0; i < filePathsToThoseMarkedForDelete.Length; i++) {
                        var pathToDelete = filePathsToThoseMarkedForDelete[i];
                        EditorUtility.DisplayProgressBar("Stripping assets", pathToDelete, (float)i / filePathsToThoseMarkedForDelete.Length);
                        AssetDatabase.DeleteAsset(pathToDelete);
                        if (File.Exists(pathToDelete)) {
                            File.Delete(pathToDelete);
                        }
                    }
                    foreach (var dir in Directory.GetDirectories("Assets")) {
                        RemoveEmptyDirectories(dir);
                    }
                    EditorUtility.DisplayProgressBar("Stripping assets", "Finished!", 1);
                    AssetDatabase.Refresh();
                }
                finally {
                    EditorUtility.ClearProgressBar();
                }
                Close();
            };
        }
    }

    private static void RemoveEmptyDirectories(string path)
    {
        foreach (var dir in Directory.GetDirectories(path)) {
            RemoveEmptyDirectories(dir);
        }

        var allFiles = new List<string>();
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly)) { // Ignore meta files
            if (Path.GetExtension(file) != ".meta") allFiles.Add(file);
        }
        if (allFiles.Count == 0 && Directory.GetDirectories(path).Length == 0) {
            FileUtil.DeleteFileOrDirectory(path);
            FileUtil.DeleteFileOrDirectory(AssetDatabase.GetTextMetaFilePathFromAssetPath(path));
        }
    }

    private static Texture2D CreateColorTexture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private static void DrawAssetItem(AssetListing asset, int indent, ref string selectedAsset)
    {
        if (IsMatchingFilter(asset.Path)) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 10);

            if (asset.Children.Count > 0) {
                asset.IsExpanded = EditorGUILayout.Foldout(asset.IsExpanded, string.Empty, true);
            } else {
                GUILayout.Space(15); // Align with foldout arrow
            }

            var wasMarkedForDelete = asset.IsMarkedForDelete;
            asset.IsMarkedForDelete = EditorGUILayout.Toggle(asset.IsMarkedForDelete, GUILayout.Width(20));
            if (wasMarkedForDelete != asset.IsMarkedForDelete) {
                MarkAllChildrenAsDelete(asset, asset.IsMarkedForDelete);
            }

            GUILayout.Label(asset.Icon, GUILayout.Width(20), GUILayout.Height(20));
            selectedAsset = GUILayout.Button(Path.GetFileName(asset.Path), EditorStyles.label) ? asset.Path : selectedAsset;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        if (asset.IsExpanded) {
            foreach (var child in asset.Children.Values) {
                DrawAssetItem(child, indent + 1, ref selectedAsset);
            }
        }
    }

    private static void MarkAllChildrenAsDelete(AssetListing asset, bool markForDelete)
    {
        asset.IsMarkedForDelete = markForDelete;
        if (asset.Children.Count > 0) {
            foreach (var child in asset.Children.Values) {
                MarkAllChildrenAsDelete(child, markForDelete);
            }
        }
    }

    private static void MarkAllChildrenAsDeleteInvert(AssetListing asset)
    {
        asset.IsMarkedForDelete = !asset.IsMarkedForDelete;
        if (asset.Children.Count > 0) {
            foreach (var child in asset.Children.Values) {
                MarkAllChildrenAsDeleteInvert(child);
            }
        }
    }

    private static string[] PathsToThoseMarkedForDelete(Dictionary<string, AssetListing> entries)
    {
        var result = new List<string>();
        foreach (var entry in entries) {
            if (entry.Value.IsMarkedForDelete && entry.Value.Icon != EditorGUIUtility.FindTexture("Folder Icon")) {
                result.Add(entry.Value.Path);
            }
            result.AddRange(PathsToThoseMarkedForDelete(entry.Value.Children));
        }
        return result.ToArray();
    }

    private static bool IsMatchingFilter(string path)
    {
        if (string.IsNullOrEmpty(sFilterWords)) return true;

        var split = sFilterWords.ToLower().Split(" ");
        for (var i = 0; i < split.Length; i++) {
            var word = split[i]; // Note: Can't write to foreach iterator (since iterators are readonly)
            if (!word.StartsWith("-") && !path.ToLower().Contains(word)) return false; // Matches
            if (word.StartsWith("-") && path.ToLower().Contains(word.TrimStart('-'))) return false; // Excludes
        }

        return true;
    }

    private static Dictionary<string, AssetListing> PopulateDeleteFileListing(IEnumerable<string> assetDeleteFileGUIDList)
    {
        var result = new Dictionary<string, AssetListing>();
        foreach (var guid in assetDeleteFileGUIDList) {
            var parts = AssetDatabase.GUIDToAssetPath(guid).Split('/');
            var currentDict = result; // Save the dict down the traversal

            for (var i = 0; i < parts.Length; i++) {
                if (string.IsNullOrWhiteSpace(parts[i])) continue;

                var part = parts[i];
                var take = new List<string>();
                for (var j = 0; j < Math.Min(i + 1, parts.Length); j++) take.Add(parts[j]);
                var currentPath = string.Join("/", take);

                if (!currentDict.TryGetValue(part, out var currentItem)) {
                    if (!(AssetDatabase.GetCachedIcon(currentPath) is Texture2D icon)) {
                        icon = EditorGUIUtility.FindTexture("Folder Icon"); // It's a folder
                        if (i == parts.Length - 1) icon = EditorGUIUtility.FindTexture("DefaultAsset Icon"); // It's a file
                    }
                    currentItem = new AssetListing
                    {
                        Path = currentPath,
                        Icon = icon,
                        IsExpanded = true,
                    };
                    currentDict[part] = currentItem;
                }

                currentDict = currentItem.Children;
            }
        }

        return result;
    }
}