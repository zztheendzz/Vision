using System;
using System.Collections.Generic;
using System.Text;
using Vision.Tools.Abstractions;

namespace UI.ViewModels.ToolboxAndParam
{
    /// <summary>Một tool trong toolbox. Khi kéo thả, chỉ ToolId được mang đi.</summary>
    public sealed class ToolboxItemViewModel
    {
        public string ToolId { get; }
        public string DisplayName { get; }
        public ToolKind Kind { get; }

        /// <summary>Hiện khi rê chuột lên: tool nhận gì, trả gì.</summary>
        public string Description { get; }

        public ToolboxItemViewModel(string toolId, string displayName, ToolKind kind, string description)
        {
            ToolId = toolId;
            DisplayName = displayName;
            Kind = kind;
            Description = description;
        }
    }
}

