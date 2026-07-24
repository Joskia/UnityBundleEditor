using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Cpp2IL;
using System.Text;

namespace UnityBundleEditor;

public class BundleManager : IDisposable
{
    private readonly AssetsManager _manager;
    private readonly BundleFileInstance _bundleInst;
    private readonly AssetsFileInstance _assetsInst;
    private readonly AssetsFile _assetsFile;
    private readonly string _bundlePath;
    private readonly string? _managedPath;
    private bool _disposed;

    public event Action<string>? OnLog;
    public event Action<double>? OnProgress; // 0.0 to 1.0

    public AssetsFileInstance AssetsInstance => _assetsInst;
    public AssetsFile AssetsFile => _assetsFile;
    public BundleFileInstance BundleInstance => _bundleInst;
    public AssetsManager Manager => _manager;
    public string BundlePath => _bundlePath;
    public string? ManagedPath => _managedPath;

    public BundleManager(string bundlePath, string? managedPath = null, int fileIndex = 0)
    {
        _bundlePath = bundlePath;
        _managedPath = managedPath;
        _manager = new AssetsManager();

        // Cargar classdata.tpk
        string tpkPath = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
        if (File.Exists(tpkPath))
        {
            _manager.LoadClassPackage(tpkPath);
        }
        else
        {
            string[] searchPaths = {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.GetDirectoryName(bundlePath) ?? ""
            };
            foreach (var sp in searchPaths)
            {
                string candidate = Path.Combine(sp, "classdata.tpk");
                if (File.Exists(candidate))
                {
                    _manager.LoadClassPackage(candidate);
                    Log($"classdata.tpk cargado desde: {candidate}");
                    break;
                }
            }
        }

        Log($"Cargando bundle: {bundlePath}");
        _bundleInst = _manager.LoadBundleFile(bundlePath, true);

        var dirInfos = _bundleInst.file.BlockAndDirInfo.DirectoryInfos;
        Log($"Archivos internos: {dirInfos.Count}  |  Seleccionado: [{fileIndex}] {dirInfos[fileIndex].Name}");

        _assetsInst = _manager.LoadAssetsFileFromBundle(_bundleInst, fileIndex, false);
        _assetsFile = _assetsInst.file;

        bool hasTypeTree = _assetsFile.Metadata.TypeTreeEnabled;
        Log($"TypeTree habilitado: {hasTypeTree}");
        Log($"Version Unity: {_assetsFile.Metadata.UnityVersion}");
        Log($"Total assets: {_assetsFile.AssetInfos.Count}");

        if (!hasTypeTree)
        {
            Log("Bundle sin TypeTree. Cargando class database...");
            _manager.LoadClassDatabaseFromPackage(_assetsFile.Metadata.UnityVersion);
        }

        if (!string.IsNullOrEmpty(managedPath))
        {
            SetupMonoBehaviourParser(managedPath);
        }
        else
        {
            Log("Advertencia: No se proporciono ruta de Managed. Los MonoBehaviours pueden no deserializarse correctamente.");
        }
    }

