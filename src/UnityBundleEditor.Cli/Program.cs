using UnityBundleEditor;

if (args.Length < 2)
{
    ShowHelp();
    return;
}

string bundlePath = args[0];
string? managedPath = null;
int fileIndex = 0;
int commandArgStart = 1;

// Check for optional --file-index before the managed path
if (args.Length > 2 && args[1] == "--file-index" && int.TryParse(args[2], out int idx))
{
    fileIndex = idx;
    commandArgStart = 3;
    managedPath = args.Length > commandArgStart ? args[commandArgStart - 1] : null;
}

// Check if next arg is a path (not a command)
if (commandArgStart < args.Length && !args[commandArgStart].StartsWith("--"))
{
    // Check if it looks like a path (contains / or \ or exists on disk)
    string candidate = args[commandArgStart];
    if (candidate.Contains('/') || candidate.Contains('\\') || Directory.Exists(candidate) || File.Exists(candidate))
    {
        managedPath = candidate;
        commandArgStart++;
    }
}

if (commandArgStart >= args.Length)
{
    ShowHelp();
    return;
}

string command = args[commandArgStart];
string[] cmdArgs = args.Skip(commandArgStart + 1).ToArray();

// Cargar bundle
using var bundle = new BundleManager(bundlePath, managedPath, fileIndex);
bundle.OnLog += msg => Console.WriteLine(msg);

switch (command.ToLower())
{
    case "list-files":
        var files = bundle.ListBundleFiles();
        Console.WriteLine("\n=== Archivos internos del bundle ===");
        Console.WriteLine($"{"Indice",-8} {"Nombre",-50} {"Tamaño",-12}");
        Console.WriteLine(new string('-', 75));
        foreach (var f in files)
            Console.WriteLine($"[{f.Index,-5}] {f.Name,-50} {f.SizeFormatted,-12}");
        break;

    case "list":
        var assets = bundle.ListAssets();
        Console.WriteLine($"\n=== Lista de Assets (total: {assets.Count}) ===");
        Console.WriteLine($"{"PathID",-12} {"TypeID",-8} {"Class",-25} {"Name",-35} {"Size",-10}");
        Console.WriteLine(new string('-', 95));
        foreach (var a in assets)
            Console.WriteLine($"{a.PathId,-12} {a.TypeId,-8} {a.ClassName,-25} {a.Name,-35} {a.ByteSize,-10}");
        break;

    case "list-types":
        var types = bundle.ListAssetTypes();
        Console.WriteLine("\n=== Resumen de Tipos de Assets ===");
        Console.WriteLine($"{"TypeID",-8} {"Class",-30} {"Cantidad",-10} {"Total Size",-15}");
        Console.WriteLine(new string('-', 68));
        foreach (var t in types)
            Console.WriteLine($"{t.TypeId,-8} {t.ClassName,-30} {t.Count,-10} {t.TotalSizeFormatted,-15}");
        Console.WriteLine($"\nTotal: {types.Sum(t => t.Count)} assets en {types.Count} tipos distintos");
        break;

    case "extract":
        if (cmdArgs.Length < 2)
        {
            Console.WriteLine("Uso: extract <PathID> <output>");
            return;
        }
        bundle.ExtractAsset(long.Parse(cmdArgs[0]), cmdArgs[1]);
        break;

    case "extract-all":
        string dir = cmdArgs.Length > 0 ? cmdArgs[0] : "extracted";
        bundle.ExtractAllAssets(dir);
        break;

    case "extract-type":
        if (cmdArgs.Length < 1)
        {
            Console.WriteLine("Uso: extract-type <TypeName> [outputDir]");
            return;
        }
        bundle.ExtractAssetsByType(cmdArgs[0], cmdArgs.Length > 1 ? cmdArgs[1] : cmdArgs[0]);
        break;

    case "read-mb":
        if (cmdArgs.Length < 1)
        {
            Console.WriteLine("Uso: read-mb <PathID>");
            return;
        }
        Console.WriteLine(bundle.ReadMonoBehaviour(long.Parse(cmdArgs[0])));
        break;

    case "dump-mb":
        if (cmdArgs.Length < 2)
        {
            Console.WriteLine("Uso: dump-mb <PathID> <output>");
            return;
        }
        bundle.DumpMonoBehaviour(long.Parse(cmdArgs[0]), cmdArgs[1]);
        break;

    case "dump-all-mb":
        string outDir = cmdArgs.Length > 0 ? cmdArgs[0] : "mb_dump";
        bundle.DumpAllMonoBehaviours(outDir);
        break;

    case "find-mb":
        if (cmdArgs.Length < 1)
        {
            Console.WriteLine("Uso: find-mb <ClassName>");
            return;
        }
        var parser = new MonoBehaviourParser(bundle);
        var found = parser.FindMonoBehavioursByScriptName(cmdArgs[0]);
        Console.WriteLine($"\n=== MonoBehaviours de clase: {cmdArgs[0]} ===");
        Console.WriteLine($"Encontrados: {found.Count}");
        Console.WriteLine($"{"PathID",-12} {"Name",-35}");
        Console.WriteLine(new string('-', 50));
        foreach (var f in found)
            Console.WriteLine($"{f.PathId,-12} {f.Name,-35}");
        break;

    case "modify-mb":
        if (cmdArgs.Length < 3)
        {
            Console.WriteLine("Uso: modify-mb <PathID> <campo=valor>");
            return;
        }
        var eqIndex = cmdArgs[1].IndexOf('=');
        if (eqIndex < 0)
        {
            Console.WriteLine("Formato invalido. Usa: campo=valor");
            return;
        }
        bundle.ModifyMonoBehaviour(long.Parse(cmdArgs[0]), cmdArgs[1][..eqIndex], cmdArgs[1][(eqIndex + 1)..]);
        break;

    default:
        Console.WriteLine($"Comando desconocido: {command}");
        ShowHelp();
        break;
}

static void ShowHelp()
{
    Console.WriteLine("UnityBundleEditor CLI");
    Console.WriteLine("Uso: UnityBundleEditor <bundle.unity3d> [managed/] [--file-index N] <comando> [args...]");
    Console.WriteLine();
    Console.WriteLine("Comandos:");
    Console.WriteLine("  list-files              Listar archivos internos del bundle");
    Console.WriteLine("  list                    Listar todos los assets");
    Console.WriteLine("  list-types              Resumen de tipos de assets");
    Console.WriteLine("  extract <PathID> <out>  Extraer un asset por PathID");
    Console.WriteLine("  extract-all [dir]       Extraer todos los assets");
    Console.WriteLine("  extract-type <T> [dir]  Extraer assets por tipo (TextAsset, Texture2D, etc)");
    Console.WriteLine("  read-mb <PathID>        Leer MonoBehaviour como texto");
    Console.WriteLine("  dump-mb <PathID> <out>  Guardar dump de MonoBehaviour a archivo");
    Console.WriteLine("  dump-all-mb [dir]       Extraer TODOS los MonoBehaviours");
    Console.WriteLine("  find-mb <ClassName>     Buscar MonoBehaviours por clase");
    Console.WriteLine("  modify-mb <PathID> <c=v> Modificar campo de MonoBehaviour");
}
