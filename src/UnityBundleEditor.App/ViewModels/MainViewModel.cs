using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Microsoft.Maui.Storage;

namespace UnityBundleEditor.App.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private BundleManager? _bundle;
    private CancellationTokenSource? _progressCts;

    // ---- Properties ----

    public ObservableCollection<string> LogEntries { get; } = new();
    public ObservableCollection<AssetListItem> Assets { get; } = new();
    public ObservableCollection<AssetTypeGroup> AssetTypes { get; } = new();
    public ObservableCollection<BundleFileItem> BundleFiles { get; } = new();
    public ObservableCollection<FieldEditorItem> FieldEditors { get; } = new();

    private string _status = "Listo";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    private bool _isProgressIndeterminate;
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set { _isProgressIndeterminate = value; OnPropertyChanged(); }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsIdle => !IsLoading;

    private bool _isBundleLoaded;
    public bool IsBundleLoaded
    {
        get => _isBundleLoaded;
        set { _isBundleLoaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowBrowser)); }
    }

    public bool ShowBrowser => IsBundleLoaded;

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); FilterAssets(); }
    }

    private AssetListItem? _selectedAsset;
    public AssetListItem? SelectedAsset
    {
        get => _selectedAsset;
        set { _selectedAsset = value; OnPropertyChanged(); OnSelectedAssetChanged(); }
    }

    private int _totalAssets;
    public int TotalAssets
    {
        get => _totalAssets;
        set { _totalAssets = value; OnPropertyChanged(); }
    }

    private int _filteredCount;
    public int FilteredCount
    {
        get => _filteredCount;
        set { _filteredCount = value; OnPropertyChanged(); }
    }

    // ---- Commands ----

    public async Task LoadBundleAsync()
    {
        try
        {
            // Pick data.unity3d
            var bundleResult = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecciona data.unity3d"
            });

            if (bundleResult == null) return;

            IsLoading = true;
            IsProgressIndeterminate = true;
            Status = "Cargando bundle...";
            Log("Seleccionando bundle: " + bundleResult.FileName);
            Log("Cargando...");

            // Pick libso/ folder (on Android we use a workaround - pick a file from managed directory)
            string? managedPath = null;
            try
            {
                var folderResult = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Selecciona global-metadata.dat o un DLL de Managed"
                });

                if (folderResult != null)
                {
                    managedPath = Path.GetDirectoryName(folderResult.FullPath);
                    Log($"Managed path: {managedPath}");
                }
            }
            catch
            {
                Log("No se selecciono carpeta Managed, los MonoBehaviours pueden no cargarse.");
            }

            // Load bundle in background
            await Task.Run(() =>
            {
                _bundle?.Dispose();
                _bundle = new BundleManager(bundleResult.FullPath, managedPath);
                _bundle.OnLog += msg =>
                {
                    MainThread.BeginInvokeOnMainThread(() => Log(msg));
                };
                _bundle.OnProgress += progress =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        ProgressValue = progress;
                        IsProgressIndeterminate = false;
                    });
                };
            });

            // Populate data
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Status = "Bundle cargado. Cargando lista de assets...";
                PopulateAssets();
                PopulateBundleFiles();
                PopulateTypes();
                IsBundleLoaded = true;
                Status = $"Bundle cargado: {Path.GetFileName(bundleResult.FileName)}";
                IsProgressIndeterminate = false;
                ProgressValue = 0;
            });
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            IsProgressIndeterminate = false;
        }
    }

    public void PopulateAssets()
    {
        if (_bundle == null) return;

        Assets.Clear();
        TotalAssets = _bundle.AssetsFile.AssetInfos.Count;

        foreach (var info in _bundle.AssetsFile.AssetInfos)
        {
            string className = ((AssetClassID)info.TypeId).ToString();
            Assets.Add(new AssetListItem
            {
                PathId = info.PathId,
                TypeId = info.TypeId,
                ClassName = className,
                FileSize = info.ByteSize,
                DisplayName = $"#{info.PathId} ({info.ByteSize} bytes)",
                TypeColor = GetTypeColor(className)
            });
        }

        FilteredCount = Assets.Count;
        Status = $"{Assets.Count} assets cargados";
    }

    public void PopulateTypes()
    {
        if (_bundle == null) return;

        AssetTypes.Clear();
        var grouped = _bundle.AssetsFile.AssetInfos
            .GroupBy(a => a.TypeId)
            .Select(g => new AssetTypeGroup
            {
                TypeId = g.Key,
                ClassName = ((AssetClassID)g.Key).ToString(),
                Count = g.Count(),
                TotalSize = g.Sum(a => (long)a.ByteSize)
            })
            .OrderByDescending(x => x.Count);

        foreach (var t in grouped)
            AssetTypes.Add(t);
    }

    public void PopulateBundleFiles()
    {
        if (_bundle == null) return;

        BundleFiles.Clear();
        var dirInfos = _bundle.BundleInstance.file.BlockAndDirInfo.DirectoryInfos;
        for (int i = 0; i < dirInfos.Count; i++)
        {
            BundleFiles.Add(new BundleFileItem
            {
                Index = i,
                Name = dirInfos[i].Name,
                Size = dirInfos[i].DecompressedSize
            });
        }
    }

    public async Task ExtractSelectedTypeAsync(string typeName)
    {
        if (_bundle == null) return;

        try
        {
            string outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                $"extracted_{typeName}");

            IsLoading = true;
            Status = $"Extrayendo {typeName}...";

            int count = 0;
            await Task.Run(() =>
            {
                count = _bundle.ExtractAssetsByType(typeName, outputDir);
            });

            Status = $"Extraidos {count} assets de tipo {typeName}";
            Log($"Extraccion completada: {count} assets en {outputDir}");
        }
        catch (Exception ex)
        {
            Log($"Error extrayendo {typeName}: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task DumpAllMonoBehavioursAsync()
    {
        if (_bundle == null) return;

        try
        {
            string outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "mb_dump");

            IsLoading = true;
            Status = "Extrayendo todos los MonoBehaviours...";

            int count = 0;
            await Task.Run(() =>
            {
                count = _bundle.DumpAllMonoBehaviours(outputDir);
            });

            Status = $"Extraidos {count} MonoBehaviours";
            Log($"Dump completado: {count} MonoBehaviours en {outputDir}");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SaveModifiedBundleAsync()
    {
        if (_bundle == null) return;

        try
        {
            IsLoading = true;
            Status = "Guardando bundle modificado...";

            await Task.Run(() => _bundle.SaveBundle());

            Status = "Bundle guardado";
            Log("Bundle modificado guardado exitosamente.");
        }
        catch (Exception ex)
        {
            Log($"Error guardando bundle: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnSelectedAssetChanged()
    {
        FieldEditors.Clear();

        if (_bundle == null || SelectedAsset == null) return;

        try
        {
            var baseField = _bundle.GetBaseFieldByPathId(SelectedAsset.PathId);
            if (baseField == null) return;

            // Add basic fields
            try
            {
                var nameField = baseField["m_Name"];
                if (nameField != null && !nameField.IsDummy)
                {
                    FieldEditors.Add(new FieldEditorItem
                    {
                        FieldName = "m_Name",
                        FieldType = "string",
                        Value = nameField.AsString ?? "",
                        IsEditable = true
                    });
                }
            }
            catch { }

            try
            {
                var enabledField = baseField["m_Enabled"];
                if (enabledField != null && !enabledField.IsDummy)
                {
                    FieldEditors.Add(new FieldEditorItem
                    {
                        FieldName = "m_Enabled",
                        FieldType = "int",
                        Value = enabledField.AsInt.ToString(),
                        IsEditable = true
                    });
                }
            }
            catch { }

            // Add script info if MonoBehaviour
            if (SelectedAsset.ClassName == "MonoBehaviour")
            {
                try
                {
                    var scriptField = baseField["m_Script"];
                    if (scriptField != null && !scriptField.IsDummy)
                    {
                        int fileId = scriptField["m_FileID"]?.AsInt ?? 0;
                        long pathId = scriptField["m_PathID"]?.AsLong ?? 0;
                        FieldEditors.Add(new FieldEditorItem
                        {
                            FieldName = "m_Script (PPtr)",
                            FieldType = "PPtr",
                            Value = $"FileID={fileId}, PathID={pathId}",
                            IsEditable = false
                        });

                        // Try to get class name
                        var parser = new MonoBehaviourParser(_bundle);
                        var (className, ns, asm) = parser.GetScriptInfo(scriptField);
                        if (className != null)
                        {
                            FieldEditors.Add(new FieldEditorItem
                            {
                                FieldName = "Script Class",
                                FieldType = "info",
                                Value = $"{ns}.{className} [{asm}]",
                                IsEditable = false
                            });
                        }
                    }
                }
                catch { }

                // Add serialized fields
                AddSerializedFields(baseField, "");
            }

            Status = $"Editando: PathID {SelectedAsset.PathId}";
        }
        catch (Exception ex)
        {
            FieldEditors.Add(new FieldEditorItem
            {
                FieldName = "Error",
                FieldType = "error",
                Value = ex.Message,
                IsEditable = false
            });
        }
    }

    private void AddSerializedFields(AssetTypeValueField field, string prefix)
    {
        foreach (var child in field.Children)
        {
            string fieldName = child.FieldName ?? "unnamed";
            string fullPath = string.IsNullOrEmpty(prefix) ? fieldName : $"{prefix}.{fieldName}";
            string typeName = child.TemplateField?.ValueType.ToString() ?? "Unknown";

            // Skip internal Unity fields we already show
            if (fieldName is "m_Name" or "m_Enabled" or "m_Script" or "m_GameObject")
                continue;

            if (child.Children.Count > 0 && typeName is not ("PPtr" or "string"))
            {
                // Nested object - add as header only
                FieldEditors.Add(new FieldEditorItem
                {
                    FieldName = fullPath,
                    FieldType = typeName,
                    Value = $"[{child.Children.Count} children]",
                    IsEditable = false,
                    IsSection = true
                });
                AddSerializedFields(child, fullPath);
            }
            else
            {
                string value = GetFieldDisplayValue(child);
                FieldEditors.Add(new FieldEditorItem
                {
                    FieldName = fullPath,
                    FieldType = typeName,
                    Value = value,
                    IsEditable = IsFieldEditable(typeName)
                });
            }
        }
    }

    private static string GetFieldDisplayValue(AssetTypeValueField field)
    {
        try
        {
            if (field.TemplateField?.ValueType == AssetValueType.String)
                return field.AsString ?? "(null)";

            if (field.Children.Count >= 2 &&
                field["m_FileID"] != null && field["m_PathID"] != null)
            {
                int fid = field["m_FileID"]?.AsInt ?? 0;
                long pid = field["m_PathID"]?.AsLong ?? 0;
                return $"FileID={fid}, PathID={pid}";
            }

            return field.TemplateField?.ValueType switch
            {
                AssetValueType.Int32 => field.AsInt.ToString(),
                AssetValueType.Int64 => field.AsLong.ToString(),
                AssetValueType.Float => field.AsFloat.ToString("G"),
                AssetValueType.Double => field.AsDouble.ToString("G"),
                AssetValueType.Bool => field.AsBool.ToString(),
                _ => field.AsString ?? "(value)"
            };
        }
        catch { return "(error)"; }
    }

    private static bool IsFieldEditable(string typeName)
    {
        return typeName is "int" or "float" or "double" or "bool" or "string" or "UInt8"
            or "SInt64" or "UInt64" or "char" or "Int32" or "Int64";
    }

    public void UpdateFieldValue(string fieldName, string newValue)
    {
        if (_bundle == null || SelectedAsset == null) return;

        try
        {
            var field = _bundle.FindMBField(SelectedAsset.PathId, fieldName);
            if (field != null)
            {
                _bundle.ModifyField(field, newValue);
                Log($"Campo '{fieldName}' actualizado a: {newValue}");
                Status = $"Campo '{fieldName}' = {newValue}";
            }
        }
        catch (Exception ex)
        {
            Log($"Error actualizando campo '{fieldName}': {ex.Message}");
        }
    }

    private void FilterAssets()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            foreach (var a in Assets)
                a.IsVisible = true;
        }
        else
        {
            var search = SearchText.ToLower();
            foreach (var a in Assets)
            {
                a.IsVisible = a.ClassName.ToLower().Contains(search) ||
                              a.PathId.ToString().Contains(search) ||
                              (a.DisplayName?.ToLower().Contains(search) ?? false);
            }
        }

        FilteredCount = Assets.Count(a => a.IsVisible);
    }

    private void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogEntries.Add($"[{timestamp}] {message}");
        if (LogEntries.Count > 500)
            LogEntries.RemoveAt(0);
    }

    private static Color GetTypeColor(string className)
    {
        return className switch
        {
            "MonoBehaviour" => Color.FromArgb("#4CAF50"),
            "GameObject" => Color.FromArgb("#2196F3"),
            "Transform" => Color.FromArgb("#FF9800"),
            "Texture2D" => Color.FromArgb("#E91E63"),
            "TextAsset" => Color.FromArgb("#9C27B0"),
            "Sprite" => Color.FromArgb("#00BCD4"),
            "Shader" => Color.FromArgb("#607D8B"),
            "AudioClip" => Color.FromArgb("#FF5722"),
            _ => Color.FromArgb("#78909C")
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

// ---- Data Models ----

public class AssetListItem : INotifyPropertyChanged
{
    public long PathId { get; set; }
    public int TypeId { get; set; }
    public string ClassName { get; set; } = "";
    public uint FileSize { get; set; }
    public string DisplayName { get; set; } = "";
    public Color TypeColor { get; set; } = Colors.Gray;

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class AssetTypeGroup
{
    public int TypeId { get; set; }
    public string ClassName { get; set; } = "";
    public int Count { get; set; }
    public long TotalSize { get; set; }
    public string Display => $"{ClassName,-25} {Count,8}  {FormatSize(TotalSize)}";

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
        };
    }
}

public class BundleFileItem
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Display => $"[{Index}] {Name}  ({FormatSize(Size)})";

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F1} GB",
            >= 1_000_000 => $"{bytes / 1_000_000.0:F1} MB",
            >= 1_000 => $"{bytes / 1_000.0:F1} KB",
            _ => $"{bytes} bytes"
        };
    }
}

public class FieldEditorItem : INotifyPropertyChanged
{
    public string FieldName { get; set; } = "";
    public string FieldType { get; set; } = "";
    public bool IsEditable { get; set; }
    public bool IsSection { get; set; }

    private string _value = "";
    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public Color ValueColor => IsEditable ? Colors.White : Color.FromArgb("#B0B0C0");
    public string Display => $"{FieldName}: {Value}";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
