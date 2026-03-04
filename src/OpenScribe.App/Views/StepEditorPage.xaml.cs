using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenScribe.App.ViewModels;
using OpenScribe.Core.Models;

namespace OpenScribe.App.Views;

public sealed partial class StepEditorPage : Page
{
    public StepEditorViewModel ViewModel { get; }

    public StepEditorPage()
    {
        ViewModel = App.Host.Services.GetRequiredService<StepEditorViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ProcessStep step)
        {
            ViewModel.LoadStep(step);

            // Load screenshot if available
            if (!string.IsNullOrEmpty(step.AnnotatedScreenshotPath) && File.Exists(step.AnnotatedScreenshotPath))
            {
                LoadScreenshot(step.AnnotatedScreenshotPath);
            }
            else if (!string.IsNullOrEmpty(step.ScreenshotPath) && File.Exists(step.ScreenshotPath))
            {
                LoadScreenshot(step.ScreenshotPath);
            }
        }
    }

    private async void LoadScreenshot(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var stream = await file.OpenReadAsync();
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bitmap.SetSourceAsync(stream);
            StepScreenshot.Source = bitmap;
        }
        catch
        {
            // Silently fail if screenshot can't be loaded
        }
    }
}
