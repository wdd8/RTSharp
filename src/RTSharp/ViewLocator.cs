using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

using CommunityToolkit.Mvvm.ComponentModel;

using Dock.Model.Core;

namespace RTSharp
{
    public class ViewLocator : IDataTemplate
    {
        private static Dictionary<string, Control> Cache = new();

        public Control Build(object data)
        {
			var name = data.GetType().FullName!.Replace("ViewModel", "View");
            var type = data.GetType().Assembly.GetType(name);

            if (data is IDockable dockable) {
                name += "_" + dockable.Id;
			}

            if (type != null)
            {
                if (Cache.TryGetValue(name, out var control)) {
                    return control;
                }
                return Cache[name] = (Control)Activator.CreateInstance(type)!;
            }
            else
            {
                return new TextBlock { Text = "Not Found: " + name };
            }
        }

        public bool Match(object data)
        {
            return data is ObservableObject || data is IDockable;
        }
    }
}