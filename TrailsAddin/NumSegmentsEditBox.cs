using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArcGIS.Desktop.Mapping.Events;
using ArcGIS.Desktop.Mapping;

namespace TrailsAddin
{
    internal class NumSegmentsEditBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        public NumSegmentsEditBox()
        {
            MapSelectionChangedEvent.Subscribe((MapSelectionChangedEventArgs args) =>
            {
                Text = Main.SGIDTrailsLayer.SelectionCount.ToString();
            });
        }
    }
}
