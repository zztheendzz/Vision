using System;
using System.Collections.Generic;
using System.Text;

namespace UI.Models
{
    using System.Windows;


        public class ToolNodeViewModel
        {
            public string Title { get; set; } = string.Empty;
            public Point Location { get; set; }
            public bool IsSelected { get; set; }
            public Point InputPortLocation { get; set; }
            public Point OutputPortLocation { get; set; }
        }

        public class ConnectionViewModel
        {
            public object? OutputConnector { get; set; }
            public object? InputConnector { get; set; }
        }
    }

