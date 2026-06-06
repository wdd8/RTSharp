namespace RTSharp.ViewModels.Options.Pages;

public interface ISettingsLoadable
{
    void Load();
    void ApplyToConfig(Core.Config config);
}
