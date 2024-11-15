using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class Runtime
{
    private class TypeToRegex
    {
        public Type Type; public string TypeName; public Regex Match;
    }
    [Serializable]
    public class AssemblyTypeInformation
    {
        public string assembly; public HashSet<string> uniqueFullTypeNames = new HashSet<string>();
    }
    [Serializable]
    public class FileReferenceInformation { public HashSet<string> referenceGUIDs = new HashSet<string>(); }

    public static bool EditorPrefsBackupAssetsByCreatingPackage { get => EditorPrefs.GetInt(nameof(EditorPrefsBackupAssetsByCreatingPackage), 1) == 1; set => EditorPrefs.SetInt(nameof(EditorPrefsBackupAssetsByCreatingPackage), value ? 1 : 0); } // Default to being on to make our users feel some trust
    public static string EditorPrefsReferenceFolder { get => EditorPrefs.GetString(nameof(EditorPrefsReferenceFolder), "Assets/Plugins;"); set => EditorPrefs.SetString(nameof(EditorPrefsReferenceFolder), value); }
    public static Task sPerformScan;
    public static CancellationTokenSource cts;
    private static readonly Dictionary<string, FileReferenceInformation> sFileReferenceInformation = new Dictionary<string, FileReferenceInformation>();
    private static string[] sAllFilesInAssetsDirectoryCache;

    public static string[] GatherAssetsToStrip(bool shouldFindCodeFiles, bool shouldFindEditorCodeFiles)
    {
        try {
            var shouldCancel = EditorUtility.DisplayCancelableProgressBar("Populating Information", "Prepping project", 0f);
            if (shouldCancel) { cts.Cancel(); return new string[] { }; }

            sFileReferenceInformation.Clear();
            sAllFilesInAssetsDirectoryCache = Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories);
            shouldCancel = EditorUtility.DisplayCancelableProgressBar("Populating Information", "Populating asset references", 0.3f);
            if (shouldCancel) { cts.Cancel(); return new string[] { }; }

            PopulateAssetReferences(sFileReferenceInformation);
            shouldCancel = EditorUtility.DisplayCancelableProgressBar("Populating Information", "Populating shader references", 0.6f);
            if (shouldCancel) { cts.Cancel(); return new string[] { }; }

            PopulateShaderReferences(sFileReferenceInformation);
            if (shouldFindCodeFiles) {
                shouldCancel = EditorUtility.DisplayCancelableProgressBar("Populating Information", "Building file map", 0.7f);
                if (shouldCancel) { cts.Cancel(); return new string[] { }; }

                BuildCodeFileMap(shouldFindEditorCodeFiles, out var codeFilesDeduplicated, out var firstPassFiles, out var firstPassTypes, out var allTypes, out var codeToFileMap);
                shouldCancel = EditorUtility.DisplayCancelableProgressBar("Populating Information", "Populating code references... might take a while", 0.8f);
                if (shouldCancel) { cts.Cancel(); return new string[] { }; }

                foreach (var codePath in firstPassFiles) {
                    PopulateReferenceClasses(sFileReferenceInformation, codeToFileMap, AssetDatabase.AssetPathToGUID(codePath), codePath, firstPassTypes);
                }
                shouldCancel = EditorUtility.DisplayCancelableProgressBar("Populating Information", "Populating code references... might take a while (pass 2)", 0.85f);
                if (shouldCancel) { cts.Cancel(); return new string[] { }; }

                foreach (var codePath in codeFilesDeduplicated) {
                    PopulateReferenceClasses(sFileReferenceInformation, codeToFileMap, AssetDatabase.AssetPathToGUID(codePath), codePath, allTypes);
                }
                if (shouldFindEditorCodeFiles) {
                    shouldCancel = EditorUtility.DisplayCancelableProgressBar("Populating Information", "Populating code references... might take a while (pass 3)", 0.9f);
                    if (shouldCancel) { cts.Cancel(); return new string[] { }; }

                    PopulateCustomEditorClasses(sFileReferenceInformation, codeToFileMap, allTypes);
                }
            }

            shouldCancel = EditorUtility.DisplayCancelableProgressBar("Populating Information", "Populated all references", 1f);
            if (shouldCancel) { cts.Cancel(); return new string[] { }; }

            var assetDeleteFileGUIDList = new HashSet<string>();
            var referenceFolders = EditorPrefsReferenceFolder.Split(";");
            foreach (var file in sAllFilesInAssetsDirectoryCache) {
                if (!shouldFindCodeFiles && Path.GetExtension(file) == ".cs") continue;
                if (Path.GetExtension(file) == ".dll") continue;
                if (Path.GetExtension(file) == ".meta") continue;
                if (Regex.IsMatch(file, "[\\/\\\\]Resources[\\/\\\\]")) continue;
                var isReferenceFolderContinue = false;
                foreach (var rf in referenceFolders) {
                    if (string.IsNullOrWhiteSpace(rf)) continue;
                    if (file.Contains(rf)) { isReferenceFolderContinue = true; break; }
                }
                if (isReferenceFolderContinue) continue;

                assetDeleteFileGUIDList.Add(AssetDatabase.AssetPathToGUID(file));
            }

            shouldCancel = EditorUtility.DisplayCancelableProgressBar("Referencing", "Starting on relevant files", 0f);
            if (shouldCancel) { cts.Cancel(); return new string[] { }; }

            RemoveAssetsInResourceFolderFromDeleteList(assetDeleteFileGUIDList, sFileReferenceInformation);
            shouldCancel = EditorUtility.DisplayCancelableProgressBar("Referencing", "Pruned references against Resources", 0.25f);
            if (shouldCancel) { cts.Cancel(); return new string[] { }; }

            RemoveScenesInBuildSettingsFromDeleteList(assetDeleteFileGUIDList, sFileReferenceInformation);
            shouldCancel = EditorUtility.DisplayCancelableProgressBar("Referencing", "Pruned references against Scenes", 0.5f);
            if (shouldCancel) { cts.Cancel(); return new string[] { }; }

            RemoveStaticAndPartialsClassesFromDeleteList(assetDeleteFileGUIDList, sFileReferenceInformation);
            shouldCancel = EditorUtility.DisplayCancelableProgressBar("Referencing", "Pruned references against Static and Partial classes", 0.75f);
            if (shouldCancel) { cts.Cancel(); return new string[] { }; }

            if (shouldFindEditorCodeFiles) {
                RemoveEditorScriptsFromDeleteList(assetDeleteFileGUIDList, sFileReferenceInformation);
                shouldCancel = EditorUtility.DisplayCancelableProgressBar("Referencing", "Pruned references against Editor", 0.825f);
                if (shouldCancel) { cts.Cancel(); return new string[] { }; }
            }

            shouldCancel = EditorUtility.DisplayCancelableProgressBar("Referencing", "Complete", 1f);
            if (shouldCancel) { cts.Cancel(); return new string[] { }; }

            return new List<string>(assetDeleteFileGUIDList).ToArray();
        }
        finally {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void PopulateAssetReferences(Dictionary<string, FileReferenceInformation> fileReferenceInformation)
    {
        foreach (var file in sAllFilesInAssetsDirectoryCache) {
            if (!File.Exists(file)) continue;
            if (Path.GetExtension(file) == ".cg") continue;
            if (Path.GetExtension(file) == ".cginc") continue;
            if (Path.GetExtension(file) == ".cs") continue;
            if (Path.GetExtension(file) == ".meta") continue;
            if (Path.GetExtension(file) == ".shader") continue;

            var byReference = GetOrAdd(fileReferenceInformation, AssetDatabase.AssetPathToGUID(file), new FileReferenceInformation { referenceGUIDs = new HashSet<string>() });
            foreach (var dependentFile in AssetDatabase.GetDependencies(new string[] { file })) {
                byReference.referenceGUIDs.Add(AssetDatabase.AssetPathToGUID(dependentFile));
            }
        }
    }

    private static void PopulateShaderReferences(Dictionary<string, FileReferenceInformation> fileReferenceInformation)
    {
        var shaderFileNameToGuid = new Dictionary<string, string>();
        var shaderFiles = Directory.GetFiles("Assets", "*.shader", SearchOption.AllDirectories);
        foreach (var filepath in shaderFiles) {
            var match = Regex.Match(File.ReadAllText(filepath), "Shader \"(?<name>.*)\"");
            if (match.Success) {
                _ = shaderFileNameToGuid.TryAdd(match.Groups["name"].ToString(), AssetDatabase.AssetPathToGUID(filepath));
            }
        }

        var cgincFiles = Directory.GetFiles("Assets", "*.cginc", SearchOption.AllDirectories);
        foreach (var filepath in cgincFiles) {
            _ = shaderFileNameToGuid.TryAdd(Path.GetFileName(filepath), AssetDatabase.AssetPathToGUID(filepath));
        }

        var cgFiles = Directory.GetFiles("Assets", "*.cg", SearchOption.AllDirectories);
        foreach (var filepath in cgFiles) {
            _ = shaderFileNameToGuid.TryAdd(Path.GetFileName(filepath), AssetDatabase.AssetPathToGUID(filepath));
        }

        foreach (var kv in shaderFileNameToGuid) {
            var guid = kv.Value;
            var shaderFilePath = AssetDatabase.GUIDToAssetPath(guid);
            if (!File.Exists(shaderFilePath)) continue;

            var byReference = GetOrAdd(fileReferenceInformation, guid, new FileReferenceInformation { referenceGUIDs = new HashSet<string>() });
            var codeBlob = StripOutComments(File.ReadAllText(shaderFilePath));
            foreach (var checkingShaderName in shaderFileNameToGuid.Keys) {
                if (checkingShaderName == kv.Key) continue;
                if (codeBlob.IndexOf(checkingShaderName) == -1) continue;
                if (!shaderFileNameToGuid.ContainsKey(checkingShaderName)) continue;
                byReference.referenceGUIDs.Add(shaderFileNameToGuid[checkingShaderName]);
            }
        }
    }

    private static void BuildCodeFileMap(bool shouldFindEditorCodeFiles, out HashSet<string> codeFilesDeduplicated, out List<string> firstPassFiles, out List<TypeToRegex> firstPassTypes, out List<TypeToRegex> allTypes, out Dictionary<Type, HashSet<string>> codeToFileGuidMap)
    {
        var fileTypes = new Dictionary<string, AssemblyTypeInformation>();
        var codeFilesUnderAssets = FindCodeFilesUnderDirectory(fileTypes, "Assets/");
        var referenceFolders = EditorPrefsReferenceFolder.Split(";");
        codeFilesDeduplicated = new HashSet<string>();
        firstPassFiles = new List<string>();

        foreach (var rf in referenceFolders) {
            if (string.IsNullOrWhiteSpace(rf)) continue;
            if (Directory.Exists(rf)) firstPassFiles.AddRange(FindCodeFilesUnderDirectory(fileTypes, rf));
        }
        foreach (var file in codeFilesUnderAssets) {
            if (!firstPassFiles.Contains(file)) {
                codeFilesDeduplicated.Add(file);
            }
        }

        var projectPath = Path.GetDirectoryName(Application.dataPath);
        var assemblyPath = Path.Combine(projectPath, "Library/ScriptAssemblies/Assembly-CSharp.dll");
        var editorAssemblyPath = Path.Combine(projectPath, "Library/ScriptAssemblies/Assembly-CSharp-Editor.dll");
        var firstPassAssemblyPath = Path.Combine(projectPath, "Library/ScriptAssemblies/Assembly-CSharp-firstpass.dll");
        var firstPassEditorAssemblyPath = Path.Combine(projectPath, "Library/ScriptAssemblies/Assembly-CSharp-Editor-firstpass.dll");
        firstPassTypes = new List<TypeToRegex>();
        allTypes = new List<TypeToRegex>();
        codeToFileGuidMap = new Dictionary<Type, HashSet<string>>();

        if (File.Exists(firstPassAssemblyPath)) foreach (var type in Assembly.LoadFile(firstPassAssemblyPath).GetTypes()) firstPassTypes.Add(new TypeToRegex() { Type = type });
        if (shouldFindEditorCodeFiles && File.Exists(firstPassEditorAssemblyPath)) foreach (var type in Assembly.LoadFile(firstPassEditorAssemblyPath).GetTypes()) firstPassTypes.Add(new TypeToRegex() { Type = type });
        PopulateCodeFileDictionary(fileTypes, firstPassTypes, firstPassFiles.ToArray());
        if (File.Exists(assemblyPath)) foreach (var type in Assembly.LoadFile(assemblyPath).GetTypes()) allTypes.Add(new TypeToRegex() { Type = type });
        if (shouldFindEditorCodeFiles && File.Exists(editorAssemblyPath)) foreach (var type in Assembly.LoadFile(editorAssemblyPath).GetTypes()) allTypes.Add(new TypeToRegex() { Type = type });
        PopulateCodeFileDictionary(fileTypes, allTypes, codeFilesDeduplicated);

        allTypes.AddRange(firstPassTypes);

        foreach (var type in allTypes) {
            var byReferenceCodeFileGUIDs = GetOrAdd(codeToFileGuidMap, type.Type, new HashSet<string>());
            var assembly = type.Type.Assembly.FullName;
            foreach (var kv in fileTypes) {
                var t = kv.Value;
                var guid = kv.Key;
                if (t.assembly == assembly && t.uniqueFullTypeNames.Contains(type.Type.FullName)) {
                    byReferenceCodeFileGUIDs.Add(guid);
                }
            }
        }

        // Match against things like GetComponent<type.TypeName>(); or new type.TypeName();
        foreach (var type in allTypes) {
            if (type.Type.IsGenericTypeDefinition) {
                type.TypeName = type.Type.GetGenericTypeDefinition().Name.Split('`')[0];
                type.Match = new Regex($"[!|&\\]\\[\\.\\s<(]{type.TypeName}[\\.\\s\\n\\r>,<(){{]");
            } else {
                type.TypeName = type.Type.Name.Split('`')[0].Replace("Attribute", string.Empty);
                type.Match = new Regex(@$"[!|&\[\.\s<(]{type.TypeName}[\.\s\n\r>,<(){{[\]]");
            }
        }
        foreach (var type in firstPassTypes) {
            if (type.Type.IsGenericTypeDefinition) {
                type.TypeName = type.Type.GetGenericTypeDefinition().Name.Split('`')[0];
                type.Match = new Regex($"[!|&\\]\\[\\.\\s<(]{type.TypeName}[\\.\\s\\n\\r>,<(){{]");
            } else {
                type.TypeName = type.Type.Name.Split('`')[0].Replace("Attribute", string.Empty);
                type.Match = new Regex(@$"[!|&\[\.\s<(]{type.TypeName}[\.\s\n\r>,<(){{[\]]");
            }
        }
    }

    private static List<string> FindCodeFilesUnderDirectory(Dictionary<string, AssemblyTypeInformation> fileTypes, string path)
    {
        var filesUnderDirectory = new List<string>();
        foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)) {
            if (!fileTypes.ContainsKey(AssetDatabase.AssetPathToGUID(file))) {
                filesUnderDirectory.Add(file);
            }
        }
        return filesUnderDirectory;
    }

    private static void PopulateCodeFileDictionary(Dictionary<string, AssemblyTypeInformation> fileTypes, IEnumerable<TypeToRegex> alltypes, IEnumerable<string> codeFilePaths)
    {
        foreach (var codePath in codeFilePaths) {
            var codeBlob = StripOutComments(File.ReadAllText(codePath));
            var byReferenceDatedType = GetOrAdd(fileTypes, AssetDatabase.AssetPathToGUID(codePath), new AssemblyTypeInformation { });
            byReferenceDatedType.uniqueFullTypeNames.Clear();

            foreach (var typeToRegex in alltypes) {
                var type = typeToRegex.Type;
                if (type.IsNested) continue;
                if (!string.IsNullOrWhiteSpace(type.Namespace)) {
                    if (!Regex.IsMatch(codeBlob, $"namespace\\s*{type.Namespace}[{{\\s\\n]")) continue;
                }

                var typeName = type.Name;
                if (type.IsGenericTypeDefinition) typeName = type.GetGenericTypeDefinition().Name.Split('`')[0];

                if (type.IsEnum) {
                    if (Regex.IsMatch(codeBlob, $"enum\\s*{type.Name}[\\s{{]")) {
                        AddToDatedTypeInformation(byReferenceDatedType, type);
                    }
                } else if (type.IsInterface) {
                    if (Regex.IsMatch(codeBlob, $"interface\\s*{typeName}[\\s<{{]")) {
                        AddToDatedTypeInformation(byReferenceDatedType, type);
                    }
                } else if (type.IsClass) {
                    if (Regex.IsMatch(codeBlob, $"class\\s*{typeName}?[\\s:<{{]")) {
                        AddToDatedTypeInformation(byReferenceDatedType, type);

                        foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.Instance)) {
                            AddToDatedTypeInformation(byReferenceDatedType, nestedType);
                        }
                    }
                } else {
                    if (Regex.IsMatch(codeBlob, $"struct\\s*{typeName}[\\s:<{{]")) {
                        AddToDatedTypeInformation(byReferenceDatedType, type);
                    } else if (Regex.IsMatch(codeBlob, $"delegate\\s*{typeName}\\s\\(")) {
                        AddToDatedTypeInformation(byReferenceDatedType, type);
                    }
                }
            }
        }
    }

    private static void PopulateReferenceClasses(Dictionary<string, FileReferenceInformation> fileReferenceInformation, IReadOnlyDictionary<Type, HashSet<string>> codeToFileGuidMap, string guid, string codePath, IEnumerable<TypeToRegex> types)
    {
        if (string.IsNullOrWhiteSpace(codePath)) return;
        if (!File.Exists(codePath)) return;

        var byReference = GetOrAdd(fileReferenceInformation, guid, new FileReferenceInformation { referenceGUIDs = new HashSet<string>() });
        var hasSetBlob = false;
        string[] codeLines = null;

        foreach (var typeToRegex in types) {
            var type = typeToRegex.Type;
            if (!codeToFileGuidMap.ContainsKey(type)) continue;
            var typeGuids = codeToFileGuidMap[type];
            if (typeGuids.Contains(guid)) continue;

            var hasNoNamespace = string.IsNullOrWhiteSpace(type.Namespace);// Is type.Namespace expensive? using the cached version didn't have huge gains but keep it in mind
            if (!hasNoNamespace) {
                if (!hasSetBlob) { // Optimization where we only read the file once upon needing it
                    var codeBlob = StripOutComments(File.ReadAllText(codePath));
                    codeLines = codeBlob.Split(Environment.NewLine);
                    hasSetBlob = true;
                }
            }

            if (!hasNoNamespace) {
                var usingNamespace = false;
                foreach (var line in codeLines) {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.Contains("#")) continue; // ignore preprocessor lines
                    if ((line.Contains("namespace") || line.Contains("using")) && line.Contains(type.Namespace)) {
                        usingNamespace = true; break;
                    }
                }
                if (!usingNamespace) continue;
            }

            foreach (var typeGuid in typeGuids) {
                if (!type.IsGenericTypeDefinition) {
                    if (!hasSetBlob) { // Optimization where we only read the file once upon needing it
                        var codeBlob = StripOutComments(File.ReadAllText(codePath));
                        codeLines = codeBlob.Split(Environment.NewLine);
                        hasSetBlob = true;
                    }
                    foreach (var line in codeLines) {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.Contains(";")) continue; // ignore semi-colons which are not present on the extension method declarations
                        if (line.Contains("#")) continue; // ignore preprocessor lines
                        // We need to check for "extension methods" like     void Methods(this Gameobject bob){}
                        if (line.Contains("this ") && line.Contains(typeToRegex.TypeName)) {
                            foreach (var baseReference in fileReferenceInformation) { // Double for loop
                                if (baseReference.Key == typeGuid) {
                                    baseReference.Value.referenceGUIDs.Add(guid);
                                    break;
                                }
                            }
                        }
                    }

                }
                if (!byReference.referenceGUIDs.Contains(typeGuid)) {
                    if (!hasSetBlob) { // Optimization where we only read the file once upon needing it
                        var codeBlob = StripOutComments(File.ReadAllText(codePath));
                        codeLines = codeBlob.Split(Environment.NewLine);
                        hasSetBlob = true;
                    }
                    foreach (var line in codeLines) {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.Contains("#")) continue; // ignore preprocessor lines
                        // We need to check for uses of classes "new Class();" or subclassing from abstract classes/interfaces
                        if (typeToRegex.Match.IsMatch(line) || ((line.Contains("abstract") || line.Contains("interface")) && line.Contains(typeToRegex.TypeName))) { // @regex slow
                            byReference.referenceGUIDs.Add(typeGuid);
                            break;
                        }
                    }
                }
            }
        }
    }

    private static void PopulateCustomEditorClasses(Dictionary<string, FileReferenceInformation> fileReferenceInformation, IReadOnlyDictionary<Type, HashSet<string>> codeToFileMap, IEnumerable<TypeToRegex> types)
    {
        foreach (var typeToRegex in types) {
            var type = typeToRegex.Type;
            if (!codeToFileMap.ContainsKey(type)) continue;

            foreach (var customEditorAttribute in type.GetCustomAttributes(typeof(CustomEditor), true)) {
                if (!(customEditorAttribute is CustomEditor customEditor)) continue;

                var referenceTypeField = typeof(CustomEditor).GetField("m_InspectedType", BindingFlags.Instance | BindingFlags.NonPublic);
                var referenceType = (Type)referenceTypeField.GetValue(customEditor);

                if (!codeToFileMap.ContainsKey(referenceType)) continue;

                foreach (var guidFrom in codeToFileMap[referenceType]) {
                    if (!fileReferenceInformation.ContainsKey(guidFrom)) continue;

                    var reference = fileReferenceInformation[guidFrom];
                    foreach (var guidTo in codeToFileMap[type]) {
                        reference.referenceGUIDs.Add(guidTo);
                    }
                }
            }
        }
    }

    private static void RemoveAssetsInResourceFolderFromDeleteList(HashSet<string> assetDeleteFileGUIDList, IReadOnlyDictionary<string, FileReferenceInformation> fileReferenceInformation)
    {
        var resourcesFiles = new List<string>();
        foreach (var path in sAllFilesInAssetsDirectoryCache) {
            if (Path.GetExtension(path) == ".meta") continue;
            if (!Regex.IsMatch(path, "[\\/\\\\]Resources[\\/\\\\]")) continue;
            resourcesFiles.Add(path);
        }
        foreach (var path in AssetDatabase.GetDependencies(resourcesFiles.ToArray())) {
            RemoveFromDeleteList(assetDeleteFileGUIDList, fileReferenceInformation, AssetDatabase.AssetPathToGUID(path));
        }
    }

    private static void RemoveScenesInBuildSettingsFromDeleteList(HashSet<string> assetDeleteFileGUIDList, IReadOnlyDictionary<string, FileReferenceInformation> fileReferenceInformation)
    {
        var scenes = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes) {
            if (!scene.enabled) continue;
            scenes.Add(scene.path);
        }
        foreach (var path in AssetDatabase.GetDependencies(scenes.ToArray())) {
            RemoveFromDeleteList(assetDeleteFileGUIDList, fileReferenceInformation, AssetDatabase.AssetPathToGUID(path));
        }
    }

    private static void RemoveEditorScriptsFromDeleteList(HashSet<string> assetDeleteFileGUIDList, IReadOnlyDictionary<string, FileReferenceInformation> fileReferenceInformation)
    {
        foreach (var path in sAllFilesInAssetsDirectoryCache) {
            if (Path.GetExtension(path) != ".cs") continue;
            if (!Regex.IsMatch(path, "[\\/\\\\]Editor[\\/\\\\]")) continue;

            var guid = AssetDatabase.AssetPathToGUID(path);
            var codeBlob = StripOutComments(File.ReadAllText(path));
            if (Regex.IsMatch(codeBlob, "\\[(AssetPostprocessor|MenuItem|PostProcessBuild)")
             || Regex.IsMatch(codeBlob, @":\s*(EditorWindow|IPreprocessBuildWithReport|IPreprocessBuild|PropertyDrawer)$")) {
                RemoveFromDeleteList(assetDeleteFileGUIDList, fileReferenceInformation, guid);
            } else if (fileReferenceInformation.TryGetValue(guid, out var refInfo)) {
                var hasAny = false;
                foreach (var c in refInfo.referenceGUIDs) {
                    if (assetDeleteFileGUIDList.Contains(c)) {
                        hasAny = true;
                        break;
                    }
                }
                if (!hasAny) {
                    RemoveFromDeleteList(assetDeleteFileGUIDList, fileReferenceInformation, guid);
                }
            }
        }
    }

    private static void RemoveStaticAndPartialsClassesFromDeleteList(HashSet<string> assetDeleteFileGUIDList, IReadOnlyDictionary<string, FileReferenceInformation> fileReferenceInformation)
    {
        foreach (var path in sAllFilesInAssetsDirectoryCache) {
            if (Path.GetExtension(path) != ".cs") continue;
            if (!Regex.IsMatch(StripOutComments(File.ReadAllText(path)), "(partial|static)\\s+class")) continue;
            RemoveFromDeleteList(assetDeleteFileGUIDList, fileReferenceInformation, AssetDatabase.AssetPathToGUID(path));
        }
    }

    private static void RemoveFromDeleteList(HashSet<string> assetDeleteFileGUIDList, IReadOnlyDictionary<string, FileReferenceInformation> fileReferenceInformation, string guid)
    {
        if (!assetDeleteFileGUIDList.Contains(guid)) return;

        assetDeleteFileGUIDList.Remove(guid);

        if (fileReferenceInformation.TryGetValue(guid, out var refInfo)) {
            foreach (var referenceGuid in refInfo.referenceGUIDs) {
                RemoveFromDeleteList(assetDeleteFileGUIDList, fileReferenceInformation, referenceGuid);
            }
        }
    }

    private static string StripOutComments(string codeBlob)
        => Regex.Replace(codeBlob, @"(/\*([^*]|[\r\n]|(\*+([^*/]|[\r\n])))*\*+/)|(//.*)|(@""""|""(?:[^""\\]|\\.)*"")|('(?:[^'\\]|\\.)*')", match =>
        {
            if (match.Value.StartsWith("\"")) return match.Value;
            if (match.Value.StartsWith("'")) return match.Value;
            if (match.Value.StartsWith("@\"")) return match.Value;
            return string.Empty;
        });

    private static void AddToDatedTypeInformation(AssemblyTypeInformation datedTypeInformation, Type addType)
    {
        datedTypeInformation.assembly = addType.Assembly.FullName;
        datedTypeInformation.uniqueFullTypeNames.Add(addType.FullName);
    }

    private static TValue GetOrAdd<TKey, TValue>(Dictionary<TKey, TValue> set, TKey key, TValue defaultValue)
    {
        if (set.TryGetValue(key, out var existingValue)) return existingValue;

        set[key] = defaultValue;
        return defaultValue;
    }
}