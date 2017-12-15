using ArcGIS.Desktop.Mapping.Events;

namespace TrailsAddin
{
    internal class NumSegmentsEditBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        public NumSegmentsEditBox()
        {
            MapSelectionChangedEvent.Subscribe((MapSelectionChangedEventArgs args) =>
            {
                Text = Main.Current.SegmentsLayer.SelectionCount.ToString();
            });
        }
    }
}
