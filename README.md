# UnityBundleEditor

Editor de bundles Unity (.unity3d) basado en **AssetsTools.NET v3** con soporte completo para **MonoBehaviours sin TypeTree**.

## Caracteristicas

- **Leer** assets de bundles Unity (.unity3d)
- **Extraer** assets individuales o todos a la vez
- **Importar/Sobrescribir** assets con datos binarios
- **Modificar** campos de MonoBehaviours directamente
- **Parsear MonoBehaviours** sin TypeTree usando los ensamblados del juego (Mono o IL2CPP)
- **Buscar** MonoBehaviours por nombre de clase

## Requisitos

- .NET 8.0 SDK
- Archivo `classdata.tpk` (descargar de [AssetRipper/Tpk](https://github.com/AssetRipper/Tpk))
- Carpeta `Managed/` del juego (para MonoBehaviours)

## Instalacion

```bash
# Clonar o descargar el proyecto
cd UnityBundleEditor

# Restaurar paquetes NuGet
dotnet restore

# Compilar
dotnet build

# O compilar para release
dotnet publish -c Release
```

Coloca `classdata.tpk` junto al ejecutable o en el directorio del bundle.

## Uso

```bash
# Ejecutar desde terminal (dotnet run)
dotnet run -- <ruta/data.unity3d> [ruta/Managed] <comando> [args...]

# O con el ejecutable compilado
./UnityBundleEditor <ruta/data.unity3d> [ruta/Managed] <comando> [args...]
```

### Comandos

| Comando | Descripcion | Ejemplo |
|---------|-------------|---------|
| `list` | Listar todos los assets | `dotnet run -- data.unity3d ./Managed list` |
| `extract` | Extraer un asset por PathID | `dotnet run -- data.unity3d ./Managed extract 12345 output.bin` |
| `extract-all` | Extraer todos los assets | `dotnet run -- data.unity3d ./Managed extract-all ./extracted` |
| `read-mb` | Leer un MonoBehaviour | `dotnet run -- data.unity3d ./Managed read-mb 67890` |
| `dump-mb` | Dump completo de MonoBehaviour a archivo | `dotnet run -- data.unity3d ./Managed dump-mb 67890 dump.txt` |
| `find-mb` | Buscar MonoBehaviours por clase | `dotnet run -- data.unity3d ./Managed find-mb PlayerController` |
| `import` | Importar datos a un asset | `dotnet run -- data.unity3d ./Managed import 12345 newdata.bin` |
| `modify-mb` | Modificar campo de MonoBehaviour | `dotnet run -- data.unity3d ./Managed modify-mb 67890 "m_Speed=10.5"` |

### Modificacion de campos

Formato: `campo.subcampo[index]=valor`

```bash
# Campo simple
dotnet run -- data.unity3d ./Managed modify-mb 12345 "m_Health=100"

# Campo anidado
dotnet run -- data.unity3d ./Managed modify-mb 12345 "m_Stats.m_Level=5"

# Elemento de array
dotnet run -- data.unity3d ./Managed modify-mb 12345 "m_Items[0].m_Count=99"
```

## Como funciona el parser de MonoBehaviours sin TypeTree

Cuando un bundle se compila con **DisableWriteTypeTree** (comun en builds release), los assets no incluyen informacion de tipos. Para deserializar MonoBehaviours correctamente:

1. **Detectar el backend**: Se verifica si el juego usa Mono (DLLs en `Managed/`) o IL2CPP (`global-metadata.dat`).

2. **MonoTempGenerator**: Se configura `manager.MonoTempGenerator` con:
   - `MonoCecilTempGenerator` para juegos Mono - lee los DLLs con Mono.Cecil
   - `Cpp2IlTempGenerator` para juegos IL2CPP - usa global-metadata.dat y GameAssembly.dll

3. **Reconstruccion de campos**: Cuando se llama `GetBaseField()` en un MonoBehaviour, AssetsTools.NET usa el generador para crear un "template" de campos basado en la definicion de la clase en los ensamblados.

4. **Deserializacion**: Con el template, se pueden leer los bytes del asset y mapearlos a los campos correctos.

## Estructura del Proyecto

```
UnityBundleEditor/
├── UnityBundleEditor.csproj    # Archivo de proyecto
├── Program.cs                  # Punto de entrada CLI
├── BundleManager.cs            # Gestion de bundles/assets
├── AssetOperations.cs          # Operaciones de lectura/escritura
├── MonoBehaviourParser.cs      # Parser de MonoBehaviours
├── Helpers/
│   ├── AssetHelper.cs          # Utilidades para assets
│   └── MonoHelper.cs           # Utilidades para MonoBehaviours
└── README.md                   # Este archivo
```

## Dependencias

- `AssetsTools.NET` v3.0.0
- `AssetsTools.NET.Extra` v3.0.0
- `AssetsTools.NET.MonoCecil` v3.0.0
- `AssetsTools.NET.Cpp2IL` v3.0.0
- `Mono.Cecil` v0.11.5

## Licencia

Este proyecto usa AssetsTools.NET (MIT License). El codigo del proyecto es de uso libre.
