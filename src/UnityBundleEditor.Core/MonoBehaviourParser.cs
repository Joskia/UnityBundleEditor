using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace UnityBundleEditor;

public class MonoBehaviourParser
{
    private readonly BundleManager _bundle;

    public MonoBehaviourParser(BundleManager bundle)
    {
        _bundle = bundle;
    }

    /// <summary>
    /// Obtiene informacion del script a partir del campo m_Script (PPtr<<MonoScript>>)
    /// </summary>
    public (string? className, string? nameSpace, string? assemblyName) GetScriptInfo(AssetTypeValueField scriptField)
    {
        try
        {
            int fileId = scriptField["m_FileID"]?.AsInt ?? 0;
            long pathId = scriptField["m_PathID"]?.AsLong ?? 0;

            if (pathId == 0)
                return (null, null, null);

            // Obtener el MonoScript usando GetExtAsset
            var extAsset = _bundle.Manager.GetExtAsset(_bundle.AssetsInstance, 0, pathId);
            if (extAsset.baseField == null || extAsset.file == null)
                return (null, null, null);

            var scriptBaseField = extAsset.baseField;

            string? className = scriptBaseField["m_ClassName"]?.AsString;
            string? nameSpace = scriptBaseField["m_Namespace"]?.AsString;
            string? assemblyName = scriptBaseField["m_AssemblyName"]?.AsString;

            return (className, nameSpace, assemblyName);
        }
        catch
        {
            return (null, null, null);
        }
    }

    /// <summary>
    /// Busca todos los MonoBehaviours que corresponden a un nombre de clase especifico.
    /// </summary>
    public List<AssetInfo> FindMonoBehavioursByScriptName(string scriptName)
    {
        var results = new List<AssetInfo>();

        foreach (var assetInfo in _bundle.AssetsFile.AssetInfos)
        {
            if (assetInfo.TypeId != (int)AssetClassID.MonoBehaviour)
                continue;

            try
            {
                var baseField = _bundle.Manager.GetBaseField(_bundle.AssetsInstance, assetInfo);
                var scriptField = baseField["m_Script"];

                if (scriptField != null && !scriptField.IsDummy)
                {
                    var (className, _, _) = GetScriptInfo(scriptField);
                    if (string.Equals(className, scriptName, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new AssetInfo
                        {
                            PathId = assetInfo.PathId,
                            TypeId = assetInfo.TypeId,
                            ClassName = className ?? "Unknown",
                            Name = baseField["m_Name"]?.AsString ?? "N/A",
                            ByteSize = assetInfo.ByteSize
                        });
                    }
                }
            }
            catch { }
        }

        return results;
    }
}
