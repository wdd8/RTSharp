using Avalonia.Controls.Templates;
using Avalonia.Controls;

using System.Collections.Generic;
using Avalonia.Metadata;
using RTSharp.Core;
using Dock.Model.Mvvm.Controls;

namespace RTSharp
{
    public class DocumentWithIconDataTemplate : IDataTemplate
    {
        [Content]
        public Dictionary<string, IDataTemplate> AvailableTemplates { get; } = new Dictionary<string, IDataTemplate>();

        public Control Build(object param)
        {
            if (param is IDocumentWithIcon)
                return AvailableTemplates["WithIcon"].Build(param);

            return AvailableTemplates["WithoutIcon"].Build(param);
        }

        public bool Match(object data)
        {
            return data is Document;
        }
    }
}
