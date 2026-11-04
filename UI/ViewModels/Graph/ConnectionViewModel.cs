using System;
using System.Collections.Generic;
using System.Text;
using UI.Models;
using Vision.Graph;

namespace UI.ViewModels.Graph
{
    public class ConnectionViewModel: ViewModelBase
    {
        public Connection Model { get; }
        public ConnectorViewModel Source { get; }
        public ConnectorViewModel Target { get; }

        public ConnectionViewModel(Connection model, ConnectorViewModel source, ConnectorViewModel target)
        {
            Model = model;
            Source = source;
            Target = target;
        }

        public ConnectionViewModel()
        {
        }

    }
}
