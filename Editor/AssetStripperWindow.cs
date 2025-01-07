using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public class AssetStripperWindow : EditorWindow
{
    [MenuItem("Moonlit/Asset Stripper/Prepare Stripping", priority = 0)]
    private static void CreateWindow()
    {
        var window = GetWindow<AssetStripperWindow>();
        var mainWindowRect = EditorGUIUtility.GetMainWindowPosition();
        window.titleContent = new GUIContent("Asset Stripper");
        window.position = new Rect(
            mainWindowRect.center.x - 400, // 800/2
            mainWindowRect.center.y - 300, // 600/2
            800,
            600
        );
    }
    protected void OnDestroy()
    {
        SessionState.EraseString(nameof(sDeleteAssetEntries));
        sDeleteAssetEntries.Clear();
    }

    private class AssetListing
    {
        public string Path;
        public Texture2D Icon;
        public bool IsMarkedForDelete, IsExpanded;
        public Dictionary<string, AssetListing> Children = new Dictionary<string, AssetListing>();
    }
    [Serializable]
    private class SerializableAssetListing
    {
        public string Path;
        public bool IsMarkedForDelete, IsExpanded;
        public List<SerializableAssetListing> Children = new List<SerializableAssetListing>();
    }
    [Serializable]
    private class SerializableAssetListingDictionary
    {
        public List<SerializableAssetListing> Entries = new List<SerializableAssetListing>();
    }
    private static SerializableAssetListing ConvertToSerializable(AssetListing listing)
    {
        var serializable = new SerializableAssetListing { Path = listing.Path, IsMarkedForDelete = listing.IsMarkedForDelete, IsExpanded = listing.IsExpanded };
        foreach (var child in listing.Children) serializable.Children.Add(ConvertToSerializable(child.Value));
        return serializable;
    }
    private static AssetListing ConvertFromSerializable(SerializableAssetListing serializable)
    {
        var listing = new AssetListing { Path = serializable.Path, Icon = LoadAppropriateIcon(Path.HasExtension(serializable.Path), serializable.Path), IsMarkedForDelete = serializable.IsMarkedForDelete, IsExpanded = serializable.IsExpanded };
        foreach (var child in serializable.Children) {
            var childListing = ConvertFromSerializable(child);
            listing.Children[childListing.Path] = childListing;
        }
        return listing;
    }

    private static void SaveToSessionState(Dictionary<string, AssetListing> sDeleteAssetEntries)
    {
        var serializable = new SerializableAssetListingDictionary();
        foreach (var entry in sDeleteAssetEntries) serializable.Entries.Add(ConvertToSerializable(entry.Value));
        SessionState.SetString(nameof(sDeleteAssetEntries), JsonUtility.ToJson(serializable));
    }
    private static Dictionary<string, AssetListing> LoadFromSessionState()
    {
        var json = SessionState.GetString(nameof(sDeleteAssetEntries), string.Empty);
        var assetListing = new Dictionary<string, AssetListing>();
        if (!string.IsNullOrEmpty(json)) {
            foreach (var entry in JsonUtility.FromJson<SerializableAssetListingDictionary>(json).Entries) {
                var listing = ConvertFromSerializable(entry);
                assetListing[listing.Path] = listing;
            }
        }
        return assetListing;
    }
    private static Dictionary<string, AssetListing> sDeleteAssetEntries = new Dictionary<string, AssetListing>();

    private enum WindowEvents { StripProject = 1, SelectAll, DeselectAll, InvertSelection, ScanProject }
    private class BackgroundColorScope : GUI.Scope
    {
        private readonly Color m_Color;
        public BackgroundColorScope(Color tempColor) { m_Color = GUI.backgroundColor; GUI.backgroundColor = tempColor; }
        protected override void CloseScope() => GUI.backgroundColor = m_Color;
    }

    private static readonly Color BetterBlue = new Color(75 / 255f, 175f / 255f, 1f);
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
            EditorGUILayout.HelpBox("Unable to Scan Project! .meta files are currently set to \"Hidden\" in project! Update \"Version Control\" under [Edit->Project Settings->Editor]", MessageType.Error);
        }
        EditorGUILayout.LabelField("To Scan", new GUIStyle(EditorStyles.largeLabel) { fontSize = 20 }, GUILayout.Height(25f));

        var thickBoxStyle = new GUIStyle("box") { border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(10, 10, 10, 10) };
        using (new EditorGUILayout.VerticalScope(thickBoxStyle)) {
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUILayout.VerticalScope()) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        GUILayout.Space(10);
                        sIsIncludingScripts = EditorGUILayout.Toggle(sIsIncludingScripts, GUILayout.Width(20));
                        EditorGUILayout.LabelField("Include Runtime Scripts", GUILayout.Width(170));
                        GUILayout.FlexibleSpace();
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    using (new EditorGUI.DisabledGroupScope(!sIsIncludingScripts)) {
                        GUILayout.Space(10);
                        sIsIncludingEditorScripts = EditorGUILayout.Toggle(sIsIncludingEditorScripts, GUILayout.Width(20));
                        GUILayout.Space(30);
                        EditorGUILayout.LabelField("Include Editor Scripts", GUILayout.Width(140));
                        GUILayout.FlexibleSpace();
                    }
                }
                using (new BackgroundColorScope(Color.cyan))
                using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(500))) {
                    if (!sIsIncludingScripts) EditorGUILayout.LabelField("Scan for all non scripts that are not referenced by the Editor.", EditorStyles.largeLabel);
                    if (!sIsIncludingScripts) EditorGUILayout.LabelField($"Reference: Scenes (Build Settings) & [Assets/Resources;{Runtime.EditorPrefsReferenceFolder}]");
                    if (!sIsIncludingScripts) EditorGUILayout.LabelField("Ignore: Runtime & Editor Scripts");
                    if (sIsIncludingScripts && !sIsIncludingEditorScripts) EditorGUILayout.LabelField("Scan for all assets that are not referenced in a build of the game.", EditorStyles.largeLabel);
                    if (sIsIncludingScripts && !sIsIncludingEditorScripts) EditorGUILayout.LabelField($"Reference: Scenes (Build Settings) & [Assets/Resources;{Runtime.EditorPrefsReferenceFolder}]");
                    if (sIsIncludingScripts && !sIsIncludingEditorScripts) EditorGUILayout.LabelField("Ignore: Editor Scripts");
                    if (sIsIncludingScripts && sIsIncludingEditorScripts) EditorGUILayout.LabelField("Scan for all assets that are not referenced at all by the Runtime or Editor.", EditorStyles.largeLabel);
                    if (sIsIncludingScripts && sIsIncludingEditorScripts) EditorGUILayout.LabelField($"Reference: Scenes (Build Settings) & [Assets/Resources;{Runtime.EditorPrefsReferenceFolder}]");
                    if (sIsIncludingScripts && sIsIncludingEditorScripts) EditorGUILayout.LabelField("");
                }
            }
            EditorGUILayout.LabelField("Separate Reference Folders by ';'. ex. [Assets/Plugins;Assets/Gizmos]");
            Runtime.EditorPrefsReferenceFolder = EditorGUILayout.TextField(Runtime.EditorPrefsReferenceFolder);
        }
        using (new EditorGUILayout.HorizontalScope()) {
            GUILayout.FlexibleSpace();
            Runtime.EditorPrefsBackupAssetsByCreatingPackage = EditorGUILayout.Toggle(Runtime.EditorPrefsBackupAssetsByCreatingPackage, GUILayout.Width(20));
            EditorGUILayout.LabelField("Generate Backup Package", GUILayout.Width(170));
        }
        EditorGUILayout.Space();
        using (new EditorGUILayout.VerticalScope("box")) {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(sScroll)) {
                sScroll = scrollScope.scrollPosition;
                if (!isWaitingForScan) {
                    foreach (var asset in sDeleteAssetEntries.Values) {
                        DrawAssetItem(asset, 0, ref selectedAsset);
                    }
                } else {
                    if (!string.IsNullOrWhiteSpace(sFilterWords)) {
                        EditorGUILayout.LabelField("There are currently no results to filter. No scan was performed yet!");
                    }
                }
            }
            if (!isWaitingForScan) {
                var richTextStyle = new GUIStyle(EditorStyles.label) { richText = true };
                EditorGUILayout.LabelField("Select unneeded assets for removal. <b>Click</b> directly on assets to ping/view them in the <b>Inspector</b>.", richTextStyle);
                using (new EditorGUILayout.HorizontalScope()) {
                    events = GUILayout.Button("Select All") ? WindowEvents.SelectAll : events;
                    events = GUILayout.Button("Deselect All") ? WindowEvents.DeselectAll : events;
                    events = GUILayout.Button("Invert Selection") ? WindowEvents.InvertSelection : events;
                }
                GUILayout.Space(10);
            }
        }

        sFilterWords = EditorGUILayout.TextField("Filter:", sFilterWords, EditorStyles.toolbarSearchField);
        EditorGUILayout.LabelField("Use keywords to filter on files (not folders). Use '-' before a keyword to exclude it. (ex. '.png' filters on png files)");
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
                using (new BackgroundColorScope(BetterBlue)) {
                    var ok = GUILayout.Button("Strip Assets", customButtonStyle, GUILayout.Width(140));
                    if (ok) {
                        events = EditorUtility.DisplayDialog("Are You Sure?", "This will remove the chosen assets from the project.", "OK", "Cancel") ? WindowEvents.StripProject : events;
                    }
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
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                var assetDeleteFileGUIDList = Runtime.GatherAssetsToStrip(sIsIncludingScripts, sIsIncludingEditorScripts);
                sDeleteAssetEntries = PopulateDeleteFileListing(assetDeleteFileGUIDList);
                SaveToSessionState(sDeleteAssetEntries);
            } else {
                Debug.Log("The user has chosen to cancel scan since there were unmodified changes.");
            }
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
                    Debug.Log($"{filePathsToThoseMarkedForDelete.Length} asset(s) successfully removed!");
                }
                finally {
                    EditorUtility.ClearProgressBar();
                }
                sDeleteAssetEntries.Clear();
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
                GUILayout.Space(60); // Align with foldout arrow
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

        foreach (var word in sFilterWords.ToLower().Split(" ")) {
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
                var isFile = i == parts.Length - 1;
                var take = new List<string>();
                for (var j = 0; j < Math.Min(i + 1, parts.Length); j++) take.Add(parts[j]);
                var currentPath = string.Join("/", take);

                if (!currentDict.TryGetValue(part, out var currentItem)) {
                    var icon = LoadAppropriateIcon(isFile, currentPath);
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

    private static Texture2D LoadAppropriateIcon(bool isFile, string currentPath)
    {
        var icon = AssetDatabase.GetCachedIcon(currentPath) as Texture2D; // Use OS associated icon
        if (icon == null && isFile) icon = EditorGUIUtility.FindTexture("DefaultAsset Icon"); // It's a file
        if (icon == null) icon = EditorGUIUtility.FindTexture("Folder Icon"); // It's a folder
        return icon;
    }

    [UnityEditor.Callbacks.DidReloadScripts]
    protected static void OnScriptsReloaded()
    {
        sDeleteAssetEntries = LoadFromSessionState();
    }
}