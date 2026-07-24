using UnityBundleEditor.App.ViewModels;

namespace UnityBundleEditor.App;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;

    public MainPage()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        BindingContext = _vm;

        // Register invert bool converter
        Resources.Add("InvertBool", new InvertBoolConverter());
    }

    private async void OnLoadBundleClicked(object? sender, EventArgs e)
    {
        await _vm.LoadBundleAsync();
    }

    private async void OnExtractTypeClicked(object? sender, EventArgs e)
    {
        string typeName = await DisplayPromptAsync(
            "Extraer por tipo",
            "Ingresa el nombre del tipo (TextAsset, Texture2D, MonoBehaviour, etc):",
            "Extraer", "Cancelar",
            placeholder: "TextAsset");

        if (!string.IsNullOrWhiteSpace(typeName))
        {
            await _vm.ExtractSelectedTypeAsync(typeName);
        }
    }

    private async void OnDumpMbClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlert(
            "Dump All MonoBehaviours",
            "Esto extraera TODOS los MonoBehaviours como texto.\n" +
            "Puede tomar varios minutos en bundles grandes.\n\n" +
            "¿Continuar?",
            "Sí, extraer", "Cancelar");

        if (confirm)
        {
            await _vm.DumpAllMonoBehavioursAsync();
        }
    }

    private async void OnSetManagedClicked(object? sender, EventArgs e)
    {
        await _vm.SetManagedPathAsync();
    }

    private async void OnSaveBundleClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlert(
            "Guardar Bundle",
            "Se creara una copia modificada del bundle actual.\n" +
            "¿Continuar?",
            "Guardar", "Cancelar");

        if (confirm)
        {
            await _vm.SaveModifiedBundleAsync();
        }
    }
}

/// <summary>
/// Converts true to false and vice versa. Used for visibility toggling.
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return value;
    }
}