    private void SetupMonoBehaviourParser(string managedPath)
    {
        Log($"Configurando parser de MonoBehaviours desde: {managedPath}");
        try
        {
            if (Directory.Exists(managedPath))
            {
                string[] dlls = Directory.GetFiles(managedPath, "*.dll");
                string metadataPath = Path.Combine(managedPath, "global-metadata.dat");

                if (dlls.Length > 0 && !File.Exists(metadataPath))
                {
                    Log("Backend detectado: Mono");
                    _manager.MonoTempGenerator = new MonoCecilTempGenerator(managedPath);
                }
                else if (File.Exists(metadataPath))
                {
                    Log("Backend detectado: IL2CPP");
                    var il2cppFiles = FindCpp2IlFiles.Find(managedPath);
                    if (il2cppFiles.success)
                    {
                        _manager.MonoTempGenerator = new Cpp2IlTempGenerator(il2cppFiles.metaPath, il2cppFiles.asmPath);
                    }
                    else
                    {
                        string? metaPath = FindFileRecursive(managedPath, "global-metadata.dat");
                        string? asmPath = FindFileRecursive(managedPath, "GameAssembly.dll")
                            ?? FindFileRecursive(managedPath, "libil2cpp.so")
                            ?? FindFileRecursive(managedPath, "il2cpp.dll");

                        if (metaPath != null && asmPath != null)
                        {
                            _manager.MonoTempGenerator = new Cpp2IlTempGenerator(metaPath, asmPath);
                        }
                        else
                        {
                            Log("No se pudieron encontrar archivos IL2CPP necesarios.");
                        }
                    }
                }
                else
                {
                    Log("No se pudo detectar el backend de scripting.");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error al configurar MonoBehaviour parser: {ex.Message}");
        }
    }

    private string? FindFileRecursive(string rootPath, string fileName)
    {
        try
        {
            foreach (string file in Directory.GetFiles(rootPath, fileName, SearchOption.AllDirectories))
                return file;
        }
        catch { }
        return null;
    }

    // ---- Info methods ----

    public List<BundleFileInfo> ListBundleFiles()
    {
        var result = new List<BundleFileInfo>();
        var dirInfos = _bundleInst.file.BlockAndDirInfo.DirectoryInfos;
        for (int i = 0; i < dirInfos.Count; i++)
        {
            result.Add(new BundleFileInfo
            {
                Index = i,
                Name = dirInfos[i].Name,
                Size = dirInfos[i].DecompressedSize
            });
        }
        return result;
    }

    public List<AssetInfo> ListAssets()
    {
        var result = new List<AssetInfo>();
        foreach (var assetInfo in _assetsFile.AssetInfos)
        {
            string className = ((AssetClassID)assetInfo.TypeId).ToString();
            string assetName = "N/A";

            try
            {
                var baseField = _manager.GetBaseField(_assetsInst, assetInfo);
                if (baseField["m_Name"] != null)
                    assetName = baseField["m_Name"].AsString ?? "N/A";
            }
            catch
            {
                assetName = "[Error]";
            }

            result.Add(new AssetInfo
            {
                PathId = assetInfo.PathId,
                TypeId = assetInfo.TypeId,
                ClassName = className,
                Name = assetName,
                ByteSize = assetInfo.ByteSize
            });
        }
        return result;
    }

    public List<AssetTypeSummary> ListAssetTypes()
    {
        var grouped = _assetsFile.AssetInfos
            .GroupBy(a => a.TypeId)
            .Select(g => new AssetTypeSummary
            {
                TypeId = g.Key,
                ClassName = ((AssetClassID)g.Key).ToString(),
                Count = g.Count(),
                TotalSize = g.Sum(a => (long)a.ByteSize)
            })
            .OrderByDescending(x => x.Count)
            .ToList();
        return grouped;
    }

    public AssetTypeValueField? GetBaseField(AssetInfo assetInfo)
    {
        var nativeInfo = _assetsFile.GetAssetInfo(assetInfo.PathId);
        if (nativeInfo == null) return null;
        return _manager.GetBaseField(_assetsInst, nativeInfo);
    }

    public AssetTypeValueField? GetBaseFieldByPathId(long pathId)
    {
        var nativeInfo = _assetsFile.GetAssetInfo(pathId);
        if (nativeInfo == null) return null;
        return _manager.GetBaseField(_assetsInst, nativeInfo);
    }

    // ---- Extract methods ----

    public byte[] ReadAssetBytes(long pathId)
    {
        var assetInfo = _assetsFile.GetAssetInfo(pathId);
        if (assetInfo == null)
            throw new Exception($"Asset con PathID {pathId} no encontrado.");

        var reader = _assetsFile.Reader;
        reader.Position = assetInfo.GetAbsoluteByteOffset(_assetsFile);
        return reader.ReadBytes((int)assetInfo.ByteSize);
    }

    public void ExtractAsset(long pathId, string outputPath)
    {
        var data = ReadAssetBytes(pathId);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, data);
        Log($"Asset {pathId} extraido: {outputPath}");
    }

    public void ExtractAllAssets(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        int count = 0;
        int total = _assetsFile.AssetInfos.Count;

        foreach (var assetInfo in _assetsFile.AssetInfos)
        {
            string className = ((AssetClassID)assetInfo.TypeId).ToString();
            string fileName = $"{className}_{assetInfo.PathId}.bin";
            string filePath = Path.Combine(outputDir, fileName);

            var reader = _assetsFile.Reader;
            reader.Position = assetInfo.GetAbsoluteByteOffset(_assetsFile);
            byte[] data = reader.ReadBytes((int)assetInfo.ByteSize);
            File.WriteAllBytes(filePath, data);

            count++;
            if (count % 1000 == 0 || count == total)
                ReportProgress((double)count / total);
        }

        Log($"\nExtraccion completada: {count} assets extraidos a {outputDir}");
    }

    public int ExtractAssetsByType(string typeName, string outputDir)
    {
        if (!Enum.TryParse<AssetClassID>(typeName, true, out var assetType))
        {
            Log($"Tipo desconocido: {typeName}");
            return 0;
        }

        int typeId = (int)assetType;
        var assets = _assetsFile.AssetInfos.Where(a => a.TypeId == typeId).ToList();

        if (assets.Count == 0)
        {
            Log($"No se encontraron assets de tipo {typeName}.");
            return 0;
        }

        Directory.CreateDirectory(outputDir);
        Log($"\nExtrayendo {assets.Count} assets de tipo {typeName} a {outputDir}/");

        int count = 0;
        foreach (var info in assets)
        {
            try
            {
                string name = $"{typeName}_{info.PathId}";
                try
                {
                    var baseField = _manager.GetBaseField(_assetsInst, info);
                    if (baseField["m_Name"] != null && !baseField["m_Name"].IsDummy)
                    {
                        string n = baseField["m_Name"].AsString;
                        if (!string.IsNullOrEmpty(n))
                            name = SanitizeFileName(n);
                    }
                }
                catch { }

                string ext = ".bin";
                if (assetType == AssetClassID.TextAsset) ext = ".txt";
                else if (assetType == AssetClassID.Texture2D) ext = ".png";
                else if (assetType == AssetClassID.AudioClip) ext = ".wav";
                else if (assetType == AssetClassID.Shader) ext = ".shader";
                else if (assetType == AssetClassID.Font) ext = ".ttf";
                else if (assetType == AssetClassID.Mesh) ext = ".obj";

                string path = Path.Combine(outputDir, $"{name}_{info.PathId}{ext}");
                var reader = _assetsFile.Reader;
                reader.Position = info.GetAbsoluteByteOffset(_assetsFile);
                byte[] data = reader.ReadBytes((int)info.ByteSize);
                File.WriteAllBytes(path, data);

                count++;
                if (count % 50 == 0)
                    ReportProgress((double)count / assets.Count);
            }
            catch (Exception ex)
            {
                Log($"  Error en PathID {info.PathId}: {ex.Message}");
            }
        }

        Log($"\nExtraccion completada: {count}/{assets.Count} assets extraidos");
        return count;
    }

    public int DumpAllMonoBehaviours(string outputDir, string extension = ".txt")
    {
        Directory.CreateDirectory(outputDir);
        int count = 0;
        int errors = 0;
        var mbAssets = _assetsFile.AssetInfos
            .Where(a => a.TypeId == (int)AssetClassID.MonoBehaviour)
            .ToList();

        Log($"\nExtrayendo {mbAssets.Count} MonoBehaviours a {outputDir}/");

        foreach (var assetInfo in mbAssets)
        {
            try
            {
                var baseField = _manager.GetBaseField(_assetsInst, assetInfo);

                string className = "Unknown";
                string objName = "";
                try
                {
                    var scriptField = baseField["m_Script"];
                    if (scriptField != null && !scriptField.IsDummy)
                    {
                        var parser = new MonoBehaviourParser(this);
                        var (foundClass, foundNs, foundAsm) = parser.GetScriptInfo(scriptField);
                        className = foundClass ?? "Unknown";
                    }
                    var nameField = baseField["m_Name"];
                    if (nameField != null && !nameField.IsDummy)
                        objName = nameField.AsString ?? "";
                }
                catch { }

                string safeName = SanitizeFileName(className);
                string fileName = $"{safeName}_{assetInfo.PathId}{extension}";
                string filePath = Path.Combine(outputDir, fileName);

                var sb = new StringBuilder();
                sb.AppendLine($"=== MonoBehaviour Dump (PathID: {assetInfo.PathId}) ===");
                sb.AppendLine($"Class: {className}");
                if (!string.IsNullOrEmpty(objName))
                    sb.AppendLine($"Name: {objName}");
                sb.AppendLine();
                AppendFieldDump(sb, baseField, 0);

                File.WriteAllText(filePath, sb.ToString());
                count++;

                if (count % 100 == 0)
                    ReportProgress((double)count / mbAssets.Count);
            }
            catch (Exception ex)
            {
                errors++;
                if (errors <= 5)
                    Log($"  Error en PathID {assetInfo.PathId}: {ex.Message}");
            }
        }

        Log($"\nExtraccion completada: {count} MonoBehaviours, {errors} errores");
        return count;
    }

    public void DumpMonoBehaviour(long pathId, string outputPath)
    {
        var assetInfo = _assetsFile.GetAssetInfo(pathId);
        if (assetInfo == null)
            throw new Exception($"Asset con PathID {pathId} no encontrado.");

        if (assetInfo.TypeId != (int)AssetClassID.MonoBehaviour)
            throw new Exception($"El asset {pathId} no es un MonoBehaviour.");

        var baseField = _manager.GetBaseField(_assetsInst, assetInfo);

        var sb = new StringBuilder();
        sb.AppendLine($"=== MonoBehaviour Dump (PathID: {pathId}) ===");
        sb.AppendLine();
        AppendFieldDump(sb, baseField, 0);

        File.WriteAllText(outputPath, sb.ToString());
        Log($"Dump guardado en: {outputPath}");
    }

    public string ReadMonoBehaviour(long pathId)
    {
        var assetInfo = _assetsFile.GetAssetInfo(pathId);
        if (assetInfo == null)
            throw new Exception($"Asset con PathID {pathId} no encontrado.");

        if (assetInfo.TypeId != (int)AssetClassID.MonoBehaviour)
            throw new Exception($"El asset {pathId} no es un MonoBehaviour.");

        var baseField = _manager.GetBaseField(_assetsInst, assetInfo);

        var sb = new StringBuilder();
        sb.AppendLine($"=== Leyendo MonoBehaviour (PathID: {pathId}) ===");
        sb.AppendLine();

        // Leer campos basicos
        try
        {
            var goField = baseField["m_GameObject"];
            if (goField != null && !goField.IsDummy)
            {
                sb.AppendLine($"m_GameObject: FileID={goField["m_FileID"]?.AsInt}, PathID={goField["m_PathID"]?.AsLong}");
            }
        }
        catch { }

        try
        {
            var enabledField = baseField["m_Enabled"];
            if (enabledField != null && !enabledField.IsDummy)
                sb.AppendLine($"m_Enabled: {enabledField.AsInt}");
        }
        catch { }

        try
        {
            var nameField = baseField["m_Name"];
            if (nameField != null && !nameField.IsDummy)
                sb.AppendLine($"m_Name: {nameField.AsString}");
        }
        catch { }

        try
        {
            var scriptField = baseField["m_Script"];
            if (scriptField != null && !scriptField.IsDummy)
            {
                var parser = new MonoBehaviourParser(this);
                var (className, nameSpace, assemblyName) = parser.GetScriptInfo(scriptField);
                sb.AppendLine($"\n=== Informacion del Script ===");
                sb.AppendLine($"ClassName: {className}");
                sb.AppendLine($"Namespace: {nameSpace}");
                sb.AppendLine($"AssemblyName: {assemblyName}");
            }
        }
        catch { }

        sb.AppendLine($"\n=== Campos Serializados ===");
        AppendFieldDump(sb, baseField, 0);

        return sb.ToString();
    }

    public AssetTypeValueField? FindMBField(long pathId, string fieldPath)
    {
        var assetInfo = _assetsFile.GetAssetInfo(pathId);
        if (assetInfo == null) return null;

        var baseField = _manager.GetBaseField(_assetsInst, assetInfo);
        if (baseField == null) return null;

        var parts = fieldPath.Split('.');
        AssetTypeValueField? current = baseField;
        foreach (var part in parts)
        {
            if (part.Contains('[') && part.EndsWith(']'))
            {
                var bracketIndex = part.IndexOf('[');
                var fieldName = part[..bracketIndex];
                var arrayIndex = int.Parse(part[(bracketIndex + 1)..^1]);

                current = current[fieldName];
                if (current == null) return null;

                if (current.Children.Count > arrayIndex)
                    current = current[arrayIndex];
                else
                    return null;
            }
            else
            {
                current = current[part];
                if (current == null) return null;
            }
        }

        return current;
    }

    public void ModifyMonoBehaviour(long pathId, string fieldPath, string value)
    {
        var assetInfo = _assetsFile.GetAssetInfo(pathId);
        if (assetInfo == null)
            throw new Exception($"Asset con PathID {pathId} no encontrado.");

        var baseField = _manager.GetBaseField(_assetsInst, assetInfo);
        var field = FindMBField(pathId, fieldPath);
        if (field == null)
            throw new Exception($"Campo '{fieldPath}' no encontrado en MonoBehaviour {pathId}.");

        SetFieldValue(field, value);
        Log($"Campo '{fieldPath}' = {value} en MonoBehaviour {pathId}");

        SaveBundle();
    }

    public void ModifyField(AssetTypeValueField field, string value)
    {
        SetFieldValue(field, value);
    }

    public void SaveBundle()
    {
        string outputPath = _bundlePath + ".modified";
        using var writer = new AssetsFileWriter(File.OpenWrite(outputPath));

        var dirInfos = _bundleInst.file.BlockAndDirInfo.DirectoryInfos;
        foreach (var dirInfo in dirInfos)
        {
            if (dirInfo.Name.EndsWith(".assets") || (dirInfo.Flags & 4) != 0)
            {
                dirInfo.SetNewData(_assetsFile);
                break;
            }
        }

        _bundleInst.file.Write(writer);
        Log($"Bundle guardado en: {outputPath}");
    }

    // ---- Field Dump Helpers ----

    public static void AppendFieldDump(StringBuilder sb, AssetTypeValueField field, int indent)
    {
        foreach (var child in field.Children)
        {
            AppendFieldRecursive(sb, child, indent);
        }
    }

    private static void AppendFieldRecursive(StringBuilder sb, AssetTypeValueField field, int indent)
    {
        string indentStr = new string(' ', indent * 2);
        string fieldName = field.FieldName ?? "(unnamed)";
        string fieldType = field.TemplateField?.ValueType.ToString() ?? "Unknown";
        string valueStr = FormatFieldValue(field);

        // Si es un PPtr, mostrar FileID y PathID
        if (fieldType == "PPtr" && field.Children.Count >= 2)
        {
            int fileId = 0;
            long pathId = 0;
            try { fileId = field["m_FileID"]?.AsInt ?? 0; } catch { }
            try { pathId = field["m_PathID"]?.AsLong ?? 0; } catch { }
            sb.AppendLine($"{indentStr}{fieldName} (PPtr): FileID={fileId}, PathID={pathId}");
            return;
        }

        if (fieldType == "Array" || fieldType == "vector")
        {
            int arraySize = 0;
            try { arraySize = field.Children.Count; } catch { }
            sb.AppendLine($"{indentStr}{fieldName} ({fieldType}, {arraySize} items):");

            for (int i = 0; i < arraySize; i++)
            {
                sb.AppendLine($"{indentStr}  [{i}]:");
                AppendFieldDump(sb, field[i], indent + 2);
            }
            return;
        }

        // Si tiene hijos y no es un tipo simple, recursivo
        if (field.Children.Count > 0 && !IsSimpleType(fieldType))
        {
            sb.AppendLine($"{indentStr}{fieldName} ({fieldType}):");
            AppendFieldDump(sb, field, indent + 1);
            return;
        }

        // Valor simple
        sb.AppendLine($"{indentStr}{fieldName} ({fieldType}): {valueStr}");
    }

    private static bool IsSimpleType(string typeName)
    {
        return typeName is "int" or "float" or "double" or "bool" or "UInt8"
            or "SInt64" or "UInt64" or "string" or "char"
            or "unsigned int" or "PPtr";
    }

    private static string FormatFieldValue(AssetTypeValueField field)
    {
        try
        {
            return field.TemplateField?.ValueType switch
            {
                AssetValueType.String => field.AsString ?? "(null)",
                AssetValueType.Int32 => field.AsInt.ToString(),
                AssetValueType.Int64 => field.AsLong.ToString(),
                AssetValueType.Float => field.AsFloat.ToString("G"),
                AssetValueType.Double => field.AsDouble.ToString("G"),
                AssetValueType.Bool => field.AsBool.ToString(),
                _ => field.AsString ?? "(value)"
            };
        }
        catch
        {
            return "(error)";
        }
    }

    private static void SetFieldValue(AssetTypeValueField field, string value)
    {
        switch (field.TemplateField.ValueType)
        {
            case AssetValueType.String:
                field.AsString = value;
                break;
            case AssetValueType.Int32:
                field.AsInt = int.Parse(value);
                break;
            case AssetValueType.Int64:
                field.AsLong = long.Parse(value);
                break;
            case AssetValueType.Float:
                field.AsFloat = float.Parse(value);
                break;
            case AssetValueType.Double:
                field.AsDouble = double.Parse(value);
                break;
            case AssetValueType.Bool:
                field.AsBool = bool.Parse(value);
                break;
            default:
                throw new Exception($"Tipo de campo no soportado: {field.TemplateField.ValueType}");
        }
    }

    public static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Unnamed" : name;
    }

