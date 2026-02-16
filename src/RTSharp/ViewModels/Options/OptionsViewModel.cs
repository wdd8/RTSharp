using System;
using System.Collections.Generic;
using RTSharp.ViewModels.Options.Pages;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.ViewModels.Options
{
    public record OptionsItem(string Icon, string Path, string Text, IList<OptionsItem> Children);

    public partial class OptionsViewModel : ObservableObject
    {
        public List<OptionsItem> Items { get; }

        public Dictionary<string, Type> PageMap { get; }

        public Dictionary<Type, object> Pages { get; } = new();

        [ObservableProperty]
        public partial OptionsItem CurrentlySelectedItem { get; set; }

        [ObservableProperty]
        public partial object? SettingsContent { get; set; }

        public OptionsViewModel()
        {
            Items = new List<OptionsItem> {
                new OptionsItem("fa-solid fa-gears", "Behavior", "Behavior", new[] {
                    new OptionsItem("fa-solid fa-gears", "Behavior > Behavior", "Behavior", Array.Empty<OptionsItem>())
                }),
                new OptionsItem("fa-solid fa-book-bookmark", "Caching", "Caching", Array.Empty<OptionsItem>())
            };

            PageMap = new Dictionary<string, Type>() {
                { "Behavior", typeof(BehaviorPageViewModel) },
                { "Caching", typeof(CachingPageViewModel) }
            };

            CurrentlySelectedItem = Items[1];
        }

        partial void OnCurrentlySelectedItemChanged(OptionsItem item)
        {
            this.SettingsContent = GetPage(item.Path);
        }

        public object? GetPage(string Path)
        {
            if (!PageMap.TryGetValue(Path, out var page)) {
                return null;
            }

            if (!Pages.TryGetValue(page, out var vm)) {
                vm = Activator.CreateInstance(page);
            }

            return vm;
        }
    }
}
