using ArcGIS.Desktop.Mapping.Events;

namespace TrailsAddin
{
    internal class NumTrailheadsEditBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        public NumTrailheadsEditBox()
        {
            MapSelectionChangedEvent.Subscribe((MapSelectionChangedEventArgs args) =>
            {
                Text = Main.Current.HeadsLayer.SelectionCount.ToString();
            });
        }
    }
}