    // ---- Logging ----

    private void Log(string message)
    {
        OnLog?.Invoke(message);
    }

    private void ReportProgress(double progress)
    {
        OnProgress?.Invoke(progress);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _manager.UnloadAll();
            _disposed = true;
        }
    }
}

// ---- Data Models ----

public class BundleFileInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string SizeFormatted
    {
        get
        {
            return Size switch
            {
                >= 1_000_000_000 => $"{Size / 1_000_000_000.0:F1} GB",
                >= 1_000_000 => $"{Size / 1_000_000.0:F1} MB",
                >= 1_000 => $"{Size / 1_000.0:F1} KB",
                _ => $"{Size} bytes"
            };
        }
    }
}

public class AssetInfo
{
    public long PathId { get; set; }
    public int TypeId { get; set; }
    public string ClassName { get; set; } = "";
    public string Name { get; set; } = "";
    public uint ByteSize { get; set; }
}

public class AssetTypeSummary
{
    public int TypeId { get; set; }
    public string ClassName { get; set; } = "";
    public int Count { get; set; }
    public long TotalSize { get; set; }
    public string TotalSizeFormatted
    {
        get
        {
            return TotalSize switch
            {
                < 1024 => $"{TotalSize} B",
                < 1024 * 1024 => $"{TotalSize / 1024.0:F1} KB",
                _ => $"{TotalSize / (1024.0 * 1024.0):F1} MB"
            };
        }
    }
}
