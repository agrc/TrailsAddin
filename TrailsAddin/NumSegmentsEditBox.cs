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
                var sgidTrailsLayer = MapView.Active.Map.GetLayersAsFlattenedList().First(l => l.Name == "SGID10.RECREATION.Trails") as FeatureLayer;
                Text = sgidTrailsLayer.SelectionCount.ToString();
            });
        }

    }
}
